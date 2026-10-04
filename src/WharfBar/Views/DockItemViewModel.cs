using System;
using System.Windows.Media;
using WharfBar.Models;

namespace WharfBar.Views;

/// <summary>
/// Render-ready snapshot of one strip slot: a dock item or a section separator.
/// Public because <see cref="Controls.DockItemCell.Row"/> is a public dependency property;
/// <see cref="Item"/> stays internal so the internal <see cref="DockAppItem"/> never leaks
/// past the assembly boundary.
/// </summary>
public sealed class DockItemViewModel
{
    private DockItemViewModel(DockAppItem? item, ImageSource icon, bool isSeparator)
    {
        Item = item;
        Icon = icon;
        IsSeparator = isSeparator;
    }

    internal DockAppItem? Item { get; }

    public ImageSource Icon { get; }

    public bool IsSeparator { get; }

    public string Id => Item?.Id ?? string.Empty;

    public string Label => Item?.Label ?? string.Empty;

    public string Tooltip => Item?.Tooltip ?? string.Empty;

    public bool IsRunning => Item?.IsRunning == true;

    public bool IsMinimized => Item?.Section == DockSection.Minimized;

    public IntPtr Handle => Item?.MainWindowHandle ?? IntPtr.Zero;

    internal static DockItemViewModel ForItem(DockAppItem item, ImageSource icon)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DockItemViewModel(item, icon, isSeparator: false);
    }

    internal static DockItemViewModel ForSeparator() => new(null, null!, isSeparator: true);
}
