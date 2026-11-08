using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WharfBar.Native;

/// <summary>
/// Documented clipboard P/Invoke calls. The repository does not use <c>System.Windows.Clipboard</c>:
/// it is bound to the UI thread and freezes the UI when <c>OpenClipboard</c> is blocked. The
/// calls here are made from an STA worker thread.
/// </summary>
internal static class ClipboardNative
{
    /// <summary>Windows' "do not add this content to history" opt-out signal.</summary>
    private const string CanIncludeInClipboardHistoryFormat = "CanIncludeInClipboardHistory";

    internal const int WM_CLIPBOARDUPDATE = 0x031D;

    private const ushort CF_UNICODETEXT = 13;
    private const ushort CF_HDROP = 15;
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
    // are deliberately not repeated here: they are not clipboard-related, but general process name
    // resolution. See NativeMethods for the canonical declarations; a call site that does the same
    // work is in RunningAppsSource.TryGetProcessPath (Services/RunningAppsSource.cs:153-181). The
    // GetOwnerProcessName here is that one with Path.GetFileNameWithoutExtension applied.

    /// <summary>Adds the clipboard change listener to the message-only window.</summary>
    internal static bool AddListener(IntPtr hwnd) => hwnd != IntPtr.Zero && AddClipboardFormatListener(hwnd);

    /// <summary>
    /// Removes the listener. <c>RemoveClipboardFormatListener</c> is <b>not</b> idempotent:
    /// if the window is not registered it returns <c>FALSE</c> + <c>ERROR_INVALID_WINDOW</c> (0x57) —
    /// measured (5/5 attempts): while registered the first <c>Remove</c> returns <c>TRUE</c>, the second and later ones
    /// <c>0x57</c>; a window that was never added also returns <c>0x57</c>. It must therefore be called only
    /// <b>while registered and only once</b>; a second call reports a safe call as an error.
    /// <c>ClipboardMessageWindow.Dispose</c> guarantees this with the <c>_disposed</c> flag.
    /// </summary>
    internal static void RemoveListener(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && !RemoveClipboardFormatListener(hwnd))
        {
            Debug.WriteLine($"[WharfBar] RemoveClipboardFormatListener failed: 0x{Marshal.GetLastWin32Error():X8}");
        }
    }

    /// <summary>
    /// Reads the text on the clipboard. If <paramref name="historyOptOut"/> is true, the source application
    /// does not want the content added to history and it must not be saved.
    /// </summary>
    /// <remarks>
    /// <c>false</c> has <b>six</b> separate paths and all of them are logged with <c>Debug.WriteLine</c>
    /// (otherwise the caller cannot diagnose the failure):
    /// <list type="number">
    /// <item><c>OpenClipboard</c> fails — the clipboard is locked by another process
    /// (<c>0x800401D0</c> = <c>CLIPBRD_E_CANT_OPEN</c>, the most common failure reason);</item>
    /// <item><c>RegisterClipboardFormatW</c> fails — the opt-out flag cannot be read;</item>
    /// <item>the source application wrote the opt-out marker (password manager, <c>DWORD 0</c>);</item>
    /// <item>the clipboard holds no <c>CF_UNICODETEXT</c> — a non-text copy;</item>
    /// <item><c>GetClipboardData</c> fails;</item>
    /// <item><c>GlobalLock</c> fails.</item>
    /// </list>
    /// The second and third paths are closed by <c>historyOptOut = true</c>, that is, content is not
    /// saved when in doubt (fail-closed).
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
    /// Writes the text to the clipboard (no paste is performed — that is left to the calling application).
    /// </summary>
    /// <remarks>
    /// The write <b>marks our own record as opt-out</b>: the <c>CanIncludeInClipboardHistory
    /// = 0</c> block is written along with it. Otherwise <c>SetClipboardData</c> triggers our own
    /// <c>WM_CLIPBOARDUPDATE</c>, our own listener records the text again and
    /// the island captures its own copy back. The <c>ReadDword == 0</c> check of the read path
    /// (<see cref="TryReadText"/>) interprets this block exactly as "do not add to history".
    /// <para><b>Ownership:</b> the <c>OpenClipboard(IntPtr.Zero)</c> + <c>EmptyClipboard()</c>
    /// call makes the clipboard owner NULL, so the previous owner (the source application) is no
    /// longer the owner. This is an unchangeable consequence of <c>EmptyClipboard</c> and holds for
    /// every write made with <c>SetClipboardData
    /// </c>; thanks to the opt-out marker, at least this record
    /// does not enter the history.</para>
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

            // Opt-out marker: DWORD 0 = "do not add this content to history". If the format id comes out
            // 0, the write is skipped (the read path likewise saves no content under the same uncertainty).
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
                // The text was written to the clipboard; only the opt-out marker was lost. The write counts
                // as successful (the user got what they asked for) but there is a risk of capturing our own
                // record back — it must not stay invisible.
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

    /// <summary>
    /// Writes CF_HDROP (a file list) to the clipboard; the paste is left to the caller. The block format
    /// is <c>DROPFILES { pFiles = offsetof(char data), fWide = 1 }</c> + a double-null-terminated UTF-16
    /// path list.
    /// </summary>
    /// <remarks>
    /// The stale clipboard owner problem from the text write (<see cref="TryWriteText"/>) exists here too:
    /// <c>EmptyClipboard</c> irrevocably discards the previous copy, so the block is
    /// allocated and filled <b>before</b> <c>EmptyClipboard</c>, and every error after that frees
    /// the block and logs. The <c>CanIncludeInClipboardHistory = 0</c> marker is <b>not</b> written:
    /// island capture is text-based (<see cref="TryReadText"/> reads only <c>CF_UNICODETEXT</c>),
    /// so this write cannot be captured back by the island.
    /// </remarks>
    internal static bool TryWriteFileDropList(IReadOnlyList<string> paths)
    {
        if (paths is null || paths.Count == 0)
        {
            return false;
        }

        int headerSize = Marshal.SizeOf<DROPFILES>();
        int byteCount = headerSize + 2; // terminating double null
        foreach (string? path in paths)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.WriteLine("[WharfBar] TryWriteFileDropList rejected an empty path.");
                return false;
            }

            byteCount += (path.Length + 1) * 2;
        }

        if (!OpenClipboard(IntPtr.Zero))
        {
            Debug.WriteLine($"[WharfBar] OpenClipboard failed (file drop list write): 0x{Marshal.GetLastWin32Error():X8}");
            return false;
        }

        try
        {
            IntPtr mem = AllocGlobalBlock(new IntPtr(byteCount), block =>
            {
                // pFiles = offsetof(char data): the path list starts right after the header.
                // pt = 0 / fNC = 0: the drag image position is meaningless for a copy.
                Marshal.StructureToPtr(
                    new DROPFILES { PFiles = (uint)headerSize, Wide = 1 },
                    block,
                    fDeleteOld: false);

                int cursor = headerSize;
                foreach (string path in paths)
                {
                    byte[] utf16 = Encoding.Unicode.GetBytes(path);
                    Marshal.Copy(utf16, 0, block + cursor, utf16.Length);
                    cursor += utf16.Length;
                    Marshal.WriteInt16(block, cursor, 0); // the path is terminated by its own null
                    cursor += 2;
                }

                // AllocGlobalBlock does not guarantee zeroed memory: write the last byte of the double
                // null explicitly (this is where the path list ends).
                Marshal.WriteInt16(block, byteCount - 2, 0);
            });
            if (mem == IntPtr.Zero)
            {
                return false;
            }

            // EmptyClipboard irrevocably discards the previous copy: every error after this point
            // must free the block. Because the block is filled in advance, the open window is two API calls.
            if (!EmptyClipboard())
            {
                Debug.WriteLine($"[WharfBar] EmptyClipboard failed (file drop list): 0x{Marshal.GetLastWin32Error():X8}");
                _ = GlobalFree(mem);
                return false;
            }

            // SetClipboardData takes ownership on success; it must NOT be freed then.
            if (SetClipboardData(CF_HDROP, mem) == IntPtr.Zero)
            {
                Debug.WriteLine($"[WharfBar] SetClipboardData(CF_HDROP) failed: 0x{Marshal.GetLastWin32Error():X8}");
                _ = GlobalFree(mem);
                return false;
            }

            return true;
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <summary>
    /// Reads the CF_HDROP list on the clipboard. The mirror image of the <see cref="TryWriteFileDropList"/>
    /// walk: the path list in the block is entered at the <c>pFiles</c> offset, each UTF-16 string
    /// ends at its own null, and an empty string (double null) terminates the list.
    /// </summary>
    /// <remarks>
    /// Reading is bounded by <c>GlobalSize</c>; a corrupt block cannot cause an over-read
    /// (fail-closed). An ANSI list (<c>fWide = 0</c>) is rejected: this writer produces only UTF-16,
    /// and the code page of a foreign ANSI block is unknown.
    /// </remarks>
    internal static bool TryReadFileDropList(out List<string> paths)
    {
        paths = new List<string>();

        if (!OpenClipboard(IntPtr.Zero))
        {
            Debug.WriteLine($"[WharfBar] OpenClipboard failed (file drop list read): 0x{Marshal.GetLastWin32Error():X8}");
            return false;
        }

        try
        {
            if (!IsClipboardFormatAvailable(CF_HDROP))
            {
                Debug.WriteLine("[WharfBar] clipboard holds no CF_HDROP.");
                return false;
            }

            IntPtr handle = GetClipboardData(CF_HDROP);
            if (handle == IntPtr.Zero)
            {
                Debug.WriteLine($"[WharfBar] GetClipboardData(CF_HDROP) failed: 0x{Marshal.GetLastWin32Error():X8}");
                return false;
            }

            IntPtr block = GlobalLock(handle);
            if (block == IntPtr.Zero)
            {
                Debug.WriteLine($"[WharfBar] GlobalLock failed (file drop list): 0x{Marshal.GetLastWin32Error():X8}");
                return false;
            }

            try
            {
                long size = (long)GlobalSize(handle);
                int headerSize = Marshal.SizeOf<DROPFILES>();
                if (size < headerSize || size > int.MaxValue)
                {
                    Debug.WriteLine($"[WharfBar] CF_HDROP block has an implausible size ({size} bytes).");
                    return false;
                }

                DROPFILES header = Marshal.PtrToStructure<DROPFILES>(block);
                long offset = header.PFiles;
                if (header.Wide == 0)
                {
                    Debug.WriteLine("[WharfBar] CF_HDROP is an ANSI list; only UTF-16 lists are read.");
                    return false;
                }

                if ((offset & 1) != 0 || offset < headerSize || offset >= size)
                {
                    Debug.WriteLine($"[WharfBar] CF_HDROP has an invalid path-list offset {offset} (block {size} bytes).");
                    return false;
                }

                var list = new List<string>();
                int cursor = (int)offset;
                while (true)
                {
                    if (!TryReadNullTerminatedUtf16(block, cursor, (int)size, out string path))
                    {
                        Debug.WriteLine("[WharfBar] CF_HDROP list is not double-null terminated inside the block.");
                        return false;
                    }

                    if (path.Length == 0)
                    {
                        break; // double null: the list ended
                    }

                    list.Add(path);
                    cursor += (path.Length + 1) * 2;
                }

                paths = list;
                return true;
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    /// <summary>
    /// Reads the null-terminated UTF-16 string at <paramref name="block"/> + <paramref name="offset"/>
    /// up to the <paramref name="end"/> byte boundary. Returns <c>false</c> if there is no null inside the
    /// boundary — <c>PtrToStringUni</c>'s unbounded read is not trusted.
    /// </summary>
    private static bool TryReadNullTerminatedUtf16(IntPtr block, int offset, int end, out string value)
    {
        value = string.Empty;
        int cursor = offset;
        while (cursor + 2 <= end)
        {
            if (Marshal.ReadInt16(block, cursor) == 0)
            {
                value = Marshal.PtrToStringUni(block + offset, (cursor - offset) / 2) ?? string.Empty;
                return true;
            }

            cursor += 2;
        }

        return false;
    }

    /// <summary>Name of the clipboard owner process; null if there is no owner or it is not accessible.</summary>
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
    /// Reads the DWORD in the "CanIncludeInClipboardHistory" block. If the block is shorter than 4 bytes it is
    /// not read (over-read) and 0 is returned — that is, the content is not added to history: the safe side is opt-out.
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
    /// Allocates a moveable global block, fills it with <paramref name="fill"/>
    /// and returns the block pointer after unlocking it. On failure it returns <see cref="IntPtr.Zero"/>
    /// (the block has already been <c>GlobalFree</c>d) and logs the reason.
    /// </summary>
    /// <remarks>
    /// <c>SetClipboardData</c> takes ownership on success, so the caller must not
    /// <c>GlobalFree</c>; on failure the caller is responsible. It shares the same
    /// <c>GlobalAlloc</c>/<c>GlobalLock</c> pattern, so that text and DWORD
    /// blocks are not written differently because of different code paths.
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

    /// <summary>Frees a block whose ownership has not been transferred; a zero pointer is a no-op.</summary>
    private static void FreeIfOwned(IntPtr mem)
    {
        if (mem != IntPtr.Zero)
        {
            _ = GlobalFree(mem);
        }
    }

    /// <summary>
    /// Header of the CF_HDROP block (<c>DROPFILES</c>): <c>PFiles</c> is the byte offset of the path list
    /// relative to the start of the block (<c>offsetof(char data)</c>), <c>Wide = 1</c> says the list is
    /// UTF-16. <c>PointX/PointY</c> and <c>NonClient</c> are not used for copying, they only carry the
    /// drag image position.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DROPFILES
    {
        public uint PFiles;
        public int PointX;
        public int PointY;
        public int NonClient;
        public int Wide;
    }

    private static int Utf8ByteCount(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
}
