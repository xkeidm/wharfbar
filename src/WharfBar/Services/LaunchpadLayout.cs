namespace WharfBar.Services;

/// <summary>
/// Sizes the launchpad card against the primary screen in DIPs. The card keeps its
/// preferred 680x740 but shrinks on small screens, and the phone panel is only shown
/// when the card plus the panel still fit inside the screen with a comfortable gutter.
/// </summary>
internal static class LaunchpadLayout
{
    /// <summary>Preferred card width; matches the centered-box design.</summary>
    internal const double DefaultCardWidth = 680;

    /// <summary>Preferred card height; the scroll body gets what is left after chrome.</summary>
    internal const double DefaultMaxCardHeight = 740;

    /// <summary>Total horizontal breathing room kept between the card and the screen edges.</summary>
    internal const double CardGutterX = 80;

    /// <summary>Total vertical breathing room kept between the card and the screen edges.</summary>
    internal const double CardGutterY = 96;

    internal const double MinCardWidth = 520;

    internal const double MinCardHeight = 360;

    /// <summary>Vertical card chrome: padding (34) + search row (44) + footer (50).</summary>
    internal const double CardChrome = 128;

    internal const double PhonePanelWidth = 268;

    internal const double PhoneGap = 12;

    /// <summary>Gutter around the card + phone panel pair (card margin, panel margin, edges).</summary>
    internal const double PhoneGutter = 64;

    internal static double CardWidth(double surfaceWidth) =>
        Math.Clamp(surfaceWidth - CardGutterX, MinCardWidth, DefaultCardWidth);

    internal static double CardHeight(double surfaceHeight) =>
        Math.Clamp(surfaceHeight - CardGutterY, MinCardHeight, DefaultMaxCardHeight);

    /// <summary>True when the card and the phone panel fit side by side on this screen.</summary>
    internal static bool PhoneFits(double surfaceWidth, double cardWidth) =>
        cardWidth + PhoneGap + PhonePanelWidth + PhoneGutter <= surfaceWidth;

    /// <summary>The scrolling body height inside a card of the given height.</summary>
    internal static double ScrollHeight(double cardHeight) =>
        Math.Max(160, cardHeight - CardChrome);
}
