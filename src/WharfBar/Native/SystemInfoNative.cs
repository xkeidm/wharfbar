using System.Runtime.InteropServices;

namespace WharfBar.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct FILETIME
{
    public uint dwLowDateTime;
    public uint dwHighDateTime;

    public readonly ulong ToTicks() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;

    public static MEMORYSTATUSEX Create() => new()
    {
        dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
    };
}

internal static class SystemInfoNative
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetSystemTimes(
        out FILETIME lpIdleTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
