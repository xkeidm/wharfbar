using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WharfBar.Native;

/// <summary>
/// Belgelenmiş pano P/Invoke'ları. Depoda <c>System.Windows.Clipboard</c> kullanılmaz:
/// UI thread'ine bağlıdır ve <c>OpenClipboard</c> bloklandığında arayüzü dondurur. Buradaki
/// çağrılar STA worker thread'inden yapılır.
/// </summary>
internal static class ClipboardNative
{
    /// <summary>Windows'un "bu içeriği geçmişe alma" opt-out sinyali.</summary>
    private const string CanIncludeInClipboardHistoryFormat = "CanIncludeInClipboardHistory";

    internal const int WM_CLIPBOARDUPDATE = 0x031D;

    private const ushort CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr data);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, IntPtr bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr mem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr mem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr mem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalSize(IntPtr mem);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string format);

    // OpenProcess / QueryFullProcessImageName / CloseHandle / PROCESS_QUERY_LIMITED_INFORMATION
    // bilerek burada tekrar edilmiyor: bunlar pano ile ilgili değil, genel süreç adı çözümlemesi.
    // Kanonik bildirimler için NativeMethods'a bak; aynı işi yapan bir çağrı noktası da
    // RunningAppsSource.TryGetProcessPath'te (Services/RunningAppsSource.cs:153-181). Buradaki
    // GetOwnerProcessName, onun Path.GetFileNameWithoutExtension uygulanmış hâlidir.

    /// <summary>Mesaj-only pencereye pano değişim dinleyicisini ekler.</summary>
    internal static bool AddListener(IntPtr hwnd) => hwnd != IntPtr.Zero && AddClipboardFormatListener(hwnd);

    /// <summary>
    /// Dinleyiciyi kaldırır. <c>RemoveClipboardFormatListener</c> idempotent <b>değildir</b>:
    /// pencere kayıtlı değilse <c>FALSE</c> + <c>ERROR_INVALID_WINDOW</c> (0x57) döner —
    /// ölçüldü (5/5 deneme): kayıt varken ilk <c>Remove</c> <c>TRUE</c>, ikinci ve sonrakiler
    /// <c>0x57</c>; hiç eklenmemiş pencere de <c>0x57</c> verir. Bu yüzden yalnızca <b>kayıtlıyken
    /// ve yalnızca bir kez</b> çağrılmalıdır; ikinci bir çağrı güvenli bir çağrıyı hata olarak
    /// raporlar. <c>ClipboardMessageWindow.Dispose</c> <c>_disposed</c> bayrağıyla bunu garanti
    /// eder.
    /// </summary>
    internal static void RemoveListener(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && !RemoveClipboardFormatListener(hwnd))
        {
            Debug.WriteLine($"[WharfBar] RemoveClipboardFormatListener failed: 0x{Marshal.GetLastWin32Error():X8}");
        }
    }

    /// <summary>
    /// Panodaki metni okur. <paramref name="historyOptOut"/> true ise kaynak uygulama
    /// içeriği geçmişe almak istemiyor demektir ve kaydedilmemelidir.
    /// </summary>
    /// <remarks>
    /// <c>false</c>'in <b>altı</b> ayrı yolu vardır ve hepsi <c>Debug.WriteLine</c> ile loglanır
    /// (yoksa çağıran taraf teşhis edemez):
    /// <list type="number">
    /// <item><c>OpenClipboard</c> başarısız — pano başka bir süreçte kilitli
    /// (<c>0x800401D0</c> = <c>CLIPBRD_E_CANT_OPEN</c>, en sık düşüş nedeni);</item>
    /// <item><c>RegisterClipboardFormatW</c> başarısız — opt-out okunamıyor;</item>
    /// <item>kaynak uygulama opt-out işareti yazmış (parola yöneticisi, <c>DWORD 0</c>);</item>
    /// <item>panoda <c>CF_UNICODETEXT</c> yok — metin dışı bir kopya;</item>
    /// <item><c>GetClipboardData</c> başarısız;</item>
    /// <item><c>GlobalLock</c> başarısız.</item>
    /// </list>
    /// İkinci ve üçüncü yol <c>historyOptOut = true</c> ile kapanır, yani belirsizlikte içerik
    /// kaydedilmez (fail-closed).
    /// </remarks>
    internal static bool TryReadText(out string? text, out bool historyOptOut, out int byteCount)
    {
        text = null;
        historyOptOut = false;
        byteCount = 0;

        if (!OpenClipboard(IntPtr.Zero))
        {
            // 0x800401D0 = CLIPBRD_E_CANT_OPEN: another process holds the clipboard lock.
            // Frequent and normal, but it is the single most common cause of a dropped copy.
            Debug.WriteLine($"[WharfBar] OpenClipboard failed: 0x{Marshal.GetLastWin32Error():X8}");
            return false;
        }

        try
        {
            uint optOutId = RegisterFormatId(CanIncludeInClipboardHistoryFormat);
            if (optOutId == 0)
            {
                // The opt-out format id is unknown, so the opt-out flag cannot be read at all.
                // Capturing anyway would silently override a password manager's "do not keep
                // this" — so we fail closed: historyOptOut = true suppresses the capture. The
                // format is registered by every modern Windows shell, so this path should not
                // change behaviour in practice.
                Debug.WriteLine("[WharfBar] RegisterClipboardFormatW(CanIncludeInClipboardHistory) failed; opt-out check skipped and capture suppressed.");
                historyOptOut = true;
                return false;
            }

            if (IsClipboardFormatAvailable(optOutId))
            {
                IntPtr optOut = GetClipboardData(optOutId);
                if (optOut != IntPtr.Zero && ReadDword(optOut) == 0)
                {
                    historyOptOut = true;
                    return false;
                }
            }

            if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                Debug.WriteLine("[WharfBar] clipboard holds no CF_UNICODETEXT (non-text copy).");
                return false;
            }

            IntPtr handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                Debug.WriteLine($"[WharfBar] GetClipboardData(CF_UNICODETEXT) failed: 0x{Marshal.GetLastWin32Error():X8}");
                return false;
            }

            IntPtr block = GlobalLock(handle);
            if (block == IntPtr.Zero)
            {
                Debug.WriteLine($"[WharfBar] GlobalLock failed: 0x{Marshal.GetLastWin32Error():X8}");
                return false;
            }

            try
            {
                text = Marshal.PtrToStringUni(block) ?? string.Empty;
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }

            byteCount = Utf8ByteCount(text);
            return true;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <summary>
    /// Metni panoya yazar (yapıştırma yapılmaz — çağıran uygulamaya bırakılır).
    /// </summary>
    /// <remarks>
    /// Yazma <b>kendi kaydımızı opt-out olarak işaretler</b>: <c>CanIncludeInClipboardHistory
    /// = 0</c> bloğu birlikte yazılır. Aksi halde <c>SetClipboardData</c> kendi
    /// <c>WM_CLIPBOARDUPDATE</c>'imizi tetikler, kendi dinleyicimiz metni yeniden kaydeder ve
    /// island kendi kopyasını geri yakalar. Okuma yolunun (<see cref="TryReadText"/>)
    /// <c>ReadDword == 0</c> kontrolü bu bloğu tam olarak "geçmişe alma" diye yorumlar.
    /// <para><b>Sahiplik:</b> <c>OpenClipboard(IntPtr.Zero)</c> + <c>EmptyClipboard()</c>
    /// çağrısı pano sahibini NULL yapar, yani önceki sahip (kaynak uygulama) artık sahip
    /// değildir. Bu <c>EmptyClipboard</c>'in değiştirilemez bir sonucudur ve <c>SetClipboardData
    /// </c> ile yapılan her yazımda böyledir; opt-out işareti sayesinde en azından bu kayıt
    /// geçmişe girmez.</para>
    /// </remarks>
    internal static bool TryWriteText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            Debug.WriteLine($"[WharfBar] OpenClipboard failed (write): 0x{Marshal.GetLastWin32Error():X8}");
            return false;
        }

        try
        {
            // CF_UNICODETEXT is null-terminated UTF-16 — UTF-8 here would be read back as mojibake.
            int byteCount = (text.Length + 1) * 2;
            IntPtr mem = AllocGlobalBlock(new IntPtr(byteCount), block =>
            {
                var utf16 = new byte[byteCount];
                int written = Encoding.Unicode.GetBytes(text.AsSpan(), utf16);
                utf16[written] = 0;
                utf16[written + 1] = 0;
                Marshal.Copy(utf16, 0, block, byteCount);
            });
            if (mem == IntPtr.Zero)
            {
                return false;
            }

            // Opt-out işareti: DWORD 0 = "bu içeriği geçmişe alma". Format id'i 0 çıkarsa
            // yazmadan geçilir (okuma yolunda da aynı belirsizlikte içerik kaydedilmez).
            uint optOutId = RegisterFormatId(CanIncludeInClipboardHistoryFormat);
            IntPtr optOutMem = optOutId == 0
                ? IntPtr.Zero
                : AllocGlobalBlock(new IntPtr(sizeof(int)), block => Marshal.WriteInt32(block, 0));

            // EmptyClipboard() irreversibly discards the user's previous copy. Every failure
            // past this point must therefore free mem AND log — the old copy is already gone and
            // cannot be restored by us. Allocating and filling mem BEFORE the wipe keeps that
            // window down to the two SetClipboardData calls.
            if (!EmptyClipboard())
            {
                Debug.WriteLine($"[WharfBar] EmptyClipboard failed: 0x{Marshal.GetLastWin32Error():X8}");
                _ = GlobalFree(mem);
                FreeIfOwned(optOutMem);
                return false;
            }

            // SetClipboardData takes ownership on success; it must NOT be freed then.
            if (SetClipboardData(CF_UNICODETEXT, mem) == IntPtr.Zero)
            {
                Debug.WriteLine($"[WharfBar] SetClipboardData failed: 0x{Marshal.GetLastWin32Error():X8}");
                _ = GlobalFree(mem);
                FreeIfOwned(optOutMem);
                return false;
            }

            if (optOutMem != IntPtr.Zero && SetClipboardData(optOutId, optOutMem) == IntPtr.Zero)
            {
                // Metin panoya yazıldı; yalnız opt-out işareti kayboldu. Yazma başarılı
                // sayılır (kullanıcı ne istiyorsa onu aldı) ama kendi kaydımızı geri
                // yakalama riski var — görünmez olmamalı.
                Debug.WriteLine($"[WharfBar] SetClipboardData(CanIncludeInClipboardHistory) failed: 0x{Marshal.GetLastWin32Error():X8}");
                _ = GlobalFree(optOutMem);
            }

            return true;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <summary>Pano sahibi sürecin adı; sahiplik yoksa veya erişilemiyorsa null.</summary>
    internal static string? GetOwnerProcessName()
    {
        IntPtr owner = GetClipboardOwner();
        if (owner == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(owner, out uint pid);
        if (pid == 0)
        {
            Debug.WriteLine("[WharfBar] GetWindowThreadProcessId returned pid 0 for the clipboard owner.");
            return null;
        }

        IntPtr process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
        {
            // Normal for elevated / other-session owners (ERROR_ACCESS_DENIED = 0x5).
            Debug.WriteLine($"[WharfBar] OpenProcess failed for pid {pid}: 0x{Marshal.GetLastWin32Error():X8}");
            return null;
        }

        try
        {
            var name = new StringBuilder(1024);
            int size = name.Capacity;
            if (!NativeMethods.QueryFullProcessImageName(process, 0, name, ref size))
            {
                Debug.WriteLine($"[WharfBar] QueryFullProcessImageName failed for pid {pid}: 0x{Marshal.GetLastWin32Error():X8}");
                return null;
            }

            return Path.GetFileNameWithoutExtension(name.ToString());
        }
        finally
        {
            _ = NativeMethods.CloseHandle(process);
        }
    }

    private static uint _historyFormatId;

    private static uint RegisterFormatId(string name)
    {
        if (_historyFormatId == 0)
        {
            _historyFormatId = RegisterClipboardFormatW(name);
        }

        return _historyFormatId;
    }

    /// <summary>
    /// "CanIncludeInClipboardHistory" bloğundaki DWORD'u okur. Blok 4 bayttan kısaysa okunmaz
    /// (taşma okuma) ve 0 döner — yani içerik geçmişe alınmaz: güvenli taraf opt-out'tur.
    /// </summary>
    private static int ReadDword(IntPtr handle)
    {
        IntPtr size = GlobalSize(handle);
        if (size < sizeof(int))
        {
            Debug.WriteLine($"[WharfBar] CanIncludeInClipboardHistory block is smaller than a DWORD ({size} bytes).");
            return 0;
        }

        IntPtr block = GlobalLock(handle);
        if (block == IntPtr.Zero)
        {
            Debug.WriteLine($"[WharfBar] GlobalLock failed (opt-out block): 0x{Marshal.GetLastWin32Error():X8}");
            return 0;
        }

        try
        {
            return Marshal.ReadInt32(block);
        }
        finally
        {
            _ = GlobalUnlock(handle);
        }
    }

    /// <summary>
    /// Hareketlenebilir (moveable) bir global blok ayırır, <paramref name="fill"/> ile doldurur
    /// ve kilidini açarak blok işaretçisini döndürür. Başarısızsa <see cref="IntPtr.Zero"/> döner
    /// (blok zaten <c>GlobalFree</c> edilmiş olur) ve nedeni loglar.
    /// </summary>
    /// <remarks>
    /// <c>SetClipboardData</c> başarıda sahipliği devralır, bu yüzden çağıran
    /// <c>GlobalFree</c> yapmamalıdır; başarısızlıkta ise çağıran sorumludur. Aynı
    /// <c>GlobalAlloc</c>/<c>GlobalLock</c> kalıbını paylaşır, böylece metin ve DWORD
    /// blokları farklı kod yolları yüzünden farklı yazılmaz.
    /// </remarks>
    private static IntPtr AllocGlobalBlock(IntPtr bytes, Action<IntPtr> fill)
    {
        IntPtr mem = GlobalAlloc(GMEM_MOVEABLE, bytes);
        if (mem == IntPtr.Zero)
        {
            Debug.WriteLine($"[WharfBar] GlobalAlloc failed: 0x{Marshal.GetLastWin32Error():X8}");
            return IntPtr.Zero;
        }

        IntPtr block = GlobalLock(mem);
        if (block == IntPtr.Zero)
        {
            Debug.WriteLine($"[WharfBar] GlobalLock failed: 0x{Marshal.GetLastWin32Error():X8}");
            _ = GlobalFree(mem);
            return IntPtr.Zero;
        }

        try
        {
            fill(block);
        }
        finally
        {
            _ = GlobalUnlock(mem);
        }

        return mem;
    }

    /// <summary>Sahiplik devredilmemiş bloğu serbest bırakır; sıfır işaretçi no-op'tur.</summary>
    private static void FreeIfOwned(IntPtr mem)
    {
        if (mem != IntPtr.Zero)
        {
            _ = GlobalFree(mem);
        }
    }

    private static int Utf8ByteCount(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
}
