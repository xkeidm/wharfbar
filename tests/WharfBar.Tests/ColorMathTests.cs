using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ColorMathTests
{
    [Fact]
    public void ToHex_UppercaseWithHash()
    {
        Assert.Equal("#E8B34C", ColorMath.ToHex(232, 179, 76));
    }

    [Fact]
    public void ToRgb_CommaSeparated()
    {
        Assert.Equal("rgb(232,179,76)", ColorMath.ToRgb(232, 179, 76));
    }
}
