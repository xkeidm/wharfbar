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
/// </summary>
internal static class TrayStateBlipGate
{
    internal static readonly TimeSpan BootstrapRetryInterval = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan MaintenanceInterval = TimeSpan.FromMinutes(20);
    internal static readonly TimeSpan MinUserIdle = TimeSpan.FromMinutes(3);

    /// <param name="hasSnapshot">The mirror currently shows (held) icons.</param>
    /// <param name="blippedThisShell">A state blip already ran for this explorer instance.</param>
    /// <param name="trayConfirmedEmpty">The last blip showed the tray and it had no app icons.</param>
    /// <param name="sinceLastStateChange">Time since the last tray-driven state flip.</param>
    /// <param name="userIdle">Time since the last user input.</param>
    internal static TrayStateBlipReason Decide(
        bool hasSnapshot,
        bool blippedThisShell,
        bool trayConfirmedEmpty,
        TimeSpan sinceLastStateChange,
        TimeSpan userIdle)
    {
        if (!blippedThisShell)
        {
            return TrayStateBlipReason.Bootstrap;
        }

        bool userAway = userIdle >= MinUserIdle;
        if (!hasSnapshot)
        {
            // Proven empty: nothing to fetch until the shell restarts (a click/overflow session
            // re-reads the tray anyway).
            if (trayConfirmedEmpty)
            {
                return TrayStateBlipReason.None;
            }

            return userAway && sinceLastStateChange >= BootstrapRetryInterval
                ? TrayStateBlipReason.BootstrapRetry
                : TrayStateBlipReason.None;
        }

        return userAway && sinceLastStateChange >= MaintenanceInterval
            ? TrayStateBlipReason.Maintenance
            : TrayStateBlipReason.None;
    }

    /// <summary>
    /// Bootstrap of a fresh shell only waits for maximized windows (startup re-flows the desktop
    /// anyway); every later blip also waits for snapped / bottom-touching windows.
    /// </summary>
    internal static bool ChecksAllAffectedWindows(TrayStateBlipReason reason) =>
        reason is TrayStateBlipReason.BootstrapRetry or TrayStateBlipReason.Maintenance;
}
