using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WharfBar.Services;

namespace WharfBar.Native;

/// <summary>
/// Sends dropped paths to the recycle bin with <c>SHFileOperationW</c>
/// (<c>FO_DELETE</c> | <c>FOF_ALLOWUNDO</c>). Never permanent-deletes, and never
/// deletes the original itself when the API fails.
/// </summary>
internal static class RecycleBinNative
{
    internal const uint FO_DELETE = 0x0003;
    internal const ushort FOF_ALLOWUNDO = 0x0040;
    internal const uint SIID_RECYCLER = 31;
    internal const uint SHGSI_ICON = 0x000000100;

    internal readonly record struct RecycleCallResult(int Accepted, int Rejected, bool ApiFailed);

    internal static int FileOpSize => Marshal.SizeOf<SHFILEOPSTRUCT>();

    internal static RecycleCallResult TryRecycle(IReadOnlyList<string> paths, IntPtr owner)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var accepted = new List<string>();
        int rejected = 0;
        foreach (string path in paths)
        {
            if (TryAccept(path, out string full))
            {
                accepted.Add(full);
            }
            else
            {
                rejected++;
            }
        }

        if (accepted.Count == 0)
        {
            return new RecycleCallResult(0, rejected, ApiFailed: false);
        }

        IntPtr from = DoubleNull(accepted);
        try
        {
            var op = new SHFILEOPSTRUCT
            {
                hwnd = owner,
                wFunc = FO_DELETE,
                pFrom = from,
                pTo = IntPtr.Zero,
                fFlags = FOF_ALLOWUNDO,
                fAnyOperationsAborted = 0,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = IntPtr.Zero,
            };

            int hr = SHFileOperationW(ref op);
            bool aborted = op.fAnyOperationsAborted != 0;
            if (!DockRecyclePolicy.ApiSucceeded(hr, aborted))
            {
                Debug.WriteLine(
                    $"WharfBar recycle failed: hr=0x{hr:X8} aborted={aborted} count={accepted.Count}");
                return new RecycleCallResult(accepted.Count, rejected, ApiFailed: true);
            }

            return new RecycleCallResult(accepted.Count, rejected, ApiFailed: false);
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    internal static bool TryGetStockIcon(out IntPtr hIcon)
    {
        hIcon = IntPtr.Zero;
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        int hr = SHGetStockIconInfo(SIID_RECYCLER, SHGSI_ICON | 0x000000000, ref info);
        if (hr != 0 || info.hIcon == IntPtr.Zero)
        {
            Debug.WriteLine($"WharfBar SHGetStockIconInfo(SIID_RECYCLER) failed: 0x{hr:X8}");
            return false;
        }

        hIcon = info.hIcon;
        return true;
    }

    private static bool TryAccept(string? path, out string full)
    {
        full = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        bool exists = File.Exists(full) || Directory.Exists(full);
        if (!DockFolderListing.TryDescribe(full, out DockFolderCandidate described))
        {
            return false;
        }

        return DockRecyclePolicy.CanRecycle(
            exists,
            described.AttributesReadable,
            described.IsDirectory,
            described.IsReparse);
    }

    private static IntPtr DoubleNull(IReadOnlyList<string> paths)
    {
        var builder = new StringBuilder();
        foreach (string path in paths)
        {
            builder.Append(path);
            builder.Append('\0');
        }

        builder.Append('\0');
        byte[] bytes = Encoding.Unicode.GetBytes(builder.ToString());
        IntPtr mem = Marshal.AllocHGlobal(bytes.Length + 2);
        Marshal.Copy(bytes, 0, mem, bytes.Length);
        Marshal.WriteInt16(mem, bytes.Length, 0);
        return mem;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        internal IntPtr hwnd;
        internal uint wFunc;
        internal IntPtr pFrom;
        internal IntPtr pTo;
        internal ushort fFlags;
        internal int fAnyOperationsAborted;
        internal IntPtr hNameMappings;
        internal IntPtr lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        internal uint cbSize;
        internal IntPtr hIcon;
        internal int iSysImageIndex;
        internal int iIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(uint siid, uint uFlags, ref SHSTOCKICONINFO psii);
}
