using System.Windows.Media;

namespace WharfBar.Services;

/// <summary>
/// Brushes the top bar surface paints with. Mica/Tint mirror the theme brushes (Theme) so the
/// bar looks like it did before this palette; transparent mode follows the wallpaper tone (For)
/// so text stays readable over any wallpaper — that palette is fully automatic.
///
/// These keys live in <c>TopBarWindow.Resources</c> and are referenced by the bar's own styles
/// only: flyout popups resolve resources through the shared WPF-UI theme dictionaries, so they
/// keep the theme look while the bar follows the wallpaper.
/// </summary>
internal static class TopBarContrastPalette
{
    /// <summary>Bar text, glyphs and the logo geometry fill.</summary>
    internal const string TextKey = "Bar.TextPrimary";

    /// <summary>Status strip hover plate: a translucent wash of the text tone (was a fixed <c>#1EFFFFFF</c>).</summary>
    internal const string HitFillKey = "Bar.HitFill";

    /// <summary>Island pill resting background (was <c>ControlFillColorDefaultBrush</c>).</summary>
    internal const string PillFillKey = "Bar.PillFill";

    /// <summary>Island pill hover (was <c>ControlFillColorSecondaryBrush</c>).</summary>
    internal const string PillFillHoverKey = "Bar.PillFillHover";

    /// <summary>Island pill stroke (was <c>ControlStrokeColorDefaultBrush</c>).</summary>
    internal const string PillStrokeKey = "Bar.PillStroke";

    /// <summary>Satellite hover (was <c>SubtleFillColorSecondaryBrush</c>).</summary>
    internal const string SubtleFillKey = "Bar.SubtleFill";

    /// <summary>All palette keys, palette order.</summary>
    internal static IReadOnlyList<string> Keys { get; } =
    [
        TextKey,
        HitFillKey,
        PillFillKey,
        PillFillHoverKey,
        PillStrokeKey,
        SubtleFillKey,
    ];

    /// <summary>
    /// Theme-following palette: each bar key mirrors the theme brush the bar's styles used
    /// before this palette. <paramref name="lightTheme"/> swaps the hover plate to a dark wash:
    /// the historical fixed <c>#1EFFFFFF</c> is nearly invisible on the light theme's light
    /// Mica/plate, so the plate follows the text tone exactly like the wallpaper-driven palettes
    /// (the default keeps the old dark-theme value for a dark theme).
    /// </summary>
    internal static IReadOnlyList<KeyValuePair<string, Brush>> Theme(Func<string, Brush> find, bool lightTheme = false) => new KeyValuePair<string, Brush>[]
    {
        new(TextKey, find("TextFillColorPrimaryBrush")),
        new(HitFillKey, lightTheme ? Frozen(0x14, 0x00, 0x00, 0x00) : Frozen(0x1E, 0xFF, 0xFF, 0xFF)),
        new(PillFillKey, find("ControlFillColorDefaultBrush")),
        new(PillFillHoverKey, find("ControlFillColorSecondaryBrush")),
        new(PillStrokeKey, find("ControlStrokeColorDefaultBrush")),
        new(SubtleFillKey, find("SubtleFillColorSecondaryBrush")),
    };

    /// <summary>Wallpaper-driven palette: a coherent transparent-bar set per text tone.</summary>
    internal static IReadOnlyList<KeyValuePair<string, Brush>> For(BarTextTone tone) => tone switch
    {
        // Light wallpaper → dark text on white-ish plates.
        BarTextTone.DarkText => new KeyValuePair<string, Brush>[]
        {
            new(TextKey, Frozen(0xE6, 0x14, 0x14, 0x14)),
            new(HitFillKey, Frozen(0x14, 0x00, 0x00, 0x00)),
            new(PillFillKey, Frozen(0xB3, 0xFF, 0xFF, 0xFF)),
            new(PillFillHoverKey, Frozen(0x99, 0xFF, 0xFF, 0xFF)),
            new(PillStrokeKey, Frozen(0x1A, 0x00, 0x00, 0x00)),
            new(SubtleFillKey, Frozen(0x14, 0x00, 0x00, 0x00)),
        },

        // Dark wallpaper → light text on translucent white plates.
        BarTextTone.LightText => new KeyValuePair<string, Brush>[]
        {
            new(TextKey, Frozen(0xE6, 0xFF, 0xFF, 0xFF)),
            new(HitFillKey, Frozen(0x1E, 0xFF, 0xFF, 0xFF)),
            new(PillFillKey, Frozen(0x0F, 0xFF, 0xFF, 0xFF)),
            new(PillFillHoverKey, Frozen(0x24, 0xFF, 0xFF, 0xFF)),
            new(PillStrokeKey, Frozen(0x1A, 0xFF, 0xFF, 0xFF)),
            new(SubtleFillKey, Frozen(0x14, 0xFF, 0xFF, 0xFF)),
        },

        _ => throw new ArgumentOutOfRangeException(nameof(tone), tone, null),
    };

    private static Brush Frozen(byte alpha, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        brush.Freeze();
        return brush;
    }
}
