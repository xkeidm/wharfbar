using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Native;
using WharfBar.Resources;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Screen color picker overlay: a transparent topmost window covering the <b>whole</b> of the
/// primary monitor (including the primary monitor's native appbar strips — top bar, dock —
/// so the loupe reaches every part of the monitor). Follows the window-flag pattern of
/// <c>WallpaperTransitionWindow</c> —
/// it is not an AppBar window and does not go with Mica (<c>DwmSystemBackdrop.None</c> is mandatory
/// alongside <c>AllowsTransparency</c>), and it does not steal focus while open
/// (<c>WS_EX_NOACTIVATE</c> + <c>ShowActivated=false</c>).
///
/// <para><b>Sampling a frozen frame:</b> the dimming mask itself is a layered
/// window and GDI <c>BitBlt</c> reads the composited desktop — once the mask settles in place
/// live sampling returns the masked (dimmed) color. That is why, before the overlay is shown,
/// a single BitBlt frame of the monitor is taken; the magnified pixels inside the loupe and the
/// color to be picked come from this frame (a grid centered on the cursor, clamped at the frame
/// edge, via <see cref="ScreenSampler.TryCropBox"/>; the exact center pixel of that grid is the HEX).
/// If the frame cannot be taken, the last resort is live
/// <see cref="ScreenSampler"/> sampling (low-quality, masked). The click
/// point turns into absolute physical px via <see cref="Window.PointToScreen"/> (PMv2).</para>
///
/// <para><b>Affordance placement:</b> the loupe is centred on the cursor and the HEX/hint card
/// sits below it; when the bottom of the monitor leaves no room the card flips above the loupe
/// (see <see cref="ColorPickerLayout"/>) instead of dragging the loupe away from the cursor.
/// The magnifier grid is sized in whole device pixels per DPI (nearest-neighbour cells are uniform),
/// and the whole affordance is placed on device-pixel boundaries so the cells do not shimmer.</para>
///
/// <para><b>Closing:</b> click (sample and write the hex to the clipboard with <see cref="IslandService.PutOnClipboard"/>),
/// Esc (cancel) or losing keyboard focus. Because the overlay is deliberately not OS-activated while
/// it runs, Esc is caught with a temporary global <c>VK_ESCAPE</c> hotkey registered for exactly the
/// overlay's lifetime (the keyboard-focus path stays as the fallback when the registration is
/// refused). The island panel CLOSES before this overlay
/// opens; once the pick is done the top bar presents the hub again — while the overlay
/// is open there is no other topmost window, the overlay is the single user-approved exception.</para>
/// </summary>
public partial class ColorPickerWindow : Window
{
    /// <summary>Info card column width (DIP); the loupe is centred on the cursor independently.</summary>
    internal const double InfoWidth = 150;

    /// <summary>Nominal magnifier cell size (DIP at 100% DPI); rounded to a whole device pixel per DPI.</summary>
    internal const double MagnifierCellDip = 6;

    /// <summary>Source pixel grid inside the loupe; an odd number, so the loupe shows the exact center pixel.</summary>
    internal const int MagnifierGrid = 13;

    /// <summary>Loupe chrome (DIP at 100% DPI): a 1 px outer frame + a 2 px light ring on each side.</summary>
    private const double LoupeFrameDip = 1;

    private const double LoupeRingDip = 2;

    /// <summary>Global Esc hotkey id; registered only while the overlay is open (ids are per-window).</summary>
    private const int EscapeHotkeyId = 0x5742;

    private static readonly PixelFormat LoupePixelFormat = PixelFormats.Bgra32;

    private HwndSource? _source;
    private IslandService? _clipboard;
    private DispatcherTimer? _closeTimer;

    /// <summary>Frozen BitBlt frame (BGRA, top-down); screen origin is <c>_snapX/_snapY</c>.</summary>
    private byte[]? _snapshot;
    private int _snapX, _snapY, _snapW, _snapH, _snapStride;

    /// <summary>Overlay screen origin (physical px), kept even when the frame capture failed.</summary>
    private int _monitorLeftPx, _monitorTopPx;

    /// <summary>Image inside the loupe: a <see cref="MagnifierGrid"/>×grid BGRA block, enlarged inside the
    /// circle with nearest-neighbor scaling (pixelated blocks).</summary>
    private WriteableBitmap? _loupeBitmap;

    /// <summary>Last valid sample: the pixel at the loupe center.</summary>
    private Color _lastSample;
    private bool _hasSample;

    /// <summary>Last cursor point (DIP, Root coordinates); the info card is re-placed when its height changes.</summary>
    private Point? _lastPoint;

    /// <summary>Loupe body diameter (DIP) for the current monitor scale (whole-device-pixel cells).</summary>
    private double _loupeDiameter = LoupeDiameterFallback;

    private const double LoupeDiameterFallback = (MagnifierGrid * MagnifierCellDip) + (2 * (LoupeFrameDip + LoupeRingDip));

    /// <summary>Device pixels per DIP of the overlay's monitor.</summary>
    private double _pixelsPerDip = 1;

    private bool _escapeHotkey;

    private bool _armed;
    private bool _picking;
    private bool _settled;
    private bool _closed;

    /// <summary>Raised once: the user approved the color, the HEX was written or it was rejected.</summary>
    internal event Action<Color, string, bool>? Settled;

    /// <summary>The overlay closed without picking a color (Esc / keyboard focus loss).</summary>
    internal event Action? Cancelled;

    public ColorPickerWindow()
    {
        InitializeComponent();
        Title = Strings.ColorPicker_Title;
        HintText.Text = Strings.ColorPicker_Hint;
        _loupeBitmap = new WriteableBitmap(
            MagnifierGrid, MagnifierGrid, 96, 96, LoupePixelFormat, null);
        LoupeImage.Source = _loupeBitmap;
        // Clip the inner grid to the circle: pixels outside the visible circle do not spill.
        LoupeContent.SizeChanged += OnLoupeContentSizeChanged;
    }

    private void OnLoupeContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var clip = new EllipseGeometry(
            new Point(e.NewSize.Width / 2, e.NewSize.Height / 2),
            e.NewSize.Width / 2,
            e.NewSize.Height / 2);
        if (clip.CanFreeze)
        {
            clip.Freeze();
        }

        LoupeContent.Clip = clip;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = PresentationSource.FromVisual(this) as HwndSource;
        IntPtr hwnd = _source?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
        {
            ApplyOverlayStyles(hwnd);
            Dwm.SetSystemBackdrop(hwnd, DwmSystemBackdrop.None);
            _source!.AddHook(WndProc);
        }

        _pixelsPerDip = VisualTreeHelper.GetDpi(this).DpiScaleX;
        ApplyMagnifierMetrics();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _pixelsPerDip = newDpi.DpiScaleX;
        ApplyMagnifierMetrics();
        Reposition();
    }

    private static void ApplyOverlayStyles(IntPtr hwnd)
    {
        WindowOverlayStyles.ExcludeFromTaskSwitcher(hwnd);
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(
            hwnd,
            NativeMethods.GWL_EXSTYLE,
            ex | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
    }

    /// <summary>
    /// Sizes the loupe for the monitor's DPI so that each magnifier cell is a whole device pixel:
    /// the content area is <see cref="MagnifierGrid"/> × <c>cellPx</c> device px, and the frame/ring
    /// chrome is converted to device px too, so the grid starts on a pixel boundary. Without this the
    /// 13×13 grid lands on fractional device pixels at 125%/175% and the "boxes" come out uneven.
    /// </summary>
    private void ApplyMagnifierMetrics()
    {
        double scale = _pixelsPerDip > 0 ? _pixelsPerDip : 1;
        int cellPx = Math.Max(2, (int)Math.Round(MagnifierCellDip * scale, MidpointRounding.AwayFromZero));
        int framePx = Math.Max(1, (int)Math.Round(LoupeFrameDip * scale, MidpointRounding.AwayFromZero));
        int ringPx = Math.Max(1, (int)Math.Round(LoupeRingDip * scale, MidpointRounding.AwayFromZero));

        _loupeDiameter = (MagnifierGrid * cellPx + (2.0 * (framePx + ringPx))) / scale;
        LoupeRing.Width = LoupeRing.Height = _loupeDiameter;
        LoupeRing.BorderThickness = new Thickness(framePx / scale);
        LoupeRingInner.BorderThickness = new Thickness(ringPx / scale);
    }

    /// <summary>Used by the top bar for the second-overlay query while the overlay is open (single overlay rule).</summary>
    internal bool IsOpen => IsLoaded && !_settled;

    /// <summary>
    /// Opens the overlay: places it over the work area (frozen frame first). The capture is a one-shot
    /// synchronous operation (tens of ms); the island panel has already closed before <see cref="Start"/> is called,
    /// so no island remains in the frame. If the frame cannot be taken the overlay still opens with live
    /// sampling at low quality. If <paramref name="clipboard"/> is null the click still samples but the result
    /// is reported without writing to the clipboard.
    /// </summary>
    /// <returns><c>false</c> if showing failed — the top bar stays without an overlay.</returns>
    /// <param name="anchorHwnd">Top bar hwnd used when the primary monitor is removed.</param>
    internal bool Start(IntPtr anchorHwnd, IslandService? clipboard)
    {
        _clipboard = clipboard;
        if (IsLoaded || _settled)
        {
            return false;
        }

        if (anchorHwnd == IntPtr.Zero)
        {
            anchorHwnd = _source?.Handle ?? IntPtr.Zero;
        }

        if (anchorHwnd == IntPtr.Zero)
        {
            return false;
        }

        RECT monitor = Display.GetMonitorBounds(anchorHwnd);
        int widthPx = monitor.Right - monitor.Left;
        int heightPx = monitor.Bottom - monitor.Top;
        if (widthPx < 8 || heightPx < 8)
        {
            return false;
        }

        _monitorLeftPx = monitor.Left;
        _monitorTopPx = monitor.Top;
        TakeSnapshot(monitor);

        Show();
        IntPtr hwnd = _source?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _ = NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            monitor.Left,
            monitor.Top,
            widthPx,
            heightPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        // Esc must cancel even though the overlay is intentionally not OS-activated. A bare Esc global
        // hotkey registered for the overlay's lifetime delivers WM_HOTKEY here regardless of which window
        // holds the foreground; the keyboard-focus path below stays as the fallback if that is refused.
        TryRegisterEscapeHotkey();

        // Keyboard focus without OS activation is best-effort (Keyboard.FocusedElement fallback), and the
        // first paint shows the loupe at the cursor before the user moves the mouse — the overlay would
        // otherwise start with an unpositioned, empty loupe.
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                if (_closed || _settled)
                {
                    return;
                }

                _armed = true;
                Keyboard.Focus(this);
                PaintAtCursor();
            }));

        return true;
    }

    /// <summary>
    /// Monitor frame; because the loupe can slide at the edge, the <see cref="MagnifierGrid"/>
    /// overflow rather than the 3 px sample box goes into the measure.
    /// </summary>
    private void TakeSnapshot(RECT monitor)
    {
        int width = monitor.Right - monitor.Left;
        int height = monitor.Bottom - monitor.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int size = MagnifierGrid;
        (_, _, int virtualWidth, int virtualHeight) = ScreenBitBlt.VirtualScreenBounds();
        if (width < size || height < size || width > virtualWidth || height > virtualHeight)
        {
            return;
        }

        if (ScreenBitBlt.TryCaptureRect(monitor.Left, monitor.Top, width, height, out byte[] bgra, out int stride))
        {
            _snapshot = bgra;
            _snapStride = stride;
            _snapW = width;
            _snapH = height;
            _snapX = monitor.Left;
            _snapY = monitor.Top;
        }
    }

    /// <summary>First paint: sample and place the affordances at the current cursor before any move.</summary>
    private void PaintAtCursor()
    {
        UpdateLayout();
        if (_closed || _settled || Root.ActualWidth < 1 || Root.ActualHeight < 1)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out POINT cursor))
        {
            return;
        }

        // Cursor px → overlay DIP through the monitor origin (the overlay covers the monitor exactly);
        // this does not depend on WPF's cached window position.
        double scale = _pixelsPerDip > 0 ? _pixelsPerDip : 1;
        bool insideMonitor = cursor.X >= _monitorLeftPx && cursor.Y >= _monitorTopPx
            && cursor.X < _monitorLeftPx + (Root.ActualWidth * scale)
            && cursor.Y < _monitorTopPx + (Root.ActualHeight * scale);

        if (!insideMonitor)
        {
            // The cursor is on another monitor: park the affordances mid-screen; the first move samples.
            PositionAffordances(new Point(Root.ActualWidth / 2, Root.ActualHeight / 2));
            return;
        }

        Point local = new Point(
            (cursor.X - _monitorLeftPx) / scale,
            (cursor.Y - _monitorTopPx) / scale);
        UpdateSample(cursor.X, cursor.Y);
        PositionAffordances(local);
    }

    private void OnRootMouseMove(object sender, MouseEventArgs e)
    {
        if (_picking || _settled)
        {
            return;
        }

        Point point = e.GetPosition(Root);
        Point screen = PointToScreen(point);
        UpdateSample((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        PositionAffordances(point);
    }

    private void UpdateSample(int pxX, int pxY)
    {
        Color sample = SampleAtPx(pxX, pxY);
        if (sample.A == 0)
        {
            return;
        }

        _lastSample = sample;
        _hasSample = true;
        var fill = new SolidColorBrush(sample);
        fill.Freeze();
        ChipSwatch.Fill = fill;
        HexText.Text = ColorMath.ToHex(sample.R, sample.G, sample.B);
    }

    /// <summary>
    /// Pixel → color + loupe content: the <see cref="MagnifierGrid"/> grid is cropped from the frozen frame with
    /// <see cref="ScreenSampler.TryCropBox"/>, written to the WriteableBitmap
    /// (each cell becomes a single-value-sample pixel block) and the color becomes the exact center
    /// pixel of the grid. If there is no frame the live screen sample (low-quality, masked) is used.
    /// </summary>
    private Color SampleAtPx(int pxX, int pxY)
    {
        byte[] bgra;
        int stride;

        if (_snapshot is not null)
        {
            int relX = pxX - _snapX;
            int relY = pxY - _snapY;
            if (!ScreenSampler.TryCropBox(
                    _snapshot, _snapStride, _snapW, _snapH, relX, relY, MagnifierGrid, out byte[]? crop))
            {
                return Color.FromArgb(0, 0, 0, 0);
            }

            bgra = crop!;
            stride = MagnifierGrid * 4;
        }
        else
        {
            // If there is no frame, live BitBlt of a small box; size = grid, center = cursor.
            int origin = MagnifierGrid / 2;
            if (!ScreenSampler.TrySample(pxX - origin, pxY - origin, MagnifierGrid, out bgra, out stride))
            {
                return Color.FromArgb(0, 0, 0, 0);
            }

            if (stride < MagnifierGrid * 4)
            {
                return Color.FromArgb(0, 0, 0, 0);
            }
        }

        _loupeBitmap?.WritePixels(new Int32Rect(0, 0, MagnifierGrid, MagnifierGrid), bgra, stride, 0);

        // The color is the crop's exact center pixel. Pressed against a frame edge the crop start
        // clamps, so the reported pixel can sit up to MagnifierGrid / 2 px away from the cursor.
        int centerOffset = ((MagnifierGrid / 2) * stride) + ((MagnifierGrid / 2) * 4);
        return Color.FromRgb(bgra[centerOffset + 2], bgra[centerOffset + 1], bgra[centerOffset]);
    }

    /// <summary>
    /// Places the loupe (centred on the cursor) and the info card (below, or above when the bottom
    /// leaves no room) through the pure <see cref="ColorPickerLayout"/> math; both positions are
    /// snapped to whole device pixels so the magnifier cells stay crisp while the cursor moves.
    /// </summary>
    private void PositionAffordances(Point point)
    {
        _lastPoint = point;
        if (Root.ActualWidth < 1 || Root.ActualHeight < 1)
        {
            return;
        }

        ColorPickerLayout.Placement placement = ColorPickerLayout.Place(
            point.X,
            point.Y,
            Root.ActualWidth,
            Root.ActualHeight,
            _loupeDiameter,
            InfoWidth,
            MeasureInfoHeight());

        Canvas.SetLeft(LoupeRing, SnapToDevicePixel(placement.Loupe.Left));
        Canvas.SetTop(LoupeRing, SnapToDevicePixel(placement.Loupe.Top));
        Canvas.SetLeft(InfoPanel, SnapToDevicePixel(placement.Info.Left));
        Canvas.SetTop(InfoPanel, SnapToDevicePixel(placement.Info.Top));
    }

    /// <summary>The info card's current height (chip + hint/status); measured because it changes on pick.</summary>
    private double MeasureInfoHeight()
    {
        InfoPanel.Measure(new Size(InfoWidth, double.PositiveInfinity));
        double height = InfoPanel.DesiredSize.Height;
        return height > 0 ? height : InfoFallbackHeight;
    }

    private const double InfoFallbackHeight = 72;

    private double SnapToDevicePixel(double dip)
    {
        double scale = _pixelsPerDip > 0 ? _pixelsPerDip : 1;
        return Math.Round(dip * scale) / scale;
    }

    /// <summary>Re-places after the card content changed (pick status/hint swap) without waiting for a mouse move.</summary>
    private void Reposition()
    {
        if (_lastPoint is not Point point)
        {
            return;
        }

        InfoPanel.UpdateLayout();
        PositionAffordances(point);
    }

    private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_picking || _settled)
        {
            return;
        }

        // Sample at the click point itself instead of trusting the last MouseMove: a click can arrive
        // before the first move (or at a different pixel than the last hover), and the pick must be the
        // pixel under the click.
        Point point = e.GetPosition(Root);
        Point screen = PointToScreen(point);
        UpdateSample((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        PositionAffordances(point);

        e.Handled = true;
        Pick();
    }

    /// <summary>Click: the sample under the cursor is approved, the HEX is written to the clipboard, the overlay closes.</summary>
    private void Pick()
    {
        if (!_hasSample)
        {
            Settle(Color.FromArgb(0, 0, 0, 0), false, Strings.ColorPicker_ReadFailed);
            return;
        }

        if (_clipboard is null)
        {
            Settle(_lastSample, false, Strings.ColorPicker_WriteFailed);
            return;
        }

        _picking = true;
        string hex = ColorMath.ToHex(_lastSample.R, _lastSample.G, _lastSample.B);
        HexText.Text = hex;
        HintText.Visibility = Visibility.Collapsed;
        SetStatus(Strings.ColorPicker_Writing);
        Reposition();

        // The write goes through the Task 7 gate: the gate paths are synchronously false and success arrives with
        // BeginInvoke — a silent false never happens, both outcomes are written on this chip (Task 7 review
        // note). The written HEX deliberately does not enter the Win+V history (CanIncludeInClipboardHistory=0,
        // product decision); verification is done by pasting with Ctrl+V.
        ArmClose(TimeSpan.FromSeconds(3)); // close if the success notification hangs
        _clipboard.PutOnClipboard(
            hex,
            completed => Settle(
                _lastSample,
                completed,
                completed
                    ? string.Format(Strings.ColorPicker_CopiedFormat, hex)
                    : Strings.ColorPicker_WriteFailed));
    }

    /// <summary>Writes the result to the chip, raises the <see cref="Settled"/> path, schedules the close.</summary>
    private void Settle(Color color, bool written, string status)
    {
        if (_settled)
        {
            return;
        }

        _settled = true;
        // The overlay is clamped for 0.9–1.6 s while the chip shows the copy result; Esc must not be
        // swallowed system-wide during that interval.
        ReleaseEscapeHotkey();
        string hex = color.A == 0 ? "#—" : ColorMath.ToHex(color.R, color.G, color.B);
        SetStatus(status);
        Reposition();
        Settled?.Invoke(color, hex, written);
        ArmClose(TimeSpan.FromMilliseconds(written ? 900 : 1600));
    }

    /// <summary>If the write does not finish healthily the chip text stops where it is; the timer breaks in and closes the overlay.</summary>
    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ArmClose(TimeSpan delay)
    {
        _closeTimer ??= new DispatcherTimer();
        _closeTimer.Tick -= OnCloseTick;
        _closeTimer.Tick += OnCloseTick;
        _closeTimer.Stop();
        _closeTimer.Interval = delay;
        _closeTimer.Start();
    }

    private void OnCloseTick(object? sender, EventArgs e)
    {
        _closeTimer?.Stop();
        if (!_settled)
        {
            // If the notification never arrived, do not leave a silent false: close with "could not write".
            Settle(_lastSample, false, Strings.ColorPicker_WriteFailed);
        }

        Close();
    }

    /// <summary>Fallback Esc path: if the hotkey registration was refused and the window somehow holds WPF keyboard focus.</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && !_picking && !_settled)
        {
            e.Handled = true;
            Cancel();
        }
    }

    /// <summary>Focus loss = the user went back to another window; it must not stay under a stale mask.</summary>
    private void OnWindowLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_armed || _picking || _settled)
        {
            return;
        }

        Cancel();
    }

    private void Cancel()
    {
        if (_settled)
        {
            return;
        }

        _settled = true;
        ReleaseEscapeHotkey();
        Cancelled?.Invoke();
        Close();
    }

    private void TryRegisterEscapeHotkey()
    {
        if (_escapeHotkey || _source is null)
        {
            return;
        }

        _escapeHotkey = HotkeyNative.RegisterHotKey(_source.Handle, EscapeHotkeyId, 0, NativeMethods.VK_ESCAPE);
    }

    private void ReleaseEscapeHotkey()
    {
        if (!_escapeHotkey || _source is null)
        {
            return;
        }

        _ = HotkeyNative.UnregisterHotKey(_source.Handle, EscapeHotkeyId);
        _escapeHotkey = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyNative.WM_HOTKEY && wParam.ToInt32() == EscapeHotkeyId)
        {
            handled = true;
            if (!_picking && !_settled)
            {
                Cancel();
            }
        }

        return IntPtr.Zero;
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _closeTimer?.Stop();
        ReleaseEscapeHotkey();
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
        }

        _snapshot = null;
        _loupeBitmap = null;
        _source = null;
        base.OnClosed(e);
    }
}
