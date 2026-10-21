using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WharfBar.Native;
using WharfBar.Services;

namespace WharfBar.Views;

/// <summary>
/// Ekran rengi seçici örtüsü: birincil monitörün <b>çalışma alanını</b> kaplayan şeffaf
/// topmost pencere. <c>WallpaperTransitionWindow</c>'un pencere-flag kalıbını izler —
/// AppBar penceresi değildir, Mica ile buluşmaz (<c>AllowsTransparency</c> yanında
/// <c>DwmSystemBackdrop.None</c> zorunlu), açıkken odak çalmaz
/// (<c>WS_EX_NOACTIVATE</c> + <c>ShowActivated=false</c>).
///
/// <para><b>Örnekleme donmuş kare:</b> karartma maskesinin kendisi katmanlı (layered)
/// bir penceredir ve GDI <c>BitBlt</c> bileşik masaüstünü okur — maske yerine oturunca
/// canlı örnekleme maskeli (karartılmış) rengi verir. Bu yüzden örtü gösterilmeden
/// önce monitörün tek BitBlt karesi alın ve örneklem bu kareden
/// <see cref="ScreenSampler.AverageBgra"/> ile hesaplanır; kare alınamazsa son çare
/// canlı <see cref="ScreenSampler.RgbAt"/> (düşük-vasıf, maskeli) kullanılır. Tıklama
/// noktası <see cref="Window.PointToScreen"/> ile mutlak fiziksel px'e döner (PMv2).</para>
///
/// <para><b>Kapanış:</b> tıkla (örnekleyip hex'i <see cref="IslandService.PutOnClipboard"/>
/// ile panoya yaz), Esc (vazgeç) ya da klavye odağının kaybı. Ada paneli bu örtü
/// açılmadan KAPANIR; seçim tamamlandıktan sonra üst bar hub'ı geri sergiler — örtü
/// açıkken başka üstmost pencere yoktur, örtü kullanıcı onaylı tek istisnadır.</para>
/// </summary>
public partial class ColorPickerWindow : Window
{
    /// <summary>Lupa + HEX kartı imleçten şu kadar DIP ileride durur.</summary>
    internal const double LoupeOffsetDip = 18;

    private const double LoupeSwatchDip = 72;

    private HwndSource? _source;
    private IslandService? _clipboard;
    private DispatcherTimer? _closeTimer;

    /// <summary>Donmuş BitBlt karesi (BGRA, top-down); ekran orijini <c>_snapX/_snapY</c>.</summary>
    private byte[]? _snapshot;
    private int _snapX, _snapY, _snapW, _snapH, _snapStride;

    /// <summary>Son geçerli örnek: imleç altındaki kutu ortalaması.</summary>
    private Color _lastSample;
    private bool _hasSample;

    private bool _armed;
    private bool _picking;
    private bool _settled;
    private bool _closed;

    /// <summary>Bir kez yükselir: kullanıcı rengi onayladı, HEX yazılmış ya da reddedilmiş.</summary>
    internal event Action<Color, string, bool>? Settled;

    /// <summary>Örtü bir renk seçmeden kapandı (Esc / klavye odak kaybı).</summary>
    internal event Action? Cancelled;

    public ColorPickerWindow()
    {
        InitializeComponent();
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
        }
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

    /// <summary>Örtü açıkken üst bar ikinci örtü sorgusu için kullanır (tek örtü kuralı).</summary>
    internal bool IsOpen => IsLoaded && !_settled;

    /// <summary>
    /// Örtüyü açar: çalışma alanına yerleştirir (önce donmuş kare). Kare alma tek seferlik
    /// eşzamanlıdır (onlarca ms); ada paneli <see cref="Start"/> çağrısından önce kapanmıştır,
    /// bu yüzden karede ada kalmaz. Kare alınamazsa örtü canlı örnekleme ile düşük vasıfta
    /// yine de açılır. <paramref name="clipboard"/> null ise tık hâlâ örnekler ama panoya
    /// yazma olmadan sonuç belirtilir.
    /// </summary>
    /// <returns>Gösterim başarısızsa <c>false</c> — üst bar örtüsüz kalır.</returns>
    /// <param name="anchorHwnd">Birincil monitör çıkarılırken kullanılan üst bar hwnd'i.</param>
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

        RECT work = Display.GetPrimaryMonitorWorkArea(anchorHwnd);
        int widthPx = work.Right - work.Left;
        int heightPx = work.Bottom - work.Top;
        if (widthPx < 8 || heightPx < 8)
        {
            return false;
        }

        TakeSnapshot(Display.GetMonitorBounds(anchorHwnd));

        Show();
        IntPtr hwnd = _source?.Handle ?? IntPtr.Zero;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        _ = NativeMethods.SetWindowPos(
            hwnd,
            NativeMethods.HWND_TOPMOST,
            work.Left,
            work.Top,
            widthPx,
            heightPx,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);

        // Klavye odağı, OS aktivasyonu olmadan: WPF InputManager tuşları Keyboard.FocusedElement
        // olarak yürüttüğü için WM_KEYDOWN hangi kendi penceresine düşerse düşsün Esc çalışır.
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
            }));

        return true;
    }

    /// <summary>
    /// Monitör karesi. Kare çalışma alanı değil monitördür — imleç çalışma alanına bağlı,
    /// ama <c>ScreenSampler.BoxSize</c> kutusu kenara kayabilir; kare geniş kapsar.
    /// </summary>
    private void TakeSnapshot(RECT monitor)
    {
        int width = monitor.Right - monitor.Left;
        int height = monitor.Bottom - monitor.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        int size = ScreenSampler.BoxSize;
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
        LoupeSwatch.Fill = fill;
        HexText.Text = ColorMath.ToHex(sample.R, sample.G, sample.B);
    }

    /// <summary>Piksel → renk: donmuş kareden; kare yoksa canlı ekran örneklemi (düşük-vasıf).</summary>
    private Color SampleAtPx(int pxX, int pxY)
    {
        if (_snapshot is null)
        {
            return ScreenSampler.RgbAt(pxX, pxY);
        }

        int size = ScreenSampler.BoxSize;
        int relX = pxX - _snapX;
        int relY = pxY - _snapY;
        if (relX < 0 || relY < 0 || relX >= _snapW || relY >= _snapH)
        {
            return Color.FromArgb(0, 0, 0, 0);
        }

        int x0 = Math.Clamp(relX - size / 2, 0, Math.Max(0, _snapW - size));
        int y0 = Math.Clamp(relY - size / 2, 0, Math.Max(0, _snapH - size));
        var crop = new byte[size * size * 4];
        for (int row = 0; row < size; row++)
        {
            Buffer.BlockCopy(_snapshot, (y0 + row) * _snapStride + x0 * 4, crop, row * 4 * size, 4 * size);
        }

        return ScreenSampler.AverageBgra(crop, size * 4, size);
    }

    private void PositionAffordances(Point point)
    {
        double clampW = Math.Max(1, ActualWidth);
        double clampH = Math.Max(1, ActualHeight);

        Canvas.SetLeft(CrossV, Math.Clamp(point.X, 0, clampW));
        CrossV.Height = clampH;
        Canvas.SetLeft(CrossH, 0);
        Canvas.SetTop(CrossH, Math.Clamp(point.Y, 0, clampH));
        CrossH.Width = clampW;

        double loupeW = LoupeSwatchDip;
        double loupeH = LoupeSwatchDip + 8 + Math.Max(1, HexCard.ActualHeight);
        double left = Math.Clamp(point.X + LoupeOffsetDip, 0, Math.Max(0, clampW - loupeW));
        double top = Math.Clamp(point.Y + LoupeOffsetDip, 0, Math.Max(0, clampH - loupeH));
        // Grid child: Canvas attached özellikleri yok sayılır; Margin ile yerleşir.
        LoupeLayer.Margin = new Thickness(left, top, 0, 0);
    }

    private void OnRootPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_picking || _settled)
        {
            return;
        }

        e.Handled = true;
        Pick();
    }

    /// <summary>Tık: imleç altındaki örnek onaylanır, HEX panoya yazılır, örtü kapanır.</summary>
    private void Pick()
    {
        if (!_hasSample)
        {
            Settle(Color.FromArgb(0, 0, 0, 0), false, "Renk okunamadı");
            return;
        }

        if (_clipboard is null)
        {
            Settle(_lastSample, false, "✗ Panoya yazılamadı");
            return;
        }

        _picking = true;
        string hex = ColorMath.ToHex(_lastSample.R, _lastSample.G, _lastSample.B);
        HexText.Text = hex;
        HintText.Visibility = Visibility.Collapsed;
        SetStatus("Panoya yazılıyor…");

        // Yazma Task 7 kapısından geçer: gate yolları senkron false, başarı BeginInvoke ile
        // gelir — sessiz kalan false olmaz, her iki sonuç bu karta yazılır (Task 7 inceleme
        // notu). Yazılan HEX bilinçli olarak Win+V geçmişine girmez (CanIncludeInClipboardHistory=0,
        // ürün kararı); doğrulama Ctrl+V yapıştırmayla yapılır.
        ArmClose(TimeSpan.FromSeconds(3)); // başarı bildirimi asılı kalırsa kapanış
        _clipboard.PutOnClipboard(
            hex,
            completed => Settle(_lastSample, completed, completed ? $"✓ {hex} kopyalandı" : "✗ Panoya yazılamadı"));
    }

    /// <summary>Sonucu karta yazar, <see cref="Settled"/> yolunu yükseltir, kapanışı zamanlar.</summary>
    private void Settle(Color color, bool written, string status)
    {
        if (_settled)
        {
            return;
        }

        _settled = true;
        string hex = color.A == 0 ? "#—" : ColorMath.ToHex(color.R, color.G, color.B);
        SetStatus(status);
        Settled?.Invoke(color, hex, written);
        ArmClose(TimeSpan.FromMilliseconds(written ? 900 : 1600));
    }

    /// <summary>Yazma sağlıklı bitmezse karta yazı durur; zaman kırarak örtüyü kapatır.</summary>
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
            // Bildirim hiç gelmediyse sessiz false kalmasın: "yazılamadı" ile kapan.
            Settle(_lastSample, false, "✗ Panoya yazılamadı");
        }

        Close();
    }

    private void OnRootPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _picking || _settled)
        {
            return;
        }

        e.Handled = true;
        Cancel();
    }

    /// <summary>Odak kaybı = kullanıcı başka pencereye döndü; bayat maskenin altında kalmasın.</summary>
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
        _settled = true;
        Cancelled?.Invoke();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _closeTimer?.Stop();
        _snapshot = null;
        _source = null;
        base.OnClosed(e);
    }
}
