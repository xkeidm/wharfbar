using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class RunningAppsSourceTests
{
    private const string Notepad = @"C:\Windows\System32\notepad.exe";

    [Fact]
    public void FromSnapshots_SameExeWithAndWithoutAumid_IsOneApp()
    {
        const string aumid = "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App";
        IReadOnlyList<RunningAppInfo> apps = RunningAppsSource.FromSnapshots(
        [
            new RunningWindowSnapshot((IntPtr)7, "Not Defteri", 10, Notepad, aumid),
            new RunningWindowSnapshot((IntPtr)8, "Arka plan", 11, Notepad, null),
        ]);

        RunningAppInfo app = Assert.Single(apps);
        Assert.Equal(aumid, app.Id);
        Assert.Equal(2, app.WindowCount);
        Assert.Equal([10u, 11u], app.ProcessIds);
        Assert.Contains(Notepad, app.MatchPaths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromSnapshots_SameProcessWithoutAPath_CollapsesOntoTheMainWindow()
    {
        IReadOnlyList<RunningAppInfo> apps = RunningAppsSource.FromSnapshots(
        [
            new RunningWindowSnapshot((IntPtr)7, "Not Defteri", 10, Notepad, "Contoso.App"),
            new RunningWindowSnapshot((IntPtr)8, "Helper", 10, string.Empty, null),
        ]);

        RunningAppInfo app = Assert.Single(apps);
        Assert.Equal(2, app.WindowCount);
        Assert.Equal([10u], app.ProcessIds);
    }

    [Fact]
    public void FromSnapshots_DifferentExecutables_StayDistinct()
    {
        IReadOnlyList<RunningAppInfo> apps = RunningAppsSource.FromSnapshots(
        [
            new RunningWindowSnapshot((IntPtr)1, "A", 1, @"C:\A\tool.exe", null),
            new RunningWindowSnapshot((IntPtr)2, "B", 2, @"C:\B\tool.exe", null),
        ]);

        Assert.Equal(2, apps.Count);
    }

    [Fact]
    public void FromSnapshots_SameAppUserModelIdDifferentPaths_IsOneApp()
    {
        const string aumid = "Contoso.App";
        IReadOnlyList<RunningAppInfo> apps = RunningAppsSource.FromSnapshots(
        [
            new RunningWindowSnapshot((IntPtr)1, "App", 1, @"C:\App\app.exe", aumid),
            new RunningWindowSnapshot((IntPtr)2, "Host", 2, @"C:\Windows\System32\ApplicationFrameHost.exe", aumid),
        ]);

        RunningAppInfo app = Assert.Single(apps);
        Assert.Equal(2, app.WindowCount);
        Assert.Equal(aumid, app.Id);
    }

    [Fact]
    public void FromSnapshots_ExtendedPathAndWin32Path_AreTheSameExe()
    {
        IReadOnlyList<RunningAppInfo> apps = RunningAppsSource.FromSnapshots(
        [
            new RunningWindowSnapshot((IntPtr)7, "Not Defteri", 10, Notepad, null),
            new RunningWindowSnapshot((IntPtr)8, "Not Defteri", 11, @"\\?\C:\Windows\System32\notepad.exe", null),
        ]);

        Assert.Single(apps);
        Assert.Equal(2, apps[0].WindowCount);
    }

    [Fact]
    public void FromSnapshots_SkipsWindowsUnderSystemApps()
    {
        string system = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SystemApps",
            "Microsoft.Windows.Something",
            "App.exe");
        IReadOnlyList<RunningAppInfo> apps = RunningAppsSource.FromSnapshots(
        [
            new RunningWindowSnapshot((IntPtr)1, "Shell", 4, system, null),
            new RunningWindowSnapshot((IntPtr)7, "Not Defteri", 10, Notepad, null),
        ]);

        RunningAppInfo app = Assert.Single(apps);
        Assert.Equal(Notepad, app.Path);
    }
}
