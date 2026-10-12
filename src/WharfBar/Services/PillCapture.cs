namespace WharfBar.Services;

/// <summary>When the last clipboard preview must leave a screenshot without the rest of the bar.</summary>
internal static class PillCapture
{
    internal static bool ShouldPark(IslandPillFace face, bool pillShown, string? title, bool lastClipIdle)
        => pillShown
            && face == IslandPillFace.Idle
            && !string.IsNullOrWhiteSpace(title)
            && lastClipIdle;
}
