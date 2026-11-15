using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// After a display change the top bar and the dock must be re-pointed at the live monitor entry:
/// their placement, the dock's AppBar reservation and the per-monitor window filter read the
/// captured <c>Monitor.Bounds</c>, so a stale copy leaves them on the old geometry.
/// </summary>
public class MonitorWindowSyncTests
{
    private static readonly RECT Bounds = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    private static readonly RECT WorkArea = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1032 };

    private static MonitorInfo Monitor(
        string deviceId = @"\\?\DISPLAY#DEL4098#5&1a2b3c4d&0&UID4352#{e6f07b5f}",
        RECT? bounds = null,
        RECT? workArea = null,
        uint dpi = 96,
        bool primary = true) =>
        new(
            IntPtr.Zero,
            deviceId,
            @"\\.\DISPLAY1",
            "Generic PnP Monitor",
            primary,
            bounds ?? Bounds,
            workArea ?? WorkArea,
            dpi);

    [Fact]
    public void UnchangedEntry_DoesNotRepoint()
    {
        Assert.False(MonitorWindowSync.ShouldRepoint(Monitor(), Monitor()));
    }

    [Fact]
    public void BoundsChanged_Repoints()
    {
        RECT resized = new() { Left = 0, Top = 0, Right = 2560, Bottom = 1440 };
        Assert.True(MonitorWindowSync.ShouldRepoint(Monitor(), Monitor(bounds: resized)));
    }

    [Fact]
    public void WorkAreaChanged_Repoints()
    {
        RECT work = new() { Left = 0, Top = 48, Right = 1920, Bottom = 1080 };
        Assert.True(MonitorWindowSync.ShouldRepoint(Monitor(), Monitor(workArea: work)));
    }

    [Fact]
    public void DpiChanged_Repoints()
    {
        Assert.True(MonitorWindowSync.ShouldRepoint(Monitor(dpi: 96), Monitor(dpi: 144)));
    }

    [Fact]
    public void PrimaryFlagChanged_Repoints()
    {
        Assert.True(MonitorWindowSync.ShouldRepoint(Monitor(primary: true), Monitor(primary: false)));
    }

    [Fact]
    public void DifferentDevice_DoesNotRepoint()
    {
        // Another display: the window belongs to the old monitor and is closed by the reconcile,
        // not re-pointed at a monitor it is not on.
        Assert.False(MonitorWindowSync.ShouldRepoint(Monitor(), Monitor(deviceId: @"\\?\DISPLAY#GSM5B7F#5&2&UID99#{e6f07b5f}")));
    }
}
