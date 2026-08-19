using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;

namespace WharfBar.Services;

/// <summary>
/// Polls the real tray and publishes a filtered snapshot for the top bar mirror (ADR-009).
/// Hybrid source: the classic <c>ToolbarWindow32</c> via documented comctl32 TB_* messages when
/// present (real icons, older Windows); UI Automation as the Windows 11 25H2+ fallback (letter
/// glyphs, no bitmaps — the classic toolbar was removed there). Read-only: never modifies shell
/// state beyond the temporary stealth tray peek used to read/clicks; nothing to restore.
/// </summary>
internal sealed class TrayMirrorService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    private readonly Dispatcher _dispatcher;
    private readonly TaskbarVisibilityService _taskbar;
    private readonly System.Threading.Timer _timer;
    private ImageSource? _fallback;
    private IntPtr _lastToolbar;
    private bool _disposed;

    internal TrayMirrorService(Dispatcher dispatcher, TaskbarVisibilityService taskbar)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));
        _timer = new System.Threading.Timer(_ => Poll(), null, PollInterval, PollInterval);
    }

    internal event EventHandler? Changed;

    internal IReadOnlyList<TrayMirrorItem> Items { get; private set; } = [];

    internal void Reenumerate() => _ = Task.Run(Poll);

    /// <summary>
    /// Replays a click on a mirrored tray icon. Routes to the source the item came from: the
    /// classic toolbar (mouse messages at the item rect) or UIA (Invoke for left; physical click
    /// on the peeked real taskbar for right/double — UIA cannot express those).
    /// </summary>
    internal void Click(TrayMirrorItem item, TrayClick kind)
    {
        if (item.Tag is AutomationElement element)
        {
            ClickUia(element, kind);
            return;
        }

        IntPtr toolbar = _lastToolbar;
        if (toolbar != IntPtr.Zero)
        {
            TrayToolbar.ClickItem(toolbar, item.Index, kind);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
    }

    private void Poll()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            IntPtr toolbar = TrayToolbar.FindToolbarHwnd();
            if (toolbar != IntPtr.Zero)
            {
                _lastToolbar = toolbar;
                Publish(PollToolbar(toolbar));
                return;
            }

            Publish(PollUia());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WharfBar: tray mirror poll failed — {ex.Message}");
        }
    }

    private IReadOnlyList<TrayMirrorItem> PollToolbar(IntPtr toolbar)
    {
        IReadOnlyList<TrayButtonInfo> buttons = TrayToolbar.ReadButtons(toolbar);
        var items = new List<TrayMirrorItem>(buttons.Count);
        for (int i = 0; i < buttons.Count; i++)
        {
            TrayButtonInfo b = buttons[i];
            if (TraySystemIconFilter.IsSystemIcon(b.Tooltip)
                || TraySystemIconFilter.IsWharfBarIcon(b.Tooltip))
            {
                continue;
            }

            items.Add(BuildToolbarItem(toolbar, b, i));
        }

        return items;
    }

    private IReadOnlyList<TrayMirrorItem> PollUia()
    {
        // UIA only exposes the tray children while the taskbar is shown. Stealth-park it
        // (cloak + off-screen) for the read, then re-hide — no visible flash.
        if (!_taskbar.BeginTrayPeek(stealth: true))
        {
            return [];
        }

        try
        {
            IntPtr tray = TrayUiaReader.FindTrayHwnd();
            if (!TrayUiaReader.TryGetTrayButtons(out IReadOnlyList<UiaTrayButton> buttons))
            {
                return [];
            }

            var items = new List<TrayMirrorItem>(buttons.Count);
            for (int i = 0; i < buttons.Count; i++)
            {
                UiaTrayButton b = buttons[i];
                // Key by name: most tray apps keep a stable tooltip; state-bearing names
                // (e.g. EarTrumpet percent) churn that one icon — acceptable UIA-path cost.
                items.Add(new TrayMirrorItem(b.Name, i, i, b.Name, TryCaptureIcon(tray, b.Element, b.Name), b.Element));
            }

            return items;
        }
        finally
        {
            _taskbar.EndTrayPeek();
        }
    }

    /// <summary>
    /// Real icon via PrintWindow of the parked taskbar; letter glyph fallback when capture fails.
    /// </summary>
    private static ImageSource TryCaptureIcon(IntPtr tray, AutomationElement element, string name)
    {
        if (tray != IntPtr.Zero)
        {
            Rect rect = TrayUiaReader.GetIconRect(element);
            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0)
            {
                var screen = new RECT
                {
                    Left = (int)Math.Round(rect.Left),
                    Top = (int)Math.Round(rect.Top),
                    Right = (int)Math.Round(rect.Right),
                    Bottom = (int)Math.Round(rect.Bottom),
                };
                if (TrayCapture.TryGetTrayIcon(tray, screen, out ImageSource? icon) && icon is not null)
                {
                    return icon;
                }
            }
        }

        return TrayLetterGlyph.Build(name);
    }

    private TrayMirrorItem BuildToolbarItem(IntPtr toolbar, TrayButtonInfo button, int index)
    {
        ImageSource icon = Fallback;
        if (TrayToolbar.TryGetIcon(toolbar, button.ImageIndex, out IntPtr hIcon))
        {
            try
            {
                BitmapSource bmp = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                icon = bmp;
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        string key = $"{button.CommandId}\0{button.Tooltip}";
        return new TrayMirrorItem(key, button.CommandId, index, button.Tooltip, icon);
    }

    private void ClickUia(AutomationElement element, TrayClick kind)
    {
        if (kind == TrayClick.Left)
        {
            // Invoke works from the stealth peek — no flash, no cursor jump.
            if (_taskbar.BeginTrayPeek(stealth: true))
            {
                try
                {
                    TrayUiaReader.TryInvoke(element);
                }
                finally
                {
                    _taskbar.EndTrayPeek();
                }
            }

            return;
        }

        // Right / double click need a real mouse at the button's on-screen position — UIA has no
        // such gesture. Peek visibly (real position) and replay a physical click, then re-hide.
        if (!_taskbar.BeginTrayPeek(stealth: false))
        {
            return;
        }

        try
        {
            Rect rect = TrayUiaReader.GetScreenRect(element);
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            SendInputMouse.Click(
                (int)(rect.Left + rect.Width / 2),
                (int)(rect.Top + rect.Height / 2),
                kind);
        }
        finally
        {
            _taskbar.EndTrayPeek();
        }
    }

    private ImageSource Fallback => _fallback ??= BuildFallback();

    private static ImageSource BuildFallback()
    {
        var bmp = new RenderTargetBitmap(20, 20, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            dc.DrawEllipse(
                new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                null,
                new Point(10, 10),
                6,
                6);
        }

        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    private void Publish(IReadOnlyList<TrayMirrorItem> items)
    {
        if (_dispatcher.CheckAccess())
        {
            Items = items;
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _dispatcher.BeginInvoke(() =>
        {
            Items = items;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
