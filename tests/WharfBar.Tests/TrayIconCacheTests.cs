using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Signature-keyed icon cache: unchanged pixels must keep the same instance (no UI work), changed
/// pixels must replace it (the Discord mute/call icon), and a failed capture must neither flicker
/// a real icon into a glyph nor keep a stale one forever. Pure; no bitmaps.
/// </summary>
public class TrayIconCacheTests
{
    private sealed class Icon(string name)
    {
        public string Name { get; } = name;

        public override string ToString() => Name;
    }

    private static readonly DateTime T0 = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Accept_SameSignature_ReturnsCachedInstance()
    {
        var cache = new TrayIconCache<Icon>();
        var first = new Icon("first");
        Assert.Same(first, cache.Accept("Discord", first, 42, T0, out bool changed1));
        Assert.True(changed1);

        Icon published = cache.Accept("Discord", new Icon("again"), 42, T0.AddSeconds(60), out bool changed2);

        Assert.Same(first, published);
        Assert.False(changed2);
    }

    [Fact]
    public void Accept_NewSignature_ReplacesTheIcon()
    {
        var cache = new TrayIconCache<Icon>();
        _ = cache.Accept("Discord", new Icon("unmuted"), 1, T0, out _);
        var muted = new Icon("muted");

        Icon published = cache.Accept("Discord", muted, 2, T0.AddSeconds(20), out bool changed);

        Assert.Same(muted, published);
        Assert.True(changed);
    }

    [Fact]
    public void Fallback_WithoutAttempt_KeepsTheRealIconRegardlessOfAge()
    {
        var cache = new TrayIconCache<Icon>();
        var real = new Icon("real");
        _ = cache.Accept("Steam", real, 7, T0, out _);

        Icon shown = cache.Fallback("Steam", () => new Icon("glyph"), T0.AddHours(3), captureAttempted: false);

        Assert.Same(real, shown);
        Assert.False(cache.IsPlaceholder("Steam"));
    }

    [Fact]
    public void Fallback_FailedCapture_KeepsRealIcon_UntilStaleLimit()
    {
        var cache = new TrayIconCache<Icon>();
        var real = new Icon("real");
        _ = cache.Accept("Discord", real, 7, T0, out _);
        TimeSpan limit = TrayIconCache<Icon>.StaleIconLimit;

        Assert.Same(real, cache.Fallback("Discord", () => new Icon("glyph"), T0 + limit - TimeSpan.FromSeconds(1), captureAttempted: true));

        Icon afterLimit = cache.Fallback("Discord", () => new Icon("glyph"), T0 + limit, captureAttempted: true);
        Assert.Equal("glyph", afterLimit.Name);
        Assert.True(cache.IsPlaceholder("Discord"));
    }

    [Fact]
    public void Accept_RefreshesTheStaleClock_EvenWhenUnchanged()
    {
        var cache = new TrayIconCache<Icon>();
        var real = new Icon("real");
        TimeSpan limit = TrayIconCache<Icon>.StaleIconLimit;
        _ = cache.Accept("Discord", real, 7, T0, out _);
        _ = cache.Accept("Discord", new Icon("same"), 7, T0 + limit - TimeSpan.FromMinutes(1), out _);

        Icon shown = cache.Fallback("Discord", () => new Icon("glyph"), T0 + limit + TimeSpan.FromMinutes(1), captureAttempted: true);

        Assert.Same(real, shown);
    }

    [Fact]
    public void Placeholder_IsBuiltOnce_AndReplacedByTheFirstRealCapture()
    {
        var cache = new TrayIconCache<Icon>();
        int builds = 0;
        Icon Build()
        {
            builds++;
            return new Icon("glyph");
        }

        Icon g1 = cache.Fallback("App", Build, T0, captureAttempted: false);
        Icon g2 = cache.Fallback("App", Build, T0.AddSeconds(4), captureAttempted: true);
        Assert.Same(g1, g2);
        Assert.Equal(1, builds);

        var real = new Icon("real");
        Icon published = cache.Accept("App", real, 0, T0.AddSeconds(8), out bool changed);
        Assert.Same(real, published);
        Assert.True(changed);
        Assert.False(cache.IsPlaceholder("App"));
    }

    [Fact]
    public void Retain_DropsKeysNoLongerInTheTray()
    {
        var cache = new TrayIconCache<Icon>();
        _ = cache.Accept("A", new Icon("a"), 1, T0, out _);
        _ = cache.Accept("B", new Icon("b"), 2, T0, out _);

        cache.Retain(["B"]);

        Assert.False(cache.TryGet("A", out _));
        Assert.True(cache.TryGet("B", out Icon b));
        Assert.Equal("b", b.Name);
        Assert.Equal(1, cache.Count);
    }
}
