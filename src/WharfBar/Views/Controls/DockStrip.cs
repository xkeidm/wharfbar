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
/// Slots that appear or disappear ease their presence from 0 to 1, so neighbours slide and
/// the plate width follows <see cref="LiveWidth"/> instead of jumping.
/// </para>
/// <para>
/// The panel reports its live width as <c>DesiredSize</c>. <c>DockWindow</c> sizes the
/// HWND to <see cref="LiveWidth"/> plus plate padding, so the plate eases with the icons.
/// Nothing here calls into Win32.
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
    private double[] _presence = [];

    /// <summary>Resting right edge of the settled strip; where a "drop past the last icon" indicator sits.</summary>
    private double _restingWidth;

    private bool _presented;
    private bool _hasLiveWidth;
    private double _liveWidth;

    public DockStrip()
    {
        ClipToBounds = false;

        // A Panel with no Background is not hit-tested outside its children, which would make
        // every IconGap band a dead zone: MouseMove would stop firing there and the magnified
        // peak would freeze between icons. Transparent fills the hit region without painting.
        Background = Brushes.Transparent;
    }

    /// <summary>
    /// When false, insert and remove snap. The first <see cref="SetRows"/> always snaps so
    /// the dock does not fade in on startup.
    /// </summary>
    internal bool MotionEnabled { get; set; } = true;

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

    /// <summary>Cells currently in the visual tree, including ones that are still easing out.</summary>
    public int SlotCount => _cells.Count;

    /// <summary>Slots that remain after the current exit animations finish.</summary>
    internal int SettledSlotCount { get; private set; }

    /// <summary>Current strip width in DIP, including fractional enter/exit slots.</summary>
    internal double LiveWidth => _liveWidth;

    internal bool HasLiveWidth => _hasLiveWidth;

    /// <summary>Raised when <see cref="LiveWidth"/> changes so the window can ease its plate.</summary>
    internal event EventHandler? LiveWidthChanged;

    /// <summary>Raised when a cell leaves the strip and should drop its input handlers.</summary>
    internal event Action<DockItemCell>? CellReleased;

    /// <summary>Button height. The running mark is drawn inside the button, so it adds no row.</summary>
    public double RestingHeight => IconSize;

    /// <summary>
    /// Replaces the slot list. Cells are reused by app identity so a pinned icon that starts
    /// running keeps its cell and only eases the open-state chrome. New cells ease in; cells
    /// that left ease out in place.
    /// </summary>
    public void SetRows(IReadOnlyList<DockItemViewModel> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        bool animate = _presented && MotionEnabled;
        foreach (DockItemCell cell in _cells)
        {
            cell.MotionEnabled = MotionEnabled;
        }

        var previousKeys = new List<string>(_cells.Count);
        var existing = new Dictionary<string, DockItemCell>(_cells.Count, StringComparer.OrdinalIgnoreCase);
        foreach (DockItemCell cell in _cells)
        {
            previousKeys.Add(cell.PresenceKey);
            if (cell.PresenceKey.Length > 0)
            {
                existing[cell.PresenceKey] = cell;
            }
        }

        var nextKeys = new List<string>(rows.Count);
        var incoming = new Dictionary<string, DockItemViewModel>(rows.Count, StringComparer.OrdinalIgnoreCase);
        foreach (DockItemViewModel row in rows)
        {
            string key = DockPresence.KeyFor(row.Item);
            nextKeys.Add(key);
            incoming[key] = row;
        }

        IReadOnlyList<DockSlotChange> plan = DockPresence.Diff(previousKeys, nextKeys);
        var next = new List<DockItemCell>(plan.Count);
        var retiring = new List<DockItemCell>();

        foreach (DockSlotChange slot in plan)
        {
            existing.TryGetValue(slot.Key, out DockItemCell? cell);
            if (slot.Kind == DockSlotKind.Exit)
            {
                if (cell is null)
                {
                    continue;
                }

                if (!animate || cell.Presence <= 0.001d)
                {
                    // Already invisible: dropping it cannot pop. A synchronous exit
                    // completion here would also mutate the strip mid-diff.
                    retiring.Add(cell);
                    continue;
                }

                next.Add(cell);
                if (!cell.IsExiting)
                {
                    cell.BeginExit(() => ReleaseCell(cell));
                }

                continue;
            }

            if (!incoming.TryGetValue(slot.Key, out DockItemViewModel? row))
            {
                continue;
            }

            if (cell is null)
            {
                cell = new DockItemCell
                {
                    MotionEnabled = MotionEnabled,
                    PresenceKey = slot.Key,
                };
                cell.PresenceLayoutChanged += OnCellPresenceLayoutChanged;
                cell.Row = row;
                if (animate)
                {
                    cell.BeginEnter();
                }
                else
                {
                    cell.SnapPresence(1d);
                }
            }
            else
            {
                cell.PresenceKey = slot.Key;
                cell.Row = row;
                if (cell.IsExiting || cell.Presence < 0.999d)
                {
                    cell.BeginEnter();
                }
            }

            next.Add(cell);
        }

        Adopt(next);
        foreach (DockItemCell cell in retiring)
        {
            Retire(cell);
        }

        RebuildRestingGeometry();
        PublishLiveWidth(ComputeLiveWidth());
        InvalidateMeasure();
        _presented = true;
    }

    /// <summary>
    /// Fractional slot index for a resting-layout x position. Uses the resting centers so
    /// the mapping cannot feed back on the magnified layout it produces.
    /// </summary>
    public double HitTestCursor(double x)
    {
        int count = SettledSlotCount;
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
    /// while magnified. Returns <see cref="SettledSlotCount"/> to mean "append at the end".
    /// Exiting slots are already omitted, so the index matches the settled strip.
    /// </summary>
    public int ResolveInsertIndex(double x)
    {
        int count = SettledSlotCount;
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

    /// <summary>Resting left edge of a settled slot; the resting right edge for <see cref="SettledSlotCount"/>.</summary>
    public double RestingLeftOf(int index) =>
        index < 0 || index >= SettledSlotCount
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
            _presence[i] = _cells[i].Presence;
            _cells[i].ApplyMetrics(GlyphSize, IconSize, height);
            _cells[i].Measure(new Size(Math.Max(IconSize, 0d), height));
        }

        DockStripLayout.ComputeScales(count, CursorPosition, Phase, MaxScale, _scales);
        DockStripLayout.ComputePresenceOffsets(
            count, IconSize, _presence, _scales, IconGap, _offsets, _baseWidths, out double live);
        PublishLiveWidth(live);
        return new Size(Math.Max(live, 0d), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        int count = _cells.Count;
        EnsureBuffers(count);
        double height = RestingHeight;

        DockStripLayout.ComputeScales(count, CursorPosition, Phase, MaxScale, _scales);
        DockStripLayout.ComputePresenceOffsets(
            count, IconSize, _presence, _scales, IconGap, _offsets, _baseWidths, out _);

        for (int i = 0; i < count; i++)
        {
            DockItemCell cell = _cells[i];
            cell.SetMagnification(_scales[i]);
            double slotWidth = _baseWidths[i];
            double drawWidth = IconSize * _scales[i];
            double left = _offsets[i] + ((slotWidth - drawWidth) / 2d);
            cell.Arrange(new Rect(left, 0d, Math.Max(drawWidth, 0d), height));
        }

        return finalSize;
    }

    protected override int VisualChildrenCount => _cells.Count;

    protected override Visual GetVisualChild(int index) => _cells[index];

    private static void OnMagnificationStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DockStrip)d).InvalidateMeasure();

    private void OnCellPresenceLayoutChanged(object? sender, EventArgs e) => InvalidateMeasure();

    private void RebuildRestingGeometry()
    {
        int settled = 0;
        for (int i = 0; i < _cells.Count; i++)
        {
            if (!_cells[i].IsExiting)
            {
                settled++;
            }
        }

        EnsureBuffers(Math.Max(_cells.Count, settled));
        int write = 0;
        for (int i = 0; i < _cells.Count; i++)
        {
            if (_cells[i].IsExiting)
            {
                continue;
            }

            _presence[write] = 1d;
            _scales[write] = 1d;
            write++;
        }

        DockStripLayout.ComputePresenceOffsets(
            settled,
            IconSize,
            _presence,
            _scales,
            IconGap,
            _restingOffsets,
            _baseWidths,
            out _restingWidth);

        for (int i = 0; i < settled; i++)
        {
            _centers[i] = _restingOffsets[i] + (IconSize / 2d);
        }

        SettledSlotCount = settled;
    }

    private double ComputeLiveWidth()
    {
        int count = _cells.Count;
        EnsureBuffers(count);
        for (int i = 0; i < count; i++)
        {
            _presence[i] = _cells[i].Presence;
            _scales[i] = 1d;
        }

        DockStripLayout.ComputePresenceOffsets(
            count, IconSize, _presence, _scales, IconGap, _offsets, _baseWidths, out double total);
        return total;
    }

    private void PublishLiveWidth(double width)
    {
        if (_hasLiveWidth && Math.Abs(_liveWidth - width) < 0.01d)
        {
            return;
        }

        _liveWidth = width;
        _hasLiveWidth = true;
        LiveWidthChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Adopt(List<DockItemCell> next)
    {
        bool same = _cells.Count == next.Count;
        if (same)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                if (!ReferenceEquals(_cells[i], next[i]))
                {
                    same = false;
                    break;
                }
            }
        }

        if (same)
        {
            return;
        }

        foreach (DockItemCell cell in _cells)
        {
            RemoveVisualChild(cell);
        }

        _cells.Clear();
        foreach (DockItemCell cell in next)
        {
            _cells.Add(cell);
            AddVisualChild(cell);
        }
    }

    private void Retire(DockItemCell cell)
    {
        cell.PresenceLayoutChanged -= OnCellPresenceLayoutChanged;
        CellReleased?.Invoke(cell);
    }

    private void ReleaseCell(DockItemCell cell)
    {
        cell.PresenceLayoutChanged -= OnCellPresenceLayoutChanged;
        if (!_cells.Remove(cell))
        {
            return;
        }

        RemoveVisualChild(cell);
        CellReleased?.Invoke(cell);
        RebuildRestingGeometry();
        PublishLiveWidth(ComputeLiveWidth());
        InvalidateMeasure();
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
        _presence = new double[size];
    }
}
