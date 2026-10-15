using System;
using System.IO;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class LanguageConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wharfbar-lang-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void SaveLoad_PreservesLanguageEn()
    {
        var service = new ConfigService(_root);
        AppConfig config = ConfigService.CreateDefault();
        config.Language = "en";
        service.Save(config);

        AppConfig loaded = new ConfigService(_root).LoadOrCreate();
        Assert.Equal(LocalizationService.LanguageEn, loaded.Language);
    }

    [Theory]
    [InlineData(null, "system")]
    [InlineData("", "system")]
    [InlineData("DE", "system")]
    [InlineData("TR", "tr")]
    public void Save_NormalizesLanguage(string? raw, string expected)
    {
        var service = new ConfigService(_root);
        AppConfig config = ConfigService.CreateDefault();
        config.Language = raw!;
        service.Save(config);
        Assert.Equal(expected, service.Current.Language);
    }

    [Fact]
    public void CreateDefault_LanguageIsSystem()
    {
        Assert.Equal(LocalizationService.LanguageSystem, ConfigService.CreateDefault().Language);
    }
}
