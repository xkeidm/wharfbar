# Hızlı Ayarlar: Canlı Ağ, Gece Işığı, Yansıtma ve Animasyonlar — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Quick Settings flyout'una canlı Wi-Fi listesi (+ bağlanma), eşleşmiş Bluetooth cihaz listesi, gerçek gece ışığı durumu/toggle'ı, native yansıtma paneli ve tile toggle animasyonları eklemek.

**Architecture:** Yeni Win32/WinRT erişimi `Native/` ince sarmalayıcılarında; domain modelleri ve servisler `Services/` altında. Her servis kendi `DispatcherTimer` poll'unu ve `Changed` event'ini yönetir (mevcut `VolumeService`/`AudioMixerService` deseni). Flyout mevcut sayfa-navigasyon desenini (`Mixer`/`Network`) üç yeni sayfayla genişletir; tile'lar poll'da yeniden kurulmaz, in-place güncellenir ve state geçişinde animasyon oynatır.

**Tech Stack:** .NET 8 + WPF (WPF-UI), Win32 P/Invoke (`wlanapi.dll`, `user32.dll`), WinRT (`Windows.Devices.Enumeration`, `Windows.Devices.Bluetooth`), xUnit.

**Spec:** [`../specs/2026-08-16-quick-settings-live-network-nightlight-project-design.md`](../specs/2026-08-16-quick-settings-live-network-nightlight-project-design.md)

## Global Constraints

- Repo kuralı (AGENTS.md): **kullanıcı istemedikçe commit yapılmaz** — her task'ın sonundaki "Commit" adımlarını atla; yerine "Doğrulama" adımı yap.
- Hedef: `net8.0-windows10.0.19041.0`; `global.json` ile 8.x'e sabitleme — 8.x SDK yoksa derleme kırılır.
- P/Invoke yalnızca `Native/` içinde; UI'dan ham `DllImport` çağrılmasın.
- Win32 çağrılarında hata dönüşünü sessiz yutma — en azından `Debug.WriteLine` ile logla.
- Yeni Win32 imzaları `docs/02-win32-appbar-dwm.md`'ye eklenir (Task 10).
- C# kuralları: `PascalCase` tür/üye, private field `_camelCase`, nullable açık.
- WPF-UI stil adları flyout XAML'indeki mevcut `DynamicResource` brush'ları kullanır; yeni kaynak brush tanımlama.
- Wlan API / registry blob / `SetDisplayConfig` / BT donanım yolları unit test edilmez; saf mantık (parser, eşleme, dönüşüm) TDD ile test edilir.

---

### Task 1: Gece ışığı blob parser

**Files:**
- Create: `src/WharfBar/Services/NightLightBlob.cs`
- Test: `tests/WharfBar.Tests/NightLightBlobTests.cs`

**Interfaces:**
- Consumes: yok (saf statik).
- Produces: `internal static class NightLightBlob` — `bool TryParse(byte[]? data, out bool enabled)`; Task 2'nin yazma yolu için `internal static bool TryFindFlag(byte[] data, out int flagIndex, out bool enabled)` ve `internal static byte[] ToggleFlag(byte[] data, bool enable)`.

CloudStore `bluelightreductionstate` `Data` değeri, ikinci `43 42 01 00` başlığından hemen sonra `02 01` bayrağı taşıdığında gece ışığı AÇIK demektir; bayrak yoksa KAPALI. Yapı tanınmazsa `false` (bilinmiyor) döner.

- [ ] **Step 1: Testi yaz**

```csharp
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class NightLightBlobTests
{
    private static byte[] EnabledBlob() =>
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00,
        0x2A, 0x06, 0xC1, 0x98, 0x99, 0xFB, 0x05, 0x2A,
        0x2B, 0x0E, 0x1F, 0x43, 0x42, 0x01, 0x00, 0x02,
        0x01, 0xCA, 0x14, 0x0E, 0x13, 0x2E, 0x39, 0x00,
    ];

    private static byte[] DisabledBlob() =>
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00,
        0x2A, 0x06, 0xC1, 0x98, 0x99, 0xFB, 0x05, 0x2A,
        0x2B, 0x0E, 0x1F, 0x43, 0x42, 0x01, 0x00, 0xCA,
        0x14, 0x0E, 0x13, 0x2E, 0x39, 0x00,
    ];

    [Fact]
    public void TryParse_EnabledBlob_IsKnownAndTrue()
    {
        Assert.True(NightLightBlob.TryParse(EnabledBlob(), out bool enabled));
        Assert.True(enabled);
    }

    [Fact]
    public void TryParse_DisabledBlob_IsKnownAndFalse()
    {
        Assert.True(NightLightBlob.TryParse(DisabledBlob(), out bool enabled));
        Assert.False(enabled);
    }

    [Fact]
    public void TryParse_Null_IsUnknown()
    {
        Assert.False(NightLightBlob.TryParse(null, out _));
    }

    [Fact]
    public void TryParse_EmptyOrTooShort_IsUnknown()
    {
        Assert.False(NightLightBlob.TryParse(Array.Empty<byte>(), out _));
        Assert.False(NightLightBlob.TryParse(new byte[] { 0x43, 0x42, 0x01, 0x00, 0x0A }, out _));
    }

    [Fact]
    public void TryParse_SingleSignatureOnly_IsUnknown()
    {
        byte[] data = [0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00];
        Assert.False(NightLightBlob.TryParse(data, out _));
    }

    [Fact]
    public void ToggleFlag_EnabledRemovesFlag()
    {
        byte[] disabled = NightLightBlob.ToggleFlag(EnabledBlob(), enable: false);
        Assert.True(NightLightBlob.TryParse(disabled, out bool enabled));
        Assert.False(enabled);
        Assert.Equal(EnabledBlob().Length - 2, disabled.Length);
    }

    [Fact]
    public void ToggleFlag_DisabledAddsFlag()
    {
        byte[] enabled = NightLightBlob.ToggleFlag(DisabledBlob(), enable: true);
        Assert.True(NightLightBlob.TryParse(enabled, out bool flagOn));
        Assert.True(flagOn);
        Assert.Equal(DisabledBlob().Length + 2, enabled.Length);
    }
}
```

- [ ] **Step 2: Testi çalıştır, başarısız olduğunu gör**

Run: `dotnet test tests/WharfBar.Tests --filter "NightLightBlobTests"`
Expected: FAIL — `NightLightBlob` türü yok.

- [ ] **Step 3: Minimal implementasyon**

```csharp
namespace WharfBar.Services;

internal static class NightLightBlob
{
    private static readonly byte[] Signature = [0x43, 0x42, 0x01, 0x00];
    private static readonly byte[] ActiveFlag = [0x02, 0x01];

    /// <summary>Best-effort: CloudStore blob layout değişebilir; yapı tanınmazsa false döner.</summary>
    internal static bool TryParse(byte[]? data, out bool enabled)
    {
        enabled = false;
        if (data is null || data.Length < 8)
        {
            return false;
        }

        if (!TryFindFlag(data, out int flagIndex, out enabled))
        {
            return false;
        }

        return flagIndex >= 0;
    }

    /// <summary>İkinci imzadan hemen sonra 02 01 bayrağı varsa enabled=true; bayrak yoksa false.</summary>
    internal static bool TryFindFlag(byte[] data, out int flagIndex, out bool enabled)
    {
        flagIndex = -1;
        enabled = false;

        int first = IndexOf(data, Signature, 0);
        if (first < 0)
        {
            return false;
        }

        int second = IndexOf(data, Signature, first + Signature.Length);
        if (second < 0)
        {
            return false;
        }

        flagIndex = second + Signature.Length;
        if (flagIndex >= data.Length)
        {
            return false;
        }

        enabled = flagIndex + 1 < data.Length
            && data[flagIndex] == ActiveFlag[0]
            && data[flagIndex + 1] == ActiveFlag[1];
        return true;
    }

    /// <summary>Bayrağı ekler/çıkarır; bayrak konumu bulunamazsa girdiyi olduğu gibi döner.</summary>
    internal static byte[] ToggleFlag(byte[] data, bool enable)
    {
        if (!TryFindFlag(data, out int flagIndex, out bool currentlyEnabled)
            || currentlyEnabled == enable)
        {
            return data;
        }

        if (enable)
        {
            var result = new byte[data.Length + ActiveFlag.Length];
            Array.Copy(data, result, flagIndex);
            ActiveFlag.CopyTo(result, flagIndex);
            Array.Copy(data, flagIndex, result, flagIndex + ActiveFlag.Length, data.Length - flagIndex);
            return result;
        }

        var trimmed = new byte[data.Length - ActiveFlag.Length];
        Array.Copy(data, trimmed, flagIndex);
        Array.Copy(data, flagIndex + ActiveFlag.Length, trimmed, flagIndex, data.Length - flagIndex - ActiveFlag.Length);
        return trimmed;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
```

- [ ] **Step 4: Testi çalıştır, geçtiğini gör**

Run: `dotnet test tests/WharfBar.Tests --filter "NightLightBlobTests"`
Expected: PASS (8 test).

---

### Task 2: NightLightService

**Files:**
- Create: `src/WharfBar/Services/NightLightService.cs`

**Interfaces:**
- Consumes: `NightLightBlob` (Task 1).
- Produces: `internal sealed class NightLightService` — `bool IsEnabled { get; }`, `void SetEnabled(bool enabled)`, `void PollRegistry()`.

- [ ] **Step 1: Servisi yaz**

```csharp
using System.Diagnostics;
using Microsoft.Win32;

namespace WharfBar.Services;

/// <summary>
/// Best-effort gece ışığı okuma/yazma. Registry blob formatı Windows sürümleri arasında
/// kırılabilir; okuyucu "bilinmiyor" dönerse oturum override'ı tile'ı doğru tutar.
/// </summary>
internal sealed class NightLightService
{
    private const string StateKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    private bool? _override;
    private bool _lastKnown;

    internal bool IsEnabled => _override ?? _lastKnown;

    /// <summary>Override önce güncellenir (tile anında doğru), ardından registry'ye yazım denenir.</summary>
    internal void SetEnabled(bool enabled)
    {
        _override = enabled;
        _lastKnown = enabled;
        TryWriteRegistry(enabled);
    }

    /// <summary>Registry'yi yeniden okur; dışarıdan değişiklik okunabilir ve override'dan
    /// farklıysa override düşer (işletim sistemi kazanır).</summary>
    internal void PollRegistry()
    {
        byte[]? data = ReadData();
        if (!NightLightBlob.TryParse(data, out bool enabled))
        {
            return;
        }

        _lastKnown = enabled;
        if (_override is bool ov && ov != enabled)
        {
            _override = null;
        }
    }

    private static byte[]? ReadData()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(StateKeyPath);
            return key?.GetValue("Data") as byte[];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Night light read failed — {ex.Message}");
            return null;
        }
    }

    private static void TryWriteRegistry(bool enabled)
    {
        try
        {
            byte[]? data = ReadData();
            if (data is null || !NightLightBlob.TryParse(data, out bool current))
            {
                Debug.WriteLine("WharfBar: Night light write skipped — blob not recognised");
                return;
            }

            if (current == enabled)
            {
                return;
            }

            using RegistryKey key = Registry.CurrentUser.CreateSubKey(StateKeyPath);
            key.SetValue("Data", NightLightBlob.ToggleFlag(data, enabled), RegistryValueKind.Binary);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Night light write failed — {ex.Message}");
        }
    }
}
```

- [ ] **Step 2: Derle**

Run: `dotnet build WharfBar.sln`
Expected: BUILD SUCCESS.

---

### Task 3: Yansıtma — DisplayTopology + ProjectionService

**Files:**
- Create: `src/WharfBar/Native/DisplayTopology.cs`
- Create: `src/WharfBar/Services/ProjectionService.cs`
- Test: `tests/WharfBar.Tests/ProjectionMappingTests.cs`

**Interfaces:**
- Consumes: yok.
- Produces:
  - `internal static class DisplayTopology` — const'lar `SDC_TOPOLOGY_INTERNAL/CLONE/EXTEND/EXTERNAL`, `bool TryGetCurrentTopology(out uint topologyId)`, `bool TryApplyTopology(uint flags)`.
  - `internal enum ProjectionMode { InternalOnly, Clone, Extend, ExternalOnly }`
  - `internal sealed class ProjectionService` — `ProjectionMode? Current { get; }`, `void Refresh()`, `bool TryApply(ProjectionMode mode)`, `static ProjectionMode? FromTopologyId(uint id)`, `static uint SdcFlagFor(ProjectionMode mode)`.

- [ ] **Step 1: Testi yaz**

```csharp
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ProjectionMappingTests
{
    [Theory]
    [InlineData(1, ProjectionMode.InternalOnly)]
    [InlineData(2, ProjectionMode.Clone)]
    [InlineData(3, ProjectionMode.Extend)]
    [InlineData(4, ProjectionMode.ExternalOnly)]
    public void FromTopologyId_MapsKnownIds(uint id, ProjectionMode expected)
    {
        Assert.Equal(expected, ProjectionService.FromTopologyId(id));
    }

    [Fact]
    public void FromTopologyId_UnknownId_IsNull()
    {
        Assert.Null(ProjectionService.FromTopologyId(0));
        Assert.Null(ProjectionService.FromTopologyId(99));
    }

    [Fact]
    public void SdcFlagFor_MapsModes()
    {
        Assert.Equal(DisplayTopology.SDC_TOPOLOGY_INTERNAL, ProjectionService.SdcFlagFor(ProjectionMode.InternalOnly));
        Assert.Equal(DisplayTopology.SDC_TOPOLOGY_CLONE, ProjectionService.SdcFlagFor(ProjectionMode.Clone));
        Assert.Equal(DisplayTopology.SDC_TOPOLOGY_EXTEND, ProjectionService.SdcFlagFor(ProjectionMode.Extend));
        Assert.Equal(DisplayTopology.SDC_TOPOLOGY_EXTERNAL, ProjectionService.SdcFlagFor(ProjectionMode.ExternalOnly));
    }
}
```

- [ ] **Step 2: Testi çalıştır, başarısız olduğunu gör**

Run: `dotnet test tests/WharfBar.Tests --filter "ProjectionMappingTests"`
Expected: FAIL — türler yok.

- [ ] **Step 3: Native wrapper**

```csharp
using System.Runtime.InteropServices;

namespace WharfBar.Native;

internal static class DisplayTopology
{
    internal const uint SDC_TOPOLOGY_INTERNAL = 0x1;
    internal const uint SDC_TOPOLOGY_CLONE = 0x2;
    internal const uint SDC_TOPOLOGY_EXTEND = 0x4;
    internal const uint SDC_TOPOLOGY_EXTERNAL = 0x8;
    internal const uint SDC_APPLY = 0x80;
    internal const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
    internal const uint SDC_ALLOW_CHANGES = 0x400;
    internal const uint QDC_DATABASE_CURRENT = 0x2;
    private const int ERROR_SUCCESS = 0;

    /// <summary>Mevcut topoloji ID'sini sıfır buffer ile okur (belgelenmiş kullanım).</summary>
    internal static bool TryGetCurrentTopology(out uint topologyId)
    {
        topologyId = 0;
        return QueryDisplayConfig(
            QDC_DATABASE_CURRENT,
            0, IntPtr.Zero,
            0, IntPtr.Zero,
            ref topologyId) == ERROR_SUCCESS;
    }

    internal static bool TryApplyTopology(uint flags) =>
        SetDisplayConfig(
            0, IntPtr.Zero,
            0, IntPtr.Zero,
            flags | SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES) == ERROR_SUCCESS;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int QueryDisplayConfig(
        uint flags,
        int numPathArrayElements,
        IntPtr pathArray,
        int numModeInfoArrayElements,
        IntPtr modeInfoArray,
        ref uint currentTopologyId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetDisplayConfig(
        int numPathArrayElements,
        IntPtr pathArray,
        int numModeInfoArrayElements,
        IntPtr modeInfoArray,
        uint flags);
}
```

- [ ] **Step 4: ProjectionService**

```csharp
using System.Diagnostics;
using WharfBar.Native;

namespace WharfBar.Services;

internal enum ProjectionMode
{
    InternalOnly,
    Clone,
    Extend,
    ExternalOnly,
}

internal sealed class ProjectionService
{
    internal ProjectionMode? Current { get; private set; }

    /// <summary>DISPLAYCONFIG_TOPOLOGY_ID 1..4 aynı sırada ProjectionMode'a karşılık gelir.</summary>
    internal static ProjectionMode? FromTopologyId(uint id) => id switch
    {
        1 => ProjectionMode.InternalOnly,
        2 => ProjectionMode.Clone,
        3 => ProjectionMode.Extend,
        4 => ProjectionMode.ExternalOnly,
        _ => null,
    };

    internal static uint SdcFlagFor(ProjectionMode mode) => mode switch
    {
        ProjectionMode.Clone => DisplayTopology.SDC_TOPOLOGY_CLONE,
        ProjectionMode.Extend => DisplayTopology.SDC_TOPOLOGY_EXTEND,
        ProjectionMode.ExternalOnly => DisplayTopology.SDC_TOPOLOGY_EXTERNAL,
        _ => DisplayTopology.SDC_TOPOLOGY_INTERNAL,
    };

    internal void Refresh()
    {
        Current = DisplayTopology.TryGetCurrentTopology(out uint id)
            ? FromTopologyId(id)
            : null;
    }

    internal bool TryApply(ProjectionMode mode)
    {
        bool ok = DisplayTopology.TryApplyTopology(SdcFlagFor(mode));
        if (ok)
        {
            Current = mode;
        }
        else
        {
            Debug.WriteLine($"WharfBar: SetDisplayConfig failed for {mode}");
        }

        return ok;
    }
}
```

- [ ] **Step 5: Testi çalıştır, geçtiğini gör**

Run: `dotnet test tests/WharfBar.Tests --filter "ProjectionMappingTests"`
Expected: PASS.

---

### Task 4: WlanApi native sarmalayıcı

**Files:**
- Create: `src/WharfBar/Native/WlanApi.cs`

**Interfaces:**
- Consumes: yok.
- Produces: `internal static class WlanApi` —
  - const'lar: `ERROR_SUCCESS`, `WLAN_AVAILABLE_NETWORK_CONNECTED = 1`, `WLAN_AVAILABLE_NETWORK_HAS_PROFILE = 2`, `WLAN_INTERFACE_STATE_CONNECTED = 4`.
  - `internal readonly record struct RawNetwork(byte[] Ssid, int Signal, bool Secured, bool HasProfile, bool Connected);`
  - `bool TryOpen(out IntPtr handle)`, `void Close(IntPtr handle)`, `bool TryGetInterfaceGuid(IntPtr handle, out Guid guid)`, `bool TryGetNetworks(IntPtr handle, Guid guid, out List<RawNetwork> networks)`, `bool TrySetProfile(IntPtr handle, Guid guid, string xml, out uint reason)`, `bool TryConnectProfile(IntPtr handle, Guid guid, string profileName)`, `bool TryGetCurrentSsid(IntPtr handle, Guid guid, out byte[] ssid)`.

- [ ] **Step 1: Struct'lar, DllImport'lar ve sarmalayıcı metodlar**

```csharp
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

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WLAN_AVAILABLE_NETWORK
{
    public uint wlanProfileFlags;
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
    public uint dot11AuthAlgorithm;
    public uint dot11CipherAlgorithm;
    public uint bDot11Rssi;
    public int wlanRssi;
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
    internal const uint WLAN_INTERFACE_STATE_CONNECTED = 4;
    private const uint wlan_connection_mode_profile = 1;
    private const uint dot11_BSS_type_infrastructure = 2;
    private const uint wlan_intf_opcode_current_connection = 7;
    private const int InterfaceListHeaderBytes = 8;

    internal readonly record struct RawNetwork(byte[] Ssid, int Signal, bool Secured, bool HasProfile, bool Connected);

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
    private static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint OpCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

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

            guid = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(listPtr + InterfaceListHeaderBytes).InterfaceGuid;
            return true;
        }
        finally
        {
            WlanFreeMemory(listPtr);
        }
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
                networks.Add(new RawNetwork(
                    n.dot11Ssid.ucSSID,
                    (int)n.wlanSignalQuality,
                    n.bSecurityEnabled != 0,
                    (n.wlanProfileFlags & WLAN_AVAILABLE_NETWORK_HAS_PROFILE) != 0,
                    (n.wlanProfileFlags & WLAN_AVAILABLE_NETWORK_CONNECTED) != 0));
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

            ssid = attrs.wlanAssociationAttributes.dot11Ssid.ucSSID;
            return true;
        }
        finally
        {
            WlanFreeMemory(data);
        }
    }
}
```

- [ ] **Step 2: Derle**

Run: `dotnet build WharfBar.sln`
Expected: BUILD SUCCESS.

---

### Task 5: WifiService

**Files:**
- Create: `src/WharfBar/Services/WifiService.cs`
- Test: `tests/WharfBar.Tests/WifiServiceTests.cs`

**Interfaces:**
- Consumes: `WlanApi` (Task 4) — `RawNetwork`, `TryOpen`, `TryGetInterfaceGuid`, `TryGetNetworks`, `TrySetProfile`, `TryConnectProfile`, `Close`.
- Produces: `internal sealed record WifiNetworkInfo(string Ssid, int Signal, bool IsSecured, bool IsKnown, bool IsConnected, string? ProfileName);` ve `internal sealed class WifiService` —
  - `bool IsAvailable { get; }`, `IReadOnlyList<WifiNetworkInfo> Networks { get; }`, `event EventHandler? Changed`, `void StartPolling()`, `void StopPolling()`, `(bool Ok, string? Error) TryConnect(WifiNetworkInfo network, string? password)`, `static string SsidToString(byte[] ssid)`, `static int SignalBars(int signal)`.

- [ ] **Step 1: Testi yaz**

```csharp
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WifiServiceTests
{
    [Theory]
    [InlineData(100, 4)]
    [InlineData(75, 4)]
    [InlineData(74, 3)]
    [InlineData(50, 3)]
    [InlineData(49, 2)]
    [InlineData(25, 2)]
    [InlineData(24, 1)]
    [InlineData(0, 1)]
    public void SignalBars_MapsQualityToBars(int signal, int expected)
    {
        Assert.Equal(expected, WifiService.SignalBars(signal));
    }

    [Fact]
    public void SsidToString_TrimsTrailingNulls()
    {
        var ssid = new byte[32];
        Array.Copy("MyNet"u8.ToArray(), ssid, 5);
        Assert.Equal("MyNet", WifiService.SsidToString(ssid));
    }

    [Fact]
    public void SsidToString_EmptyOrAllNulls_ReturnsUnknown()
    {
        Assert.Equal("(Bilinmeyen ağ)", WifiService.SsidToString(Array.Empty<byte>()));
        Assert.Equal("(Bilinmeyen ağ)", WifiService.SsidToString(new byte[32]));
    }
}
```

- [ ] **Step 2: Testi çalıştır, başarısız olduğunu gör**

Run: `dotnet test tests/WharfBar.Tests --filter "WifiServiceTests"`
Expected: FAIL — tür yok.

- [ ] **Step 3: Servisi yaz**

```csharp
using System.Diagnostics;
using System.Security;
using System.Text;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed record WifiNetworkInfo(string Ssid, int Signal, bool IsSecured, bool IsKnown, bool IsConnected, string? ProfileName);

/// <summary>Wlan API üzerinden ağ listesi ve bağlanma. Sadece Wifi sayfası açıkken poll edilir.</summary>
internal sealed class WifiService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _timer;
    private bool _disposed;

    internal WifiService()
    {
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => Refresh();
    }

    internal bool IsAvailable { get; private set; }

    internal IReadOnlyList<WifiNetworkInfo> Networks { get; private set; } = [];

    internal event EventHandler? Changed;

    internal void StartPolling()
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        _timer.Start();
    }

    internal void StopPolling() => _timer.Stop();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }

    internal (bool Ok, string? Error) TryConnect(WifiNetworkInfo network, string? password)
    {
        if (!WlanApi.TryOpen(out IntPtr handle))
        {
            return (false, "Wi-Fi hizmeti açılamadı");
        }

        try
        {
            if (!WlanApi.TryGetInterfaceGuid(handle, out Guid guid))
            {
                return (false, "Adaptör bulunamadı");
            }

            string profileName;
            if (network.IsKnown && !string.IsNullOrEmpty(network.ProfileName))
            {
                profileName = network.ProfileName;
            }
            else
            {
                string xml = BuildProfileXml(network.Ssid, network.IsSecured ? password : null);
                if (!WlanApi.TrySetProfile(handle, guid, xml, out uint reason))
                {
                    return (false, $"Profil oluşturulamadı (hata {reason})");
                }

                profileName = network.Ssid;
            }

            return WlanApi.TryConnectProfile(handle, guid, profileName)
                ? (true, null)
                : (false, "Bağlanılamadı");
        }
        finally
        {
            WlanApi.Close(handle);
        }
    }

    internal static string SsidToString(byte[] ssid)
    {
        int len = 0;
        while (len < ssid.Length && ssid[len] != 0 && len < 32)
        {
            len++;
        }

        if (len == 0)
        {
            return "(Bilinmeyen ağ)";
        }

        byte[] slice = ssid[..len];
        string utf8 = Encoding.UTF8.GetString(slice);
        return utf8.Contains('\uFFFD') ? Encoding.Latin1.GetString(slice) : utf8;
    }

    internal static int SignalBars(int signal) => signal switch
    {
        >= 75 => 4,
        >= 50 => 3,
        >= 25 => 2,
        _ => 1,
    };

    private static string BuildProfileXml(string ssid, string? password)
    {
        string name = SecurityElement.Escape(ssid);
        string auth = password is null ? "open" : "WPA2PSK";
        string enc = password is null ? "none" : "AES";
        string sharedKey = password is null
            ? string.Empty
            : $"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(password)}</keyMaterial></sharedKey>";

        return $"""<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{name}</name>
  <SSIDConfig><SSID><name>{name}</name></SSID></SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM><security>
    <authEncryption><authentication>{auth}</authentication><encryption>{enc}</encryption><useOneX>false</useOneX></authEncryption>
    {sharedKey}
  </security></MSM>
</WLANProfile>""";
    }

    private void Refresh()
    {
        if (!WlanApi.TryOpen(out IntPtr handle))
        {
            IsAvailable = false;
            Networks = [];
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        try
        {
            if (!WlanApi.TryGetInterfaceGuid(handle, out Guid guid))
            {
                IsAvailable = false;
                Networks = [];
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            IsAvailable = true;
            if (!WlanApi.TryGetNetworks(handle, guid, out List<WlanApi.RawNetwork> raw))
            {
                Networks = [];
                Changed?.Invoke(this, EventArgs.Empty);
                return;
            }

            Networks = raw
                .Select(n => new WifiNetworkInfo(
                    SsidToString(n.Ssid),
                    n.Signal,
                    n.Secured,
                    n.HasProfile,
                    n.Connected,
                    n.HasProfile ? SsidToString(n.Ssid) : null))
                .ToList();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Wi-Fi refresh failed — {ex.Message}");
        }
        finally
        {
            WlanApi.Close(handle);
        }
    }
}
```

- [ ] **Step 4: Testi çalıştır, geçtiğini gör**

Run: `dotnet test tests/WharfBar.Tests --filter "WifiServiceTests"`
Expected: PASS.

- [ ] **Step 5: Derle**

Run: `dotnet build WharfBar.sln`
Expected: BUILD SUCCESS.

---

### Task 6: BluetoothService

**Files:**
- Create: `src/WharfBar/Services/BluetoothService.cs`

**Interfaces:**
- Consumes: WinRT `Windows.Devices.Bluetooth` / `Windows.Devices.Enumeration` (hedef TFM ile hazır).
- Produces: `internal sealed record BluetoothDeviceInfo(string Id, string Name, bool IsConnected);` ve `internal sealed class BluetoothService` — `IReadOnlyList<BluetoothDeviceInfo> Devices { get; }`, `event EventHandler? Changed`, `void StartPolling()`, `void StopPolling()`, `IDisposable`.

- [ ] **Step 1: Servisi yaz**

```csharp
using System.Diagnostics;
using System.Windows.Threading;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace WharfBar.Services;

internal sealed record BluetoothDeviceInfo(string Id, string Name, bool IsConnected);

/// <summary>Eşleşmiş Bluetooth cihazlarını listeler; bağlı olanlar işaretlenir.</summary>
internal sealed class BluetoothService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _timer;
    private bool _disposed;

    internal BluetoothService()
    {
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => Refresh();
    }

    internal IReadOnlyList<BluetoothDeviceInfo> Devices { get; private set; } = [];

    internal event EventHandler? Changed;

    internal void StartPolling()
    {
        if (_disposed)
        {
            return;
        }

        Refresh();
        _timer.Start();
    }

    internal void StopPolling() => _timer.Stop();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }

    private async void Refresh()
    {
        try
        {
            DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelector());
            var result = new List<BluetoothDeviceInfo>();
            foreach (DeviceInformation device in devices)
            {
                if (!device.Pairing.IsPaired)
                {
                    continue;
                }

                bool connected = await TryGetConnectedAsync(device.Id);
                result.Add(new BluetoothDeviceInfo(
                    device.Id,
                    string.IsNullOrWhiteSpace(device.Name) ? "(Adsız cihaz)" : device.Name,
                    connected));
            }

            Devices = result;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Bluetooth refresh failed — {ex.Message}");
        }
    }

    private static async Task<bool> TryGetConnectedAsync(string deviceId)
    {
        try
        {
            BluetoothDevice? device = await BluetoothDevice.FromIdAsync(deviceId);
            return device is not null && device.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: BT status failed — {ex.Message}");
            return false;
        }
    }
}
```

- [ ] **Step 2: Derle**

Run: `dotnet build WharfBar.sln`
Expected: BUILD SUCCESS.

---

### Task 7: Servis kablolaması — QuickSettingsService + TopBarWindow

**Files:**
- Modify: `src/WharfBar/Services/QuickSettingsService.cs`
- Modify: `src/WharfBar/Views/TopBarWindow.xaml.cs`

**Interfaces:**
- Consumes: `NightLightService` (Task 2), `WifiService` (Task 5), `BluetoothService` (Task 6), `ProjectionService` (Task 3).
- Produces: `QuickSettingsService` ctor'u `internal QuickSettingsService(NightLightService nightLight)`; erişimciler `internal bool IsWifiRadioOn => _wifiOn;` ve `internal bool IsBluetoothRadioOn => _bluetoothOn;`; `ToggleTileAsync`'te `nightlight` gerçek toggle yapar, `project` Win+P göndermez. Flyout `Attach` imzası Task 8'de genişletilir.

- [ ] **Step 1: QuickSettingsService güncelle**

`src/WharfBar/Services/QuickSettingsService.cs`:

```csharp
    private readonly NightLightService _nightLight;
    private bool? _airplaneSessionOverride;

    internal QuickSettingsService(NightLightService nightLight)
    {
        _nightLight = nightLight;
        Network = ReadNetworkDetails();
        Tiles = BuildTiles();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => OnPollTick();
        _ = RefreshRadiosAsync();
    }

    internal bool IsWifiRadioOn => _wifiOn;

    internal bool IsBluetoothRadioOn => _bluetoothOn;
```

`Refresh()` içinde `BuildTiles()` öncesi:

```csharp
    private void Refresh()
    {
        _nightLight.PollRegistry();
        NetworkDetails nextNet = ReadNetworkDetails();
        IReadOnlyList<QuickTileState> nextTiles = BuildTiles();
        Network = nextNet;
        Tiles = nextTiles;
        Changed?.Invoke(this, EventArgs.Empty);
    }
```

`ToggleTileAsync` switch'i — `project` case'ini çıkar, `nightlight`'ı değiştir:

```csharp
            case "nightlight":
                _nightLight.SetEnabled(!_nightLight.IsEnabled);
                break;
```

`BuildTiles()` gece ışığı satırı:

```csharp
            new QuickTileState(
                "nightlight",
                "Gece ışığı",
                "\uE708",
                IsEnabled: true,
                IsActive: _nightLight.IsEnabled,
                HasChevron: false,
                Tooltip: "Gece ışığı"),
```

- [ ] **Step 2: TopBarWindow güncelle**

`src/WharfBar/Views/TopBarWindow.xaml.cs` — sınıf alanları:

```csharp
    private readonly NightLightService _nightLight;
    private readonly WifiService _wifi;
    private readonly BluetoothService _bluetooth;
    private readonly ProjectionService _projection;
```

Ctor'da mevcut satırların yanına (mevcut `_mixer = new AudioMixerService(_volume); _quick = new QuickSettingsService();` bloğu değiştirilir):

```csharp
        _mixer = new AudioMixerService(_volume);
        _nightLight = new NightLightService();
        _wifi = new WifiService();
        _bluetooth = new BluetoothService();
        _projection = new ProjectionService();
        _quick = new QuickSettingsService(_nightLight);
        QuickSettingsHost.Attach(_volume, _mixer, _quick, _wifi, _bluetooth, _projection);
```

Kapanışta (mevcut `QuickSettingsHost.Detach()` çağrısının olduğu `OnClosing`/cleanup bloğuna) yeni servisleri dispose et:

```csharp
        QuickSettingsHost.Detach();
        _volume.Dispose();
        _mixer.Dispose();
        _quick.Dispose();
        _wifi.Dispose();
        _bluetooth.Dispose();
```

> Not: `_nightLight`, `_projection` IDisposable değil; dispose listesine gerek yok. `_quick.Dispose()` mevcut kodda yoksa yalnızca yeni eklenenleri ekle; mevcut temizlik akışını bozma.

- [ ] **Step 3: Derle**

Run: `dotnet build WharfBar.sln`
Expected: BUILD FAILS yalnızca `Attach` imza uyuşmazlığı yüzünden (`QuickSettingsFlyout.Attach` henüz 6 parametre almıyor). Bu beklenen; Task 8'de düzeltilir. (Kesin doğrulama Task 8 sonunda.)

---

### Task 8: Flyout sayfaları + navigasyon

**Files:**
- Modify: `src/WharfBar/Views/QuickSettingsFlyout.xaml`
- Modify: `src/WharfBar/Views/QuickSettingsFlyout.xaml.cs`
- Modify: `src/WharfBar/Services/QuickSettingsService.cs` (`QuickSettingsPage` enum)

**Interfaces:**
- Consumes: `QuickSettingsPage.Wifi/Bluetooth/Project` (bu task'ta eklenir), `WifiService`, `BluetoothService`, `ProjectionService`, `QuickSettingsService.IsWifiRadioOn/IsBluetoothRadioOn`.
- Produces: `QuickSettingsFlyout.Attach(VolumeService, AudioMixerService, QuickSettingsService, WifiService, BluetoothService, ProjectionService)`; `ShowPage` yeni sayfalarda wifi/bt poll başlat/durdur ve projection refresh; chevron/tile yönlendirme; Wifi/BT/Project sayfalarının XAML + code-behind listeleri.

- [ ] **Step 1: Enum güncelle**

`src/WharfBar/Services/QuickSettingsService.cs`:

```csharp
internal enum QuickSettingsPage
{
    Main,
    Mixer,
    Network,
    Wifi,
    Bluetooth,
    Project,
}
```

- [ ] **Step 2: XAML — üç yeni sayfa**

`src/WharfBar/Views/QuickSettingsFlyout.xaml`, `NetworkPage`'in kapandığı `</Grid>`'in hemen öncesine üç `Grid` ekle (mevcut `<!-- NETWORK -->` bloğunun sonundaki `</Grid>`'den sonra, ana kapanış `</Grid>`'den önce):

```xml
            <!-- WIFI -->
            <Grid x:Name="WifiPage" Visibility="Collapsed">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <DockPanel Grid.Row="0" Margin="0,0,0,12">
                    <Button Style="{StaticResource QsIconButton}"
                            Click="OnBackToMainClick"
                            ToolTip="Geri">
                        <TextBlock Style="{StaticResource QsGlyph}" Text="&#xE76B;" />
                    </Button>
                    <TextBlock Margin="8,0,0,0"
                               VerticalAlignment="Center"
                               FontWeight="SemiBold"
                               FontSize="14"
                               Text="Wi-Fi" />
                </DockPanel>

                <Border Grid.Row="1"
                        Background="{DynamicResource ControlFillColorDefaultBrush}"
                        CornerRadius="8"
                        Padding="14,12"
                        Margin="0,0,0,10">
                    <DockPanel>
                        <TextBlock x:Name="WifiGlyphLarge"
                                   FontFamily="{DynamicResource AppIconFontFamily}"
                                   FontSize="20"
                                   VerticalAlignment="Center"
                                   Text="&#xE701;" />
                        <StackPanel Margin="12,0,0,0" VerticalAlignment="Center">
                            <TextBlock x:Name="WifiSummaryTitle"
                                       FontWeight="SemiBold"
                                       FontSize="14"
                                       Text="Wi-Fi" />
                            <TextBlock x:Name="WifiSummarySubtitle"
                                       FontSize="12"
                                       Opacity="0.75"
                                       TextWrapping="Wrap"
                                       Text="—" />
                        </StackPanel>
                    </DockPanel>
                </Border>

                <ScrollViewer Grid.Row="2"
                              MaxHeight="260"
                              VerticalScrollBarVisibility="Auto"
                              HorizontalScrollBarVisibility="Disabled">
                    <StackPanel x:Name="WifiNetworkList" />
                </ScrollViewer>

                <Border x:Name="WifiPasswordPanel"
                        Grid.Row="3"
                        Margin="0,10,0,0"
                        Background="{DynamicResource ControlFillColorDefaultBrush}"
                        BorderBrush="{DynamicResource ControlStrokeColorDefaultBrush}"
                        BorderThickness="1"
                        CornerRadius="8"
                        Padding="12"
                        Visibility="Collapsed">
                    <StackPanel>
                        <TextBlock x:Name="WifiPasswordSsid"
                                   FontSize="12"
                                   FontWeight="SemiBold"
                                   Margin="0,0,0,8" />
                        <PasswordBox x:Name="WifiPasswordBox" Margin="0,0,0,8" />
                        <DockPanel>
                            <Button x:Name="WifiConnectButton"
                                    DockPanel.Dock="Right"
                                    Content="Bağlan"
                                    Padding="12,6"
                                    Click="OnWifiConnectClick" />
                            <Button x:Name="WifiCancelButton"
                                    DockPanel.Dock="Right"
                                    Content="İptal"
                                    Margin="0,0,8,0"
                                    Padding="12,6"
                                    Click="OnWifiCancelClick" />
                        </DockPanel>
                        <TextBlock x:Name="WifiErrorText"
                                   FontSize="11"
                                   Opacity="0.8"
                                   TextWrapping="Wrap" />
                    </StackPanel>
                </Border>

                <StackPanel Grid.Row="4" Margin="0,12,0,0">
                    <Button Content="Ağ ayarlarını aç"
                            Margin="0,0,0,8"
                            Padding="12,8"
                            HorizontalContentAlignment="Left"
                            Click="OnOpenNetworkSettingsClick" />
                    <Button Content="Wi-Fi ayarları"
                            Padding="12,8"
                            HorizontalContentAlignment="Left"
                            Click="OnOpenWifiSettingsClick" />
                </StackPanel>
            </Grid>

            <!-- BLUETOOTH -->
            <Grid x:Name="BluetoothPage" Visibility="Collapsed">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <DockPanel Grid.Row="0" Margin="0,0,0,12">
                    <Button Style="{StaticResource QsIconButton}"
                            Click="OnBackToMainClick"
                            ToolTip="Geri">
                        <TextBlock Style="{StaticResource QsGlyph}" Text="&#xE76B;" />
                    </Button>
                    <TextBlock Margin="8,0,0,0"
                               VerticalAlignment="Center"
                               FontWeight="SemiBold"
                               FontSize="14"
                               Text="Bluetooth" />
                </DockPanel>

                <Border Grid.Row="1"
                        Background="{DynamicResource ControlFillColorDefaultBrush}"
                        CornerRadius="8"
                        Padding="14,12"
                        Margin="0,0,0,10">
                    <DockPanel>
                        <TextBlock x:Name="BtGlyphLarge"
                                   FontFamily="{DynamicResource AppIconFontFamily}"
                                   FontSize="20"
                                   VerticalAlignment="Center"
                                   Text="&#xE702;" />
                        <StackPanel Margin="12,0,0,0" VerticalAlignment="Center">
                            <TextBlock x:Name="BtSummaryTitle"
                                       FontWeight="SemiBold"
                                       FontSize="14"
                                       Text="Bluetooth" />
                            <TextBlock x:Name="BtSummarySubtitle"
                                       FontSize="12"
                                       Opacity="0.75"
                                       TextWrapping="Wrap"
                                       Text="—" />
                        </StackPanel>
                    </DockPanel>
                </Border>

                <ScrollViewer Grid.Row="2"
                              MaxHeight="260"
                              VerticalScrollBarVisibility="Auto"
                              HorizontalScrollBarVisibility="Disabled">
                    <StackPanel x:Name="BluetoothDeviceList" />
                </ScrollViewer>

                <Button Grid.Row="3"
                        Content="Bluetooth ayarlarını aç"
                        Margin="0,12,0,0"
                        Padding="12,8"
                        HorizontalContentAlignment="Left"
                        Click="OnOpenBluetoothSettingsClick" />
            </Grid>

            <!-- PROJECT -->
            <Grid x:Name="ProjectPage" Visibility="Collapsed">
                <Grid.RowDefinitions>
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                    <RowDefinition Height="Auto" />
                </Grid.RowDefinitions>

                <DockPanel Grid.Row="0" Margin="0,0,0,12">
                    <Button Style="{StaticResource QsIconButton}"
                            Click="OnBackToMainClick"
                            ToolTip="Geri">
                        <TextBlock Style="{StaticResource QsGlyph}" Text="&#xE76B;" />
                    </Button>
                    <TextBlock Margin="8,0,0,0"
                               VerticalAlignment="Center"
                               FontWeight="SemiBold"
                               FontSize="14"
                               Text="Yansıt" />
                </DockPanel>

                <StackPanel Grid.Row="1" x:Name="ProjectModeList" />

                <Button Grid.Row="2"
                        Content="Ekran ayarları"
                        Margin="0,12,0,0"
                        Padding="12,8"
                        HorizontalContentAlignment="Left"
                        Click="OnOpenDisplaySettingsClick" />
            </Grid>
```

- [ ] **Step 3: Code-behind — alanlar + Attach**

`src/WharfBar/Views/QuickSettingsFlyout.xaml.cs`:

```csharp
    private WifiService? _wifi;
    private BluetoothService? _bluetooth;
    private ProjectionService? _projection;
    private WifiNetworkInfo? _pendingWifiConnect;
```

`Attach` imzasını genişlet ve event bağla / `Detach`'te sök:

```csharp
    internal void Attach(VolumeService volume, AudioMixerService mixer, QuickSettingsService quick,
        WifiService wifi, BluetoothService bluetooth, ProjectionService projection)
    {
        Detach();
        _volume = volume;
        _mixer = mixer;
        _quick = quick;
        _wifi = wifi;
        _bluetooth = bluetooth;
        _projection = projection;
        _volume.Changed += OnVolumeChanged;
        _mixer.Changed += OnMixerChanged;
        _quick.Changed += OnQuickChanged;
        _wifi.Changed += OnWifiChanged;
        _bluetooth.Changed += OnBluetoothChanged;
    }

    internal void Detach()
    {
        if (_volume is not null)
        {
            _volume.Changed -= OnVolumeChanged;
        }

        if (_mixer is not null)
        {
            _mixer.Changed -= OnMixerChanged;
        }

        if (_quick is not null)
        {
            _quick.Changed -= OnQuickChanged;
        }

        if (_wifi is not null)
        {
            _wifi.Changed -= OnWifiChanged;
        }

        if (_bluetooth is not null)
        {
            _bluetooth.Changed -= OnBluetoothChanged;
        }

        _volume = null;
        _mixer = null;
        _quick = null;
        _wifi = null;
        _bluetooth = null;
        _projection = null;
    }
```

- [ ] **Step 4: Code-behind — ShowPage + event handler'lar**

`ShowPage`'un görünürlük satırlarını genişlet:

```csharp
        MainPage.Visibility = page == QuickSettingsPage.Main ? Visibility.Visible : Visibility.Collapsed;
        MixerPage.Visibility = page == QuickSettingsPage.Mixer ? Visibility.Visible : Visibility.Collapsed;
        NetworkPage.Visibility = page == QuickSettingsPage.Network ? Visibility.Visible : Visibility.Collapsed;
        WifiPage.Visibility = page == QuickSettingsPage.Wifi ? Visibility.Visible : Visibility.Collapsed;
        BluetoothPage.Visibility = page == QuickSettingsPage.Bluetooth ? Visibility.Visible : Visibility.Collapsed;
        ProjectPage.Visibility = page == QuickSettingsPage.Project ? Visibility.Visible : Visibility.Collapsed;
```

`ShowPage`'un mixer poll bloğundan sonra:

```csharp
        if (page == QuickSettingsPage.Wifi)
        {
            _wifi?.StartPolling();
        }
        else
        {
            _wifi?.StopPolling();
            WifiPasswordPanel.Visibility = Visibility.Collapsed;
        }

        if (page == QuickSettingsPage.Bluetooth)
        {
            _bluetooth?.StartPolling();
        }
        else
        {
            _bluetooth?.StopPolling();
        }

        if (page == QuickSettingsPage.Project)
        {
            RefreshProjectPage();
        }
```

Yeni event handler'lar:

```csharp
    private void OnWifiChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshWifiPage);

    private void OnBluetoothChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(RefreshBluetoothPage);
```

- [ ] **Step 5: Code-behind — chevron ve tile yönlendirme**

`OnTileChevronClick`:

```csharp
        e.Handled = true;
        if (id is "wifi")
        {
            ShowPage(QuickSettingsPage.Wifi);
            return;
        }

        if (id == "bluetooth")
        {
            ShowPage(QuickSettingsPage.Bluetooth);
            return;
        }

        if (id == "project")
        {
            ShowPage(QuickSettingsPage.Project);
        }
```

`OnTileClick`'in başına (toggle'dan önce):

```csharp
        e.Handled = true;
        if (id == "project")
        {
            ShowPage(QuickSettingsPage.Project);
            return;
        }

        await _quick.ToggleTileAsync(id);
        RefreshQuick();
```

- [ ] **Step 6: Code-behind — Wi-Fi sayfası listeleri**

```csharp
    private void RefreshWifiPage()
    {
        if (_wifi is null || _page != QuickSettingsPage.Wifi)
        {
            return;
        }

        if (_quick is not null)
        {
            bool radioOn = _quick.IsWifiRadioOn;
            WifiSummarySubtitle.Text = !_wifi.IsAvailable
                ? "Adaptör bulunamadı"
                : !radioOn
                    ? "Wi-Fi kapalı"
                    : _wifi.Networks.FirstOrDefault(n => n.IsConnected)?.Ssid ?? "Bağlı değil";
        }

        WifiNetworkList.Children.Clear();
        foreach (WifiNetworkInfo network in _wifi.Networks)
        {
            WifiNetworkList.Children.Add(BuildWifiRow(network));
        }

        if (WifiNetworkList.Children.Count == 0 && _wifi.IsAvailable)
        {
            WifiNetworkList.Children.Add(new TextBlock
            {
                Text = "Ağ bulunamadı",
                Opacity = 0.55,
                FontSize = 12,
                Margin = new Thickness(4, 4, 0, 0),
            });
        }
    }

    private UIElement BuildWifiRow(WifiNetworkInfo network)
    {
        int bars = WifiService.SignalBars(network.Signal);
        string signalGlyph = bars switch
        {
            4 => "\uE854",
            3 => "\uE853",
            2 => "\uE852",
            _ => "\uE851",
        };

        var border = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 6),
            Background = network.IsConnected
                ? TryFindResource("AccentFillColorDefaultBrush") as Brush
                    ?? TryFindResource("ControlFillColorDefaultBrush") as Brush
                : TryFindResource("ControlFillColorDefaultBrush") as Brush,
            Tag = network,
        };

        var dock = new DockPanel();

        var signal = new TextBlock
        {
            Text = signalGlyph,
            FontFamily = SystemVersion.IconFontFamily,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(signal, Dock.Right);

        var lockIcon = network.IsSecured ? "\uE72E" : "\uE730";
        var name = new TextBlock
        {
            Text = network.Ssid,
            FontSize = 13,
            FontWeight = network.IsConnected ? FontWeights.SemiBold : FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var security = new TextBlock
        {
            Text = lockIcon,
            FontFamily = SystemVersion.IconFontFamily,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Opacity = 0.75,
        };
        DockPanel.SetDock(security, Dock.Left);
        if (!network.IsSecured)
        {
            security.Visibility = Visibility.Collapsed;
        }

        var badge = network.IsConnected
            ? "Bağlı"
            : network.IsKnown
                ? "Kayıtlı"
                : string.Empty;
        if (badge.Length > 0)
        {
            var badgeText = new TextBlock
            {
                Text = badge,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Opacity = 0.7,
            };
            dock.Children.Add(badgeText);
        }

        dock.Children.Add(signal);
        dock.Children.Add(security);
        dock.Children.Add(name);
        border.Child = dock;
        border.MouseLeftButtonUp += OnWifiRowClick;
        return border;
    }

    private void OnWifiRowClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WifiNetworkInfo network } || _wifi is null)
        {
            return;
        }

        e.Handled = true;
        if (network.IsConnected)
        {
            return;
        }

        if (network.IsKnown || !network.IsSecured)
        {
            (bool ok, string? error) = _wifi.TryConnect(network, password: null);
            if (!ok)
            {
                ShowWifiError(error ?? "Bağlanılamadı");
            }

            return;
        }

        _pendingWifiConnect = network;
        WifiPasswordSsid.Text = network.Ssid;
        WifiErrorText.Text = string.Empty;
        WifiPasswordBox.Password = string.Empty;
        WifiPasswordPanel.Visibility = Visibility.Visible;
        WifiPasswordBox.Focus();
    }

    private void OnWifiConnectClick(object sender, RoutedEventArgs e)
    {
        if (_wifi is null || _pendingWifiConnect is not { } network)
        {
            return;
        }

        string password = WifiPasswordBox.Password;
        (bool ok, string? error) = _wifi.TryConnect(network, password);
        if (ok)
        {
            WifiPasswordPanel.Visibility = Visibility.Collapsed;
            _pendingWifiConnect = null;
            RefreshWifiPage();
            return;
        }

        ShowWifiError(error ?? "Bağlanılamadı");
    }

    private void OnWifiCancelClick(object sender, RoutedEventArgs e)
    {
        WifiPasswordPanel.Visibility = Visibility.Collapsed;
        _pendingWifiConnect = null;
    }

    private void ShowWifiError(string message) => WifiErrorText.Text = message;
```

- [ ] **Step 7: Code-behind — Bluetooth + Project sayfaları**

```csharp
    private void RefreshBluetoothPage()
    {
        if (_bluetooth is null || _page != QuickSettingsPage.Bluetooth)
        {
            return;
        }

        if (_quick is not null)
        {
            BtSummarySubtitle.Text = _quick.IsBluetoothRadioOn ? "Açık" : "Kapalı";
        }

        BluetoothDeviceList.Children.Clear();
        foreach (BluetoothDeviceInfo device in _bluetooth.Devices)
        {
            var border = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 6),
                Background = TryFindResource("ControlFillColorDefaultBrush") as Brush,
                Tag = device,
            };
            border.Child = new DockPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = device.Name,
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            };
            if (device.IsConnected)
            {
                var badge = new TextBlock
                {
                    Text = "Bağlı",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                    Opacity = 0.7,
                };
                DockPanel.SetDock(badge, Dock.Right);
                ((DockPanel)border.Child).Children.Add(badge);
            }

            border.MouseLeftButtonUp += OnBluetoothDeviceClick;
            BluetoothDeviceList.Children.Add(border);
        }

        if (BluetoothDeviceList.Children.Count == 0)
        {
            BluetoothDeviceList.Children.Add(new TextBlock
            {
                Text = "Eşleşmiş cihaz yok",
                Opacity = 0.55,
                FontSize = 12,
                Margin = new Thickness(4, 4, 0, 0),
            });
        }
    }

    private void OnBluetoothDeviceClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: BluetoothDeviceInfo })
        {
            return;
        }

        e.Handled = true;
        QuickSettingsService.OpenSettingsUri("ms-settings:bluetooth");
    }

    private void OnOpenBluetoothSettingsClick(object sender, RoutedEventArgs e) =>
        QuickSettingsService.OpenSettingsUri("ms-settings:bluetooth");

    private static readonly (ProjectionMode Mode, string Label)[] ProjectModes =
    [
        (ProjectionMode.InternalOnly, "Yalnızca PC ekranı"),
        (ProjectionMode.Clone, "Çoğalt"),
        (ProjectionMode.Extend, "Genişlet"),
        (ProjectionMode.ExternalOnly, "Yalnızca 2. ekran"),
    ];

    private void RefreshProjectPage()
    {
        if (_projection is null || _page != QuickSettingsPage.Project)
        {
            return;
        }

        _projection.Refresh();
        ProjectModeList.Children.Clear();
        foreach ((ProjectionMode mode, string label) in ProjectModes)
        {
            bool active = _projection.Current == mode;
            var border = new Border
            {
                Padding = new Thickness(12, 10, 12, 10),
                CornerRadius = new CornerRadius(8),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 6),
                Background = active
                    ? TryFindResource("AccentFillColorDefaultBrush") as Brush
                    : TryFindResource("ControlFillColorDefaultBrush") as Brush,
                Tag = mode,
            };

            var glyph = new TextBlock
            {
                Text = active ? "\uE73E" : "\uE756",
                FontFamily = SystemVersion.IconFontFamily,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = active
                    ? TryFindResource("TextFillColorInverseBrush") as Brush
                    : null,
            };
            DockPanel.SetDock(glyph, Dock.Left);

            var labelBlock = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };

            var dock = new DockPanel();
            dock.Children.Add(glyph);
            dock.Children.Add(labelBlock);
            border.Child = dock;
            border.MouseLeftButtonUp += OnProjectModeClick;
            ProjectModeList.Children.Add(border);
        }
    }

    private void OnProjectModeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ProjectionMode mode } || _projection is null)
        {
            return;
        }

        e.Handled = true;
        if (_projection.TryApply(mode))
        {
            RefreshProjectPage();
        }
    }

    private void OnOpenDisplaySettingsClick(object sender, RoutedEventArgs e) =>
        QuickSettingsService.OpenSettingsUri("ms-settings:display");
```

- [ ] **Step 8: Derle + test**

Run: `dotnet build WharfBar.sln` ardından `dotnet test`
Expected: BUILD SUCCESS; tüm testler PASS.

---

### Task 9: In-place tile güncelleme + animasyonlar

**Files:**
- Modify: `src/WharfBar/Views/QuickSettingsFlyout.xaml.cs`

**Interfaces:**
- Consumes: mevcut `BuildTile`, `OnTileClick`, `RefreshQuick` (Task 8'deki hali).
- Produces: `RefreshQuick` in-place güncelleme; `AnimateTileToggle(TileUi ui, bool activating)`; tile elemanları artık yeniden kurulmadan güncellenir.

- [ ] **Step 1: TileUi yardımcı sınıfı + alanlar**

```csharp
    private sealed class TileUi
    {
        internal required string Id { get; init; }
        internal required StackPanel Cell { get; init; }
        internal required Border Border { get; init; }
        internal required TextBlock Icon { get; init; }
        internal required TextBlock Label { get; init; }
        internal bool IsActive { get; set; }
        internal Brush BaseBrush { get; init; }
        internal Brush AccentBrush { get; init; }
    }

    private readonly Dictionary<string, TileUi> _tiles = new();
```

- [ ] **Step 2: RefreshQuick'u in-place'e çevir**

```csharp
    private void RefreshQuick()
    {
        if (_quick is null)
        {
            return;
        }

        IReadOnlyList<QuickTileState> tiles = _quick.Tiles;

        if (tiles.Count != _tiles.Count || tiles.Any(t => !_tiles.ContainsKey(t.Id)))
        {
            TilesGrid.Children.Clear();
            _tiles.Clear();
            foreach (QuickTileState tile in tiles)
            {
                TileUi ui = BuildTile(tile);
                _tiles[tile.Id] = ui;
                TilesGrid.Children.Add(ui.Cell);
            }
        }
        else
        {
            foreach (QuickTileState tile in tiles)
            {
                UpdateTile(_tiles[tile.Id], tile);
            }
        }

        NetworkDetails net = _quick.Network;
        NetworkTitleText.Text = net.Title;
        NetworkSubtitleText.Text = net.IsOnline ? net.Subtitle : "Bağlantı yok";
        NetworkGlyphLarge.Text = net.Kind switch
        {
            NetworkKind.WiFi => "\uE701",
            NetworkKind.Ethernet => "\uE839",
            NetworkKind.Other when net.IsOnline => "\uE774",
            _ => "\uE704",
        };
    }
```

- [ ] **Step 3: BuildTile'ı TileUi döndürecek şekilde yeniden yaz**

```csharp
    private TileUi BuildTile(QuickTileState tile)
    {
        var cell = new StackPanel { Margin = new Thickness(4) };

        Brush baseBrush = TryFindResource("ControlFillColorDefaultBrush") as Brush ?? Foreground;
        Brush accentBrush = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Foreground;

        var border = new Border
        {
            Style = TryFindResource("QsTile") as Style,
            Background = tile.IsEnabled
                ? tile.IsActive ? accentBrush : baseBrush
                : baseBrush,
            Tag = tile.Id,
            ToolTip = CreateTip(tile.Tooltip),
            IsEnabled = tile.IsEnabled,
        };
        if (!tile.IsEnabled)
        {
            border.Style = TryFindResource("QsTileDisabled") as Style;
            border.Opacity = 0.45;
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (tile.HasChevron)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        var icon = new TextBlock
        {
            Text = tile.Glyph,
            Style = TryFindResource("QsGlyph") as Style,
            Foreground = tile.IsActive
                ? TryFindResource("TextFillColorInverseBrush") as Brush ?? Brushes.Black
                : TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Foreground,
        };
        grid.Children.Add(icon);

        if (tile.HasChevron)
        {
            var chevron = new Border
            {
                Width = 22,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = tile.Id,
                Child = new TextBlock
                {
                    Text = "\uE76C",
                    FontFamily = SystemVersion.IconFontFamily,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.85,
                    Foreground = icon.Foreground,
                },
            };
            Grid.SetColumn(chevron, 1);
            chevron.MouseLeftButtonUp += OnTileChevronClick;
            grid.Children.Add(chevron);

            border.BorderBrush = TryFindResource("ControlStrokeColorDefaultBrush") as Brush;
            border.BorderThickness = new Thickness(0);
        }

        border.Child = grid;
        border.MouseLeftButtonUp += OnTileClick;
        cell.Children.Add(border);
        var label = new TextBlock
        {
            Text = tile.Label,
            Style = TryFindResource("QsTileLabel") as Style,
        };
        cell.Children.Add(label);

        return new TileUi
        {
            Id = tile.Id,
            Cell = cell,
            Border = border,
            Icon = icon,
            Label = label,
            IsActive = tile.IsActive,
            BaseBrush = baseBrush,
            AccentBrush = accentBrush,
        };
    }
```

> Not: `border.MouseLeftButtonUp += OnTileClick;` — `OnTileClick` imzası `(object, MouseButtonEventArgs)`; mevcut hali de öyledir.

- [ ] **Step 4: UpdateTile + animasyon**

```csharp
    private void UpdateTile(TileUi ui, QuickTileState tile)
    {
        if (ui.Label.Text != tile.Label)
        {
            ui.Label.Text = tile.Label;
        }

        if (ui.Border.ToolTip is ToolTip tip)
        {
            tip.Content = tile.Tooltip;
        }
        else if (ui.Border.ToolTip is not string)
        {
            ui.Border.ToolTip = CreateTip(tile.Tooltip);
        }

        bool activeChanged = ui.IsActive != tile.IsActive;
        if (activeChanged)
        {
            ui.IsActive = tile.IsActive;
            ui.Icon.Foreground = tile.IsActive
                ? TryFindResource("TextFillColorInverseBrush") as Brush ?? Brushes.Black
                : TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Foreground;
            var target = new SolidColorBrush((tile.IsActive ? ui.AccentBrush : ui.BaseBrush).Color);
            SetTileBackground(ui, target);
            AnimateTileToggle(ui, target.Color, tile.IsActive);
        }
        else
        {
            SetTileBackground(ui, tile.IsActive ? ui.AccentBrush : ui.BaseBrush);
        }

        if (!tile.IsEnabled)
        {
            ui.Border.Style = TryFindResource("QsTileDisabled") as Style;
            ui.Border.Opacity = 0.45;
            ui.Border.Cursor = Cursors.Arrow;
        }
        else
        {
            ui.Border.Style = TryFindResource("QsTile") as Style;
            ui.Border.Opacity = 1;
            ui.Border.Cursor = Cursors.Hand;
        }
    }

    private static void SetTileBackground(TileUi ui, Brush brush)
    {
        ui.Border.Background = brush;
    }

    private void AnimateTileToggle(TileUi ui, Color targetColor, bool activating)
    {
        var group = new TransformGroup();
        var scale = new ScaleTransform(1, 1);
        var rotate = new RotateTransform(0);
        group.Children.Add(scale);
        group.Children.Add(rotate);
        ui.Icon.RenderTransform = group;
        ui.Icon.RenderTransformOrigin = new Point(0.5, 0.5);

        var storyboard = new Storyboard();

        var pop = new DoubleAnimation(
                fromValue: 1,
                toValue: activating ? 1.15 : 0.9,
                duration: TimeSpan.FromMilliseconds(90))
        {
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(pop, ui.Icon);
        Storyboard.SetTargetProperty(pop, new PropertyPath("RenderTransform.Children[0].ScaleX"));
        storyboard.Children.Add(pop);

        if (ui.Border.Background is SolidColorBrush from)
        {
            var targetBrush = new SolidColorBrush(targetColor);
            ui.Border.Background = targetBrush;
            if (from.Color != targetColor)
            {
                var colorAnim = new ColorAnimation(from.Color, targetColor, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                };
                Storyboard.SetTarget(colorAnim, targetBrush);
                Storyboard.SetTargetProperty(colorAnim, new PropertyPath("Color"));
                storyboard.Children.Add(colorAnim);
            }
        }

        storyboard.Begin();
    }
```

- [ ] **Step 5: Kullanılmayan QsTileActive stilini temizle (isteğe bağlı)**

`QuickSettingsFlyout.xaml` içindeki `QsTileActive` stil tanımı artık kullanılmıyor; dokunulabilir veya silinebilir. Silersen silinen style'a başka referans olmadığından emin ol.

- [ ] **Step 6: Derle + test**

Run: `dotnet build WharfBar.sln` ardından `dotnet test`
Expected: BUILD SUCCESS; tüm testler PASS.

---

### Task 10: Doküman güncelleme + final doğrulama

**Files:**
- Modify: `docs/01-architecture.md`
- Modify: `docs/02-win32-appbar-dwm.md`
- Modify: `.agent/project.md`
- Modify: `.agent/context/roadmap.md`
- Modify: `.agent/screens/README.md`

- [ ] **Step 1: docs/02-win32-appbar-dwm.md — yeni Win32 imzaları**

Win32 bölümüne (kısa imza notu olarak) ekle:

```markdown
### wlanapi.dll — Wi-Fi listesi / bağlanma (Quick Settings)

- `WlanOpenHandle(2, NULL, &ver, &handle)` → `ERROR_SUCCESS`
- `WlanEnumInterfaces(handle, NULL, &list)` — WLAN_INTERFACE_INFO_LIST (InterfaceGuid)
- `WlanGetAvailableNetworkList(handle, &guid, 0, NULL, &list)` — WLAN_AVAILABLE_NETWORK_LIST
  (wlanProfileFlags & CONNECTED/HAS_PROFILE, wlanSignalQuality 0-100, bSecurityEnabled)
- `WlanSetProfile(handle, &guid, 0, xml, NULL, TRUE, NULL, &reason)` — kalıcı profil
- `WlanConnect(handle, &guid, &params)` — WLAN_CONNECTION_PARAMETERS (mode=profile)
- `WlanQueryInterface(handle, &guid, wlan_intf_opcode_current_connection, ...)` — mevcut SSID
- `WlanFreeMemory` / `WlanCloseHandle`

### user32.dll — display topology (Quick Settings / Yansıt)

- `QueryDisplayConfig(QDC_DATABASE_CURRENT, 0, NULL, 0, NULL, &topologyId)` — mevcut topoloji
- `SetDisplayConfig(0, NULL, 0, NULL, SDC_TOPOLOGY_x | SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES)`
```

- [ ] **Step 2: docs/01-architecture.md**

Servis listesine ekle (mevcut liste stilinde, kısa):

```markdown
- `Services/WifiService` — Wlan API ile ağ listesi + bağlanma (QS Wifi sayfası)
- `Services/BluetoothService` — eşleşmiş BT cihazları + bağlı durumu (QS Bluetooth sayfası)
- `Services/NightLightService` — CloudStore best-effort gece ışığı okuma/yazma + oturum override
- `Services/ProjectionService` — `SetDisplayConfig` ile yansıtma modları (QS Project sayfası)
```

- [ ] **Step 3: .agent/project.md ve .agent/context/roadmap.md**

Quick Settings ilerleme satırını güncelle: canlı Wi-Fi listesi/bağlanma, BT cihaz listesi, gece ışığı gerçek durum+toggle, native yansıtma paneli, tile animasyonları tamamlandı.

- [ ] **Step 4: .agent/screens/README.md**

Quick Settings UI turuna yeni sayfaları ekle: Wi-Fi (ağ listesi + şifre paneli), Bluetooth (cihaz listesi), Yansıt (4 mod), gece ışığı tile'ının gerçek durumu ve animasyon davranışı.

- [ ] **Step 5: Final doğrulama**

Run: `dotnet build WharfBar.sln`; ardından `dotnet test`
Expected: BUILD SUCCESS; tüm testler PASS.

Run: `dotnet run --project src/WharfBar` ve spec'teki manuel doğrulama listesini uygula (Wi-Fi chevron → liste, bağlanma, BT listesi, gece ışığı durumu/toggle, Yansıt 4 mod, animasyonlar). Uygulamayı kapatırken pencereyi normal yoldan kapat (AppBar kaydı düzgün kaldırılsın).

---

## Self-Review notları

- **Spec kapsamı:** 4 eksenin her biri bir task zinciriyle kapatıldı: Wi-Fi (T4-T5-T8), Bluetooth (T6-T8), gece ışığı (T1-T2-T7), Yansıt (T3-T8), animasyonlar + in-place (T9). Doküman güncellemeleri T10.
- **Polling yaşam döngüsü:** Wifi/BT yalnızca ilgili sayfa açıkken poll eder (T8 ShowPage); gece ışığı QS timer içinden (T7 Refresh).
- **Bilinen risk:** gece ışığı registry yazımı sürüm duyarlı; T2 yazımı atlayıp override korur (spec kararı). Wlan profil/bağlanma akışı donanıma bağlı, manuel doğrulama gerekir.