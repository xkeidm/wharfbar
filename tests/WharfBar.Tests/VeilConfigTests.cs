using WharfBar.Models;
using Xunit;

namespace WharfBar.Tests;

public class VeilConfigTests
{
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(40, 40)]
    [InlineData(100, 100)]
    [InlineData(150, 100)]
    public void ClampOpacity_BoundsValues(int input, int expected)
    {
        Assert.Equal(expected, VeilNormalizer.ClampOpacity(input));
    }

    [Theory]
    [InlineData("#4A90D9", "#4a90d9")]
    [InlineData("#4a90d9", "#4a90d9")]
    [InlineData("#ABC", "#aabbcc")]
    [InlineData("F0F0F0", "#f0f0f0")]
    public void NormalizeColor_ValidHex_NormalizesToLowercaseRgb(string input, string expected)
    {
        Assert.Equal(expected, VeilNormalizer.NormalizeColor(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void NormalizeColor_Invalid_FallsBackToDefault(string? input)
    {
        Assert.Equal(VeilConfig.DefaultColor, VeilNormalizer.NormalizeColor(input));
    }
}
