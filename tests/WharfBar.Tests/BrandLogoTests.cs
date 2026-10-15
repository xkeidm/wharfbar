using System.Windows;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class BrandLogoTests
{
    private static readonly BrandColors Dark = BrandPalette.For(Color.FromRgb(0x00, 0x78, 0xD4), isDark: true);

    [Fact]
    public void BarGlyph_FollowsStyle_AndStaysOnTheGrid()
    {
        Geometry wharf = BrandLogo.BarGlyph(LogoStyles.WharfBar);
        Geometry windows = BrandLogo.BarGlyph(LogoStyles.Windows);

        Assert.NotSame(wharf, windows);
        Assert.Same(wharf, BrandLogo.BarGlyph("unknown"));
        Assert.True(wharf.IsFrozen);
        Assert.True(windows.IsFrozen);
        Assert.True(new Rect(0, 0, BrandLogo.Grid, BrandLogo.Grid).Contains(wharf.Bounds));
        Assert.True(new Rect(0, 0, BrandLogo.Grid, BrandLogo.Grid).Contains(windows.Bounds));
    }

    [Fact]
    public void DockTile_IsCachedPerStyleAndPalette_NotSize()
    {
        RunSta(() =>
        {
            DrawingImage wharf = BrandLogo.DockTile(LogoStyles.WharfBar, Dark, 48);
            DrawingImage sameAt24 = BrandLogo.DockTile(LogoStyles.WharfBar, Dark, 24);
            DrawingImage windows = BrandLogo.DockTile(LogoStyles.Windows, Dark, 48);

            Assert.Same(wharf, sameAt24);
            Assert.Same(wharf, BrandLogo.DockTile(LogoStyles.WharfBar, Dark, 64));
            Assert.Same(windows, BrandLogo.DockTile(LogoStyles.Windows, Dark, 24));
            Assert.NotSame(wharf, windows);
            Assert.True(wharf.IsFrozen && windows.IsFrozen);

            BrandColors light = BrandPalette.For(Color.FromRgb(0x00, 0x78, 0xD4), isDark: false);
            Assert.NotSame(wharf, BrandLogo.DockTile(LogoStyles.WharfBar, light, 48));
        });
    }

    [Fact]
    public void DockTile_WharfBarIsSolidMark_ThreeDrawings()
    {
        RunSta(() =>
        {
            var wharf = (DrawingGroup)BrandLogo.DockTile(LogoStyles.WharfBar, Dark, 48).Drawing;
            var windows = (DrawingGroup)BrandLogo.DockTile(LogoStyles.Windows, Dark, 48).Drawing;

            // Tile bg + sun + solid pier (no sea-line pen drawing).
            Assert.Equal(3, wharf.Children.Count);
            Assert.Equal(2, windows.Children.Count);
            Assert.Equal(Dark.Background, ((SolidColorBrush)((GeometryDrawing)wharf.Children[0]).Brush).Color);
            Assert.Equal(Dark.Sun, ((SolidColorBrush)((GeometryDrawing)wharf.Children[1]).Brush).Color);
            Assert.Equal(Dark.Pier, ((SolidColorBrush)((GeometryDrawing)wharf.Children[2]).Brush).Color);
            Assert.Equal(Dark.Pier, ((SolidColorBrush)((GeometryDrawing)windows.Children[1]).Brush).Color);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (error is not null)
        {
            throw error;
        }
    }
}
