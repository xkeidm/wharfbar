namespace WharfBar.Services;

/// <summary>Taskbar-style progress the dock may paint. <see cref="None"/> means the shell exposed nothing.</summary>
internal enum DockProgressKind
{
    None,
    Normal,
    Indeterminate,
    Paused,
    Error,
}

/// <summary>
/// One progress reading. <see cref="Percent"/> is set only when the source gave a number.
/// A state without a number does not invent one.
/// </summary>
internal readonly record struct DockProgressState(DockProgressKind Kind, double? Percent)
{
    internal static DockProgressState None { get; } = new(DockProgressKind.None, null);

    internal bool IsVisible => Kind != DockProgressKind.None;

    internal static DockProgressState Normal(double percent) =>
        new(DockProgressKind.Normal, Clamp(percent));

    internal static DockProgressState Indeterminate() =>
        new(DockProgressKind.Indeterminate, null);

    internal static DockProgressState Paused(double? percent) =>
        new(DockProgressKind.Paused, percent is null ? null : Clamp(percent.Value));

    internal static DockProgressState Error(double? percent) =>
        new(DockProgressKind.Error, percent is null ? null : Clamp(percent.Value));

    private static double Clamp(double percent) => Math.Clamp(percent, 0d, 100d);
}

/// <summary>Flags a progress element can expose. They are not guessed from a missing pattern.</summary>
[Flags]
internal enum DockProgressFlags
{
    None = 0,
    Indeterminate = 1,
    Paused = 2,
    Error = 4,
}

/// <summary>
/// What UI Automation actually returned for one taskbar button. <see cref="Exposed"/> false
/// is the honest empty case: the hidden taskbar (or a button without a progress pattern)
/// did not yield a value or a named state.
/// </summary>
internal readonly record struct DockProgressSample(
    bool Exposed,
    double? Minimum,
    double? Maximum,
    double? Value,
    DockProgressFlags Flags);

/// <summary>One shell button name paired with the progress it exposed.</summary>
internal readonly record struct DockProgressButton(string Name, DockProgressState State);

/// <summary>A dock tile the progress reader can match to a taskbar button.</summary>
internal readonly record struct DockProgressTile(string Key, string Title, string Label);

/// <summary>Maps an automation sample onto a dock progress state. No sample, no bar.</summary>
internal static class DockProgress
{
    internal static DockProgressState FromSample(DockProgressSample sample)
    {
        if (!sample.Exposed)
        {
            return DockProgressState.None;
        }

        double? percent = PercentOf(sample.Minimum, sample.Maximum, sample.Value);
        if ((sample.Flags & DockProgressFlags.Error) != 0)
        {
            return DockProgressState.Error(percent);
        }

        if ((sample.Flags & DockProgressFlags.Paused) != 0)
        {
            return DockProgressState.Paused(percent);
        }

        if ((sample.Flags & DockProgressFlags.Indeterminate) != 0)
        {
            return DockProgressState.Indeterminate();
        }

        if (percent is null)
        {
            return DockProgressState.None;
        }

        return DockProgressState.Normal(percent.Value);
    }

    /// <summary>
    /// Named states only. A title like "Downloading" is not a percent and not a state.
    /// Error wins when the text names more than one.
    /// </summary>
    internal static DockProgressFlags FlagsFromText(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return DockProgressFlags.None;
        }

        DockProgressFlags flags = DockProgressFlags.None;
        if (status.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            flags |= DockProgressFlags.Error;
        }

        if (status.Contains("paused", StringComparison.OrdinalIgnoreCase))
        {
            flags |= DockProgressFlags.Paused;
        }

        if (status.Contains("indeterminate", StringComparison.OrdinalIgnoreCase))
        {
            flags |= DockProgressFlags.Indeterminate;
        }

        return flags;
    }

    /// <summary>
    /// A range is a percent only when both ends and a value are present and the span is positive.
    /// </summary>
    internal static double? PercentOf(double? minimum, double? maximum, double? value)
    {
        if (minimum is not double min || maximum is not double max || value is not double current)
        {
            return null;
        }

        double span = max - min;
        if (span <= 0d || double.IsNaN(span) || double.IsInfinity(span) || double.IsNaN(current))
        {
            return null;
        }

        return Math.Clamp((current - min) / span * 100d, 0d, 100d);
    }

    /// <summary>
    /// Matches a taskbar button to a tile by its window title or app label. Buttons that
    /// exposed no progress are skipped, so a name hit cannot invent a bar.
    /// </summary>
    internal static IReadOnlyDictionary<string, DockProgressState> Assign(
        IReadOnlyList<DockProgressTile> tiles,
        IReadOnlyList<DockProgressButton> buttons)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        ArgumentNullException.ThrowIfNull(buttons);

        var map = new Dictionary<string, DockProgressState>(StringComparer.OrdinalIgnoreCase);
        foreach (DockProgressTile tile in tiles)
        {
            if (string.IsNullOrEmpty(tile.Key))
            {
                continue;
            }

            foreach (DockProgressButton button in buttons)
            {
                if (!button.State.IsVisible || !NamesMatch(button.Name, tile))
                {
                    continue;
                }

                map[tile.Key] = button.State;
                break;
            }
        }

        return map;
    }

    private static bool NamesMatch(string? buttonName, DockProgressTile tile)
    {
        if (string.IsNullOrWhiteSpace(buttonName))
        {
            return false;
        }

        string name = buttonName.Trim();
        if (name.Length == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(tile.Title)
            && string.Equals(name, tile.Title.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(tile.Label))
        {
            return false;
        }

        string label = tile.Label.Trim();
        if (label.Length == 0)
        {
            return false;
        }

        if (string.Equals(name, label, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return name.StartsWith(label + " - ", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(label + " — ", StringComparison.OrdinalIgnoreCase);
    }
}
