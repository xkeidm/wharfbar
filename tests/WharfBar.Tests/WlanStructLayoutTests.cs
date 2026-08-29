using System.Runtime.InteropServices;
using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// WLAN P/Invoke struct'larının native wlanapi.h layout'uyla bayt-bayt uyumu.
/// Kaynak: Windows SDK 10.0.26100.0 um\wlanapi.h.
/// Geçmişte struct yanlıştı (sahte wlanProfileFlags ilk alan + eksik dwFlags/dwReserved
/// + uydurma RSSI alanları) → stride 632 vs native 628 → SSID metni kayıyor, sinyal çöp.
/// </summary>
public class WlanStructLayoutTests
{
    [Fact]
    public void WLAN_AVAILABLE_NETWORK_MatchesNativeSize628()
    {
        Assert.Equal(628, Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>());
    }

    [Fact]
    public void WLAN_AVAILABLE_NETWORK_FieldOffsets_MatchNative()
    {
        Assert.Equal(0, (int)Marshal.OffsetOf<WLAN_AVAILABLE_NETWORK>(nameof(WLAN_AVAILABLE_NETWORK.strProfileName)));
        Assert.Equal(512, (int)Marshal.OffsetOf<WLAN_AVAILABLE_NETWORK>(nameof(WLAN_AVAILABLE_NETWORK.dot11Ssid)));
        Assert.Equal(620, (int)Marshal.OffsetOf<WLAN_AVAILABLE_NETWORK>(nameof(WLAN_AVAILABLE_NETWORK.dwFlags)));
        Assert.Equal(624, (int)Marshal.OffsetOf<WLAN_AVAILABLE_NETWORK>(nameof(WLAN_AVAILABLE_NETWORK.dwReserved)));
    }

    [Fact]
    public void DOT11_SSID_MatchesNativeSize36()
    {
        Assert.Equal(36, Marshal.SizeOf<DOT11_SSID>());
        Assert.Equal(0, (int)Marshal.OffsetOf<DOT11_SSID>(nameof(DOT11_SSID.uSSIDLength)));
        Assert.Equal(4, (int)Marshal.OffsetOf<DOT11_SSID>(nameof(DOT11_SSID.ucSSID)));
    }

    [Fact]
    public void WLAN_INTERFACE_INFO_MatchesNativeSize532()
    {
        // Guid(16) + WCHAR[256](512) + DWORD(4)
        Assert.Equal(532, Marshal.SizeOf<WLAN_INTERFACE_INFO>());
    }

    [Fact]
    public void WLAN_INTERFACE_INFO_LIST_HeaderIs8Bytes()
    {
        // dwNumberOfItems + dwIndex → liste öğeleri 8. bayttan başlar.
        Assert.Equal(8, (int)Marshal.OffsetOf<WLAN_INTERFACE_INFO_LIST>(nameof(WLAN_INTERFACE_INFO_LIST.InterfaceInfo)));
    }
}
