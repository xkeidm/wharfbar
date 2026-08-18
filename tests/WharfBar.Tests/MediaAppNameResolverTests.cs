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