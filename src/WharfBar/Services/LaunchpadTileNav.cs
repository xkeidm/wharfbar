namespace WharfBar.Services;

internal enum LaunchpadMove
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// One navigable grid inside the launchpad body: its first item in the flat navigation
/// order, its item count, and how many items sit in one visual row.
/// </summary>
internal readonly record struct LaunchpadNavSection(int FirstIndex, int Count, int Columns);

/// <summary>
/// Keyboard navigation math for the launchpad tile grids. Kept pure so the arrow-key
/// behavior can be tested without a window; the face maps the returned index back to a
/// button and calls <c>Focus</c>/<c>BringIntoView</c>.
/// </summary>
internal static class LaunchpadTileNav
{
    /// <summary>
    /// Returns the next flat item index for an arrow key, or <c>-1</c> when the move
    /// leaves the tile grids (Up from the first row hands focus back to the search box).
    /// </summary>
    internal static int Move(
        IReadOnlyList<LaunchpadNavSection> sections,
        int index,
        LaunchpadMove move)
    {
        ArgumentNullException.ThrowIfNull(sections);
        if (sections.Count == 0)
        {
            return -1;
        }

        if (index < 0)
        {
            return move is LaunchpadMove.Down or LaunchpadMove.Right ? sections[0].FirstIndex : -1;
        }

        int sectionIndex = SectionOf(sections, index);
        if (sectionIndex < 0)
        {
            return -1;
        }

        LaunchpadNavSection section = sections[sectionIndex];
        int local = index - section.FirstIndex;
        int columns = Math.Max(1, section.Columns);
        int row = local / columns;
        int column = local % columns;
        int rows = Rows(section, columns);

        switch (move)
        {
            case LaunchpadMove.Left:
                return column > 0 ? index - 1 : index;
            case LaunchpadMove.Right:
                return column + 1 < columns && local + 1 < section.Count ? index + 1 : index;
            case LaunchpadMove.Down:
            {
                if (row + 1 < rows)
                {
                    int target = local + columns;
                    return section.FirstIndex + Math.Min(target, section.Count - 1);
                }

                return sectionIndex + 1 < sections.Count
                    ? sections[sectionIndex + 1].FirstIndex
                    : index;
            }

            case LaunchpadMove.Up:
            {
                if (row > 0)
                {
                    return index - columns;
                }

                if (sectionIndex == 0)
                {
                    return -1;
                }

                LaunchpadNavSection previous = sections[sectionIndex - 1];
                int previousColumns = Math.Max(1, previous.Columns);
                int previousRows = Rows(previous, previousColumns);
                int previousRow = previousRows - 1;
                int previousColumn = Math.Min(column, previousColumns - 1);
                int previousLocal = Math.Min(
                    (previousRow * previousColumns) + previousColumn,
                    previous.Count - 1);
                return previous.FirstIndex + previousLocal;
            }

            default:
                return index;
        }
    }

    /// <summary>The section owning a flat index, or <c>-1</c> when the index is outside.</summary>
    internal static int SectionOf(IReadOnlyList<LaunchpadNavSection> sections, int index)
    {
        for (int i = 0; i < sections.Count; i++)
        {
            LaunchpadNavSection section = sections[i];
            if (section.Count > 0 && index >= section.FirstIndex && index < section.FirstIndex + section.Count)
            {
                return i;
            }
        }

        return -1;
    }

    private static int Rows(LaunchpadNavSection section, int columns) =>
        Math.Max(1, (section.Count + columns - 1) / columns);
}
