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
                if (network.IsSecured && password is null)
                {
                    return (false, "Şifre gerekli");
                }

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
            : $"<sharedKey><keyType>passPhrase</keyType><protected>true</protected><keyMaterial>{SecurityElement.Escape(password)}</keyMaterial></sharedKey>";

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
                    n.ProfileName))
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