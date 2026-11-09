using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Pure rule behind undoing the pointer warp a tray menu owner performs when it opens its context
/// menu at the real tray icon. The user clicked in the top bar; the pointer must stay there.
/// </summary>
public class TrayCursorWarpTests
{
    private static readonly RECT Monitor = new() { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
    private static readonly RECT TrayButton = new() { Left = 1453, Top = 1032, Right = 1485, Bottom = 1080 };

    [Fact]
    public void PointerWarpedToTheTrayAnchor_IsRestored()
    {
        // Measured: right-clicking AMD's tray button via the Menu key warped the pointer to
        // 1469,1056 (the real tray slot) ~30 ms after the gesture.
        Assert.True(TrayCursorWarp.ShouldRestore(new POINT { X = 1469, Y = 1056 }, TrayButton, Monitor));
    }

    [Fact]
    public void PointerWarpedToTheBottomEdgeOrTaskbarSlot_IsRestored()
    {
        Assert.True(TrayCursorWarp.ShouldRestore(new POINT { X = 640, Y = 1079 }, TrayButton, Monitor));
        Assert.True(TrayCursorWarp.ShouldRestore(new POINT { X = 1910, Y = 1020 }, TrayButton, Monitor));
    }

    [Fact]
    public void PointerNearAnotherTrayButtonAnchor_IsRestored()
    {
        // The owner may anchor to its own slot, not necessarily the clicked one (EarTrumpet at
        // 1572,695 opened its flyout from there; the bottom band still catches it).
        Assert.True(TrayCursorWarp.ShouldRestore(new POINT { X = 1572, Y = 900 }, TrayButton, Monitor));
    }

    [Fact]
    public void PointerMovedIntoTheDesktop_IsLeftAlone()
    {
        // A user moving the mouse after the click must never be yanked back.
        Assert.False(TrayCursorWarp.ShouldRestore(new POINT { X = 600, Y = 400 }, TrayButton, Monitor));
        Assert.False(TrayCursorWarp.ShouldRestore(new POINT { X = 1200, Y = 600 }, TrayButton, Monitor));
    }

    [Fact]
    public void PointerOnAnotherMonitor_IsLeftAlone()
    {
        Assert.False(TrayCursorWarp.ShouldRestore(new POINT { X = 2400, Y = 1000 }, TrayButton, Monitor));
    }

    [Fact]
    public void UnknownGeometry_IsNeverRestored()
    {
        Assert.False(TrayCursorWarp.ShouldRestore(new POINT { X = 100, Y = 100 }, TrayButton, default));
        Assert.False(TrayCursorWarp.ShouldRestore(new POINT { X = 100, Y = 100 }, default, Monitor));
    }
}
