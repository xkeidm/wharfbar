using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WindowsKeyGestureTests
{
    private const uint LWin = 0x5B;
    private const uint RWin = 0x5C;
    private const uint E = 0x45;
    private const uint Tab = 0x09;

    [Fact]
    public void BareTap_SwallowsBothEventsAndOpensTheLaunchpad()
    {
        var gesture = new WindowsKeyGesture();

        Assert.Equal(WindowsKeyAction.Swallow, gesture.OnKey(LWin, keyUp: false, injected: false));
        Assert.Equal(LWin, gesture.HeldKey);
        Assert.Equal(WindowsKeyAction.SwallowAndOpenLaunchpad, gesture.OnKey(LWin, keyUp: true, injected: false));
        Assert.Equal(0u, gesture.HeldKey);
    }

    [Fact]
    public void BareTap_DoesNotTouchTheStateOfTheHeldKeyBetweenEvents()
    {
        var gesture = new WindowsKeyGesture();

        _ = gesture.OnKey(LWin, keyUp: false, injected: false);

        Assert.Equal(LWin, gesture.HeldKey);
    }

    [Fact]
    public void RepeatedKeyDown_IsSwallowedOnce()
    {
        var gesture = new WindowsKeyGesture();

        Assert.Equal(WindowsKeyAction.Swallow, gesture.OnKey(LWin, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.Swallow, gesture.OnKey(LWin, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.Swallow, gesture.OnKey(LWin, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.SwallowAndOpenLaunchpad, gesture.OnKey(LWin, keyUp: true, injected: false));
    }

    [Fact]
    public void Chord_SynthesizesTheWinKeyDownForTheFirstChordKeyAndReleasesItOnTheKeyUp()
    {
        var gesture = new WindowsKeyGesture();

        Assert.Equal(WindowsKeyAction.Swallow, gesture.OnKey(LWin, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.InjectWinDownAndPass, gesture.OnKey(E, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(E, keyUp: true, injected: false));
        Assert.Equal(WindowsKeyAction.SwallowAndInjectWinUp, gesture.OnKey(LWin, keyUp: true, injected: false));
        Assert.Equal(0u, gesture.HeldKey);
    }

    [Fact]
    public void Chord_InjectsOnlyOnceForLaterChordKeys()
    {
        var gesture = new WindowsKeyGesture();

        _ = gesture.OnKey(LWin, keyUp: false, injected: false);
        Assert.Equal(WindowsKeyAction.InjectWinDownAndPass, gesture.OnKey(E, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(Tab, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(Tab, keyUp: true, injected: false));
        Assert.Equal(WindowsKeyAction.SwallowAndInjectWinUp, gesture.OnKey(LWin, keyUp: true, injected: false));
    }

    [Fact]
    public void ModifierChord_IsTreatedLikeAnyOtherChord()
    {
        var gesture = new WindowsKeyGesture();

        _ = gesture.OnKey(LWin, keyUp: false, injected: false);

        // Win+Shift+S: Shift is the first chord key, so it triggers the synthesized Win down.
        Assert.Equal(WindowsKeyAction.InjectWinDownAndPass, gesture.OnKey(0x10, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(0x53, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.SwallowAndInjectWinUp, gesture.OnKey(LWin, keyUp: true, injected: false));
    }

    [Fact]
    public void InjectedEvents_AreNeverSwallowed()
    {
        var gesture = new WindowsKeyGesture();

        // Another process (or this app's own Win+Tab dock tile) injecting the Win key: pass through.
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(LWin, keyUp: false, injected: true));
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(LWin, keyUp: true, injected: true));
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(Tab, keyUp: false, injected: true));
    }

    [Fact]
    public void InjectedEvents_DoNotDisturbAPhysicalTap()
    {
        var gesture = new WindowsKeyGesture();

        _ = gesture.OnKey(LWin, keyUp: false, injected: false);
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(E, keyUp: false, injected: true));
        Assert.Equal(WindowsKeyAction.SwallowAndOpenLaunchpad, gesture.OnKey(LWin, keyUp: true, injected: false));
    }

    [Fact]
    public void RightWindowsKey_WorksTheSame()
    {
        var gesture = new WindowsKeyGesture();

        Assert.Equal(WindowsKeyAction.Swallow, gesture.OnKey(RWin, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.SwallowAndOpenLaunchpad, gesture.OnKey(RWin, keyUp: true, injected: false));
    }

    [Fact]
    public void StrayKeyUp_PassesThroughInsteadOfOpeningTheLaunchpad()
    {
        var gesture = new WindowsKeyGesture();

        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(LWin, keyUp: true, injected: false));
        Assert.Equal(0u, gesture.HeldKey);
    }

    [Fact]
    public void Reset_ForgetsTheHeldKey()
    {
        var gesture = new WindowsKeyGesture();

        _ = gesture.OnKey(LWin, keyUp: false, injected: false);
        gesture.Reset();

        Assert.Equal(0u, gesture.HeldKey);
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(E, keyUp: false, injected: false));
        Assert.Equal(WindowsKeyAction.Pass, gesture.OnKey(LWin, keyUp: true, injected: false));
    }
}
