using System;
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormatW(string format);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// <summary>Mesaj-only pencereye pano değişim dinleyicisini ekler.</summary>
    internal static bool AddListener(IntPtr hwnd) => hwnd != IntPtr.Zero && AddClipboardFormatListener(hwnd);

    /// <summary>Dinleyiciyi kaldırır (idempotent olmayan Win32 çağrısı — yalnız bir kez çağrılır).</summary>
    internal static void RemoveListener(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
        {
            _ = RemoveClipboardFormatListener(hwnd);
        }
    }

    /// <summary>
    /// Panodaki metni okur. <paramref name="historyOptOut"/> true ise kaynak uygulama
    /// içeriği geçmişe almak istemiyor demektir ve kaydedilmemelidir.
    /// </summary>
    internal static bool TryReadText(out string? text, out bool historyOptOut, out int byteCount)
    {
        text = null;
        historyOptOut = false;
        byteCount = 0;

        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            uint optOutId = RegisterFormatId(CanIncludeInClipboardHistoryFormat);
            if (optOutId != 0
                && IsClipboardFormatAvailable(optOutId)
                && GetClipboardData(optOutId) != IntPtr.Zero
                && ReadDword(GetClipboardData(optOutId)) == 0)
            {
                historyOptOut = true;
                return false;
            }

            if (!IsClipboardFormatAvailable(CF_UNICODETEXT))
            {
                return false;
            }

            IntPtr handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            IntPtr block = GlobalLock(handle);
            if (block == IntPtr.Zero)
            {
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

    /// <summary>Metni panoya yazar (yapıştırma yapılmaz — çağıran uygulamaya bırakılır).</summary>
    internal static bool TryWriteText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            // CF_UNICODETEXT is null-terminated UTF-16 — UTF-8 here would be read back as mojibake.
            int byteCount = (text.Length + 1) * 2;
            IntPtr mem = GlobalAlloc(GMEM_MOVEABLE, new IntPtr(byteCount));
            if (mem == IntPtr.Zero)
            {
                return false;
            }

            IntPtr block = GlobalLock(mem);
            if (block == IntPtr.Zero)
            {
                _ = GlobalFree(mem);
                return false;
            }

            try
            {
                var utf16 = new byte[byteCount];
                int written = Encoding.Unicode.GetBytes(text.AsSpan(), utf16);
                utf16[written] = 0;
                utf16[written + 1] = 0;
                Marshal.Copy(utf16, 0, block, byteCount);
            }
            finally
            {
                _ = GlobalUnlock(mem);
            }

            if (!EmptyClipboard())
            {
                _ = GlobalFree(mem);
                return false;
            }

            // SetClipboardData takes ownership on success; it must NOT be freed here.
            return SetClipboardData(CF_UNICODETEXT, mem) != IntPtr.Zero;
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
            return null;
        }

        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            int size = 1024;
            var name = new StringBuilder(size);
            return QueryFullProcessImageName(process, 0, name, ref size)
                ? Path.GetFileNameWithoutExtension(name.ToString())
                : null;
        }
        finally
        {
            _ = CloseHandle(process);
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

    private static int ReadDword(IntPtr handle)
    {
        IntPtr block = GlobalLock(handle);
        if (block == IntPtr.Zero)
        {
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

    private static int Utf8ByteCount(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
}
