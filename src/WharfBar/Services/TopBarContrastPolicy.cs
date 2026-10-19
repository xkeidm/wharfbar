using WharfBar.Models;

namespace WharfBar.Services;

/// <summary>Foregrould tone the top bar paints with.</summary>
internal enum BarTextTone
{
    /// <summary>Mirrors the active theme (Mica/Tint behaviour before this palette).</summary>
    FollowTheme,

    /// <summary>Light (near-white) text for a dark wallpaper strip — transparent mode.</summary>
    LightText,

    /// <summary>Dark (near-black) text for a light wallpaper strip — transparent mode.</summary>
    DarkText,
}

/// <summary>
/// Automatic text contrast for the fully transparent top bar. The wallpaper strip behind the
/// bar is measured; light strips get dark text, dark strips keep light text. Fully automatic —
/// there is no user setting; non-transparent modes always follow the theme. Primary monitor
/// only (SPI wallpaper is the primary-monitor wallpaper).
/// </summary>
internal static class TopBarContrastPolicy
{
    /// <summary>
    /// sRGB luma above which the strip is treated as light wallpaper. Mid-gray sizes in around
    /// 0.44; white-ish wallpaper areas land well above, dark photos far below.
    /// </summary>
    internal const double LightWallpaperThreshold = 0.55;

    /// <summary>Auto tone is only correct with a fully see-through bar on the primary monitor.</summary>
    internal static bool IsWallpaperDriven(string? backgroundMode, bool isPrimaryMonitor)
        => isPrimaryMonitor
           && string.Equals(backgroundMode, TopBarBackgroundModes.Transparent, StringComparison.OrdinalIgnoreCase);

    /// <summary>&gt;= <see cref="LightWallpaperThreshold"/> → dark text, else light text.</summary>
    internal static BarTextTone Decide(double stripLuma)
        => stripLuma >= LightWallpaperThreshold ? BarTextTone.DarkText : BarTextTone.LightText;
}
