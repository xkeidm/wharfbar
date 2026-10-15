using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Show-only pixel-refresh cadence. Only the quiet path follows this policy; the state blip has
/// its own gate (<see cref="TrayStateBlipGate"/>), so no request here can move the work area.
/// </summary>
public class TrayIconRefreshPolicyTests
{
    [Fact]
    public void ShowOnly_TightensWhileIconsChange_AndRelaxesWhenStable()
    {
        Assert.Equal(
            TrayIconRefreshPolicy.ActiveShowOnlyInterval,
            TrayIconRefreshPolicy.MinGap(expedite: false, iconsRecentlyChanged: true));
        Assert.Equal(
            TrayMirrorService.BlipMinInterval,
            TrayIconRefreshPolicy.MinGap(expedite: false, iconsRecentlyChanged: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expedite_UsesTheFloor(bool iconsRecentlyChanged)
    {
        Assert.Equal(
            TrayIconRefreshPolicy.ExpediteMinGap,
            TrayIconRefreshPolicy.MinGap(expedite: true, iconsRecentlyChanged));
    }

    [Fact]
    public void Intervals_AreOrdered_SoNoRequestCanOutpaceTheFloor()
    {
        Assert.True(TrayIconRefreshPolicy.ExpediteMinGap <= TrayIconRefreshPolicy.ActiveShowOnlyInterval);
        Assert.True(TrayIconRefreshPolicy.ActiveShowOnlyInterval < TrayMirrorService.BlipMinInterval);
        // The floor is several polls wide: a burst of requests cannot turn into a peek per poll.
        Assert.True(TrayIconRefreshPolicy.ExpediteMinGap >= TrayMirrorService.PollInterval * 3);
    }

    [Fact]
    public void QuietRefreshCadence_IsFarBelowAnyStateBlipInterval()
    {
        // Even the slowest show-only cadence is much shorter than the rarest state blip: no
        // configuration can make the loud path the routine one.
        Assert.True(TrayMirrorService.BlipMinInterval * 5 < TrayStateBlipGate.BootstrapRetryInterval);
        Assert.True(TrayStateBlipGate.BootstrapRetryInterval <= TrayStateBlipGate.MaintenanceInterval);
    }

    [Fact]
    public void ShouldRefresh_WaitsForTheGap_AndNeverDuringAClick()
    {
        TimeSpan gap = TimeSpan.FromSeconds(60);

        Assert.False(TrayIconRefreshPolicy.ShouldRefresh(TimeSpan.FromSeconds(59), gap, clickBusy: false));
        Assert.True(TrayIconRefreshPolicy.ShouldRefresh(TimeSpan.FromSeconds(60), gap, clickBusy: false));
        Assert.False(TrayIconRefreshPolicy.ShouldRefresh(TimeSpan.FromHours(1), gap, clickBusy: true));
    }

    [Fact]
    public void FirstPoll_RefreshesImmediately()
    {
        TimeSpan sinceNever = DateTime.UtcNow - default(DateTime);
        Assert.True(TrayIconRefreshPolicy.ShouldRefresh(
            sinceNever,
            TrayIconRefreshPolicy.MinGap(expedite: false, iconsRecentlyChanged: false),
            clickBusy: false));
    }

    [Fact]
    public void ClickCapture_IsThrottled_ButFresherThanTheStableCadence()
    {
        Assert.True(TrayIconRefreshPolicy.ClickCaptureMinAge >= TrayIconRefreshPolicy.ExpediteMinGap);
        Assert.True(TrayIconRefreshPolicy.ClickCaptureMinAge < TrayMirrorService.BlipMinInterval);
    }

    [Fact]
    public void Expedite_IsDueOnlyOnceItsTimeHasCome()
    {
        var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.False(TrayIconRefreshPolicy.IsExpediteDue(0, now));
        Assert.False(TrayIconRefreshPolicy.IsExpediteDue(now.AddSeconds(1).Ticks, now));
        Assert.True(TrayIconRefreshPolicy.IsExpediteDue(now.Ticks, now));
        Assert.True(TrayIconRefreshPolicy.IsExpediteDue(now.AddSeconds(-5).Ticks, now));
    }
}
