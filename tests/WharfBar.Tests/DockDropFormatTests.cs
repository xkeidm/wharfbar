using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockDropFormatTests
{
    [Theory]
    [InlineData(@"C:\apps\thing.exe", true)]
    [InlineData(@"C:\apps\thing.EXE", true)]
    [InlineData(@"C:\apps\thing.lnk", true)]
    [InlineData(@"C:\apps\thing.bat", true)]
    [InlineData(@"C:\apps\thing.cmd", true)]
    [InlineData(@"C:\apps\notes.txt", false)]
    [InlineData(@"C:\apps\folder", false)]
    [InlineData("", false)]
    public void IsSupported_FollowsTheExtensionAllowList(string path, bool expected)
    {
        Assert.Equal(expected, DockDropFormats.IsSupported(path));
    }

    [Fact]
    public void Resolve_WhenExtensionIsUnsupported_ReturnsEmpty()
    {
        (string path, string args) = DockDropFormats.Resolve(@"C:\apps\notes.txt");
        Assert.Equal(string.Empty, path);
        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void Resolve_WhenPlainExecutable_ReturnsPathUnchanged()
    {
        (string path, string args) = DockDropFormats.Resolve(@"C:\apps\thing.exe");
        Assert.Equal(@"C:\apps\thing.exe", path);
        Assert.Equal(string.Empty, args);
    }

    [Fact]
    public void Resolve_WhenShortcutIsUnreadable_KeepsShortcutPath()
    {
        // No such file on disk: the resolver cannot read it, so the .lnk path is used and
        // the store derives a label from the file name.
        (string path, _) = DockDropFormats.Resolve(@"C:\nope\missing.lnk");
        Assert.Equal(@"C:\nope\missing.lnk", path);
    }
}