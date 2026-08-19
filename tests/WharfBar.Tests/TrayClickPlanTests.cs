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
}