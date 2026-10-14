using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class TrayToolbarTests
{
    [Fact]
    public void ButtonBytes_MatchesNativeTbbutton()
    {
        int expected = Environment.Is64BitProcess ? 32 : 20;
        Assert.Equal(expected, TrayToolbar.ButtonBytes);
    }
}
