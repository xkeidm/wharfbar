using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WharfBar.Views.Controls;

/// <summary>
/// One dock slot: an app icon with its running pill, or a section separator.
/// The cell never decides its own size — <see cref="DockStrip"/> pushes metrics in and
/// scales the icon from its bottom edge so growth reads as "rising off the plate".
/// </summary>
internal sealed partial class DockItemCell : UserControl
{
    private const double MetricsEpsilon = 0.01d;

    private const double MagnificationEpsilon = 5e-4d;

    private double _appliedScale = 1d;

    // NaN = "never applied": every comparison below is false on the first call, so the first
    // ApplyMetrics always writes even if a metric legitimately reaches 0.
    private double _iconSize = double.NaN;
    private double _indicatorGap = double.NaN;
    private double _indicatorSize = double.NaN;

    public DockItemCell()
    {
        InitializeComponent();
        RenderOptions.SetBitmapScalingMode(IconGlyph, BitmapScalingMode.HighQuality);
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public static readonly DependencyProperty RowProperty =
        DependencyProperty.Register(
            nameof(Row),
            typeof(DockItemViewModel),
            typeof(DockItemCell),
            new PropertyMetadata(null, OnRowChanged));

    /// <summary>Slot content. Setting it re-applies icon, pill, and separator visibility.</summary>
    public DockItemViewModel? Row
    {
        get => (DockItemViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>1 px divider rather than an app icon. A null <see cref="Row"/> is an empty slot, not a divider.</summary>
    public bool IsSeparator => Row?.IsSeparator == true;

    internal void ApplyMetrics(double iconSize, double indicatorGap, double indicatorSize, double height)
    {
        Root.Height = height;
        SeparatorBar.Visibility = IsSeparator ? Visibility.Visible : Visibility.Collapsed;

        // One guard per group of writes, and each guard tracks exactly what its writes depend on.
        if (MetricsChanged(iconSize, _iconSize))
        {
            _iconSize = iconSize;
            IconImage.Width = iconSize;
            IconImage.Height = iconSize;
            IconScale.ScaleX = _appliedScale;
            IconScale.ScaleY = _appliedScale;
        }

        if (MetricsChanged(indicatorGap, _indicatorGap) || MetricsChanged(indicatorSize, _indicatorSize))
        {
            _indicatorGap = indicatorGap;
            _indicatorSize = indicatorSize;
            RunningPill.Width = indicatorSize * 3.5d;
            RunningPill.Height = indicatorSize;
            RunningPill.Margin = new Thickness(0, 0, 0, indicatorGap);
            IconImage.Margin = new Thickness(0, 0, 0, indicatorGap + indicatorSize);
        }
    }

    internal void SetMagnification(double scale)
    {
        if (Math.Abs(scale - _appliedScale) < MagnificationEpsilon)
        {
            return;
        }

        _appliedScale = scale;
        IconScale.ScaleX = scale;
        IconScale.ScaleY = scale;
    }

    private static bool MetricsChanged(double next, double applied) =>
        double.IsNaN(applied) || Math.Abs(next - applied) >= MetricsEpsilon;

    private static void OnRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DockItemCell)d).ApplyRow();

    private void ApplyRow()
    {
        DockItemViewModel? row = Row;
        if (row is null)
        {
            IconGlyph.Source = null;
            RunningPill.Visibility = Visibility.Collapsed;
            SeparatorBar.Visibility = Visibility.Collapsed;
            return;
        }

        bool separator = row.IsSeparator;
        IconImage.Visibility = separator ? Visibility.Collapsed : Visibility.Visible;
        SeparatorBar.Visibility = separator ? Visibility.Visible : Visibility.Collapsed;
        RunningPill.Visibility = !separator && row.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        IconGlyph.Source = separator ? null : row.Icon;
        ToolTip = null;
        ContextMenu = null;
    }
}
