using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The magnifier crop over a frozen BGRA frame (top-down, row-padded stride): a cursor-centered
/// size×size box that fits exactly inside the frame, with a tight stride (size*4) on output. A pure
/// seam that does not touch native GDI; the frame is driven by injection.
/// </summary>
public class SnapshotMagnifierTests
{
    [Fact]
    public void TryCropBox_CenterOfFrame_ExtractsTightTopDownBox()
    {
        // A 5 wide × 4 high frame, padded to 6 pixels per row (stride 24).
        byte[] frame = PaddedFrame(width: 5, height: 4, pitch: 6, pixelAt: (x, y) => ((byte)(x * 10), (byte)(y * 20), (byte)(x + y)));
        int frameStride = 6 * 4;

        bool ok = ScreenSampler.TryCropBox(frame, frameStride, 5, 4, relX: 2, relY: 2, size: 3, out byte[]? crop);

        Assert.True(ok);
        Assert.NotNull(crop);
        int stride = 3 * 4;
        Assert.Equal(stride * 3, crop!.Length);
        // The box starts at frame corner (1,1): each cell takes the (1+r, 1+c) pixel from the source.
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 3; col++)
            {
                (int x, int y) = (1 + col, 1 + row);
                (byte r, byte g, byte b) = ((byte)(x * 10), (byte)(y * 20), (byte)(x + y));
                int offset = (row * stride) + (col * 4);
                Assert.Equal(b, crop[offset]);
                Assert.Equal(g, crop[offset + 1]);
                Assert.Equal(r, crop[offset + 2]);
            }
        }
    }

    [Fact]
    public void TryCropBox_CursorNearFrameEdge_ClampsInsideFrame()
    {
        // The cursor sits at the bottom-right edge (4,3); so that the 3×3 box does not overflow the 5×4
        // frame it shifts: x0 = min(4-1, 5-3) = 2, y0 = min(3-1, 4-3) = 1.
        byte[] frame = PaddedFrame(width: 5, height: 4, pitch: 6, pixelAt: (x, y) => ((byte)(x * 10), (byte)(y * 20), (byte)(x + y)));
        int frameStride = 6 * 4;

        bool ok = ScreenSampler.TryCropBox(frame, frameStride, 5, 4, relX: 4, relY: 3, size: 3, out byte[]? crop);

        Assert.True(ok);
        int stride = 3 * 4;
        // The top-left cell is now frame (2,1).
        (byte r, byte g, byte b) = ((byte)(2 * 10), (byte)(1 * 20), (byte)(2 + 1));
        Assert.Equal(b, crop![0]);
        Assert.Equal(g, crop[1]);
        Assert.Equal(r, crop[2]);
        // The bottom-right cell is frame (4,3).
        (r, g, b) = ((byte)(4 * 10), (byte)(3 * 20), (byte)(4 + 3));
        int offset = (2 * stride) + (2 * 4);
        Assert.Equal(b, crop[offset]);
        Assert.Equal(g, crop[offset + 1]);
        Assert.Equal(r, crop[offset + 2]);
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(2, -1)]
    [InlineData(-1, 2)]
    public void TryCropBox_CursorOutsideFrame_ReturnsFalse(int relX, int relY)
    {
        byte[] frame = PaddedFrame(width: 5, height: 4, pitch: 6, pixelAt: (x, y) => (0, 0, 0));

        bool ok = ScreenSampler.TryCropBox(frame, 6 * 4, 5, 4, relX, relY, size: 3, out byte[]? crop);

        Assert.False(ok);
        Assert.Null(crop);
    }

    [Fact]
    public void TryCropBox_CursorAtTopLeftCorner_ClampsToFrameOrigin()
    {
        // The cursor sits at (0,0); the 3×3 box start clamps to 0, so the crop's centre pixel is
        // frame (1,1), not the cursor pixel — an edge pick can be up to MagnifierGrid/2 px away.
        byte[] frame = PaddedFrame(width: 5, height: 4, pitch: 6, pixelAt: (x, y) => ((byte)(x * 10), (byte)(y * 20), (byte)(x + y)));

        bool ok = ScreenSampler.TryCropBox(frame, 6 * 4, 5, 4, relX: 0, relY: 0, size: 3, out byte[]? crop);

        Assert.True(ok);
        int stride = 3 * 4;
        (byte r, byte g, byte b) = ((byte)10, (byte)20, (byte)2);
        int offset = stride + 4;
        Assert.Equal(b, crop![offset]);
        Assert.Equal(g, crop[offset + 1]);
        Assert.Equal(r, crop[offset + 2]);
    }

    [Theory]
    [InlineData(0)] // zero
    [InlineData(-3)] // negative
    [InlineData(6)] // wider than the 5 px frame
    [InlineData(100)] // far larger than the frame
    public void TryCropBox_InvalidSize_ReturnsFalse(int size)
    {
        byte[] frame = PaddedFrame(width: 5, height: 4, pitch: 6, pixelAt: (x, y) => (0, 0, 0));

        bool ok = ScreenSampler.TryCropBox(frame, 6 * 4, 5, 4, relX: 2, relY: 2, size, out byte[]? crop);

        Assert.False(ok);
        Assert.Null(crop);
    }

    [Fact]
    public void TryCropBox_SizeTallerThanFrame_ReturnsFalse()
    {
        // 4 fits the 8 px width but not the 3 px height.
        byte[] frame = PaddedFrame(width: 8, height: 3, pitch: 8, pixelAt: (x, y) => (0, 0, 0));

        bool ok = ScreenSampler.TryCropBox(frame, 8 * 4, 8, 3, relX: 2, relY: 1, size: 4, out byte[]? crop);

        Assert.False(ok);
        Assert.Null(crop);
    }

    [Fact]
    public void TryCropBox_StrideTimesHeightOverflowsInt_ReturnsFalse()
    {
        // stride * height = int.MaxValue * 4: 32-bit math wraps negative and would pass the
        // buffer check, reading past the frame.
        bool ok = ScreenSampler.TryCropBox(new byte[32], int.MaxValue, 5, 4, relX: 2, relY: 2, size: 3, out byte[]? crop);

        Assert.False(ok);
        Assert.Null(crop);
    }

    [Fact]
    public void TryCropBox_ShortBuffer_ReturnsFalse()
    {
        bool ok = ScreenSampler.TryCropBox(new byte[10], 6 * 4, 5, 4, relX: 2, relY: 2, size: 3, out byte[]? crop);

        Assert.False(ok);
        Assert.Null(crop);
    }

    /// <summary>Produces a top-down, row-padded BGRA frame: each pixel is (r,g,b) plus a don't-care 0 byte.</summary>
    private static byte[] PaddedFrame(int width, int height, int pitch, Func<int, int, (byte r, byte g, byte b)> pixelAt)
    {
        var frame = new byte[pitch * 4 * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                (byte r, byte g, byte b) = pixelAt(x, y);
                int offset = (y * pitch * 4) + (x * 4);
                frame[offset] = b;
                frame[offset + 1] = g;
                frame[offset + 2] = r;
                frame[offset + 3] = 0;
            }
        }

        return frame;
    }
}
