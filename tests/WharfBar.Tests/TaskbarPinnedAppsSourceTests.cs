using System.Text;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TaskbarPinnedAppsSourceTests
{
    [Fact]
    public void OrderLinks_FollowsFavoritesBlobThenFileName()
    {
        byte[] favorites = Encoding.Unicode.GetBytes("xxCalc.lnk----Steam.lnk");
        string[] links =
        [
            @"C:\pins\Steam.lnk",
            @"C:\pins\Notepad.lnk",
            @"C:\pins\Calc.lnk",
        ];

        IReadOnlyList<string> ordered = TaskbarPinnedAppsSource.OrderLinks(links, favorites);

        Assert.Equal(
            ["Calc.lnk", "Steam.lnk", "Notepad.lnk"],
            ordered.Select(static path => System.IO.Path.GetFileName(path)));
    }

    [Fact]
    public void OrderLinks_WhenFavoritesIsEmpty_SortsByFileName()
    {
        string[] links =
        [
            @"C:\pins\Steam.lnk",
            @"C:\pins\Calc.lnk",
        ];

        IReadOnlyList<string> ordered = TaskbarPinnedAppsSource.OrderLinks(links, ReadOnlySpan<byte>.Empty);

        Assert.Equal(
            ["Calc.lnk", "Steam.lnk"],
            ordered.Select(static path => System.IO.Path.GetFileName(path)));
    }

    [Fact]
    public void PinFromShortcut_Unresolved_KeepsTheShortcutSoThePinStillShows()
    {
        var pin = TaskbarPinnedAppsSource.PinFromShortcut(
            @"C:\pins\Store.lnk",
            new ShortcutLaunch(false, string.Empty, string.Empty, string.Empty));

        Assert.Equal(@"C:\pins\Store.lnk", pin.Path);
        Assert.Equal("Store", pin.Label);
        Assert.Equal(string.Empty, pin.Args);
    }

    [Fact]
    public void PinFromShortcut_DirectoryTarget_StaysTheFolder()
    {
        var pin = TaskbarPinnedAppsSource.PinFromShortcut(
            @"C:\pins\Windows.lnk",
            new ShortcutLaunch(true, @"C:\Windows", string.Empty, "Windows"));

        Assert.Equal(@"C:\Windows", pin.Path.TrimEnd('\\'));
        Assert.Equal("Windows", pin.Label);
    }

    [Fact]
    public void PinFromShortcut_ExplorerWithFolderArgument_DoesNotBecomeExplorer()
    {
        var pin = TaskbarPinnedAppsSource.PinFromShortcut(
            @"C:\pins\Belgeler.lnk",
            new ShortcutLaunch(true, @"C:\Windows\explorer.exe", @"C:\Windows", "Belgeler"));

        Assert.Equal(@"C:\Windows", pin.Path.TrimEnd('\\'));
        Assert.Equal("Belgeler", pin.Label);
    }

    [Fact]
    public void PinFromShortcut_ExecutableTarget_KeepsPathAndArgs()
    {
        var pin = TaskbarPinnedAppsSource.PinFromShortcut(
            @"C:\pins\Notepad.lnk",
            new ShortcutLaunch(true, @"C:\Windows\System32\notepad.exe", "/A", "Not Defteri"));

        Assert.EndsWith("notepad.exe", pin.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/A", pin.Args);
        Assert.Equal("Not Defteri", pin.Label);
    }
}
