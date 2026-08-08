namespace WharfBar.Models;

internal static class DemoPinnedApps
{
    internal static IReadOnlyList<PinnedApp> All { get; } =
    [
        new(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "", "Explorer"),
        new(System.IO.Path.Combine(Environment.SystemDirectory, "notepad.exe"), "", "Notepad"),
        new(System.IO.Path.Combine(Environment.SystemDirectory, "calc.exe"), "", "Calculator"),
        new(System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"), "", "Command Prompt"),
    ];
}
