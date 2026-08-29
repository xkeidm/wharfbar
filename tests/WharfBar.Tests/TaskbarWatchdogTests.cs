using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TaskbarWatchdogStateStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "WharfBar.Tests",
        Guid.NewGuid().ToString("N"));

    private TaskbarWatchdogStateStore Store => new(_dir);

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        Assert.Null(Store.Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsState()
    {
        var state = new TaskbarWatchdogState
        {
            Pid = 4242,
            ProcessStartTime = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Local),
            BaselineState = 3,
            WorkAreaLeft = 0,
            WorkAreaTop = 0,
            WorkAreaRight = 2560,
            WorkAreaBottom = 1392,
            HaveWorkArea = true,
        };

        Store.Save(state);
        TaskbarWatchdogState? loaded = Store.Load();

        Assert.NotNull(loaded);
        Assert.Equal(state.Pid, loaded!.Pid);
        Assert.Equal(state.ProcessStartTime, loaded.ProcessStartTime);
        Assert.Equal(state.BaselineState, loaded.BaselineState);
        Assert.Equal(state.WorkAreaRight, loaded.WorkAreaRight);
        Assert.Equal(state.WorkAreaBottom, loaded.WorkAreaBottom);
        Assert.True(loaded.HaveWorkArea);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsNull()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Store.Path, "{ not json");

        Assert.Null(Store.Load());
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        Store.Save(new TaskbarWatchdogState { Pid = 1, ProcessStartTime = DateTime.Now });
        Assert.True(File.Exists(Store.Path));

        Store.Delete();

        Assert.False(File.Exists(Store.Path));
        Assert.Null(Store.Load());
    }

    [Fact]
    public void Delete_MissingFile_IsNoop()
    {
        Store.Delete();
        Assert.Null(Store.Load());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }
}

public class TaskbarWatchdogDecisionTests
{
    private static TaskbarWatchdogState State(int pid = 100, DateTime? start = null) => new()
    {
        Pid = pid,
        ProcessStartTime = start ?? new DateTime(2026, 8, 25, 9, 0, 0, DateTimeKind.Local),
        BaselineState = 1,
        HaveWorkArea = false,
    };

    [Fact]
    public void MissingState_DoesNotRestore()
    {
        Assert.False(TaskbarWatchdog.ShouldRestore(null, 100, State().ProcessStartTime));
    }

    [Fact]
    public void PidMismatch_DoesNotRestore()
    {
        TaskbarWatchdogState state = State(pid: 100);
        Assert.False(TaskbarWatchdog.ShouldRestore(state, 999, state.ProcessStartTime));
    }

    [Fact]
    public void StartTimeMismatch_DoesNotRestore()
    {
        // PID reuse: same pid, different process generation.
        TaskbarWatchdogState state = State(pid: 100);
        DateTime reused = state.ProcessStartTime + TimeSpan.FromMinutes(5);
        Assert.False(TaskbarWatchdog.ShouldRestore(state, 100, reused));
    }

    [Fact]
    public void MatchingPidAndStartTime_Restores()
    {
        TaskbarWatchdogState state = State(pid: 100);
        Assert.True(TaskbarWatchdog.ShouldRestore(state, 100, state.ProcessStartTime));
    }

    [Fact]
    public void StartTimeWithinOneSecond_Restores()
    {
        // File round-trip truncates ticks slightly; tolerate sub-second drift.
        TaskbarWatchdogState state = State(pid: 100);
        DateTime drifted = state.ProcessStartTime + TimeSpan.FromMilliseconds(800);
        Assert.True(TaskbarWatchdog.ShouldRestore(state, 100, drifted));
    }
}
