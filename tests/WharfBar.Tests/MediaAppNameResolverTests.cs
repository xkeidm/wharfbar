using System.Collections.Generic;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MediaAppNameResolverTests
{
    [Theory]
    [InlineData("chrome.exe", "chrome")]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.Media.Player", "Microsoft.Media.Player")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Microsoft.ZuneMusic")]
    [InlineData("no-extension", "no-extension")]
    [InlineData("", "")]
    [InlineData("!OnlyBang", "OnlyBang")]
    public void SimplifyExeName_StripsBangSegmentAndExe(string aumid, string expected)
    {
        Assert.Equal(expected, MediaAppNameResolver.SimplifyExeName(aumid));
    }

    [Fact]
    public void ResolveCore_ShortcutHit_WinsOverProcess()
    {
        var shortcuts = new Dictionary<string, string> { ["com.github.th-ch.youtube-music"] = "YouTube Music" };
        Assert.Equal(
            "YouTube Music",
            MediaAppNameResolver.ResolveCore(
                "com.github.th-ch.youtube-music",
                shortcuts,
                _ => "Kronik Kromozom"));
    }

    [Fact]
    public void ResolveCore_NoShortcut_UsesProcessDescription()
    {
        var shortcuts = new Dictionary<string, string>();
        Assert.Equal(
            "Google Chrome",
            MediaAppNameResolver.ResolveCore("chrome.exe", shortcuts, _ => "Google Chrome"));
    }

    [Fact]
    public void ResolveCore_NoShortcutNoProcess_FallsBackToSimplified()
    {
        var shortcuts = new Dictionary<string, string>();
        Assert.Equal(
            "ghost-app-xyz",
            MediaAppNameResolver.ResolveCore("ghost-app-xyz.exe", shortcuts, _ => null));
    }

    [Fact]
    public void ResolveCore_ShortcutKey_IsCaseInsensitive()
    {
        var shortcuts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["F0DC299D809B9700"] = "Zen",
        };
        Assert.Equal(
            "Zen",
            MediaAppNameResolver.ResolveCore("f0dc299d809b9700", shortcuts, _ => null));
    }

    [Fact]
    public void ResolveCore_Empty_ReturnsEmpty()
    {
        Assert.Equal(
            string.Empty,
            MediaAppNameResolver.ResolveCore(string.Empty, new Dictionary<string, string>(), _ => null));
    }

    [Fact]
    public void Resolve_UnknownProcess_FallsBackToSimplifiedName()
    {
        var resolver = new MediaAppNameResolver();
        Assert.Equal("ghost-app-xyz", resolver.Resolve("ghost-app-xyz.exe"));
    }

    [Fact]
    public void Resolve_PackagedUnknown_FallsBackToAppSegment()
    {
        var resolver = new MediaAppNameResolver();
        Assert.Equal("MyApp", resolver.Resolve("SomePackage_abc!MyApp.exe"));
    }

    [Fact]
    public void Resolve_IsCached()
    {
        var resolver = new MediaAppNameResolver();
        string first = resolver.Resolve("ghost-app-xyz.exe");
        string second = resolver.Resolve("ghost-app-xyz.exe");
        Assert.Equal(first, second);
        Assert.Equal("ghost-app-xyz", second);
    }
}