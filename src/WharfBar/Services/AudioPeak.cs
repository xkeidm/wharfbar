using System.Runtime.InteropServices;

namespace WharfBar.Services;

/// <summary>
/// Peak of the default render endpoint. A failed activation returns false
/// so the island does not invent bar heights.
/// </summary>
internal static class AudioPeak
{
    internal static bool TryRead(out float peak)
    {
        peak = 0f;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? meterObject = null;
        try
        {
            Type? type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"));
            enumerator = type is null ? null : (IMMDeviceEnumerator?)Activator.CreateInstance(type);
            if (enumerator is null || enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0 || device is null)
            {
                return false;
            }

            Guid iid = typeof(IAudioMeterInformation).GUID;
            if (device.Activate(ref iid, 1, IntPtr.Zero, out meterObject) != 0
                || meterObject is not IAudioMeterInformation meter)
            {
                return false;
            }

            return meter.GetPeakValue(out peak) == 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            if (meterObject is not null)
            {
                Marshal.ReleaseComObject(meterObject);
            }

            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
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
