using System;
using System.IO;
using System.Text.Json.Nodes;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The first-run setup marker: a fresh install starts incomplete (the wizard runs), an upgraded
/// config without the field is completed (the wizard never nags an existing user), and a config
/// that was reset after corruption does not count as a new install. An explicit false — the user
/// quit in the middle of the wizard — is preserved, so setup continues on the next launch.
/// </summary>
public class OnboardingConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "wharfbar-onboarding-config-tests", Guid.NewGuid().ToString("N"));

    private string ConfigFile => Path.Combine(_root, "config.json");

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

    private ConfigService NewService() => new(_root);

    [Fact]
    public void CreateDefault_StartsWithoutOnboardingCompleted()
    {
        Assert.False(ConfigService.CreateDefault().OnboardingCompleted);
    }

    [Fact]
    public void FirstLoad_WithoutConfigFile_IsFirstRunAndWritesIncompleteMarker()
    {
        ConfigService service = NewService();
        AppConfig config = service.LoadOrCreate();

        Assert.True(service.IsFirstRun);
        Assert.False(config.OnboardingCompleted);
        Assert.True(File.Exists(ConfigFile));

        // The marker is explicit on disk: a second launch reads the file (not a new install) and
        // keeps the incomplete state, so an interrupted setup is shown again.
        ConfigService reloaded = NewService();
        AppConfig second = reloaded.LoadOrCreate();
        Assert.False(reloaded.IsFirstRun);
        Assert.False(second.OnboardingCompleted);
    }

    [Fact]
    public void Save_WithCompletedMarker_RoundTrips()
    {
        ConfigService service = NewService();
        AppConfig config = service.LoadOrCreate();
        config.OnboardingCompleted = true;
        service.Save(config);

        ConfigService reloaded = NewService();
        Assert.True(reloaded.LoadOrCreate().OnboardingCompleted);
        Assert.False(reloaded.IsFirstRun);
    }

    [Fact]
    public void ExistingConfigWithoutOnboardingKey_IsTreatedAsCompleted()
    {
        // A config written by an older WharfBar build has no onboardingCompleted field.
        ConfigService service = NewService();
        service.LoadOrCreate();
        JsonNode root = JsonNode.Parse(File.ReadAllText(ConfigFile))!;
        Assert.True(root.AsObject().Remove("onboardingCompleted"));
        File.WriteAllText(ConfigFile, root.ToJsonString());

        ConfigService upgraded = NewService();
        AppConfig config = upgraded.LoadOrCreate();

        Assert.False(upgraded.IsFirstRun);
        Assert.True(config.OnboardingCompleted);
        Assert.Contains("onboardingCompleted", File.ReadAllText(ConfigFile), StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptConfig_ResetsDefaultsWithoutReplayingSetup()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(ConfigFile, "{ not json");

        ConfigService service = NewService();
        AppConfig config = service.LoadOrCreate();

        Assert.False(service.IsFirstRun);
        Assert.True(config.OnboardingCompleted);
        Assert.True(File.Exists(ConfigFile + ".bak"));
    }

    [Fact]
    public void ExplicitlyIncompleteConfig_StaysIncomplete()
    {
        ConfigService service = NewService();
        service.LoadOrCreate();

        // The file already carries "onboardingCompleted": false (fresh-install default).
        ConfigService reloaded = NewService();
        Assert.False(reloaded.LoadOrCreate().OnboardingCompleted);
    }
}
