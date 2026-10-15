using System.Windows;
using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

/// <summary>Background removal for captured tray icons (dark + light taskbar fills).</summary>
public class TrayIconMatteTests
{
    private static readonly BgrColor Dark = new(0x20, 0x20, 0x20);
    private static readonly BgrColor Light = new(0xEE, 0xEE, 0xEE);

    private static byte[] Pixels(params BgrColor[] colors)
    {
        var bytes = new byte[colors.Length * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            bytes[i * 4] = colors[i].B;
            bytes[(i * 4) + 1] = colors[i].G;
            bytes[(i * 4) + 2] = colors[i].R;
            bytes[(i * 4) + 3] = 0xFF;
        }

        return bytes;
    }

    [Theory]
    [InlineData(0x20)]
    [InlineData(0xEE)]
    public void BackgroundPixels_BecomeFullyTransparent(byte level)
    {
        var bg = new BgrColor(level, level, level);
        var noisy = new BgrColor((byte)(level - 5), level, (byte)(level - 3));

        byte[] result = TrayIconMatte.RemoveBackground(Pixels(bg, noisy), bg);

        Assert.Equal(0, result[3]);
        Assert.Equal(0, result[7]);
        Assert.False(TrayIconMatte.HasVisiblePixels(result));
    }

    [Fact]
    public void WhiteGlyph_OnDarkBar_StaysOpaqueWhite()
    {
        byte[] result = TrayIconMatte.RemoveBackground(Pixels(new BgrColor(0xFF, 0xFF, 0xFF)), Dark);

        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, result);
    }

    [Fact]
    public void BlackGlyph_OnLightBar_StaysOpaqueBlack()
    {
        byte[] result = TrayIconMatte.RemoveBackground(Pixels(new BgrColor(0, 0, 0)), Light);

        Assert.Equal(new byte[] { 0, 0, 0, 0xFF }, result);
    }

    [Fact]
    public void AntiAliasedEdge_KeepsPartialAlpha_AndRecoversTheGlyphColour()
    {
        // 50 % white over the dark fill: (255 + 32) / 2 ≈ 143.
        byte[] result = TrayIconMatte.RemoveBackground(Pixels(new BgrColor(143, 143, 143)), Dark);

        Assert.InRange(result[3], 110, 145);
        Assert.InRange(result[0], 245, 255);
    }

    [Fact]
    public void SaturatedColourIcon_OnEitherTheme_IsOpaqueAndUnchanged()
    {
        var red = new BgrColor(0x10, 0x20, 0xE0);
        foreach (BgrColor bg in new[] { Dark, Light })
        {
            byte[] result = TrayIconMatte.RemoveBackground(Pixels(red), bg);

            Assert.True(result[3] >= 200, $"alpha {result[3]} on bg {bg}");
            Assert.InRange(result[2], 0xD0, 0xFF);
        }
    }

    [Fact]
    public void MedianColor_IgnoresAMinorityOfOutliers()
    {
        var samples = new List<BgrColor> { Dark, Dark, Dark, new(0xFF, 0xFF, 0xFF), Dark, new(0, 0, 0xFF) };

        Assert.Equal(Dark, TrayIconMatte.MedianColor(samples));
    }

    [Fact]
    public void InflateClamped_StopsAtTheFrameEdges()
    {
        Assert.Equal(new Int32Rect(7, 7, 22, 22), TrayCapture.InflateClamped(new Int32Rect(10, 10, 16, 16), 3, 100, 100));
        Assert.Equal(new Int32Rect(0, 0, 19, 19), TrayCapture.InflateClamped(new Int32Rect(0, 0, 16, 16), 3, 100, 100));
        Assert.Equal(new Int32Rect(87, 87, 13, 13), TrayCapture.InflateClamped(new Int32Rect(90, 90, 16, 16), 3, 100, 100));
    }

    [Fact]
    public void SplitRing_SeparatesTheIconFromItsSurroundings()
    {
        // 4x4 outer, 2x2 inner at (1,1): inner pixels are 0xAA, ring pixels 0x11.
        var outer = new Int32Rect(0, 0, 4, 4);
        var inner = new Int32Rect(1, 1, 2, 2);
        var pixels = new byte[4 * 4 * 4];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                bool inside = x is 1 or 2 && y is 1 or 2;
                byte v = inside ? (byte)0xAA : (byte)0x11;
                int i = ((y * 4) + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
            }
        }

        TrayCapture.SplitRing(pixels, outer, inner, out byte[] innerPixels, out List<BgrColor> ring);

        Assert.Equal(2 * 2 * 4, innerPixels.Length);
        Assert.All(Enumerable.Range(0, 4), p => Assert.Equal(0xAA, innerPixels[p * 4]));
        Assert.Equal(12, ring.Count);
        Assert.All(ring, c => Assert.Equal(new BgrColor(0x11, 0x11, 0x11), c));
    }
}
