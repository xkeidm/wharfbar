using System.Globalization;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public sealed class LocalizationServiceTests
{
    [Theory]
    [InlineData(null, AppConfig.LanguageSystem)]
    [InlineData("", AppConfig.LanguageSystem)]
    [InlineData("nope", AppConfig.LanguageSystem)]
    [InlineData("EN", AppConfig.LanguageEn)]
    [InlineData("tr", AppConfig.LanguageTr)]
    [InlineData("System", AppConfig.LanguageSystem)]
    public void NormalizeLanguage_maps_tokens(string? input, string expected)
    {
        Assert.Equal(expected, LocalizationService.NormalizeLanguage(input));
    }

    [Fact]
    public void ResolveCulture_system_turkish_os_returns_trTR()
    {
        CultureInfo culture = LocalizationService.ResolveCulture(
            AppConfig.LanguageSystem,
            new CultureInfo("tr-TR"));
        Assert.Equal("tr-TR", culture.Name);
    }

    [Fact]
    public void ResolveCulture_system_english_os_returns_enUS()
    {
        CultureInfo culture = LocalizationService.ResolveCulture(
            AppConfig.LanguageSystem,
            new CultureInfo("en-GB"));
        Assert.Equal("en-US", culture.Name);
    }

    [Fact]
    public void ResolveCulture_fixed_en_and_tr()
    {
        Assert.Equal("en-US", LocalizationService.ResolveCulture(AppConfig.LanguageEn).Name);
        Assert.Equal("tr-TR", LocalizationService.ResolveCulture(AppConfig.LanguageTr).Name);
    }
}
