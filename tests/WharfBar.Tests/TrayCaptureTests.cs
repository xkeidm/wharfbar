using System.Windows;
using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class TrayCaptureTests
{
    [Fact]
    public void TryComputeCropRect_ConvertsScreenToWindowCoords()
    {
        var win = new RECT { Left = 0, Top = 1088, Right = 1920, Bottom = 1136 };
        var btn = new RECT { Left = 1800, Top = 1104, Right = 1830, Bottom = 1124 };

        bool ok = TrayCapture.TryComputeCropRect(win, btn, 1920, 48, out Int32Rect crop);

        Assert.True(ok);
        Assert.Equal(new Int32Rect(1800, 16, 30, 20), crop);
    }

    [Fact]
    public void TryComputeCropRect_ClampsToCaptureBounds()
    {
        var win = new RECT { Left = 0, Top = 1088, Right = 1920, Bottom = 1136 };
        var btn = new RECT { Left = 1900, Top = 1104, Right = 1960, Bottom = 1150 };

        bool ok = TrayCapture.TryComputeCropRect(win, btn, 1920, 48, out Int32Rect crop);

        Assert.True(ok);
        Assert.Equal(new Int32Rect(1900, 16, 20, 32), crop);
    }

    [Fact]
    public void TryComputeCropRect_False_WhenOutsideCapture()
    {
        var win = new RECT { Left = 0, Top = 1088, Right = 1920, Bottom = 1136 };
        var btn = new RECT { Left = 2000, Top = 1104, Right = 2100, Bottom = 1124 };

        Assert.False(TrayCapture.TryComputeCropRect(win, btn, 1920, 48, out _));
    }

    [Fact]
    public void TryComputeCropRect_False_WhenWindowInvalid()
    {
        var win = new RECT();
        var btn = new RECT { Left = 10, Top = 10, Right = 30, Bottom = 30 };

        Assert.False(TrayCapture.TryComputeCropRect(win, btn, 100, 50, out _));
    }

    [Fact]
    public void TryComputeCropRect_False_WhenZeroSized()
    {
        var win = new RECT { Left = 0, Top = 0, Right = 100, Bottom = 50 };
        var btn = new RECT { Left = 10, Top = 10, Right = 10, Bottom = 30 };

        Assert.False(TrayCapture.TryComputeCropRect(win, btn, 100, 50, out _));
    }
}
