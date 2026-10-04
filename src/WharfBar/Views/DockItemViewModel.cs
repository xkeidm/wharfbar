using System;
using System.Windows.Media;
using WharfBar.Models;

namespace WharfBar.Views;

/// <summary>
/// Render-ready snapshot of one strip slot: a dock item or a section separator.
/// Internal because every consumer lives in this assembly; <see cref="Item"/> stays internal
/// too, so the internal <see cref="DockAppItem"/> never leaks past the assembly boundary.
/// </summary>
internal sealed class DockItemViewModel
{
    private DockItemViewModel(DockAppItem? item, ImageSource icon, bool isSeparator)
    {
        Item = item;
        Icon = icon;
        IsSeparator = isSeparator;
    }

    internal DockAppItem? Item { get; }

    internal ImageSource Icon { get; }

    internal bool IsSeparator { get; }

    internal string Tooltip => Item?.Tooltip ?? string.Empty;

    internal bool IsRunning => Item?.IsRunning == true;

    internal static DockItemViewModel ForItem(DockAppItem item, ImageSource icon)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DockItemViewModel(item, icon, isSeparator: false);
    }

    internal static DockItemViewModel ForSeparator() => new(null, null!, isSeparator: true);
}
