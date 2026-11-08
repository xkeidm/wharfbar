using WharfBar.Native;
using Xunit;

namespace WharfBar.Tests;

public sealed class ProcessIntegrityTests
{
    [Theory]
    [InlineData(0x1000, true)] // low
    [InlineData(0x2000, false)] // medium
    [InlineData(0x3000, false)] // high
    [InlineData(0, true)] // SECURITY_MANDATORY_UNTRUSTED_RID is below medium
    [InlineData(-1, false)] // unreadable / probe failure → fail-open
    public void IsBelowMedium_UsesTheMediumBoundary(int rid, bool expected)
    {
        Assert.Equal(expected, ProcessIntegrity.IsBelowMedium(rid));
    }

    [Fact]
    public void TryReadLastSubAuthority_MediumSid_ReadsRid()
    {
        // S-1-16-8192: revision 1, one sub-authority, authority 16, RID 0x2000 (little-endian).
        byte[] sid = [1, 1, 0, 0, 0, 0, 0, 16, 0x00, 0x20, 0x00, 0x00];

        Assert.True(ProcessIntegrity.TryReadLastSubAuthority(sid, out int rid));
        Assert.Equal(ProcessIntegrity.MediumRid, rid);
    }

    [Fact]
    public void TryReadLastSubAuthority_UntrustedSid_ReadsZero()
    {
        // S-1-16-0: the untrusted RID is a real value, not a failure sentinel.
        byte[] sid = [1, 1, 0, 0, 0, 0, 0, 16, 0x00, 0x00, 0x00, 0x00];

        Assert.True(ProcessIntegrity.TryReadLastSubAuthority(sid, out int rid));
        Assert.Equal(0, rid);
    }

    [Fact]
    public void TryReadLastSubAuthority_MalformedSid_ReturnsFalse()
    {
        byte[][] malformed =
        [
            [],
            [1, 1, 0, 0, 0, 0, 0, 16], // authority only, no sub-authority
            [1, 2, 0, 0, 0, 0, 0, 16, 0x00, 0x20, 0x00, 0x00], // count does not match length
        ];

        foreach (byte[] sid in malformed)
        {
            Assert.False(ProcessIntegrity.TryReadLastSubAuthority(sid, out int rid));
            Assert.Equal(-1, rid);
        }
    }
}
