using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WharfBar.Models;
using WharfBar.Resources;
using WharfBar.Services;

using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace WharfBar.Views.Pages;

/// <summary>One row in the dock settings list: drag handle, label, path, remove glyph.</summary>
/// <para>
/// Built in code rather than by a <c>DataTemplate</c> so the row can own its remove glyph and
/// let the page decide what a press means (a press on <see cref="RemoveGlyph"/> is a remove,
/// never a reorder drag). The text blocks are plain <see cref="System.Windows.Controls.TextBlock"/>
/// — the ones the page's implicit style targets, so they pick up AppTextFontFamily.
/// </para>
internal sealed class DockSettingsRow : Border
{
    /// <summary>Path column cap, so one deep path cannot stretch the row past the card.</summary>
    private const double PathMaxWidthDip = 260d;

    internal DockSettingsRow(PinnedApp item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Id = DockItemStore.IdFor(item);
        CornerRadius = new CornerRadius(8);
        BorderThickness = new Thickness(1);
        BorderBrush = BrushFor("ControlStrokeColorDefaultBrush");
        Background = BrushFor("ControlFillColorDefaultBrush");
        Padding = new Thickness(12, 8, 12, 8);
        Margin = new Thickness(0, 0, 0, 6);
        AllowDrop = true;
        Cursor = Cursors.Hand;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var handle = new SymbolIcon(SymbolRegular.ReOrderDotsVertical24)
        {
            FontSize = 16,
            Foreground = BrushFor("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Strings.DockRow_DragTooltip,
        };
        Grid.SetColumn(handle, 0);
        grid.Children.Add(handle);

        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var path = new TextBlock
        {
            Text = item.Path,
            FontSize = 11,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = PathMaxWidthDip,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(path, 2);
        grid.Children.Add(path);

        var remove = new SymbolIcon(SymbolRegular.Dismiss24)
        {
            FontSize = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Foreground = BrushFor("TextFillColorSecondaryBrush"),
            ToolTip = Strings.DockRow_RemoveTooltip,
        };
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);

        RemoveGlyph = remove;
        remove.MouseLeftButtonUp += (_, _) => RemoveClicked?.Invoke(this, EventArgs.Empty);
        Child = grid;
    }

    /// <summary>The store key — the normalized path, the only id the store understands.</summary>
    internal string Id { get; }

    /// <summary>
    /// The ✕ glyph. The page hit-tests against it so a press here never starts a reorder drag.
    /// </summary>
    internal FrameworkElement RemoveGlyph { get; }

    internal event EventHandler? RemoveClicked;

    /// <summary>
    /// App-scoped lookup, not <c>FindResource</c>: the row is constructed before it is added to
    /// the page tree, so the page's resource dictionaries are not reachable yet. A missing key
    /// casts to null, which the control reads as "inherit" — same as TopBarPage's cards.
    /// </summary>
    private static Brush BrushFor(string key) =>
        (Brush)Application.Current.TryFindResource(key);
}
