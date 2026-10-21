using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class PrompterMotionTests
{
    [Fact]
    public void SpeedDipPerSec_OneX_IsBase() => Assert.Equal(66d, PrompterMotion.SpeedDipPerSec(1.0, true));

    [Theory]
    [InlineData(0.5)] [InlineData(3.0)] [InlineData(2.2)]
    public void SpeedDipPerSec_Multiplier_ScalesLinearly(double m)
        => Assert.Equal(66d * m, PrompterMotion.SpeedDipPerSec(PrompterMotion.ClampMultiplier(m), true));

    [Theory]
    [InlineData(0.49)] [InlineData(3.01)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void ClampMultiplier_OutOfRange_ClampsToBounds(double raw)
        => Assert.InRange(PrompterMotion.ClampMultiplier(raw), 0.5, 3.0);

    [Fact]
    public void SpeedDipPerSec_MotionDisabled_IsZero() => Assert.Equal(0d, PrompterMotion.SpeedDipPerSec(2.0, false));

    [Fact]
    public void UpdateOffset_PlayingTick_Advances() => Assert.Equal(76d, PrompterMotion.UpdateOffset(10, 1.0, 1.0, true));

    [Fact]
    public void UpdateOffset_MotionDisabled_HoldsOffset() => Assert.Equal(10d, PrompterMotion.UpdateOffset(10, 1.0, 1.0, false));

    [Fact]
    public void UpdateOffset_NegativeElapsed_Ignores() => Assert.Equal(10d, PrompterMotion.UpdateOffset(10, -1, 1.0, true));

    [Theory]
    [InlineData(400, 400, 123, 0)]
    [InlineData(400, 300, 123, 100)]
    [InlineData(400, 300, 999, 100)]
    public void ClampOffset_BoundsContent(double content, double viewport, double offset, double expected)
        => Assert.Equal(expected, PrompterMotion.ClampOffset(offset, content, viewport));

    [Fact]
    public void Toggle_Editing_Plays() => Assert.Equal(PrompterMotion.Playback.Playing, PrompterMotion.Toggle(PrompterMotion.Playback.Editing));

    [Fact]
    public void Toggle_FromPlaying_Pauses() => Assert.Equal(PrompterMotion.Playback.Paused, PrompterMotion.Toggle(PrompterMotion.Playback.Playing));

    [Fact]
    public void Toggle_FromPaused_Plays() => Assert.Equal(PrompterMotion.Playback.Playing, PrompterMotion.Toggle(PrompterMotion.Playback.Paused));
}
