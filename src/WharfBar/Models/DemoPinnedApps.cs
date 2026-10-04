namespace WharfBar.Models;

/// <summary>
/// Default pin list, used only to seed <c>dock.items</c> in config.json on first run
/// (<c>ConfigService.CreateDefault</c>). It is not a fallback for anything: since the
/// ADR-005 revision (2026-10-04) the dock never reads the Windows taskbar, so there is no
/// taskbar source that could come back empty and need a stand-in. Seeded once, then owned
/// by the user through the dock's own store.
/// </summary>
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
