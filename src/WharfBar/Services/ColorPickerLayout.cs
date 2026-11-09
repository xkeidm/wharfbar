using System.Windows;

namespace WharfBar.Services;

/// <summary>
/// Pure placement math for the screen color picker's two affordances: the cursor-centred loupe and
/// the HEX/hint card that normally sits below it. When the bottom of the monitor leaves no room for
/// the card, the card flips <b>above</b> the loupe (y-axis flip) instead of dragging the loupe away
/// from the cursor; both rectangles are clamped fully inside the viewport. No WPF layout, no screen
/// access — fully unit-testable.
/// </summary>
internal static class ColorPickerLayout
{
    /// <summary>Gap (DIP) between the loupe and the info card.</summary>
    internal const double DefaultGap = 12;

    /// <summary>Minimum distance (DIP) kept between an affordance and the monitor edge.</summary>
    internal const double DefaultMargin = 8;

    /// <summary>Loupe square (its centre is the cursor) plus the info card rectangle and flip state.</summary>
    internal readonly record struct Placement(Rect Loupe, Rect Info, bool InfoAbove);

    /// <summary>
    /// Places the loupe centred on (<paramref name="cursorX"/>, <paramref name="cursorY"/>) and the
    /// info card below it. If the card would run past the bottom margin it is put above the loupe
    /// (<see cref="Placement.InfoAbove"/>); if the viewport is too small for either side the card is
    /// clamped inside the viewport so it stays visible.
    /// </summary>
    internal static Placement Place(
        double cursorX,
        double cursorY,
        double viewportWidth,
        double viewportHeight,
        double loupeSize,
        double infoWidth,
        double infoHeight,
        double gap = DefaultGap,
        double margin = DefaultMargin)
    {
        double width = Math.Max(1, viewportWidth);
        double height = Math.Max(1, viewportHeight);

        // Loupe: centred on the cursor, pushed inside by the margin only at the monitor edges.
        double loupeLeft = Math.Clamp(
            cursorX - (loupeSize / 2), margin, Math.Max(margin, width - margin - loupeSize));
        double loupeTop = Math.Clamp(
            cursorY - (loupeSize / 2), margin, Math.Max(margin, height - margin - loupeSize));
        var loupe = new Rect(loupeLeft, loupeTop, loupeSize, loupeSize);

        // Info card: centred on the cursor horizontally, clamped only at the side edges.
        double infoLeft = Math.Clamp(
            cursorX - (infoWidth / 2), margin, Math.Max(margin, width - margin - infoWidth));

        double belowTop = loupe.Bottom + gap;
        double aboveTop = loupe.Top - gap - infoHeight;
        bool fitsBelow = belowTop + infoHeight <= height - margin;
        bool fitsAbove = aboveTop >= margin;

        bool infoAbove = !fitsBelow && fitsAbove;
        double infoTop;
        if (infoAbove)
        {
            infoTop = aboveTop;
        }
        else if (fitsBelow)
        {
            infoTop = belowTop;
        }
        else
        {
            // Tiny viewport (e.g. a very short screen): keep the card on screen rather than off it.
            infoTop = Math.Clamp(belowTop, margin, Math.Max(margin, height - margin - infoHeight));
        }

        return new Placement(loupe, new Rect(infoLeft, infoTop, infoWidth, infoHeight), infoAbove);
    }
}
