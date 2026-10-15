using System.IO;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public sealed class WallpaperPathGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "WharfBarTests", "wpg-" + Guid.NewGuid().ToString("N"));
    private readonly WallpaperService _svc;

    public WallpaperPathGuardTests()
    {
        string bundled = Path.Combine(_root, "bundled");
        string user = Path.Combine(_root, "user");
        Directory.CreateDirectory(bundled);
        Directory.CreateDirectory(user);
        File.WriteAllBytes(Path.Combine(bundled, "ok.png"), [1, 2, 3]);
        _svc = new WallpaperService(bundled, user);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void IsAllowedPath_AcceptsBundledFile()
    {
        string path = Path.Combine(_svc.BundledRoot, "ok.png");
        Assert.True(_svc.IsAllowedPath(path));
    }

    [Fact]
    public void IsAllowedPath_RejectsOutsideRoot()
    {
        string outside = Path.Combine(_root, "evil.png");
        File.WriteAllBytes(outside, [1]);
        Assert.False(_svc.IsAllowedPath(outside));
    }

    [Fact]
    public void TryApply_RejectsOutsideWithoutCallingThrough()
        => Assert.False(_svc.TryApply(Path.Combine(_root, "nope.png")));
}
