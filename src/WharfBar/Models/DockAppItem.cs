using System;

namespace WharfBar.Models;

/// <summary>Which dock section an item belongs to. Sections render in enum order.</summary>
internal enum DockSection
{
    Pinned,
    Running,
    Minimized,
}

internal sealed record DockAppItem(
    string Id,
    string Path,
    string Args,
    string Label,
    bool IsPinned,
    bool IsRunning,
    IntPtr MainWindowHandle,
    DockSection Section,
    string WindowTitle)
{
    /// <summary>Tooltip copy: minimized tiles name the window, everything else the app.</summary>
    internal string Tooltip =>
        Section != DockSection.Minimized || string.IsNullOrEmpty(WindowTitle) || WindowTitle == Label
            ? Label
            : $"{Label} — {WindowTitle}";
}
