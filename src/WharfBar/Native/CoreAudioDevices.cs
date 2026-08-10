using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal sealed record AudioOutputDevice(
    string Id,
    string Name,
    bool IsDefault,
    int Percent,
    bool IsMuted);

/// <summary>
/// Enumerates active render endpoints and sets the default multimedia device.
/// Listing uses documented MMDevice; SetDefault uses IPolicyConfig (practical path used by
/// EarTrumpet-class tools — no public WinRT setter for the system default).
/// </summary>
internal static class CoreAudioDevices
{
    private static readonly Guid MmDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid PolicyConfigClientClsid = new("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9");

    private static readonly PROPERTYKEY PkeyDeviceFriendlyName = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14,
    };

    private const int EDataFlowRender = 0;
    private const int ERoleMultimedia = 1;
    private const int DeviceStateActive = 0x1;
    private const int ClsCtxInprocServer = 1;
    private const int StgmRead = 0;

    internal static IReadOnlyList<AudioOutputDevice> ListActiveRenderDevices()
    {
        var list = new List<AudioOutputDevice>();
        object? enumeratorObj = null;
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        string? defaultId = null;

        try
        {
            if (!TryCreateEnumerator(out enumeratorObj, out enumerator) || enumerator is null)
            {
                return list;
            }

            if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out IMMDevice? defaultDevice)
                == NativeMethods.S_OK
                && defaultDevice is not null)
            {
                try
                {
                    if (defaultDevice.GetId(out string? id) == NativeMethods.S_OK)
                    {
                        defaultId = id;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(defaultDevice);
                }
            }

            if (enumerator.EnumAudioEndpoints(EDataFlowRender, DeviceStateActive, out collection)
                == NativeMethods.S_OK
                && collection is not null
                && collection.GetCount(out uint count) == NativeMethods.S_OK)
            {
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out IMMDevice? device) != NativeMethods.S_OK || device is null)
                    {
                        continue;
                    }

                    try
                    {
                        TryAddDevice(device, defaultId, list);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }

            // If enum failed but the default endpoint is available (VolumeService path works),
            // still surface one block so the mixer is usable.
            if (list.Count == 0 && !string.IsNullOrEmpty(defaultId)
                && enumerator.GetDevice(defaultId, out IMMDevice? fallback) == NativeMethods.S_OK
                && fallback is not null)
            {
                try
                {
                    TryAddDevice(fallback, defaultId, list);
                }
                finally
                {
                    Marshal.ReleaseComObject(fallback);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Core Audio device list failed — {ex.Message}");
        }
        finally
        {
            if (collection is not null)
            {
                Marshal.ReleaseComObject(collection);
            }

            ReleaseEnumerator(enumerator, enumeratorObj);
        }

        return list;
    }

    internal static bool TrySetDefaultRenderDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        object? policyObj = null;
        try
        {
            Type? type = Type.GetTypeFromCLSID(PolicyConfigClientClsid);
            if (type is null)
            {
                return false;
            }

            policyObj = Activator.CreateInstance(type);
            if (policyObj is null)
            {
                return false;
            }

            var policy = (IPolicyConfig)policyObj;
            // eConsole = 0, eMultimedia = 1, eCommunications = 2 — set all three roles.
            int hr0 = policy.SetDefaultEndpoint(deviceId, 0);
            int hr1 = policy.SetDefaultEndpoint(deviceId, 1);
            int hr2 = policy.SetDefaultEndpoint(deviceId, 2);
            return hr0 == NativeMethods.S_OK || hr1 == NativeMethods.S_OK || hr2 == NativeMethods.S_OK;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: SetDefaultEndpoint failed — {ex.Message}");
            return false;
        }
        finally
        {
            if (policyObj is not null)
            {
                Marshal.ReleaseComObject(policyObj);
            }
        }
    }

    private static void TryAddDevice(IMMDevice device, string? defaultId, List<AudioOutputDevice> list)
    {
        if (device.GetId(out string? id) != NativeMethods.S_OK || string.IsNullOrEmpty(id))
        {
            return;
        }

        string name = TryGetFriendlyName(device) ?? id;
        int percent = 100;
        bool muted = false;
        if (AudioEndpointVolume.TryGetScalarForDevice(id, out float level, out muted))
        {
            percent = Math.Clamp(
                (int)Math.Round(level * 100f, MidpointRounding.AwayFromZero),
                0,
                100);
        }

        list.Add(new AudioOutputDevice(
            id,
            name,
            string.Equals(id, defaultId, StringComparison.Ordinal),
            percent,
            muted));
    }

    private static string? TryGetFriendlyName(IMMDevice device)
    {
        if (device.OpenPropertyStore(StgmRead, out IPropertyStore? store) != NativeMethods.S_OK || store is null)
        {
            return null;
        }

        try
        {
            PROPERTYKEY key = PkeyDeviceFriendlyName;
            if (store.GetValue(ref key, out PropVariant value) != NativeMethods.S_OK)
            {
                return null;
            }

            try
            {
                if (value.vt == 31 && value.union.pszVal != IntPtr.Zero) // VT_LPWSTR
                {
                    return Marshal.PtrToStringUni(value.union.pszVal);
                }

                if (value.vt == 8 && value.union.bstrVal != IntPtr.Zero) // VT_BSTR
                {
                    return Marshal.PtrToStringBSTR(value.union.bstrVal);
                }
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }

        return null;
    }

    private static bool TryCreateEnumerator(out object? enumeratorObj, out IMMDeviceEnumerator? enumerator)
    {
        enumeratorObj = null;
        enumerator = null;
        Type? clsidType = Type.GetTypeFromCLSID(MmDeviceEnumeratorClsid);
        if (clsidType is null)
        {
            return false;
        }

        enumeratorObj = Activator.CreateInstance(clsidType);
        enumerator = (IMMDeviceEnumerator)enumeratorObj!;
        return true;
    }

    private static void ReleaseEnumerator(IMMDeviceEnumerator? enumerator, object? enumeratorObj)
    {
        if (enumerator is not null)
        {
            Marshal.ReleaseComObject(enumerator);
        }
        else if (enumeratorObj is not null)
        {
            Marshal.ReleaseComObject(enumeratorObj);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public PropVariantUnion union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariantUnion
    {
        [FieldOffset(0)] public IntPtr pszVal;
        [FieldOffset(0)] public IntPtr bstrVal;
        [FieldOffset(0)] public long hVal;
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IMMDeviceCollection devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint pcDevices);

        [PreserveSig]
        int Item(uint nDevice, out IMMDevice ppDevice);
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

        [PreserveSig]
        int OpenPropertyStore(int stgmAccess, out IPropertyStore ppProperties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);

        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);

        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PropVariant pv);

        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PropVariant pv);

        [PreserveSig]
        int Commit();
    }

    /// <summary>Undocumented but stable PolicyConfig used to set the default endpoint.</summary>
    [ComImport]
    [Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        void Unused1();
        void Unused2();
        void Unused3();
        void Unused4();
        void Unused5();
        void Unused6();
        void Unused7();
        void Unused8();
        void Unused9();
        void Unused10();

        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
    }
}
