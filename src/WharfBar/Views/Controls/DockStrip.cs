using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WharfBar.Services;

namespace WharfBar.Views.Controls;

/// <summary>
/// The dock's horizontal strip: owns one <see cref="DockItemCell"/> per slot, arranges them
/// with magnified slot widths, and maps the cursor to a fractional slot index so the peak
/// icon slides between neighbours instead of snapping.
/// <para>
/// The panel reports its live width as <c>DesiredSize</c> so the plate hugs the icons, while
/// the window itself stays sized for the worst case — nothing here calls into Win32.
/// </para>
/// </summary>
internal sealed class DockStrip : Panel
{
    private readonly List<DockItemCell> _cells = [];
    private double[] _scales = [];

    /// <summary>Live (magnified) left edges — rewritten on every measure/arrange.</summary>
    private double[] _offsets = [];

    /// <summary>
    /// Left edges of the un-magnified row. Written only by
    /// <see cref="RebuildRestingGeometry"/>, never by the per-frame path, so the resting
    /// geometry has one owner and cannot be clobbered by a measure mid-drag.
    /// </summary>
    private double[] _restingOffsets = [];
    private double[] _baseWidths = [];
    private double[] _centers = [];

    /// <summary>Resting right edge of the strip; where a "drop past the last icon" indicator sits.</summary>
    private double _restingWidth;

    public DockStrip()
    {
        ClipToBounds = false;

        // A Panel with no Background is not hit-tested outside its children, which would make
        // every IconGap band a dead zone: MouseMove would stop firing there and the magnified
        // peak would freeze between icons. Transparent fills the hit region without painting.
        Background = Brushes.Transparent;
    }

    public double IconSize { get; set; } = DockPlateMetrics.ButtonSize(24d);

    /// <summary>Glyph drawn inside the button. <see cref="IconSize"/> is the button, not the glyph.</summary>
    public double GlyphSize { get; set; } = 24d;

    public double IconGap { get; set; } = DockPlateMetrics.GapDip;

    public double IndicatorGap { get; set; } = 2d;

    /// <summary>Matches the 2 DIP running pill so the plate does not reserve a thick underline.</summary>
    public double IndicatorSize { get; set; } = 2d;

    public double MaxScale { get; set; } = DockMagnification.Default;

    /// <summary>0 at rest, 1 fully magnified. Driven by an animation on enter/leave.</summary>
    public static readonly DependencyProperty PhaseProperty =
        DependencyProperty.Register(
            nameof(Phase),
            typeof(double),
            typeof(DockStrip),
            new PropertyMetadata(0d, OnMagnificationStateChanged));

    public double Phase
    {
        get => (double)GetValue(PhaseProperty);
        set => SetValue(PhaseProperty, value);
    }

    /// <summary>Cursor position in slot units; fractional values interpolate between slots.</summary>
    public static readonly DependencyProperty CursorPositionProperty =
        DependencyProperty.Register(
            nameof(CursorPosition),
            typeof(double),
            typeof(DockStrip),
            new PropertyMetadata(0d, OnMagnificationStateChanged));

    public double CursorPosition
    {
        get => (double)GetValue(CursorPositionProperty);
        set => SetValue(CursorPositionProperty, value);
    }

    public IReadOnlyList<DockItemCell> Cells => _cells;

    public int SlotCount => _cells.Count;

    /// <summary>Button height. The running mark is drawn inside the button, so it adds no row.</summary>
    public double RestingHeight => IconSize;

    /// <summary>
    /// Replaces the slot list, reusing existing cells so hover state, bitmaps, and menus
    /// survive the composer's 2-second poll.
    /// </summary>
    public void SetRows(IReadOnlyList<DockItemViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        while (_cells.Count > rows.Count)
        {
            DockItemCell extra = _cells[^1];
            _cells.RemoveAt(_cells.Count - 1);
            RemoveVisualChild(extra);
        }

        while (_cells.Count < rows.Count)
        {
            var cell = new DockItemCell();
            _cells.Add(cell);
            AddVisualChild(cell);
        }

        for (int i = 0; i < rows.Count; i++)
        {
            _cells[i].Row = rows[i];
        }

        RebuildRestingGeometry();
        InvalidateMeasure();
    }

    /// <summary>
    /// Fractional slot index for a resting-layout x position. Uses the resting centers so
    /// the mapping cannot feed back on the magnified layout it produces.
    /// </summary>
    public double HitTestCursor(double x)
    {
        int count = _cells.Count;
        if (count == 0)
        {
            return 0d;
        }

        if (x <= _centers[0])
        {
            return 0d;
        }

        for (int i = 1; i < count; i++)
        {
            if (x < _centers[i])
            {
                double span = _centers[i] - _centers[i - 1];
                return span <= 0d ? i - 1 : i - 1 + ((x - _centers[i - 1]) / span);
            }
        }

        return count - 1;
    }

    /// <summary>
    /// Insertion index for a drag, using the resting layout so the target does not drift
    /// while magnified. Returns <c>SlotCount</c> to mean "append at the end".
    /// </summary>
    public int ResolveInsertIndex(double x)
    {
        int count = _cells.Count;
        if (count == 0)
        {
            return 0;
        }

        if (x > _centers[count - 1])
        {
            return count;
        }

        double slot = HitTestCursor(x);
        return Math.Clamp((int)Math.Floor(slot + 0.5d), 0, count);
    }

    /// <summary>Resting left edge of a slot; the resting right edge for <c>SlotCount</c>.</summary>
    public double RestingLeftOf(int index) =>
        index < 0 || index >= _restingOffsets.Length || index >= _cells.Count
            ? _restingWidth
            : _restingOffsets[index];

    /// <summary>Live slot widths must fit inside the budget <c>DockWindow</c> sized the window to.</summary>
    public double MaxLiveWidth() => DockStripLayout.MaxExtentWidth(
        Math.Max(SlotCount, 1), IconSize, IconGap, MaxScale);

    protected override Size MeasureOverride(Size availableSize)
    {
        int count = _cells.Count;
        EnsureBuffers(count);

        double height = RestingHeight;
        for (int i = 0; i < count; i++)
        {
            _cells[i].ApplyMetrics(GlyphSize, IconSize, height);
            _cells[i].Measure(new Size(_baseWidths[i], height));
        }

        DockStripLayout.ComputeScales(count, CursorPosition, Phase, MaxScale, _scales);
        DockStripLayout.ComputeOffsets(count, _baseWidths, _scales, IconGap, _offsets, out double live);
        return new Size(live, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = _cells.Count;
        EnsureBuffers(count);
        double height = RestingHeight;

        DockStripLayout.ComputeScales(count, CursorPosition, Phase, MaxScale, _scales);
        DockStripLayout.ComputeOffsets(count, _baseWidths, _scales, IconGap, _offsets, out _);

        for (int i = 0; i < count; i++)
        {
            DockItemCell cell = _cells[i];
            cell.SetMagnification(_scales[i]);
            cell.Arrange(new Rect(_offsets[i], 0, _baseWidths[i] * _scales[i], height));
        }

        return finalSize;
    }

    protected override int VisualChildrenCount => _cells.Count;

    protected override Visual GetVisualChild(int index) => _cells[index];

    private static void OnMagnificationStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DockStrip)d).InvalidateMeasure();

    private void RebuildRestingGeometry()
    {
        int count = _cells.Count;
        EnsureBuffers(count);

        // Every slot is an icon slot: there is no divider row type, so all base widths are the
        // icon width. The buffer stays because ComputeOffsets/MaxExtentWidth are written against it.
        for (int i = 0; i < count; i++)
        {
            _baseWidths[i] = IconSize;
        }

        Array.Fill(_scales, 1.0d);
        DockStripLayout.ComputeOffsets(count, _baseWidths, _scales, IconGap, _restingOffsets, out _restingWidth);

        // Derived from the resting buffer, never from _offsets: _centers drives HitTestCursor and
        // ResolveInsertIndex, and those must not see the magnified layout they sit beside.
        for (int i = 0; i < count; i++)
        {
            _centers[i] = _restingOffsets[i] + (_baseWidths[i] / 2.0d);
        }
    }

    private void EnsureBuffers(int count)
    {
        if (_scales.Length >= count)
        {
            return;
        }

        int size = Math.Max(count, 8);
        _scales = new double[size];
        _offsets = new double[size];
        _restingOffsets = new double[size];
        _baseWidths = new double[size];
        _centers = new double[size];
    }
}
