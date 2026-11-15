using WharfBar.Native;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public sealed class DesktopIconLayoutTests
{
    private const string Primary = @"\\.\DISPLAY1";
    private const string Secondary = @"\\.\DISPLAY2";

    private static MonitorWorkArea Monitor(string deviceName, int left, int top, int right, int bottom) =>
        new(deviceName, new WorkAreaRect(left, top, right, bottom));

    private static List<DesktopIconPoint> GridIcons()
    {
        // Three icons of the 2026-11-15 field report: pitch 76×91, origin (18,2).
        return
        [
            new DesktopIconPoint(@"C:\Users\me\Desktop\one.lnk", 18, 2),
            new DesktopIconPoint(@"C:\Users\me\Desktop\two.lnk", 18, 93),
            new DesktopIconPoint(@"C:\Users\me\Desktop\three.lnk", 94, 2),
        ];
    }

    [Fact]
    public void Create_UsesTopLeftIconAsAnchor_AndStoresGridCells()
    {
        DesktopIconLayoutSnapshot snapshot = DesktopIconLayoutSnapshot.Create(
            GridIcons(),
            [Monitor(Primary, 0, 0, 1920, 1032)],
            76,
            91)!;

        Assert.NotNull(snapshot);
        Assert.Equal(3, snapshot.Count);
        Assert.Equal(76, snapshot.PitchX);
        Assert.Equal(91, snapshot.PitchY);
        Assert.True(snapshot.TryGetAnchor(Primary, out DesktopIconAnchor anchor));
        Assert.Equal(18, anchor.X);
        Assert.Equal(2, anchor.Y);

        Assert.True(snapshot.TryGetCell(@"C:\Users\me\Desktop\one.lnk", out DesktopIconCell one));
        Assert.Equal(0, one.Column);
        Assert.Equal(0, one.Row);
        Assert.True(snapshot.TryGetCell(@"C:\Users\me\Desktop\two.lnk", out DesktopIconCell two));
        Assert.Equal(0, two.Column);
        Assert.Equal(1, two.Row);
        Assert.True(snapshot.TryGetCell(@"C:\Users\me\Desktop\three.lnk", out DesktopIconCell three));
        Assert.Equal(1, three.Column);
        Assert.Equal(0, three.Row);
    }

    [Fact]
    public void Map_ReprojectsCells_WhenPitchShrinksAndWorkAreaTopMoves()
    {
        DesktopIconLayoutSnapshot snapshot = DesktopIconLayoutSnapshot.Create(
            GridIcons(),
            [Monitor(Primary, 0, 0, 1920, 1032)],
            76,
            91)!;

        // Windows 11 25H2 after the top bar reserves 30 px: the whole layout is re-expressed on a
        // 90 px row pitch whose origin sits 30 px lower.
        List<DesktopIconPoint> targets = DesktopIconLayoutMap.Map(
            snapshot,
            [Monitor(Primary, 0, 30, 1920, 1032)],
            76,
            90,
            null);

        Assert.Equal(3, targets.Count);
        AssertTarget(targets, @"C:\Users\me\Desktop\one.lnk", 18, 32);
        AssertTarget(targets, @"C:\Users\me\Desktop\two.lnk", 18, 122);
        AssertTarget(targets, @"C:\Users\me\Desktop\three.lnk", 94, 32);
    }

    [Fact]
    public void Map_LeavesUntouchedMonitorIconsInPlace()
    {
        List<DesktopIconPoint> icons =
        [
            new DesktopIconPoint(@"C:\primary.lnk", 18, 2),
            new DesktopIconPoint(@"C:\secondary.lnk", 1938, 40),
        ];
        DesktopIconLayoutSnapshot snapshot = DesktopIconLayoutSnapshot.Create(
            icons,
            [Monitor(Primary, 0, 0, 1920, 1032), Monitor(Secondary, 1920, 0, 3840, 1040)],
            76,
            91)!;

        // Only the primary work area changes; the secondary monitor keeps its rect.
        List<DesktopIconPoint> targets = DesktopIconLayoutMap.Map(
            snapshot,
            [Monitor(Primary, 0, 30, 1920, 1040), Monitor(Secondary, 1920, 0, 3840, 1040)],
            76,
            90,
            null);

        AssertTarget(targets, @"C:\primary.lnk", 18, 32);
        AssertTarget(targets, @"C:\secondary.lnk", 1938, 40);
    }

    [Fact]
    public void Map_SkipsIconsOfAMonitorThatIsGone()
    {
        List<DesktopIconPoint> icons =
        [
            new DesktopIconPoint(@"C:\primary.lnk", 18, 2),
            new DesktopIconPoint(@"C:\secondary.lnk", 1938, 40),
        ];
        DesktopIconLayoutSnapshot snapshot = DesktopIconLayoutSnapshot.Create(
            icons,
            [Monitor(Primary, 0, 0, 1920, 1032), Monitor(Secondary, 1920, 0, 3840, 1040)],
            76,
            91)!;

        List<DesktopIconPoint> targets = DesktopIconLayoutMap.Map(
            snapshot,
            [Monitor(Primary, 0, 30, 1920, 1032)],
            76,
            90,
            null);

        DesktopIconPoint only = Assert.Single(targets);
        Assert.Equal(@"C:\primary.lnk", only.Name);
    }

    [Fact]
    public void Map_ReturnsOnlyRequestedIcons()
    {
        DesktopIconLayoutSnapshot snapshot = DesktopIconLayoutSnapshot.Create(
            GridIcons(),
            [Monitor(Primary, 0, 0, 1920, 1032)],
            76,
            91)!;

        List<DesktopIconPoint> targets = DesktopIconLayoutMap.Map(
            snapshot,
            [Monitor(Primary, 0, 30, 1920, 1032)],
            76,
            90,
            [@"C:\Users\me\Desktop\two.lnk", @"c:\users\me\desktop\unknown.lnk"]);

        DesktopIconPoint only = Assert.Single(targets);
        Assert.Equal(@"C:\Users\me\Desktop\two.lnk", only.Name);
    }

    [Fact]
    public void Map_FallsBackToCapturedPitch_WhenLiveSpacingIsMissing()
    {
        DesktopIconLayoutSnapshot snapshot = DesktopIconLayoutSnapshot.Create(
            GridIcons(),
            [Monitor(Primary, 0, 0, 1920, 1032)],
            76,
            91)!;

        List<DesktopIconPoint> targets = DesktopIconLayoutMap.Map(
            snapshot,
            [Monitor(Primary, 0, 30, 1920, 1032)],
            0,
            0,
            null);

        AssertTarget(targets, @"C:\Users\me\Desktop\two.lnk", 18, 123);
    }

    [Fact]
    public void Create_DerivesPitch_WhenSpacingIsUnavailable()
    {
        DesktopIconLayoutSnapshot snapshot = DesktopIconLayoutSnapshot.Create(
            GridIcons(),
            [Monitor(Primary, 0, 0, 1920, 1032)],
            0,
            0)!;

        Assert.Equal(76, snapshot.PitchX);
        Assert.Equal(91, snapshot.PitchY);
    }

    [Fact]
    public void Create_RefusesFreePositionedLayout()
    {
        List<DesktopIconPoint> icons =
        [
            new DesktopIconPoint(@"C:\one.lnk", 10, 5),
            new DesktopIconPoint(@"C:\two.lnk", 97, 48),
            new DesktopIconPoint(@"C:\three.lnk", 183, 101),
        ];

        DesktopIconLayoutSnapshot? snapshot = DesktopIconLayoutSnapshot.Create(
            icons,
            [Monitor(Primary, 0, 0, 1920, 1032)],
            76,
            91);

        Assert.Null(snapshot);
    }

    [Fact]
    public void Create_RefusesEmptyDesktop()
    {
        Assert.Null(DesktopIconLayoutSnapshot.Create(
            [],
            [Monitor(Primary, 0, 0, 1920, 1032)],
            76,
            91));
    }

    [Fact]
    public void WorkAreaRect_DistanceIsZeroInsideAndGrowsOutside()
    {
        var rect = new WorkAreaRect(0, 0, 100, 100);
        Assert.True(rect.Contains(50, 50));
        Assert.Equal(0L, rect.DistanceSquared(50, 50));
        Assert.Equal(0L, rect.DistanceSquared(100, 100));
        Assert.Equal(25L, rect.DistanceSquared(105, 100));
        Assert.Equal(50L, rect.DistanceSquared(-5, -5));
    }

    private static void AssertTarget(List<DesktopIconPoint> targets, string name, int x, int y)
    {
        DesktopIconPoint target = Assert.Single(targets, item => item.Name == name);
        Assert.Equal(x, target.X);
        Assert.Equal(y, target.Y);
    }
}
