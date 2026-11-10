namespace WharfBar.Services;

/// <summary>Why a state blip (<c>ABM_SETSTATE(0)</c> ↔ <c>ABS_AUTOHIDE</c>) is allowed to run.</summary>
internal enum TrayStateBlipReason
{
    /// <summary>Not allowed — the bar keeps its (possibly stale) snapshot.</summary>
    None,

    /// <summary>First read of this shell session: the mirror has nothing to show yet.</summary>
    Bootstrap,

    /// <summary>The mirror is still empty after an earlier bootstrap could not read the tray.</summary>
    BootstrapRetry,

    /// <summary>Rare upkeep (new / exited tray apps) while the user is away.</summary>
    Maintenance,
}

/// <summary>
/// Pure gate for the only tray read that flaps the work area. Every blip reserves and releases
/// the taskbar strip, so every maximized/snapped/bottom-touching window jumps twice — a stale
/// icon is the lesser evil. Out-of-band requests (click follow-up, theme change, inconclusive
/// retry) never reach this gate; they only drive the stateless show-only peek.
/// <para>
/// A bootstrap that could not read the tray (lost unpark race, UIA walk threw, peek refused) is
/// <b>not evidence</b> that the tray is empty. It used to leave the mirror blank until a 10-minute
/// retry that also needed a 3-minute idle user (2026-11-09: on a machine where the quiet reveal
/// never works, a single lost race blanked the bar for the whole session). While the mirror has
/// never been filled the gate therefore allows a <b>short, bounded</b> retry series
/// (<see cref="BootstrapShortRetryInterval"/> × <see cref="MaxBootstrapAttempts"/>), and only a
/// <b>verified</b> empty tray (<c>trayConfirmedEmpty</c>) stops it at once.
/// </para>
/// </summary>
internal static class TrayStateBlipGate
{
    internal static readonly TimeSpan BootstrapRetryInterval = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(20);
    internal static readonly TimeSpan MinUserIdle = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Gap between the bounded retries of a bootstrap that never read the tray. Short on purpose:
    /// the desktop already re-flowed for the first attempt, so a blank bar is the worse trade —
    /// but the series is capped and a verified-empty tray never continues it.
    /// </summary>
    internal static readonly TimeSpan BootstrapShortRetryInterval = TimeSpan.FromSeconds(30);

    /// <summary>How many state blips a shell session may spend trying to fill an empty mirror.</summary>
    internal const int MaxBootstrapAttempts = 3;

    /// <summary>
    /// How long a bootstrap may stay deferred by the blocked-window checks before one blip runs
    /// anyway. The blocked paths do not advance the attempt counters (a blocked blip is not
    /// evidence about the tray), so without this escape a visible maximized window on the primary
    /// monitor would defer the only read for the whole session and the bar would show no tray
    /// icons at all (2026-11-10 field report: dual-monitor PC, <c>state-blip blocked
    /// reason=Bootstrap attempt=0</c> repeating forever). One work-area reflow is the lesser evil.
    /// </summary>
    internal static readonly TimeSpan MaxBlockedBootstrapWait = TimeSpan.FromSeconds(30);

    /// <param name="hasSnapshot">The mirror currently shows (held) icons.</param>
    /// <param name="bootstrapAttempts">
    /// State blips already spent on this explorer instance (0 = none yet). Maintenance requires at
    /// least one: a snapshot supplied by the quiet reveal refresh must never trigger a work-area
    /// flip.
    /// </param>
    /// <param name="trayConfirmedEmpty">The last blip verifiably showed the tray and it had no app icons.</param>
    /// <param name="sinceLastStateChange">Time since the last tray-driven state flip.</param>
    /// <param name="userIdle">Time since the last user input.</param>
    internal static TrayStateBlipReason Decide(
        bool hasSnapshot,
        int bootstrapAttempts,
        bool trayConfirmedEmpty,
        TimeSpan sinceLastStateChange,
        TimeSpan userIdle)
    {
        bool userAway = userIdle >= MinUserIdle;
        if (!hasSnapshot)
        {
            // Nothing on the bar: the bootstrap is the only read that can fill it. Once the quiet
            // reveal refresh supplies a snapshot this branch is never reached — a work-area flapping
            // blip must not run for pixels the mirror already has.
            if (bootstrapAttempts == 0)
            {
                return TrayStateBlipReason.Bootstrap;
            }

            // Proven empty: nothing to fetch until the shell restarts (a click/overflow session
            // re-reads the tray anyway).
            if (trayConfirmedEmpty)
            {
                return TrayStateBlipReason.None;
            }

            // The earlier attempts never read the tray, so they prove nothing. Retry while the bar
            // is blank, bounded, without waiting for a 3-minute idle user (nobody can be idle and
            // blank-barred at the same time for long anyway) — see the type doc.
            if (bootstrapAttempts < MaxBootstrapAttempts
                && sinceLastStateChange >= BootstrapShortRetryInterval)
            {
                return TrayStateBlipReason.BootstrapRetry;
            }

            return userAway && sinceLastStateChange >= BootstrapRetryInterval
                ? TrayStateBlipReason.BootstrapRetry
                : TrayStateBlipReason.None;
        }

        // Maintenance only exists to refresh a mirror that a state blip itself filled. When the
        // quiet reveal refresh supplied the snapshot, no blip ever ran this shell — flipping the
        // work area for pixels already on the bar would be a gratuitous jump, so stay at None.
        return bootstrapAttempts > 0 && userAway && sinceLastStateChange >= MaintenanceInterval
            ? TrayStateBlipReason.Maintenance
            : TrayStateBlipReason.None;
    }

    /// <summary>
    /// Pure: may a bootstrap that the blocked-window checks keep deferring run anyway? The blocked
    /// paths never advance <c>bootstrapAttempts</c> (a deferred blip proves nothing about the
    /// tray), so without this escape a visible maximized window on the primary monitor would
    /// defer the only read for the whole session. Only the first-read cases (Bootstrap and the
    /// bounded BootstrapRetry series) are forced; the bounded series still caps how often.
    /// </summary>
    internal static bool ShouldForceBlockedBootstrap(
        bool hasSnapshot,
        TrayStateBlipReason reason,
        TimeSpan blockedFor) =>
        !hasSnapshot
        && reason is TrayStateBlipReason.Bootstrap or TrayStateBlipReason.BootstrapRetry
        && blockedFor >= MaxBlockedBootstrapWait;

    /// <summary>
    /// Bootstrap of a fresh shell only waits for maximized windows (startup re-flows the desktop
    /// anyway); every later blip also waits for snapped / bottom-touching windows.
    /// </summary>
    internal static bool ChecksAllAffectedWindows(TrayStateBlipReason reason) =>
        reason is TrayStateBlipReason.BootstrapRetry or TrayStateBlipReason.Maintenance;

    /// <summary>
    /// The bounded bootstrap retries (<see cref="MaxBootstrapAttempts"/> per shell) keep the cheap
    /// check: the desktop already re-flowed for the first blip, and the mirror is blank — waiting
    /// for every snapped/bottom-touching window to move would leave the bar empty indefinitely.
    /// The long-interval retry and maintenance keep the full check.
    /// </summary>
    internal static bool ChecksAllAffectedWindows(TrayStateBlipReason reason, int bootstrapAttempts) =>
        reason == TrayStateBlipReason.BootstrapRetry
            && bootstrapAttempts < MaxBootstrapAttempts
            ? false
            : ChecksAllAffectedWindows(reason);
}
