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
/// <para>
/// Quiet refresh (25H2 path): the tray is parked in a 2 px auto-hide sliver while WharfBar runs
/// and explorer virtualizes its XAML buttons, so the read hover-reveals the tray exactly like the
/// click path does (<see cref="TrayAutoHideReveal.TryReveal"/>) — no <c>ABM_SETSTATE</c>, no
/// work-area change, no window jump. The cursor is restored afterwards and the reveal only runs
/// while the pointer is idle (or while the mirror is empty), see <see cref="TrayRevealPolicy"/>.
/// </para>
/// </summary>
internal sealed class TrayMirrorService : IDisposable
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Quiet refresh interval while icons are stable; it tightens to
    /// <see cref="TrayIconRefreshPolicy.ActiveShowOnlyInterval"/> while they change.
    /// </summary>
    internal static readonly TimeSpan BlipMinInterval = TimeSpan.FromSeconds(60);

    private const int PeekSettleMs = 160;

    /// <summary>
    /// Second read before a shown tray may be declared empty: explorer rebuilds the XAML tray
    /// around every show, so the first read right after the reveal is often the rebuild, not the
    /// tray (a false <see cref="TrayBlipOutcome.ConfirmedEmpty"/> used to wipe the whole mirror).
    /// </summary>
    private const int EmptyConfirmDelayMs = 220;

    /// <summary>Hover-reveal budget for a click or a quiet read (auto-hide slide-in is ~0.3–0.6 s).</summary>
    private const int RevealTimeoutMs = 1200;

    /// <summary>Taskbar strip height the state flip would reserve (physical px, 100–150 % DPI).</summary>
    private const int ReservedStripPx = 72;

    /// <summary>
    /// How long a right-click keeps the real tray up and the cursor on the button. Slow owners
    /// (command palette, first click of a cold app) build the menu after the old 300 ms peek
    /// had already hidden the tray, so the first clicks produced no menu.
    /// </summary>
    private const int RightClickPeekHoldMs = 1000;
    private const int HoldMinMs = 750;
    private const int HoldMaxMs = 20000;
    private const int HoldPollMs = 120;
    private const int ClickJoinGraceMs = 3000;
    private const int LiveResolveAttempts = 10;
    private const int LiveResolvePollMs = 100;

    /// <summary>
    /// Why a refresh returned no icons. The decision to clear the mirror needs exactly this distinction:
    /// only <see cref="ConfirmedEmpty"/> is <b>evidence</b> about the tray (the tray was shown,
    /// yet read as empty); every other value means "we never looked", and the snapshot is kept.
    /// </summary>
    internal enum TrayBlipOutcome
    {
        /// <summary>Never attempted: throttled, mid-click, user busy, or full screen. Not evidence.</summary>
        Skipped,

        /// <summary>Attempted but the peek did not open / reveal timed out / UIA navigation threw — the tray was never read.</summary>
        Inconclusive,

        /// <summary>Attempted, the tray was shown, and the read came back empty: the tray really is empty.</summary>
        ConfirmedEmpty,

        /// <summary>Attempted and icons were read (and also returned).</summary>
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

    // The quiet reveal and a mirror click both move the real pointer and restore it. One gate
    // serializes every cursor-owning session (poll runs on the STA worker, click on a background
    // thread); the active lease is kept in a field so Dispose can put the pointer back when the
    // process quits mid-reveal (the STA thread is a background thread and may be killed).
    private readonly SemaphoreSlim _cursorGate = new(1, 1);
    private readonly object _cursorLeaseLock = new();
    private readonly object _holdLock = new();
    private SendInputMouse.CursorLease? _activeCursorLease;

    // A parked tray reads empty on every poll; that is not evidence and must never blank the
    // bar. Only a refresh that actually looked at a shown tray (and confirmed it empty) may
    // clear the mirror — a stale icon beats a blank bar (and a work-area jump).
    private readonly TrayIconCache<ImageSource> _iconCache = new();
    private IReadOnlyList<TrayMirrorItem> _lastGoodUiaItems = [];
    private IReadOnlyList<TrayMirrorItem> _lastPublished = [];
    private int _lastDirectReadCount = int.MinValue;

    // Out-of-band refresh request (UTC ticks, 0 = none): after a mirror click, a theme change,
    // or an inconclusive refresh. Written from the UI / click threads, consumed on the STA.
    private long _expediteDueTicks;
    private bool _inconclusiveRetryUsed;
    private bool _iconsRecentlyChanged;
    private DateTime _lastPixelCapture;

    // State-blip bookkeeping, per explorer instance (reset when Shell_TrayWnd changes).
    private IntPtr _shellTray;
    private bool _stateBlippedThisShell;
    private bool _stateBlipConfirmedEmpty;
    private DateTime _lastStateChange;
    private int _revealFailures;
    private long _lastRevealFailureTicks;
    // Quiet-reveal attempts spent from the empty-mirror idle bypass in this shell session; once
    // used up, a genuinely empty tray does not keep hopping the cursor behind a busy user.
    private int _emptyMirrorBypassAttempts;
    // Once UIA has delivered icons, the mirror source is locked for the session: the
    // classic toolbar can transiently reappear during state blips (unpark windows) and
    // its differently-keyed items would wipe and re-add the whole mirror on every flip.
    private bool _uiaLocked;

    /// <summary>Last quiet (reveal) refresh attempt — throttles the cursor hop, see <see cref="TrayIconRefreshPolicy"/>.</summary>
    private DateTime _lastQuietRefresh;

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

    /// <summary>
    /// Takes the cursor-home lease and remembers it as the active one so the exit path can put
    /// the pointer back if it never reaches its <c>finally</c>.
    /// </summary>
    private SendInputMouse.CursorLease AcquireCursorLease()
    {
        SendInputMouse.CursorLease lease = SendInputMouse.HoldCursorHome();
        lock (_cursorLeaseLock)
        {
            _activeCursorLease = lease;
        }

        return lease;
    }

    /// <summary>Releases a cursor lease (idempotent; safe from the exit path as well).</summary>
    private void ReleaseCursorLease(SendInputMouse.CursorLease? lease)
    {
        if (lease is null)
        {
            return;
        }

        lock (_cursorLeaseLock)
        {
            if (ReferenceEquals(_activeCursorLease, lease))
            {
                _activeCursorLease = null;
            }
        }

        lease.Dispose();
    }

    /// <summary>Puts the pointer back for a lease an interrupted session never released.</summary>
    private void RestoreActiveCursor()
    {
        SendInputMouse.CursorLease? lease;
        lock (_cursorLeaseLock)
        {
            lease = _activeCursorLease;
            _activeCursorLease = null;
        }

        lease?.Dispose();
    }

    private DateTime LastRevealFailureUtc =>
        new(Interlocked.Read(ref _lastRevealFailureTicks), DateTimeKind.Utc);

    private void RecordRevealMiss(DateTime now)
    {
        Interlocked.Exchange(ref _lastRevealFailureTicks, now.Ticks);
        Interlocked.Increment(ref _revealFailures);
    }

    private void RecordRevealSuccess() => Interlocked.Exchange(ref _revealFailures, 0);

    private bool IsRevealBackedOff(DateTime now) =>
        TrayRevealPolicy.IsRevealBackedOff(
            Volatile.Read(ref _revealFailures),
            now - LastRevealFailureUtc);

    internal event EventHandler? Changed;

    internal IReadOnlyList<TrayMirrorItem> Items { get; private set; } = [];

    internal void Reenumerate() => _ = _staDispatcher?.BeginInvoke(Poll, DispatcherPriority.Background);

    /// <summary>
    /// Windows theme / accent / colorization changed: tray apps swap their light/dark icon
    /// variants and the captured crops carry the old taskbar paint. Asks for one pixel refresh
    /// shortly after explorer has repainted (rate-limited; no new timer — the poll picks it up).
    /// </summary>
    internal void NotifyAppearanceChanged() => RequestExpeditedRefresh(TrayIconRefreshPolicy.AppearanceSettleDelay);

    private void RequestExpeditedRefresh(TimeSpan delay) =>
        Interlocked.Exchange(ref _expediteDueTicks, (DateTime.UtcNow + delay).Ticks);

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
            if (toolbar == IntPtr.Zero)
            {
                return;
            }

            RECT anchor = ToNativeRect(anchorRect);
            if (anchor.Width > 0 && TrayToolbar.TryGetItemScreenRect(toolbar, item.Index, out RECT button))
            {
                // Snapshot before SendMessage: the owner may create the menu inside that call.
                BeginReanchor(button, anchor);
            }

            TrayToolbar.ClickItem(toolbar, item.Index, kind);
            return;
        }

        // A previous click's flyout-hold must never eat the next click: cancel its hold. The
        // busy flag is claimed SYNCHRONOUSLY here — the STA poll checks it at entry, and a quiet
        // reveal must never start between this call being queued and its Task.Run beginning.
        // While another session owns the cursor the click is dropped (a dropped click beats two
        // threads restoring the pointer over each other).
        _ = TryCancelHold();
        if (Interlocked.CompareExchange(ref _clickBusy, 1, 0) != 0)
        {
            TrayPeekDiagnostics.LogNote("click skipped: another click session owns the cursor");
            return;
        }

        _ = Task.Run(() =>
        {
            bool gateTaken = false;
            try
            {
                // One cursor owner at a time: wait for a running quiet reveal to finish its hop.
                gateTaken = _cursorGate.Wait(ClickJoinGraceMs);
                if (!gateTaken)
                {
                    TrayPeekDiagnostics.LogNote("click skipped: cursor gate busy");
                    return;
                }

                ClickUia(item, kind, anchorRect);
            }
            finally
            {
                if (gateTaken)
                {
                    _cursorGate.Release();
                }

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

        // A quit can land while the STA worker is mid-reveal. Restore the pointer from here —
        // the worker is a background thread and the process may tear it down before its finally.
        RestoreActiveCursor();

        // Cancel and dispose while holding the same lock ClickUia uses to detach the source, so
        // Cancel() can never race a concurrent Dispose() of the same token source.
        lock (_holdLock)
        {
            CancellationTokenSource? hold = _holdCts;
            _holdCts = null;
            if (hold is not null)
            {
                try
                {
                    hold.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }

                hold.Dispose();
            }
        }

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
                IReadOnlyList<TrayMirrorItem> classic = PollToolbar(toolbar);
                if (classic.Count > 0)
                {
                    _lastToolbar = toolbar;
                    Publish(classic);
                    return;
                }
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
        DateTime now = DateTime.UtcNow;
        for (int i = 0; i < buttons.Count; i++)
        {
            TrayButtonInfo b = buttons[i];
            if (TraySystemIconFilter.IsSystemIcon(b.Tooltip)
                || TraySystemIconFilter.IsWharfBarIcon(b.Tooltip))
            {
                continue;
            }

            items.Add(BuildToolbarItem(toolbar, b, i, now));
        }

        _iconCache.Retain(items.Select(static item => item.Key));
        return items;
    }

    private IReadOnlyList<TrayMirrorItem> PollUia()
    {
        // Desktop-root UIA read (TrayUiaReader). While the strip-release auto-hide state is on,
        // explorer parks the tray in a 2 px sliver and virtualizes its XAML buttons, so this
        // direct read is empty most of the time; the quiet reveal refresh (TryRefreshTray)
        // exposes the real buttons and captures pixels. Poll already runs on the STA worker, so a
        // direct call — RunOnSta here would deadlock on the queue it is blocking.
        TrackShellInstance();
        IReadOnlyList<UiaTrayButton>? buttons = TrayUiaReader.TryGetTrayButtons();
        bool directHasButtons = buttons is { Count: > 0 };
        LogDirectReadTransition(buttons?.Count ?? -1);

        // Refreshes run on their own cadence even when the direct read has buttons — gating them
        // on an EMPTY read froze icon state (Discord mute/call) for whole sessions on machines
        // where the direct read is not empty.
        IReadOnlyList<TrayMirrorItem>? refreshed = TryRefreshTray(directHasButtons, out TrayBlipOutcome outcome);
        if (refreshed is { Count: > 0 })
        {
            _lastGoodUiaItems = refreshed;
            _uiaLocked = true;
            _iconCache.Retain(refreshed.Select(static item => item.Key));
            return refreshed;
        }

        // Null (walk failed), empty (virtualized parked tray) and a refresh that proved nothing
        // all mean "no fresh data" — hold the last good snapshot, never publish an empty one.
        buttons ??= [];
        IntPtr tray = TrayUiaReader.FindTrayHwnd();

        // Cached pixels for known keys; a new key gets one shared capture only if the tray is
        // actually shown (outside a reveal it never is — PrintWindow would paint nothing), else
        // a letter glyph that the next refresh replaces.
        var items = new List<TrayMirrorItem>(buttons.Count);
        DateTime now = DateTime.UtcNow;
        TrayCaptureFrame? frame = null;
        bool frameTried = false;
        var stats = default(CaptureStats);
        Dictionary<string, Rect> knownRects = RectsByKey(_lastGoodUiaItems);
        IReadOnlyList<string> keys = TrayMirrorOrder.UniqueKeys(
            buttons.Select(static b => b.Name).ToList());
        for (int i = 0; i < buttons.Count; i++)
        {
            UiaTrayButton b = buttons[i];
            string key = keys[i];
            Rect rect = knownRects.GetValueOrDefault(key);
            if (!_iconCache.TryGet(key, out ImageSource icon))
            {
                if (!frameTried)
                {
                    frameTried = true;
                    _ = TrayCapture.TryCaptureFrame(tray, out frame);
                }

                icon = CaptureIcon(frame, b, key, now, captureAttempted: frame is not null, ref stats, out Rect captured);
                if (captured.Width > 0)
                {
                    rect = captured;
                }
            }

            // Key by tooltip/name (unique per button, index-suffixed when needed): most tray apps
            // keep a stable tooltip; state-bearing names (e.g. EarTrumpet percent) churn that one
            // icon — the accepted UIA-path cost.
            items.Add(new TrayMirrorItem(key, i, i, b.Name, icon, b.Element, rect));
        }

        if (items.Count == 0)
        {
            // A parked tray reads empty on EVERY poll; that is not evidence and must never blank
            // the bar. Only a refresh that actually looked at a shown tray and found it empty
            // (ConfirmedEmpty) clears the mirror. Without evidence the last good snapshot is held:
            // a stale icon beats a blank bar and a work-area jump to re-read it.
            if (ShouldHoldSnapshotOnEmptyRead(_lastGoodUiaItems.Count, outcome == TrayBlipOutcome.ConfirmedEmpty))
            {
                return _lastGoodUiaItems;
            }

            // Proven empty: drop the held snapshot too, otherwise a later single empty read would
            // resurrect the stale items — and their UIA elements with them, which is what a
            // mirrored click forwards to.
            _lastGoodUiaItems = [];
            _iconCache.Clear();
            return items;
        }

        _lastGoodUiaItems = items;
        _iconCache.Retain(items.Select(static item => item.Key));
        _uiaLocked = true;
        return items;
    }

    /// <summary>
    /// Pure: does an empty direct read keep the held snapshot on the bar, or clear the mirror?
    /// A parked auto-hide tray virtualizes its buttons, so an empty direct read is the NORMAL
    /// state while WharfBar runs — only a refresh that showed the tray and read it empty
    /// (<paramref name="trayConfirmedEmptyByRefresh"/>) is evidence that the tray is empty.
    /// A stale icon is better than a blank mirror (and than a work-area jump to re-read it).
    /// </summary>
    internal static bool ShouldHoldSnapshotOnEmptyRead(
        int heldItemCount,
        bool trayConfirmedEmptyByRefresh) =>
        !trayConfirmedEmptyByRefresh && heldItemCount > 0;

    /// <summary>
    /// Pure: what a blip that actually ran proves. Split out of the peek plumbing so the
    /// "only a fully shown tray counts as evidence" rule is testable without Win32 — the caller
    /// must never hand <see cref="TrayBlipOutcome.ConfirmedEmpty"/> to a blip that never looked.
    /// <paramref name="trayFullyOnScreen"/> must be re-read after the reveal (or be the state-blip
    /// path, which unparks the tray): the peek opening is not enough, because the tray is also
    /// "not parked" while its rect cannot be read and while explorer is mid slide-out.
    /// </summary>
    internal static TrayBlipOutcome ClassifyBlipRead(bool trayFullyOnScreen, bool readWalkFailed, int readCount) =>
        !trayFullyOnScreen || readWalkFailed ? TrayBlipOutcome.Inconclusive
            : readCount > 0 ? TrayBlipOutcome.Refreshed
            : TrayBlipOutcome.ConfirmedEmpty;

    /// <summary>
    /// Refreshes the tray snapshot. Quiet path first: hover-reveal the parked auto-hide tray (the
    /// click path's own gesture — no <c>ABM_SETSTATE</c>, no work-area change) and capture pixels
    /// while it is on screen. The loud state blip is only the fallback for when the quiet read
    /// cannot run and the direct read has nothing. Returns a ready snapshot, or null with
    /// <paramref name="outcome"/> saying why — only <see cref="TrayBlipOutcome.ConfirmedEmpty"/>
    /// is evidence that the tray is empty. Runs on the STA worker (Poll already does).
    /// </summary>
    private IReadOnlyList<TrayMirrorItem>? TryRefreshTray(bool directHasButtons, out TrayBlipOutcome outcome)
    {
        outcome = TrayBlipOutcome.Skipped;
        if (Volatile.Read(ref _clickBusy) != 0)
        {
            return null;
        }

        // One cursor owner at a time. Non-blocking on purpose: this runs on the STA worker and
        // a click's element resolve is queued onto that same dispatcher — blocking here while a
        // click holds the gate would deadlock. A skipped refresh costs one cadence tick.
        if (!_cursorGate.Wait(0))
        {
            return null;
        }

        try
        {
            DateTime now = DateTime.UtcNow;
            long expediteTicks = Interlocked.Read(ref _expediteDueTicks);
            bool expedite = TrayIconRefreshPolicy.IsExpediteDue(expediteTicks, now);

            // Quiet path: the reveal peek touches no app-bar state and no work area, so it alone
            // follows the refresh cadence and the out-of-band requests.
            bool hasSnapshot = _lastGoodUiaItems.Count > 0;
            TimeSpan idle = TrayAutoHideReveal.UserIdleTime();
            TimeSpan minGap = TrayIconRefreshPolicy.MinGap(expedite, _iconsRecentlyChanged);
            if (TrayIconRefreshPolicy.ShouldRefresh(now - _lastQuietRefresh, minGap, clickBusy: false)
                && ShouldAttemptQuietRefresh(hasSnapshot, idle, now))
            {
                _lastQuietRefresh = now;
                if (expedite)
                {
                    _ = Interlocked.CompareExchange(ref _expediteDueTicks, 0, expediteTicks);
                }

                // An empty mirror may force a hop even while the user is active, but only for
                // the first few attempts of the shell session; after that the idle wait applies.
                if (!hasSnapshot && idle < TrayRevealPolicy.MinUserIdle)
                {
                    _emptyMirrorBypassAttempts++;
                }

                IReadOnlyList<TrayMirrorItem>? quiet = TryPeekIcons(stateBlip: false, out outcome);
                if (quiet is { Count: > 0 })
                {
                    return AfterRefresh(quiet, outcome);
                }

                // A revealed tray that read back empty is real evidence. Anything else (no
                // invisibility mechanism, reveal timeout, walk exception) proved nothing, so fall
                // through to the loud path — unless the direct read already has the buttons.
                if (outcome == TrayBlipOutcome.ConfirmedEmpty || directHasButtons)
                {
                    return AfterRefresh(null, outcome);
                }

                AfterRefresh(null, outcome);
            }

            if (directHasButtons)
            {
                // Fresh buttons already came from the direct read; the loud path would only move
                // windows for pixels we already have.
                return null;
            }

            // Loud path: ABM_SETSTATE(0) ↔ ABS_AUTOHIDE reserves and releases the taskbar strip, so
            // every maximized/snapped/bottom-touching window jumps twice. Gated hard (bootstrap of
            // an empty mirror, or rare upkeep while the user is away); never brought forward by
            // clicks, theme changes or failed peeks. A blocked blip does not stamp the clock.
            TrayStateBlipReason reason = TrayStateBlipGate.Decide(
                hasSnapshot: hasSnapshot,
                blippedThisShell: _stateBlippedThisShell,
                trayConfirmedEmpty: _stateBlipConfirmedEmpty,
                sinceLastStateChange: now - _lastStateChange,
                userIdle: idle);
            if (reason == TrayStateBlipReason.None)
            {
                return null;
            }

            bool blocked = TrayStateBlipGate.ChecksAllAffectedWindows(reason)
                ? TrayAutoHideReveal.HasWindowAffectedByWorkAreaChange(ReservedStripPx)
                : HasMaximizedWindowOnPrimary();
            if (blocked)
            {
                return null;
            }

            _lastStateChange = now;
            _stateBlippedThisShell = true;
            TrayPeekDiagnostics.LogStateChange($"refresh-{reason}");
            IReadOnlyList<TrayMirrorItem>? result = TryPeekIcons(stateBlip: true, out outcome);
            if (outcome == TrayBlipOutcome.ConfirmedEmpty)
            {
                _stateBlipConfirmedEmpty = true;
            }
            else if (outcome == TrayBlipOutcome.Refreshed)
            {
                _stateBlipConfirmedEmpty = false;
            }

            return AfterRefresh(result, outcome);
        }
        finally
        {
            _cursorGate.Release();
        }
    }

    /// <summary>
    /// The quiet refresh moves the pointer to the bottom edge for a few hundred ms while
    /// explorer slides the auto-hide tray in, so it waits for the user to be idle; an empty
    /// mirror is the exception (a short hop is strictly better than a blank bar), capped by the
    /// empty-mirror bypass budget. A real fullscreen app (not merely a maximized window)
    /// suppresses it: the bar is hidden there and the tray should stay out of the way.
    /// </summary>
    private bool ShouldAttemptQuietRefresh(bool hasSnapshot, TimeSpan idle, DateTime now)
    {
        if (IsForegroundFullscreenOnPrimary())
        {
            return false;
        }

        return TrayRevealPolicy.ShouldAttemptReveal(
            idle,
            hasSnapshot,
            Volatile.Read(ref _revealFailures),
            now - LastRevealFailureUtc,
            _emptyMirrorBypassAttempts);
    }

    /// <summary>
    /// True when the foreground window is a real fullscreen app covering the primary monitor.
    /// Maximized frames are excluded: DWM stretches them a few pixels past the monitor and they
    /// are <c>IsZoomed</c>, so they must not be mistaken for fullscreen.
    /// </summary>
    private static bool IsForegroundFullscreenOnPrimary()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero
            || NativeMethods.IsIconic(foreground)
            || NativeMethods.IsZoomed(foreground)
            || !NativeMethods.GetWindowRect(foreground, out RECT window))
        {
            return false;
        }

        IntPtr monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
        };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return false;
        }

        const int tolerance = 2;
        RECT screen = info.rcMonitor;
        return window.Left <= screen.Left + tolerance
            && window.Top <= screen.Top + tolerance
            && window.Right >= screen.Right - tolerance
            && window.Bottom >= screen.Bottom - tolerance;
    }

    /// <summary>
    /// A new <c>Shell_TrayWnd</c> means explorer restarted: its tray is a fresh unknown, so every
    /// cached element, icon, snapshot and gate is dropped and the bootstrap is re-armed. Dead UIA
    /// elements (and their stale icons) must not survive into the new shell — mirrored clicks
    /// would forward to them and silently miss.
    /// </summary>
    private void TrackShellInstance()
    {
        IntPtr tray = TrayUiaReader.FindTrayHwnd();
        if (tray == IntPtr.Zero || tray == _shellTray)
        {
            return;
        }

        bool restarted = _shellTray != IntPtr.Zero;
        if (restarted)
        {
            TrayPeekDiagnostics.LogNote($"shell restarted tray={tray}");
        }

        _shellTray = tray;
        _stateBlippedThisShell = false;
        _stateBlipConfirmedEmpty = false;
        _lastStateChange = default;
        Interlocked.Exchange(ref _revealFailures, 0);
        Interlocked.Exchange(ref _lastRevealFailureTicks, 0);
        _emptyMirrorBypassAttempts = 0;
        _uiaLocked = false;
        _lastToolbar = IntPtr.Zero;
        _lastGoodUiaItems = [];
        _lastQuietRefresh = default;
        _inconclusiveRetryUsed = false;
        _iconsRecentlyChanged = false;
        _iconCache.Clear();
        if (restarted)
        {
            // Clear the bar now instead of waiting for the next poll to diff an empty snapshot
            // against the previous one (equal counts would otherwise skip the Changed event).
            Publish([], force: true);
        }
    }

    /// <summary>
    /// One quick retry after a refresh that never read the tray (peek refused, reveal timeout,
    /// walk threw): otherwise the next try is a whole interval away. A refresh that did read the
    /// tray re-arms the retry.
    /// </summary>
    private IReadOnlyList<TrayMirrorItem>? AfterRefresh(IReadOnlyList<TrayMirrorItem>? result, TrayBlipOutcome outcome)
    {
        if (outcome == TrayBlipOutcome.Inconclusive)
        {
            if (!_inconclusiveRetryUsed)
            {
                _inconclusiveRetryUsed = true;
                RequestExpeditedRefresh(TrayIconRefreshPolicy.ExpediteMinGap);
            }
        }
        else if (outcome is TrayBlipOutcome.Refreshed or TrayBlipOutcome.ConfirmedEmpty)
        {
            _inconclusiveRetryUsed = false;
        }

        return result;
    }

    /// <summary>
    /// Shows the tray just long enough to read buttons and capture icons. The parked auto-hide
    /// tray virtualizes its XAML content — a stealth show in place reads zero buttons — so the
    /// quiet read hover-reveals the tray exactly like a click does (real input at the bottom
    /// edge, cursor restored afterwards, no work-area change). <paramref name="stateBlip"/> is
    /// the loud fallback: it drops <c>ABS_AUTOHIDE</c> first, which unparks the tray but flaps
    /// the work area. Icons are captured <b>while the peek is still open</b> — PrintWindow needs
    /// a shown, rendered window; once the peek ends every capture comes back a solid black DIB.
    /// Must run on the STA worker.
    /// </summary>
    private IReadOnlyList<TrayMirrorItem>? TryPeekIcons(bool stateBlip, out TrayBlipOutcome outcome)
    {
        TrayPeekDiagnostics.LogNote(stateBlip ? "refresh state-blip" : "refresh reveal");
        bool shown = false;
        bool readWalkFailed = true;
        int readCount = 0;
        bool refresh = false;
        bool locked = false;
        bool trayFullyOnScreen = false;
        SendInputMouse.CursorLease? cursorHome = null;
        try
        {
            if (stateBlip)
            {
                // Hold the desktop's paint across the autohide drop/restore so icon positions
                // do not flash the intermediate work area. Unlock is in the finally.
                locked = NativeMethods.LockWindowUpdate(NativeMethods.GetDesktopWindow());
                TaskbarNative.SetState(0);
                _taskbar.BeginIconRefresh();
                refresh = true;
            }

            shown = _taskbar.BeginTrayPeek(stealth: true);
            if (shown)
            {
                IntPtr tray = TrayUiaReader.FindTrayHwnd();
                if (stateBlip && tray != IntPtr.Zero)
                {
                    // The tray may still sit at its parked position when the state drops; slide it
                    // fully on-screen (no clamp at state 0) so the UIA rects line up with the
                    // window's own coordinate space before the crop is computed.
                    TaskbarNative.EnsureTrayOnScreen(tray);

                    // The state drop unparks the tray, so this path counts as shown.
                    trayFullyOnScreen = true;
                }
                else if (!stateBlip && tray != IntPtr.Zero)
                {
                    if (TrayAutoHideReveal.IsParkedNow(tray))
                    {
                        // Re-check the click flag right before the hop: a click queued after this
                        // poll started owns the cursor, and its pointer must not be moved.
                        if (Volatile.Read(ref _clickBusy) != 0)
                        {
                            outcome = TrayBlipOutcome.Inconclusive;
                            return null;
                        }

                        // Parked tray: hover the bottom edge like a user so explorer slides the
                        // auto-hide bar on screen. No ABM_SETSTATE, no work-area change. The cursor
                        // stays at the edge until the capture is done and is restored in the finally.
                        cursorHome = AcquireCursorLease();
                        int revealX = DefaultRevealX(tray);
                        if (!TrayAutoHideReveal.TryReveal(
                                tray,
                                revealX,
                                RevealTimeoutMs,
                                cursorHome.ParkedAt,
                                out int revealMs))
                        {
                            // A failing hover is retried only after the reveal backoff; otherwise a
                            // shell that cannot reveal would move the pointer every cadence tick.
                            RecordRevealMiss(DateTime.UtcNow);
                            TrayPeekDiagnostics.LogNote(
                                $"reveal FAILED x={revealX} ms={revealMs} misses={Volatile.Read(ref _revealFailures)}");
                            outcome = TrayBlipOutcome.Inconclusive;
                            return null;
                        }

                        RecordRevealSuccess();
                        TrayPeekDiagnostics.LogNote($"reveal ok x={revealX} ms={revealMs}");
                    }
                    else
                    {
                        // Already on screen (or user-revealed): no gesture was needed, and the
                        // quiet read is healthy.
                        RecordRevealSuccess();
                    }
                }

                Thread.Sleep(PeekSettleMs);

                if (!stateBlip && tray != IntPtr.Zero)
                {
                    // Evidence requires the tray fully on screen WHILE the read runs. "Not parked"
                    // alone also means "rect read failed" or "explorer is mid slide-out" — a read
                    // that never looked must not clear the mirror.
                    trayFullyOnScreen = TrayAutoHideReveal.IsFullyOnScreenNow(tray);
                }

                IReadOnlyList<UiaTrayButton>? read = TrayUiaReader.TryGetTrayButtons();
                if (read is { Count: 0 })
                {
                    // Explorer rebuilds the XAML tray around every show; one short second read
                    // separates "still rebuilding" from "the tray really is empty".
                    Thread.Sleep(EmptyConfirmDelayMs);
                    read = TrayUiaReader.TryGetTrayButtons();
                }

                readWalkFailed = read is null;
                readCount = read?.Count ?? 0;
                if (read is { Count: > 0 })
                {
                    outcome = TrayBlipOutcome.Refreshed;
                    return BuildCapturedItems(
                        tray,
                        read,
                        skipCaptureIndex: -1,
                        stateBlip ? "refresh" : "reveal-refresh");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: tray icon refresh failed — {ex.Message}");
        }
        finally
        {
            // Cursor back BEFORE the tray is hidden (so the dock poll never sees the parked
            // pointer after the peek flag clears) and before the exit path may shut us down.
            ReleaseCursorLease(cursorHome);
            _taskbar.EndTrayPeek();
            if (refresh)
            {
                _taskbar.EndIconRefresh();
            }

            if (locked)
            {
                _ = NativeMethods.LockWindowUpdate(IntPtr.Zero);
            }
        }

        // Peek that never opened, a reveal that timed out, or a UIA walk that threw: nothing was
        // read, so nothing is proved. Only a tray that was fully on screen and read back zero
        // buttons is evidence of an empty tray.
        outcome = ClassifyBlipRead(trayFullyOnScreen, readWalkFailed, readCount);
        return null;
    }

    /// <summary>
    /// Builds a snapshot from a live read while the tray is shown: one <c>PrintWindow</c>, then
    /// a crop per button. <paramref name="skipCaptureIndex"/> keeps the cached icon for a button
    /// under the cursor (its hover plate would be captured as a "change"). Runs on the STA.
    /// </summary>
    private IReadOnlyList<TrayMirrorItem> BuildCapturedItems(
        IntPtr tray,
        IReadOnlyList<UiaTrayButton> read,
        int skipCaptureIndex,
        string source)
    {
        _ = TrayCapture.TryCaptureFrame(tray, out TrayCaptureFrame? frame);
        DateTime now = DateTime.UtcNow;
        var stats = default(CaptureStats);
        Dictionary<string, Rect> knownRects = RectsByKey(_lastGoodUiaItems);
        IReadOnlyList<string> keys = TrayMirrorOrder.UniqueKeys(
            read.Select(static b => b.Name).ToList());
        var items = new List<TrayMirrorItem>(read.Count);
        for (int i = 0; i < read.Count; i++)
        {
            UiaTrayButton b = read[i];
            string key = keys[i];
            ImageSource icon;
            Rect rect;
            if (i == skipCaptureIndex)
            {
                rect = TrayUiaReader.GetIconRect(b.Element);
                icon = _iconCache.TryGet(key, out ImageSource cached)
                    ? cached
                    : _iconCache.Fallback(key, () => TrayLetterGlyph.Build(b.Name), now, captureAttempted: false);
            }
            else
            {
                icon = CaptureIcon(frame, b, key, now, captureAttempted: true, ref stats, out rect);
            }

            if (rect.IsEmpty || rect.Width <= 0)
            {
                rect = knownRects.GetValueOrDefault(key);
            }

            // Key by tooltip/name (unique per button, index-suffixed when needed): most tray apps
            // keep a stable tooltip; state-bearing names (e.g. EarTrumpet percent) churn that one
            // icon — the accepted UIA-path cost.
            items.Add(new TrayMirrorItem(key, i, i, b.Name, icon, b.Element, rect));
        }

        if (stats.Captured > 0)
        {
            _lastPixelCapture = now;
        }

        _iconsRecentlyChanged = stats.Changed > 0;
        TrayPeekDiagnostics.LogNote(
            $"{source} read={read.Count} frame={frame is not null} captured={stats.Captured} changed={stats.Changed}");
        return items;
    }

    private static Dictionary<string, Rect> RectsByKey(IReadOnlyList<TrayMirrorItem> items)
    {
        var rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        foreach (TrayMirrorItem item in items)
        {
            if (item.ScreenRect.Width > 0)
            {
                rects.TryAdd(item.Key, item.ScreenRect);
            }
        }

        return rects;
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

    private struct CaptureStats
    {
        internal int Captured;
        internal int Changed;
    }

    /// <summary>
    /// Crops the button out of <paramref name="frame"/> and runs it through the signature cache:
    /// unchanged pixels return the cached instance (no UI work), a failed crop keeps the last real
    /// icon (bounded by <see cref="TrayIconCache{TIcon}.StaleIconLimit"/>) or a letter glyph.
    /// </summary>
    private ImageSource CaptureIcon(
        TrayCaptureFrame? frame,
        UiaTrayButton button,
        string cacheKey,
        DateTime now,
        bool captureAttempted,
        ref CaptureStats stats,
        out Rect rect)
    {
        rect = Rect.Empty;
        if (frame is not null)
        {
            rect = TrayUiaReader.GetIconRect(button.Element);
            if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0
                && TrayCapture.TryCropIcon(frame, ToNativeRect(rect), out TrayIconCrop? crop)
                && crop is not null)
            {
                stats.Captured++;
                ulong signature = TrayIconSignature.Compute(crop.Pixels, crop.Icon.PixelWidth, crop.Icon.PixelHeight);
                ImageSource icon = _iconCache.Accept(cacheKey, crop.Icon, signature, now, out bool changed);
                if (changed)
                {
                    stats.Changed++;
                }

                return icon;
            }
        }

        string name = button.Name;
        return _iconCache.Fallback(cacheKey, () => TrayLetterGlyph.Build(name), now, captureAttempted);
    }

    private TrayMirrorItem BuildToolbarItem(IntPtr toolbar, TrayButtonInfo button, int index, DateTime now)
    {
        string key = $"{button.CommandId}\0{button.Tooltip}";
        ImageSource? icon = null;
        if (TrayToolbar.TryGetIcon(toolbar, button.ImageIndex, out IntPtr hIcon))
        {
            try
            {
                BitmapSource bmp = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                // Same pixels → the cached instance, so a steady 4 s poll publishes nothing.
                icon = _iconCache.Accept(key, bmp, TrayIconSignature.Of(bmp), now, out _);
            }
            finally
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }

        icon ??= _iconCache.Fallback(key, () => Fallback, now, captureAttempted: true);
        return new TrayMirrorItem(key, button.CommandId, index, button.Tooltip, icon);
    }

    /// <summary>Diag only on a change of the direct read's shape (null / empty / count), not every poll.</summary>
    private void LogDirectReadTransition(int count)
    {
        int shape = count <= 0 ? count : 1;
        if (shape == _lastDirectReadCount)
        {
            return;
        }

        _lastDirectReadCount = shape;
        TrayPeekDiagnostics.LogNote(count < 0 ? "direct read failed" : $"direct read buttons={count}");
    }

    private void ClickUia(TrayMirrorItem item, TrayClick kind, Rect anchorRect)
    {
        // The stored element is captured during a peek; the shell tears its XAML tree down
        // when we hide the bar again, so by click time it is usually dead — Invoke/rect
        // would silently no-op. Resolve a FRESH element from the live tree once the
        // visible peek has brought the tray back.
        AutomationElement stale = item.Tag as AutomationElement ?? throw new InvalidOperationException("UIA mirror click without an element.");

        var cts = new CancellationTokenSource();
        lock (_holdLock)
        {
            _holdCts = cts;
        }

        // While ABS_AUTOHIDE is on, explorer parks the tray in a 2 px sliver and virtualizes its
        // buttons. Hovering the bottom edge reveals it the way a user would — no work-area
        // change. Only when that reveal fails does the click fall back to dropping the state
        // (BeginIconRefresh), which reserves the strip and moves windows; that is logged as a
        // state change. The cursor lease puts the pointer back after the whole session, and is
        // registered so the exit path can restore it if this thread is killed mid-click.
        SendInputMouse.CursorLease cursorHome = AcquireCursorLease();
        bool peeked = false;
        bool stateDropped = false;
        try
        {
            peeked = _taskbar.BeginTrayPeek(stealth: true);
            if (!peeked)
            {
                TrayPeekDiagnostics.LogNote("click peek FAILED");
                return;
            }

        try
        {
            TrayPeekDiagnostics.LogNote($"click begin kind={kind} key='{item.Key}'");

            TaskbarNative.BeginTrayClickInput();
            stateDropped = !RevealTrayForClick(item, cursorHome.ParkedAt);

            // The shell rebuilds its XAML content around show/hide; resolve-first, then retry
            // until the live tree is up — no fixed settle, so a fast rebuild clicks sooner.
            AutomationElement? target = null;
            IReadOnlyList<UiaTrayButton>? live = null;
            int targetIndex = -1;
            for (int attempt = 0; attempt < LiveResolveAttempts && target is null; attempt++)
            {
                target = ResolveLiveButton(item, out live, out targetIndex);
                if (target is null)
                {
                    Thread.Sleep(LiveResolvePollMs);
                }
            }

            int liveButtons = live?.Count ?? 0;
            TrayPeekDiagnostics.LogNote(
                target is not null
                    ? $"click live-found buttons={liveButtons}"
                    : $"click live-MISSING buttons={liveButtons}");

            if (target is null && liveButtons > 0)
            {
                // The live tray is up and this key is not on it: the app is gone (held snapshot).
                // Its old rect now belongs to a neighbour — clicking it would hit the wrong app.
                TrayPeekDiagnostics.LogNote("click skipped: item no longer in the tray");
                return;
            }

            if (live is { Count: > 0 })
            {
                CaptureDuringClick(live, targetIndex);
            }

            Rect targetRect = target is not null
                ? TrayUiaReader.GetScreenRect(target)
                : Rect.Empty;
            if (targetRect.IsEmpty && item.ScreenRect.Width > 0)
            {
                // Tree not exposed: the rect recorded on the unparked tray (layout is stable).
                targetRect = item.ScreenRect;
            }

            if (targetRect.IsEmpty)
            {
                // Last resort: whatever rect the stored element still reports.
                targetRect = TrayUiaReader.GetScreenRect(stale);
            }

            TrayPeekDiagnostics.LogNote(
                $"click rect={targetRect.Left:F0},{targetRect.Top:F0},{targetRect.Right:F0},{targetRect.Bottom:F0} empty={targetRect.IsEmpty}");

            // Physical input for every kind — InvokePattern is unverified on the 25H2 XAML
            // tray buttons, while a real click is exactly what a human would have done.
            // Right-click is included: UIA cannot express it, and the owner's own menu
            // (HMENU / flyout) opens from the physical button, then re-anchor moves it.
            if (!targetRect.IsEmpty && targetRect.Width > 0 && targetRect.Height > 0)
            {
                RECT anchor = ToNativeRect(anchorRect);
                var buttonRect = new RECT
                {
                    Left = (int)Math.Round(targetRect.Left),
                    Top = (int)Math.Round(targetRect.Top),
                    Right = (int)Math.Round(targetRect.Right),
                    Bottom = (int)Math.Round(targetRect.Bottom),
                };
                Task<bool>? reanchor = null;
                if (anchor.Width > 0)
                {
                    // Before the click, so a menu created inside SendInput is still "new".
                    reanchor = BeginReanchor(buttonRect, anchor);
                }

                int cx = (buttonRect.Left + buttonRect.Right) / 2;
                int cy = (buttonRect.Top + buttonRect.Bottom) / 2;
                if (kind == TrayClick.Right)
                {
                    // Cursor stays on the real button until the menu is moved (or the hold
                    // expires). Restoring at a fixed 160 ms made slow owners read the top-bar
                    // position and drop the click; hiding the tray before the menu existed
                    // destroyed tray-owned popups. The diag log showed "reanchor timeout"
                    // on the first right-click and a hit only on a later retry.
                    using (SendInputMouse.RightClickHoldingCursor(cx, cy))
                    {
                        WaitForMenu(reanchor);
                    }
                }
                else
                {
                    SendInputMouse.Click(cx, cy, kind);
                    reanchor?.Wait(300);
                }

                TrayPeekDiagnostics.LogNote("click sent");
            }
            else
            {
                bool invoked = TrayUiaReader.TryInvoke(target ?? stale);
                TrayPeekDiagnostics.LogNote($"click invoke-fallback={invoked}");
            }
        }
        catch (Exception ex)
        {
            TrayPeekDiagnostics.LogNote($"click ERROR: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            TaskbarNative.EndTrayClickInput();
            // A tray click usually changes that app's icon (mute, pause, connect) — or removed a
            // dead item; refresh once the owner's menu action has had time to land. Only the
            // quiet show-only path honours this; it never brings a state blip forward.
            RequestExpeditedRefresh(TrayIconRefreshPolicy.ClickFollowUpDelay);
            TrayPeekDiagnostics.LogNote("click end");
        }
        }
        finally
        {
            lock (_holdLock)
            {
                if (ReferenceEquals(_holdCts, cts))
                {
                    _holdCts = null;
                }
            }

            cts.Dispose();

            // Cursor back BEFORE the tray is hidden (same ordering as the quiet reveal), and
            // idempotently again in case the peek never opened.
            ReleaseCursorLease(cursorHome);
            _taskbar.EndTrayPeek();
            if (stateDropped)
            {
                _taskbar.EndIconRefresh();
            }
        }
    }

    /// <summary>
    /// Gets the parked tray on screen for a click. Hover reveal first (no work-area change);
    /// after <see cref="TrayRevealPolicy.RevealMissLimit"/> consecutive misses the reveal is
    /// skipped for <see cref="TrayRevealPolicy.RevealBackoff"/> — the same bounded backoff the
    /// quiet refresh uses (a shared miss counter: if the hover does not work for one path it will
    /// not work for the other). Returns false when it had to drop the app-bar state instead — the
    /// caller then owes <see cref="TaskbarVisibilityService.EndIconRefresh"/>.
    /// </summary>
    private bool RevealTrayForClick(TrayMirrorItem item, Action<int, int> onCursorParked)
    {
        IntPtr tray = TrayUiaReader.FindTrayHwnd();
        DateTime now = DateTime.UtcNow;
        if (!IsRevealBackedOff(now))
        {
            int x = item.ScreenRect.Width > 0
                ? (int)Math.Round(item.ScreenRect.Left + (item.ScreenRect.Width / 2))
                : DefaultRevealX(tray);
            if (TrayAutoHideReveal.TryReveal(tray, x, RevealTimeoutMs, onCursorParked, out int elapsedMs))
            {
                RecordRevealSuccess();
                TrayPeekDiagnostics.LogNote($"click reveal ok x={x} ms={elapsedMs}");
                return true;
            }

            RecordRevealMiss(now);
            TrayPeekDiagnostics.LogNote(
                $"click reveal FAILED x={x} ms={elapsedMs} misses={Volatile.Read(ref _revealFailures)}");
        }

        // BeginIconRefresh drops ABS_AUTOHIDE and keeps EnsureStripReleased from putting it
        // back mid-click: the tray unparks, the strip is reserved, windows move.
        if (TaskbarNative.GetState() == NativeMethods.ABS_AUTOHIDE)
        {
            _lastStateChange = now;
            TrayPeekDiagnostics.LogStateChange("click-fallback");
        }

        _taskbar.BeginIconRefresh();
        return false;
    }

    private static int DefaultRevealX(IntPtr tray) =>
        NativeMethods.GetWindowRect(tray, out RECT rect) && rect.Width > 0
            ? rect.Right - 100
            : 0;

    /// <summary>
    /// The click already has the tray shown, so refresh the icons then (the hovered target keeps
    /// its cached pixels), throttled by <see cref="TrayIconRefreshPolicy.ClickCaptureMinAge"/>.
    /// </summary>
    private void CaptureDuringClick(IReadOnlyList<UiaTrayButton> live, int targetIndex)
    {
        if (DateTime.UtcNow - _lastPixelCapture < TrayIconRefreshPolicy.ClickCaptureMinAge)
        {
            return;
        }

        try
        {
            RunOnSta(() =>
            {
                IntPtr tray = TrayUiaReader.FindTrayHwnd();
                IReadOnlyList<TrayMirrorItem> items = BuildCapturedItems(tray, live, targetIndex, "click-capture");
                _lastGoodUiaItems = items;
                _uiaLocked = true;
                _iconCache.Retain(items.Select(static i => i.Key));
                Publish(items);
                return true;
            });
        }
        catch (Exception ex)
        {
            TrayPeekDiagnostics.LogNote($"click-capture error: {ex.Message}");
        }
    }

    private static RECT ToNativeRect(Rect rect) => new()
    {
        Left = (int)Math.Round(rect.Left),
        Top = (int)Math.Round(rect.Top),
        Right = (int)Math.Round(rect.Right),
        Bottom = (int)Math.Round(rect.Bottom),
    };

    /// <summary>
    /// Starts the popup watch with a snapshot taken now. The caller must invoke this before
    /// the synthetic click. Matching stays geometric (button zone + <c>#32768</c>); 25H2 XAML
    /// tray buttons all report explorer's pid, so pid cannot pick the menu.
    /// </summary>
    private static Task<bool> BeginReanchor(RECT buttonRect, RECT anchor)
    {
        if (buttonRect.Width <= 0 || anchor.Width <= 0)
        {
            return Task.FromResult(false);
        }

        HashSet<IntPtr> snapshot = TrayMenuReanchor.CaptureVisibleWindows();
        return Task.Run(() => TrayMenuReanchor.TryReanchor(buttonRect, anchor, snapshot, TrayMenuReanchor.WatchTimeoutMs));
    }

    /// <summary>
    /// Blocks until the menu watcher finishes, but never shorter than the cursor hold the
    /// owner needs to read <c>GetCursorPos</c>, and no longer than <see cref="RightClickPeekHoldMs"/>.
    /// The watcher itself runs on for <see cref="TrayMenuReanchor.WatchTimeoutMs"/>, so a
    /// popup that opens after the tray is hidden again is still moved.
    /// </summary>
    private static void WaitForMenu(Task<bool>? reanchor)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            reanchor?.Wait(RightClickPeekHoldMs);
        }
        catch (Exception ex) when (ex is AggregateException or ObjectDisposedException)
        {
            TrayPeekDiagnostics.LogNote($"click reanchor wait: {ex.GetType().Name}");
        }

        int remain = SendInputMouse.RightClickCursorHoldMs - (int)sw.ElapsedMilliseconds;
        if (remain > 0)
        {
            Thread.Sleep(remain);
        }
    }

    /// <summary>
    /// Fresh UIA button matching the mirrored item's key; null when gone. Matching rules live
    /// in <see cref="TrayButtonMatcher"/> (exact → containment for named keys; unnamed items
    /// resolve by their poll-time index — never containment, which would hit the wrong app).
    /// </summary>
    private AutomationElement? ResolveLiveButton(
        TrayMirrorItem item,
        out IReadOnlyList<UiaTrayButton>? buttons,
        out int index)
    {
        buttons = null;
        index = -1;
        try
        {
            buttons = RunOnSta(() => TrayUiaReader.TryGetTrayButtons());
            if (buttons is null)
            {
                return null;
            }

            string?[] names = new string[buttons.Count];
            for (int i = 0; i < buttons.Count; i++)
            {
                names[i] = buttons[i].Name;
            }

            index = TrayButtonMatcher.ResolveIndex(names, item.Key, item.Index);
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
        lock (_holdLock)
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

    /// <param name="force">
    /// Publish even when the diff says nothing changed. Used after an explorer restart: the old
    /// snapshot's icons are dead, and an empty snapshot equal in count to the previous one would
    /// otherwise skip the <see cref="Changed"/> event that clears the bar.
    /// </param>
    private void Publish(IReadOnlyList<TrayMirrorItem> items, bool force = false)
    {
        // Steady polls hand back the same keys and cached icon instances: no Changed, so the bar
        // skips ApplyTrayMirror + a status layout pass every 4 s.
        if (!force && TrayMirrorDiff.IsSameSnapshot(_lastPublished, items))
        {
            return;
        }

        _lastPublished = items;
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
