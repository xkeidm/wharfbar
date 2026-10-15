using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class SystemAccentPackTests
{
    [Fact]
    public void ToAccentDword_PacksBgrWithAlphaFf()
    {
        // R=0x11 G=0x22 B=0x33 → 0xFF332211
        Assert.Equal(0xFF332211u, SystemAccentPack.ToAccentDword(new IslandRgb(0x11, 0x22, 0x33)));
    }

    [Fact]
    public void BuildAccentPalette_Is32Bytes_CenterSlotMatches()
    {
        var c = new IslandRgb(10, 20, 30);
        byte[] p = SystemAccentPack.BuildAccentPalette(c);
        Assert.Equal(32, p.Length);
        // Slot index 3 (bytes 12..15) is the base accent in common Explorer layouts
        Assert.Equal(30, p[12]); // B
        Assert.Equal(20, p[13]); // G
        Assert.Equal(10, p[14]); // R
        Assert.Equal(0, p[15]);  // A
    }
}
