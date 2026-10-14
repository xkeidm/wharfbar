using System.Diagnostics;
using System.Security;
using System.Text;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

internal sealed record WifiNetworkInfo(
    string Ssid,
    int Signal,
    bool IsSecured,
    bool IsKnown,
    bool IsConnected,
    string? ProfileName,
    uint AuthAlgorithm = 0,
    bool IsConnectable = true);

/// <summary>Wlan API üzerinden ağ listesi ve bağlanma. Sadece Wifi sayfası açıkken poll edilir.</summary>
internal sealed class WifiService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private int _refreshGate;
    private bool _disposed;

    internal WifiService()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => _ = RefreshAsync();
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

        _ = RefreshAsync();
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

    internal const uint AuthWpa = 3;
    internal const uint AuthWpaPsk = 4;
    internal const uint AuthRsna = 6;
    internal const uint AuthRsnaPsk = 7;
    internal const uint AuthWpa3Ent192 = 8;
    internal const uint AuthWpa3Sae = 9;
    internal const uint AuthOwe = 10;
    internal const uint AuthWpa3Ent = 11;
    internal const string HiddenSsidLabel = "(Gizli ağ)";

    /// <summary>Bağlanma denemeden önce elenen ağlar. Şifre eksikliği burada yoktur.</summary>
    internal static string? ConnectBlockReason(WifiNetworkInfo network)
    {
        if (network.Ssid == HiddenSsidLabel)
        {
            return "Gizli ağa buradan bağlanılamaz";
        }

        if (!network.IsConnectable)
        {
            return "Bu ağa bağlanılamıyor";
        }

        if (IsEnterpriseAuth(network.AuthAlgorithm))
        {
            return "Kurumsal ağ — Wi-Fi ayarlarından bağlanın";
        }

        return null;
    }

    internal static bool IsEnterpriseAuth(uint auth) =>
        auth is AuthWpa or AuthRsna or AuthWpa3Ent192 or AuthWpa3Ent;

    internal (bool Ok, string? Error) TryConnect(WifiNetworkInfo network, string? password)
    {
        if (ConnectBlockReason(network) is { } blocked)
        {
            return (false, blocked);
        }

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
                if (network.IsSecured && string.IsNullOrEmpty(password))
                {
                    return (false, "Şifre gerekli");
                }

                string xml = BuildProfileXml(network.Ssid, network.IsSecured ? password : null, network.AuthAlgorithm);
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

    internal (bool Ok, string? Error) TryDisconnect()
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

            return WlanApi.TryDisconnect(handle, guid)
                ? (true, null)
                : (false, "Bağlantı kesilemedi");
        }
        finally
        {
            WlanApi.Close(handle);
        }
    }

    /// <summary>
    /// <c>WlanConnect</c> isteği kabul etmek ile ilişkilendirme aynı şey değildir.
    /// Yanlış parolada API yine başarı döner; SSID gerçekten bağlanana kadar beklenir.
    /// </summary>
    internal bool WaitUntilConnected(string ssid, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        do
        {
            if (string.Equals(TryReadCurrentSsid(), ssid, StringComparison.Ordinal))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            Thread.Sleep(400);
        }
        while (true);
    }

    private static string? TryReadCurrentSsid()
    {
        if (!WlanApi.TryOpen(out IntPtr handle))
        {
            return null;
        }

        try
        {
            if (!WlanApi.TryGetInterfaceGuid(handle, out Guid guid)
                || !WlanApi.TryGetCurrentSsid(handle, guid, out byte[] ssid))
            {
                return null;
            }

            return SsidToString(ssid, ssid.Length);
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

        return SsidToString(ssid, len);
    }

    internal static string SsidToString(byte[] ssid, int length)
    {
        int len = Math.Min(Math.Max(length, 0), Math.Min(ssid.Length, 32));
        if (len == 0)
        {
            // Boş SSID = yayınını kapatmış AP (gizli ağ).
            return HiddenSsidLabel;
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

    /// <summary>Aynı SSID bir kez: bağlı, sonra kayıtlı, sonra en güçlü sinyal.</summary>
    internal static IReadOnlyList<WifiNetworkInfo> DedupeNetworks(IEnumerable<WifiNetworkInfo> networks) =>
        networks
            .GroupBy(n => n.Ssid, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(n => n.IsConnected)
                .ThenByDescending(n => n.IsKnown)
                .ThenByDescending(n => n.Signal)
                .First())
            .ToList();

    /// <summary>Bağlı ağ en üste; ardından sinyal gücüne göre azalan.</summary>
    internal static IReadOnlyList<WifiNetworkInfo> SortNetworks(IEnumerable<WifiNetworkInfo> networks) =>
        networks
            .OrderByDescending(n => n.IsConnected)
            .ThenByDescending(n => n.Signal)
            .ToList();

    /// <summary>
    /// Düz metin parola <c>protected=false</c> ister. <c>true</c> şifreli anahtar blob'u demektir
    /// ve WlanSetProfile düz metni reddeder.
    /// </summary>
    internal static string BuildProfileXml(string ssid, string? password, uint authAlgorithm = 0)
    {
        string name = SecurityElement.Escape(ssid);
        (string auth, string enc) = ProfileSecurity(authAlgorithm, password is not null);
        string sharedKey = password is null
            ? string.Empty
            : $"<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>{SecurityElement.Escape(password)}</keyMaterial></sharedKey>";

        return $"""
<?xml version="1.0"?>
<WLANProfile xmlns="http://www.microsoft.com/networking/WLAN/profile/v1">
  <name>{name}</name>
  <SSIDConfig><SSID><name>{name}</name></SSID></SSIDConfig>
  <connectionType>ESS</connectionType>
  <connectionMode>manual</connectionMode>
  <MSM><security>
    <authEncryption><authentication>{auth}</authentication><encryption>{enc}</encryption><useOneX>false</useOneX></authEncryption>
    {sharedKey}
  </security></MSM>
</WLANProfile>
""";
    }

    internal static (string Auth, string Enc) ProfileSecurity(uint authAlgorithm, bool hasPassword)
    {
        if (!hasPassword)
        {
            return authAlgorithm == AuthOwe ? ("OWE", "AES") : ("open", "none");
        }

        return authAlgorithm switch
        {
            AuthWpaPsk => ("WPAPSK", "AES"),
            AuthWpa3Sae => ("WPA3SAE", "AES"),
            AuthOwe => ("OWE", "AES"),
            _ => ("WPA2PSK", "AES"),
        };
    }

    private async Task RefreshAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshGate, 1) != 0)
        {
            return;
        }

        try
        {
            (bool available, IReadOnlyList<WifiNetworkInfo> networks) = await Task.Run(ReadSnapshot);
            if (_disposed)
            {
                return;
            }

            void Apply()
            {
                if (_disposed)
                {
                    return;
                }

                IsAvailable = available;
                Networks = networks;
                Changed?.Invoke(this, EventArgs.Empty);
            }

            if (_dispatcher.CheckAccess())
            {
                Apply();
            }
            else
            {
                _ = _dispatcher.BeginInvoke(Apply);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _refreshGate, 0);
        }
    }

    private static (bool Available, IReadOnlyList<WifiNetworkInfo> Networks) ReadSnapshot()
    {
        if (!WlanApi.TryOpen(out IntPtr handle))
        {
            return (false, []);
        }

        try
        {
            if (!WlanApi.TryGetInterfaceGuid(handle, out Guid guid))
            {
                return (false, []);
            }

            // Tarama asenkron tamamlanır; sonuç sonraki poll'ların cache'inde görünür.
            WlanApi.Scan(handle, guid);
            if (!WlanApi.TryGetNetworks(handle, guid, out List<WlanApi.RawNetwork> raw))
            {
                return (true, []);
            }

            IReadOnlyList<WifiNetworkInfo> networks = SortNetworks(DedupeNetworks(raw
                .Select(n => new WifiNetworkInfo(
                    SsidToString(n.Ssid, n.SsidLength),
                    n.Signal,
                    n.Secured,
                    n.HasProfile,
                    n.Connected,
                    n.ProfileName,
                    n.AuthAlgorithm,
                    n.Connectable))));
            return (true, networks);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: Wi-Fi refresh failed — {ex.Message}");
            return (false, []);
        }
        finally
        {
            WlanApi.Close(handle);
        }
    }
}