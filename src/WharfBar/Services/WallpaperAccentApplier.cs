using System.Windows;
using System.Windows.Media;
using WharfBar.Native;
using Wpf.Ui.Appearance;

namespace WharfBar.Services;

internal static class WallpaperAccentApplier
{
    internal static bool TryApplyFromPath(string path, bool enabled)
    {
        IslandRgb? color = WallpaperAccent.TryFromPath(path);
        if (!WallpaperAccent.ShouldApply(enabled, color))
        {
            return false;
        }

        IslandRgb c = color!.Value;
        try
        {
            var media = Color.FromRgb(c.R, c.G, c.B);
            ApplicationAccentColorManager.Apply(
                media,
                AppThemePreference.Resolve(),
                systemGlassColor: false,
                systemAccentColor: false);

            if (Application.Current is App app)
            {
                app.ApplyLogoChanges();
            }

            _ = SystemAccentWriter.TryApply(c);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
