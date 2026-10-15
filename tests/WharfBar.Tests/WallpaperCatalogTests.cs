using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public sealed class WallpaperCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WharfBarTests", "wp-" + Guid.NewGuid().ToString("N"));

    public WallpaperCatalogTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* temp */ }
    }

    [Theory]
    [InlineData("a.jpg", true)]
    [InlineData("a.JPEG", true)]
    [InlineData("a.png", true)]
    [InlineData("a.webp", true)]
    [InlineData("a.txt", false)]
    [InlineData("a.bmp", false)]
    public void IsImageExtension_Filters(string name, bool ok)
        => Assert.Equal(ok, WallpaperCatalog.IsImageExtension(name));

    [Fact]
    public void List_MergesBundledThenUser_SortedByName()
    {
        string bundled = Path.Combine(_root, "bundled");
        string user = Path.Combine(_root, "user");
        Directory.CreateDirectory(bundled);
        Directory.CreateDirectory(user);
        File.WriteAllBytes(Path.Combine(bundled, "zeta.png"), [0]);
        File.WriteAllBytes(Path.Combine(bundled, "alpha.png"), [0]);
        File.WriteAllBytes(Path.Combine(user, "mine.jpg"), [0]);
        File.WriteAllText(Path.Combine(user, "skip.txt"), "x");

        var list = WallpaperCatalog.List(bundled, user);
        Assert.Equal(3, list.Count);
        Assert.Equal(WallpaperSource.Bundled, list[0].Source);
        Assert.Equal("alpha.png", list[0].DisplayName);
        Assert.Equal("zeta.png", list[1].DisplayName);
        Assert.Equal(WallpaperSource.User, list[2].Source);
        Assert.Equal("mine.jpg", list[2].DisplayName);
    }

    [Fact]
    public void List_MissingRoots_ReturnsEmpty()
        => Assert.Empty(WallpaperCatalog.List(Path.Combine(_root, "nope"), Path.Combine(_root, "nope2")));
}
