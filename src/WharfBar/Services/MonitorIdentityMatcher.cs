using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Pure matching between configured monitor entries and live monitors. No P/Invoke — unit-testable.
/// Identity is the EnumDisplayDevices registry path (DeviceId); DeviceName is a fallback because
/// the display number can renumber on replug.
/// </summary>
internal static class MonitorIdentityMatcher
{
    internal static bool Matches(MonitorConfig config, MonitorInfo live)
    {
        if (!string.IsNullOrWhiteSpace(config.DeviceId) && !string.IsNullOrWhiteSpace(live.DeviceId))
        {
            return string.Equals(config.DeviceId, live.DeviceId, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(config.DeviceName)
            && string.Equals(config.DeviceName, live.DeviceName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the configured entry for <paramref name="live"/>, or a synthesized default:
    /// the primary monitor defaults to both on (preserves v1 behavior), everything else off.
    /// </summary>
    internal static MonitorConfig Resolve(IReadOnlyList<MonitorConfig> configured, MonitorInfo live)
    {
        MonitorConfig? match = configured.FirstOrDefault(c => Matches(c, live));
        if (match is not null)
        {
            return match;
        }

        return new MonitorConfig
        {
            DeviceId = live.DeviceId,
            DeviceName = live.DeviceName,
            Name = live.FriendlyName,
            TopBar = live.IsPrimary,
            Dock = live.IsPrimary,
        };
    }

    /// <summary>Live monitors that have no configured entry (newly plugged displays).</summary>
    internal static IReadOnlyList<MonitorInfo> MissingMonitors(
        IReadOnlyList<MonitorConfig> configured,
        IReadOnlyList<MonitorInfo> live)
    {
        return live.Where(m => !configured.Any(c => Matches(c, m))).ToList();
    }
}