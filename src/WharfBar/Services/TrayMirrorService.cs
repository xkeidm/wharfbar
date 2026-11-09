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
/// present (real icons, older Windows); UI Automation as the Windows 11 25H2+ fallback (the
/// classic toolbar was removed there). Read-only: the only shell state it touches is the bounded
/// state blip that unparks the tray to read it; nothing else is modified.
/// <para>
/// <b>Clicks never move the physical cursor.</b> UI Automation's <c>InvokePattern</c> (left) and
/// button focus + the Menu key (right) replay the gesture on the cached tray button, and a cached
/// element keeps working while the tray is hidden (measured on 25H2: a fresh UIA walk returns 0
/// buttons, yet the element captured earlier still invokes and still opens its context menu).
/// When the cached element has died (XAML rebuild, explorer restart) one bounded, cursor-free
/// state blip re-reads the tray and the gesture is replayed on the fresh element.
/// </para>
/// <para>
/// Refresh: the parked auto-hide tray virtualizes its XAML buttons, so a read needs the tray
/// unparked. Only the gated state blip (<see cref="TrayStateBlipGate"/>) unparks it — there is no
/// cursor-hopping hover reveal any more — and the icon pixel capture rides inside that blip.
/// A read that comes back empty never clears a populated mirror (a stale icon beats a blank bar).
/// </para>
/// </summary>
internal sealed class TrayMirrorService : IDisposable
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(4);

    /// <summary>Time the shell gets to rebuild the XAML tray around a show before it is read.</summary>
    private const int PeekSettleMs = 160;

    /// <summary>
    /// Budget for the state blip to actually unpark the tray. <c>ABM_SETSTATE(0)</c> is
    /// asynchronous (measured 0.3–1.2 s), and a read taken while the bar still sits in its 2 px
    /// sliver sees the virtualized XAML tray: 0 buttons that say nothing about the tray.
    /// 2026-11-09 diag: one start had the tray fully on screen at read time, the next was still
    /// parked and the false "empty" blanked the mirror for the whole session.
    /// </summary>
    private const int UnparkWaitTimeoutMs = 1500;

    /// <summary>Budget for the verified <c>SetWindowPos</c> fallback when the state drop did not unpark it.</summary>
    private const int UnparkMoveTimeoutMs = 600;

    /// <summary>
    /// Second read before a shown tray may be declared empty: explorer rebuilds the XAML tray
    /// around every show, so the first read right after the reveal is often the rebuild, not the
    /// tray (a false <see cref="TrayBlipOutcome.ConfirmedEmpty"/> used to wipe the whole mirror).
    /// </summary>
    private const int EmptyConfirmDelayMs = 220;

    /// <summary>Taskbar strip height the state flip would reserve (physical px, 100–150 % DPI).</summary>
    private const int ReservedStripPx = 72;

    /// <summary>How long a click session waits for a running state blip to release the tray peek.</summary>
    private const int ClickJoinGraceMs = 3000;

    /// <summary>
    /// Budget for the click repair path: drop the app-bar state, let explorer unpark the tray,
    /// read it and replay the gesture. Only reached when a cached UIA element has died.
    /// </summary>
    private const int ClickRepairTimeoutMs = 1500;

    /// <summary>How long a click waits for the re-anchor watcher to place the owner's popup.</summary>
    private const int ClickReanchorWaitMs = 450;

    /// <summary>Gap between the two invoked left clicks that make up a mirrored double-click.</summary>
    private const int DoubleClickGapMs = 90;

    /// <summary>A click repair re-captures the strip only when the last capture is older than this.</summary>
    private static readonly TimeSpan ClickCaptureMinAge = TimeSpan.FromSeconds(15);

    /// <summary>Attempts for the repair path's fresh UIA read (the shell rebuilds the tree on show).</summary>
    private const int RepairReadAttempts = 4;

    /// <summary>
    /// Why a refresh returned no icons. The decision to clear the mirror needs exactly this distinction:
    /// only <see cref="ConfirmedEmpty"/> is <b>evidence</b> about the tray (the tray was shown,
    /// yet read as empty); every other value means "we never looked", and the snapshot is kept.
    /// </summary>
    internal enum TrayBlipOutcome
    {
        /// <summary>Never attempted: throttled, mid-click, user busy, or full screen. Not evidence.</summary>
        Skipped,

        /// <summary>Attempted but the peek did not open / the tray never came on screen / UIA navigation threw — the tray was never read.</summary>
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

    // A state blip (unpark the tray, read it, park it again) and a click repair both need the
    // tray shown. One gate serializes them: the poll runs on the STA worker, a click on a
    // background thread. The poll never blocks on it (a queued click read runs on the same
    // dispatcher, so blocking there would deadlock); it just skips the tick.
    private readonly SemaphoreSlim _peekGate = new(1, 1);

    // A parked tray reads empty on every poll; that is not evidence and must never blank the
    // bar. An empty read never clears a held snapshot — a stale icon beats a blank bar (and the
    // work-area jump that filling it would need).
    private readonly TrayIconCache<ImageSource> _iconCache = new();
    private IReadOnlyList<TrayMirrorItem> _lastGoodUiaItems = [];
    private IReadOnlyList<TrayMirrorItem> _lastPublished = [];

    /// <summary>
    /// Mirror state of the last publish. A state-only change (empty mirror: not read yet →
    /// verified empty) must still reach the bar, otherwise the chevron keeps saying "reading…".
    /// </summary>
    private TrayMirrorState _lastPublishedState = TrayMirrorState.Unread;
    private int _lastDirectReadCount = int.MinValue;
    private DateTime _lastPixelCapture;

    // State-blip bookkeeping, per explorer instance (reset when Shell_TrayWnd changes).
    private IntPtr _shellTray;

    // State blips spent on this explorer instance. A counter, not a bool: a bootstrap that never
    // read the tray (lost unpark race, UIA walk threw, peek refused) is NOT evidence that the tray
    // is empty, so the gate may retry it a bounded number of times while the mirror is still blank.
    private int _bootstrapBlips;
    private bool _stateBlipConfirmedEmpty;
    private DateTime _lastStateChange;

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

    /// <summary>
    /// What the bar may honestly say about the tray: populated, verified-empty (icons are in
    /// Windows' hidden area) or not read yet. Drives the chevron tooltip so an empty mirror is
    /// never silent (ADR-009 keeps hidden icons in <c>Win+B</c>).
    /// </summary>
    internal TrayMirrorState State { get; private set; } = TrayMirrorState.Unread;

    internal void Reenumerate() => _ = _staDispatcher?.BeginInvoke(Poll, DispatcherPriority.Background);

    /// <summary>
    /// Windows theme / accent / colorization changed: tray apps swap their light/dark icon
    /// variants and the captured crops carry the old taskbar paint. The next gated state blip
    /// re-captures them; the request only re-runs the poll so a dead cached element list is
    /// rebuilt as soon as the gate allows a blip.
    /// </summary>
    internal void NotifyAppearanceChanged() => Reenumerate();

    /// <summary>
    /// Replays a click on a mirrored tray icon <b>without touching the physical cursor</b>.
    /// Classic toolbar (older Windows): documented TB_* mouse messages at the item rect.
    /// UIA (Windows 11 25H2+): the button element cached in the mirror snapshot is driven with
    /// <c>InvokePattern</c> (left / double) or focus + the Menu key (right). A cached element keeps
    /// working while the tray is hidden (measured on 25H2), so the click needs no peek at all; if
    /// the element has died, one bounded state blip re-reads the tray and replays the gesture.
    /// <paramref name="anchorRect"/> is the mirror icon's on-screen rect (physical px); the
    /// re-anchor watcher moves the owner's popup/menu under it.
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

        // Claim the busy flag synchronously: the STA poll checks it at entry, so a state blip
        // cannot start between this call being queued and its Task.Run beginning. A click that
        // arrives while another one is still running is dropped — replaying two gestures on the
        // same XAML button concurrently opens two popups and corrupts the focus/menu-key path.
        if (Interlocked.CompareExchange(ref _clickBusy, 1, 0) != 0)
        {
            TrayPeekDiagnostics.LogNote("click skipped: another click session is active");
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                ClickUia(item, kind, anchorRect);
            }
            catch (Exception ex)
            {
                TrayPeekDiagnostics.LogNote($"click ERROR: {ex.GetType().Name}: {ex.Message}");
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
                IReadOnlyList<TrayMirrorItem> classic = PollToolbar(toolbar);
                if (classic.Count > 0)
                {
                    _lastToolbar = toolbar;
                    State = TrayMirrorStates.For(classic.Count, TrayBlipOutcome.Skipped);
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
        // direct read is empty most of the time; the gated state blip (TryRefreshTray) unparks
        // the tray and reads (and captures) the real buttons. Poll already runs on the STA
        // worker, so a direct call — RunOnSta here would deadlock on the queue it is blocking.
        TrackShellInstance();
        IReadOnlyList<UiaTrayButton>? buttons = TrayUiaReader.TryGetTrayButtons();
        bool directHasButtons = buttons is { Count: > 0 };
        LogDirectReadTransition(buttons?.Count ?? -1);

        IReadOnlyList<TrayMirrorItem>? refreshed = TryRefreshTray(directHasButtons, out TrayBlipOutcome outcome);
        if (refreshed is { Count: > 0 })
        {
            _lastGoodUiaItems = refreshed;
            _uiaLocked = true;
            _iconCache.Retain(refreshed.Select(static item => item.Key));
            State = TrayMirrorStates.For(refreshed.Count, outcome);
            return refreshed;
        }

        // Null (walk failed), empty (virtualized parked tray) and a refresh that proved nothing
        // all mean "no fresh data" — hold the last good snapshot, never publish an empty one.
        buttons ??= [];
        IntPtr tray = TrayUiaReader.FindTrayHwnd();

        // Cached pixels for known keys; a new key gets one shared capture only if the tray is
        // actually shown (the direct read only has buttons while it is — outside that PrintWindow
        // would paint nothing), else a letter glyph that the next state blip replaces.
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
            // A parked tray reads empty on EVERY poll; that is not evidence about the tray. Even a
            // verified read of an empty tray never clears a populated mirror: a stale icon beats a
            // blank bar (and the work-area jump that filling it would need). The snapshot is reset
            // only when the shell instance changes (TrackShellInstance), and a dead item is dropped
            // by the click repair when the user tries to use it.
            if (ShouldHoldSnapshotOnEmptyRead(_lastGoodUiaItems.Count))
            {
                State = TrayMirrorStates.For(_lastGoodUiaItems.Count, outcome);
                return _lastGoodUiaItems;
            }

            _lastGoodUiaItems = [];
            _iconCache.Clear();
            State = TrayMirrorStates.For(0, outcome);
            return items;
        }

        _lastGoodUiaItems = items;
        _iconCache.Retain(items.Select(static item => item.Key));
        _uiaLocked = true;
        State = TrayMirrorStates.For(items.Count, outcome);
        return items;
    }

    /// <summary>
    /// Pure: does an empty read keep the held snapshot on the bar, or clear the mirror?
    /// A parked auto-hide tray virtualizes its buttons, so an empty direct read is the NORMAL
    /// state while WharfBar runs; and a "verified empty" refresh has been measured to lie while a
    /// fullscreen window (WharfBar's own colour picker) covers the monitor. A stale icon is
    /// therefore preferred over a blank mirror (and over the work-area jump needed to re-read it),
    /// and a populated snapshot is only dropped when the shell instance changes.
    /// </summary>
    internal static bool ShouldHoldSnapshotOnEmptyRead(int heldItemCount) => heldItemCount > 0;

    /// <summary>
    /// Pure: what a blip that actually ran proves. Split out of the peek plumbing so the
    /// "only a fully shown tray counts as evidence" rule is testable without Win32 — the caller
    /// must never hand <see cref="TrayBlipOutcome.ConfirmedEmpty"/> to a blip that never looked.
    /// <paramref name="unparkVerified"/> must be the <b>effect</b> (the tray is fully on screen
    /// while the read runs), never the intent: the state drop is asynchronous, so "we asked
    /// explorer to unpark it" is not enough and a read of the parked, virtualized tray returns 0
    /// buttons without meaning the tray is empty.
    /// </summary>
    internal static TrayBlipOutcome ClassifyBlipRead(bool unparkVerified, bool readWalkFailed, int readCount) =>
        !unparkVerified || readWalkFailed ? TrayBlipOutcome.Inconclusive
            : readCount > 0 ? TrayBlipOutcome.Refreshed
            : TrayBlipOutcome.ConfirmedEmpty;

    /// <summary>
    /// Unparks the tray for one gated state blip, then reads and captures the icons. There is no
    /// cursor-hopping hover reveal any more: <paramref name="directHasButtons"/> short-circuits when
    /// a direct read already exposed the buttons, and otherwise only <see cref="TrayStateBlipGate"/>
    /// may spend the work-area flip. Returns a ready snapshot, or null with
    /// <paramref name="outcome"/> saying why — only <see cref="TrayBlipOutcome.ConfirmedEmpty"/> is
    /// evidence that the tray is empty (and even that keeps a populated mirror, see
    /// <see cref="ShouldHoldSnapshotOnEmptyRead"/>). Runs on the STA worker (Poll already does).
    /// </summary>
    private IReadOnlyList<TrayMirrorItem>? TryRefreshTray(bool directHasButtons, out TrayBlipOutcome outcome)
    {
        outcome = TrayBlipOutcome.Skipped;
        if (Volatile.Read(ref _clickBusy) != 0)
        {
            return null;
        }

        // Non-blocking on purpose: this runs on the STA worker and a click's UIA read is queued
        // onto that same dispatcher — blocking here while a click holds the gate would deadlock.
        // A skipped refresh costs one cadence tick.
        if (!_peekGate.Wait(0))
        {
            return null;
        }

        try
        {
            DateTime now = DateTime.UtcNow;
            bool hasSnapshot = _lastGoodUiaItems.Count > 0;
            TimeSpan idle = TrayAutoHideReveal.UserIdleTime();

            if (directHasButtons)
            {
                // Fresh buttons already came from the direct read; a blip would only move windows
                // for pixels we already have. Cached bitmaps stay until the next gated read.
                return null;
            }

            // A fullscreen foreground window (WharfBar's own colour picker included) makes the
            // shell virtualize the tray: a read taken there returns 0 buttons no matter how the
            // bar is shown (2026-11-09 diag: a false "tray is empty" under the fullscreen picker),
            // and a work-area flap under a fullscreen app is visible. Never blip there.
            if (IsForegroundFullscreenOnPrimary())
            {
                return null;
            }

            // ABM_SETSTATE(0) ↔ ABS_AUTOHIDE reserves and releases the taskbar strip, so every
            // maximized/snapped/bottom-touching window jumps twice. Gated hard (bootstrap of an
            // empty mirror, its bounded retries, or rare upkeep while the user is away). A blocked
            // blip does not stamp the clock.
            TrayStateBlipReason reason = TrayStateBlipGate.Decide(
                hasSnapshot: hasSnapshot,
                bootstrapAttempts: _bootstrapBlips,
                trayConfirmedEmpty: _stateBlipConfirmedEmpty,
                sinceLastStateChange: now - _lastStateChange,
                userIdle: idle);
            if (reason == TrayStateBlipReason.None)
            {
                return null;
            }

            bool blocked = TrayStateBlipGate.ChecksAllAffectedWindows(reason, _bootstrapBlips)
                ? TrayAutoHideReveal.HasWindowAffectedByWorkAreaChange(ReservedStripPx)
                : HasMaximizedWindowOnPrimary();
            if (blocked)
            {
                // Visible to QA in the diag log: an empty mirror can stay empty for as long as
                // this window is up, so the reason must not be silent.
                TrayPeekDiagnostics.LogNote(
                    $"state-blip blocked reason={reason} attempt={_bootstrapBlips} (maximized/affected window)");
                return null;
            }

            _lastStateChange = now;
            _bootstrapBlips++;
            TrayPeekDiagnostics.LogStateChange($"refresh-{reason}");
            IReadOnlyList<TrayMirrorItem>? result = TryPeekIcons(out outcome);
            if (outcome == TrayBlipOutcome.ConfirmedEmpty)
            {
                _stateBlipConfirmedEmpty = true;
            }
            else if (outcome == TrayBlipOutcome.Refreshed)
            {
                _stateBlipConfirmedEmpty = false;
            }

            return result;
        }
        finally
        {
            _peekGate.Release();
        }
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
        _bootstrapBlips = 0;
        _stateBlipConfirmedEmpty = false;
        _lastStateChange = default;
        _uiaLocked = false;
        _lastToolbar = IntPtr.Zero;
        _lastGoodUiaItems = [];
        _iconCache.Clear();
        State = TrayMirrorState.Unread;
        if (restarted)
        {
            // Clear the bar now instead of waiting for the next poll to diff an empty snapshot
            // against the previous one (equal counts would otherwise skip the Changed event).
            Publish([], force: true);
        }
    }

    /// <summary>
    /// Shows the tray just long enough to read buttons and capture icons. The parked auto-hide
    /// tray virtualizes its XAML content — a stealth show in place reads zero buttons — so the
    /// state blip drops <c>ABS_AUTOHIDE</c> first (the work-area flip is the price of a real read),
    /// verifies that explorer actually unparked the bar, then reads the buttons and captures the
    /// icons <b>while the peek is still open</b>: PrintWindow needs a shown, rendered window; once
    /// the peek ends every capture comes back a solid black DIB. No cursor input is involved.
    /// Must run on the STA worker.
    /// </summary>
    private IReadOnlyList<TrayMirrorItem>? TryPeekIcons(out TrayBlipOutcome outcome)
    {
        TrayPeekDiagnostics.LogNote("refresh state-blip");
        outcome = TrayBlipOutcome.Skipped;
        bool shown = false;
        bool readWalkFailed = true;
        int readCount = 0;
        bool trayFullyOnScreen = false;
        bool locked = false;
        bool refreshWindow = false;
        try
        {
            // Hold the desktop's paint across the autohide drop/restore so icon positions do not
            // flash the intermediate work area. Unlock is in the finally.
            locked = NativeMethods.LockWindowUpdate(NativeMethods.GetDesktopWindow());
            TaskbarNative.SetState(0);
            _taskbar.BeginIconRefresh();
            refreshWindow = true;
            shown = _taskbar.BeginTrayPeek(stealth: true);
            if (shown)
            {
                IntPtr tray = TrayUiaReader.FindTrayHwnd();
                if (tray != IntPtr.Zero)
                {
                    // ABM_SETSTATE(0) is asynchronous: the bar can still sit in its 2 px sliver when
                    // the peek opens (2026-11-09 diag: y=1032 at one start, still y=1078 at the
                    // next). Wait for the EFFECT, then fall back to one verified SetWindowPos. A
                    // read taken on the parked, virtualized tray returns 0 buttons that say nothing
                    // about the tray being empty — so an unverified unpark must never be read.
                    trayFullyOnScreen = TrayAutoHideReveal.WaitFullyOnScreen(tray, UnparkWaitTimeoutMs)
                        || TaskbarNative.TryEnsureTrayOnScreen(tray, UnparkMoveTimeoutMs);
                    if (!trayFullyOnScreen)
                    {
                        TrayPeekDiagnostics.LogNote(
                            $"state-blip unpark NOT verified — read skipped attempt={_bootstrapBlips}");
                        outcome = TrayBlipOutcome.Inconclusive;
                        return null;
                    }
                }

                Thread.Sleep(PeekSettleMs);

                if (tray != IntPtr.Zero)
                {
                    // Evidence requires the tray fully on screen WHILE the read runs. "Not parked"
                    // alone also means "rect read failed" or "explorer is mid slide-out" — a read
                    // that never looked must not claim the tray is empty. The blip can also lose its
                    // unpark again during the settle (explorer clamps the bar back).
                    trayFullyOnScreen = TrayAutoHideReveal.IsFullyOnScreenNow(tray);
                    if (!trayFullyOnScreen)
                    {
                        TrayPeekDiagnostics.LogNote(
                            $"state-blip re-parked before the read — read skipped attempt={_bootstrapBlips}");
                        outcome = TrayBlipOutcome.Inconclusive;
                        return null;
                    }
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
                    TrayPeekDiagnostics.LogNote(
                        $"refresh outcome={outcome} blip=True onScreen={trayFullyOnScreen} " +
                        $"count={read.Count} attempt={_bootstrapBlips}");
                    return BuildCapturedItems(tray, read, skipCaptureIndex: -1, "refresh");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WharfBar: tray icon refresh failed — {ex.Message}");
        }
        finally
        {
            _taskbar.EndTrayPeek();
            if (refreshWindow)
            {
                _taskbar.EndIconRefresh();
            }

            if (locked)
            {
                _ = NativeMethods.LockWindowUpdate(IntPtr.Zero);
            }
        }

        // Peek that never opened, an unverified unpark, or a UIA walk that threw: nothing was
        // read, so nothing is proved. Only a tray that was fully on screen and read back zero
        // buttons is evidence of an empty tray.
        outcome = ClassifyBlipRead(trayFullyOnScreen, readWalkFailed, readCount);

        // The outcome decides whether the mirror is kept or cleared; the diag line is how a
        // false ConfirmedEmpty is diagnosed after the fact.
        TrayPeekDiagnostics.LogNote(
            $"refresh outcome={outcome} blip=True shown={shown} onScreen={trayFullyOnScreen} " +
            $"walkFailed={readWalkFailed} count={readCount} attempt={_bootstrapBlips}");
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
        TrayPeekDiagnostics.LogNote($"click begin kind={kind} key='{item.Key}'");

        // A menu owner may call SetCursorPos to its real tray icon at the screen's bottom edge
        // while it opens its menu (measured 0–20 ms after the keyboard Menu key). The guard
        // prevents that visible warp with a short cursor clip; an escape is put back, and a
        // refused SetCursorPos is logged instead of being reported as a restore.
        _ = NativeMethods.GetCursorPos(out POINT pointerHome);
        TrayPointerGuard? pointer = null;
        if (kind == TrayClick.Right && TryGetPrimaryMonitor(out RECT monitor))
        {
            pointer = TrayPointerGuard.Start(pointerHome, ToNativeRect(item.ScreenRect), monitor);
        }

        try
        {
            // Fast path: the button element cached by the last mirror read. It keeps working while
            // the tray is hidden (measured on 25H2), so the common click needs no peek, no app-bar
            // state change and no pointer input at all.
            AutomationElement? cached = item.Tag as AutomationElement;
            bool stale = false;
            if (cached is not null
                && TryReplayOnButton(item, cached, kind, anchorRect, out stale, pointer)
                && !stale)
            {
                TrayPeekDiagnostics.LogNote("click done (cached element)");
                return;
            }

            // The cached element died (explorer rebuilt its XAML tree, the shell restarted): one
            // bounded, cursor-free repair round — unpark the tray, read the live buttons, replay
            // the gesture on the fresh element.
            TrayPeekDiagnostics.LogNote(stale ? "click element stale — repair" : "click action refused — repair");
            if (RepairAndReplay(item, kind, anchorRect, pointer))
            {
                TrayPeekDiagnostics.LogNote("click done (repair)");
            }
            else
            {
                TrayPeekDiagnostics.LogNote("click dropped");
            }
        }
        finally
        {
            pointer?.Dispose();
        }
    }

    private static bool TryGetPrimaryMonitor(out RECT monitor)
    {
        monitor = default;
        IntPtr tray = TrayUiaReader.FindTrayHwnd();
        IntPtr anchor = tray != IntPtr.Zero ? tray : NativeMethods.GetDesktopWindow();
        IntPtr handle = NativeMethods.MonitorFromWindow(anchor, NativeMethods.MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>(),
        };
        if (handle == IntPtr.Zero || !NativeMethods.GetMonitorInfo(handle, ref info))
        {
            return false;
        }

        monitor = info.rcMonitor;
        return true;
    }

    /// <summary>
    /// Replays one mirrored gesture on a UIA button without any pointer input: left / double via
    /// <c>InvokePattern</c>, right via verified keyboard focus + the Menu key (Shift+F10 as the
    /// fallback for owners that ignore the Menu key). The re-anchor watcher starts <b>before</b>
    /// the gesture, so a popup created synchronously inside it is still a "new" window. Returns
    /// true when the UIA action was accepted; <paramref name="stale"/> says the element is gone.
    /// </summary>
    private static bool TryReplayOnButton(
        TrayMirrorItem item,
        AutomationElement element,
        TrayClick kind,
        Rect anchorRect,
        out bool stale,
        TrayPointerGuard? pointer = null)
    {
        stale = false;
        Rect buttonRect = item.ScreenRect.Width > 0
            ? item.ScreenRect
            : TrayUiaReader.GetScreenRect(element);
        RECT anchor = ToNativeRect(anchorRect);
        Task<bool>? reanchor = buttonRect.Width > 0 && anchor.Width > 0
            ? BeginReanchor(ToNativeRect(buttonRect), anchor)
            : null;

        switch (TrayUiaGestures.For(kind))
        {
            case TrayUiaGesture.FocusMenuKey:
                // The clip is armed inside the focus+menu-key sequence: the tray button's focus
                // change releases any earlier clip, and the owner warps the pointer right after
                // the key.
                if (!TrayUiaReader.TryOpenContextMenu(element, out stale, pointer is null ? null : pointer.ArmClip))
                {
                    return false;
                }

                // Owners that only answer Shift+F10 (measured: NVIDIA's tray button) show no popup
                // for the Menu key. Give the watcher a moment, then try the other keyboard gesture.
                if (!WaitForReanchor(reanchor, ClickReanchorWaitMs))
                {
                    pointer?.ArmClip();
                    SendInputKeys.TrySendShiftF10();
                    WaitForReanchor(reanchor, ClickReanchorWaitMs);
                }

                return true;

            case TrayUiaGesture.InvokeTwice:
                if (!TrayUiaReader.TryInvoke(element, out stale))
                {
                    return false;
                }

                Thread.Sleep(DoubleClickGapMs);
                TrayUiaReader.TryInvoke(element, out _);
                WaitForReanchor(reanchor, ClickReanchorWaitMs);
                return true;

            default:
                if (!TrayUiaReader.TryInvoke(element, out stale))
                {
                    return false;
                }

                WaitForReanchor(reanchor, ClickReanchorWaitMs);
                return true;
        }
    }

    /// <summary>
    /// Waits a bounded time for the popup watcher to find and move a popup. False when it has
    /// already finished without a candidate (so the caller may retry the gesture); true when it
    /// found one or is still watching (it keeps running for its own timeout).
    /// </summary>
    private static bool WaitForReanchor(Task<bool>? reanchor, int timeoutMs)
    {
        if (reanchor is null)
        {
            return true;
        }

        try
        {
            return reanchor.Wait(timeoutMs) ? reanchor.Result : true;
        }
        catch (Exception ex) when (ex is AggregateException or ObjectDisposedException)
        {
            TrayPeekDiagnostics.LogNote($"click reanchor wait: {ex.GetType().Name}");
            return true;
        }
    }

    /// <summary>
    /// Cursor-free repair round for a click whose cached element is gone: unpark the tray (a
    /// verified state blip — the only way to expose the XAML buttons), read the live tray, rebuild
    /// the mirror snapshot (which also refreshes its icons and elements), then replay the gesture
    /// on the fresh element. Returns false when the tray could not be read or the item is gone.
    /// </summary>
    private bool RepairAndReplay(TrayMirrorItem item, TrayClick kind, Rect anchorRect, TrayPointerGuard? pointer)
    {
        if (!_peekGate.Wait(ClickJoinGraceMs))
        {
            TrayPeekDiagnostics.LogNote("click repair skipped: tray peek gate busy");
            return false;
        }

        bool peeked = false;
        bool stateDropped = false;
        try
        {
            peeked = _taskbar.BeginTrayPeek(stealth: true);
            if (!peeked)
            {
                TrayPeekDiagnostics.LogNote("click repair peek FAILED");
                return false;
            }

            IntPtr tray = TrayUiaReader.FindTrayHwnd();
            if (tray == IntPtr.Zero)
            {
                return false;
            }

            // The parked auto-hide tray virtualizes its buttons: drop the state so explorer
            // unparks it, then verify the effect before reading it.
            if (TrayAutoHideReveal.IsParkedNow(tray))
            {
                stateDropped = true;
                _lastStateChange = DateTime.UtcNow;
                TrayPeekDiagnostics.LogStateChange("click-repair");
                _taskbar.BeginIconRefresh();
            }

            if (!TrayAutoHideReveal.WaitFullyOnScreen(tray, ClickRepairTimeoutMs))
            {
                _ = TaskbarNative.TryEnsureTrayOnScreen(tray, ClickRepairTimeoutMs);
            }

            if (!TrayAutoHideReveal.IsFullyOnScreenNow(tray))
            {
                TrayPeekDiagnostics.LogNote("click repair: tray did not come on screen");
                return false;
            }

            IReadOnlyList<UiaTrayButton>? live = null;
            for (int attempt = 0; attempt < RepairReadAttempts && live is not { Count: > 0 }; attempt++)
            {
                if (attempt > 0)
                {
                    Thread.Sleep(PeekSettleMs);
                }

                live = RunOnSta(() => TrayUiaReader.TryGetTrayButtons());
            }

            if (live is not { Count: > 0 })
            {
                TrayPeekDiagnostics.LogNote($"click repair: live read empty (walkFailed={live is null})");
                return false;
            }

            int index = ResolveLiveIndex(item, live);
            if (index < 0)
            {
                // The app is gone while the mirror still held its icon: drop the stale item.
                TrayPeekDiagnostics.LogNote("click skipped: item no longer in the tray");
                DropItem(item.Key);
                return false;
            }

            CaptureDuringClick(live, index);
            AutomationElement target = live[index].Element;
            return TryReplayOnButton(item, target, kind, anchorRect, out _, pointer);
        }
        catch (Exception ex)
        {
            TrayPeekDiagnostics.LogNote($"click repair error: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            if (stateDropped)
            {
                _taskbar.EndIconRefresh();
            }

            _taskbar.EndTrayPeek();
            _peekGate.Release();
        }
    }

    /// <summary>
    /// The click repair already has the tray shown, so refresh the icons then, throttled by
    /// <see cref="ClickCaptureMinAge"/>. Replaces the mirror snapshot with fresh elements, which
    /// is what makes the next click stay on the no-peek fast path.
    /// </summary>
    private void CaptureDuringClick(IReadOnlyList<UiaTrayButton> live, int targetIndex)
    {
        if (DateTime.UtcNow - _lastPixelCapture < ClickCaptureMinAge)
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
    /// Fresh UIA button index matching the mirrored item's key; -1 when gone. Matching rules live
    /// in <see cref="TrayButtonMatcher"/> (exact → containment for named keys; unnamed items
    /// resolve by their poll-time index — never containment, which would hit the wrong app).
    /// </summary>
    private static int ResolveLiveIndex(TrayMirrorItem item, IReadOnlyList<UiaTrayButton> buttons)
    {
        string?[] names = new string[buttons.Count];
        for (int i = 0; i < buttons.Count; i++)
        {
            names[i] = buttons[i].Name;
        }

        return TrayButtonMatcher.ResolveIndex(names, item.Key, item.Index);
    }

    /// <summary>Drops a mirrored item whose tray button no longer exists (the app exited).</summary>
    private void DropItem(string key)
    {
        if (!_lastGoodUiaItems.Any(i => string.Equals(i.Key, key, StringComparison.Ordinal)))
        {
            return;
        }

        IReadOnlyList<TrayMirrorItem> next = _lastGoodUiaItems
            .Where(i => !string.Equals(i.Key, key, StringComparison.Ordinal))
            .ToList();
        _lastGoodUiaItems = next;
        _iconCache.Retain(next.Select(static i => i.Key));
        Publish(next, force: true);
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
        // skips ApplyTrayMirror + a status layout pass every 4 s. The mirror STATE is compared too:
        // "not read yet" → "verified empty" publishes no new icons but changes what the chevron
        // must tell the user, so it has to raise the event.
        TrayMirrorState state = State;
        if (!force && state == _lastPublishedState && TrayMirrorDiff.IsSameSnapshot(_lastPublished, items))
        {
            return;
        }

        _lastPublished = items;
        _lastPublishedState = state;
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
