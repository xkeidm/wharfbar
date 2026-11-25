using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class DockRestoreSnapshotCacheTests
{
    private static BitmapSource FakeShot(int width = 8, int height = 8)
    {
        var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
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

    [Fact]
    public void Put_WideShot_StoresDownscaledFrozenCopy()
    {
        var cache = new DockRestoreSnapshotCache();
        var hwnd = new IntPtr(11);
        var rect = new RECT { Left = 5, Top = 6, Right = 1005, Bottom = 806 };
        BitmapSource shot = FakeShot(960, 540);

        cache.Put(hwnd, shot, rect);

        Assert.True(cache.TryGet(hwnd, out BitmapSource? got, out RECT gotRect));
        Assert.NotNull(got);
        Assert.NotSame(shot, got);
        Assert.True(got.PixelWidth <= DockRestoreSnapshotCache.MaxStoredWidth);
        Assert.True(got.PixelHeight < shot.PixelHeight);
        Assert.True(got.IsFrozen);
        // The restore rectangle must survive the downscale unchanged: geometry comes from the RECT,
        // never from the bitmap's pixel size.
        Assert.Equal(rect.Left, gotRect.Left);
        Assert.Equal(rect.Top, gotRect.Top);
        Assert.Equal(rect.Right, gotRect.Right);
        Assert.Equal(rect.Bottom, gotRect.Bottom);
    }

    [Fact]
    public void Put_ByteBudgetOverflow_EvictsOldest()
    {
        var cache = new DockRestoreSnapshotCache();

        // 480 px wide is stored as-is; 480x900 BGRA = 1,728,000 bytes per entry. At 30 entries the
        // cache is at ~51.8 MB, over the 48 MB budget while still below the 32-entry count cap, so
        // the byte budget alone must have evicted the oldest frame.
        const int frames = 30;
        for (int i = 1; i <= frames; i++)
        {
            cache.Put(new IntPtr(i), FakeShot(480, 900), new RECT { Left = i, Top = 0, Right = i + 1, Bottom = 1 });
        }

        Assert.Equal(29, cache.Count);
        Assert.False(cache.TryGet(new IntPtr(1), out _, out _));
        Assert.True(cache.TryGet(new IntPtr(2), out _, out _));
        Assert.True(cache.TryGet(new IntPtr(frames), out _, out _));
    }

    [Fact]
    public void Put_SameHwndTwice_ReplacesWithoutGrowingCount()
    {
        var cache = new DockRestoreSnapshotCache();
        var hwnd = new IntPtr(21);
        BitmapSource first = FakeShot(960, 540);
        BitmapSource second = FakeShot(480, 270);
        var rect = new RECT { Left = 1, Top = 2, Right = 3, Bottom = 4 };

        cache.Put(hwnd, first, rect);
        cache.Put(hwnd, second, rect);

        Assert.Equal(1, cache.Count);
        Assert.True(cache.TryGet(hwnd, out BitmapSource? got, out _));
        Assert.Same(second, got);
    }
}
