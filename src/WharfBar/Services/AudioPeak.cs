using System.Runtime.InteropServices;

namespace WharfBar.Services;

/// <summary>
/// Peak of the default render endpoint. A failed activation returns false
/// so the island does not invent bar heights.
/// </summary>
internal static class AudioPeak
{
    private static readonly object Gate = new();
    private static IMMDeviceEnumerator? _enumerator;
    private static IMMDevice? _device;
    private static IAudioMeterInformation? _meter;

    internal static bool TryRead(out float peak)
    {
        peak = 0f;
        lock (Gate)
        {
            try
            {
                if (_meter is not null && _meter.GetPeakValue(out peak) == 0)
                {
                    return true;
                }

                Release();
                return TryOpen(out peak);
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or EntryPointNotFoundException)
            {
                Release();
                return false;
            }
        }
    }

    private static bool TryOpen(out float peak)
    {
        peak = 0f;
        Type? type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
        _enumerator = type is null ? null : (IMMDeviceEnumerator?)Activator.CreateInstance(type);
        if (_enumerator is null || _enumerator.GetDefaultAudioEndpoint(0, 1, out _device) != 0 || _device is null)
        {
            Release();
            return false;
        }

        Guid iid = typeof(IAudioMeterInformation).GUID;
        if (_device.Activate(ref iid, 1, IntPtr.Zero, out object meterObject) != 0
            || meterObject is not IAudioMeterInformation meter)
        {
            if (meterObject is not null)
            {
                Marshal.ReleaseComObject(meterObject);
            }

            Release();
            return false;
        }

        _meter = meter;
        return _meter.GetPeakValue(out peak) == 0;
    }

    private static void Release()
    {
        if (_meter is not null)
        {
            Marshal.ReleaseComObject(_meter);
            _meter = null;
        }

        if (_device is not null)
        {
            Marshal.ReleaseComObject(_device);
            _device = null;
        }

        if (_enumerator is not null)
        {
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig]
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

    [PreserveSig]
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport]
[Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioMeterInformation
{
    [PreserveSig]
    int GetPeakValue(out float peak);
}
