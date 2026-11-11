using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WharfBar.Services;

/// <summary>
/// Produces a small "letter tile" glyph used when a tray icon's real bitmap is not available
/// (Windows 25H2 UIA path — the classic toolbar, and therefore real icon extraction, is gone).
/// </summary>
internal static class TrayLetterGlyph
{
    private const double SizeDip = 20;

    /// <summary>First non-whitespace character, uppercased. Empty when no character exists.</summary>
    internal static string FirstLetter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        return char.ToUpperInvariant(name.Trim()[0]).ToString();
    }

    /// <summary>Frozen 20px bitmap with the first letter centered (call on an STA thread).</summary>
    internal static ImageSource Build(string? name)
    {
        string letter = FirstLetter(name);
        var bmp = new RenderTargetBitmap((int)SizeDip, (int)SizeDip, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            if (letter.Length > 0)
            {
                var text = new FormattedText(
                    letter,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        SystemVersion.TextFontFamily,
                        FontStyles.Normal,
                        FontWeights.Normal,
                        FontStretches.Normal),
                    13,
                    // Neutral mid gray: the tile is frozen and cached per key, so it cannot follow a
                    // later light/dark swap — a single tone that reads on both themes (and matches
                    // the generic tray fallback dot) is the only honest option. The old #E0E0E0 was
                    // a dark-theme-only letter that disappeared on the light bar/dock.
                    new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                    VisualTreeHelper.GetDpi(dv).PixelsPerDip);
                dc.DrawText(text, new Point((SizeDip - text.Width) / 2, (SizeDip - text.Height) / 2));
            }
        }

        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }
}
