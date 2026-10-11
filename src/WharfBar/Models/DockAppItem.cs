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
    string WindowTitle,
    bool IsOpenOnDesktop = false,
    int WindowCount = 0,
    IReadOnlyList<uint>? ProcessIds = null)
{
    /// <summary>
    /// Tooltip copy: minimized tiles name the window, everything else the app.
    /// Several windows or processes of that app append the count.
    /// </summary>
    internal string Tooltip
    {
        get
        {
            string name = Section != DockSection.Minimized
                || string.IsNullOrEmpty(WindowTitle)
                || WindowTitle == Label
                ? Label
                : $"{Label} — {WindowTitle}";
            return WindowCount > 1 ? $"{name} ({WindowCount})" : name;
        }
    }
}
