using System.Diagnostics;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class LaunchpadCatalogCacheTests
{
    private static LaunchpadCatalogSnapshot Snapshot(string name = "Edge") => new(
        [new LaunchpadApp(name, @"C:\apps\edge.lnk")],
        [],
        DateTimeOffset.UtcNow);

    [Fact]
    public void TryGet_ReturnsNothingBeforeWarm()
    {
        var cache = new LaunchpadCatalogCache(() => Snapshot());

        Assert.False(cache.TryGet(out _));
        Assert.Null(cache.Snapshot);
    }

    [Fact]
    public void Warm_FillsSnapshotWithLoaderData()
    {
        int calls = 0;
        var cache = new LaunchpadCatalogCache(() =>
        {
            Interlocked.Increment(ref calls);
            return Snapshot("Nachricht");
        });

        cache.Warm();
        Assert.True(SpinWaituntilLoaded(cache, TimeSpan.FromSeconds(5)));

        Assert.True(cache.TryGet(out LaunchpadCatalogSnapshot? snapshot));
        Assert.Equal(["Nachricht"], snapshot.Apps.Select(app => app.Name).ToArray());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Warm_DoesNotLoadTwice()
    {
        int calls = 0;
        var cache = new LaunchpadCatalogCache(() =>
        {
            Interlocked.Increment(ref calls);
            return Snapshot();
        });

        cache.Warm();
        Assert.True(SpinWaituntilLoaded(cache, TimeSpan.FromSeconds(5)));
        cache.Warm();
        Thread.Sleep(50);

        Assert.Equal(1, calls);
    }

    [Fact]
    public void Publish_ReplacesTheSnapshot()
    {
        var cache = new LaunchpadCatalogCache(() => Snapshot());

        cache.Publish(Snapshot("Yeni"));

        Assert.True(cache.TryGet(out LaunchpadCatalogSnapshot? snapshot));
        Assert.Equal(["Yeni"], snapshot.Apps.Select(app => app.Name).ToArray());
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(100, 60_000, false)]
    [InlineData(61_000, 60_000, true)]
    public void IsStale_ChecksTheAgeAgainstTheTtl(int ageMs, int ttlMs, bool expected)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var snapshot = new LaunchpadCatalogSnapshot(
            [],
            [],
            now - TimeSpan.FromMilliseconds(ageMs));

        Assert.Equal(
            expected,
            LaunchpadCatalogCache.IsStale(snapshot, now, TimeSpan.FromMilliseconds(ttlMs)));
    }

    [Fact]
    public void IsStale_NullSnapshotIsStale()
    {
        Assert.True(LaunchpadCatalogCache.IsStale(null, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void LoadSync_JoinsAnInFlightWarmScan()
    {
        using var gate = new ManualResetEventSlim(false);
        using var started = new ManualResetEventSlim(false);
        int calls = 0;
        var cache = new LaunchpadCatalogCache(() =>
        {
            Interlocked.Increment(ref calls);
            started.Set();
            gate.Wait(TimeSpan.FromSeconds(5));
            return Snapshot();
        });

        cache.Warm();
        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                cache.LoadSync();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.Start();

        // Give the joiner time to block on the in-flight task before the loader may finish.
        Thread.Sleep(100);
        Assert.Equal(1, calls);
        gate.Set();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(error);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void SnapshotChanged_FiresOncePerLoad()
    {
        var cache = new LaunchpadCatalogCache(() => Snapshot());
        int fired = 0;
        cache.SnapshotChanged += () => fired++;

        cache.Warm();
        Assert.True(SpinWaituntilLoaded(cache, TimeSpan.FromSeconds(5)));

        Assert.Equal(1, fired);
    }

    private static bool SpinWaituntilLoaded(LaunchpadCatalogCache cache, TimeSpan timeout)
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (cache.Snapshot is not null)
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return false;
    }
}
