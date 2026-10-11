using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Reserves a full-width bottom strip while the dock is always visible, so a
/// maximized window stops at the plate's top edge. The plate stays an overlay.
/// This sibling HWND is the AppBar — a WPF <c>HwndSource</c> paints that strip
/// opaque black (DWM redirection ignores <c>LWA_ALPHA</c>), so the window is a
/// raw layered popup at alpha 0. Wallpaper shows through; clicks pass through.
/// Desktop icons share the work area, so they stop on the same line.
/// </summary>
internal sealed class DockWorkAreaReservation : IDisposable
{
    private const string ClassName = "WharfBarDockEdge";
    private const string CallbackName = "WharfBarDockEdgeMessage";
    private const int ErrorClassAlreadyExists = 1410;

    private static readonly NativeMethods.WndProc WindowProc = OnWindowMessage;
    private static readonly Dictionary<IntPtr, DockWorkAreaReservation> Live = new();
    private static bool _classRegistered;

    private readonly Dispatcher _dispatcher;
    private IntPtr _hwnd;
    private AppBar? _appBar;
    private uint _taskbarCreatedMessage;
    private RECT _monitor;
    private int _heightPx;
    private RECT _lastApproved;
    private bool _haveLastApproved;
    private bool _applying;
    private bool _queued;
    private bool _pendingReregister;
    private bool _disposed;

    internal DockWorkAreaReservation()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// Reserves <paramref name="heightPx"/> at the bottom of <paramref name="monitor"/>.
    /// A non-positive height releases the strip.
    /// </summary>
    internal void Reserve(RECT monitor, int heightPx)
    {
        if (_disposed || monitor.Width <= 0 || monitor.Height <= 0 || heightPx <= 0)
        {
            Release();
            return;
        }

        bool same =
            _appBar is { IsRegistered: true }
            && _heightPx == heightPx
            && AppBarPlacement.RectsEqual(_monitor, monitor)
            && _haveLastApproved;
        if (same)
        {
            return;
        }

        bool monitorChanged = _hwnd != IntPtr.Zero && !AppBarPlacement.RectsEqual(_monitor, monitor);
        _monitor = monitor;
        _heightPx = heightPx;
        if (!EnsureWindow())
        {
            return;
        }

        if (monitorChanged)
        {
            _appBar?.Unregister();
            _haveLastApproved = false;
        }

        MoveOntoMonitor();
        if (_appBar is { IsRegistered: false, IsRemoved: false })
        {
            _appBar.Register();
        }

        Apply();
    }

    /// <summary>Drops the bottom reservation. The HWND stays so the next reserve can re-register.</summary>
    internal void Release()
    {
        if (_disposed)
        {
            return;
        }

        _appBar?.Unregister();
        _haveLastApproved = false;
        _heightPx = 0;
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_HIDE);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _appBar?.Remove();
        _appBar = null;
        if (_hwnd != IntPtr.Zero)
        {
            IntPtr hwnd = _hwnd;
            _hwnd = IntPtr.Zero;
            NativeMethods.DestroyWindow(hwnd);
            Live.Remove(hwnd);
        }
    }

    private bool EnsureWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return true;
        }

        if (!EnsureClass())
        {
            return false;
        }

        int exStyle =
            NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_LAYERED
            | NativeMethods.WS_EX_TRANSPARENT
            | NativeMethods.WS_EX_NOACTIVATE;

        _hwnd = NativeMethods.CreateWindowEx(
            exStyle,
            ClassName,
            "WharfBarDockEdge",
            NativeMethods.WS_POPUP,
            _monitor.Left,
            _monitor.Bottom - 1,
            Math.Max(1, _monitor.Width),
            1,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            Debug.WriteLine($"WharfBar dock edge window failed: {Marshal.GetLastWin32Error()}");
            return false;
        }

        Live[_hwnd] = this;

        // Alpha 0 before the first show. A WPF HwndSource cannot do this: its
        // redirection surface stays opaque black and covers the wallpaper.
        if (!NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 0, NativeMethods.LWA_ALPHA))
        {
            Debug.WriteLine($"WharfBar dock edge alpha failed: {Marshal.GetLastWin32Error()}");
        }

        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _appBar = new AppBar(
            _hwnd,
            NativeMethods.RegisterWindowMessage(CallbackName),
            NativeMethods.ABE_BOTTOM);
        return true;
    }

    private static bool EnsureClass()
    {
        if (_classRegistered)
        {
            return true;
        }

        var wc = new NativeMethods.WNDCLASS
        {
            lpfnWndProc = WindowProc,
            hInstance = NativeMethods.GetModuleHandle(null),
            lpszClassName = ClassName,
        };

        if (NativeMethods.RegisterClass(ref wc) == 0
            && Marshal.GetLastWin32Error() != ErrorClassAlreadyExists)
        {
            Debug.WriteLine($"WharfBar dock edge class failed: {Marshal.GetLastWin32Error()}");
            return false;
        }

        _classRegistered = true;
        return true;
    }

    private void MoveOntoMonitor()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        RECT strip = AppBarPlacement.BottomStrip(_monitor, _heightPx);
        NativeMethods.SetWindowPos(
            _hwnd,
            NativeMethods.HWND_BOTTOM,
            strip.Left,
            strip.Top,
            Math.Max(1, strip.Width),
            Math.Max(1, strip.Height),
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private void Apply()
    {
        if (_disposed || _hwnd == IntPtr.Zero || _appBar is not { IsRegistered: true } || _applying || _heightPx <= 0)
        {
            return;
        }

        _applying = true;
        try
        {
            RECT desired = AppBarPlacement.BottomStrip(_monitor, _heightPx);
            if (desired.Width <= 0 || desired.Height <= 0)
            {
                return;
            }

            RECT approved = _appBar.Query(desired);
            bool needSetPos = AppBarPlacement.NeedsShellSetPos(_haveLastApproved, _lastApproved, approved);
            if (needSetPos)
            {
                approved = _appBar.CommitPos(approved);
            }

            _lastApproved = approved;
            _haveLastApproved = true;

            bool windowMatches = NativeMethods.GetWindowRect(_hwnd, out RECT current)
                && AppBarPlacement.RectsEqual(current, approved);
            if (needSetPos || !windowMatches)
            {
                NativeMethods.SetWindowPos(
                    _hwnd,
                    NativeMethods.HWND_BOTTOM,
                    approved.Left,
                    approved.Top,
                    approved.Width,
                    approved.Height,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private void QueueApply(bool reRegister = false)
    {
        if (reRegister)
        {
            _pendingReregister = true;
        }

        if (_queued || _disposed)
        {
            return;
        }

        _queued = true;
        _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _queued = false;
            if (_disposed)
            {
                return;
            }

            if (_pendingReregister)
            {
                _pendingReregister = false;
                _haveLastApproved = false;
                _appBar?.ReRegister();
            }

            if (_heightPx > 0)
            {
                Apply();
            }
        });
    }

    private static IntPtr OnWindowMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (!Live.TryGetValue(hwnd, out DockWorkAreaReservation? self))
        {
            return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
        }

        if (msg == NativeMethods.WM_ERASEBKGND)
        {
            return new IntPtr(1);
        }

        if (self._appBar is not null
            && self._appBar.TryGetNotification((int)msg, wParam, out AppBarNotification notification))
        {
            if (notification is AppBarNotification.PosChanged or AppBarNotification.StateChange)
            {
                self.QueueApply();
            }

            return IntPtr.Zero;
        }

        if (self._taskbarCreatedMessage != 0 && msg == self._taskbarCreatedMessage)
        {
            self.QueueApply(reRegister: true);
            return IntPtr.Zero;
        }

        switch (msg)
        {
            case NativeMethods.WM_WINDOWPOSCHANGING:
                self.ClampToApproved(lParam);
                break;

            case NativeMethods.WM_WINDOWPOSCHANGED:
                if (NativeMethods.GetWindowRect(hwnd, out RECT current)
                    && AppBarPlacement.ShouldNotifyShellWindowPosChanged(
                        self._applying,
                        self._haveLastApproved,
                        current,
                        self._lastApproved))
                {
                    self._appBar?.NotifyWindowPosChanged();
                }

                break;

            case NativeMethods.WM_DPICHANGED:
            case NativeMethods.WM_DISPLAYCHANGE:
                self.QueueApply();
                break;
        }

        return NativeMethods.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ClampToApproved(IntPtr lParam)
    {
        if (_hwnd == IntPtr.Zero || lParam == IntPtr.Zero || !_haveLastApproved)
        {
            return;
        }

        WINDOWPOS pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
        if (!NativeMethods.GetWindowRect(_hwnd, out RECT current))
        {
            return;
        }

        if (!AppBarPlacement.TryAlignToRect(
                pos.x,
                pos.y,
                pos.cx,
                pos.cy,
                pos.flags,
                current.Left,
                current.Top,
                current.Width,
                current.Height,
                _lastApproved,
                out int nx,
                out int ny,
                out int ncx,
                out int ncy,
                out uint nflags))
        {
            return;
        }

        pos.x = nx;
        pos.y = ny;
        pos.cx = ncx;
        pos.cy = ncy;
        pos.flags = nflags;
        Marshal.StructureToPtr(pos, lParam, false);
    }
}
