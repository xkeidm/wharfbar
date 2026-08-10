using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal sealed record AudioAppSession(
    string SessionInstanceId,
    string DeviceId,
    string DisplayName,
    int ProcessId,
    string? ProcessPath,
    string? IconPath,
    int Percent,
    bool IsMuted);

/// <summary>
/// Per-application (and system-sounds) volumes via documented
/// <c>IAudioSessionManager2</c> / <c>ISimpleAudioVolume</c> on render endpoints.
/// </summary>
internal static class CoreAudioSessions
{
    private static readonly Guid MmDeviceEnumeratorClsid = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IAudioSessionManager2Iid = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

    private const int EDataFlowRender = 0;
    private const int ERoleMultimedia = 1;
    private const int ClsCtxInprocServer = 1;
    private const int DeviceStateActive = 0x1;
    private const int AudioSessionStateExpired = 2;

    internal static IReadOnlyList<AudioAppSession> ListSessionsOnDefaultRender()
    {
        string? defaultId = TryGetDefaultRenderId();
        return string.IsNullOrEmpty(defaultId)
            ? Array.Empty<AudioAppSession>()
            : ListSessionsOnDevice(defaultId);
    }

    internal static IReadOnlyList<AudioAppSession> ListSessionsOnDevice(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return Array.Empty<AudioAppSession>();
        }

        var list = new List<AudioAppSession>();
        object? enumeratorObj = null;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;

        try
        {
            if (!TryCreateEnumerator(out enumeratorObj, out enumerator) || enumerator is null)
            {
                return list;
            }

            if (enumerator.GetDevice(deviceId, out device) != NativeMethods.S_OK || device is null)
            {
                return list;
            }

            CollectSessions(device, deviceId, list);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Core Audio sessions failed — {ex.Message}");
        }
        finally
        {
            ReleaseCom(device);
            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
            else if (enumeratorObj is not null)
            {
                Marshal.ReleaseComObject(enumeratorObj);
            }
        }

        return list
            .OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Sessions on every active render device (EarTrumpet-style grouping).</summary>
    internal static IReadOnlyList<AudioAppSession> ListSessionsOnAllActiveRenderDevices()
    {
        var list = new List<AudioAppSession>();
        object? enumeratorObj = null;
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            if (!TryCreateEnumerator(out enumeratorObj, out enumerator) || enumerator is null)
            {
                return list;
            }

            if (enumerator.EnumAudioEndpoints(EDataFlowRender, DeviceStateActive, out collection)
                != NativeMethods.S_OK
                || collection is null)
            {
                return list;
            }

            if (collection.GetCount(out uint count) != NativeMethods.S_OK)
            {
                return list;
            }

            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out IMMDevice? device) != NativeMethods.S_OK || device is null)
                {
                    continue;
                }

                try
                {
                    if (device.GetId(out string? id) != NativeMethods.S_OK || string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    CollectSessions(device, id, list);
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Core Audio all-device sessions failed — {ex.Message}");
        }
        finally
        {
            ReleaseCom(collection);
            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
            else if (enumeratorObj is not null)
            {
                Marshal.ReleaseComObject(enumeratorObj);
            }
        }

        return list
            .OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal static bool TrySetSessionVolume(string sessionInstanceId, float level01, bool? muted = null)
    {
        level01 = Math.Clamp(level01, 0f, 1f);
        return MutateSession(
            sessionInstanceId,
            simple =>
            {
                int hr = simple.SetMasterVolume(level01, Guid.Empty);
                if (hr != NativeMethods.S_OK)
                {
                    return false;
                }

                if (muted is { } m)
                {
                    return simple.SetMute(m ? 1 : 0, Guid.Empty) == NativeMethods.S_OK;
                }

                return true;
            });
    }

    internal static bool TrySetSessionMute(string sessionInstanceId, bool muted)
    {
        return MutateSession(
            sessionInstanceId,
            simple => simple.SetMute(muted ? 1 : 0, Guid.Empty) == NativeMethods.S_OK);
    }

    private static void CollectSessions(IMMDevice device, string deviceId, List<AudioAppSession> list)
    {
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessionEnum = null;
        try
        {
            Guid iid = IAudioSessionManager2Iid;
            if (device.Activate(ref iid, ClsCtxInprocServer, IntPtr.Zero, out object? managerObj)
                != NativeMethods.S_OK
                || managerObj is null)
            {
                return;
            }

            manager = (IAudioSessionManager2)managerObj;
            if (manager.GetSessionEnumerator(out sessionEnum) != NativeMethods.S_OK || sessionEnum is null)
            {
                return;
            }

            if (sessionEnum.GetCount(out int count) != NativeMethods.S_OK)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                if (sessionEnum.GetSession(i, out IAudioSessionControl? control) != NativeMethods.S_OK
                    || control is null)
                {
                    continue;
                }

                try
                {
                    if (TryReadSession(control, deviceId, out AudioAppSession? session) && session is not null)
                    {
                        list.Add(session);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(control);
                }
            }
        }
        finally
        {
            ReleaseCom(sessionEnum);
            ReleaseCom(manager);
        }
    }

    private static bool MutateSession(string sessionInstanceId, Func<ISimpleAudioVolume, bool> mutate)
    {
        if (string.IsNullOrEmpty(sessionInstanceId))
        {
            return false;
        }

        object? enumeratorObj = null;
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;

        try
        {
            if (!TryCreateEnumerator(out enumeratorObj, out enumerator) || enumerator is null)
            {
                return false;
            }

            if (enumerator.EnumAudioEndpoints(EDataFlowRender, DeviceStateActive, out collection)
                != NativeMethods.S_OK
                || collection is null)
            {
                return false;
            }

            if (collection.GetCount(out uint count) != NativeMethods.S_OK)
            {
                return false;
            }

            for (uint d = 0; d < count; d++)
            {
                if (collection.Item(d, out IMMDevice? device) != NativeMethods.S_OK || device is null)
                {
                    continue;
                }

                try
                {
                    if (TryMutateOnDevice(device, sessionInstanceId, mutate))
                    {
                        return true;
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Set session volume failed — {ex.Message}");
            return false;
        }
        finally
        {
            ReleaseCom(collection);
            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }
            else if (enumeratorObj is not null)
            {
                Marshal.ReleaseComObject(enumeratorObj);
            }
        }

        return false;
    }

    private static bool TryMutateOnDevice(
        IMMDevice device,
        string sessionInstanceId,
        Func<ISimpleAudioVolume, bool> mutate)
    {
        IAudioSessionManager2? manager = null;
        IAudioSessionEnumerator? sessionEnum = null;
        try
        {
            Guid iid = IAudioSessionManager2Iid;
            if (device.Activate(ref iid, ClsCtxInprocServer, IntPtr.Zero, out object? managerObj)
                != NativeMethods.S_OK
                || managerObj is null)
            {
                return false;
            }

            manager = (IAudioSessionManager2)managerObj;
            if (manager.GetSessionEnumerator(out sessionEnum) != NativeMethods.S_OK || sessionEnum is null)
            {
                return false;
            }

            if (sessionEnum.GetCount(out int count) != NativeMethods.S_OK)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (sessionEnum.GetSession(i, out IAudioSessionControl? control) != NativeMethods.S_OK
                    || control is null)
                {
                    continue;
                }

                try
                {
                    var control2 = (IAudioSessionControl2)control;
                    if (control2.GetSessionInstanceIdentifier(out string? id) != NativeMethods.S_OK
                        || !string.Equals(id, sessionInstanceId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var simple = (ISimpleAudioVolume)control;
                    return mutate(simple);
                }
                finally
                {
                    Marshal.ReleaseComObject(control);
                }
            }
        }
        finally
        {
            ReleaseCom(sessionEnum);
            ReleaseCom(manager);
        }

        return false;
    }

    private static bool TryReadSession(IAudioSessionControl control, string deviceId, out AudioAppSession? session)
    {
        session = null;
        try
        {
            var control2 = (IAudioSessionControl2)control;
            if (control2.GetState(out int state) == NativeMethods.S_OK
                && state == AudioSessionStateExpired)
            {
                return false;
            }

            if (control2.GetSessionInstanceIdentifier(out string? instanceId) != NativeMethods.S_OK
                || string.IsNullOrEmpty(instanceId))
            {
                return false;
            }

            control2.GetProcessId(out uint pid);
            bool isSystem = control2.IsSystemSoundsSession() == NativeMethods.S_OK;

            string display = ResolveDisplayName(control2, (int)pid, isSystem);
            var simple = (ISimpleAudioVolume)control;
            if (simple.GetMasterVolume(out float level) != NativeMethods.S_OK)
            {
                level = 1f;
            }

            if (simple.GetMute(out int muteInt) != NativeMethods.S_OK)
            {
                muteInt = 0;
            }

            string? path = TryGetProcessPath((int)pid);
            string? iconPath = null;
            if (control2.GetIconPath(out string? rawIcon) == NativeMethods.S_OK
                && !string.IsNullOrWhiteSpace(rawIcon)
                && !rawIcon.StartsWith('@'))
            {
                iconPath = rawIcon;
            }

            int percent = (int)Math.Round(level * 100f, MidpointRounding.AwayFromZero);
            session = new AudioAppSession(
                instanceId,
                deviceId,
                display,
                (int)pid,
                path,
                iconPath,
                Math.Clamp(percent, 0, 100),
                muteInt != 0);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Read audio session failed — {ex.Message}");
            return false;
        }
    }

    private static string ResolveDisplayName(IAudioSessionControl2 control2, int pid, bool isSystem)
    {
        if (control2.GetDisplayName(out string? name) == NativeMethods.S_OK
            && !string.IsNullOrWhiteSpace(name)
            && !name.StartsWith('@'))
        {
            return name;
        }

        if (pid > 0)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (!string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    return process.MainWindowTitle;
                }

                if (!string.IsNullOrWhiteSpace(process.ProcessName))
                {
                    return process.ProcessName;
                }
            }
            catch
            {
                // Process may have exited.
            }
        }

        if (isSystem)
        {
            return "Sistem sesleri";
        }

        return "Uygulama";
    }

    private static string? TryGetProcessPath(int pid)
    {
        if (pid <= 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetDefaultRenderId()
    {
        object? enumeratorObj = null;
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            if (!TryCreateEnumerator(out enumeratorObj, out enumerator) || enumerator is null)
            {
                return null;
            }

            if (enumerator.GetDefaultAudioEndpoint(EDataFlowRender, ERoleMultimedia, out device)
                != NativeMethods.S_OK
                || device is null)
            {
                return null;
            }

            return device.GetId(out string? id) == NativeMethods.S_OK ? id : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseCom(device);
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

    private static void ReleaseCom(object? com)
    {
        if (com is not null)
        {
            Marshal.ReleaseComObject(com);
        }
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
        int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    }

    [ComImport]
    [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        [PreserveSig]
        int GetAudioSessionControl(IntPtr AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);

        [PreserveSig]
        int GetSimpleAudioVolume(IntPtr AudioSessionGuid, uint StreamFlags, out IntPtr AudioVolume);

        [PreserveSig]
        int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

        [PreserveSig]
        int RegisterSessionNotification(IntPtr sessionNotification);

        [PreserveSig]
        int UnregisterSessionNotification(IntPtr sessionNotification);
    }

    /// <summary>
    /// Windows returns an enumerator whose vtable is <c>IAudioSessionEnumerator</c> but
    /// <c>QueryInterface(E2F5BB11-…)</c> fails with <c>E_NOINTERFACE</c>. Tagging the
    /// ComImport with the IUnknown IID lets the marshaller wrap the pointer; GetCount /
    /// GetSession then call through the real enumerator vtable (verified on Win11).
    /// </summary>
    [ComImport]
    [Guid("00000000-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        [PreserveSig]
        int GetCount(out int sessionCount);

        [PreserveSig]
        int GetSession(int sessionCount, out IAudioSessionControl session);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetDisplayName();

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);

        void SetIconPath();
        void GetGroupingParam();
        void SetGroupingParam();
        void RegisterAudioSessionNotification();
        void UnregisterAudioSessionNotification();
    }

    [ComImport]
    [Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);

        void SetDisplayName();

        [PreserveSig]
        int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);

        void SetIconPath();
        void GetGroupingParam();
        void SetGroupingParam();
        void RegisterAudioSessionNotification();
        void UnregisterAudioSessionNotification();

        [PreserveSig]
        int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);

        [PreserveSig]
        int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string retVal);

        [PreserveSig]
        int GetProcessId(out uint retVal);

        [PreserveSig]
        int IsSystemSoundsSession();

        [PreserveSig]
        int SetDuckingPreference(int optOut);
    }

    [ComImport]
    [Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        [PreserveSig]
        int SetMasterVolume(float fLevel, Guid eventContext);

        [PreserveSig]
        int GetMasterVolume(out float pfLevel);

        [PreserveSig]
        int SetMute(int bMute, Guid eventContext);

        [PreserveSig]
        int GetMute(out int pbMute);
    }
}
