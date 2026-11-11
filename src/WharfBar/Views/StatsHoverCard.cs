using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Hover card for the stats text. A sparkline beside the 12px line does not fit
/// in the 30 DIP bar without growing it, so the lines live here.
/// </summary>
internal sealed class StatsHoverCard : StackPanel
{
    private readonly TextBlock _summary = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _cpuLabel = Label("CPU");
    private readonly Polyline _cpu = Line();
    private readonly TextBlock _gpuLabel = Label("GPU");
    private readonly Polyline _gpu = Line();
    private readonly TextBlock _ramLabel = Label("RAM");
    private readonly Polyline _ram = Line();
    private readonly TextBlock _net = new() { FontSize = 11, Margin = new Thickness(0, 8, 0, 0), Text = Strings.Qs_Network };

    internal StatsHoverCard()
    {
        Width = 168;
        Children.Add(_summary);
        Children.Add(_cpuLabel);
        Children.Add(_cpu);
        Children.Add(_gpuLabel);
        Children.Add(_gpu);
        Children.Add(_ramLabel);
        Children.Add(_ram);
        Children.Add(_net);

        // Live accent reference, not a brush handed in at paint time: the card is built once with
        // the bar and must follow a later light/dark or accent swap on its own.
        _cpu.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        _gpu.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
        _ram.SetResourceReference(Shape.StrokeProperty, "AccentFillColorDefaultBrush");
    }

    internal void Apply(SystemStatsSnapshot snapshot, string text)
    {
        _summary.Text = text;
        Paint(_cpu, snapshot.CpuHistory);
        bool gpu = snapshot.GpuPercents is { Length: > 0 };
        _gpuLabel.Visibility = gpu ? Visibility.Visible : Visibility.Collapsed;
        _gpu.Visibility = gpu ? Visibility.Visible : Visibility.Collapsed;
        if (gpu)
        {
            Paint(_gpu, snapshot.GpuHistory);
        }

        Paint(_ram, snapshot.RamHistory);
        string down = string.IsNullOrEmpty(snapshot.NetworkDownText) ? "—" : snapshot.NetworkDownText;
        string up = string.IsNullOrEmpty(snapshot.NetworkUpText) ? "—" : snapshot.NetworkUpText;
        _net.Text = $"↓ {down}   ↑ {up}";
    }

    private static void Paint(Polyline line, IReadOnlyList<double>? samples)
    {
        line.Points = Sparkline.Plot(Sparkline.Decimate(samples, Sparkline.DefaultPoints), 160, 18);
    }

    private static TextBlock Label(string text)
        => new()
        {
            Text = text,
            FontSize = 11,
            Opacity = 0.75,
            Margin = new Thickness(0, 6, 0, 0),
        };

    private static Polyline Line()
        => new()
        {
            Height = 18,
            StrokeThickness = 1.25,
        };
}
