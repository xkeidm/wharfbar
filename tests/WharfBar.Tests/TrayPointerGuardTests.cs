using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Pure rules behind keeping the pointer at the clicked mirror icon while a tray menu owner tries
/// to warp it to the real tray button at the screen's bottom edge.
/// </summary>
public class TrayPointerGuardTests
{
    private static readonly RECT Monitor = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };

    [Fact]
    public void ClipBox_IsATinyBoxAroundTheClick()
    {
        var box = TrayPointerGuard.ComputeClipBox(new POINT { X = 1551, Y = 15 }, Monitor, 6);

        Assert.Equal(1545, box.Left);
        Assert.Equal(9, box.Top);
        Assert.Equal(1558, box.Right);
        Assert.Equal(22, box.Bottom);
        // The visible displacement when an owner warps must stay imperceptible.
        Assert.True(box.Width <= 13 && box.Height <= 13);
    }

    [Fact]
    public void ClipBox_StaysInsideTheMonitorAtEveryEdge()
    {
        RECT topLeft = TrayPointerGuard.ComputeClipBox(new POINT { X = 0, Y = 0 }, Monitor, 6);
        Assert.Equal(0, topLeft.Left);
        Assert.Equal(0, topLeft.Top);
        Assert.True(topLeft.Width > 0 && topLeft.Height > 0);

        RECT bottomRight = TrayPointerGuard.ComputeClipBox(new POINT { X = 1920, Y = 1080 }, Monitor, 6);
        Assert.Equal(1920, bottomRight.Right);
        Assert.Equal(1080, bottomRight.Bottom);
        Assert.True(bottomRight.Width > 0 && bottomRight.Height > 0);

        RECT rightEdge = TrayPointerGuard.ComputeClipBox(new POINT { X = 1920, Y = 15 }, Monitor, 6);
        Assert.True(rightEdge.Right <= Monitor.Right);
        Assert.True(rightEdge.Width > 0);
    }

    [Fact]
    public void ClipBox_UnknownMonitorFallsBackToTheClickPoint()
    {
        var box = TrayPointerGuard.ComputeClipBox(new POINT { X = 500, Y = 500 }, default, 6);

        Assert.Equal(494, box.Left);
        Assert.Equal(494, box.Top);
        Assert.Equal(507, box.Right);
        Assert.Equal(507, box.Bottom);
    }

    [Fact]
    public void MovedFrom_IgnoresAFewPixelsOfClampDrift()
    {
        var home = new POINT { X = 1551, Y = 15 };

        Assert.False(TrayPointerGuard.MovedFrom(new POINT { X = 1553, Y = 17 }, home, 2));
        Assert.True(TrayPointerGuard.MovedFrom(new POINT { X = 1560, Y = 15 }, home, 2));
    }

    [Fact]
    public void IsClampedWarp_OnlyAcceptsATinyDisplacement()
    {
        var home = new POINT { X = 900, Y = 100 };

        Assert.True(TrayPointerGuard.IsClampedWarp(new POINT { X = 907, Y = 107 }, home));
        Assert.False(TrayPointerGuard.IsClampedWarp(new POINT { X = 940, Y = 100 }, home));
        Assert.False(TrayPointerGuard.IsClampedWarp(new POINT { X = 1341, Y = 1056 }, home));
    }

    [Fact]
    public void ClipPolicy_StaysShortAndTight()
    {
        // Measured: every owner warps 0–20 ms after the Menu key, so a short hold is enough and
        // never fights the user's own mouse for long.
        Assert.InRange(TrayPointerGuard.ClipHoldMs, 100, 250);
        Assert.InRange(TrayPointerGuard.ClipMarginPx, 2, 10);
        Assert.True(TrayPointerGuard.WatchMs >= TrayPointerGuard.ClipHoldMs * 3);
    }
}
