using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ShowDesktopToggleTests
{
    /// <summary>In-memory window source recording every minimize/restore call.</summary>
    private sealed class FakeWindows : IShowDesktopWindows
    {
        public List<IntPtr> Minimizable = [];
        public List<IntPtr> Alive = [];
        public List<IntPtr> MinimizedCalls = [];
        public List<IntPtr> RestoredCalls = [];

        public IReadOnlyList<IntPtr> MinimizableWindows() => Minimizable;

        public bool IsAlive(IntPtr hwnd) => Alive.Contains(hwnd);

        public void Minimize(IntPtr hwnd) => MinimizedCalls.Add(hwnd);

        public void Restore(IntPtr hwnd) => RestoredCalls.Add(hwnd);
    }

    private static IntPtr H(int id) => new(id);

    [Fact]
    public void FirstClick_MinimizesFrontWindowsInZOrder_AndGoesToDesktop()
    {
        var windows = new FakeWindows { Minimizable = [H(1), H(2), H(3)], Alive = [H(1), H(2), H(3)] };
        var toggle = new ShowDesktopToggle();

        var result = toggle.Click(windows);

        Assert.Equal(ShowDesktopAction.Minimize, result.Action);
        Assert.Equal(3, result.Affected);
        Assert.Equal([H(1), H(2), H(3)], windows.MinimizedCalls);
        Assert.True(toggle.IsOnDesktop);
    }

    [Fact]
    public void SecondClick_RestoresInReverseZOrder_AndReturnsToNormal()
    {
        var windows = new FakeWindows { Minimizable = [H(1), H(2), H(3)], Alive = [H(1), H(2), H(3)] };
        var toggle = new ShowDesktopToggle();
        toggle.Click(windows);

        var result = toggle.Click(windows);

        Assert.Equal(ShowDesktopAction.Restore, result.Action);
        Assert.Equal(3, result.Affected);
        // Bottom-most restored first, the previously topmost window ends back on top.
        Assert.Equal([H(3), H(2), H(1)], windows.RestoredCalls);
        Assert.False(toggle.IsOnDesktop);
    }

    [Fact]
    public void SecondClick_SkipsWindowsClosedWhileOnDesktop()
    {
        var windows = new FakeWindows { Minimizable = [H(1), H(2), H(3)], Alive = [H(1), H(2), H(3)] };
        var toggle = new ShowDesktopToggle();
        toggle.Click(windows);

        // The user closed one app while looking at the desktop.
        windows.Alive = [H(2)];
        var result = toggle.Click(windows);

        Assert.Equal(ShowDesktopAction.Restore, result.Action);
        Assert.Equal(1, result.Affected);
        Assert.Equal([H(2)], windows.RestoredCalls);
        Assert.False(toggle.IsOnDesktop);
    }

    [Fact]
    public void ThirdClick_MinimizesTheWindowsOpenedInTheMeantime()
    {
        var windows = new FakeWindows { Minimizable = [H(1)], Alive = [H(1)] };
        var toggle = new ShowDesktopToggle();
        toggle.Click(windows);
        windows.MinimizedCalls = []; // record only this toggle step's calls
        toggle.Click(windows);

        // A new window was opened and restored while on the desktop.
        windows.Minimizable = [H(4), H(5)];
        windows.Alive = [H(4), H(5)];
        var result = toggle.Click(windows);

        Assert.Equal(ShowDesktopAction.Minimize, result.Action);
        Assert.Equal(2, result.Affected);
        Assert.Equal([H(4), H(5)], windows.MinimizedCalls);
        Assert.True(toggle.IsOnDesktop);
    }

    [Fact]
    public void Click_WithNothingOnScreen_StillFlipsPhaseAndStaysHarmless()
    {
        var windows = new FakeWindows { Alive = [] };
        var toggle = new ShowDesktopToggle();

        Assert.Equal(ShowDesktopAction.Minimize, toggle.Click(windows).Action);
        Assert.True(toggle.IsOnDesktop);

        var restore = toggle.Click(windows);
        Assert.Equal(ShowDesktopAction.Restore, restore.Action);
        Assert.Equal(0, restore.Affected);
        Assert.False(toggle.IsOnDesktop);
    }
}
