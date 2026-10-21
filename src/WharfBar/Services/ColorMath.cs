namespace WharfBar.Services;

/// <summary>
/// Pure color formatting helpers for the island color picker. No screen access, no state —
/// fully unit-testable. Output forms are what the clipboard receives ("#RRGGBB" for most
/// targets, "rgb(r,g,b)" where a CSS value is expected).
/// </summary>
internal static class ColorMath
{
    /// <summary>Uppercase hex with a leading hash, no alpha: <c>#RRGGBB</c>.</summary>
    internal static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>CSS functional notation, no alpha: <c>rgb(r,g,b)</c>.</summary>
    internal static string ToRgb(byte r, byte g, byte b) => $"rgb({r},{g},{b})";
}
