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
public sealed partial class DockItemCell : UserControl
{
    private const double MagnificationEpsilon = 5e-4d;

    private double _appliedScale = 1d;
    private double _iconSize;

    public DockItemCell()
    {
        InitializeComponent();
        RenderOptions.SetBitmapScalingMode(IconImage, BitmapScalingMode.HighQuality);
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

    /// <summary>1 px divider rather than an app icon.</summary>
    public bool IsSeparator => Row?.IsSeparator != false;

    internal void ApplyMetrics(double iconSize, double indicatorGap, double indicatorSize, double height)
    {
        Root.Height = height;
        SeparatorBar.Visibility = IsSeparator ? Visibility.Visible : Visibility.Collapsed;

        if (Math.Abs(iconSize - _iconSize) < 0.01d)
        {
            return;
        }

        _iconSize = iconSize;
        IconImage.Width = iconSize;
        IconImage.Height = iconSize;
        RunningPill.Width = indicatorSize * 3.5d;
        RunningPill.Height = indicatorSize;
        RunningPill.Margin = new Thickness(0, 0, 0, indicatorGap);
        IconImage.Margin = new Thickness(0, 0, 0, indicatorGap + indicatorSize);
        IconScale.ScaleX = _appliedScale;
        IconScale.ScaleY = _appliedScale;
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

    private static void OnRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DockItemCell)d).ApplyRow();

    private void ApplyRow()
    {
        DockItemViewModel? row = Row;
        if (row is null)
        {
            IconImage.Source = null;
            RunningPill.Visibility = Visibility.Collapsed;
            SeparatorBar.Visibility = Visibility.Collapsed;
            return;
        }

        bool separator = row.IsSeparator;
        IconImage.Visibility = separator ? Visibility.Collapsed : Visibility.Visible;
        SeparatorBar.Visibility = separator ? Visibility.Visible : Visibility.Collapsed;
        RunningPill.Visibility = !separator && row.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        IconImage.Source = separator ? null : row.Icon;
        ToolTip = null;
        ContextMenu = null;
    }
}
