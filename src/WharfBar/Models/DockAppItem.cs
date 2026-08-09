namespace WharfBar.Models;

/// <summary>Unified dock strip entry (pinned and/or running).</summary>
internal sealed record DockAppItem(
    string Id,
    string Path,
    string Args,
    string Label,
    bool IsPinned,
    bool IsRunning,
    IntPtr MainWindowHandle);
