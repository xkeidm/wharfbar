using System.Globalization;
using System.Text;
using WharfBar.Models;

namespace WharfBar.Services;

internal sealed record SystemStatsDisplay(
    bool CpuVisible,
    string CpuBarText,
    string CpuTooltip,
    bool GpuVisible,
    string GpuBarText,
    string GpuTooltip,
    bool RamVisible,
    string RamBarText,
    string RamTooltip);

internal static class SystemStatsFormatter
{
    private const string Separator = " · ";

    internal static SystemStatsDisplay Format(SystemStatsSnapshot snapshot, TopBarConfig config)
    {
        if (!config.ShowSystemStats)
        {
            return new SystemStatsDisplay(
                false, string.Empty, string.Empty,
                false, string.Empty, string.Empty,
                false, string.Empty, string.Empty);
        }

        bool cpuPctOk = true;
        bool cpuGhzOk = snapshot.CpuGhz is > 0;
        bool ramOk = true;
        bool gpuPctOk = snapshot.IsGpuUsageAvailable;
        bool gpuVramOk = snapshot.IsGpuVramAvailable
            && snapshot.GpuUsedVramGb is not null
            && snapshot.GpuTotalVramGb is > 0;

        (bool cpuVisible, string cpuBar, string cpuTip) = FormatBadge(
            config.ShowCpuStats,
            Field(config.CpuPercentPlacement, StatPlacement.Bar, cpuPctOk, $"{snapshot.CpuPercent}%",
                $"CPU kullanımı: {snapshot.CpuPercent}%"),
            Field(config.CpuGhzPlacement, StatPlacement.Tooltip, cpuGhzOk,
                string.Format(CultureInfo.CurrentCulture, "{0:F1} GHz", snapshot.CpuGhz ?? 0),
                string.Format(CultureInfo.CurrentCulture, "CPU hızı: {0:F1} GHz", snapshot.CpuGhz ?? 0)));

        (bool ramVisible, string ramBar, string ramTip) = FormatBadge(
            config.ShowRamStats,
            Field(config.RamPercentPlacement, StatPlacement.Bar, ramOk, $"{snapshot.RamPercent}%",
                $"RAM kullanımı: {snapshot.RamPercent}%"),
            Field(config.RamUsedTotalPlacement, StatPlacement.Tooltip, ramOk,
                FormatGbPair(snapshot.UsedRamGb, snapshot.TotalRamGb),
                $"RAM: {FormatGbPair(snapshot.UsedRamGb, snapshot.TotalRamGb)}"));

        (bool gpuVisible, string gpuBar, string gpuTip) = FormatBadge(
            config.ShowGpuStats,
            Field(config.GpuPercentPlacement, StatPlacement.Bar, gpuPctOk, $"{snapshot.GpuPercent}%",
                $"GPU kullanımı: {snapshot.GpuPercent}%"),
            Field(config.GpuVramPlacement, StatPlacement.Tooltip, gpuVramOk,
                FormatGbPair(snapshot.GpuUsedVramGb ?? 0, snapshot.GpuTotalVramGb ?? 0),
                $"GPU VRAM: {FormatGbPair(snapshot.GpuUsedVramGb ?? 0, snapshot.GpuTotalVramGb ?? 0)}"));

        return new SystemStatsDisplay(
            cpuVisible, cpuBar, cpuTip,
            gpuVisible, gpuBar, gpuTip,
            ramVisible, ramBar, ramTip);
    }

    internal static string FormatGbPair(double used, double total)
    {
        string usedText = used.ToString("F1", CultureInfo.CurrentCulture);
        string totalText = Math.Abs(total - Math.Round(total)) < 0.15
            ? Math.Round(total).ToString("F0", CultureInfo.CurrentCulture)
            : total.ToString("F1", CultureInfo.CurrentCulture);
        return $"{usedText} / {totalText} GB";
    }

    private readonly record struct FieldSpec(
        string Placement,
        string Fallback,
        bool Available,
        string BarPiece,
        string TooltipLine);

    private static FieldSpec Field(
        string placement, string fallback, bool available, string barPiece, string tooltipLine) =>
        new(placement, fallback, available, barPiece, tooltipLine);

    private static (bool Visible, string BarText, string Tooltip) FormatBadge(
        bool showBadge, FieldSpec first, FieldSpec second)
    {
        if (!showBadge)
        {
            return (false, string.Empty, string.Empty);
        }

        List<string> barParts = [];
        List<string> tipLines = [];
        bool any = false;

        Append(first, barParts, tipLines, ref any);
        Append(second, barParts, tipLines, ref any);

        if (!any)
        {
            return (false, string.Empty, string.Empty);
        }

        StringBuilder tip = new();
        for (int i = 0; i < tipLines.Count; i++)
        {
            if (i > 0)
            {
                tip.AppendLine();
            }

            tip.Append(tipLines[i]);
        }

        return (true, string.Join(Separator, barParts), tip.ToString());
    }

    private static void Append(
        FieldSpec field, List<string> barParts, List<string> tipLines, ref bool any)
    {
        if (!field.Available)
        {
            return;
        }

        if (StatPlacement.IsBar(field.Placement, field.Fallback))
        {
            barParts.Add(field.BarPiece);
            tipLines.Add(field.TooltipLine);
            any = true;
            return;
        }

        if (StatPlacement.IsOnTooltip(field.Placement, field.Fallback))
        {
            tipLines.Add(field.TooltipLine);
            any = true;
        }
    }
}
