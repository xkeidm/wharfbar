using System;
using System.Windows.Media;
using WharfBar.Models;

namespace WharfBar.Views;

/// <summary>
/// Render-ready snapshot of one strip slot. Internal because every consumer lives in this
/// assembly; <see cref="Item"/> stays internal too, so the internal <see cref="DockAppItem"/>
/// never leaks past the assembly boundary.
/// </summary>
internal sealed class DockItemViewModel
{
    private DockItemViewModel(DockAppItem item, ImageSource icon)
    {
        Item = item;
        Icon = icon;
    }

    internal DockAppItem Item { get; }

    internal ImageSource Icon { get; }

    internal string Tooltip => Item.Tooltip;

    internal bool IsRunning => Item.IsRunning;

    internal static DockItemViewModel ForItem(DockAppItem item, ImageSource icon)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DockItemViewModel(item, icon);
    }
}
