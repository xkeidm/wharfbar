namespace WharfBar.Services;

/// <summary>
/// Pure SMTC timeline math: interpolates a live playback position from the last value the app reported.
/// </summary>
/// <remarks>
/// <para>
/// <c>GlobalSystemMediaTransportControlsSessionTimelineProperties.Position</c> does not advance on its own.
/// A player publishes a still image (position + <c>LastUpdatedTime</c>) only when it reports (play/pause,
/// seek, track change), so between reports the raw value is frozen. The island music panel polls
/// <see cref="MediaSessionService.RefreshTimeline"/> while it is open; interpolating locally keeps the
/// progress/time/duration live:
/// </para>
/// <para>
/// <c>position = rawPosition + (now - lastUpdatedTime) * playbackRate</c>, clamped to
/// <c>[0, duration]</c>, where <c>duration = EndTime - StartTime</c>.
/// </para>
/// <para>
/// Defensive rules (all covered by unit tests): a paused session does not advance; a default or future
/// <c>LastUpdatedTime</c> is not extrapolated; a non-positive/non-finite <c>PlaybackRate</c> falls back
/// to 1.0; a play rate beyond <see cref="MaxPlaybackRate"/> is treated as 1.0; and an anchor older than
/// <see cref="MaxInterpolationGap"/> is considered stale (sleep/resume, a player that stopped reporting)
/// so no absurd jump is invented. A negative position is clamped to zero.
/// </para>
/// </remarks>
internal static class MediaTimeline
{
    /// <summary>
    /// Minimum position advance that justifies a <see cref="MediaSessionService.Changed"/> raise.
    /// At the island panel's ~250 ms poll this caps the event rate at ~4-5 per second.
    /// </summary>
    internal const double RaisePositionDeltaMs = 200;

    /// <summary>Playback rates outside this bound are not trusted; only 1.0 is really common.</summary>
    internal const double MaxPlaybackRate = 16.0;

    /// <summary>
    /// An anchor older than this is not extrapolated: the elapsed time cannot be trusted
    /// (system sleep/resume, a player that stopped republishing timeline data).
    /// </summary>
    internal static readonly TimeSpan MaxInterpolationGap = TimeSpan.FromHours(1);

    /// <summary>
    /// A raw SMTC timeline reading plus the playback state needed to turn it into a live position.
    /// <see cref="Duration"/> is <c>EndTime - StartTime</c>; null when the session reports no duration.
    /// </summary>
    internal readonly record struct MediaTimelineSample(
        TimeSpan? Position,
        TimeSpan? Duration,
        DateTimeOffset LastUpdated,
        double PlaybackRate,
        bool IsPlaying);

    /// <summary>
    /// Live position at <paramref name="now"/>, or null when the session reports no position.
    /// The result is clamped to <c>[0, duration]</c> when the duration is usable.
    /// </summary>
    internal static TimeSpan? Interpolate(in MediaTimelineSample sample, DateTimeOffset now)
    {
        if (sample.Position is not TimeSpan rawPosition)
        {
            return null;
        }

        TimeSpan? duration = sample.Duration is TimeSpan reported && reported > TimeSpan.Zero
            ? reported
            : null;

        TimeSpan position = rawPosition < TimeSpan.Zero ? TimeSpan.Zero : rawPosition;

        if (sample.IsPlaying && sample.LastUpdated != default)
        {
            TimeSpan elapsed = now - sample.LastUpdated;

            // A future timestamp (clock skew / a bogus report) means "no time has passed".
            // An aged-out anchor is left at the last reported value instead of guessing.
            if (elapsed > TimeSpan.Zero && elapsed <= MaxInterpolationGap)
            {
                double rate = sample.PlaybackRate;
                if (!double.IsFinite(rate) || rate <= 0 || rate > MaxPlaybackRate)
                {
                    rate = 1.0;
                }

                position += TimeSpan.FromTicks((long)Math.Round(elapsed.Ticks * rate));
            }
        }

        if (duration is TimeSpan total && position > total)
        {
            position = total;
        }

        return position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    /// <summary>
    /// Whether publishing the newly interpolated timeline is worth a <see cref="MediaSessionService.Changed"/>
    /// raise: the duration, seek capability or playback state changed, or the shown position moved at least
    /// <paramref name="minPositionDeltaMs"/> (a seek backwards counts as a move too).
    /// </summary>
    /// <remarks>
    /// <c>publishedIsPlaying</c>/<c>nextIsPlaying</c> are part of the predicate so the panel poll also
    /// reconciles a playback state that changed without a clean SMTC event (for example the optimistic
    /// play/pause flip, whose value is corrected once the grace window has passed).
    /// </remarks>
    internal static bool ShouldRaiseTimeline(
        TimeSpan? publishedPosition,
        TimeSpan? publishedDuration,
        bool publishedCanSeek,
        bool publishedIsPlaying,
        TimeSpan? nextPosition,
        TimeSpan? nextDuration,
        bool nextCanSeek,
        bool nextIsPlaying,
        double minPositionDeltaMs = RaisePositionDeltaMs)
    {
        if (publishedDuration != nextDuration
            || publishedCanSeek != nextCanSeek
            || publishedIsPlaying != nextIsPlaying)
        {
            return true;
        }

        if (publishedPosition is not TimeSpan before)
        {
            return nextPosition is not null;
        }

        if (nextPosition is not TimeSpan after)
        {
            return true;
        }

        return Math.Abs((after - before).TotalMilliseconds) >= Math.Abs(minPositionDeltaMs);
    }
}

/// <summary>
/// Pure guard for the asynchronous metadata reload race: a metadata read or artwork decode completion
/// may only publish while it belongs to the newest reload and its session is still the selected one.
/// </summary>
internal static class MediaReloadGuard
{
    /// <summary>True when an issued generation is still the latest and its session is still selected.</summary>
    internal static bool IsCurrent(
        int issuedGeneration,
        int latestGeneration,
        string? issuedSessionId,
        string? currentSessionId)
        => issuedGeneration == latestGeneration
           && issuedSessionId is not null
           && string.Equals(issuedSessionId, currentSessionId, StringComparison.Ordinal);
}
