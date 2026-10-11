using System.Windows.Media.Animation;

namespace WharfBar.Services;

/// <summary>
/// Offsets for one in-panel quick settings page change. Positive X is to the right.
/// A forward push moves the current page left and brings the next page in from the right.
/// </summary>
internal readonly record struct QuickSettingsPageShift(
    bool Animate,
    double OutgoingFromX,
    double OutgoingToX,
    double IncomingFromX,
    double IncomingToX);

/// <summary>
/// Timing and direction for quick-settings page changes. The flyout's own open
/// animation is separate: the first paint after open does not use this shift.
/// </summary>
internal static class QuickSettingsPageMotion
{
    public const int DurationMs = 220;

    /// <summary>Small nudge, clipped by the page host so it stays inside the flyout.</summary>
    public const double SlideDip = 20;

    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(DurationMs);

    public static IEasingFunction CreateEase() =>
        new CubicEase { EasingMode = EasingMode.EaseOut };

    public static QuickSettingsPageShift Shift(QuickSettingsPage from, QuickSettingsPage to, bool flyoutOpen)
    {
        if (!flyoutOpen || from == to)
        {
            return new QuickSettingsPageShift(false, 0, 0, 0, 0);
        }

        int delta = Depth(to) - Depth(from);
        if (delta > 0)
        {
            return new QuickSettingsPageShift(true, 0, -SlideDip, SlideDip, 0);
        }

        if (delta < 0)
        {
            return new QuickSettingsPageShift(true, 0, SlideDip, -SlideDip, 0);
        }

        return new QuickSettingsPageShift(true, 0, 0, 0, 0);
    }

    private static int Depth(QuickSettingsPage page) =>
        page == QuickSettingsPage.Main ? 0 : 1;
}
