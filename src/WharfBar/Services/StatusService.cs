using System.Globalization;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>How the machine reaches the network, as far as .NET can tell.</summary>
internal enum NetworkKind
{
    Offline,
    WiFi,
    Ethernet,
    Other,
}

/// <summary>Immutable UI-facing snapshot produced by <see cref="StatusService"/>.</summary>
internal sealed record StatusSnapshot(
    string ClockText,
    string ClockTooltip,
    bool IsNetworkOnline,
    NetworkKind NetworkKind,
    string NetworkTooltip,
    bool IsBatteryPresent,
    int BatteryPercent,
    bool IsBatteryCharging,
    bool IsAcOnline,
    string BatteryText,
    string BatteryTooltip);

/// <summary>
/// Polls clock / power and listens for network change events. Values come from .NET and
/// <c>GetSystemPowerStatus</c> — never from reading other apps' tray icons.
/// </summary>
internal sealed class StatusService : IDisposable
{
    private static readonly TimeSpan ClockInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PowerInterval = TimeSpan.FromSeconds(15);

    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _powerTimer;
    private bool _disposed;

    internal StatusService()
    {
        Snapshot = BuildSnapshot();

        _clockTimer = new DispatcherTimer { Interval = ClockInterval };
        _clockTimer.Tick += (_, _) => RefreshClock();

        _powerTimer = new DispatcherTimer { Interval = PowerInterval };
        _powerTimer.Tick += (_, _) => RefreshPowerAndNetwork();

        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;

        _clockTimer.Start();
        _powerTimer.Start();
    }

    internal StatusSnapshot Snapshot { get; private set; }

    internal event EventHandler? Changed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _clockTimer.Stop();
        _powerTimer.Stop();
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        // NetworkChange callbacks arrive on a thread-pool thread.
        var dispatcher = _clockTimer.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            RefreshPowerAndNetwork();
        }
        else
        {
            dispatcher.BeginInvoke(RefreshPowerAndNetwork);
        }
    }

    private void RefreshClock()
    {
        StatusSnapshot previous = Snapshot;
        (string clock, string tip) = FormatClock(DateTime.Now);
        if (clock == previous.ClockText && tip == previous.ClockTooltip)
        {
            return;
        }

        Snapshot = previous with { ClockText = clock, ClockTooltip = tip };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshPowerAndNetwork()
    {
        StatusSnapshot next = BuildSnapshot();
        StatusSnapshot previous = Snapshot;

        // Keep the just-ticked clock text if BuildSnapshot lands in the same minute.
        if (next.ClockText == previous.ClockText
            && next.IsNetworkOnline == previous.IsNetworkOnline
            && next.NetworkKind == previous.NetworkKind
            && next.IsBatteryPresent == previous.IsBatteryPresent
            && next.BatteryPercent == previous.BatteryPercent
            && next.IsBatteryCharging == previous.IsBatteryCharging
            && next.IsAcOnline == previous.IsAcOnline)
        {
            return;
        }

        Snapshot = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static StatusSnapshot BuildSnapshot()
    {
        DateTime now = DateTime.Now;
        (string clock, string clockTip) = FormatClock(now);

        (bool online, NetworkKind kind, string netTip) = ReadNetwork();
        PowerStatusInfo power = PowerStatus.Query();
        (string batteryText, string batteryTip) = FormatBattery(power);

        return new StatusSnapshot(
            clock,
            clockTip,
            online,
            kind,
            netTip,
            power.IsBatteryPresent,
            power.Percent,
            power.IsCharging,
            power.IsAcOnline,
            batteryText,
            batteryTip);
    }

    private static (string Text, string Tooltip) FormatClock(DateTime now)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        string text = now.ToString("t", culture);
        string tip = now.ToString("f", culture);
        return (text, tip);
    }

    private static (bool Online, NetworkKind Kind, string Tooltip) ReadNetwork()
    {
        bool available = NetworkInterface.GetIsNetworkAvailable();
        if (!available)
        {
            return (false, NetworkKind.Offline, "No network connection");
        }

        NetworkKind kind = NetworkKind.Other;
        string? name = null;

        try
        {
            foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                        or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                NetworkKind candidate = nic.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => NetworkKind.WiFi,
                    NetworkInterfaceType.Ethernet
                        or NetworkInterfaceType.Ethernet3Megabit
                        or NetworkInterfaceType.FastEthernetT
                        or NetworkInterfaceType.FastEthernetFx
                        or NetworkInterfaceType.GigabitEthernet => NetworkKind.Ethernet,
                    _ => NetworkKind.Other,
                };

                // Prefer Wi-Fi, then Ethernet, over opaque "Other" adapters.
                bool better = kind == NetworkKind.Other && candidate != NetworkKind.Other
                    || candidate == NetworkKind.WiFi && kind != NetworkKind.WiFi
                    || candidate == NetworkKind.Ethernet && kind == NetworkKind.Other;

                if (better || name is null)
                {
                    kind = candidate;
                    name = nic.Name;
                    if (candidate == NetworkKind.WiFi)
                    {
                        break;
                    }
                }
            }
        }
        catch (NetworkInformationException)
        {
            return (available, NetworkKind.Other, "Network connected");
        }

        string tip = kind switch
        {
            NetworkKind.WiFi => string.IsNullOrEmpty(name) ? "Wi-Fi connected" : $"Wi-Fi: {name}",
            NetworkKind.Ethernet => string.IsNullOrEmpty(name) ? "Ethernet connected" : $"Ethernet: {name}",
            _ => string.IsNullOrEmpty(name) ? "Network connected" : $"Connected: {name}",
        };

        return (true, kind, tip);
    }

    private static (string Text, string Tooltip) FormatBattery(PowerStatusInfo power)
    {
        if (!power.IsBatteryPresent)
        {
            return (string.Empty, power.IsAcOnline ? "Plugged in (no battery)" : "Power status unknown");
        }

        string text = $"{power.Percent}%";
        string tip = power.IsCharging
            ? $"Battery {power.Percent}% (charging)"
            : power.IsAcOnline
                ? $"Battery {power.Percent}% (plugged in)"
                : $"Battery {power.Percent}%";

        return (text, tip);
    }
}
