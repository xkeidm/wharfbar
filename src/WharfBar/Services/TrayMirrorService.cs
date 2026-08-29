using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WharfBar.Models;
using WharfBar.Native;
using System.Threading;

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
    private const int HoldMinMs = 750;
    private const int HoldMaxMs = 20000;
    private const int HoldPollMs = 120;
    private const int ClickJoinGraceMs = 3000;
    private const int ClickJoinPollMs = 50;
    private const int LiveResolveAttempts = 10;
    private const int LiveResolvePollMs = 100;

    private readonly Dispatcher _dispatcher;
    private readonly TaskbarVisibilityService _taskbar;
    private readonly System.Threading.Timer _timer;
    private ImageSource? _fallback;
    private IntPtr _lastToolbar;
    private bool _disposed;
    private int _clickBusy;
    private CancellationTokenSource? _holdCts;

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
    /// <paramref name="anchorRect"/> is the mirror icon's on-screen rect (physical px); the UIA
    /// path re-anchors the app's popup below it after the click.
    /// </summary>
    internal void Click(TrayMirrorItem item, TrayClick kind, Rect anchorRect)
    {
        if (item.Tag is not AutomationElement)
        {
            IntPtr toolbar = _lastToolbar;
            if (toolbar != IntPtr.Zero)
            {
                TrayToolbar.ClickItem(toolbar, item.Index, kind);
            }

            return;
        }

        // A previous click's flyout-hold must never eat the next click: cancel its hold, then
        // join the busy session on the BACKGROUND thread — never on the UI thread, which must
        // stay responsive (hover, further clicks) while the previous session winds down.
        _ = TryCancelHold();
        _ = Task.Run(() =>
        {
            var sw = Stopwatch.StartNew();
            while (Interlocked.Exchange(ref _clickBusy, 1) != 0)
            {
                if (sw.ElapsedMilliseconds > ClickJoinGraceMs)
                {
                    return;
                }

                Thread.Sleep(ClickJoinPollMs);
            }

            try
            {
                ClickUia(item, kind, anchorRect);
            }
            finally
            {
                Interlocked.Exchange(ref _clickBusy, 0);
            }
        });
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

    private void ClickUia(TrayMirrorItem item, TrayClick kind, Rect anchorRect)
    {
        var cts = new CancellationTokenSource();
        _holdCts = cts;

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

        // Invisible anchored click: persistent stealth styles stay on (alpha 0 — the real
        // bar never becomes visible); we drop only the click-through bit so the parked bar
        // receives the synthetic input at its bottom-edge position. App flyouts/menus anchor
        // to their icon rect THERE and render normally — they belong to the app, not to the
        // bar, so no Windows taskbar ever appears.
        if (!_taskbar.BeginTrayPeek(stealth: true))
        {
            TrayPeekDiagnostics.LogNote("click peek FAILED");
            return;
        }

        try
        {
            TrayPeekDiagnostics.LogNote($"click begin kind={kind} key='{item.Key}'");

            TaskbarNative.BeginTrayClickInput();

            // The shell rebuilds its XAML content around show/hide; resolve-first, then retry
            // until the live tree is up — no fixed settle, so a fast rebuild clicks sooner.
            // A failed resolve falls back to the stored element's rect, which sits at the same
            // parked position anyway (tray layout is stable between peeks).
            AutomationElement? target = null;
            int liveButtons = 0;
            for (int attempt = 0; attempt < LiveResolveAttempts && target is null; attempt++)
            {
                target = ResolveLiveButton(item, out liveButtons);
                if (target is null)
                {
                    Thread.Sleep(LiveResolvePollMs);
                }
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

                // The app anchors its popup by its own strategy (icon rect at the parked
                // bottom edge, restored cursor, screen corner). Move it below the mirror
                // icon the user actually clicked; bounded watcher, own thread, no-op when
                // no qualifying popup appears. Matching is geometric (button rect zone) —
                // the 25H2 XAML buttons all report explorer's pid, so pid is useless here.
                if (!anchorRect.IsEmpty && anchorRect.Width > 0 && !targetRect.IsEmpty)
                {
                    var anchor = new RECT
                    {
                        Left = (int)Math.Round(anchorRect.Left),
                        Top = (int)Math.Round(anchorRect.Top),
                        Right = (int)Math.Round(anchorRect.Right),
                        Bottom = (int)Math.Round(anchorRect.Bottom),
                    };
                    var buttonRect = new RECT
                    {
                        Left = (int)Math.Round(targetRect.Left),
                        Top = (int)Math.Round(targetRect.Top),
                        Right = (int)Math.Round(targetRect.Right),
                        Bottom = (int)Math.Round(targetRect.Bottom),
                    };
                    _ = Task.Run(() => TrayMenuReanchor.TryReanchor(buttonRect, anchor));
                }
            }
            else
            {
                bool invoked = TrayUiaReader.TryInvoke(target ?? stale);
                TrayPeekDiagnostics.LogNote($"click invoke-fallback={invoked}");
            }

            HoldPeekWhileFlyoutFocused(appPid, cts.Token);
        }
        catch (Exception ex)
        {
            TrayPeekDiagnostics.LogNote($"click ERROR: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            if (_holdCts == cts)
            {
                _holdCts = null;
            }

            TaskbarNative.EndTrayClickInput();
            _taskbar.EndTrayPeek();
            TrayPeekDiagnostics.LogNote("click end");
        }
    }

    /// <summary>
    /// Fresh UIA button matching the mirrored item's key; null when gone. Matching rules live
    /// in <see cref="TrayButtonMatcher"/> (exact → containment for named keys; unnamed items
    /// resolve by their poll-time index — never containment, which would hit the wrong app).
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
            string?[] names = new string[buttons.Count];
            for (int i = 0; i < buttons.Count; i++)
            {
                names[i] = buttons[i].Name;
            }

            int index = TrayButtonMatcher.ResolveIndex(names, item.Key, item.Index);
            return index >= 0 ? buttons[index].Element : null;
        }
        catch (Exception ex)
        {
            TrayPeekDiagnostics.LogNote($"click resolve error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Keeps the peeked bar up while its flyout/menu has focus (min hold, bounded max), then
    /// lets the caller re-hide. Runs on the click's background thread; a new incoming click
    /// cancels the wait via <paramref name="ct"/> so it is never eaten by this loop.
    /// Shell-hosted flyouts (Start-adjacent popups, some tray menus) foreground the explorer
    /// process rather than the tray app, so explorer's pid counts as flyout-focused too.
    /// </summary>
    private void HoldPeekWhileFlyoutFocused(int appPid, CancellationToken ct)
    {
        _ = NativeMethods.GetWindowThreadProcessId(TaskbarNative.FindTrayHwnd(), out uint explorerPid);
        DateTime start = DateTime.UtcNow;
        while ((DateTime.UtcNow - start).TotalMilliseconds < HoldMaxMs)
        {
            if (ct.WaitHandle.WaitOne(HoldPollMs))
            {
                return;
            }

            double elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            _ = NativeMethods.GetWindowThreadProcessId(foreground, out uint foregroundPid);
            bool flyoutFocused = (appPid != 0 && foregroundPid == (uint)appPid)
                || (explorerPid != 0 && foregroundPid == explorerPid);
            if (elapsed >= HoldMinMs && !flyoutFocused && !TrayOverflowNative.IsOverflowVisible())
            {
                break;
            }
        }
    }

    private bool TryCancelHold()
    {
        try
        {
            _holdCts?.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
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
