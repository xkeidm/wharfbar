using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using WharfBar.Resources;
using WharfBar.Services;
using WharfBar.Views.Controls;

using MenuItem = Wpf.Ui.Controls.MenuItem;

namespace WharfBar.Views;

public partial class DockWindow
{
    private DockSurfaceFlyout? _flyout;
    private DockMinimizeCue? _minimizeCue;
    private readonly DockRestoreSnapshotCache _restoreCache = new();
    private DockWindowCloak? _restoreCloak;
    private IntPtr _restoreCueHwnd;
    private readonly DockAttentionTracker _attention = new();
    private readonly Dictionary<string, DockProgressState> _progressByKey = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _progressTimer;
    private DispatcherTimer? _thumbTimer;
    private DockItemCell? _thumbCell;
    private DockItemCell? _tipHeld;
    private int _shellHookMessage;
    private bool _shellHookRegistered;
    private IntPtr _minimizeHook;
    private DockShellHook.WinEventProc? _minimizeProc;

    private void AttachSurface(IntPtr hwnd)
    {
        _flyout = new DockSurfaceFlyout(SurfaceFlyout, SurfaceFlyoutCard, SurfaceFlyoutBody, ActivateFromFlyout);
        _flyout.Closed += OnFlyoutClosed;
        _minimizeCue = new DockMinimizeCue(MinimizeCue, MinimizeShot, MinimizeScale);

        uint message = DockShellHook.RegisterMessage();
        _shellHookMessage = unchecked((int)message);
        _shellHookRegistered = DockShellHook.TryRegister(hwnd);

        _minimizeProc = OnMinimizeEvent;
        _minimizeHook = DockShellHook.TryHookMinimize(_minimizeProc);

        _progressTimer = new DispatcherTimer { Interval = DockTaskbarProgress.ActiveInterval };
        _progressTimer.Tick += OnProgressTick;
        _progressTimer.Start();
    }

    private void DetachSurface()
    {
        _progressTimer?.Stop();
        _progressTimer = null;
        _thumbTimer?.Stop();
        _thumbTimer = null;
        if (_flyout is not null)
        {
            _flyout.Closed -= OnFlyoutClosed;
            _flyout.Dispose();
        }
        _flyout = null;
        CancelRestoreCue();
        _restoreCache.Clear();
        _minimizeCue?.Dispose();
        _minimizeCue = null;

        if (_shellHookRegistered && _source is not null)
        {
            DockShellHook.Unregister(_source.Handle);
            _shellHookRegistered = false;
        }

        if (_minimizeHook != IntPtr.Zero)
        {
            DockShellHook.Unhook(_minimizeHook);
            _minimizeHook = IntPtr.Zero;
        }

        _minimizeProc = null;
    }

    private void OnSurfaceCellEnter(object sender, MouseEventArgs e)
    {
        if (sender is not DockItemCell cell || cell.Row is not { } row)
        {
            return;
        }

        _flyout?.CancelClose();
        if (row.Kind != DockTileKind.App || !row.IsRunning || _dragItemId is not null)
        {
            return;
        }

        _thumbCell = cell;
        HoldTip(cell);
        _thumbTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _thumbTimer.Tick -= OnThumbTick;
        _thumbTimer.Tick += OnThumbTick;
        _thumbTimer.Stop();
        _thumbTimer.Start();
    }

    private void OnSurfaceCellLeave(object sender, MouseEventArgs e)
    {
        _thumbTimer?.Stop();
        _thumbCell = null;
        if (_flyout is { IsOpen: true })
        {
            _flyout.ScheduleClose();
            return;
        }

        // The preview never opened (the app has no listed window, or the pointer left before
        // the delay): give the system tooltip its normal behaviour back.
        ReleaseTip();
    }

    private void OnThumbTick(object? sender, EventArgs e)
    {
        _thumbTimer?.Stop();
        DockItemCell? cell = _thumbCell;
        if (cell?.Row is not { } row || _flyout is null)
        {
            return;
        }

        if (_flyout.IsOpen && _flyout.ShowingFolder && _flyout.Anchor == cell)
        {
            return;
        }

        if (row.Kind != DockTileKind.App || !row.IsRunning)
        {
            return;
        }

        List<AppWindowInfo> windows = WindowsFor(row.Item);
        if (windows.Count == 0)
        {
            return;
        }

        _flyout.ShowWindows(cell, windows, row.Icon, CachedWindowFrame);
    }

    /// <summary>
    /// Last frame of a window WharfBar itself saw minimize. A minimized window's
    /// <c>PrintWindow</c> is the iconic stub (about 160×28), so the flyout prefers this frame
    /// and then falls back to the application icon with the window title.
    /// </summary>
    private BitmapSource? CachedWindowFrame(IntPtr hwnd) =>
        _restoreCache.TryGet(hwnd, out BitmapSource shot, out _) ? shot : null;

    private void OnFlyoutClosed() => ReleaseTip();

    /// <summary>
    /// Takes the tooltip down before the preview timer starts. The system tooltip shows at
    /// 250 ms and the preview at 220 ms, so closing it once the preview opens is too late: the
    /// tooltip is already queued and lands on top of the cards.
    /// </summary>
    private void HoldTip(DockItemCell cell)
    {
        if (_tipHeld is not null && !ReferenceEquals(_tipHeld, cell))
        {
            ToolTipService.SetIsEnabled(_tipHeld, true);
        }

        if (cell.ToolTip is ToolTip tip)
        {
            tip.IsOpen = false;
        }

        ToolTipService.SetIsEnabled(cell, false);
        _tipHeld = cell;
    }

    private void ReleaseTip()
    {
        if (_tipHeld is null)
        {
            return;
        }

        ToolTipService.SetIsEnabled(_tipHeld, true);
        _tipHeld = null;
    }

    private bool TryHandleSpecialClick(DockItemCell cell, DockItemViewModel row)
    {
        if (row.Kind == DockTileKind.Launchpad)
        {
            _flyout?.Close();
            LaunchpadWindow.Toggle(_config);
            return true;
        }

        if (row.Kind == DockTileKind.Trash)
        {
            _flyout?.Close();
            _ = DockShellOpen.TryOpenRecycleBin();
            return true;
        }

        if (row.Kind is not (DockTileKind.Folder or DockTileKind.Downloads))
        {
            return false;
        }

        if (_flyout is { IsOpen: true, ShowingFolder: true } && _flyout.Anchor == cell)
        {
            _flyout.Close();
            return true;
        }

        string? directory = row.Kind == DockTileKind.Downloads
            ? (string.IsNullOrWhiteSpace(row.Item.Path) ? null : row.Item.Path)
            : DockBuiltInTiles.ResolveDirectory(row.Item.Path);
        if (directory is null || _flyout is null)
        {
            return true;
        }

        _thumbTimer?.Stop();
        _flyout.ShowFolder(cell, directory);
        return true;
    }

    private bool TryHandleTrashDrag(object sender, DragEventArgs e)
    {
        if (sender is not DockItemCell { Row: { Kind: DockTileKind.Trash } }
            || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        return true;
    }

    private bool TryHandleTrashDrop(object sender, DragEventArgs e)
    {
        if (sender is not DockItemCell { Row: { Kind: DockTileKind.Trash } }
            || !e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return false;
        }

        RecycleBinNative.RecycleCallResult result = RecycleBinNative.TryRecycle(
            paths,
            _source?.Handle ?? IntPtr.Zero);
        Debug.WriteLine(
            $"WharfBar recycle accepted={result.Accepted} rejected={result.Rejected} failed={result.ApiFailed}");
        e.Handled = true;
        return true;
    }

    private void OnProgressTick(object? sender, EventArgs e)
    {
        IReadOnlyList<DockAppItem> items = _composer?.Items ?? [];
        bool anyRunning = false;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == DockTileKind.App && items[i].IsRunning)
            {
                anyRunning = true;
                break;
            }
        }

        if (!anyRunning)
        {
            SetProgressInterval(DockTaskbarProgress.QuietInterval);
            if (_progressByKey.Count > 0)
            {
                _progressByKey.Clear();
                ApplySurfaceChrome();
            }

            return;
        }

        try
        {
            DockTaskbarProgress.Scan scan = DockTaskbarProgress.ReadButtons();
            SetProgressInterval(DockTaskbarProgress.NextInterval(anyRunning, scan.Exposed));
            IReadOnlyDictionary<string, DockProgressState> map = DockTaskbarProgress.Assign(
                items,
                _minimizedSource?.Windows ?? [],
                scan);
            _progressByKey.Clear();
            foreach ((string key, DockProgressState state) in map)
            {
                _progressByKey[key] = state;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar dock progress poll failed: {ex.Message}");
            _progressByKey.Clear();
            SetProgressInterval(DockTaskbarProgress.QuietInterval);
        }

        ApplySurfaceChrome();
    }

    private void SetProgressInterval(TimeSpan interval)
    {
        if (_progressTimer is not null && _progressTimer.Interval != interval)
        {
            _progressTimer.Interval = interval;
        }
    }

    private void ApplySurfaceChrome()
    {
        long now = Environment.TickCount64;
        _attention.Expire(now);
        foreach (DockItemCell cell in _strip.Cells)
        {
            string key = cell.PresenceKey;
            DockProgressState state = key.Length > 0 && _progressByKey.TryGetValue(key, out DockProgressState found)
                ? found
                : DockProgressState.None;
            cell.ApplyProgress(state);
            cell.SyncAttention(_attention.Until(key), now);
        }
    }

    private void ClearAttentionFor(DockItemViewModel row, DockItemCell cell)
    {
        string key = cell.PresenceKey.Length > 0 ? cell.PresenceKey : DockPresence.KeyFor(row.Item);
        _attention.Clear(key);
        cell.ClearAttention();
    }

    private void OnShellMessage(IntPtr wParam, IntPtr lParam)
    {
        long code = wParam.ToInt64();
        if (code == DockShellHook.HSHELL_FLASH)
        {
            NoteFlash(lParam);
        }
        else if (code == DockShellHook.HSHELL_WINDOWACTIVATED
            || code == DockShellHook.HSHELL_RUDEAPPACTIVATED)
        {
            NoteActivated(lParam);
        }
    }

    private void NoteFlash(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (!DockAttentionTracker.ShouldNotice(pid, (uint)Environment.ProcessId))
        {
            return;
        }

        if (NativeMethods.GetWindowRect(hwnd, out RECT rect))
        {
            var placed = new DockPlacedWindow(hwnd, pid, false, rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (!DockMonitorWindowFilter.CenterLiesOn(placed, DockMonitorBounds.From(Monitor.Bounds)))
            {
                return;
            }
        }

        DockItemCell? cell = CellForWindow(hwnd);
        if (cell is null || cell.PresenceKey.Length == 0)
        {
            return;
        }

        long now = Environment.TickCount64;
        _attention.StartOrRestart(cell.PresenceKey, now);
        cell.SyncAttention(_attention.Until(cell.PresenceKey), now);
    }

    private void NoteActivated(IntPtr hwnd)
    {
        DockItemCell? cell = CellForWindow(hwnd);
        if (cell is null)
        {
            return;
        }

        _attention.Clear(cell.PresenceKey);
        cell.ClearAttention();
    }

    private void OnMinimizeEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (idObject != DockShellHook.OBJID_WINDOW || hwnd == IntPtr.Zero)
        {
            return;
        }

        if (!NativeMethods.GetWindowRect(hwnd, out RECT rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var placed = new DockPlacedWindow(hwnd, 0, false, rect.Left, rect.Top, rect.Right, rect.Bottom);
        if (!DockMonitorWindowFilter.CenterLiesOn(placed, DockMonitorBounds.From(Monitor.Bounds)))
        {
            return;
        }

        BitmapSource? shot = DockWindowSnapshot.TryCopy(hwnd);
        bool captured = shot is not null;
        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!DockMinimizeMotion.ShouldPlay(SystemParameters.ClientAreaAnimation, captured) || shot is null)
            {
                return;
            }

            _restoreCache.Put(hwnd, shot, rect);

            DockItemCell? cell = CellForWindow(hwnd);
            if (cell is null || _minimizeCue is null)
            {
                return;
            }

            if (PresentationSource.FromVisual(cell)?.CompositionTarget is not CompositionTarget target)
            {
                return;
            }

            Matrix fromDevice = target.TransformFromDevice;
            Point origin = fromDevice.Transform(new Point(rect.Left, rect.Top));
            Point far = fromDevice.Transform(new Point(rect.Right, rect.Bottom));
            Point icon = fromDevice.Transform(cell.PointToScreen(new Point(cell.ActualWidth / 2d, cell.ActualHeight / 2d)));
            _minimizeCue.Play(shot, new Rect(origin, far), icon);
        });
    }

    private bool IsRestorePendingOrIconic(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && (_restoreCueHwnd == hwnd || NativeMethods.IsIconic(hwnd));

    /// <summary>Aborts any in-flight restore cue. Never leaves the window cloaked.</summary>
    private void CancelRestoreCue()
    {
        _minimizeCue?.Cancel();
        _restoreCloak?.Dispose();
        _restoreCloak = null;
        if (_restoreCueHwnd != IntPtr.Zero)
        {
            _restoreCache.Remove(_restoreCueHwnd);
            _restoreCueHwnd = IntPtr.Zero;
        }
    }

    /// <summary>
    /// Dock/flyout path for iconic windows. Returns true when this call handled the click
    /// (cue started, immediate cloaked fallback finished, or an in-flight cue was aborted).
    /// </summary>
    private bool TryBeginRestoreCue(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        if (_restoreCueHwnd == hwnd)
        {
            // Second click during the cue: window is already restored underneath the cloak.
            CancelRestoreCue();
            ActivateRestored(hwnd);
            return true;
        }

        if (!NativeMethods.IsIconic(hwnd))
        {
            return false;
        }

        if (_restoreCueHwnd != IntPtr.Zero)
        {
            CancelRestoreCue();
        }

        bool haveShot = _restoreCache.TryGet(hwnd, out BitmapSource? shot, out RECT restorePx);
        if (!DockMinimizeMotion.ShouldPlay(SystemParameters.ClientAreaAnimation, haveShot) || shot is null)
        {
            return false;
        }

        DockItemCell? cell = CellForWindow(hwnd);
        if (cell is null || _minimizeCue is null)
        {
            return false;
        }

        if (PresentationSource.FromVisual(cell)?.CompositionTarget is not CompositionTarget target)
        {
            return false;
        }

        DockWindowCloak? cloak = DockWindowCloak.TryHide(hwnd);
        if (cloak is null)
        {
            return false;
        }

        _restoreCloak = cloak;
        _restoreCueHwnd = hwnd;

        _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        _ = NativeMethods.PostMessage(hwnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_RESTORE, IntPtr.Zero);

        if (restorePx.Width <= 0 || restorePx.Height <= 0)
        {
            if (NativeMethods.GetWindowRect(hwnd, out RECT live) && live.Width > 0 && live.Height > 0)
            {
                restorePx = live;
            }
            else
            {
                FinishRestoreCue(hwnd, activate: true);
                return true;
            }
        }

        Matrix fromDevice = target.TransformFromDevice;
        Point origin = fromDevice.Transform(new Point(restorePx.Left, restorePx.Top));
        Point far = fromDevice.Transform(new Point(restorePx.Right, restorePx.Bottom));
        Point icon = fromDevice.Transform(cell.PointToScreen(new Point(cell.ActualWidth / 2d, cell.ActualHeight / 2d)));

        try
        {
            _minimizeCue.PlayRestore(shot, new Rect(origin, far), icon, () =>
            {
                if (_restoreCueHwnd == hwnd)
                {
                    FinishRestoreCue(hwnd, activate: true);
                }
            });
        }
        catch
        {
            FinishRestoreCue(hwnd, activate: true);
        }

        return true;
    }

    private void FinishRestoreCue(IntPtr hwnd, bool activate)
    {
        _restoreCloak?.Dispose();
        _restoreCloak = null;
        if (_restoreCueHwnd == hwnd)
        {
            _restoreCueHwnd = IntPtr.Zero;
        }

        _restoreCache.Remove(hwnd);
        if (activate)
        {
            ActivateRestored(hwnd);
        }
    }

    private void ActivateRestored(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        _ = NativeMethods.SetForegroundWindow(hwnd);
        _activation?.NoteDockActivated(hwnd);
        NoteActivated(hwnd);
    }

    private void ActivateFromFlyout(IntPtr hwnd)
    {
        if (IsRestorePendingOrIconic(hwnd) && TryBeginRestoreCue(hwnd))
        {
            return;
        }

        if (_activation?.TryActivate(hwnd) == true)
        {
            NoteActivated(hwnd);
        }
    }

    private DockItemCell? CellForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        _ = NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        foreach (DockItemCell cell in _strip.Cells)
        {
            if (cell.Row is not { } row || row.Kind != DockTileKind.App)
            {
                continue;
            }

            if (row.Item.MainWindowHandle == hwnd)
            {
                return cell;
            }

            IReadOnlyList<uint>? pids = row.Item.ProcessIds;
            if (pid == 0 || pids is null)
            {
                continue;
            }

            for (int i = 0; i < pids.Count; i++)
            {
                if (pids[i] == pid)
                {
                    return cell;
                }
            }
        }

        return null;
    }

    private List<AppWindowInfo> WindowsFor(DockAppItem item) =>
        DockWindowPicker.Collect(
            _minimizedSource?.Windows ?? [],
            ProcessIdsOf(item),
            DockMonitorBounds.From(Monitor.Bounds),
            CenterRectOf);

    /// <summary>
    /// The rectangle whose center decides which monitor owns a window. An iconic window's
    /// <c>GetWindowRect</c> is the off-screen icon point (-32000, -32000), so a minimized
    /// window is placed from its restored rectangle instead — otherwise the flyout and the
    /// "Windows" submenu silently lose every minimized window.
    /// </summary>
    private static RECT? CenterRectOf(AppWindowInfo window)
    {
        if (!NativeMethods.GetWindowRect(window.Handle, out RECT rect))
        {
            return null;
        }

        if (!window.IsMinimized
            || !DockMonitorWindowFilter.TryNormalRect(window.Handle, out RECT normal)
            || normal.Width <= 0
            || normal.Height <= 0)
        {
            return rect;
        }

        return DockMonitorWindowFilter.RectForCenter(true, rect, normal, true);
    }

    private ContextMenu BuildBuiltInMenu(DockAppItem item)
    {
        var menu = new ContextMenu();
        var header = new MenuItem
        {
            Header = item.Label,
            IsEnabled = false,
            Focusable = false,
        };
        menu.Items.Add(header);
        var open = new MenuItem { Header = Strings.Dock_Open };
        open.Click += (_, _) =>
        {
            if (item.Kind == DockTileKind.Launchpad)
            {
                LaunchpadWindow.Toggle(_config);
            }
            else if (item.Kind == DockTileKind.Trash)
            {
                _ = DockShellOpen.TryOpenRecycleBin();
            }
            else if (!string.IsNullOrWhiteSpace(item.Path))
            {
                _ = DockShellOpen.TryOpen(item.Path);
            }
        };
        menu.Items.Add(open);
        return menu;
    }
}
