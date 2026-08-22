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
    private const int PeekSettleMs = 160;
    private const int HoldMinMs = 1500;
    private const int HoldMaxMs = 20000;
    private const double TopBarHeightDip = 30d;

    private readonly Dispatcher _dispatcher;
    private readonly TaskbarVisibilityService _taskbar;
    private readonly System.Threading.Timer _timer;
    private ImageSource? _fallback;
    private IntPtr _lastToolbar;
    private bool _disposed;
    private int _clickBusy;

    // UIA tree is torn down/rebuilt around every peek; a single empty read right after
    // SW_SHOW is usually that rebuild, not "user closed every tray app". Hold the last
    // good snapshot for up to two consecutive empty reads before clearing the mirror.
    private IReadOnlyList<TrayMirrorItem> _lastGoodUiaItems = [];
    private int _consecutiveEmptyUiaReads;

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
    /// classic toolbar (mouse messages at the item rect) or UIA (visible peek so the app's own
    /// flyout/menu anchors next to the top bar, then physical click where UIA can't express it).
    /// </summary>
    internal void Click(TrayMirrorItem item, TrayClick kind)
    {
        if (item.Tag is AutomationElement)
        {
            if (Interlocked.Exchange(ref _clickBusy, 1) != 0)
            {
                return;
            }

            _ = Task.Run(() =>
            {
                try
                {
                    ClickUia(item, kind);
                }
                finally
                {
                    Interlocked.Exchange(ref _clickBusy, 0);
                }
            });
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
        // (cloak/alpha + shown) for the read, then re-hide — no visible flash.
        if (!_taskbar.BeginTrayPeek(stealth: true))
        {
            return [];
        }

        try
        {
            // The show tears down and rebuilds the shell's XAML content; reading immediately
            // yields an empty/partial tree (mirror icons blink away for a poll cycle).
            Thread.Sleep(PeekSettleMs);

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

            if (items.Count == 0)
            {
                _consecutiveEmptyUiaReads++;
                if (_lastGoodUiaItems.Count > 0 && _consecutiveEmptyUiaReads < 3)
                {
                    return _lastGoodUiaItems;
                }

                return items;
            }

            _consecutiveEmptyUiaReads = 0;
            _lastGoodUiaItems = items;
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

    private void ClickUia(TrayMirrorItem item, TrayClick kind)
    {
        // The stored element is captured during a peek; the shell tears its XAML tree down
        // when we hide the bar again, so by click time it is usually dead — Invoke/rect
        // would silently no-op. Resolve a FRESH element from the live tree once the
        // visible peek has brought the tray back.
        AutomationElement stale = item.Tag as AutomationElement ?? throw new InvalidOperationException("UIA mirror click without an element.");
        int appPid;
        try
        {
            appPid = stale.Current.ProcessId;
        }
        catch (Exception)
        {
            appPid = 0;
        }

        // Visible peek: the app's flyout/menu anchors to the REAL taskbar, so it must be
        // genuinely on-screen for the user to see anything. Stealth invoke alone made the
        // flyout open against an invisible bar ("nothing happens").
        if (!_taskbar.BeginTrayPeek(stealth: false))
        {
            return;
        }

        try
        {
            TrayPeekDiagnostics.LogNote($"click begin kind={kind} key='{item.Key}'");

            Thread.Sleep(PeekSettleMs);

            // Park the real bar directly under the top bar near the cursor so the app's
            // flyout/context menu opens next to the mirrored icon instead of at the bottom.
            int anchorX = CursorX();
            bool moved = TaskbarNative.TryMoveTrayNear(anchorX, TopBarBottomY(anchorX));
            TrayPeekDiagnostics.LogNote($"click move={moved} anchorX={anchorX}");

            // The shell rebuilds its XAML content around show/move; poll for the live
            // button briefly instead of trusting one immediate read.
            AutomationElement? target = null;
            int liveButtons = 0;
            for (int attempt = 0; attempt < 8 && target is null; attempt++)
            {
                Thread.Sleep(120);
                target = ResolveLiveButton(item, out liveButtons);
            }

            TrayPeekDiagnostics.LogNote(
                target is not null
                    ? $"click live-found buttons={liveButtons}"
                    : $"click live-MISSING buttons={liveButtons}");

            Rect targetRect = target is not null
                ? TrayUiaReader.GetScreenRect(target)
                : Rect.Empty;
            if (targetRect.IsEmpty)
            {
                // Last resort: whatever rect the stored element still reports.
                targetRect = TrayUiaReader.GetScreenRect(stale);
            }

            TrayPeekDiagnostics.LogNote(
                $"click rect={targetRect.Left:F0},{targetRect.Top:F0},{targetRect.Right:F0},{targetRect.Bottom:F0} empty={targetRect.IsEmpty}");

            // Physical input for every kind — InvokePattern is unverified on the 25H2 XAML
            // tray buttons, while a real click is exactly what a human would have done.
            if (!targetRect.IsEmpty && targetRect.Width > 0 && targetRect.Height > 0)
            {
                SendInputMouse.Click(
                    (int)(targetRect.Left + targetRect.Width / 2),
                    (int)(targetRect.Top + targetRect.Height / 2),
                    kind);
                TrayPeekDiagnostics.LogNote("click sent");
            }
            else
            {
                bool invoked = TrayUiaReader.TryInvoke(target ?? stale);
                TrayPeekDiagnostics.LogNote($"click invoke-fallback={invoked}");
            }

            HoldPeekWhileFlyoutFocused(appPid);
        }
        finally
        {
            TaskbarNative.RestoreMovedTray();
            _taskbar.EndTrayPeek();
            TrayPeekDiagnostics.LogNote("click end");
        }
    }

    /// <summary>
    /// Fresh UIA button matching the mirrored item's key; null when gone. Names with live
    /// state (EarTrumpet percent) churn, so fall back to a case-insensitive/trimmed match
    /// and then to a unique substring containment.
    /// </summary>
    private static AutomationElement? ResolveLiveButton(TrayMirrorItem item, out int buttonCount)
    {
        buttonCount = 0;
        try
        {
            if (!TrayUiaReader.TryGetTrayButtons(out IReadOnlyList<UiaTrayButton> buttons))
            {
                return null;
            }

            buttonCount = buttons.Count;
            string key = item.Key.Trim();
            foreach (UiaTrayButton b in buttons)
            {
                if (string.Equals(b.Name?.Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return b.Element;
                }
            }

            UiaTrayButton? contained = buttons
                .Where(b => !string.IsNullOrWhiteSpace(b.Name))
                .FirstOrDefault(b =>
                    b.Name!.Contains(key, StringComparison.OrdinalIgnoreCase)
                    || key.Contains(b.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            return contained?.Element;
        }
        catch (Exception ex)
        {
            TrayPeekDiagnostics.LogNote($"click resolve error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Keeps the peeked bar up while its flyout/menu has focus (min hold, bounded max), then
    /// lets the caller re-hide. Runs on the click's background thread.
    /// </summary>
    private void HoldPeekWhileFlyoutFocused(int appPid)
    {
        DateTime start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalMilliseconds < HoldMaxMs)
        {
            Thread.Sleep(250);
            double elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            _ = NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundPid);
            bool flyoutFocused = appPid != 0 && foregroundPid == (uint)appPid;
            if (elapsed >= HoldMinMs && !flyoutFocused && !TrayOverflowNative.IsOverflowVisible())
            {
                break;
            }
        }
    }

    private static int CursorX()
    {
        return NativeMethods.GetCursorPos(out POINT point) ? point.X : 0;
    }

    private static int TopBarBottomY(int anchorX)
    {
        IReadOnlyList<MonitorInfo> monitors = MonitorNative.Enumerate();
        MonitorInfo? monitor = monitors.FirstOrDefault(m =>
                m.Bounds.Left <= anchorX && anchorX < m.Bounds.Right && m.IsPrimary)
            ?? monitors.FirstOrDefault(m => m.Bounds.Left <= anchorX && anchorX < m.Bounds.Right)
            ?? monitors.FirstOrDefault(m => m.IsPrimary)
            ?? monitors.FirstOrDefault();

        if (monitor is null)
        {
            return 0;
        }

        int barPx = Display.DipToPixels(TopBarHeightDip, monitor.Dpi);
        return monitor.Bounds.Top + barPx + 4;
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
