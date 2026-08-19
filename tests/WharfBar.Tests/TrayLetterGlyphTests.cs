using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayLetterGlyphTests
{
    [Theory]
    [InlineData("Monitorian", "M")]
    [InlineData("G HUB Logitech G HUB", "G")]
    [InlineData(" Telefon Bağlantısı - Bağlı", "T")]
    [InlineData("AMD Software: Adrenalin Edition", "A")]
    [InlineData("nvidia ayarları", "N")]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void FirstLetter_ExtractsFirstCharacter(string? name, string expected) =>
        Assert.Equal(expected, TrayLetterGlyph.FirstLetter(name));

    [Fact]
    public void Build_ReturnsFrozen20pxBitmapOnStaThread()
    {
        Exception? caught = null;
        ImageSource? result = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = TrayLetterGlyph.Build("Monitorian");
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(caught);
        Assert.NotNull(result);
        Assert.True(result.IsFrozen);
        var bmp = Assert.IsAssignableFrom<BitmapSource>(result);
        Assert.Equal(20, bmp.PixelWidth);
        Assert.Equal(20, bmp.PixelHeight);
    }
}
