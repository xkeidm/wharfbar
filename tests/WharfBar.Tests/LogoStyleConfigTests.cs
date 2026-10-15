using System;
using System.IO;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class LogoStyleConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wharfbar-logo-" + Guid.NewGuid().ToString("N"));

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

    [Theory]
    [InlineData("wharfbar", "wharfbar")]
    [InlineData("WharfBar", "wharfbar")]
    [InlineData("windows", "windows")]
    [InlineData("Windows", "windows")]
    [InlineData(" WINDOWS ", "windows")]
    [InlineData(null, "wharfbar")]
    [InlineData("", "wharfbar")]
    [InlineData("apple", "wharfbar")]
    public void Normalize_FallsBackToWharfBar(string? input, string expected)
    {
        Assert.Equal(expected, LogoStyles.Normalize(input));
    }

    [Fact]
    public void Defaults_AreWharfBar()
    {
        Assert.Equal("wharfbar", LogoStyles.Default);
        Assert.Equal("wharfbar", new AppConfig().LogoStyle);
        Assert.Equal("wharfbar", ConfigService.CreateDefault().LogoStyle);
    }

    [Fact]
    public void SaveLoad_PreservesWindows()
    {
        var service = new ConfigService(_root);
        AppConfig config = ConfigService.CreateDefault();
        config.LogoStyle = "Windows";
        service.Save(config);

        Assert.Equal(LogoStyles.Windows, service.Current.LogoStyle);
        Assert.Contains("\"logoStyle\": \"windows\"", File.ReadAllText(service.ConfigPath));

        AppConfig loaded = new ConfigService(_root).LoadOrCreate();
        Assert.Equal(LogoStyles.Windows, loaded.LogoStyle);
        Assert.Equal(AppConfig.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [Theory]
    [InlineData("{ \"schemaVersion\": 2 }")]
    [InlineData("{ \"schemaVersion\": 2, \"logoStyle\": \"mystery\" }")]
    [InlineData("{ \"schemaVersion\": 2, \"logoStyle\": null }")]
    public void Load_MissingOrUnknown_BecomesWharfBar(string json)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "config.json"), json);

        AppConfig loaded = new ConfigService(_root).LoadOrCreate();

        Assert.Equal(LogoStyles.WharfBar, loaded.LogoStyle);
        Assert.Equal(AppConfig.CurrentSchemaVersion, loaded.SchemaVersion);
    }
}
