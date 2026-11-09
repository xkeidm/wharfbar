using System.Windows.Media;

namespace WharfBar.Models;

/// <summary>
/// Snapshot of the playing media (SMTC session). Pure data record — unit-tested.
/// </summary>
/// <remarks>
/// <see cref="Position"/> is the locally interpolated value published by
/// <see cref="Services.MediaSessionService.RefreshTimeline"/> (SMTC's raw timeline position is frozen
/// between app reports); <see cref="Duration"/> is the session's <c>EndTime - StartTime</c>.
/// </remarks>
internal sealed record MediaSnapshot(
    string SessionId,
    string AppName,
    string Title,
    string Artist,
    bool IsPlaying,
    ImageSource? Thumbnail,
    TimeSpan? Position = null,
    TimeSpan? Duration = null,
    bool CanSeek = false)
{
    /// <summary>Empty snapshot used when there is no session.</summary>
    internal static MediaSnapshot Idle { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, false, null);

    /// <summary>Whether there is an active SMTC session (id is not empty).</summary>
    internal bool HasSession => SessionId.Length > 0;

    /// <summary>
    /// <c>m:ss / m:ss</c> when both SMTC position and duration exist (<c>h:mm:ss</c> when there are hours).
    /// Null if either is missing or the duration is 0 — no percentage is invented.
    /// </summary>
    internal string? TimelineText()
    {
        if (Position is not TimeSpan position || Duration is not TimeSpan duration || duration <= TimeSpan.Zero)
        {
            return null;
        }

        return $"{FormatClock(position)} / {FormatClock(duration)}";
    }

    private static string FormatClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
    }
}
