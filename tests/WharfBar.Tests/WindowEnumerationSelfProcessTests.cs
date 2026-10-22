using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class WindowEnumerationSelfProcessTests
{
    [Fact]
    public void TaskbarWindow_RequiresAppWindowWithoutToolWindow()
    {
        Assert.True(WindowEnumeration.IsTaskbarWindow(hasAppWindow: true, hasToolWindow: false));
        Assert.False(WindowEnumeration.IsTaskbarWindow(hasAppWindow: true, hasToolWindow: true));
        Assert.False(WindowEnumeration.IsTaskbarWindow(hasAppWindow: false, hasToolWindow: false));
        Assert.False(WindowEnumeration.IsTaskbarWindow(hasAppWindow: false, hasToolWindow: true));
    }
}
