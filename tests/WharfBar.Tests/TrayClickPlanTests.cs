using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class TrayClickPlanTests
{
    [Fact]
    public void PackLParam_CombinesXAndY()
    {
        IntPtr packed = TrayClickPlan.PackLParam(0x1234, 0x5678);
        int value = packed.ToInt32();
        Assert.Equal(0x1234, value & 0xFFFF);
        Assert.Equal(0x5678, (value >> 16) & 0xFFFF);
    }

    [Fact]
    public void Left_BuildsDownUpSequence()
    {
        var plan = TrayClickPlan.Build(TrayClick.Left, 10, 20);
        Assert.Equal(2, plan.Count);
        Assert.Equal(NativeMethods.WM_LBUTTONDOWN, plan[0].Message);
        Assert.Equal((uint)NativeMethods.WM_LBUTTONUP, plan[1].Message);
        Assert.Equal(plan[0].LParam, plan[1].LParam);
    }

    [Fact]
    public void DoubleLeft_BuildsDownUpDblclkUp()
    {
        var plan = TrayClickPlan.Build(TrayClick.DoubleLeft, 10, 20);
        Assert.Equal(4, plan.Count);
        Assert.Equal(NativeMethods.WM_LBUTTONDBLCLK, plan[2].Message);
    }

    [Fact]
    public void Right_BuildsDownUpSequence()
    {
        var plan = TrayClickPlan.Build(TrayClick.Right, 10, 20);
        Assert.Equal(2, plan.Count);
        Assert.Equal(NativeMethods.WM_RBUTTONDOWN, plan[0].Message);
        Assert.Equal((uint)NativeMethods.WM_RBUTTONUP, plan[1].Message);
    }

    [Fact]
    public void RightDown_DoesNotForward()
    {
        bool forward = TrayGestureMap.TryForward(isRightButton: true, clickCount: 1, isRelease: false, out _);
        Assert.False(forward);
    }

    [Fact]
    public void RightRelease_ForwardsRight()
    {
        bool forward = TrayGestureMap.TryForward(isRightButton: true, clickCount: 1, isRelease: true, out TrayClick kind);
        Assert.True(forward);
        Assert.Equal(TrayClick.Right, kind);
    }

    [Fact]
    public void LeftDown_ForwardsLeft_AndDoubleOnSecondPress()
    {
        Assert.True(TrayGestureMap.TryForward(false, 1, false, out TrayClick single));
        Assert.Equal(TrayClick.Left, single);
        Assert.True(TrayGestureMap.TryForward(false, 2, false, out TrayClick doubled));
        Assert.Equal(TrayClick.DoubleLeft, doubled);
    }

    [Fact]
    public void LeftRelease_DoesNotForward()
    {
        bool forward = TrayGestureMap.TryForward(isRightButton: false, clickCount: 1, isRelease: true, out _);
        Assert.False(forward);
    }

    [Fact]
    public void NormalizeAbsolute_MapsVirtualScreenEdges()
    {
        Assert.Equal(0, SendInputMouse.NormalizeAbsolute(0, 0, 1920));
        Assert.Equal(65535, SendInputMouse.NormalizeAbsolute(1919, 0, 1920));
        Assert.Equal(0, SendInputMouse.NormalizeAbsolute(-1920, -1920, 3840));
        Assert.Equal(65535, SendInputMouse.NormalizeAbsolute(1919, -1920, 3840));
    }
}