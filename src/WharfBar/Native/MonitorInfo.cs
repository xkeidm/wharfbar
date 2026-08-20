namespace WharfBar.Native;

/// <summary>
/// A live display monitor. <see cref="DeviceId"/> is the EnumDisplayDevices registry path,
/// stable across reboots; <see cref="DeviceName"/> (e.g. "\\.\DISPLAY1") can renumber on replug.
/// All geometry is in physical pixels (virtual-screen coordinates).
/// </summary>
internal sealed record MonitorInfo(
    IntPtr Handle,
    string DeviceId,
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    RECT Bounds,
    RECT WorkArea,
    uint Dpi);
