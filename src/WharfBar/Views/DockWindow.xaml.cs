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
using WharfBar.Resources;
using WharfBar.Services;
using WharfBar.Views.Controls;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

using MenuItem = Wpf.Ui.Controls.MenuItem;
using TextBlock = Wpf.Ui.Controls.TextBlock;

namespace WharfBar.Views;

/// <summary>
/// Windows-taskbar-style dock overlay. The plate is not an AppBar — placement uses
/// physical-pixel SetWindowPos only. When auto-hide is off, a sibling bottom AppBar
/// (<see cref="DockWorkAreaReservation"/>) reserves the strip so maximized windows
/// and desktop icons stop at the plate's top edge.
/// </summary>
public partial class DockWindow : Window
{
    /// <summary>Vertical padding around the button row (DIP).</summary>
    private const double VerticalPaddingDip = DockPlateMetrics.VerticalPaddingDip;

    /// <summary>Horizontal inset inside the dock surface (DIP).</summary>
    private const double HorizontalPaddingDip = DockPlateMetrics.HorizontalPaddingDip;

    /// <summary>Gap between buttons (DIP).</summary>
    private const double IconGapDip = DockPlateMetrics.GapDip;

    /// <summary>
    /// Gap between dock window bottom and the primary monitor's bottom edge (DIP).
    /// Kept at 0 so the strip sits on the screen edge (work-area bottom often leaves a taskbar-sized hole
    /// while Shell_TrayWnd is hidden).
    /// </summary>
    private const double BottomGapDip = 0d;

    private const int AutoHideAnimMs = 200;

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

    private readonly AppConfig _config;
    private readonly TaskbarVisibilityService _taskbar;
    private readonly DockStrip _strip;
    private double _iconSizeDip;

    /// <summary>Last <see cref="DockConfig.ShowRunningApps"/> value pushed into the composer.</summary>
    private bool _appliedShowRunningApps;

    /// <summary>Last magnification ceiling pushed into the strip.</summary>
    private double _appliedMagnification;

    private readonly DockAutoHideController _autoHide;
    private DockWorkAreaReservation? _edgeReservation;
    private HwndSource? _source;
    private readonly AppIconService _icons = new();
    private readonly ProcessLauncherService _launcher = new();
    private RunningAppsSource? _runningSource;
    private MinimizedWindowsSource? _minimizedSource;
    private TaskbarPinnedAppsSource? _taskbarPins;
    private DockAppsComposer? _composer;
    private WindowActivationService? _activation;
    private WindowAppearance? _appearance;
    private DispatcherTimer? _tintSaveTimer;
    private int _pendingTintOpacity;

    /// <summary>
    /// Live DPI for this window. Starts at <see cref="MonitorInfo.Dpi"/> and is refreshed
    /// from <c>WM_DPICHANGED</c> so a runtime scale change does not leave the strip sized
    /// for the old DPI.
    /// </summary>
    private uint _monitorDpi;

    private readonly DispatcherTimer _cursorTimer;
    private double _cursorTarget = 0d;
    private long _lastCursorFrameMs;
    private bool _isFullscreenHidden;
    private bool _isApplyingPlacement;
    private bool _isAnimatingY;
    private int _lastPlacementX;
    private int _lastPlacementY;
    private int _lastPlacementWidthPx;
    private int _lastPlacementHeightPx;
    private bool _hasPlacement;
    private bool _plateFollowQueued;
    private int _lastVisibleY;
    private int _lastHiddenY;
    private int _lastVisibleX;
    private int _lastHiddenX;
    private bool _haveSlideRange;

    // --- Click vs. drag arbitration -------------------------------------------------
    // One left button, two gestures: a click launches, a drag reorders. The drag is the one
    // that needs bookkeeping, because the two share both their button and (after a reorder
    // drop) possibly their cell: the drop mutates the order, the composer's Changed rebuilds
    // the strip, and DockStrip.SetRows reuses cell objects by app identity. So nothing
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
        var window = (DockWindow)d;
        int value = (int)Math.Round((double)e.NewValue);
        if (window._strip.Vertical)
        {
            window.SetWindowX(value);
        }
        else
        {
            window.SetWindowY(value);
        }
    }

    internal DockWindow(AppConfig config, TaskbarVisibilityService taskbar, MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(taskbar);

        Monitor = monitor;
        _monitorDpi = monitor.Dpi;

        // Must be set before InitializeComponent — WPF locks AllowsTransparency after creation.
        AllowsTransparency = TopBarBackgroundModes.NeedsTransparency(config.Dock.BackgroundMode);
        InitializeComponent();
        _strip = Strip;
        _strip.LiveWidthChanged += OnStripLiveWidthChanged;
        _strip.CellReleased += OnStripCellReleased;

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
        ApplyPlatePadding();
        Surface.ContextMenu = new ContextMenu();
        Surface.ContextMenuOpening += OnPlateContextMenuOpening;
    }

    /// <summary>The monitor this dock window is anchored to.</summary>
    internal MonitorInfo Monitor { get; }

    /// <summary>
    /// Re-applies config-driven settings (icon size, auto-hide, magnification, surface,
    /// running-app visibility).
    /// </summary>
    internal void ApplyConfigChanges()
    {
        ApplyStripGeometry();
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
                SetWindowRect(geo.HiddenX, geo.HiddenY, geo.WidthPx, geo.HeightPx);
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
        ApplyPlatePadding();

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
        // right-hand running apps — only rebuilds when one of its sources raises Changed. The
        // store is that relay: Reload raises Changed with the list itself untouched, and the
        // composer's ItemsEqual check then keeps a toggle that moves nothing off the strip.
        if (_composer is not null && _appliedShowRunningApps != _config.Dock.ShowRunningApps)
        {
            _appliedShowRunningApps = _config.Dock.ShowRunningApps;
            ItemStore?.Reload();
        }
    }

    private void ApplyPlatePadding()
    {
        Surface.Padding = new Thickness(
            HorizontalPaddingDip,
            VerticalPaddingDip,
            HorizontalPaddingDip,
            VerticalPaddingDip);
    }

    /// <summary>
    /// Mica: the window is the plate and DWM draws it. Tint and transparent: the window is
    /// clear and the rounded <see cref="Surface"/> carries the fill.
    /// </summary>
    private void ApplySurfaceFromConfig()
    {
        string mode = TopBarBackgroundModes.NormalizeMode(_config.Dock.BackgroundMode);
        if (!TopBarBackgroundModes.NeedsTransparency(mode))
        {
            _appearance?.Apply();
            Surface.Background = Brushes.Transparent;
            Surface.CornerRadius = new CornerRadius(0);
            return;
        }

        _appearance?.ApplyTranslucent(null);
        Surface.CornerRadius = new CornerRadius(DockPlateMetrics.CornerRadiusDip);
        Surface.Background = mode == TopBarBackgroundModes.Tint
            ? CreateTintBrush(_config.Dock.TintOpacity)
            : Brushes.Transparent;
    }

    /// <summary>The surface first: it re-applies the app theme the brand tile reads.</summary>
    private void ApplyThemeFromSystem()
    {
        ApplySurfaceFromConfig();
        RefreshBrandTile();
    }

    private static Brush CreateTintBrush(int opacity)
    {
        int clamped = TopBarBackgroundModes.ClampOpacity(opacity);
        byte alpha = (byte)(int)Math.Round(clamped / 100.0 * 255.0);
        bool dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        byte rgb = dark ? (byte)0x20 : (byte)0xFA;
        var brush = new SolidColorBrush(Color.FromArgb(alpha, rgb, rgb, rgb));
        brush.Freeze();
        return brush;
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

        bool translucent = TopBarBackgroundModes.NeedsTransparency(_config.Dock.BackgroundMode);
        _appearance = new WindowAppearance(
            this,
            hwnd,
            translucent ? WindowBackdropType.None : WindowBackdropType.Mica,
            translucent ? DwmCornerPreference.DoNotRound : DwmCornerPreference.Round);
        ApplySurfaceFromConfig();

        DockHitTest.ApplyInteractive(hwnd);

        FullscreenState.Instance.Changed += OnFullscreenChanged;
        WorkAreaChanged.Raised += OnWorkAreaChanged;

        DockItemStore store = ItemStore
            ?? throw new InvalidOperationException("App.DockItems is not initialized yet.");
        _runningSource = new RunningAppsSource(Dispatcher);
        _minimizedSource = new MinimizedWindowsSource(Dispatcher);
        _taskbarPins = new TaskbarPinnedAppsSource(Dispatcher);
        _composer = new DockAppsComposer(
            store,
            _runningSource,
            _minimizedSource,
            _config.Dock,
            _taskbarPins,
            DockMonitorBounds.From(Monitor.Bounds));
        _activation = new WindowActivationService();
        _composer.Changed += OnComposerChanged;
        AttachSurface(hwnd);

        ApplyStripGeometry();
        Surface.MouseLeave += OnSurfaceMouseLeave;

        RebuildStrip();

        _autoHide.Changed += OnAutoHideChanged;
        if (_autoHide.Enabled)
        {
            if (TryGetPlacementGeometry(out PlacementGeometry geo))
            {
                SetWindowRect(geo.HiddenX, geo.HiddenY, geo.WidthPx, geo.HeightPx);
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
    /// Reserves the dock strip at the target monitor's bottom when the dock is always
    /// visible (auto-hide off). The reservation is a bottom AppBar, not
    /// <c>SPI_SETWORKAREA</c> — that call is a no-op on 25H2, so neither maximized
    /// windows nor desktop icons would move. Auto-hide releases the AppBar and the
    /// bottom edge is free again.
    /// </summary>
    private void ApplyWorkAreaReservation()
    {
        if (_source is null || _autoHide.Enabled || !TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            _edgeReservation?.Release();
            _taskbar.SetDockReservation(Monitor, 0);
            return;
        }

        uint dpi = Display.GetDpi(_source.Handle);
        int bottomGapPx = Display.DipToPixels(BottomGapDip, dpi);
        bool left = geo.Vertical;
        int thicknessPx = left ? geo.WidthPx : geo.HeightPx + bottomGapPx;
        _edgeReservation ??= new DockWorkAreaReservation();
        _edgeReservation.Reserve(
            Monitor.Bounds,
            thicknessPx,
            left ? NativeMethods.ABE_LEFT : NativeMethods.ABE_BOTTOM);
        _taskbar.SetDockReservation(Monitor, left ? 0 : thicknessPx);
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachSurface();
        _edgeReservation?.Dispose();
        _edgeReservation = null;
        _taskbar.SetDockReservation(Monitor, 0);

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
        _taskbarPins?.Dispose();
        _taskbarPins = null;
        _activation = null;

        _autoHide.Changed -= OnAutoHideChanged;
        _autoHide.Dispose();

        Surface.MouseLeave -= OnSurfaceMouseLeave;
        _tintSaveTimer?.Stop();
        _cursorTimer.Tick -= OnCursorFrame;
        _cursorTimer.Stop();

        _source?.RemoveHook(WndProc);
        _source = null;

        base.OnClosed(e);
    }

    private void OnComposerChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(RebuildStrip);

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
            viewModels.Add(DockItemViewModel.ForItem(row.Item, LoadIcon(row.Item, iconPx)));
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
            if (row is null)
            {
                continue;
            }

            BindDockTip(cell, row.Tooltip);

            // Spec §6.3/§9: the user picked "sistem tooltip" as it behaves today, so these two
            // timings are part of the approved look, not incidental defaults.
            ToolTipService.SetInitialShowDelay(cell, ToolTipInitialShowDelayMs);
            ToolTipService.SetShowDuration(cell, ToolTipShowDurationMs);

            cell.ContextMenu = BuildContextMenuForItem(row.Item);

            // Every tile launches on click; only pinned tiles reorder.
            cell.PreviewMouseLeftButtonDown += OnCellPreviewMouseLeftButtonDown;
            cell.MouseLeftButtonUp += OnCellMouseLeftButtonUp;
            cell.PreviewMouseMove += OnCellMouseMove;
            cell.MouseEnter += OnSurfaceCellEnter;
            cell.MouseLeave += OnSurfaceCellLeave;

            // Reorder and drop are store operations keyed by the pinned path; running and
            // minimized tiles carry a non-pinned id the store cannot move.
            if (ReorderKeyFor(row) is not null || row.Item.Kind == DockTileKind.Trash)
            {
                cell.AllowDrop = true;
                cell.DragOver += OnCellDragOver;
                cell.Drop += OnCellDrop;
            }
        }

        ApplySurfaceChrome();
        ApplyPlacement();
    }

    private void DetachCellHandlers(DockItemCell cell)
    {
        cell.PreviewMouseLeftButtonDown -= OnCellPreviewMouseLeftButtonDown;
        cell.MouseLeftButtonUp -= OnCellMouseLeftButtonUp;
        cell.PreviewMouseMove -= OnCellMouseMove;
        cell.MouseEnter -= OnSurfaceCellEnter;
        cell.MouseLeave -= OnSurfaceCellLeave;
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
        if (row.Item is not { IsPinned: true } item)
        {
            return null;
        }

        string key = DockItemStore.Normalize(item.Path);
        return key.Length > 0 ? key : null;
    }

    private void OnCellPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DockItemCell { Row: { } row })
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
        if (sender is not DockItemCell { Row: { } row })
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

        if (sender is DockItemCell cell && TryHandleSpecialClick(cell, row))
        {
            ClearAttentionFor(row, cell);
            return;
        }

        if (row.Item.IsRunning
            && IsRestorePendingOrIconic(row.Item.MainWindowHandle)
            && TryBeginRestoreCue(row.Item.MainWindowHandle))
        {
            if (sender is DockItemCell restored)
            {
                ClearAttentionFor(row, restored);
            }

            return;
        }

        _activation?.LaunchOrActivate(row.Item, _launcher);
        if (sender is DockItemCell activated)
        {
            ClearAttentionFor(row, activated);
        }
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
        if (TryHandleTrashDrag(sender, e))
        {
            return;
        }

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
        ShowDropIndicator(Strip.ResolveInsertIndex(Strip.Axis(e.GetPosition(Strip))));
    }

    private void OnCellDrop(object sender, DragEventArgs e)
    {
        HideDropIndicator();
        if (TryHandleTrashDrop(sender, e))
        {
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.StringFormat) is not string dragged
            || dragged.Length == 0)
        {
            return;
        }

        int slot = Strip.ResolveInsertIndex(Strip.Axis(e.GetPosition(Strip)));
        ItemStore?.Move(dragged, StoreIndexForDropSlot(slot));
        e.Handled = true;
    }

    /// <summary>
    /// Settled slots skip cells that are animating out, matching
    /// <see cref="Controls.DockStrip.ResolveInsertIndex"/>. Launchpad and Downloads
    /// occupy slots but are not store rows.
    /// </summary>
    private int StoreIndexForDropSlot(int slot)
    {
        DockItemStore? store = ItemStore;
        if (store is null)
        {
            return 0;
        }

        var settled = new List<DockAppItem>();
        foreach (DockItemCell cell in Strip.Cells)
        {
            if (cell.IsExiting || cell.Row is not { } row)
            {
                continue;
            }

            settled.Add(row.Item);
        }

        return DockReorder.StoreIndexBeforeSlot(
            settled,
            slot,
            item => DockReorder.IsStoredPin(item, store.Items));
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
            || index > Strip.SettledSlotCount
            || VisualTreeHelper.GetParent(DropIndicator) is not UIElement host)
        {
            HideDropIndicator();
            return;
        }

        // RestingLeftOf, not a live offset: it reads the strip's resting buffer, so the line
        // marks where the tile lands in the resting order whatever phase the strip happens to
        // be at. (The 180 ms rest-out on drag start is a nicety; this is the guarantee.)
        double along = Strip.RestingLeftOf(index);
        if (Strip.Vertical)
        {
            Point top = Strip.TranslatePoint(new Point(0, along), host);
            DropIndicator.Width = Strip.RestingHeight;
            DropIndicator.Height = 2;
            DropIndicator.Margin = new Thickness(HorizontalPaddingDip, top.Y, 0, 0);
        }
        else
        {
            Point left = Strip.TranslatePoint(new Point(along, 0), host);
            DropIndicator.Width = 2;
            DropIndicator.Height = Strip.RestingHeight;
            DropIndicator.Margin = new Thickness(left.X, 0, 0, VerticalPaddingDip);
        }

        DropIndicator.Visibility = Visibility.Visible;
    }

    private void HideDropIndicator() => DropIndicator.Visibility = Visibility.Collapsed;

    /// <summary>
    /// Keeps the open tooltip instance. Replacing <c>ToolTip</c> on the composer's poll
    /// leaves the previous popup on screen, so the same label appears twice.
    /// </summary>
    private static void BindDockTip(DockItemCell cell, string text)
    {
        if (cell.ToolTip is ToolTip tip)
        {
            if (!Equals(tip.Content, text))
            {
                tip.IsOpen = false;
                tip.Content = text;
            }

            return;
        }

        cell.ToolTip = CreateDockTip(text, cell);
    }

    private static ToolTip CreateDockTip(string text, UIElement target)
    {
        var tip = new ToolTip
        {
            Content = text,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Custom,
            PlacementTarget = target,
        };
        tip.CustomPopupPlacementCallback = (tipSize, targetSize, _) =>
        {
            double scale = target is Visual visual ? VisualTreeHelper.GetDpi(visual).DpiScaleX : 1;
            (double x, double y) = DockTipPlacement.CenterAbove(
                tipSize.Width, tipSize.Height, targetSize.Width, scale);
            return
            [
                new System.Windows.Controls.Primitives.CustomPopupPlacement(
                    new Point(x, y),
                    System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
            ];
        };
        return tip;
    }

    private void OnPlateContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindCell(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        if (Surface.ContextMenu is not ContextMenu menu)
        {
            return;
        }

        menu.Items.Clear();
        FillBackgroundMenu(menu);
    }

    private static DockItemCell? FindCell(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is DockItemCell cell)
            {
                return cell;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void FillBackgroundMenu(ContextMenu menu)
    {
        menu.Items.Add(new MenuItem
        {
            Header = Strings.TopBar_Background,
            IsEnabled = false,
        });

        string mode = TopBarBackgroundModes.NormalizeMode(_config.Dock.BackgroundMode);
        int opacity = TopBarBackgroundModes.ClampOpacity(_config.Dock.TintOpacity);
        var radios = new List<MenuItem>(3)
        {
            CreateBackgroundRadio("Mica", TopBarBackgroundModes.Mica, mode),
            CreateBackgroundRadio("Transparent", TopBarBackgroundModes.Transparent, mode),
            CreateBackgroundRadio("Tint", TopBarBackgroundModes.Tint, mode),
        };

        var opacityHost = new MenuItem
        {
            StaysOpenOnClick = true,
            IsEnabled = mode == TopBarBackgroundModes.Tint,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new System.Windows.Controls.TextBlock
        {
            Text = string.Format(Strings.Culture ?? System.Globalization.CultureInfo.CurrentUICulture, Strings.TopBar_OpacityFormat, opacity),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Width = 88,
        };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Width = 120,
            Value = opacity,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        slider.ValueChanged += (_, args) =>
        {
            int v = (int)Math.Round(args.NewValue);
            label.Text = string.Format(Strings.Culture ?? System.Globalization.CultureInfo.CurrentUICulture, Strings.TopBar_OpacityFormat, v);
            ScheduleDockTintSave(v);
        };
        row.Children.Add(label);
        row.Children.Add(slider);
        opacityHost.Header = row;

        foreach (MenuItem radio in radios)
        {
            string radioMode = (string)radio.Tag!;
            radio.Click += (_, _) =>
            {
                SetDockBackgroundMode(radioMode);
                SyncDockBackgroundMenu(
                    radios,
                    opacityHost,
                    TopBarBackgroundModes.NormalizeMode(_config.Dock.BackgroundMode));
            };
            menu.Items.Add(radio);
        }

        menu.Items.Add(opacityHost);
    }

    private static MenuItem CreateBackgroundRadio(string header, string mode, string current)
    {
        return new MenuItem
        {
            Header = header,
            IsCheckable = true,
            IsChecked = current == mode,
            StaysOpenOnClick = true,
            Tag = mode,
        };
    }

    private static void SyncDockBackgroundMenu(
        IReadOnlyList<MenuItem> radios,
        MenuItem opacityHost,
        string mode)
    {
        foreach (MenuItem radio in radios)
        {
            radio.IsChecked = string.Equals(radio.Tag as string, mode, StringComparison.Ordinal);
        }

        opacityHost.IsEnabled = mode == TopBarBackgroundModes.Tint;
    }

    private void PersistDockBackground(string mode, int? opacity = null)
    {
        _config.Dock.BackgroundMode = TopBarBackgroundModes.NormalizeMode(mode);
        if (opacity is not null)
        {
            _config.Dock.TintOpacity = TopBarBackgroundModes.ClampOpacity(opacity.Value);
        }

        ((App)Application.Current).Config?.Save(_config);
    }

    private void SetDockBackgroundMode(string mode)
    {
        _tintSaveTimer?.Stop();
        string normalized = TopBarBackgroundModes.NormalizeMode(mode);
        bool needTx = TopBarBackgroundModes.NeedsTransparency(normalized);
        PersistDockBackground(normalized);

        if (needTx != AllowsTransparency)
        {
            ((App)Application.Current).RecreateDock();
            return;
        }

        ApplySurfaceFromConfig();
    }

    private void ScheduleDockTintSave(int opacity)
    {
        _pendingTintOpacity = TopBarBackgroundModes.ClampOpacity(opacity);
        if (TopBarBackgroundModes.NormalizeMode(_config.Dock.BackgroundMode) == TopBarBackgroundModes.Tint)
        {
            Surface.Background = CreateTintBrush(_pendingTintOpacity);
        }

        if (_tintSaveTimer is null)
        {
            _tintSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _tintSaveTimer.Tick += (_, _) =>
            {
                _tintSaveTimer.Stop();
                if (TopBarBackgroundModes.NormalizeMode(_config.Dock.BackgroundMode) == TopBarBackgroundModes.Tint)
                {
                    PersistDockBackground(TopBarBackgroundModes.Tint, _pendingTintOpacity);
                }
            };
        }

        _tintSaveTimer.Stop();
        _tintSaveTimer.Start();
    }

    private ContextMenu BuildContextMenuForItem(DockAppItem item)
    {
        if (item.Kind is DockTileKind.Downloads or DockTileKind.Trash or DockTileKind.Launchpad)
        {
            return BuildBuiltInMenu(item);
        }

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
            Source = LoadIcon(item, sizePx),
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
            Header = item.IsPinned ? Strings.Dock_Unpin : Strings.Dock_Pin,
            Icon = CreateMenuIcon(item.IsPinned ? SymbolRegular.PinOff24 : SymbolRegular.Pin24),
        };
        pinItem.Click += (_, _) => TogglePin(item, pin: !item.IsPinned);
        menu.Items.Add(pinItem);

        // 3. Görevi sonlandır (Force Quit)
        var forceQuitItem = new MenuItem
        {
            Header = Strings.Dock_EndTask,
            Icon = CreateMenuIcon(SymbolRegular.DismissCircle24),
            IsEnabled = item.IsRunning && item.MainWindowHandle != IntPtr.Zero,
        };
        forceQuitItem.Click += (_, _) => ForceQuitApp(item);
        menu.Items.Add(forceQuitItem);

        // 4. Pencereyi kapat (Close Window)
        var closeItem = new MenuItem
        {
            Header = Strings.Dock_CloseWindow,
            Icon = CreateMenuIcon(SymbolRegular.Dismiss24),
            IsEnabled = item.IsRunning && item.MainWindowHandle != IntPtr.Zero,
        };
        closeItem.Click += (_, _) => CloseAppWindow(item);
        menu.Items.Add(closeItem);

        // 5. Additional actions if path exists
        bool fileExists = !string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path);
        bool directoryExists = !fileExists
            && !string.IsNullOrWhiteSpace(item.Path)
            && Directory.Exists(item.Path);
        if (fileExists || directoryExists)
        {
            menu.Items.Add(new Separator());

            if (fileExists)
            {
                var runAdminItem = new MenuItem
                {
                    Header = Strings.Dock_RunAsAdmin,
                    Icon = CreateMenuIcon(SymbolRegular.Shield24),
                };
                runAdminItem.Click += (_, _) => _launcher.LaunchAsAdmin(item.Path, item.Args);
                menu.Items.Add(runAdminItem);
            }

            var openFolderItem = new MenuItem
            {
                Header = Strings.Dock_OpenFileLocation,
                Icon = CreateMenuIcon(SymbolRegular.Folder24),
            };
            openFolderItem.Click += (_, _) => OpenFileLocation(item.Path);
            menu.Items.Add(openFolderItem);

            if (fileExists)
            {
                var newInstanceItem = new MenuItem
                {
                    Header = Strings.Dock_NewWindow,
                    Icon = CreateMenuIcon(SymbolRegular.Add24),
                };
                newInstanceItem.Click += (_, _) => _launcher.Launch(item.Path, item.Args);
                menu.Items.Add(newInstanceItem);
            }
        }

        // 6. Pencereler — the cached window table only. EnumWindows on the UI thread stalls on a
        // hung HWND, and the menu opens on the UI thread. Gated on IsRunning, not on Section: a
        // pinned app that is running stays one Pinned tile with IsRunning and a live handle,
        // and that is the tile a user actually right-clicks.
        if (item.IsRunning && item.MainWindowHandle != IntPtr.Zero)
        {
            // One cell can stand for several processes. The cached table is filtered by each
            // pid; EnumWindows stays off this UI thread.
            IReadOnlyList<AppWindowInfo> table = _minimizedSource?.Windows ?? [];
            var seen = new HashSet<IntPtr>();
            List<AppWindowInfo> windows = [];
            foreach (uint pid in ProcessIdsOf(item))
            {
                foreach (AppWindowInfo candidate in MinimizedWindowsSource.ForProcess(table, pid))
                {
                    if (!seen.Add(candidate.Handle))
                    {
                        continue;
                    }

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

            windows = WhereOnThisMonitor(windows);
            if (windows.Count > 0)
            {
                menu.Items.Add(new Separator());

                var windowsMenu = new MenuItem { Header = Strings.Dock_Windows };
                foreach (AppWindowInfo window in windows)
                {
                    var entry = new MenuItem
                    {
                        Header = string.IsNullOrWhiteSpace(window.Title)
                            ? Strings.Dock_UntitledWindow
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
        foreach (uint pid in ProcessIdsOf(item))
        {
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                proc.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WharfBar force quit failed for {item.Label} (pid {pid}): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Process ids for this cell: the ones the composer collapsed, plus the representative
    /// window's process when that lookup succeeds. Order is stable and duplicates are dropped.
    /// </summary>
    private static List<uint> ProcessIdsOf(DockAppItem item)
    {
        var pids = new List<uint>();
        if (item.ProcessIds is not null)
        {
            for (int i = 0; i < item.ProcessIds.Count; i++)
            {
                AddPid(pids, item.ProcessIds[i]);
            }
        }

        if (item.MainWindowHandle != IntPtr.Zero)
        {
            uint threadId = NativeMethods.GetWindowThreadProcessId(item.MainWindowHandle, out uint pid);
            if (threadId != 0)
            {
                AddPid(pids, pid);
            }
        }

        return pids;
    }

    private static void AddPid(List<uint> pids, uint pid)
    {
        if (pid != 0 && !pids.Contains(pid))
        {
            pids.Add(pid);
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
    /// Pins into <c>dock.items</c> and, when the target is a real file or folder, into
    /// User Pinned\TaskBar as a <c>.lnk</c>. Taskband is not written.
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
        bool folderChanged = pin
            ? UserPinnedWriter.TryPin(item.Path, item.Args, item.Label)
            : UserPinnedWriter.TryUnpin(item.Path);
        bool changed = pin
            ? store.TryAdd(item.Path, item.Args, item.Label)
            : store.Remove(item.Path);
        changed |= folderChanged;

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
        int px = Display.DipToPixels(_iconSizeDip, dpi);
        return Math.Clamp(px <= 0 ? 24 : px, 16, 256);
    }

    private ImageSource LoadIcon(DockAppItem item, int sizePx) =>
        DockIconFactory.Load(_icons, item, sizePx, _config.LogoStyle, _config.UseSystemAccentColor);

    /// <summary>
    /// Re-tints the launch tile after a theme, accent or logo-style change. Only that one cell's
    /// glyph is swapped, so menus, tooltips and the other icons stay as they are.
    /// </summary>
    internal void RefreshBrandTile()
    {
        int iconPx = ResolveIconPixelSize();
        foreach (DockItemCell cell in _strip.Cells)
        {
            if (cell.Row is { Kind: DockTileKind.Launchpad } row)
            {
                cell.ReplaceIcon(LoadIcon(row.Item, iconPx));
            }
        }
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
            int x = ResolvePlacementX(geo);
            int y = ResolvePlacementY(geo);
            SetWindowRect(x, y, geo.WidthPx, geo.HeightPx);
        }
        finally
        {
            _isApplyingPlacement = false;
        }

        // Slide frames must not touch the shell. The reservation only changes when the
        // plate is parked visible (auto-hide off) or when auto-hide turns the strip loose.
        if (!_isAnimatingY)
        {
            ApplyWorkAreaReservation();
        }
    }

    private int ResolvePlacementY(PlacementGeometry geo)
    {
        if (geo.Vertical)
        {
            return geo.VisibleY;
        }

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

    private int ResolvePlacementX(PlacementGeometry geo)
    {
        if (!geo.Vertical)
        {
            return geo.VisibleX;
        }

        if (_autoHide.Enabled && _isAnimatingY && _source is not null
            && NativeMethods.GetWindowRect(_source.Handle, out RECT rect))
        {
            return rect.Left;
        }

        if (_autoHide.Enabled
            && _autoHide.State is DockAutoHideVisualState.Hidden or DockAutoHideVisualState.Hiding)
        {
            return geo.HiddenX;
        }

        return geo.VisibleX;
    }

    /// <summary>
    /// Dock plate where it rests when shown. Auto-hide may have slid the window off-screen;
    /// the launchpad still anchors to this rect.
    /// </summary>
    internal bool TryGetRestingRect(out RECT rect)
    {
        rect = default;
        if (!_haveSlideRange || _lastPlacementWidthPx <= 0 || _lastPlacementHeightPx <= 0)
        {
            return false;
        }

        rect = new RECT
        {
            Left = _lastVisibleX,
            Top = _lastVisibleY,
            Right = _lastVisibleX + _lastPlacementWidthPx,
            Bottom = _lastVisibleY + _lastPlacementHeightPx,
        };
        return true;
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
        int thicknessPx = Display.DipToPixels(MaxHeightDip, dpi);
        int bottomGapPx = Display.DipToPixels(BottomGapDip, dpi);
        RECT monitor = Monitor.Bounds;
        bool left = DockPositions.IsLeft(_config.Dock.Position);

        if (monitor.Width <= 0 || monitor.Height <= 0 || thicknessPx <= 0)
        {
            return false;
        }

        int lengthPx = ComputeMaxWidthPx(dpi);
        if (left)
        {
            lengthPx = Math.Clamp(lengthPx, 1, monitor.Height);
            int visibleX = monitor.Left;
            int hiddenX = monitor.Left - thicknessPx;
            int y = monitor.Top + Math.Max(0, (monitor.Height - lengthPx) / 2);
            _lastVisibleX = visibleX;
            _lastHiddenX = hiddenX;
            _lastVisibleY = y;
            _lastHiddenY = y;
            _haveSlideRange = true;
            geo = new PlacementGeometry(visibleX, hiddenX, y, y, thicknessPx, lengthPx, Vertical: true);
            return true;
        }

        lengthPx = Math.Clamp(lengthPx, 1, monitor.Width);
        int x = BottomAnchorX(monitor, lengthPx, dpi);
        int visibleY = monitor.Bottom - thicknessPx - bottomGapPx;
        int hiddenY = monitor.Bottom;

        _lastVisibleX = x;
        _lastHiddenX = x;
        _lastVisibleY = visibleY;
        _lastHiddenY = hiddenY;
        _haveSlideRange = true;

        geo = new PlacementGeometry(x, x, visibleY, hiddenY, lengthPx, thicknessPx, Vertical: false);
        return true;
    }

    /// <summary>Bottom-edge horizontal anchor. The left dock does not use this.</summary>
    private int BottomAnchorX(RECT monitor, int widthPx, uint dpi)
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

        // Use last SetWindowPos geometry — Measure() on the cursor poll stalls the UI
        // thread (laggy hover slide, delayed top-bar menus). Hidden docks poll at
        // DockAutoHideController.IdlePollMs.
        int widthPx = _lastPlacementWidthPx > 0
            ? _lastPlacementWidthPx
            : 1;
        int heightPx = _lastPlacementHeightPx > 0
            ? _lastPlacementHeightPx
            : Display.DipToPixels(MaxHeightDip, dpi);
        int x = _lastPlacementWidthPx > 0
            ? _lastPlacementX
            : monitor.Left;
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
            dpi,
            DockPositions.IsLeft(_config.Dock.Position));
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
                AnimateYTo(geo.Vertical ? geo.VisibleX : geo.VisibleY);
                break;
            case DockAutoHideVisualState.Hiding:
                DockHitTest.ApplyPassthrough(hwnd);
                AnimateYTo(geo.Vertical ? geo.HiddenX : geo.HiddenY);
                break;
            case DockAutoHideVisualState.Visible:
                CancelYAnimation();
                SetWindowRect(geo.VisibleX, geo.VisibleY, geo.WidthPx, geo.HeightPx);
                DockHitTest.ApplyInteractive(hwnd);
                break;
            case DockAutoHideVisualState.Hidden:
                CancelYAnimation();
                SetWindowRect(geo.HiddenX, geo.HiddenY, geo.WidthPx, geo.HeightPx);
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

        bool horizontal = geo.Vertical;
        int from = horizontal ? rect.Left : rect.Top;
        if (horizontal)
        {
            SetWindowRect(from, geo.VisibleY, geo.WidthPx, geo.HeightPx);
        }
        else
        {
            SetWindowRect(geo.VisibleX, from, geo.WidthPx, geo.HeightPx);
        }

        if (from == targetY)
        {
            OnYAnimationCompleted();
            return;
        }

        CancelYAnimation();
        _isAnimatingY = true;
        AnimYOffset = from;

        var anim = new DoubleAnimation(from, targetY, TimeSpan.FromMilliseconds(AutoHideAnimMs))
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

    private void SetWindowX(int x) =>
        SetWindowRect(x, _lastPlacementY, _lastPlacementWidthPx, _lastPlacementHeightPx);

    private void SetWindowRect(int x, int y, int widthPx, int heightPx)
    {
        if (_source is null)
        {
            return;
        }

        IntPtr insertAfter = _isFullscreenHidden
            ? NativeMethods.HWND_BOTTOM
            : NativeMethods.HWND_TOPMOST;

        // Always call SetWindowPos, even when the rect matches. Placement is also how the
        // dock re-asserts topmost after another window covers it. The width-follow path
        // skips this method when the pixel rect has not changed.
        _hasPlacement = true;
        _lastPlacementX = x;
        _lastPlacementY = y;
        _lastPlacementWidthPx = widthPx;
        _lastPlacementHeightPx = heightPx;

        NativeMethods.SetWindowPos(
            _source.Handle,
            insertAfter,
            x,
            y,
            widthPx,
            heightPx,
            NativeMethods.SWP_NOACTIVATE);
    }

    private int ComputeMaxWidthPx(uint dpi)
    {
        // Follow the strip's live width so an icon easing in or out moves the plate with it.
        // The empty dock still reserves one button, matching the pre-animation budget.
        double minStrip = DockPlateMetrics.StripWidth(1, _iconSizeDip);
        double strip = _strip.HasLiveWidth
            ? _strip.LiveWidth
            : DockPlateMetrics.StripWidth(Math.Max(_strip.SettledSlotCount, 1), _iconSizeDip);
        if (strip < minStrip)
        {
            strip = minStrip;
        }

        double width = strip + (2d * HorizontalPaddingDip);
        return Display.DipToPixels(width, dpi);
    }

    private void OnStripLiveWidthChanged(object? sender, EventArgs e)
    {
        if (_plateFollowQueued || _source is null)
        {
            return;
        }

        _plateFollowQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(FollowPlateWidth));
    }

    private void FollowPlateWidth()
    {
        _plateFollowQueued = false;
        if (_source is null || _isApplyingPlacement)
        {
            return;
        }

        if (!TryGetPlacementGeometry(out PlacementGeometry geo))
        {
            return;
        }

        int x = ResolvePlacementX(geo);
        int y = ResolvePlacementY(geo);
        if (_hasPlacement
            && x == _lastPlacementX
            && y == _lastPlacementY
            && geo.WidthPx == _lastPlacementWidthPx
            && geo.HeightPx == _lastPlacementHeightPx)
        {
            return;
        }

        ApplyPlacement();
    }

    private void OnStripCellReleased(DockItemCell cell) => DetachCellHandlers(cell);

    private double MaxHeightDip => DockPlateMetrics.PlateHeight(_iconSizeDip);

    /// <summary>Pushes button metrics into the strip. Scale stays at 1 — the plate does not grow.</summary>
    private void ApplyStripGeometry()
    {
        _strip.Vertical = DockPositions.IsLeft(_config.Dock.Position);
        _strip.GlyphSize = _iconSizeDip;
        _strip.IconSize = DockPlateMetrics.ButtonSize(_iconSizeDip);
        SurfaceFlyout.Placement = _strip.Vertical
            ? System.Windows.Controls.Primitives.PlacementMode.Right
            : System.Windows.Controls.Primitives.PlacementMode.Top;
        _strip.IconGap = IconGapDip;
        _strip.IndicatorGap = 0d;
        _strip.IndicatorSize = 0d;
        _strip.MaxScale = 1d;
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
        // The dock is topmost. A browser video fullscreen (ABN_FULLSCREENAPP) covers the
        // monitor, so the strip has to leave even when auto-hide is off. The top bar only
        // drops its z-order (ADR-003); this window would stay painted on the video.
        if (FullscreenState.Instance.IsFullscreen)
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
        if (_shellHookMessage != 0 && msg == _shellHookMessage)
        {
            OnShellMessage(wParam, lParam);
            return IntPtr.Zero;
        }

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
                Dispatcher.BeginInvoke(ApplyThemeFromSystem);
                break;

            case NativeMethods.WM_SETTINGCHANGE:
                if (IsImmersiveColorSet(lParam))
                {
                    Dispatcher.BeginInvoke(ApplyThemeFromSystem);
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
        bool allowSlide = _isAnimatingY
            || _autoHide.State is DockAutoHideVisualState.Showing or DockAutoHideVisualState.Hiding;
        if (_strip.Vertical)
        {
            int proposedX = noMove ? current.Left : pos.x;
            int alongY = noMove ? current.Top : pos.y;
            int minX = Math.Min(_lastVisibleX, _lastHiddenX);
            int maxX = Math.Max(_lastVisibleX, _lastHiddenX);
            if (!_autoHide.Enabled || !allowSlide)
            {
                int parkedX = _autoHide.Enabled && _autoHide.State is DockAutoHideVisualState.Hidden
                    ? _lastHiddenX
                    : _lastVisibleX;
                minX = maxX = parkedX;
            }

            int clampedX = Math.Clamp(proposedX, minX, maxX);
            if (clampedX == proposedX && alongY == _lastVisibleY)
            {
                return;
            }

            pos.x = clampedX;
            pos.y = _lastVisibleY;
            pos.flags &= ~NativeMethods.SWP_NOMOVE;
            Marshal.StructureToPtr(pos, lParam, false);
            return;
        }

        int proposedY = noMove ? current.Top : pos.y;
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
        int VisibleX,
        int HiddenX,
        int VisibleY,
        int HiddenY,
        int WidthPx,
        int HeightPx,
        bool Vertical);
}
