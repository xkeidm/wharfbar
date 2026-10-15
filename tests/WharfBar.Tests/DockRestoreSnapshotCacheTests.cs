using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockRestoreSnapshotCacheTests
{
    private static BitmapSource FakeShot()
    {
        var bmp = new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgra32, null);
        bmp.Freeze();
        return bmp;
    }

    [Fact]
    public void Put_ThenTryGet_ReturnsSameRectAndShot()
    {
        var cache = new DockRestoreSnapshotCache();
        var hwnd = new IntPtr(42);
        var rect = new RECT { Left = 10, Top = 20, Right = 110, Bottom = 220 };
        BitmapSource shot = FakeShot();

        cache.Put(hwnd, shot, rect);

        Assert.True(cache.TryGet(hwnd, out BitmapSource? got, out RECT gotRect));
        Assert.Same(shot, got);
        Assert.Equal(10, gotRect.Left);
        Assert.Equal(20, gotRect.Top);
        Assert.Equal(110, gotRect.Right);
        Assert.Equal(220, gotRect.Bottom);
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var cache = new DockRestoreSnapshotCache();
        var hwnd = new IntPtr(7);
        cache.Put(hwnd, FakeShot(), new RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 });
        cache.Remove(hwnd);
        Assert.False(cache.TryGet(hwnd, out _, out _));
    }

    [Fact]
    public void Put_OverMax_EvictsOldest()
    {
        var cache = new DockRestoreSnapshotCache();
        for (int i = 1; i <= DockRestoreSnapshotCache.MaxEntries + 1; i++)
        {
            cache.Put(new IntPtr(i), FakeShot(), new RECT { Left = i, Top = 0, Right = i + 1, Bottom = 1 });
        }

        Assert.False(cache.TryGet(new IntPtr(1), out _, out _));
        Assert.True(cache.TryGet(new IntPtr(2), out _, out _));
        Assert.True(cache.TryGet(new IntPtr(DockRestoreSnapshotCache.MaxEntries + 1), out _, out _));
        Assert.Equal(DockRestoreSnapshotCache.MaxEntries, cache.Count);
    }

    [Fact]
    public void Put_ZeroHwnd_IsIgnored()
    {
        var cache = new DockRestoreSnapshotCache();
        cache.Put(IntPtr.Zero, FakeShot(), new RECT { Left = 0, Top = 0, Right = 1, Bottom = 1 });
        Assert.Equal(0, cache.Count);
    }
}
