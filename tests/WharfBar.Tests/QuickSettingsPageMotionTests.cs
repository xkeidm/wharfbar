using System.Windows.Media.Animation;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class QuickSettingsPageMotionTests
{
    [Fact]
    public void Duration_IsAShortEaseOut()
    {
        Assert.InRange(QuickSettingsPageMotion.DurationMs, 180, 280);
        Assert.Equal(TimeSpan.FromMilliseconds(QuickSettingsPageMotion.DurationMs), QuickSettingsPageMotion.Duration);
        Assert.InRange(QuickSettingsPageMotion.SlideDip, 8d, 28d);

        var ease = Assert.IsType<CubicEase>(QuickSettingsPageMotion.CreateEase());
        Assert.Equal(EasingMode.EaseOut, ease.EasingMode);
    }

    [Fact]
    public void Shift_DoesNotAnimateUntilTheFlyoutIsOpen()
    {
        QuickSettingsPageShift shift = QuickSettingsPageMotion.Shift(
            QuickSettingsPage.Main,
            QuickSettingsPage.Wifi,
            flyoutOpen: false);

        Assert.False(shift.Animate);
        Assert.Equal(0d, shift.OutgoingToX);
        Assert.Equal(0d, shift.IncomingFromX);
    }

    [Fact]
    public void Shift_DoesNotAnimateWhenThePageStays()
    {
        QuickSettingsPageShift shift = QuickSettingsPageMotion.Shift(
            QuickSettingsPage.Mixer,
            QuickSettingsPage.Mixer,
            flyoutOpen: true);

        Assert.False(shift.Animate);
    }

    [Fact]
    public void Shift_ForwardComesInFromTheRight()
    {
        foreach (QuickSettingsPage sub in SubPages())
        {
            QuickSettingsPageShift shift = QuickSettingsPageMotion.Shift(
                QuickSettingsPage.Main,
                sub,
                flyoutOpen: true);

            Assert.True(shift.Animate);
            Assert.Equal(0d, shift.OutgoingFromX);
            Assert.Equal(-QuickSettingsPageMotion.SlideDip, shift.OutgoingToX);
            Assert.Equal(QuickSettingsPageMotion.SlideDip, shift.IncomingFromX);
            Assert.Equal(0d, shift.IncomingToX);
        }
    }

    [Fact]
    public void Shift_BackComesInFromTheLeft()
    {
        foreach (QuickSettingsPage sub in SubPages())
        {
            QuickSettingsPageShift shift = QuickSettingsPageMotion.Shift(
                sub,
                QuickSettingsPage.Main,
                flyoutOpen: true);

            Assert.True(shift.Animate);
            Assert.Equal(0d, shift.OutgoingFromX);
            Assert.Equal(QuickSettingsPageMotion.SlideDip, shift.OutgoingToX);
            Assert.Equal(-QuickSettingsPageMotion.SlideDip, shift.IncomingFromX);
            Assert.Equal(0d, shift.IncomingToX);
        }
    }

    [Fact]
    public void Shift_PeerPagesCrossfadeWithoutASlide()
    {
        QuickSettingsPageShift shift = QuickSettingsPageMotion.Shift(
            QuickSettingsPage.Mixer,
            QuickSettingsPage.Network,
            flyoutOpen: true);

        Assert.True(shift.Animate);
        Assert.Equal(0d, shift.OutgoingFromX);
        Assert.Equal(0d, shift.OutgoingToX);
        Assert.Equal(0d, shift.IncomingFromX);
        Assert.Equal(0d, shift.IncomingToX);
    }

    private static IEnumerable<QuickSettingsPage> SubPages() =>
    [
        QuickSettingsPage.Mixer,
        QuickSettingsPage.Network,
        QuickSettingsPage.Wifi,
        QuickSettingsPage.Bluetooth,
        QuickSettingsPage.Project,
    ];
}
