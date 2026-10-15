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
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Show-only refresh interval while icons are stable (no app-bar state change); it tightens
    /// to <see cref="TrayIconRefreshPolicy.ActiveShowOnlyInterval"/> while they change.
    /// </summary>
    internal static readonly TimeSpan BlipMinInterval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long empty direct reads may keep the held snapshot while a quiet (show-only) refresh
    /// is available to confirm or refill it. Without a quiet path the snapshot is held until
    /// evidence (see <see cref="ShouldHoldSnapshotOnEmptyRead"/>).
    /// </summary>
    internal static readonly TimeSpan EmptyReadGraceWindow = TimeSpan.FromMinutes(3);

    private const int PeekSettleMs = 160;

    /// <summary>Hover-reveal budget for a click (auto-hide slide-in is ~0.3–0.6 s).</summary>
    private const int RevealTimeoutMs = 1200;

    /// <summary>Consecutive reveal misses after which clicks go straight to the state fallback.</summary>
    private const int RevealFailureLimit = 2;

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
    private const int ClickJoinPollMs = 50;
    private const int LiveResolveAttempts = 10;
    private const int LiveResolvePollMs = 100;

    /// <summary>
    /// Kaç ardışık boş UIA okuması boyunca son iyi snapshot tutulur (yalnız sessiz bir yenileme
    /// yolu varken). Sabit <b>değil, türetilir</b>: eşik <see cref="EmptyReadGraceWindow"/>
    /// penceresini aşmak zorunda; daha kısa bir eşik aynayı yenilemeler arasındaki boşluğun
    /// ortasında temizler. <c>+2</c> tolerans: pencere dolduğunda yeni yenileme henüz bir
    /// sonraki zamanlayıcı dilimini bekler.
    /// </summary>
    internal static readonly int EmptyReadGraceReads =
        (int)Math.Ceiling(EmptyReadGraceWindow.TotalMilliseconds / PollInterval.TotalMilliseconds) + 2;

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
    private readonly TrayIconCache<ImageSource> _iconCache = new();
    private IReadOnlyList<TrayMirrorItem> _lastGoodUiaItems = [];
    private IReadOnlyList<TrayMirrorItem> _lastPublished = [];
    private int _consecutiveEmptyUiaReads;
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
    private DateTime _lastRevealFailure;
    // Once UIA has delivered icons, the mirror source is locked for the session: the
    // classic toolbar can transiently reappear during state blips (unpark windows) and
    // its differently-keyed items would wipe and re-add the whole mirror on every flip.
    private bool _uiaLocked;

    /// <summary>
    /// null = not probed yet. true = a stealth show without <c>ABM_SETSTATE</c> returned icons,
    /// so later refreshes must not flap the work area. false = that read was empty and the
    /// state blip is required.
    /// </summary>
    private bool? _showOnlyReadWorks;

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
        // Desktop-root UIA read (TrayUiaReader). While the strip-release auto-hide state
        // is on, explorer virtualizes the tray content away and this read returns empty;
        // a state blip (see TryRefreshViaBlip) re-exposes it only when that is invisible.
        // Poll itself already runs on the STA worker (queued there by the timer), so a
        // direct call — RunOnSta here would deadlock on the queue it is blocking.
        TrackShellInstance();
        IReadOnlyList<UiaTrayButton>? buttons = TrayUiaReader.TryGetTrayButtons();
        bool directHasButtons = buttons is { Count: > 0 };
        LogDirectReadTransition(buttons?.Count ?? -1);

        // Pixels exist only while the tray is shown, so the refresh peek runs on its own cadence
        // even when the direct read has buttons — gating it on an EMPTY direct read froze every
        // icon (Discord mute/call state) for the whole session on machines where it is not empty.
        IReadOnlyList<TrayMirrorItem>? blip = TryRefreshViaBlip(directHasButtons, out TrayBlipOutcome blipOutcome);
        if (blip is { Count: > 0 })
        {
            _consecutiveEmptyUiaReads = 0;
            _lastGoodUiaItems = blip;
            _uiaLocked = true;
            _iconCache.Retain(blip.Select(static item => IconCacheKey(item.Key, item.Index)));
            return blip;
        }

        // Null (walk failed), empty (virtualized parked tray) and a blip that proved nothing all
        // mean "no fresh data" — fall through to the last-good grace instead of publishing an
        // empty snapshot that would collapse the mirror 200ms after every refresh.
        buttons ??= [];

        IntPtr tray = TrayUiaReader.FindTrayHwnd();

        // Cached pixels for known keys; a new key gets one shared capture only if the tray is
        // actually shown (outside a peek it never is — PrintWindow would paint nothing), else a
        // placeholder glyph that the next refresh replaces.
        var items = new List<TrayMirrorItem>(buttons.Count);
        DateTime now = DateTime.UtcNow;
        TrayCaptureFrame? frame = null;
        bool frameTried = false;
        var stats = default(CaptureStats);
        Dictionary<string, Rect> knownRects = RectsByKey(_lastGoodUiaItems);
        for (int i = 0; i < buttons.Count; i++)
        {
            UiaTrayButton b = buttons[i];
            string cacheKey = IconCacheKey(b.Name, i);
            Rect rect = knownRects.GetValueOrDefault(b.Name);
            if (!_iconCache.TryGet(cacheKey, out ImageSource icon))
            {
                if (!frameTried)
                {
                    frameTried = true;
                    _ = TrayCapture.TryCaptureFrame(tray, out frame);
                }

                icon = CaptureIcon(frame, b, cacheKey, now, captureAttempted: frame is not null, ref stats, out Rect captured);
                if (captured.Width > 0)
                {
                    rect = captured;
                }
            }

            // Key by name: most tray apps keep a stable tooltip; state-bearing names
            // (e.g. EarTrumpet percent) churn that one icon — acceptable UIA-path cost.
            items.Add(new TrayMirrorItem(b.Name, i, i, b.Name, icon, b.Element, rect));
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
            // Without a quiet refresh path (show-only latched off) nothing but a work-area flap
            // could confirm anything, so the snapshot is held until evidence arrives.
            _consecutiveEmptyUiaReads++;
            if (ShouldHoldSnapshotOnEmptyRead(
                    _consecutiveEmptyUiaReads,
                    _lastGoodUiaItems.Count,
                    blipOutcome == TrayBlipOutcome.ConfirmedEmpty,
                    quietRefreshAvailable: _showOnlyReadWorks != false))
            {
                return _lastGoodUiaItems;
            }

            // Grace exhausted, or the tray proven empty: it really is empty. Drop the held
            // snapshot too, otherwise a later single empty read would resurrect the stale items —
            // and their UIA elements with them, which is what a mirrored click forwards to.
            _lastGoodUiaItems = [];
            _consecutiveEmptyUiaReads = 0;
            _iconCache.Clear();
            return items;
        }

        _consecutiveEmptyUiaReads = 0;
        _lastGoodUiaItems = items;
        _iconCache.Retain(items.Select(static item => IconCacheKey(item.Key, item.Index)));
        if (items.Count > 0)
        {
            _uiaLocked = true;
        }

        return items;
    }

    /// <summary>
    /// Pure: does an empty UIA read keep the held snapshot on the bar, or clear the mirror?
    /// With a quiet refresh path the grace is bounded — that path will confirm or refill within
    /// the window. Without one (only a work-area flap could read the tray) the snapshot is held
    /// until evidence: a stale icon is better than a blank mirror or a desktop that jumps.
    /// <paramref name="trayConfirmedEmptyByBlip"/> short-circuits both: a blip that really ran
    /// already proved the tray has nothing.
    /// </summary>
    internal static bool ShouldHoldSnapshotOnEmptyRead(
        int consecutiveEmptyReads,
        int heldItemCount,
        bool trayConfirmedEmptyByBlip,
        bool quietRefreshAvailable = true) =>
        !trayConfirmedEmptyByBlip
        && heldItemCount > 0
        && (!quietRefreshAvailable || consecutiveEmptyReads < EmptyReadGraceReads);

    /// <summary>
    /// A refresh may skip the app-bar state toggle once a show-only read has succeeded.
    /// Unknown (null) still tries show-only first. A failed probe (false) goes straight to
    /// the state blip — repeating a doomed show would flash the shell twice per interval.
    /// </summary>
    internal static bool ShouldTryShowOnly(bool? showOnlyWorks) => showOnlyWorks != false;

    /// <summary>
    /// Latches the show-only probe. A success sticks. An empty read after a success does not
    /// revoke it (that would bring the work-area flap back). An empty read before any success
    /// latches failure so the next refreshes use the state blip only.
    /// </summary>
    internal static bool? LatchShowOnly(bool? current, bool foundIcons)
    {
        if (current == false)
        {
            return false;
        }

        if (foundIcons || current == true)
        {
            return true;
        }

        return false;
    }

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
    private IReadOnlyList<TrayMirrorItem>? TryRefreshViaBlip(bool directHasButtons, out TrayBlipOutcome outcome)
    {
        outcome = TrayBlipOutcome.Skipped;
        if (_clickBusy != 0)
        {
            return null;
        }

        DateTime now = DateTime.UtcNow;

        // Quiet path: the stealth show-only peek touches no app-bar state and no work area, so
        // it alone follows the refresh cadence and the out-of-band requests.
        if (ShouldTryShowOnly(_showOnlyReadWorks))
        {
            long expediteTicks = Interlocked.Read(ref _expediteDueTicks);
            bool expedite = TrayIconRefreshPolicy.IsExpediteDue(expediteTicks, now);
            TimeSpan minGap = TrayIconRefreshPolicy.MinGap(expedite, _iconsRecentlyChanged);
            if (!TrayIconRefreshPolicy.ShouldRefresh(now - _lastBlip, minGap, clickBusy: false))
            {
                return null;
            }

            _lastBlip = now;
            if (expedite)
            {
                _ = Interlocked.CompareExchange(ref _expediteDueTicks, 0, expediteTicks);
            }

            IReadOnlyList<TrayMirrorItem>? quiet = TryPeekIcons(stateBlip: false, out outcome);
            if (quiet is { Count: > 0 })
            {
                _showOnlyReadWorks = LatchShowOnly(_showOnlyReadWorks, foundIcons: true);
                return AfterRefresh(quiet, outcome);
            }

            if (directHasButtons)
            {
                // The direct read just saw buttons, so an empty show-only read contradicts it —
                // a rebuild race, not a broken show-only path. Neither latch nor escalate.
                outcome = TrayBlipOutcome.Inconclusive;
                return AfterRefresh(null, outcome);
            }

            _showOnlyReadWorks = LatchShowOnly(_showOnlyReadWorks, foundIcons: false);
            if (_showOnlyReadWorks == true)
            {
                outcome = TrayBlipOutcome.Inconclusive;
                return AfterRefresh(null, outcome);
            }

            TrayPeekDiagnostics.LogNote("show-only latched off: a parked auto-hide tray exposes no buttons");
        }

        if (directHasButtons)
        {
            outcome = TrayBlipOutcome.Skipped;
            return null;
        }

        // Loud path: ABM_SETSTATE(0) ↔ ABS_AUTOHIDE reserves and releases the taskbar strip, so
        // every maximized/snapped/bottom-touching window jumps twice. Gated hard (bootstrap of
        // an empty mirror, or rare upkeep while the user is away); never brought forward by
        // clicks, theme changes or failed peeks. A blocked blip does not stamp the clock.
        TrayStateBlipReason reason = TrayStateBlipGate.Decide(
            hasSnapshot: _lastGoodUiaItems.Count > 0,
            blippedThisShell: _stateBlippedThisShell,
            trayConfirmedEmpty: _stateBlipConfirmedEmpty,
            sinceLastStateChange: now - _lastStateChange,
            userIdle: TrayAutoHideReveal.UserIdleTime());
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

        _lastBlip = now;
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

    /// <summary>
    /// A new <c>Shell_TrayWnd</c> means explorer restarted: its tray is a fresh unknown, so the
    /// show-only probe and the bootstrap blip are re-armed for it.
    /// </summary>
    private void TrackShellInstance()
    {
        IntPtr tray = TrayUiaReader.FindTrayHwnd();
        if (tray == IntPtr.Zero || tray == _shellTray)
        {
            return;
        }

        if (_shellTray != IntPtr.Zero)
        {
            TrayPeekDiagnostics.LogNote($"shell restarted tray={tray}");
        }

        _shellTray = tray;
        _showOnlyReadWorks = null;
        _stateBlippedThisShell = false;
        _stateBlipConfirmedEmpty = false;
        _revealFailures = 0;
    }

    /// <summary>
    /// One quick retry after a refresh that never read the tray (peek refused, walk threw):
    /// otherwise the next try is a whole interval away and the empty-read grace can run out.
    /// A refresh that did read the tray re-arms the retry.
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
    /// Shows the tray just long enough to read buttons and capture icons. <paramref name="stateBlip"/>
    /// drops <c>ABS_AUTOHIDE</c> first — that unparks a virtualized tray, and it also flaps the
    /// work area. The show-only path must not move the window; a <c>SetWindowPos</c> alone makes
    /// explorer resync the bars.
    /// </summary>
    private IReadOnlyList<TrayMirrorItem>? TryPeekIcons(bool stateBlip, out TrayBlipOutcome outcome)
    {
        TrayPeekDiagnostics.LogNote(stateBlip ? "refresh state-blip" : "refresh show-only");
        bool shown = false;
        bool readWalkFailed = true;
        int readCount = 0;
        bool refresh = false;
        bool locked = false;
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
                }

                Thread.Sleep(PeekSettleMs);

                IReadOnlyList<UiaTrayButton>? read = TrayUiaReader.TryGetTrayButtons();
                readWalkFailed = read is null;
                readCount = read?.Count ?? 0;
                if (read is { Count: > 0 })
                {
                    outcome = TrayBlipOutcome.Refreshed;
                    return BuildCapturedItems(tray, read, skipCaptureIndex: -1, "refresh");
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
            if (refresh)
            {
                _taskbar.EndIconRefresh();
            }

            if (locked)
            {
                _ = NativeMethods.LockWindowUpdate(IntPtr.Zero);
                locked = false;
            }
        }

        // Peek that never opened, or a UIA walk that threw: nothing was read, so nothing is
        // proved — the grace keeps holding. Only a shown tray that read back zero buttons is
        // evidence of an empty tray.
        outcome = ClassifyBlipRead(shown, readWalkFailed, readCount);
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
        var items = new List<TrayMirrorItem>(read.Count);
        for (int i = 0; i < read.Count; i++)
        {
            UiaTrayButton b = read[i];
            string cacheKey = IconCacheKey(b.Name, i);
            ImageSource icon;
            Rect rect;
            if (i == skipCaptureIndex)
            {
                rect = TrayUiaReader.GetIconRect(b.Element);
                icon = _iconCache.TryGet(cacheKey, out ImageSource cached)
                    ? cached
                    : _iconCache.Fallback(cacheKey, () => TrayLetterGlyph.Build(b.Name), now, captureAttempted: false);
            }
            else
            {
                icon = CaptureIcon(frame, b, cacheKey, now, captureAttempted: true, ref stats, out rect);
            }

            if (rect.IsEmpty || rect.Width <= 0)
            {
                rect = knownRects.GetValueOrDefault(b.Name);
            }

            // Key by name: most tray apps keep a stable tooltip; state-bearing names
            // (e.g. EarTrumpet percent) churn that one icon — acceptable UIA-path cost.
            items.Add(new TrayMirrorItem(b.Name, i, i, b.Name, icon, b.Element, rect));
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

    /// <summary>Unnamed buttons get an index key so two of them do not overwrite each other's pixels.</summary>
    private static string IconCacheKey(string name, int index) =>
        name.Length > 0 ? name : $"\0#{index}";

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
        var cts = new CancellationTokenSource();
        _holdCts = cts;

        // The stored element is captured during a peek; the shell tears its XAML tree down
        // when we hide the bar again, so by click time it is usually dead — Invoke/rect
        // would silently no-op. Resolve a FRESH element from the live tree once the
        // visible peek has brought the tray back.
        AutomationElement stale = item.Tag as AutomationElement ?? throw new InvalidOperationException("UIA mirror click without an element.");

        // While ABS_AUTOHIDE is on, explorer parks the tray in a 2 px sliver and virtualizes its
        // buttons. Hovering the bottom edge reveals it the way a user would — no work-area
        // change. Only when that reveal fails does the click fall back to dropping the state
        // (BeginIconRefresh), which reserves the strip and moves windows; that is logged as a
        // state change. The cursor lease puts the pointer back after the whole session.
        using IDisposable cursorHome = SendInputMouse.HoldCursorHome();
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
            stateDropped = !RevealTrayForClick(item);

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
            if (_holdCts == cts)
            {
                _holdCts = null;
            }

            TaskbarNative.EndTrayClickInput();
            _taskbar.EndTrayPeek();
            // A tray click usually changes that app's icon (mute, pause, connect) — or removed a
            // dead item; refresh once the owner's menu action has had time to land. Only the
            // quiet show-only path honours this; it never brings a state blip forward.
            RequestExpeditedRefresh(TrayIconRefreshPolicy.ClickFollowUpDelay);
            TrayPeekDiagnostics.LogNote("click end");
        }
        }
        finally
        {
            if (stateDropped)
            {
                _taskbar.EndIconRefresh();
            }
        }
    }

    /// <summary>
    /// Gets the parked tray on screen for a click. Hover reveal first (no work-area change);
    /// after <see cref="RevealFailureLimit"/> consecutive misses the reveal is skipped for a
    /// while. Returns false when it had to drop the app-bar state instead — the caller then owes
    /// <see cref="TaskbarVisibilityService.EndIconRefresh"/>.
    /// </summary>
    private bool RevealTrayForClick(TrayMirrorItem item)
    {
        IntPtr tray = TrayUiaReader.FindTrayHwnd();
        DateTime now = DateTime.UtcNow;
        bool tryReveal = _revealFailures < RevealFailureLimit
            || now - _lastRevealFailure >= TrayStateBlipGate.BootstrapRetryInterval;
        if (tryReveal)
        {
            int x = item.ScreenRect.Width > 0
                ? (int)Math.Round(item.ScreenRect.Left + (item.ScreenRect.Width / 2))
                : DefaultRevealX(tray);
            if (TrayAutoHideReveal.TryReveal(tray, x, RevealTimeoutMs, out int elapsedMs))
            {
                _revealFailures = 0;
                TrayPeekDiagnostics.LogNote($"click reveal ok x={x} ms={elapsedMs}");
                return true;
            }

            _revealFailures++;
            _lastRevealFailure = now;
            TrayPeekDiagnostics.LogNote($"click reveal FAILED x={x} ms={elapsedMs} misses={_revealFailures}");
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
    /// The click already has the tray shown — the only moment pixels exist on a machine where
    /// background peeks cannot read the parked tray. Refresh the icons then (the hovered target
    /// keeps its cached pixels), throttled by <see cref="TrayIconRefreshPolicy.ClickCaptureMinAge"/>.
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
                _consecutiveEmptyUiaReads = 0;
                _lastGoodUiaItems = items;
                _uiaLocked = true;
                _iconCache.Retain(items.Select(static i => IconCacheKey(i.Key, i.Index)));
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
        // Steady polls hand back the same keys and cached icon instances: no Changed, so the bar
        // skips ApplyTrayMirror + a status layout pass every 4 s.
        if (TrayMirrorDiff.IsSameSnapshot(_lastPublished, items))
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
