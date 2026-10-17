using WharfBar.Models;
using WharfBar.Services;
using Wpf.Ui.Appearance;
using Xunit;

namespace WharfBar.Tests;

public class WallpaperThemeSyncPolicyTests
{
    [Theory]
    [InlineData(AppConfig.ThemeSystem, true)]
    [InlineData(AppConfig.ThemeLight, false)]
    [InlineData(AppConfig.ThemeDark, false)]
    public void ShouldFollowExternalWindows_OnlySystem(string mode, bool expected)
        => Assert.Equal(expected, WallpaperThemeSyncPolicy.ShouldFollowExternalWindows(mode));

    [Theory]
    [InlineData(ApplicationTheme.Light, true)]
    [InlineData(ApplicationTheme.Dark, true)]
    [InlineData(ApplicationTheme.HighContrast, false)]
    public void ShouldSyncForResolved_SkipsHighContrast(ApplicationTheme theme, bool expected)
        => Assert.Equal(expected, WallpaperThemeSyncPolicy.ShouldSyncForResolved(theme));
}
