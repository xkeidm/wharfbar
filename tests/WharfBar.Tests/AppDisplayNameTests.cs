using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class AppDisplayNameTests
{
    [Fact]
    public void Choose_PrefersLocalizedShellNameOverTheShortcutFileName()
    {
        string name = AppDisplayName.Choose(
            "Dosya Gezgini",
            "wharfbar - Dosya Gezgini",
            @"C:\pins\File Explorer.lnk");

        Assert.Equal("Dosya Gezgini", name);
    }

    [Fact]
    public void Choose_WhenShellNameIsTheFileName_UsesTheWindowTitleApp()
    {
        string name = AppDisplayName.Choose(
            "explorer.exe",
            "wharfbar - Dosya Gezgini",
            @"C:\Windows\explorer.exe");

        Assert.Equal("Dosya Gezgini", name);
    }

    [Fact]
    public void Choose_TitleWithoutASeparator_StaysWhole()
    {
        Assert.Equal("Zen", AppDisplayName.Choose(null, "Zen", @"C:\apps\Zen.exe"));
    }

    [Fact]
    public void Choose_EmptyTitle_FallsBackToTheFileStem()
    {
        Assert.Equal("notepad", AppDisplayName.Choose(null, "  ", @"C:\Windows\System32\notepad.exe"));
    }

    [Fact]
    public void ApplicationSegment_UsesTheLastDash()
    {
        Assert.Equal("Dosya Gezgini", AppDisplayName.ApplicationSegment("wharfbar - Dosya Gezgini"));
        Assert.Equal("Not Defteri", AppDisplayName.ApplicationSegment("Belge — Not Defteri"));
        Assert.Equal("Başlıksız", AppDisplayName.ApplicationSegment("Başlıksız"));
    }

    [Fact]
    public void IsUsableShellName_RejectsTheBareFileName()
    {
        Assert.False(AppDisplayName.IsUsableShellName("File Explorer", @"C:\pins\File Explorer.lnk"));
        Assert.False(AppDisplayName.IsUsableShellName("explorer.exe", @"C:\Windows\explorer.exe"));
        Assert.True(AppDisplayName.IsUsableShellName("Dosya Gezgini", @"C:\pins\File Explorer.lnk"));
    }
}
