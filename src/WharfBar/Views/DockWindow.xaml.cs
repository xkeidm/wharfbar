using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Services;
using WharfBar.Views.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using MenuItem = Wpf.Ui.Controls.MenuItem;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace WharfBar.Views;

/// <summary>
/// macOS-style dock overlay anchored to the primary monitor work-area bottom.
/// Not an AppBar — placement uses physical-pixel SetWindowPos only.
/// </summary>
public partial class DockWindow : Window
{
    /// <summary>Vertical padding around icons (DIP).</summary>
    private const double VerticalPaddingDip = 6d;

    /// <summary>Horizontal inset inside the dock surface (DIP).</summary>
    private const double HorizontalPaddingDip = 12d;

    /// <summary>Gap between adjacent icons (DIP). Wider than early 4 DIP — less cramped.</summary>
    private const double IconGapDip = 12d;

    /// <summary>Height of the W11-style running pill (DIP).</summary>
    private const double RunningIndicatorSizeDip = 4d;

    /// <summary>Gap between icon bottom and the running pill (DIP).</summary>
    private const double RunningIndicatorGapDip = 2d;

    /// <summary>
    /// Gap between dock window bottom and the primary monitor's bottom edge (DIP).
    /// Kept at 0 so the strip sits on the screen edge (work-area bottom often leaves a taskbar-sized hole
    /// while Shell_TrayWnd is hidden).
    /// </summary>
    private const double BottomGapDip = 0d;

    private const int AutoHideAnimMs = 200;
    private const double MinWidthDip = 180d;

    /// <summary>Magnification enter/exit durations (ms).</summary>
    private const int PhaseInMs = 130;

    private const int PhaseOutMs = 180;

    /// <summary>Cursor catch-up frame interval (ms).</summary>
    private const int CursorFrameMs = 16;

    /// <summary>Tooltip timings kept from the pre-redesign dock (spec §6.3/§9).</summary>
    private const int ToolTipInitialShowDelayMs = 250;

    private const int ToolTipShowDurationMs = 8000;

    /// <summary>Below this distance the phase is treated as already reached.</summary>
    private const double PhaseSettleEpsilon = 1e-3d;

    /// <summary>Mica translucency over the theme color (0–255 alpha).</summary>
    private const byte MicaDarkAlpha = 0x99;

    private const byte MicaLightAlpha = 0xD9;

    /// <summary>Acrylic surface translucency (0–255 alpha over the theme color).</summary>
    private const byte AcrylicDarkAlpha = 0xCC;

    private const byte AcrylicLightAlpha = 0xE6;

    private readonly AppConfig _config;
    private readonly TaskbarVisibilityService _taskbar;
    private readonly DockStrip _strip;
    private double _iconSizeDip;

    /// <summary>Last <see cref="DockConfig.ShowRunningApps"/> value pushed into the composer.</summary>
    private bool _appliedShowRunningApps;

    /// <summary>Last magnification ceiling pushed into the strip.</summary>
    private double _appliedMagnification;

    private readonly DockAutoHideController _autoHide;
    private HwndSource? _source;
    private readonly AppIconService _icons = new();
    private readonly ProcessLauncherService _launcher = new();
    private RunningAppsSource? _runningSource;
    private MinimizedWindowsSource? _minimizedSource;
    private DockAppsComposer? _composer;
    private WindowActivationService? _activation;

    /// <summary>
    /// Live DPI for this window. Starts at <see cref="MonitorInfo.Dpi"/> and is refreshed
    /// from <c>WM_DPICHANGED</c> so a runtime scale change does not leave the strip sized
    /// for the old DPI.
    /// </summary>
    private uint _monitorDpi;

    private readonly DispatcherTimer _cursorTimer;
    private double _cursorTarget;
    private long _lastCursorFrameMs;
    private bool _isFullscreenHidden;
    private bool _isApplyingPlacement;
    private bool _isAnimatingY;
    private int _lastPlacementX;
    private int _lastPlacementWidthPx;
    private int _lastPlacementHeightPx;
    private int _lastVisibleY;
    private int _lastHiddenY;
    private bool _haveSlideRange;

    // --- Click vs. drag arbitration -------------------------------------------------
    // One left button, two gestures: a click launches, a drag reorders. The drag is the one
    // that needs bookkeeping, because the two share both their button and (after a reorder
    // drop) possibly their cell: the drop mutates the order, the composer's Changed rebuilds
    // the strip, and DockStrip.SetRows re-maps the reused cell objects by index. So nothing
    // below may compare against the row the cell holds at release time.

    /// <summary>Press point of the current left-drag, in window coordinates.</summary>
    private Point _dragOrigin;

    /// <summary>
    /// Store key of the tile under the cursor while the left button is down, or null when the
    /// gesture cannot become a reorder drag. Cleared the moment the drag starts. This is the
    /// OLE drag payload; <see cref="_suppressNextClick"/> is what arbitrates the click.
    /// </summary>
    private string? _dragItemId;

    /// <summary>
    /// Set once a drag has ended, so the <c>MouseLeftButtonUp</c> that ends it is swallowed
    /// instead of launching the app. Deliberately keyless: the dragged tile's row moves under
    /// the cursor during the drag, so a key comparison in the release handler would miss
    /// whenever the drop reorders the very cell the release lands on.
    /// <para>
    /// Lifecycle: cleared on every fresh press (a drag whose release is never delivered must
    /// not eat the next click), set unconditionally after <c>DoDragDrop</c> returns — a
    /// refused drag is still a drag — and consumed by the first release.
    /// </para>
    /// </summary>
    private bool _suppressNextClick;

    private static readonly DependencyProperty AnimYOffsetProperty =
        DependencyProperty.Register(
            nameof(AnimYOffset),
            typeof(double),
            typeof(DockWindow),
            new PropertyMetadata(0d, OnAnimYOffsetChanged));

    private double AnimYOffset
    {
        get => (double)GetValue(AnimYOffsetProperty);
        set => SetValue(AnimYOffsetProperty, value);
    }

    private static void OnAnimYOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DockWindow)d).SetWindowY((int)Math.Round((double)e.NewValue));
    }

    internal DockWindow(AppConfig config, TaskbarVisibilityService taskbar, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(taskbar);

        Monitor = monitor;
        _monitorDpi = monitor.Dpi;
        InitializeComponent();
        _strip = Strip;

        _config = config;
        _taskbar = taskbar;
        _iconSizeDip = Math.Clamp(
            config.Dock.IconSize <= 0 ? DockConfig.DefaultIconSize : config.Dock.IconSize,
            DockConfig.MinIconSize,
            DockConfig.MaxIconSize);
        _autoHide = new DockAutoHideController(config.Dock.AutoHide, Dispatcher);

        // Seed the "what has already been applied" baselines so the first ApplyConfigChanges
        // does not mistake the current config for a change.
        _appliedShowRunningApps = config.Dock.ShowRunningApps;
        _appliedMagnification = DockMagnification.Clamp(config.Dock.Magnification);

        _cursorTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(CursorFrameMs),
        };
        _cursorTimer.Tick += OnCursorFrame;

        ApplyStripGeometry();

        // Keep XAML padding in sync with the height budget used by SetWindowPos.
        Surface.Padding = new Thickness(
            HorizontalPaddingDip,
            VerticalPaddingDip,
            HorizontalPaddingDip,
            VerticalPaddingDip);
    }

    /// <summary>The monitor this dock window is anchored to.</summary>
    internal MonitorInfo Monitor { get; }

    /// <summary>
    /// Re-applies config-driven settings (icon size, auto-hide, magnification, surface,
    /// running-app visibility).
    /// </summary>
    internal void ApplyConfigChanges()
    {
        ApplyIconSizeFromConfig();
        ApplySurfaceFromConfig();
        ApplyRunningAndMagnificationChanges();

        bool wasEnabled = _autoHide.Enabled;
        _autoHide.SetEnabled(_config.Dock.AutoHide);

        if (_source is null)
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        if (_config.Dock.AutoHide)
        {
            _autoHide.Start(BuildEdgeProbe);
            if (!wasEnabled && TryGetPlacementGeometry(out PlacementGeometry geo))
            {
                SetWindowRect(geo.X, geo.HiddenY, geo.WidthPx, geo.HeightPx);
                DockHitTest.ApplyPassthrough(hwnd);
                _autoHide.AssumeHidden();
            }
            else if (wasEnabled)
            {
                // Icon size / height may have changed while auto-hide stays on.
                ApplyPlacement();
            }
        }
        else
        {
            CancelYAnimation();
            DockHitTest.ApplyInteractive(hwnd);
            ApplyPlacement();
        }

        ApplyWorkAreaReservation();

        // Fullscreen + auto-hide toggle: if auto-hide turned OFF while a fullscreen app is
        // active, the dock was collapsed for the fullscreen — re-show it (it now reserves).
        if (_isFullscreenHidden && !_config.Dock.AutoHide)
        {
            OnFullscreenChanged(this, EventArgs.Empty);
        }
    }

    private void ApplyIconSizeFromConfig()
    {
        double next = Math.Clamp(
            _config.Dock.IconSize <= 0 ? DockConfig.DefaultIconSize : _config.Dock.IconSize,
            DockConfig.MinIconSize,
            DockConfig.MaxIconSize);

        if (Math.Abs(next - _iconSizeDip) < 0.01)
        {
            return;
        }

        _iconSizeDip = next;

        Surface.Padding = new Thickness(
            HorizontalPaddingDip,
            VerticalPaddingDip,
            HorizontalPaddingDip,
            VerticalPaddingDip);

        // Before RebuildStrip: SetRows bakes slot widths from the strip's current IconSize,
        // so pushing the new metrics afterwards would leave the layout one icon size stale.
        ApplyStripGeometry();

        if (_composer is not null)
        {
            RebuildStrip();
        }
    }

    /// <summary>
    /// Pushes the two dock settings nothing else announces: the magnification ceiling and the
    /// running-apps toggle. Both are diffed against the last applied value because
    /// <see cref="ApplyConfigChanges"/> runs on <em>every</em> settings save, including the ones
    /// that touch neither.
    /// </summary>
    private void ApplyRunningAndMagnificationChanges()
    {
        double ceiling = DockMagnification.Clamp(_config.Dock.Magnification);
        if (_composer is not null && Math.Abs(ceiling - _appliedMagnification) >= 0.001d)
        {
            _appliedMagnification = ceiling;

            // The ceiling is DockStrip.MaxScale, a plain CLR property: setting it re-measures
            // nothing and leaves every icon rastered for the old one. ApplyIconSizeFromConfig
            // early-returns while the DIP icon size is unchanged, so a magnification-only save
            // never reaches the strip at all. RebuildStrip is the one path that both invalidates
            // the measure (SetRows) and re-requests each icon at the new ceiling.
            ApplyStripGeometry();
            RebuildStrip();
        }

        // DockConfig is plain data with no change notification, and the composer — which owns the
        // Running/Minimized sections — only rebuilds when one of its sources raises Changed. The
        // store is that relay: Reload raises Changed with the list itself untouched, and the
        // composer's ItemsEqual check then keeps a toggle that moves nothing off the strip.
        if (_composer is not null && _appliedShowRunningApps != _config.Dock.ShowRunningApps)
        {
            _appliedShowRunningApps = _config.Dock.ShowRunningApps;
            ItemStore?.Reload();
        }
    }

    /// <summary>
    /// The dock surface is drawn by WPF (layered window), so DWM backdrops are off the table
    /// (docs/04-gotchas.md:7). Mica is a translucent theme tint; Acrylic is denser. Both follow
    /// the system light/dark preference.
    /// </summary>
    private void ApplySurfaceFromConfig()
    {
        bool acrylic = string.Equals(_config.Dock.Surface, DockSurfaceStyles.Acrylic, StringComparison.OrdinalIgnoreCase);
        bool dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        byte baseRgb = dark ? (byte)0x20 : (byte)0xFA;
        byte alpha = acrylic
            ? (dark ? AcrylicDarkAlpha : AcrylicLightAlpha)
            : (dark ? MicaDarkAlpha : MicaLightAlpha);

        var brush = new SolidColorBrush(Color.FromArgb(alpha, baseRgb, baseRgb, baseRgb));
        brush.Freeze();
        Surface.Background = brush;
    }

    /// <summary>
    /// The one dock item store, owned by <see cref="App"/>. A property, not a field: every dock
    /// window and the settings page must share the instance, so a settings edit reaches the
    /// strip immediately instead of only after the next launch.
    /// </summary>
    private DockItemStore? ItemStore => ((App)Application.Current).DockItems;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _source = (HwndSource?)PresentationSource.FromVisual(this);
        if (_source is null)
        {
            return;
        }

        _source.AddHook(WndProc);

        IntPtr hwnd = _source.Handle;
        WindowOverlayStyles.ExcludeFromTaskSwitcher(hwnd);

        // Layered window: keep the composition surface clear so only the strip is visible.
        _source.CompositionTarget.BackgroundColor = Colors.Transparent;
        ApplySurfaceFromConfig();

        DockHitTest.ApplyInteractive(hwnd);

        FullscreenState.Instance.Changed += OnFullscreenChanged;
        WorkAreaChanged.Raised += OnWorkAreaChanged;

        DockItemStore store = ItemStore
            ?? throw new InvalidOperationException("App.DockItems is not initialized yet.");
        _runningSource = new RunningAppsSource(Dispatcher);
        _minimizedSource = new MinimizedWindowsSource(Dispatcher);
        _composer = new DockAppsComposer(store, _runningSource, _minimizedSource, _config.Dock);
        _activation = new WindowActivationService();
        _composer.Changed += OnComposerChanged;

        ApplyStripGeometry();
        Surface.MouseMove += OnSurfaceMouseMove;
        Surface.MouseLeave += OnSurfaceMouseLeave;

        RebuildStrip();

        _autoHide.Changed += OnAutoHideChanged;
        if (_autoHide.Enabled)
        {
            if (TryGetPlacementGeometry(out PlacementGeometry geo))
            {
                SetWindowRect(geo.X, geo.HiddenY, geo.WidthPx, geo.HeightPx);
                DockHitTest.ApplyPassthrough(hwnd);
                _autoHide.AssumeHidden();
            }

            _autoHide.Start(BuildEdgeProbe);
        }

        ApplyWorkAreaReservation();

        if (FullscreenState.Instance.IsFullscreen)
        {
            OnFullscreenChanged(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Reserves the dock strip at the target monitor's work-area bottom when the dock is
    /// always visible (auto-hide off), so maximized apps stop at the dock's top edge. In
    /// auto-hide mode nothing is reserved and maximized apps cover the full bottom.
    /// </summary>
    private void ApplyWorkAreaReservation()
    {
        if (_source is null || _autoHide.Enabled || !TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            _taskbar.SetDockReservation(Monitor, 0);
            return;
        }

        uint dpi = Display.GetDpi(_source.Handle);
        int bottomGapPx = Display.DipToPixels(BottomGapDip, dpi);
        _taskbar.SetDockReservation(Monitor, geo.HeightPx + bottomGapPx);
    }

    protected override void OnClosed(EventArgs e)
    {
        FullscreenState.Instance.Changed -= OnFullscreenChanged;
        WorkAreaChanged.Raised -= OnWorkAreaChanged;

        if (_composer is not null)
        {
            _composer.Changed -= OnComposerChanged;
            _composer.Dispose();
            _composer = null;
        }

        _runningSource?.Dispose();
        _runningSource = null;
        _minimizedSource?.Dispose();
        _minimizedSource = null;
        _activation = null;

        _autoHide.Changed -= OnAutoHideChanged;
        _autoHide.Dispose();

        Surface.MouseMove -= OnSurfaceMouseMove;
        Surface.MouseLeave -= OnSurfaceMouseLeave;
        _cursorTimer.Tick -= OnCursorFrame;
        _cursorTimer.Stop();

        _source?.RemoveHook(WndProc);
        _source = null;

        base.OnClosed(e);
    }

    private void OnComposerChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RebuildStrip);

    private void OnSurfaceMouseMove(object sender, MouseEventArgs e)
    {
        // Hover is tracked on Surface, not Strip: Surface owns the 12/6 padding, and Strip's
        // Background only fills between the slots. Hooking Strip made the padding ring at both
        // ends of the plate a leave zone, so the dock collapsed while the pointer was still on it.
        // The coordinate space is unchanged though — GetPosition(Strip) still measures from the
        // strip's own left edge, which is what HitTestCursor indexes against.
        _cursorTarget = Strip.HitTestCursor(e.GetPosition(Strip).X);

        if (!_cursorTimer.IsEnabled)
        {
            _lastCursorFrameMs = Environment.TickCount64;
            _cursorTimer.Start();
        }

        AnimatePhase(1d, PhaseInMs);
    }

    private void OnSurfaceMouseLeave(object sender, MouseEventArgs e)
    {
        // The pointer leaving the dock also ends a reorder gesture: a drag released outside
        // never raises Drop, so the drop indicator has to be taken down here.
        HideDropIndicator();

        _cursorTimer.Stop();
        AnimatePhase(0d, PhaseOutMs);
    }

    private void OnCursorFrame(object? sender, EventArgs e)
    {
        long now = Environment.TickCount64;
        double dt = now - _lastCursorFrameMs;
        _lastCursorFrameMs = now;

        double next = DockMagnification.Eased(Strip.CursorPosition, _cursorTarget, dt);
        if (Math.Abs(next - _cursorTarget) < 0.001d)
        {
            Strip.CursorPosition = _cursorTarget;
            _cursorTimer.Stop();
            return;
        }

        Strip.CursorPosition = next;
    }

    private void AnimatePhase(double to, int milliseconds)
    {
        // MouseMove fires at pointer rate and re-targeting an already-reached value allocates
        // an animation per event for nothing. The epsilon is far below one screen pixel of
        // layout drift, so skipping is invisible.
        if (Math.Abs(Strip.Phase - to) < PhaseSettleEpsilon)
        {
            return;
        }

        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Strip.BeginAnimation(DockStrip.PhaseProperty, animation);
    }

    private void RebuildStrip()
    {
        DockAppsComposer? composer = _composer;
        if (composer is null)
        {
            // OnComposerChanged queues this on the dispatcher, and a queued rebuild can outlive
            // the dock it was queued for: MonitorManager.Reconcile closes a dock whose monitor
            // went away (unplug, re-arrange) while the process keeps running. Nothing at App
            // level catches DispatcherUnhandledException, so this guard is the only thing
            // between that race and a NullReferenceException that kills the app.
            return;
        }

        IReadOnlyList<DockAppItem> items = composer.Items;
        IReadOnlyList<DockStripRow> rows = DockStripBuilder.Build(items);
        int iconPx = ResolveIconPixelSize();

        var viewModels = new List<DockItemViewModel>(rows.Count);
        foreach (DockStripRow row in rows)
        {
            viewModels.Add(row.IsSeparator
                ? DockItemViewModel.ForSeparator()
                : DockItemViewModel.ForItem(row.Item!, _icons.GetIcon(row.Item!.Path, iconPx)));
        }

        _strip.SetRows(viewModels);

        // One block, on purpose: a cell reused from the previous rebuild must always get a
        // fresh tooltip and menu, otherwise a stale menu leaks onto a different app.
        // The detach-first is load-bearing, not boilerplate: SetRows reuses cell instances and
        // this loop runs on every rebuild the composer's ~2 s poll triggers, so a bare +=
        // would stack one more handler per rebuild. Same rule as the island pill: binding a
        // subset in two steps silently leaves an element dead (see TopBarWindow.xaml:156-157).
        foreach (DockItemCell cell in _strip.Cells)
        {
            DetachCellHandlers(cell);

            DockItemViewModel? row = cell.Row;
            if (row is null || row.IsSeparator)
            {
                continue;
            }

            cell.ToolTip = CreateDockTip(row.Tooltip, cell);

            // Spec §6.3/§9: the user picked "sistem tooltip" as it behaves today, so these two
            // timings are part of the approved look, not incidental defaults.
            ToolTipService.SetInitialShowDelay(cell, ToolTipInitialShowDelayMs);
            ToolTipService.SetShowDuration(cell, ToolTipShowDurationMs);

            cell.ContextMenu = BuildContextMenuForItem(row.Item!);

            // Every tile launches on click; only pinned tiles reorder.
            cell.PreviewMouseLeftButtonDown += OnCellPreviewMouseLeftButtonDown;
            cell.MouseLeftButtonUp += OnCellMouseLeftButtonUp;
            cell.PreviewMouseMove += OnCellMouseMove;

            // Reorder and drop are store operations keyed by the pinned path; running and
            // minimized tiles carry a non-pinned id the store cannot move.
            if (ReorderKeyFor(row) is not null)
            {
                cell.AllowDrop = true;
                cell.DragOver += OnCellDragOver;
                cell.Drop += OnCellDrop;
            }
        }

        ApplyPlacement();
    }

    private void DetachCellHandlers(DockItemCell cell)
    {
        cell.PreviewMouseLeftButtonDown -= OnCellPreviewMouseLeftButtonDown;
        cell.MouseLeftButtonUp -= OnCellMouseLeftButtonUp;
        cell.PreviewMouseMove -= OnCellMouseMove;
        cell.DragOver -= OnCellDragOver;
        cell.Drop -= OnCellDrop;

        // A cell that was pinned in the previous rebuild and is a plain running tile now must
        // stop being a drop target, or it would keep accepting reorder drops it cannot honour.
        cell.AllowDrop = false;
    }

    /// <summary>
    /// The store key of a reorderable tile, or null when the tile cannot be reordered.
    /// <para>
    /// Deliberately the normalized path, never the tile's own id (<see cref="DockAppItem.Id"/>):
    /// on a pinned-and-running tile the composer fills that id from the running app (an
    /// AppUserModelId for a Store/UWP app), and <see cref="DockItemStore"/> keys on the normalized
    /// path — an id-keyed Move would silently miss exactly the tiles that are running, i.e. the
    /// ones users drag most.
    /// </para>
    /// </summary>
    private static string? ReorderKeyFor(DockItemViewModel row)
    {
        if (row.IsSeparator || row.Item is not { IsPinned: true } item)
        {
            return null;
        }

        string key = DockItemStore.Normalize(item.Path);
        return key.Length > 0 ? key : null;
    }

    private void OnCellPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DockItemCell { Row: { IsSeparator: false } row })
        {
            return;
        }

        // A flag from a drag whose release was never delivered must not eat this
        // click: the suppression is only meaningful between a drag's end and its own release.
        _suppressNextClick = false;

        // Foreground must be sampled before anything else touches focus (docs/04-gotchas.md:30).
        _activation?.CaptureForegroundHint();

        _dragOrigin = e.GetPosition(null);
        _dragItemId = ReorderKeyFor(row);
    }

    private void OnCellMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DockItemCell { Row: { IsSeparator: false } row })
        {
            return;
        }

        _dragItemId = null;

        // Keyless on purpose: after a reorder drop the cell under the cursor may already hold
        // a different tile, so no key comparison here could be trusted.
        if (_suppressNextClick)
        {
            _suppressNextClick = false;
            return;
        }

        _activation?.LaunchOrActivate(row.Item!, _launcher);
    }

    private void OnCellMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton is not MouseButtonState.Pressed || _dragItemId is null)
        {
            return;
        }

        Point position = e.GetPosition(null);
        double dx = position.X - _dragOrigin.X;
        double dy = position.Y - _dragOrigin.Y;
        if (Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        string dragged = _dragItemId;
        _dragItemId = null;

        // Rest the strip while reordering: the drop indicator is anchored to the resting
        // layout, and a magnified strip under the cursor is just noise mid-drag.
        AnimatePhase(0d, PhaseOutMs);

        _ = DragDrop.DoDragDrop((DependencyObject)sender, dragged, DragDropEffects.Move);

        // Escape, or a release outside the strip: no Drop is ever raised, so the indicator is
        // taken down here rather than in the Drop handler.
        HideDropIndicator();

        // Suppress the release even when the drop was refused — the gesture became a drag, so
        // the matching MouseLeftButtonUp must not read as a click and launch the app.
        _suppressNextClick = true;
    }

    private void OnCellDragOver(object sender, DragEventArgs e)
    {
        // A file drop belongs to the surface, not to a reorder: leave it unhandled so it
        // bubbles up to OnSurfaceDragOver, which is the one that offers Copy.
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.StringFormat) is not string dragged || dragged.Length == 0)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            HideDropIndicator();
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        ShowDropIndicator(Strip.ResolveInsertIndex(e.GetPosition(Strip).X));
    }

    private void OnCellDrop(object sender, DragEventArgs e)
    {
        HideDropIndicator();

        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.StringFormat) is not string dragged
            || dragged.Length == 0)
        {
            return;
        }

        ItemStore?.Move(dragged, Strip.ResolveInsertIndex(e.GetPosition(Strip).X));
        e.Handled = true;
    }

    private void OnSurfaceDragOver(object sender, DragEventArgs e)
    {
        // A layered Topmost window may not be offered OLE drops at all; then Effects stays
        // None and no drag is accepted. RegisterDragDrop is per-HWND, so that failure takes
        // the reorder down with it — not just the file drop (docs/04-gotchas.md).
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        // Not a drop target here — an icon gap, the plate margin, a separator. Nothing can be
        // dropped on it, so the reorder line must come down instead of pointing at an insert
        // slot the cursor is not over.
        HideDropIndicator();
        e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSurfaceDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) is false
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        // A multi-select drop is partly rejected by design: unsupported extensions resolve to an
        // empty path and duplicates are refused by the store, so only some of the paths land.
        int added = 0;
        foreach (string path in paths)
        {
            (string resolvedPath, string args) = DockDropFormats.Resolve(path);
            if (ItemStore?.TryAdd(resolvedPath, args, string.Empty) == true)
            {
                added++;
            }
        }

        Debug.WriteLine($"[WharfBar] dock drop: {added}/{paths.Length} added");
        e.Handled = true;
    }

    private void ShowDropIndicator(int index)
    {
        if (index < 0
            || index > Strip.SlotCount
            || VisualTreeHelper.GetParent(DropIndicator) is not UIElement host)
        {
            HideDropIndicator();
            return;
        }

        // RestingLeftOf, not a live offset: it reads the strip's resting buffer, so the line
        // marks where the tile lands in the resting order whatever phase the strip happens to
        // be at. (The 180 ms rest-out on drag start is a nicety; this is the guarantee.)
        Point left = Strip.TranslatePoint(new Point(Strip.RestingLeftOf(index), 0), host);

        // Height follows the strip's resting content height, and the bottom inset is the
        // plate's own VerticalPaddingDip: the line is bottom-aligned to the *window*, which is
        // VerticalPaddingDip lower than the strip's baseline. Without the inset the line would
        // hang a few DIP below the icons it is measuring.
        DropIndicator.Height = Strip.RestingHeight;
        DropIndicator.Margin = new Thickness(left.X, 0, 0, VerticalPaddingDip);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator() => DropIndicator.Visibility = Visibility.Collapsed;

    private static ToolTip CreateDockTip(string text, UIElement target) => new()
    {
        Content = text,
        Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
        PlacementTarget = target,
        CustomPopupPlacementCallback = CenterAboveTarget,
    };

    private static System.Windows.Controls.Primitives.CustomPopupPlacement[] CenterAboveTarget(
        Size tipSize, Size targetSize, Point offset)
    {
        return
        [
            new System.Windows.Controls.Primitives.CustomPopupPlacement(
                new Point((targetSize.Width - tipSize.Width) / 2, -tipSize.Height - 25),
                System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
        ];
    }

    private ContextMenu BuildContextMenuForItem(DockAppItem item)
    {
        var menu = new ContextMenu();

        // 1. Header (App Icon + App Name)
        int sizePx = ResolveIconPixelSize();
        var headerItem = new MenuItem
        {
            IsEnabled = false,
            Focusable = false,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 4, 8, 4),
        };
        var headerStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var headerIcon = new System.Windows.Controls.Image
        {
            Width = 18,
            Height = 18,
            Stretch = Stretch.Uniform,
            Source = string.IsNullOrWhiteSpace(item.Path)
                ? _icons.GetIcon(string.Empty, sizePx)
                : _icons.GetIcon(item.Path, sizePx),
            Margin = new Thickness(0, 0, 8, 0),
        };
        RenderOptions.SetBitmapScalingMode(headerIcon, BitmapScalingMode.HighQuality);
        var headerText = new TextBlock
        {
            Text = item.Label,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextFillColorPrimaryBrush"),
        };
        headerStack.Children.Add(headerIcon);
        headerStack.Children.Add(headerText);
        headerItem.Header = headerStack;
        menu.Items.Add(headerItem);
        menu.Items.Add(new Separator());

        // 2. Pin / Unpin
        var pinItem = new MenuItem
        {
            Header = item.IsPinned ? "Dock'tan kaldır" : "Dock'a sabitle",
            Icon = CreateMenuIcon(item.IsPinned ? SymbolRegular.PinOff24 : SymbolRegular.Pin24),
        };
        pinItem.Click += (_, _) => TogglePin(item, pin: !item.IsPinned);
        menu.Items.Add(pinItem);

        // 3. Görevi sonlandır (Force Quit)
        var forceQuitItem = new MenuItem
        {
            Header = "Görevi sonlandır",
            Icon = CreateMenuIcon(SymbolRegular.DismissCircle24),
            IsEnabled = item.IsRunning && item.MainWindowHandle != IntPtr.Zero,
        };
        forceQuitItem.Click += (_, _) => ForceQuitApp(item);
        menu.Items.Add(forceQuitItem);

        // 4. Pencereyi kapat (Close Window)
        var closeItem = new MenuItem
        {
            Header = "Pencereyi kapat",
            Icon = CreateMenuIcon(SymbolRegular.Dismiss24),
            IsEnabled = item.IsRunning && item.MainWindowHandle != IntPtr.Zero,
        };
        closeItem.Click += (_, _) => CloseAppWindow(item);
        menu.Items.Add(closeItem);

        // 5. Additional actions if path exists
        if (!string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
        {
            menu.Items.Add(new Separator());

            var runAdminItem = new MenuItem
            {
                Header = "Yönetici olarak çalıştır",
                Icon = CreateMenuIcon(SymbolRegular.Shield24),
            };
            runAdminItem.Click += (_, _) => _launcher.LaunchAsAdmin(item.Path, item.Args);
            menu.Items.Add(runAdminItem);

            var openFolderItem = new MenuItem
            {
                Header = "Dosya konumunu aç",
                Icon = CreateMenuIcon(SymbolRegular.Folder24),
            };
            openFolderItem.Click += (_, _) => OpenFileLocation(item.Path);
            menu.Items.Add(openFolderItem);

            var newInstanceItem = new MenuItem
            {
                Header = "Yeni pencere aç",
                Icon = CreateMenuIcon(SymbolRegular.Add24),
            };
            newInstanceItem.Click += (_, _) => _launcher.Launch(item.Path, item.Args);
            menu.Items.Add(newInstanceItem);
        }

        // 6. Pencereler — the cached window table only. EnumWindows on the UI thread stalls on a
        // hung HWND, and the menu opens on the UI thread. Gated on IsRunning, not on Section: the
        // composer marks pinned-and-running tiles IsRunning with a live handle too
        // (DockAppsComposer.cs:84-87), and those are the tiles a user actually right-clicks.
        if (item.IsRunning && item.MainWindowHandle != IntPtr.Zero)
        {
            // The return value is the owning thread id; the out-param is the process id — only
            // the latter can match AppWindowInfo.Pid.
            uint threadId = NativeMethods.GetWindowThreadProcessId(item.MainWindowHandle, out uint pid);
            List<AppWindowInfo> windows = [];
            if (threadId != 0)
            {
                foreach (AppWindowInfo candidate in MinimizedWindowsSource.ForProcess(_minimizedSource?.Windows ?? [], pid))
                {
                    // A minimized tile *is* the window it stands for, so that handle is already
                    // on screen; listing it back is noise, and dropping it here is also what
                    // keeps an app with only that one window from showing an empty submenu.
                    if (item.Section == DockSection.Minimized && candidate.Handle == item.MainWindowHandle)
                    {
                        continue;
                    }

                    windows.Add(candidate);
                }
            }

            if (windows.Count > 0)
            {
                menu.Items.Add(new Separator());

                var windowsMenu = new MenuItem { Header = "Pencereler" };
                foreach (AppWindowInfo window in windows)
                {
                    var entry = new MenuItem
                    {
                        Header = string.IsNullOrWhiteSpace(window.Title)
                            ? "Başlıksız pencere"
                            : window.Title,
                    };
                    entry.Click += (_, _) => _activation?.TryActivate(window.Handle);
                    windowsMenu.Items.Add(entry);
                }

                menu.Items.Add(windowsMenu);
            }
        }

        return menu;
    }

    private static SymbolIcon CreateMenuIcon(SymbolRegular symbol) =>
        new(symbol) { FontSize = 16 };

    private static void ForceQuitApp(DockAppItem item)
    {
        if (item.MainWindowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            NativeMethods.GetWindowThreadProcessId(item.MainWindowHandle, out uint pid);
            if (pid > 0)
            {
                using var proc = Process.GetProcessById((int)pid);
                proc.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar force quit failed for {item.Label}: {ex.Message}");
        }
    }

    private static void CloseAppWindow(DockAppItem item)
    {
        if (item.MainWindowHandle != IntPtr.Zero)
        {
            NativeMethods.PostMessage(item.MainWindowHandle, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static void OpenFileLocation(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar open file location failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds or removes the item from WharfBar's own dock list. The Windows taskbar is
    /// never written — the dock stopped mirroring it in this redesign.
    /// </summary>
    private void TogglePin(DockAppItem item, bool pin)
    {
        // Local, not a repeated property read: the null check would otherwise have to be
        // re-derived by the compiler at every use below.
        DockItemStore? store = ItemStore;
        if (store is null)
        {
            return;
        }

        // Remove by Path, never by Id: on a pinned-and-running tile the composer fills Id from
        // RunningAppInfo, which for a Store/UWP app is an AppUserModelId rather than a path, and
        // DockItemStore keys on normalized paths — so an Id-based Remove silently missed those.
        // Path is pin.Path for both pinned shapes (DockAppsComposer.cs:76 and :85).
        bool changed = pin
            ? store.TryAdd(item.Path, item.Args, item.Label)
            : store.Remove(item.Path);

        if (!changed)
        {
            // Duplicate add, or a remove that matched nothing — either way the strip already
            // shows the requested state, so this is not an error, only an invisible no-op.
            Debug.WriteLine($"WharfBar dock pin toggle was a no-op for {item.Label}: store already in the requested state.");
            return;
        }

        // DockItemStore raises Changed, which reaches the composer and rebuilds the strip.
        ApplyPlacement();
    }

    private int ResolveIconPixelSize()
    {
        uint dpi = _source is null ? _monitorDpi : Display.GetDpi(_source.Handle);
        // Request the bitmap at the magnification ceiling so a magnified icon stays sharp —
        // upscaling a resting-size bitmap looks blurry (docs/04-gotchas.md:31).
        double maxScale = DockMagnification.Clamp(_config.Dock.Magnification);
        int px = Display.DipToPixels(_iconSizeDip * maxScale, dpi);
        return Math.Clamp(px <= 0 ? 24 : px, 16, 256);
    }

    /// <summary>
    /// Places the dock on the primary monitor bottom edge in physical pixels, anchored per
    /// <see cref="DockConfig.Position"/>. Uses monitor bounds (not work area) for Y so the strip
    /// sits flush when the taskbar is hidden but the shell work area still reserves that strip.
    /// WPF Left/Top/Width are unused (primary-scale bias).
    /// </summary>
    private void ApplyPlacement()
    {
        if (_source is null || _isApplyingPlacement)
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            return;
        }

        _isApplyingPlacement = true;
        try
        {
            int y = ResolvePlacementY(geo);
            SetWindowRect(geo.X, y, geo.WidthPx, geo.HeightPx);
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private int ResolvePlacementY(PlacementGeometry geo)
    {
        if (_autoHide.Enabled && _isAnimatingY && _source is not null
            && NativeMethods.GetWindowRect(_source.Handle, out RECT rect))
        {
            return rect.Top;
        }

        if (_autoHide.Enabled)
        {
            return _autoHide.State is DockAutoHideVisualState.Hidden or DockAutoHideVisualState.Hiding
                ? geo.HiddenY
                : geo.VisibleY;
        }

        return geo.VisibleY;
    }

    private bool TryGetPlacementGeometry(out PlacementGeometry geo)
    {
        geo = default;
        if (_source is null)
        {
            return false;
        }

        IntPtr hwnd = _source.Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        uint dpi = _monitorDpi;
        int heightPx = Display.DipToPixels(MaxHeightDip, dpi);
        int bottomGapPx = Display.DipToPixels(BottomGapDip, dpi);
        RECT monitor = Monitor.Bounds;

        if (monitor.Width <= 0 || heightPx <= 0)
        {
            return false;
        }

        int widthPx = ComputeMaxWidthPx(dpi);
        int minWidthPx = Display.DipToPixels(MinWidthDip, dpi);
        widthPx = Math.Clamp(widthPx, minWidthPx, monitor.Width);

        int x = ResolvePlacementX(monitor, widthPx, dpi);
        int visibleY = monitor.Bottom - heightPx - bottomGapPx;
        int hiddenY = monitor.Bottom;

        _lastVisibleY = visibleY;
        _lastHiddenY = hiddenY;
        _haveSlideRange = true;

        geo = new PlacementGeometry(x, visibleY, hiddenY, widthPx, heightPx);
        return true;
    }

    /// <summary>
    /// Horizontal anchor. Only bottom-left/center/right: the work-area reservation and the
    /// downward auto-hide slide stay untouched, so a side-docked variant would be a
    /// different feature (spec §4.2).
    /// </summary>
    private int ResolvePlacementX(RECT monitor, int widthPx, uint dpi)
    {
        int marginPx = Display.DipToPixels(12d, dpi);

        return DockPositions.Normalize(_config.Dock.Position) switch
        {
            DockPositions.BottomLeft => monitor.Left + marginPx,
            DockPositions.BottomRight => monitor.Right - widthPx - marginPx,
            _ => monitor.Left + ((monitor.Width - widthPx) / 2),
        };
    }

    private EdgeProbeContext BuildEdgeProbe()
    {
        uint dpi = _monitorDpi;
        RECT monitor = Monitor.Bounds;

        // Use last SetWindowPos geometry — Measure() on the 50 ms poll stalls the UI
        // thread (laggy hover slide, delayed top-bar menus).
        int widthPx = _lastPlacementWidthPx > 0
            ? _lastPlacementWidthPx
            : Math.Max(Display.DipToPixels(MinWidthDip, dpi), 1);
        int heightPx = _lastPlacementHeightPx > 0
            ? _lastPlacementHeightPx
            : Display.DipToPixels(MaxHeightDip, dpi);
        int x = _lastPlacementWidthPx > 0
            ? _lastPlacementX
            : ResolvePlacementX(monitor, widthPx, dpi);
        int yVisible = _haveSlideRange
            ? _lastVisibleY
            : monitor.Bottom - heightPx;

        return new EdgeProbeContext(
            monitor.Left,
            monitor.Top,
            monitor.Right,
            monitor.Bottom,
            x,
            yVisible,
            x + widthPx,
            yVisible + heightPx,
            dpi);
    }

    private void OnAutoHideChanged(object? sender, EventArgs e)
    {
        if (_source is null || !_autoHide.Enabled)
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        if (!TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            return;
        }

        switch (_autoHide.State)
        {
            case DockAutoHideVisualState.Showing:
                DockHitTest.ApplyInteractive(hwnd);
                AnimateYTo(geo.VisibleY);
                break;
            case DockAutoHideVisualState.Hiding:
                DockHitTest.ApplyPassthrough(hwnd);
                AnimateYTo(geo.HiddenY);
                break;
            case DockAutoHideVisualState.Visible:
                CancelYAnimation();
                SetWindowRect(geo.X, geo.VisibleY, geo.WidthPx, geo.HeightPx);
                DockHitTest.ApplyInteractive(hwnd);
                break;
            case DockAutoHideVisualState.Hidden:
                CancelYAnimation();
                SetWindowRect(geo.X, geo.HiddenY, geo.WidthPx, geo.HeightPx);
                DockHitTest.ApplyPassthrough(hwnd);
                break;
        }
    }

    private void AnimateYTo(int targetY)
    {
        if (_source is null || !TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            return;
        }

        IntPtr hwnd = _source.Handle;
        if (!NativeMethods.GetWindowRect(hwnd, out RECT rect))
        {
            return;
        }

        int fromY = rect.Top;
        SetWindowRect(geo.X, fromY, geo.WidthPx, geo.HeightPx);

        if (fromY == targetY)
        {
            OnYAnimationCompleted();
            return;
        }

        CancelYAnimation();
        _isAnimatingY = true;
        AnimYOffset = fromY;

        var anim = new DoubleAnimation(fromY, targetY, TimeSpan.FromMilliseconds(AutoHideAnimMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) =>
        {
            _isAnimatingY = false;
            SetWindowY(targetY);
            OnYAnimationCompleted();
        };
        BeginAnimation(AnimYOffsetProperty, anim);
    }

    private void CancelYAnimation()
    {
        BeginAnimation(AnimYOffsetProperty, null);
        _isAnimatingY = false;
    }

    private void OnYAnimationCompleted() => _autoHide.NotifyAnimationCompleted();

    private void SetWindowY(int y) =>
        SetWindowRect(_lastPlacementX, y, _lastPlacementWidthPx, _lastPlacementHeightPx);

    private void SetWindowRect(int x, int y, int widthPx, int heightPx)
    {
        if (_source is null)
        {
            return;
        }

        _lastPlacementX = x;
        _lastPlacementWidthPx = widthPx;
        _lastPlacementHeightPx = heightPx;

        IntPtr hwnd = _source.Handle;
        IntPtr insertAfter = _isFullscreenHidden
            ? NativeMethods.HWND_BOTTOM
            : NativeMethods.HWND_TOPMOST;

        NativeMethods.SetWindowPos(
            hwnd,
            insertAfter,
            x,
            y,
            widthPx,
            heightPx,
            NativeMethods.SWP_NOACTIVATE);
    }

    private int ComputeMaxWidthPx(uint dpi)
    {
        // Budget the window for the worst magnified layout. Hover only ever re-runs WPF
        // measure/arrange — no SetWindowPos per frame, which is what used to stutter.
        double panel = _strip.MaxLiveWidth();
        double width = panel + (2d * HorizontalPaddingDip);
        return Display.DipToPixels(width, dpi);
    }

    private double MaxHeightDip =>
        DockStripLayout.MaxExtentHeight(
            _iconSizeDip,
            RunningIndicatorGapDip,
            RunningIndicatorSizeDip,
            VerticalPaddingDip,
            DockMagnification.Clamp(_config.Dock.Magnification));

    /// <summary>Pushes icon metrics and the magnification ceiling into the strip.</summary>
    private void ApplyStripGeometry()
    {
        _strip.IconSize = _iconSizeDip;
        _strip.IconGap = IconGapDip;
        _strip.IndicatorGap = RunningIndicatorGapDip;
        _strip.IndicatorSize = RunningIndicatorSizeDip;
        _strip.MaxScale = DockMagnification.Clamp(_config.Dock.Magnification);
    }

    private void OnWorkAreaChanged(object? sender, EventArgs e)
    {
        if (_autoHide.Enabled
            && _autoHide.State is DockAutoHideVisualState.Showing or DockAutoHideVisualState.Hiding)
        {
            return;
        }

        CancelYAnimation();
        ApplyPlacement();
    }

    private void OnFullscreenChanged(object? sender, EventArgs e)
    {
        // Auto-hide: the fullscreen app must cover the full monitor — hide the dock.
        // Always-visible (auto-hide off): the dock reserves its strip and stays put.
        if (FullscreenState.Instance.IsFullscreen && _autoHide.Enabled)
        {
            _isFullscreenHidden = true;
            _autoHide.NotifyFullscreen(true);
            Visibility = Visibility.Collapsed;
            return;
        }

        _isFullscreenHidden = false;
        Visibility = Visibility.Visible;

        if (_source is null)
        {
            return;
        }

        DockHitTest.ApplyInteractive(_source.Handle);
        _autoHide.NotifyFullscreen(false);

        NativeMethods.SetWindowPos(
            _source.Handle,
            NativeMethods.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

        CancelYAnimation();
        ApplyPlacement();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_WINDOWPOSCHANGING:
                ClampDockPlacement(lParam);
                break;

            case NativeMethods.WM_DPICHANGED:
                // wParam high word carries the new DPI; MonitorInfo.Dpi was captured at
                // construction and goes stale when the user changes display scale.
                _monitorDpi = (uint)(wParam >> 16);
                if (_monitorDpi == 0)
                {
                    _monitorDpi = Monitor.Dpi;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CancelYAnimation();

                    // Not ApplyIconSizeFromConfig: a scale change leaves the DIP icon size
                    // untouched, so that method early-returns before RebuildStrip and every icon
                    // would stay upscaled from the old raster — the blur ResolveIconPixelSize's
                    // own comment warns against. RebuildStrip re-requests each icon at the new
                    // DPI (ResolveIconPixelSize reads it) and ends with ApplyPlacement itself.
                    if (_composer is not null)
                    {
                        RebuildStrip();
                        return;
                    }

                    ApplyPlacement();
                }));
                break;

            case NativeMethods.WM_DISPLAYCHANGE:
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CancelYAnimation();
                    ApplyPlacement();
                }));
                break;

            case NativeMethods.WM_THEMECHANGED:
            case NativeMethods.WM_SYSCOLORCHANGE:
            case NativeMethods.WM_DWMCOLORIZATIONCOLORCHANGED:
                Dispatcher.BeginInvoke(ApplySurfaceFromConfig);
                break;

            case NativeMethods.WM_SETTINGCHANGE:
                if (IsImmersiveColorSet(lParam))
                {
                    Dispatcher.BeginInvoke(ApplySurfaceFromConfig);
                }

                break;
        }

        return IntPtr.Zero;
    }

    private void ClampDockPlacement(IntPtr lParam)
    {
        if (_source is null || lParam == IntPtr.Zero || !_haveSlideRange)
        {
            return;
        }

        WINDOWPOS pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if (!NativeMethods.GetWindowRect(_source.Handle, out RECT current))
        {
            return;
        }

        bool noMove = (pos.flags & NativeMethods.SWP_NOMOVE) != 0;
        int proposedY = noMove ? current.Top : pos.y;
        bool allowSlide = _isAnimatingY
            || _autoHide.State is DockAutoHideVisualState.Showing or DockAutoHideVisualState.Hiding;
        AppBarPlacement.GetDockSlideClampRange(
            _autoHide.Enabled,
            allowSlide,
            parkedHidden: _autoHide.State is DockAutoHideVisualState.Hidden,
            _lastVisibleY,
            _lastHiddenY,
            out int minY,
            out int maxY);

        int clampedY = AppBarPlacement.ClampSlideY(proposedY, minY, maxY);
        if (clampedY == proposedY)
        {
            return;
        }

        pos.x = noMove ? current.Left : pos.x;
        pos.y = clampedY;
        pos.flags &= ~NativeMethods.SWP_NOMOVE;
        Marshal.StructureToPtr(pos, lParam, false);
    }

    private static bool IsImmersiveColorSet(IntPtr lParam)
    {
        return lParam != IntPtr.Zero
            && string.Equals(Marshal.PtrToStringUni(lParam), "ImmersiveColorSet", StringComparison.Ordinal);
    }

    private readonly record struct PlacementGeometry(
        int X,
        int VisibleY,
        int HiddenY,
        int WidthPx,
        int HeightPx);
}
