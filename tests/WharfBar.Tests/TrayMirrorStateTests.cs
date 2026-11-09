using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// The bar must not be silent about an empty tray mirror. Only a VERIFIED empty tray may be stated
/// as a fact ("your icons are in Windows' hidden area, Win+B"); every other empty result is "still
/// reading". Everything parked in the Hidden icons pill is an intentional empty bar and keeps the
/// default copy.
/// </summary>
public class TrayMirrorStateTests
{
    [Fact]
    public void PublishedIcons_ReportPopulated()
    {
        Assert.Equal(
            TrayMirrorState.Populated,
            TrayMirrorStates.For(3, TrayMirrorService.TrayBlipOutcome.Refreshed));
        Assert.Equal(
            TrayMirrorState.Populated,
            TrayMirrorStates.For(3, TrayMirrorService.TrayBlipOutcome.Inconclusive));
    }

    [Fact]
    public void VerifiedEmptyTray_IsReportedAsSuch()
    {
        Assert.Equal(
            TrayMirrorState.TrayConfirmedEmpty,
            TrayMirrorStates.For(0, TrayMirrorService.TrayBlipOutcome.ConfirmedEmpty));
    }

    [Fact]
    public void EmptyMirrorThatWasNeverRead_IsNotPresentedAsAFact()
    {
        Assert.Equal(
            TrayMirrorState.Unread,
            TrayMirrorStates.For(0, TrayMirrorService.TrayBlipOutcome.Inconclusive));
        Assert.Equal(
            TrayMirrorState.Unread,
            TrayMirrorStates.For(0, TrayMirrorService.TrayBlipOutcome.Skipped));
    }

    [Fact]
    public void IconsOnTheBar_KeepTheDefaultTooltip()
    {
        Assert.Equal(
            TrayOverflowTooltip.Default,
            TrayMirrorStates.TooltipFor(
                visibleIconCount: 8, mirrorItemCount: 12, state: TrayMirrorState.Populated));
    }

    [Fact]
    public void EverythingParkedInThePill_IsAnIntentionalEmptyBar()
    {
        // The user dragged every icon into the Hidden icons pill: the chevron keeps its normal copy.
        Assert.Equal(
            TrayOverflowTooltip.Default,
            TrayMirrorStates.TooltipFor(
                visibleIconCount: 0, mirrorItemCount: 4, state: TrayMirrorState.Populated));
    }

    [Fact]
    public void VerifiedEmptyTray_ExplainsWhereTheIconsAre()
    {
        Assert.Equal(
            TrayOverflowTooltip.NoVisibleIcons,
            TrayMirrorStates.TooltipFor(
                visibleIconCount: 0, mirrorItemCount: 0, state: TrayMirrorState.TrayConfirmedEmpty));
    }

    [Fact]
    public void UnreadEmptyMirror_SaysItIsStillReading()
    {
        Assert.Equal(
            TrayOverflowTooltip.Unread,
            TrayMirrorStates.TooltipFor(
                visibleIconCount: 0, mirrorItemCount: 0, state: TrayMirrorState.Unread));
    }
}
