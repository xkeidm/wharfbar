using System.Runtime.InteropServices;
using WharfBar.Native;

namespace WharfBar.Native;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct PHYSICAL_MONITOR
{
    internal IntPtr hPhysicalMonitor;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string szPhysicalMonitorDescription;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct MONITORINFOEX
{
    internal uint cbSize;
    internal RECT rcMonitor;
    internal RECT rcWork;
    internal uint dwFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    internal string szDevice;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
internal struct DISPLAY_DEVICE
{
    internal uint cb;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
    internal string DeviceName;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string DeviceString;

    internal uint StateFlags;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string DeviceID;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    internal string DeviceKey;
}

internal delegate bool MonitorEnumDelegate(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

internal static class MonitorNative
{
    private const string Dxva2 = "dxva2.dll";
    private const string User32 = "user32.dll";

    internal static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var list = new List<MonitorInfo>();

        void EnumerateOne(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor)
        {
            var info = new MONITORINFOEX { cbSize = (uint)Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(hMonitor, ref info))
            {
                return;
            }

            string deviceId = string.Empty;
            string friendly = string.Empty;
            var device = new DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (EnumDisplayDevices(info.szDevice, 0, ref device, 0))
            {
                deviceId = device.DeviceID ?? string.Empty;
                friendly = device.DeviceString ?? string.Empty;
            }

            bool primary = (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
            uint dpi = Display.DefaultDpi;
            if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == 0 && dpiX > 0)
            {
                dpi = dpiX;
            }

            list.Add(new MonitorInfo(
                hMonitor,
                deviceId,
                info.szDevice,
                friendly,
                primary,
                info.rcMonitor,
                info.rcWork,
                dpi));
        }

        bool Callback(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr _)
        {
            EnumerateOne(hMonitor, hdc, ref rect);
            return true;
        }

        _ = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);

        return list
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .ToList();
    }

    [DllImport(User32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumDelegate lpfnEnum, IntPtr dwData);

    [DllImport(User32, CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport(User32, CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport(Dxva2, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport(Dxva2, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport(Dxva2, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport(Dxva2, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyPhysicalMonitor(IntPtr hMonitor);

    [DllImport(Dxva2, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint pdwMinimumBrightness, out uint pdwCurrentBrightness, out uint pdwMaximumBrightness);

    [DllImport(Dxva2, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);
}
