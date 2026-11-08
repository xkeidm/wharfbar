namespace WharfBar.Services;

/// <summary>
/// Pure gate for the quiet tray read: WharfBar keeps the taskbar in <c>ABS_AUTOHIDE</c> while it
/// runs, and explorer parks the bar in a ~2 px sliver and virtualizes its XAML tray content — a
/// stealth show in place reads back zero buttons (the diag log proves it: <c>direct read
/// buttons=0</c> in every parked sample, <c>buttons=11</c> whenever the tray happened to be
/// revealed). The only quiet way to expose the buttons is what the click path already does:
/// hover the bottom edge with real input so explorer slides the auto-hide tray on screen — no
/// work-area change, no window jump.
/// <para>
/// That move is visible to the user (the cursor hops to the bottom edge for a few hundred ms), so
/// it only runs while the pointer is parked/idle — or when the mirror is empty, where a short hop
/// is strictly better than a blank bar. A busy user never has the cursor yanked while working;
/// the next idle pause picks the icons up.
/// </para>
/// <para>
/// The idle rule alone is not enough on a machine where the reveal keeps failing (or where the
/// tray is genuinely empty): an empty mirror would bypass the idle wait on every cadence tick,
/// and a failing reveal would be retried forever. <see cref="IsRevealBackedOff"/> caps retries
/// after <see cref="RevealMissLimit"/> consecutive misses, and the empty-mirror bypass expires
/// after <see cref="EmptyMirrorBypassAttempts"/> tries in a shell session.
/// </para>
/// </summary>
internal static class TrayRevealPolicy
{
    /// <summary>Mouse/keyboard quiet time that makes a cursor hop acceptable.</summary>
    internal static readonly TimeSpan MinUserIdle = TimeSpan.FromSeconds(1.5);

    /// <summary>How long a failing quiet reveal is skipped before it is tried again.</summary>
    internal static readonly TimeSpan RevealBackoff = TimeSpan.FromMinutes(10);

    /// <summary>Consecutive reveal misses that trip <see cref="RevealBackoff"/>.</summary>
    internal const int RevealMissLimit = 2;

    /// <summary>
    /// Quiet attempts an empty mirror may force while the user is active. After these the normal
    /// idle wait applies: a genuinely empty tray (or a shell that never exposes it) must not hop
    /// the cursor forever behind a user who is working.
    /// </summary>
    internal const int EmptyMirrorBypassAttempts = 3;

    /// <summary>
    /// Should the quiet refresh reveal the parked tray now?
    /// <paramref name="hasSnapshot"/> false (blank mirror) bypasses the idle wait: there is
    /// nothing stale to protect and a hop is the only way to fill the bar.
    /// </summary>
    internal static bool ShouldReveal(TimeSpan userIdle, bool hasSnapshot) =>
        !hasSnapshot || userIdle >= MinUserIdle;

    /// <summary>
    /// Full gate for one quiet reveal attempt: back off after
    /// <see cref="RevealMissLimit"/> consecutive misses, and stop forcing an empty-mirror
    /// reveal while the user is active once <see cref="EmptyMirrorBypassAttempts"/> have run
    /// in this shell session.
    /// </summary>
    internal static bool ShouldAttemptReveal(
        TimeSpan userIdle,
        bool hasSnapshot,
        int consecutiveRevealMisses,
        TimeSpan sinceLastRevealMiss,
        int emptyMirrorBypassUsed)
    {
        if (IsRevealBackedOff(consecutiveRevealMisses, sinceLastRevealMiss))
        {
            return false;
        }

        bool emptyMirrorBypass = !hasSnapshot && emptyMirrorBypassUsed < EmptyMirrorBypassAttempts;
        return emptyMirrorBypass || ShouldReveal(userIdle, hasSnapshot: true);
    }

    /// <summary>
    /// True while a reveal that just missed should be skipped instead of trying again. Two misses
    /// in a row mean the hover gesture is not working on this machine; retrying every cadence
    /// tick would only keep moving the pointer under the user (docs/04: skip for 10 min).
    /// </summary>
    internal static bool IsRevealBackedOff(int consecutiveRevealMisses, TimeSpan sinceLastRevealMiss) =>
        consecutiveRevealMisses >= RevealMissLimit && sinceLastRevealMiss < RevealBackoff;
}
