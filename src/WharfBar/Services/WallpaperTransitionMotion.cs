namespace WharfBar.Services;

/// <summary>
/// Pure timing and visual budget for wallpaper overlays (unit-tested).
/// </summary>
internal static class WallpaperTransitionMotion
{
    /// <summary>Plain fade duration (opacity + slight zoom).</summary>
    internal const int FadeMs = 1000;

    /// <summary>Blur+fade duration (longer so the blur reads clearly).</summary>
    internal const int BlurFadeMs = 1250;

    /// <summary>Blur radius at the end of blurFade (starts near zero).</summary>
    internal const double BlurRadiusEnd = 28;

    /// <summary>Peak black veil opacity during blurFade (helps separate modes).</summary>
    internal const double BlurDimPeak = 0.18;

    /// <summary>End scale for plain fade (subtle Ken-Burns out).</summary>
    internal const double FadeScaleEnd = 1.06;

    /// <summary>Decode width as a fraction of the monitor width (blur needs detail).</summary>
    internal const double DecodeWidthFraction = 0.75;

    private const int DecodeMinPx = 640;
    private const int DecodeMaxPx = 2560;

    internal static int DurationMs(bool useBlur) => useBlur ? BlurFadeMs : FadeMs;

    internal static int DecodePixelWidth(int monitorWidthPx)
    {
        int scaled = (int)Math.Round(monitorWidthPx * DecodeWidthFraction);
        return Math.Clamp(scaled, DecodeMinPx, DecodeMaxPx);
    }
}
