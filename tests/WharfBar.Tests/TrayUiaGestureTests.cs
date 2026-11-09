using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Which cursor-free UIA gesture replays each mirrored click. The click path must never fall back
/// to physical input (that moves the user's pointer), so every kind maps to a UIA/keyboard action.
/// </summary>
public class TrayUiaGestureTests
{
    [Fact]
    public void LeftClick_InvokesTheButton()
    {
        Assert.Equal(TrayUiaGesture.Invoke, TrayUiaGestures.For(TrayClick.Left));
    }

    [Fact]
    public void DoubleLeftClick_InvokesTwice()
    {
        Assert.Equal(TrayUiaGesture.InvokeTwice, TrayUiaGestures.For(TrayClick.DoubleLeft));
    }

    [Fact]
    public void RightClick_UsesTheKeyboardMenuKey()
    {
        Assert.Equal(TrayUiaGesture.FocusMenuKey, TrayUiaGestures.For(TrayClick.Right));
    }

    [Fact]
    public void NoGestureDependsOnPhysicalPointerInput()
    {
        // Every kind is a UIA / keyboard action; there is deliberately no "physical click" value.
        foreach (TrayClick kind in Enum.GetValues<TrayClick>())
        {
            TrayUiaGesture gesture = TrayUiaGestures.For(kind);
            Assert.True(Enum.IsDefined(gesture), $"{kind} mapped outside the enum");
        }
    }
}
