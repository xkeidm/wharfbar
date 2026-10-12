using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockProgressTests
{
    [Fact]
    public void FromSample_HiddenTaskbarStaysAbsent()
    {
        DockProgressState state = DockProgress.FromSample(new DockProgressSample(false, 0, 100, 40, DockProgressFlags.None));
        Assert.Equal(DockProgressKind.None, state.Kind);
        Assert.False(state.IsVisible);
        Assert.Null(state.Percent);
    }

    [Fact]
    public void FromSample_NormalPercentComesFromTheRange()
    {
        var sample = new DockProgressSample(true, 0, 100, 40, DockProgressFlags.None);
        DockProgressState state = DockProgress.FromSample(sample);
        Assert.Equal(DockProgressKind.Normal, state.Kind);
        Assert.Equal(40d, state.Percent);
    }

    [Fact]
    public void FromSample_RangeWithoutASpanIsNotAPercent()
    {
        var sample = new DockProgressSample(true, 5, 5, 5, DockProgressFlags.None);
        Assert.Equal(DockProgressState.None, DockProgress.FromSample(sample));
    }

    [Fact]
    public void FromSample_ExposedWithoutValueOrStateStaysAbsent()
    {
        var sample = new DockProgressSample(true, null, null, null, DockProgressFlags.None);
        Assert.Equal(DockProgressKind.None, DockProgress.FromSample(sample).Kind);
    }

    [Fact]
    public void FromSample_IndeterminateDoesNotInventAPercent()
    {
        var sample = new DockProgressSample(true, null, null, null, DockProgressFlags.Indeterminate);
        DockProgressState state = DockProgress.FromSample(sample);
        Assert.Equal(DockProgressKind.Indeterminate, state.Kind);
        Assert.Null(state.Percent);
    }

    [Fact]
    public void FromSample_PausedAndErrorKeepARealPercent()
    {
        var paused = DockProgress.FromSample(new DockProgressSample(true, 0, 100, 10, DockProgressFlags.Paused));
        Assert.Equal(DockProgressKind.Paused, paused.Kind);
        Assert.Equal(10d, paused.Percent);

        var error = DockProgress.FromSample(new DockProgressSample(true, 0, 1, 0.25, DockProgressFlags.Error | DockProgressFlags.Paused));
        Assert.Equal(DockProgressKind.Error, error.Kind);
        Assert.Equal(25d, error.Percent);
    }

    [Fact]
    public void FlagsFromText_OnlyNamesAState()
    {
        Assert.Equal(DockProgressFlags.Paused, DockProgress.FlagsFromText("Paused"));
        Assert.Equal(DockProgressFlags.Error, DockProgress.FlagsFromText("Error"));
        Assert.Equal(DockProgressFlags.Indeterminate, DockProgress.FlagsFromText("indeterminate"));
        Assert.Equal(DockProgressFlags.None, DockProgress.FlagsFromText("Downloading 40%"));
        Assert.Equal(DockProgressFlags.None, DockProgress.FlagsFromText(null));
    }

    [Fact]
    public void Assign_MatchesTitleAndDoesNotInventABar()
    {
        var tiles = new[]
        {
            new DockProgressTile("path:notepad", "readme.txt - Notepad", "Notepad"),
            new DockProgressTile("path:calc", "Calculator", "Calculator"),
        };
        var buttons = new[]
        {
            new DockProgressButton("Notepad", DockProgressState.None),
            new DockProgressButton("readme.txt - Notepad", DockProgressState.Normal(15)),
        };

        IReadOnlyDictionary<string, DockProgressState> map = DockProgress.Assign(tiles, buttons);
        Assert.Equal(15d, map["path:notepad"].Percent);
        Assert.False(map.ContainsKey("path:calc"));
    }

    [Fact]
    public void Assign_EmptyButtonListLeavesEveryTileAbsent()
    {
        var tiles = new[] { new DockProgressTile("id:a", "A", "A") };
        Assert.Empty(DockProgress.Assign(tiles, []));
    }
}
