using WharfBar.Models;

namespace WharfBar.Services;

internal static class WallpaperTransitionPolicy
{
    internal static bool ShouldAnimate(string? mode, bool respectSystem, bool clientAreaAnimation)
    {
        string m = IslandNormalizer.NormalizeWallpaperTransition(mode);
        if (m == IslandConfig.TransitionOff)
        {
            return false;
        }

        if (respectSystem && !clientAreaAnimation)
        {
            return false;
        }

        return true;
    }

    internal static bool UsesBlur(string? mode)
        => IslandNormalizer.NormalizeWallpaperTransition(mode) == IslandConfig.TransitionBlurFade;
}
