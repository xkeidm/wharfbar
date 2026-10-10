using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WharfBar.Services;

namespace WharfBar.Views.Controls;

/// <summary>
/// One dock slot: an app icon with its running pill.
/// The cell never decides its own size — <see cref="DockStrip"/> pushes the button and glyph in.
/// The glyph stays at resting size; hover paints the button behind it.
/// </summary>
internal sealed partial class DockItemCell : UserControl
{
    private const double MetricsEpsilon = 0.01d;

    // NaN = "never applied": every comparison below is false on the first call, so the first
    // ApplyMetrics always writes even if a metric legitimately reaches 0.
    private double _glyphSize = double.NaN;
    private double _buttonSize = double.NaN;

    public DockItemCell()
    {
        InitializeComponent();
        Cursor = System.Windows.Input.Cursors.Hand;
    }

    public static readonly DependencyProperty RowProperty =
        DependencyProperty.Register(
            nameof(Row),
            typeof(DockItemViewModel),
            typeof(DockItemCell),
            new PropertyMetadata(null, OnRowChanged));

    /// <summary>Slot content. Setting it re-applies icon and pill visibility.</summary>
    public DockItemViewModel? Row
    {
        get => (DockItemViewModel?)GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    internal void ApplyMetrics(double glyphSize, double buttonSize, double height)
    {
        Root.Height = height;

        if (MetricsChanged(glyphSize, _glyphSize) || MetricsChanged(buttonSize, _buttonSize))
        {
            _glyphSize = glyphSize;
            _buttonSize = buttonSize;
            IconImage.Width = buttonSize;
            IconImage.Height = buttonSize;
            IconGlyph.Width = glyphSize;
            IconGlyph.Height = glyphSize;
            HoverPlate.Width = buttonSize;
            HoverPlate.Height = buttonSize;
            HoverPlate.CornerRadius = new CornerRadius(DockPlateMetrics.HoverCornerRadiusDip);
            IconScale.ScaleX = 1d;
            IconScale.ScaleY = 1d;
            RunningPill.Width = DockPlateMetrics.PillWidthDip;
            RunningPill.Height = DockPlateMetrics.PillThicknessDip;
            RunningPill.CornerRadius = new CornerRadius(DockPlateMetrics.PillThicknessDip);
            RunningPill.Margin = new Thickness(0, 0, 0, DockPlateMetrics.PillInsetDip);
            IconImage.Margin = new Thickness(0);
        }
    }

    internal void SetMagnification(double scale)
    {
        // The plate no longer grows under the pointer. The argument stays so DockStrip's
        // arrange path compiles against the old call; the scale is not applied.
        _ = scale;
        IconScale.ScaleX = 1d;
        IconScale.ScaleY = 1d;
    }

    private static bool MetricsChanged(double next, double applied) =>
        double.IsNaN(applied) || Math.Abs(next - applied) >= MetricsEpsilon;

    private static void OnRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DockItemCell)d).ApplyRow();

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (Row is not null)
        {
            HoverPlate.Visibility = Visibility.Visible;
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        HoverPlate.Visibility = Visibility.Collapsed;
    }

    private void ApplyRow()
    {
        DockItemViewModel? row = Row;
        if (row is null)
        {
            IconGlyph.Source = null;
            RunningPill.Visibility = Visibility.Collapsed;
            HoverPlate.Visibility = Visibility.Collapsed;
            return;
        }

        IconImage.Visibility = Visibility.Visible;
        RunningPill.Visibility = row.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        HoverPlate.Visibility = IsMouseOver ? Visibility.Visible : Visibility.Collapsed;
        IconGlyph.Source = row.Icon;
        ToolTip = null;
        ContextMenu = null;
    }
}
