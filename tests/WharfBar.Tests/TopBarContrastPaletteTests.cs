using System.Windows.Media;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TopBarContrastPaletteTests
{
    [Fact]
    public void DarkTextPalette_HasBlackTextOnLightWallpaper()
    {
        // RED: the TopBarContrastPalette class does not exist yet — a compile error is expected.
        var map = TopBarContrastPalette
            .For(BarTextTone.DarkText)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var text = (SolidColorBrush)map[TopBarContrastPalette.TextKey];
        Assert.True(text.Color.R < 51, $"koyu metin beklenir: {text.Color}");
    }

    [Fact]
    public void LightTextPalette_HasWhiteTextOnDarkWallpaper()
    {
        var map = TopBarContrastPalette
            .For(BarTextTone.LightText)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        var text = (SolidColorBrush)map[TopBarContrastPalette.TextKey];
        Assert.True(text.Color.R > 204, $"açık metin beklenir: {text.Color}");
    }

    [Fact]
    public void ThemePalette_MapsThemeBrushesToBarKeys()
    {
        var themeBrush = new SolidColorBrush(Colors.Red);
        var pillBrush = new SolidColorBrush(Colors.Blue);
        var strokeBrush = new SolidColorBrush(Colors.Green);
        var hoverBrush = new SolidColorBrush(Colors.Orange);

        var map = TopBarContrastPalette
            .Theme(key => key switch
            {
                "TextFillColorPrimaryBrush" => themeBrush,
                "ControlFillColorDefaultBrush" => pillBrush,
                "ControlFillColorSecondaryBrush" => hoverBrush,
                "ControlStrokeColorDefaultBrush" => strokeBrush,
                "SubtleFillColorSecondaryBrush" => hoverBrush,
                _ => throw new InvalidOperationException(key),
            })
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        Assert.Same(themeBrush, map[TopBarContrastPalette.TextKey]);
        Assert.Same(pillBrush, map[TopBarContrastPalette.PillFillKey]);
        Assert.Same(hoverBrush, map[TopBarContrastPalette.PillFillHoverKey]);
        Assert.Same(strokeBrush, map[TopBarContrastPalette.PillStrokeKey]);
        Assert.Same(hoverBrush, map[TopBarContrastPalette.SubtleFillKey]);

        // Mica/Tint contract: the hover touch that was hardcoded in the old XAML is preserved (#1EFFFFFF).
        var hit = (SolidColorBrush)map[TopBarContrastPalette.HitFillKey];
        Assert.Equal(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF), hit.Color);
    }

    [Fact]
    public void TextKeyIsExposedForTests()
    {
        Assert.False(string.IsNullOrEmpty(TopBarContrastPalette.TextKey));
    }
}
