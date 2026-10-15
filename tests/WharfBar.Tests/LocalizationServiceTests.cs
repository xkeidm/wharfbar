using System.Globalization;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class LocalizationServiceTests
{
    [Theory]
    [InlineData(null, "system")]
    [InlineData("", "system")]
    [InlineData("   ", "system")]
    [InlineData("SYSTEM", "system")]
    [InlineData("en", "en")]
    [InlineData("EN", "en")]
    [InlineData("tr", "tr")]
    [InlineData("Tr", "tr")]
    [InlineData("de", "system")]
    [InlineData("nope", "system")]
    public void NormalizeLanguage_MapsExpected(string? input, string expected)
    {
        Assert.Equal(expected, LocalizationService.NormalizeLanguage(input));
    }

    [Fact]
    public void ResolveCulture_En_ReturnsEnglish()
    {
        CultureInfo culture = LocalizationService.ResolveCulture("en");
        Assert.Equal("en", culture.Name);
    }

    [Fact]
    public void ResolveCulture_Tr_ReturnsTurkish()
    {
        CultureInfo culture = LocalizationService.ResolveCulture("tr");
        Assert.Equal("tr-TR", culture.Name);
    }

    [Theory]
    [InlineData("tr-TR", "tr-TR")]
    [InlineData("tr", "tr-TR")]
    [InlineData("en-US", "en")]
    [InlineData("de-DE", "en")]
    public void ResolveCulture_System_UsesProvidedUiCulture(string uiName, string expectedName)
    {
        var ui = CultureInfo.GetCultureInfo(uiName);
        CultureInfo culture = LocalizationService.ResolveCulture("system", ui);
        Assert.Equal(expectedName, culture.Name);
    }

    [Fact]
    public void Get_KnownKey_ReturnsEnglishByDefault()
    {
        // Neutral resx is English; process culture may be anything in CI.
        string value = LocalizationService.Get("General_PageTitle");
        Assert.False(string.IsNullOrWhiteSpace(value));
        Assert.NotEqual("General_PageTitle", value);
    }

    [Fact]
    public void Get_UnknownKey_ReturnsKey()
    {
        Assert.Equal("Definitely_Missing_Key_XYZ", LocalizationService.Get("Definitely_Missing_Key_XYZ"));
    }
}
