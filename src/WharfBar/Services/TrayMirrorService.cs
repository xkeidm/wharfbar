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
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1500);

    /// <summary>At most one state blip per this interval — the blip flaps the work area.</summary>
    internal static readonly TimeSpan BlipMinInterval = TimeSpan.FromSeconds(60);

    private const int PeekSettleMs = 160;
    private const int HoldMinMs = 750;
    private const int HoldMaxMs = 20000;
    private const int HoldPollMs = 120;
    private const int ClickJoinGraceMs = 3000;
    private const int ClickJoinPollMs = 50;
    private const int LiveResolveAttempts = 10;
    private const int LiveResolvePollMs = 100;

    /// <summary>
    /// Kaç ardışık boş UIA okuması boyunca son iyi snapshot tutulur. Sabit <b>değil,
    /// türetilir</b>: eşik bir <b>tam blip aralığını</b> aşmak zorunda, çünkü park
    /// halinde (25H2'de explorer tray'i sanallaştırır) doğrudan okuma her
    /// <see cref="PollInterval"/>'da boş döner ve yalnızca blip ikonları geri getirir; blip de
    /// <see cref="BlipMinInterval"/> ile kısıtlıdır. Daha kısa bir eşik, aynayı o boşluğun
    /// ortasında temizler ve ikonlar her blip döngüsünde kaybolup geri gelir — ölçülen
    /// regresyon: 3 okuma × 1.5 sn = 4.5 sn grace, 60 sn'lik boşluğun yalnızca %7.5'inde
    /// ikonları görünür kılıyordu. <c>+2</c> tolerans: tam blip aralığı dolduğunda yeni blip
    /// henüz bir sonraki zamanlayıcı dilimini bekler, iki okuma bu kenar dalgalanmasını yutar.
    /// </summary>
    internal static readonly int EmptyReadGraceReads =
        (int)Math.Ceiling(BlipMinInterval.TotalMilliseconds / PollInterval.TotalMilliseconds) + 2;

    /// <summary>
    /// Bir blip neden ikon döndürmedi. Boş okuma grace'i tam olarak bu ayrımı ister:
    /// yalnız <see cref="ConfirmedEmpty"/> tray hakkında <b>kanıttır</b> (tray gösterildi,
    /// yine de boş okundu); diğer her değer "hiç bakmadık" demektir ve grace sürdürülür.
    /// </summary>
    internal enum TrayBlipOutcome
    {
        /// <summary>Hiç denenmedi: throttle, tıklamada, ya da birincilde maximize pencere. Kanıt değil.</summary>
        Skipped,

        /// <summary>Denendi ama peek açılmadı / UIA gezintisi patladı — tray hiç okunmadı. Kanıt değil.</summary>
        Inconclusive,

        /// <summary>Denendi, tray gösterildi ve okuma boş döndü: tray gerçekten boş.</summary>
        ConfirmedEmpty,

        /// <summary>Denendi ve ikon okundu (ayrıca döndürüldü).</summary>
        Refreshed,
    }

    private readonly Dispatcher _dispatcher;
    private readonly TaskbarVisibilityService _taskbar;
    private readonly System.Threading.Timer _timer;
    private Dispatcher? _staDispatcher;
    private ImageSource? _fallback;
    private IntPtr _lastToolbar;
    private bool _disposed;
    private int _clickBusy;
    private CancellationTokenSource? _holdCts;

    // UIA tree is torn down/rebuilt around every peek; a single empty read right after
    // SW_SHOW is usually that rebuild, not "user closed every tray app". Hold the last
    // good snapshot for EmptyReadGraceReads consecutive empty reads before clearing the
    // mirror — the grace must stay BOUNDED but also outlast a whole blip interval, see
    // the empty branch in PollUia.
    private IReadOnlyList<TrayMirrorItem> _lastGoodUiaItems = [];
    private int _consecutiveEmptyUiaReads;
    // Once UIA has delivered icons, the mirror source is locked for the session: the
    // classic toolbar can transiently reappear during state blips (unpark windows) and
    // its differently-keyed items would wipe and re-add the whole mirror on every flip.
    private bool _uiaLocked;

    internal TrayMirrorService(Dispatcher dispatcher, TaskbarVisibilityService taskbar)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _taskbar = taskbar ?? throw new ArgumentNullException(nameof(taskbar));

        // Desktop-scope UIA walks (TrayUiaReader) return empty/throw on MTA threads and
        // need a pumping STA apartment, so every read runs on this dedicated STA
        // dispatcher thread; timers/clicks just schedule work onto it.
        var staThread = new Thread(() =>
        {
            _staDispatcher = Dispatcher.CurrentDispatcher;
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WharfBar.TrayMirror",
        };
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();

        _timer = new System.Threading.Timer(
            _ => _ = _staDispatcher?.BeginInvoke(Poll, DispatcherPriority.Background),
            null,
            PollInterval,
            PollInterval);
    }

    /// <summary>Queues <paramref name="action"/> onto the STA dispatcher and waits for it.</summary>
    private T RunOnSta<T>(Func<T> action)
    {
        Dispatcher? dispatcher = _staDispatcher;
        if (dispatcher is null)
        {
            throw new InvalidOperationException("Tray mirror STA worker not started.");
        }

        return dispatcher.Invoke(action, DispatcherPriority.Send);
    }

    internal event EventHandler? Changed;

    internal IReadOnlyList<TrayMirrorItem> Items { get; private set; } = [];

    internal void Reenumerate() => _ = _staDispatcher?.BeginInvoke(Poll, DispatcherPriority.Background);

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
        _staDispatcher?.BeginInvokeShutdown(DispatcherPriority.Send);
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
            if (toolbar != IntPtr.Zero && !_uiaLocked)
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
        // Desktop-root UIA read (TrayUiaReader). While the strip-release auto-hide state
        // is on, explorer virtualizes the tray content away and this read returns empty;
        // a state blip (see TryRefreshViaBlip) re-exposes it only when that is invisible.
        // Poll itself already runs on the STA worker (queued there by the timer), so a
        // direct call — RunOnSta here would deadlock on the queue it is blocking.
        IReadOnlyList<UiaTrayButton>? buttons = TrayUiaReader.TryGetTrayButtons();
        TrayBlipOutcome blipOutcome = TrayBlipOutcome.Skipped;
        if (buttons is null || buttons.Count == 0)
        {
            IReadOnlyList<TrayMirrorItem>? blip = TryRefreshViaBlip(out blipOutcome);
            if (blip is { Count: > 0 })
            {
                _consecutiveEmptyUiaReads = 0;
                _lastGoodUiaItems = blip;
                _uiaLocked = true;
                return blip;
            }
        }

        // Null (walk failed), empty (virtualized parked tray) and a blip that proved nothing all
        // mean "no fresh data" — fall through to the last-good grace instead of publishing an
        // empty snapshot that would collapse the mirror 200ms after every refresh.
        buttons ??= [];

        IntPtr tray = TrayUiaReader.FindTrayHwnd();

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
            // While the strip-release auto-hide state is on, an empty read is usually the shell
            // virtualizing the parked tray, not "the user closed every tray app" — so the last
            // good snapshot is held for a BOUNDED number of polls (EmptyReadGraceReads) before
            // the mirror is cleared. The bound must outlast a WHOLE blip interval: the blip is
            // the only thing that re-exposes the parked tray and it runs at most once per
            // BlipMinInterval, so a shorter grace blanks the mirror in the middle of every
            // throttle gap (measured regression: 3 reads x 1.5 s of grace against a 60 s gap —
            // icons visible 7.5 % of the time).
            //
            // A blip that actually RAN and came back empty is the answer the grace was waiting
            // for — the tray was shown and still had nothing — so it clears at once. A
            // throttled / blocked / failed blip never looked at the tray and keeps holding.
            _consecutiveEmptyUiaReads++;
            if (ShouldHoldSnapshotOnEmptyRead(
                    _consecutiveEmptyUiaReads,
                    _lastGoodUiaItems.Count,
                    blipOutcome == TrayBlipOutcome.ConfirmedEmpty))
            {
                return _lastGoodUiaItems;
            }

            // Grace exhausted, or the tray proven empty: it really is empty. Drop the held
            // snapshot too, otherwise a later single empty read would resurrect the stale items —
            // and their UIA elements with them, which is what a mirrored click forwards to.
            _lastGoodUiaItems = [];
            _consecutiveEmptyUiaReads = 0;
            return items;
        }

        _consecutiveEmptyUiaReads = 0;
        _lastGoodUiaItems = items;
        if (items.Count > 0)
        {
            _uiaLocked = true;
        }

        return items;
    }

    /// <summary>
    /// Pure: does an empty UIA read keep the held snapshot on the bar, or clear the mirror?
    /// Clear the moment the grace is used up — a bounded freeze is a grace period, not a cache.
    /// <paramref name="trayConfirmedEmptyByBlip"/> short-circuits the grace: a blip that really
    /// ran already proved the tray has nothing, which is what the grace was waiting for.
    /// </summary>
    internal static bool ShouldHoldSnapshotOnEmptyRead(
        int consecutiveEmptyReads,
        int heldItemCount,
        bool trayConfirmedEmptyByBlip) =>
        !trayConfirmedEmptyByBlip
        && consecutiveEmptyReads < EmptyReadGraceReads
        && heldItemCount > 0;

    /// <summary>
    /// Pure: what a blip that actually ran proves. Split out of the peek plumbing so the
    /// "only a shown tray counts as evidence" rule is testable without Win32 — the caller must
    /// never hand <see cref="TrayBlipOutcome.ConfirmedEmpty"/> to a blip that never looked.
    /// </summary>
    internal static TrayBlipOutcome ClassifyBlipRead(bool trayShown, bool readWalkFailed, int readCount) =>
        !trayShown || readWalkFailed ? TrayBlipOutcome.Inconclusive
            : readCount > 0 ? TrayBlipOutcome.Refreshed
            : TrayBlipOutcome.ConfirmedEmpty;

    private DateTime _lastBlip;

    /// <summary>
    /// Briefly shows the tray so the shell re-exposes its buttons, reads them, and captures the
    /// icons <b>while the peek is still open</b> — PrintWindow needs a shown, rendered window;
    /// once the peek ends the tray is hidden again and every capture comes back a solid black
    /// DIB. Icons captured inside the peek are returned ready to publish; null whenever
    /// <paramref name="outcome"/> is not <see cref="TrayBlipOutcome.Refreshed"/>. Runs on the
    /// STA worker (Poll already does).
    /// </summary>
    /// <param name="outcome">
    /// Neden ikon dönmedi — boş okuma grace'inin hammaddesi. Yalnız
    /// <see cref="TrayBlipOutcome.ConfirmedEmpty"/> kanıttır.
    /// </param>
    private IReadOnlyList<TrayMirrorItem>? TryRefreshViaBlip(out TrayBlipOutcome outcome)
    {
        // Cheap gates first: HasMaximizedWindowOnPrimary enumerates every top-level window and
        // must not run on the (common) throttled poll.
        if ((DateTime.UtcNow - _lastBlip) < BlipMinInterval || _clickBusy != 0)
        {
            outcome = TrayBlipOutcome.Skipped;
            return null;
        }

        if (HasMaximizedWindowOnPrimary())
        {
            // Deliberately does NOT stamp _lastBlip: the blip flaps the work area, which a
            // maximized window would visibly absorb — so it is skipped, and the next poll is
            // free to blip the moment the window leaves the screen. That is what resolves the
            // mirror (icons back, or proven empty) within one poll of the block lifting.
            outcome = TrayBlipOutcome.Skipped;
            return null;
        }

        _lastBlip = DateTime.UtcNow;
        TaskbarNative.SetState(0);
        _taskbar.BeginIconRefresh();
        bool shown = false;
        bool readWalkFailed = true;
        int readCount = 0;
        try
        {
            shown = _taskbar.BeginTrayPeek(stealth: true);
            if (shown)
            {
                // The tray may still sit at its parked position when the state drops; slide it
                // fully on-screen (no clamp at state 0) so the UIA rects line up with the
                // window's own coordinate space before the crop is computed.
                IntPtr tray = TrayUiaReader.FindTrayHwnd();
                if (tray != IntPtr.Zero)
                {
                    TaskbarNative.EnsureTrayOnScreen(tray);
                }

                Thread.Sleep(PeekSettleMs);

                IReadOnlyList<UiaTrayButton>? read = TrayUiaReader.TryGetTrayButtons();
                readWalkFailed = read is null;
                readCount = read?.Count ?? 0;
                if (read is { Count: > 0 })
                {
                    var items = new List<TrayMirrorItem>(read.Count);
                    for (int i = 0; i < read.Count; i++)
                    {
                        UiaTrayButton b = read[i];
                        // Key by name: most tray apps keep a stable tooltip; state-bearing names
                        // (e.g. EarTrumpet percent) churn that one icon — acceptable UIA-path cost.
                        items.Add(new TrayMirrorItem(b.Name, i, i, b.Name, TryCaptureIcon(tray, b.Element, b.Name), b.Element));
                    }

                    outcome = TrayBlipOutcome.Refreshed;
                    return items;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: tray icon refresh blip failed — {ex.Message}");
        }
        finally
        {
            _taskbar.EndTrayPeek();
            _taskbar.EndIconRefresh();
        }

        // Peek that never opened, or a UIA walk that threw: nothing was read, so nothing is
        // proved — the grace keeps holding. Only a shown tray that read back zero buttons is
        // evidence of an empty tray.
        outcome = ClassifyBlipRead(shown, readWalkFailed, readCount);
        return null;
    }

    /// <summary>True when a maximized top-level window sits on the primary monitor.</summary>
    private static bool HasMaximizedWindowOnPrimary()
    {
        bool maximized = false;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (maximized
                || !NativeMethods.IsWindowVisible(hwnd)
                || NativeMethods.IsIconic(hwnd)
                || !NativeMethods.IsZoomed(hwnd))
            {
                return true;
            }

            IntPtr monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
            var info = new MONITORINFO
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
            };
            if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info)
                || (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) == 0)
            {
                return true;
            }

            maximized = true;
            return false;
        }, IntPtr.Zero);
        return maximized;
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

        // Invisible anchored click: persistent stealth styles stay on (alpha 0 — the real
        // bar never becomes visible); we drop only the click-through bit so the parked bar
        // receives the synthetic input at its bottom-edge position. App flyouts/menus anchor
        // to their icon rect THERE and render normally — they belong to the app, not to the
        // bar, so no Windows taskbar ever appears.
        // State blip first: while ABS_AUTOHIDE is on, explorer parks the tray off-screen
        // and clamps it, so forwarded clicks would land outside the monitor. Dropping the
        // state for the interaction lets the tray sit at its normal on-screen position;
        // ApplyHide re-applies auto-hide when the peek ends (gap re-check).
        TaskbarNative.SetState(0);
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

            // The app's flyout/menu is its own top-level popup and survives the tray
            // re-hide, so the peek is not held while it is focused — ending the peek
            // immediately keeps the work-area flap (maximized-window shrink) to a
            // fraction of a second. Wait briefly so the popup spawns and
            // TrayMenuReanchor can latch its geometry before the state flips back.
            Thread.Sleep(300);
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
    private AutomationElement? ResolveLiveButton(TrayMirrorItem item, out int buttonCount)
    {
        buttonCount = 0;
        try
        {
            IReadOnlyList<UiaTrayButton>? buttons = RunOnSta(() => TrayUiaReader.TryGetTrayButtons());
            if (buttons is null)
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
