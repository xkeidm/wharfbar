using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

/// <summary>
/// Render endpoint master volume via Core Audio (default or by device id).
/// </summary>
internal static class AudioEndpointVolume
{
    private static readonly Guid MmDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IAudioEndpointVolumeIid = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private const int EDataFlowRender = 0;
    private const int ERoleMultimedia = 1;
    private const int ClsCtxInprocServer = 1;

    internal static bool TryGetScalar(out float level01, out bool muted) =>
        TryGetScalarForDevice(deviceId: null, out level01, out muted);

    internal static bool TrySetScalar(float level01) =>
        TrySetScalarForDevice(deviceId: null, level01);

    internal static bool TrySetMute(bool muted) =>
        TrySetMuteForDevice(deviceId: null, muted);

    internal static bool TryGetScalarForDevice(string? deviceId, out float level01, out bool muted)
    {
        level01 = 0f;
        muted = false;
        if (!TryGetEndpoint(deviceId, out IAudioEndpointVolume? volume) || volume is null)
        {
            return false;
        }

        try
        {
            if (volume.GetMasterVolumeLevelScalar(out level01) != NativeMethods.S_OK)
            {
                return false;
            }

            if (volume.GetMute(out int muteInt) != NativeMethods.S_OK)
            {
                return false;
            }

            muted = muteInt != 0;
            return true;
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    internal static bool TrySetScalarForDevice(string? deviceId, float level01)
    {
        level01 = Math.Clamp(level01, 0f, 1f);
        if (!TryGetEndpoint(deviceId, out IAudioEndpointVolume? volume) || volume is null)
        {
            return false;
        }

        try
        {
            return volume.SetMasterVolumeLevelScalar(level01, Guid.Empty) == NativeMethods.S_OK;
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    internal static bool TrySetMuteForDevice(string? deviceId, bool muted)
    {
        if (!TryGetEndpoint(deviceId, out IAudioEndpointVolume? volume) || volume is null)
        {
            return false;
        }

        try
        {
            return volume.SetMute(muted ? 1 : 0, Guid.Empty) == NativeMethods.S_OK;
        }
        finally
        {
            Marshal.ReleaseComObject(volume);
        }
    }

    private static bool TryGetEndpoint(string? deviceId, out IAudioEndpointVolume? volume)
    {
        volume = null;
        object? enumeratorObj = null;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;

        try
        {
            Type? clsidType = Type.GetTypeFromCLSID(MmDeviceEnumeratorClsid);
            if (clsidType is null)
            {
                return false;
            }

            enumeratorObj = Activator.CreateInstance(clsidType);
            enumerator = (IMMDeviceEnumerator)enumeratorObj!;

            int hr;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                hr = enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out device);
            }
            else
            {
                hr = enumerator.GetDevice(deviceId, out device);
            }

            if (hr != NativeMethods.S_OK || device is null)
            {
                return false;
            }

            Guid iid = IAudioEndpointVolumeIid;
            hr = device.Activate(ref iid, ClsCtxInprocServer, IntPtr.Zero, out object endpoint);
            if (hr != NativeMethods.S_OK || endpoint is null)
            {
                return false;
            }

            volume = (IAudioEndpointVolume)endpoint;
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Core Audio open failed — {ex.Message}");
            return false;
        }
        finally
        {
            if (device is not null)
            {
                Marshal.ReleaseComObject(device);
            }

            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
            else if (enumeratorObj is not null)
            {
                Marshal.ReleaseComObject(enumeratorObj);
            }
        }
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(
            ref Guid iid,
            int dwClsCtx,
            IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    }

    /// <summary>Partial layout — only the slots we call; earlier methods are stubs for vtable alignment.</summary>
    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr pNotify);
        void UnregisterControlChangeNotify(IntPtr pNotify);
        void GetChannelCount(out uint pnChannelCount);
        void SetMasterVolumeLevel(float levelDb, Guid eventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, Guid eventContext);

        void GetMasterVolumeLevel(out float levelDb);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);

        void SetChannelVolumeLevel(uint channel, float levelDb, Guid eventContext);
        void SetChannelVolumeLevelScalar(uint channel, float level, Guid eventContext);
        void GetChannelVolumeLevel(uint channel, out float levelDb);
        void GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig]
        int SetMute(int bMute, Guid eventContext);

        [PreserveSig]
        int GetMute(out int pbMute);
    }
}
