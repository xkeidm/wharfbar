using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TraySystemIconFilterAdditionalTests
{
    [Theory]
    [InlineData("Tepsi Girişi Göstergesi")]
    [InlineData("Tepsi Girişi Göstergesi Türkçe\nTürkçe Q\n\nGiriş yöntemleri arasında geçiş yapmak için Windows tuşu + boşluk tuşlarına basın.")]
    [InlineData("Input indicator")]
    [InlineData("Input indicator (English)")]
    public void IsSystemIcon_True_ForInputIndicator(string tooltip) =>
        Assert.True(TraySystemIconFilter.IsSystemIcon(tooltip));
}
