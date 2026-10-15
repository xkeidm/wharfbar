using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WharfBar.Models;
using WharfBar.Services;
using WharfBar.Views;

namespace WharfBar.Views.Controls;

/// <summary>
/// One dock slot: an app icon with its open-window pill.
/// The cell never decides its own size — <see cref="DockStrip"/> pushes the button and glyph in.
/// The glyph stays at resting size. Hover paints the plate. A restored window keeps that plate
/// and a long accent line. A minimized app keeps only the short neutral line.
/// Plate, line, and badge fade; the line's length eases. The cell's own
/// <see cref="Presence"/> eases the slot in and out.
/// </summary>
internal sealed partial class DockItemCell : UserControl
{
    private const double MetricsEpsilon = 0.01d;

    // NaN = "never applied": every comparison below is false on the first call, so the first
    // ApplyMetrics always writes even if a metric legitimately reaches 0.
    private double _glyphSize = double.NaN;
    private double _buttonSize = double.NaN;
    private readonly ScaleTransform _presenceScale = new(1d, 1d);
    private bool _chromeReady;
    private bool _presenceArmed;
    private int _presenceEpoch;
    private bool _plateShown;
    private bool _plateLatched;
    private int _plateEpoch;
    private bool _pillShown;
    private bool _pillOpen;
    private bool _pillLatched;
    private double _pillWidth = double.NaN;
    private int _pillEpoch;
    private bool _badgeShown;
    private bool _badgeLatched;
    private int _badgeEpoch;
    private DockProgressState _progress = DockProgressState.None;
    private long _attentionUntil;
    private int _pulseEpoch;

    public DockItemCell()
    {
        InitializeComponent();
        Cursor = Cursors.Hand;
        RenderTransform = _presenceScale;
        RenderTransformOrigin = new Point(0.5d, 0.5d);
        ProgressTrack.SizeChanged += (_, _) => PaintProgress();
    }

    /// <summary>
    /// When false, chrome and presence snap. Tests use this so they can read the settled
    /// visibility without waiting out the clock.
    /// </summary>
    internal bool MotionEnabled { get; set; } = true;

    /// <summary>Stable identity assigned by <see cref="DockStrip"/>.</summary>
    internal string PresenceKey { get; set; } = string.Empty;

    /// <summary>True while the slot is shrinking out of the strip.</summary>
    internal bool IsExiting { get; private set; }

    /// <summary>Raised when <see cref="Presence"/> changes so the strip can reflow.</summary>
    internal event EventHandler? PresenceLayoutChanged;

    public static readonly DependencyProperty PresenceProperty =
        DependencyProperty.Register(
            nameof(Presence),
            typeof(double),
            typeof(DockItemCell),
            new PropertyMetadata(1d, OnPresenceChanged));

    /// <summary>0 is a collapsed slot, 1 is a full button. Animated on insert and remove.</summary>
    public double Presence
    {
        get => (double)GetValue(PresenceProperty);
        set => SetValue(PresenceProperty, value);
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

    /// <summary>New slot: start collapsed, then ease to full size.</summary>
    internal void BeginEnter()
    {
        IsExiting = false;
        if (!_presenceArmed)
        {
            _presenceEpoch++;
            BeginAnimation(PresenceProperty, null);
            Presence = 0d;
            _presenceArmed = true;
        }

        AnimatePresenceTo(1d, null);
    }

    /// <summary>Ease the slot shut, then run <paramref name="completed"/> once.</summary>
    internal void BeginExit(Action completed)
    {
        ArgumentNullException.ThrowIfNull(completed);
        IsExiting = true;
        IsHitTestVisible = false;
        _presenceArmed = true;
        AnimatePresenceTo(0d, completed);
    }

    /// <summary>Jump to a presence and drop any in-flight clock. Used for the first layout.</summary>
    internal void SnapPresence(double value)
    {
        _presenceEpoch++;
        IsExiting = false;
        BeginAnimation(PresenceProperty, null);
        Presence = value;
        _presenceArmed = true;
        IsHitTestVisible = !IsExiting && value > 0.35d;
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
            // Bottom margin shifts the centered glyph up into the plate, leaving the lane clear.
            IconGlyph.Margin = new Thickness(0, 0, 0, DockPlateMetrics.IndicatorLaneDip);
            double plate = DockPlateMetrics.HoverPlateSize(buttonSize);
            HoverPlate.Width = plate;
            HoverPlate.Height = plate;
            HoverPlate.CornerRadius = new CornerRadius(DockPlateMetrics.HoverCornerRadiusDip);
            IconScale.ScaleX = 1d;
            IconScale.ScaleY = 1d;
            RunningPill.Height = DockPlateMetrics.PillThicknessDip;
            RunningPill.CornerRadius = new CornerRadius(DockPlateMetrics.PillThicknessDip);
            RunningPill.Margin = new Thickness(0, 0, 0, DockPlateMetrics.PillBottomInsetDip);
            IconImage.Margin = new Thickness(0);
            AttentionPulse.Width = plate;
            AttentionPulse.Height = plate;
            AttentionPulse.CornerRadius = new CornerRadius(DockPlateMetrics.HoverCornerRadiusDip);
        }

        ApplyChrome();
    }

    /// <summary>Swaps the glyph only. Setting <see cref="Row"/> would also drop the menu and tooltip.</summary>
    internal void ReplaceIcon(ImageSource icon)
    {
        if (Row is not null && !ReferenceEquals(IconGlyph.Source, icon))
        {
            IconGlyph.Source = icon;
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

    private static void OnPresenceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var cell = (DockItemCell)d;
        cell.ApplyPresenceVisual((double)e.NewValue);
        cell.PresenceLayoutChanged?.Invoke(cell, EventArgs.Empty);
    }

    private void ApplyPresenceVisual(double presence)
    {
        double t = Math.Clamp(presence, 0d, 1d);
        Opacity = t;
        double scale = DockMotion.ScaleFor(t);
        _presenceScale.ScaleX = scale;
        _presenceScale.ScaleY = scale;
        IsHitTestVisible = !IsExiting && t > 0.35d;
    }

    private void AnimatePresenceTo(double to, Action? onArrived)
    {
        _presenceEpoch++;
        int token = _presenceEpoch;
        _presenceArmed = true;
        double from = Presence;
        if (!MotionEnabled || Math.Abs(from - to) < 0.001d)
        {
            BeginAnimation(PresenceProperty, null);
            Presence = to;
            if (to <= 0.001d)
            {
                onArrived?.Invoke();
            }

            return;
        }

        DockMotionSpec spec = DockMotion.Span(from, to);
        BeginAnimation(PresenceProperty, DockMotion.Create(spec, () =>
        {
            if (token == _presenceEpoch)
            {
                onArrived?.Invoke();
            }
        }));
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        if (Row is not null)
        {
            SetHoverPlate(true, snap: !MotionEnabled || !_chromeReady);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        bool keepPlate = Row is { } row
            && (row.Item.LocalPresence == DockLocalPresence.Open
                || (row.Item.LocalPresence == DockLocalPresence.Unscoped && row.IsOpenOnDesktop));
        SetHoverPlate(keepPlate, snap: !MotionEnabled || !_chromeReady);
    }

    private void ApplyRow()
    {
        DockItemViewModel? row = Row;
        if (row is null)
        {
            IconGlyph.Source = null;
            SetPill(false, false, 0d, snap: true);
            SetBadge(false, null, snap: true);
            SetHoverPlate(false, snap: true);
            ApplyProgress(DockProgressState.None);
            ClearAttention();
            _chromeReady = false;
            return;
        }

        IconImage.Visibility = Visibility.Visible;
        IconGlyph.Source = row.Icon;
        ToolTip = null;
        ContextMenu = null;
        ApplyChrome();
    }

    /// <summary>
    /// Restored window: hover plate plus the long accent line. Minimized: short neutral line,
    /// plate only while the pointer is on the slot. Closed: no line.
    /// </summary>
    private void ApplyChrome()
    {
        DockItemViewModel? row = Row;
        if (row is null)
        {
            SetPill(false, false, 0d, snap: true);
            SetBadge(false, null, snap: true);
            SetHoverPlate(false, snap: true);
            _chromeReady = false;
            return;
        }

        bool open;
        bool showPill;
        switch (row.Item.LocalPresence)
        {
            case DockLocalPresence.Open:
                open = true;
                showPill = true;
                break;
            case DockLocalPresence.Minimized:
                open = false;
                showPill = true;
                break;
            case DockLocalPresence.Absent:
                open = false;
                showPill = false;
                break;
            default:
                open = row.IsOpenOnDesktop;
                showPill = open || row.IsRunning;
                break;
        }
        double width = showPill && !double.IsNaN(_glyphSize)
            ? open
                ? DockPlateMetrics.PillWidth(_glyphSize)
                : DockPlateMetrics.MiniPillWidth(_glyphSize)
            : 0d;
        int count = row.WindowCount;
        bool several = showPill && count > 1;
        string badge = count > 99 ? "99+" : count.ToString();
        bool snap = !MotionEnabled || !_chromeReady;

        SetPill(showPill, open, width, snap);
        SetBadge(several, several ? badge : null, snap);
        // IsMouseOver is live for a row swap under the pointer. Enter/leave set the plate
        // themselves, because a raised mouse event does not update IsMouseOver.
        SetHoverPlate(open || IsMouseOver, snap);

        if (!double.IsNaN(_glyphSize))
        {
            _chromeReady = true;
        }
    }

    private void SetHoverPlate(bool show, bool snap)
    {
        if (!snap && show == _plateShown && _plateLatched)
        {
            return;
        }

        _plateLatched = true;
        _plateShown = show;
        if (snap || !MotionEnabled)
        {
            _plateEpoch++;
            HoverPlate.BeginAnimation(OpacityProperty, null);
            HoverPlate.Opacity = show ? 1d : 0d;
            HoverPlate.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        _plateEpoch++;
        int token = _plateEpoch;
        if (show)
        {
            HoverPlate.Visibility = Visibility.Visible;
            Fade(HoverPlate, 1d, () => token == _plateEpoch, null);
        }
        else
        {
            Fade(HoverPlate, 0d, () => token == _plateEpoch, () =>
            {
                if (!_plateShown)
                {
                    HoverPlate.Visibility = Visibility.Collapsed;
                }
            });
        }
    }

    private void SetBadge(bool show, string? text, bool snap)
    {
        if (show && text is not null)
        {
            WindowCountText.Text = text;
        }

        if (!snap && show == _badgeShown && _badgeLatched)
        {
            return;
        }

        _badgeLatched = true;
        _badgeShown = show;
        if (snap || !MotionEnabled)
        {
            _badgeEpoch++;
            WindowCountBadge.BeginAnimation(OpacityProperty, null);
            WindowCountBadge.Opacity = show ? 1d : 0d;
            WindowCountBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        _badgeEpoch++;
        int token = _badgeEpoch;
        if (show)
        {
            WindowCountBadge.Visibility = Visibility.Visible;
            Fade(WindowCountBadge, 1d, () => token == _badgeEpoch, null);
        }
        else
        {
            Fade(WindowCountBadge, 0d, () => token == _badgeEpoch, () =>
            {
                if (!_badgeShown)
                {
                    WindowCountBadge.Visibility = Visibility.Collapsed;
                }
            });
        }
    }

    private void SetPill(bool show, bool open, double width, bool snap)
    {
        bool widthChanged = double.IsNaN(_pillWidth) || Math.Abs(width - _pillWidth) >= MetricsEpsilon;
        if (!snap && show == _pillShown && !widthChanged && open == _pillOpen && _pillLatched)
        {
            return;
        }

        _pillLatched = true;
        _pillShown = show;
        _pillOpen = open;
        _pillWidth = width;

        if (show)
        {
            ApplyPillBrush(open);
        }

        if (snap || !MotionEnabled)
        {
            _pillEpoch++;
            RunningPill.BeginAnimation(WidthProperty, null);
            RunningPill.BeginAnimation(OpacityProperty, null);
            RunningPill.Width = show ? width : 0d;
            RunningPill.Opacity = show ? 1d : 0d;
            RunningPill.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        _pillEpoch++;
        int token = _pillEpoch;
        if (show)
        {
            RunningPill.Visibility = Visibility.Visible;
            EaseWidth(RunningPill, width);
            if (RunningPill.Opacity < 0.999d)
            {
                Fade(RunningPill, 1d, () => token == _pillEpoch, null);
            }
        }
        else
        {
            EaseWidth(RunningPill, 0d);
            Fade(RunningPill, 0d, () => token == _pillEpoch, () =>
            {
                if (!_pillShown)
                {
                    RunningPill.Visibility = Visibility.Collapsed;
                }
            });
        }
    }

    internal void ApplyProgress(DockProgressState state)
    {
        _progress = state;
        PaintProgress();
    }

    internal void SyncAttention(long untilTick, long nowTick)
    {
        if (untilTick <= nowTick)
        {
            ClearAttention();
            return;
        }

        if (_attentionUntil == untilTick)
        {
            return;
        }

        _attentionUntil = untilTick;
        PlayPulse(Math.Max(1, untilTick - nowTick));
    }

    internal void ClearAttention()
    {
        _attentionUntil = 0;
        _pulseEpoch++;
        AttentionPulse.BeginAnimation(OpacityProperty, null);
        AttentionPulse.Opacity = 0;
        AttentionPulse.Visibility = Visibility.Collapsed;
    }

    private void PlayPulse(long durationMs)
    {
        _pulseEpoch++;
        int token = _pulseEpoch;
        AttentionPulse.BeginAnimation(OpacityProperty, null);
        AttentionPulse.Visibility = Visibility.Visible;
        AttentionPulse.Opacity = 0;
        if (!MotionEnabled)
        {
            AttentionPulse.Opacity = 1;
            return;
        }

        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(durationMs),
        };
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromPercent(0)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(1d, KeyTime.FromPercent(0.35)));
        animation.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromPercent(1)));
        animation.Completed += (_, _) =>
        {
            if (token == _pulseEpoch)
            {
                AttentionPulse.Visibility = Visibility.Collapsed;
            }
        };
        AttentionPulse.BeginAnimation(OpacityProperty, animation);
    }

    private void PaintProgress()
    {
        if (!_progress.IsVisible)
        {
            ProgressTrack.Visibility = Visibility.Collapsed;
            return;
        }

        ProgressTrack.Visibility = Visibility.Visible;
        ProgressFill.Background = ProgressBrush(_progress.Kind);
        ProgressFill.Opacity = _progress.Kind == DockProgressKind.Indeterminate ? 0.45 : 1;
        if (_progress.Percent is double percent && ProgressTrack.ActualWidth > 0)
        {
            ProgressFill.HorizontalAlignment = HorizontalAlignment.Left;
            ProgressFill.Width = ProgressTrack.ActualWidth * (Math.Clamp(percent, 0d, 100d) / 100d);
            return;
        }

        ProgressFill.HorizontalAlignment = HorizontalAlignment.Stretch;
        ProgressFill.ClearValue(WidthProperty);
    }

    private Brush ProgressBrush(DockProgressKind kind)
    {
        string key = kind switch
        {
            DockProgressKind.Error => "SystemFillColorCriticalBrush",
            DockProgressKind.Paused => "SystemFillColorCautionBrush",
            _ => "AccentFillColorDefaultBrush",
        };

        if (TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        if (TryFindResource("AccentFillColorDefaultBrush") is Brush accent)
        {
            return accent;
        }

        return Brushes.DodgerBlue;
    }

    private void ApplyPillBrush(bool open)
    {
        if (TryFindResource(open
                ? "AccentFillColorDefaultBrush"
                : "TextFillColorPrimaryBrush") is Brush brush)
        {
            RunningPill.Background = brush;
        }
    }

    private void Fade(UIElement target, double to, Func<bool> stillCurrent, Action? whenSettled)
    {
        double from = target.Opacity;
        if (double.IsNaN(from))
        {
            from = 0d;
        }

        if (Math.Abs(from - to) < 0.001d)
        {
            target.BeginAnimation(OpacityProperty, null);
            target.Opacity = to;
            whenSettled?.Invoke();
            return;
        }

        DockMotionSpec spec = DockMotion.Fade(from, to);
        target.BeginAnimation(OpacityProperty, DockMotion.Create(spec, () =>
        {
            if (stillCurrent())
            {
                whenSettled?.Invoke();
            }
        }));
    }

    private static void EaseWidth(FrameworkElement target, double to)
    {
        double from = target.Width;
        if (double.IsNaN(from))
        {
            from = 0d;
        }

        if (Math.Abs(from - to) < MetricsEpsilon)
        {
            target.BeginAnimation(WidthProperty, null);
            target.Width = to;
            return;
        }

        target.BeginAnimation(WidthProperty, DockMotion.Create(DockMotion.Span(from, to)));
    }
}
