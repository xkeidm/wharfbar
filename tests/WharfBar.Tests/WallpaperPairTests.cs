using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class WallpaperPairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wharfbar-pair-" + Guid.NewGuid().ToString("N"));

    public WallpaperPairTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* ignore */ }
    }

    private string Touch(string name)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, [0]);
        return Path.GetFullPath(path);
    }

    [Fact]
    public void TryResolvePartner_LightToDark_WhenSiblingExists()
    {
        string light = Touch("pier-horizon-light.jpg");
        string dark = Touch("pier-horizon-dark.jpg");
        Assert.Equal(dark, WallpaperPair.TryResolvePartner(light, wantDark: true), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolvePartner_DarkToLight_WhenSiblingExists()
    {
        string light = Touch("pier-horizon-light.jpg");
        string dark = Touch("pier-horizon-dark.jpg");
        Assert.Equal(light, WallpaperPair.TryResolvePartner(dark, wantDark: false), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryResolvePartner_AlreadyDark_WantDark_IsNull()
    {
        string dark = Touch("pier-horizon-dark.jpg");
        Touch("pier-horizon-light.jpg");
        Assert.Null(WallpaperPair.TryResolvePartner(dark, wantDark: true));
    }

    [Fact]
    public void TryResolvePartner_MissingSibling_IsNull()
    {
        string light = Touch("alpine-lake-light.jpg");
        Assert.Null(WallpaperPair.TryResolvePartner(light, wantDark: true));
    }

    [Fact]
    public void TryResolvePartner_UnpairedSolid_IsNull()
    {
        string solid = Touch("solid-navy.jpg");
        Assert.Null(WallpaperPair.TryResolvePartner(solid, wantDark: true));
    }

    [Fact]
    public void TryResolvePartner_PreservesExtension()
    {
        string light = Touch("glass-rose-light.png");
        string dark = Touch("glass-rose-dark.png");
        Assert.Equal(dark, WallpaperPair.TryResolvePartner(light, wantDark: true), StringComparer.OrdinalIgnoreCase);
    }
}
