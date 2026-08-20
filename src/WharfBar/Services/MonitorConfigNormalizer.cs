using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>Pure normalization for <see cref="AppConfig.Monitors"/> (unit-tested).</summary>
internal static class MonitorConfigNormalizer
{
    internal static IReadOnlyList<MonitorConfig> Normalize(List<MonitorConfig>? monitors)
    {
        if (monitors is null)
        {
            return [];
        }

        var result = new List<MonitorConfig>(monitors.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (MonitorConfig? entry in monitors)
        {
            if (entry is null)
            {
                continue;
            }

            entry.DeviceId = entry.DeviceId?.Trim() ?? string.Empty;
            entry.DeviceName = entry.DeviceName?.Trim() ?? string.Empty;
            entry.Name = entry.Name?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(entry.DeviceId))
            {
                continue;
            }

            if (!seen.Add(entry.DeviceId))
            {
                continue;
            }

            result.Add(entry);
        }

        return result;
    }
}
