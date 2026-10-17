using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class ReentrancyGuardTests
{
    [Fact]
    public void NestedCall_IsNoOp_AndDoesNotRecurse()
    {
        var guard = new ReentrancyGuard();
        int runs = 0;
        bool? nested = null;

        void Body()
        {
            runs++;
            nested = guard.TryRun(Body); // simulates WndProc re-entry
        }

        Assert.True(guard.TryRun(Body));
        Assert.Equal(1, runs);
        Assert.False(nested);
    }

    [Fact]
    public void AfterCompletion_RunsAgain()
    {
        var guard = new ReentrancyGuard();
        int runs = 0;
        Assert.True(guard.TryRun(() => runs++));
        Assert.True(guard.TryRun(() => runs++));
        Assert.Equal(2, runs);
    }

    [Fact]
    public void ThrowingAction_ReleasesGuard()
    {
        var guard = new ReentrancyGuard();
        Assert.Throws<InvalidOperationException>(() => guard.TryRun(() => throw new InvalidOperationException()));
        Assert.True(guard.TryRun(() => { }));
    }
}
