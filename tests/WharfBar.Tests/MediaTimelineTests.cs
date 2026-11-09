using Xunit;
using static WharfBar.Services.MediaTimeline;
using TimelineSample = WharfBar.Services.MediaTimeline.MediaTimelineSample;

namespace WharfBar.Tests;

/// <summary>
/// Pure tests for the local SMTC timeline interpolation: SMTC's raw position is frozen between app
/// reports, so <see cref="WharfBar.Services.MediaSessionService.RefreshTimeline"/> extrapolates it.
/// </summary>
public class MediaTimelineTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static TimelineSample Playing(
        TimeSpan? position,
        TimeSpan? duration,
        DateTimeOffset lastUpdated,
        double rate = 1.0)
        => new(position, duration, lastUpdated, rate, IsPlaying: true);

    [Fact]
    public void Interpolate_Playing_AdvancesAtNormalRate()
    {
        TimeSpan? position = Interpolate(
            Playing(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), Anchor),
            Anchor.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(15), position);
    }

    [Fact]
    public void Interpolate_Playing_HonoursPlaybackRate()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(12.5),
            Interpolate(Playing(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), Anchor, 0.5), Anchor.AddSeconds(5)));

        Assert.Equal(
            TimeSpan.FromSeconds(20),
            Interpolate(Playing(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), Anchor, 2.0), Anchor.AddSeconds(5)));
    }

    [Fact]
    public void Interpolate_Paused_DoesNotAdvance()
    {
        var sample = new TimelineSample(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            Anchor,
            PlaybackRate: 1.0,
            IsPlaying: false);

        Assert.Equal(TimeSpan.FromSeconds(10), Interpolate(sample, Anchor.AddSeconds(30)));
    }

    [Fact]
    public void Interpolate_ClampsAtDuration()
    {
        TimeSpan? position = Interpolate(
            Playing(TimeSpan.FromSeconds(58), TimeSpan.FromSeconds(60), Anchor),
            Anchor.AddSeconds(10));

        Assert.Equal(TimeSpan.FromSeconds(60), position);
    }

    [Fact]
    public void Interpolate_NegativePosition_ClampsToZero()
    {
        Assert.Equal(
            TimeSpan.Zero,
            Interpolate(Playing(TimeSpan.FromSeconds(-5), TimeSpan.FromMinutes(3), Anchor), Anchor));

        Assert.Equal(
            TimeSpan.FromSeconds(2),
            Interpolate(Playing(TimeSpan.FromSeconds(-5), TimeSpan.FromMinutes(3), Anchor), Anchor.AddSeconds(2)));
    }

    [Fact]
    public void Interpolate_DefaultTimestamp_DoesNotExtrapolate()
    {
        var sample = Playing(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), default);

        Assert.Equal(TimeSpan.FromSeconds(10), Interpolate(sample, Anchor));
    }

    [Fact]
    public void Interpolate_FutureTimestamp_DoesNotMoveBackwards()
    {
        var sample = Playing(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), Anchor.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(10), Interpolate(sample, Anchor));
    }

    [Fact]
    public void Interpolate_AgedOutAnchor_DoesNotInventJump()
    {
        var sample = Playing(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), Anchor.AddDays(-2));

        Assert.Equal(TimeSpan.FromSeconds(10), Interpolate(sample, Anchor));
    }

    [Fact]
    public void Interpolate_AnchorExactlyAtMaxGap_StillExtrapolates()
    {
        var sample = Playing(TimeSpan.FromSeconds(10), TimeSpan.FromHours(2), Anchor);

        // Exactly one hour of elapsed time is still trusted (clamped by the duration).
        Assert.Equal(
            TimeSpan.FromHours(1) + TimeSpan.FromSeconds(10),
            Interpolate(sample, Anchor + MaxInterpolationGap));
    }

    [Fact]
    public void Interpolate_AnchorBeyondMaxGap_StopsExtrapolating()
    {
        var sample = Playing(TimeSpan.FromSeconds(10), TimeSpan.FromHours(2), Anchor);

        TimeSpan? position = Interpolate(sample, Anchor + MaxInterpolationGap + TimeSpan.FromMilliseconds(1));

        Assert.Equal(TimeSpan.FromSeconds(10), position);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(100.0)]
    public void Interpolate_UnusablePlaybackRate_FallsBackToNormalSpeed(double rate)
    {
        TimeSpan? position = Interpolate(
            Playing(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), Anchor, rate),
            Anchor.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(15), position);
    }

    [Fact]
    public void Interpolate_NoPosition_ReturnsNull()
    {
        Assert.Null(Interpolate(Playing(null, TimeSpan.FromMinutes(3), Anchor), Anchor));
    }

    [Fact]
    public void Interpolate_NoDuration_AdvancesWithoutClamping()
    {
        TimeSpan? position = Interpolate(
            Playing(TimeSpan.FromSeconds(10), duration: null, Anchor),
            Anchor.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(15), position);
    }

    [Fact]
    public void Interpolate_ZeroDuration_IsTreatedAsUnknown()
    {
        TimeSpan? position = Interpolate(
            Playing(TimeSpan.FromSeconds(10), TimeSpan.Zero, Anchor),
            Anchor.AddSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(15), position);
    }

    [Fact]
    public void Interpolate_HonoursTimestampOffset()
    {
        var lastUpdatedWithOffset = new DateTimeOffset(2026, 1, 1, 15, 0, 0, TimeSpan.FromHours(3));
        var sample = Playing(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(3), lastUpdatedWithOffset);

        // The absolute instant is the same, so the result must not depend on the DateTimeOffset offset.
        Assert.Equal(TimeSpan.FromSeconds(15), Interpolate(sample, lastUpdatedWithOffset.ToUniversalTime().AddSeconds(5)));
    }

    [Fact]
    public void ShouldRaiseTimeline_Unchanged_ReturnsFalse()
    {
        Assert.False(ShouldRaiseTimeline(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: true,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: true));
    }

    [Fact]
    public void ShouldRaiseTimeline_BelowThreshold_ReturnsFalse()
    {
        Assert.False(ShouldRaiseTimeline(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: true,
            TimeSpan.FromMilliseconds(10_100),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: true));
    }

    [Fact]
    public void ShouldRaiseTimeline_AtThreshold_ReturnsTrue()
    {
        Assert.True(ShouldRaiseTimeline(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: true,
            TimeSpan.FromMilliseconds(10_200),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: true));
    }

    [Fact]
    public void ShouldRaiseTimeline_SeekBackwards_ReturnsTrue()
    {
        Assert.True(ShouldRaiseTimeline(
            TimeSpan.FromSeconds(60),
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: true,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: true));
    }

    [Fact]
    public void ShouldRaiseTimeline_DurationChanged_ReturnsTrue()
    {
        Assert.True(ShouldRaiseTimeline(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: true,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(4),
            nextCanSeek: true,
            nextIsPlaying: true));
    }

    [Fact]
    public void ShouldRaiseTimeline_CanSeekChanged_ReturnsTrue()
    {
        Assert.True(ShouldRaiseTimeline(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            publishedCanSeek: false,
            publishedIsPlaying: true,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: true));
    }

    [Fact]
    public void ShouldRaiseTimeline_IsPlayingChanged_ReturnsTrue()
    {
        // An optimistic play/pause flip is reconciled by the panel poll even when the position is unchanged.
        Assert.True(ShouldRaiseTimeline(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: true,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: false));

        Assert.True(ShouldRaiseTimeline(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: false,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: true));
    }

    [Fact]
    public void ShouldRaiseTimeline_TimelineGainedOrLost_ReturnsTrue()
    {
        Assert.True(ShouldRaiseTimeline(
            null,
            null,
            publishedCanSeek: false,
            publishedIsPlaying: false,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: false));

        Assert.True(ShouldRaiseTimeline(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: false,
            null,
            null,
            nextCanSeek: false,
            nextIsPlaying: false));
    }

    [Fact]
    public void ShouldRaiseTimeline_BothMissing_ReturnsFalse()
    {
        Assert.False(ShouldRaiseTimeline(
            null,
            null,
            publishedCanSeek: false,
            publishedIsPlaying: false,
            null,
            null,
            nextCanSeek: false,
            nextIsPlaying: false));
    }

    [Fact]
    public void ShouldRaiseTimeline_CustomThreshold_IsRespected()
    {
        Assert.False(ShouldRaiseTimeline(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: true,
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: true,
            minPositionDeltaMs: 1000));

        Assert.True(ShouldRaiseTimeline(
            TimeSpan.Zero,
            TimeSpan.FromMinutes(3),
            publishedCanSeek: true,
            publishedIsPlaying: true,
            TimeSpan.FromMilliseconds(1000),
            TimeSpan.FromMinutes(3),
            nextCanSeek: true,
            nextIsPlaying: true,
            minPositionDeltaMs: 1000));
    }
}
