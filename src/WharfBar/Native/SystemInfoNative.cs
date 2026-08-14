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

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_INFO
{
    public ushort wProcessorArchitecture;
    public ushort wReserved;
    public uint dwPageSize;
    public IntPtr lpMinimumApplicationAddress;
    public IntPtr lpMaximumApplicationAddress;
    public IntPtr dwActiveProcessorMask;
    public uint dwNumberOfProcessors;
    public uint dwProcessorType;
    public uint dwAllocationGranularity;
    public ushort wProcessorLevel;
    public ushort wProcessorRevision;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROCESSOR_POWER_INFORMATION
{
    public uint Number;
    public uint MaxMhz;
    public uint CurrentMhz;
    public uint MhzLimit;
    public uint MaxIdleState;
    public uint CurrentIdleState;
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

    private const int ProcessorInformation = 11;

    [DllImport("kernel32.dll")]
    internal static extern void GetSystemInfo(out SYSTEM_INFO lpSystemInfo);

    [DllImport("powrprof.dll")]
    internal static extern uint CallNtPowerInformation(
        int informationLevel,
        IntPtr inputBuffer,
        uint inputBufferLength,
        IntPtr outputBuffer,
        uint outputBufferLength);

    internal static double? TryReadCpuGhz()
    {
        try
        {
            GetSystemInfo(out SYSTEM_INFO sys);
            int count = (int)sys.dwNumberOfProcessors;
            if (count <= 0)
            {
                return null;
            }

            int size = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
            IntPtr buffer = Marshal.AllocHGlobal(checked(size * count));
            try
            {
                uint status = CallNtPowerInformation(
                    ProcessorInformation,
                    IntPtr.Zero,
                    0,
                    buffer,
                    (uint)(size * count));
                if (status != 0)
                {
                    return null;
                }

                double sum = 0;
                int n = 0;
                for (int i = 0; i < count; i++)
                {
                    PROCESSOR_POWER_INFORMATION info = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(
                        buffer + (i * size));
                    if (info.CurrentMhz > 0)
                    {
                        sum += info.CurrentMhz;
                        n++;
                    }
                }

                if (n == 0)
                {
                    return null;
                }

                return (sum / n) / 1000.0;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }
}
