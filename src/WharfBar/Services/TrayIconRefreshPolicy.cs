namespace WharfBar.Services;

/// <summary>
/// Pure cadence rules for the tray mirror's <b>show-only</b> pixel refresh (stealth peek, no
/// app-bar state change, no work-area change). Pixels can only be captured while the tray is
/// shown, so freshness is bounded by how often this may run — independent of whether the cheap
/// direct UIA read returned buttons. The state blip has its own gate
/// (<see cref="TrayStateBlipGate"/>) and is never brought forward by these requests.
/// </summary>
internal static class TrayIconRefreshPolicy
{
    /// <summary>Show-only cadence while icons are changing (a call/mute icon, a badge).</summary>
    internal static readonly TimeSpan ActiveShowOnlyInterval = TimeSpan.FromSeconds(20);

    /// <summary>Floor between refreshes when one was requested out of band.</summary>
    internal static readonly TimeSpan ExpediteMinGap = TimeSpan.FromSeconds(15);

    /// <summary>After a mirror click: time for the owner's menu action (mute, pause) to land.</summary>
    internal static readonly TimeSpan ClickFollowUpDelay = TimeSpan.FromSeconds(6);

    /// <summary>After a theme / accent change: time for explorer to repaint the tray.</summary>
    internal static readonly TimeSpan AppearanceSettleDelay = TimeSpan.FromSeconds(3);

    /// <summary>A click session re-captures the strip only when the last capture is older than this.</summary>
    internal static readonly TimeSpan ClickCaptureMinAge = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Minimum time since the last show-only refresh: tighter while icons change, relaxed once
    /// they are stable, the expedite floor when a refresh was requested.
    /// </summary>
    internal static TimeSpan MinGap(bool expedite, bool iconsRecentlyChanged)
    {
        if (expedite)
        {
            return ExpediteMinGap;
        }

        return iconsRecentlyChanged ? ActiveShowOnlyInterval : TrayMirrorService.BlipMinInterval;
    }

    internal static bool ShouldRefresh(TimeSpan sinceLastRefresh, TimeSpan minGap, bool clickBusy) =>
        !clickBusy && sinceLastRefresh >= minGap;

    /// <summary>An out-of-band request (0 = none) is due once its time has come.</summary>
    internal static bool IsExpediteDue(long dueTicks, DateTime now) => dueTicks != 0 && now.Ticks >= dueTicks;
}
