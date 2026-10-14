using System.Runtime.InteropServices;

namespace WharfBar.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct DOT11_SSID
{
    public uint uSSIDLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    public byte[] ucSSID;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WLAN_INTERFACE_INFO
{
    public Guid InterfaceGuid;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string strInterfaceDescription;
    public uint isState;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WLAN_INTERFACE_INFO_LIST
{
    public uint dwNumberOfItems;
    public uint dwIndex;
    public WLAN_INTERFACE_INFO InterfaceInfo;
}

/// <summary>
/// wlanapi.h'taki native layout'un birebir kopyası — alan sırası/boyut testle kilitli
/// (WlanStructLayoutTests: toplam 628 bayt, dwFlags offset 620). Geçmişte struct sapmıştı
/// ve SSID metni kayıyordu; değiştirmeden önce testleri güncelle.
/// </summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WLAN_AVAILABLE_NETWORK
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string strProfileName;
    public DOT11_SSID dot11Ssid;
    public uint dot11BssType;
    public uint uNumberOfBssids;
    public uint bNetworkConnectable;
    public uint wlanNotConnectableReason;
    public uint uNumberOfPhyTypes;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public uint[] dot11PhyType;
    public uint bMorePhyTypes;
    public uint wlanSignalQuality;
    public uint bSecurityEnabled;
    public uint dot11DefaultAuthAlgorithm;
    public uint dot11DefaultCipherAlgorithm;
    public uint dwFlags;
    public uint dwReserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WLAN_AVAILABLE_NETWORK_LIST
{
    public uint dwNumberOfItems;
    public uint dwIndex;
    public WLAN_AVAILABLE_NETWORK Network;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WLAN_CONNECTION_PARAMETERS
{
    public uint wlanConnectionMode;
    public IntPtr strProfile;
    public IntPtr pDot11Ssid;
    public IntPtr pDesiredBssidList;
    public uint dot11BssType;
    public uint dwFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WLAN_ASSOCIATION_ATTRIBUTES
{
    public DOT11_SSID dot11Ssid;
    public uint dot11BssType;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    public byte[] dot11Bssid;
    public uint dot11PhyType;
    public uint uDot11PhyIndex;
    public uint wlanSignalQuality;
    public uint ulRxRate;
    public uint ulTxRate;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WLAN_SECURITY_ATTRIBUTES
{
    public uint bSecurityEnabled;
    public uint bOneXEnabled;
    public uint dot11AuthAlgorithm;
    public uint dot11CipherAlgorithm;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WLAN_CONNECTION_ATTRIBUTES
{
    public uint isState;
    public uint wlanConnectionMode;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    public string strProfileName;
    public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
    public WLAN_SECURITY_ATTRIBUTES wlanSecurityAttributes;
}

internal static class WlanApi
{
    internal const uint ERROR_SUCCESS = 0;
    internal const uint WLAN_AVAILABLE_NETWORK_CONNECTED = 1;
    internal const uint WLAN_AVAILABLE_NETWORK_HAS_PROFILE = 2;
    /// <summary><c>wlan_interface_state_connected</c> (wlanapi.h). 4 is disconnected.</summary>
    internal const uint WLAN_INTERFACE_STATE_CONNECTED = 1;
    private const uint wlan_connection_mode_profile = 1;
    private const uint dot11_BSS_type_infrastructure = 2;
    private const uint wlan_intf_opcode_current_connection = 7;
    private const int InterfaceListHeaderBytes = 8;

    internal readonly record struct RawNetwork(
        byte[] Ssid,
        int SsidLength,
        int Signal,
        bool Secured,
        bool HasProfile,
        bool Connected,
        string? ProfileName,
        uint AuthAlgorithm = 0,
        bool Connectable = true);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint dwFlags, IntPtr pReserved, out IntPtr ppAvailableNetworkList);

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanSetProfile(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint dwFlags, string strProfileXml, IntPtr strAllUserProfileSecurity, bool bOverwrite, IntPtr pReserved, out uint pdwReasonCode);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanConnect(IntPtr hClientHandle, ref Guid pInterfaceGuid, ref WLAN_CONNECTION_PARAMETERS pConnectionParameters);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanDisconnect(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint OpCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanScan(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pDot11Ssid, IntPtr pIeData, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    internal static bool TryOpen(out IntPtr handle)
    {
        handle = IntPtr.Zero;
        return WlanOpenHandle(2, IntPtr.Zero, out _, out handle) == ERROR_SUCCESS && handle != IntPtr.Zero;
    }

    internal static void Close(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    internal static bool TryGetInterfaceGuid(IntPtr handle, out Guid guid)
    {
        guid = Guid.Empty;
        if (WlanEnumInterfaces(handle, IntPtr.Zero, out IntPtr listPtr) != ERROR_SUCCESS)
        {
            return false;
        }

        try
        {
            WLAN_INTERFACE_INFO_LIST list = Marshal.PtrToStructure<WLAN_INTERFACE_INFO_LIST>(listPtr);
            if (list.dwNumberOfItems == 0)
            {
                return false;
            }

            int stride = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
            IntPtr first = listPtr + InterfaceListHeaderBytes;
            Guid? connected = null;
            Guid fallback = Guid.Empty;
            for (int i = 0; i < list.dwNumberOfItems; i++)
            {
                WLAN_INTERFACE_INFO info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(first + (i * stride));
                if (i == 0)
                {
                    fallback = info.InterfaceGuid;
                }

                if (info.isState == WLAN_INTERFACE_STATE_CONNECTED)
                {
                    connected = info.InterfaceGuid;
                    break;
                }
            }

            guid = connected ?? fallback;
            return true;
        }
        finally
        {
            WlanFreeMemory(listPtr);
        }
    }

    /// <summary>WlanScan tetikler; sonuç asenkron gelir, sonraki GetAvailableNetworkList çağrılarında görünür.</summary>
    internal static void Scan(IntPtr handle, Guid guid)
    {
        WlanScan(handle, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    }

    internal static bool TryGetNetworks(IntPtr handle, Guid guid, out List<RawNetwork> networks)
    {
        networks = [];
        if (WlanGetAvailableNetworkList(handle, ref guid, 0, IntPtr.Zero, out IntPtr listPtr) != ERROR_SUCCESS)
        {
            return false;
        }

        try
        {
            WLAN_AVAILABLE_NETWORK_LIST list = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK_LIST>(listPtr);
            int stride = Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>();
            IntPtr itemPtr = listPtr + InterfaceListHeaderBytes;
            for (int i = 0; i < list.dwNumberOfItems; i++)
            {
                WLAN_AVAILABLE_NETWORK n = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(itemPtr + i * stride);
                bool hasProfile = (n.dwFlags & WLAN_AVAILABLE_NETWORK_HAS_PROFILE) != 0;
                networks.Add(new RawNetwork(
                    n.dot11Ssid.ucSSID,
                    (int)n.dot11Ssid.uSSIDLength,
                    (int)n.wlanSignalQuality,
                    n.bSecurityEnabled != 0,
                    hasProfile,
                    (n.dwFlags & WLAN_AVAILABLE_NETWORK_CONNECTED) != 0,
                    hasProfile && !string.IsNullOrEmpty(n.strProfileName) ? n.strProfileName : null,
                    n.dot11DefaultAuthAlgorithm,
                    n.bNetworkConnectable != 0));
            }

            return true;
        }
        finally
        {
            WlanFreeMemory(listPtr);
        }
    }

    internal static bool TrySetProfile(IntPtr handle, Guid guid, string xml, out uint reason)
    {
        reason = 0;
        return WlanSetProfile(handle, ref guid, 0, xml, IntPtr.Zero, true, IntPtr.Zero, out reason) == ERROR_SUCCESS;
    }

    internal static bool TryConnectProfile(IntPtr handle, Guid guid, string profileName)
    {
        var parameters = new WLAN_CONNECTION_PARAMETERS
        {
            wlanConnectionMode = wlan_connection_mode_profile,
            strProfile = Marshal.StringToCoTaskMemUni(profileName),
            pDot11Ssid = IntPtr.Zero,
            pDesiredBssidList = IntPtr.Zero,
            dot11BssType = dot11_BSS_type_infrastructure,
            dwFlags = 0,
        };

        try
        {
            return WlanConnect(handle, ref guid, ref parameters) == ERROR_SUCCESS;
        }
        finally
        {
            Marshal.FreeCoTaskMem(parameters.strProfile);
        }
    }

    internal static bool TryDisconnect(IntPtr handle, Guid guid) =>
        WlanDisconnect(handle, ref guid, IntPtr.Zero) == ERROR_SUCCESS;

    internal static bool TryGetCurrentSsid(IntPtr handle, Guid guid, out byte[] ssid)
    {
        ssid = Array.Empty<byte>();
        if (WlanQueryInterface(handle, ref guid, wlan_intf_opcode_current_connection, IntPtr.Zero, out _, out IntPtr data, IntPtr.Zero) != ERROR_SUCCESS)
        {
            return false;
        }

        try
        {
            WLAN_CONNECTION_ATTRIBUTES attrs = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(data);
            if (attrs.isState != WLAN_INTERFACE_STATE_CONNECTED)
            {
                return false;
            }

            DOT11_SSID dot11 = attrs.wlanAssociationAttributes.dot11Ssid;
            int len = Math.Min((int)dot11.uSSIDLength, dot11.ucSSID?.Length ?? 0);
            if (len <= 0 || dot11.ucSSID is null)
            {
                return false;
            }

            ssid = dot11.ucSSID[..len];
            return true;
        }
        finally
        {
            WlanFreeMemory(data);
        }
    }
}
