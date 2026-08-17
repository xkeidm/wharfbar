using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class NightLightBlobTests
{
    private static byte[] EnabledBlob() =>
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00,
        0x2A, 0x06, 0xC1, 0x98, 0x99, 0xFB, 0x05, 0x2A,
        0x2B, 0x0E, 0x1F, 0x43, 0x42, 0x01, 0x00, 0x02,
        0x01, 0xCA, 0x14, 0x0E, 0x13, 0x2E, 0x39, 0x00,
    ];

    private static byte[] DisabledBlob() =>
    [
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00,
        0x2A, 0x06, 0xC1, 0x98, 0x99, 0xFB, 0x05, 0x2A,
        0x2B, 0x0E, 0x1F, 0x43, 0x42, 0x01, 0x00, 0xCA,
        0x14, 0x0E, 0x13, 0x2E, 0x39, 0x00,
    ];

    [Fact]
    public void TryParse_EnabledBlob_IsKnownAndTrue()
    {
        Assert.True(NightLightBlob.TryParse(EnabledBlob(), out bool enabled));
        Assert.True(enabled);
    }

    [Fact]
    public void TryParse_DisabledBlob_IsKnownAndFalse()
    {
        Assert.True(NightLightBlob.TryParse(DisabledBlob(), out bool enabled));
        Assert.False(enabled);
    }

    [Fact]
    public void TryParse_Null_IsUnknown()
    {
        Assert.False(NightLightBlob.TryParse(null, out _));
    }

    [Fact]
    public void TryParse_EmptyOrTooShort_IsUnknown()
    {
        Assert.False(NightLightBlob.TryParse(Array.Empty<byte>(), out _));
        Assert.False(NightLightBlob.TryParse(new byte[] { 0x43, 0x42, 0x01, 0x00, 0x0A }, out _));
    }

    [Fact]
    public void TryParse_SingleSignatureOnly_IsUnknown()
    {
        byte[] data = [0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00];
        Assert.False(NightLightBlob.TryParse(data, out _));
    }

    [Fact]
    public void ToggleFlag_EnabledRemovesFlag()
    {
        byte[] disabled = NightLightBlob.ToggleFlag(EnabledBlob(), enable: false);
        Assert.True(NightLightBlob.TryParse(disabled, out bool enabled));
        Assert.False(enabled);
        Assert.Equal(EnabledBlob().Length - 2, disabled.Length);
    }

    [Fact]
    public void ToggleFlag_DisabledAddsFlag()
    {
        byte[] enabled = NightLightBlob.ToggleFlag(DisabledBlob(), enable: true);
        Assert.True(NightLightBlob.TryParse(enabled, out bool flagOn));
        Assert.True(flagOn);
        Assert.Equal(DisabledBlob().Length + 2, enabled.Length);
    }

    [Fact]
    public void ToggleFlag_NoOpWhenAlreadyDesired()
    {
        Assert.Equal(EnabledBlob(), NightLightBlob.ToggleFlag(EnabledBlob(), enable: true));
        Assert.Equal(DisabledBlob(), NightLightBlob.ToggleFlag(DisabledBlob(), enable: false));
    }

    [Fact]
    public void ToggleFlag_EnabledRemovesFlag_ByteIdenticalToDisabledBlob()
    {
        byte[] disabled = NightLightBlob.ToggleFlag(EnabledBlob(), enable: false);
        Assert.Equal(DisabledBlob(), disabled);
    }

    [Fact]
    public void TryParse_SecondSignatureAtVeryEnd_IsUnknown()
    {
        byte[] data = new byte[] { 0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00, 0x43, 0x42, 0x01, 0x00 };
        Assert.False(NightLightBlob.TryParse(data, out _));
    }
}