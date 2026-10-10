using System.IO;
using System.Runtime.InteropServices;
using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public class ShellIconTests
{
    [Theory]
    [InlineData(16, NativeMethods.SHIL_SMALL)]
    [InlineData(32, NativeMethods.SHIL_EXTRALARGE)]
    [InlineData(48, NativeMethods.SHIL_EXTRALARGE)]
    [InlineData(49, NativeMethods.SHIL_JUMBO)]
    [InlineData(256, NativeMethods.SHIL_JUMBO)]
    public void ImageListForPixelSize_NeverSelectsAListSmallerThanTheRequest(int sizePx, int expected) =>
        Assert.Equal(expected, ShellIcon.ImageListForPixelSize(sizePx));

    [Theory]
    [InlineData(32, 64, true)]
    [InlineData(16, 48, true)]
    [InlineData(48, 64, false)]
    [InlineData(256, 64, false)]
    [InlineData(64, 64, false)]
    [InlineData(0, 64, false)]
    public void WouldUpscale_RejectsOnly16Or32Bitmaps(int iconWidthPx, int requestedPx, bool expected) =>
        Assert.Equal(expected, ShellIcon.WouldUpscale(iconWidthPx, requestedPx));

    [Fact]
    public void TryExtract_NotepadAt64_IsShellSizedNotAStretched32()
    {
        string path = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        Assert.True(File.Exists(path));

        Assert.True(ShellIcon.TryExtract(path, 64, out IntPtr bitmap, out IntPtr icon));
        try
        {
            Assert.Equal(IntPtr.Zero, bitmap);
            Assert.NotEqual(IntPtr.Zero, icon);
            Assert.True(NativeMethods.GetIconInfo(icon, out ICONINFO info));
            try
            {
                IntPtr bits = info.hbmColor != IntPtr.Zero ? info.hbmColor : info.hbmMask;
                Assert.NotEqual(IntPtr.Zero, bits);
                Assert.NotEqual(0, NativeMethods.GetObject(bits, Marshal.SizeOf<BITMAP>(), out BITMAP bmp));
                Assert.True(bmp.bmWidth >= 48, $"notepad icon was {bmp.bmWidth}px; a 16/32 bitmap would be upscaled");
            }
            finally
            {
                if (info.hbmColor != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(info.hbmColor);
                }

                if (info.hbmMask != IntPtr.Zero)
                {
                    NativeMethods.DeleteObject(info.hbmMask);
                }
            }
        }
        finally
        {
            ShellIcon.Destroy(icon);
        }
    }
}
