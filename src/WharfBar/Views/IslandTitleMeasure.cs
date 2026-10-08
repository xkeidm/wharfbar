using System.Windows;
using System.Windows.Controls;

namespace WharfBar.Views;

/// <summary>
/// <see cref="UIElement.Measure"/> reports a zero <see cref="UIElement.DesiredSize"/>
/// while <see cref="UIElement.Visibility"/> is <see cref="Visibility.Collapsed"/>.
/// The now-playing title starts collapsed, so it has to be measured in a state
/// that still participates in layout.
/// </summary>
internal static class IslandTitleMeasure
{
    internal static double Dip(TextBlock block)
    {
        bool collapsed = block.Visibility == Visibility.Collapsed;
        if (collapsed)
        {
            block.Visibility = Visibility.Hidden;
        }

        block.Measure(new Size(1000, 24));
        double width = block.DesiredSize.Width;

        if (collapsed)
        {
            block.Visibility = Visibility.Collapsed;
        }

        return width;
    }
}
