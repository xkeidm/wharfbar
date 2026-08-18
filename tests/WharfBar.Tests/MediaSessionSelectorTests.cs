using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class MediaSessionSelectorTests
{
    [Fact]
    public void AddSession_Appends_And_Current_IsFirst()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        Assert.Equal(["A", "B"], sel.Sessions);
        Assert.Equal("A", sel.Current);
    }

    [Fact]
    public void MarkActive_MovesSessionToFront()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.MarkActive("B");
        Assert.Equal(["B", "A"], sel.Sessions);
        Assert.Equal("B", sel.Current);
    }

    [Fact]
    public void MarkActive_UnknownId_IsNoOp()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.MarkActive("X");
        Assert.Equal(["A"], sel.Sessions);
        Assert.Equal("A", sel.Current);
    }

    [Fact]
    public void AddSession_Existing_IsNoOp()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("A");
        Assert.Equal(["A"], sel.Sessions);
    }

    [Fact]
    public void RemoveSession_Current_FallsBackToMostRecentRemaining()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.MarkActive("A"); // A, B
        sel.RemoveSession("A");
        Assert.Equal("B", sel.Current);
        Assert.Equal(["B"], sel.Sessions);
    }

    [Fact]
    public void RemoveSession_All_CurrentIsNull()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.RemoveSession("A");
        Assert.Null(sel.Current);
        Assert.Empty(sel.Sessions);
    }

    [Fact]
    public void MoveCurrent_WrapsAround()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.AddSession("C");
        Assert.Equal("B", sel.MoveCurrent(1));          // [B, A, C]
        Assert.Equal(["B", "A", "C"], sel.Sessions);
        Assert.Equal("C", sel.MoveCurrent(1));          // [C, B, A]
        Assert.Equal(["C", "B", "A"], sel.Sessions);
        Assert.Equal("A", sel.MoveCurrent(1));          // [A, C, B]
        Assert.Equal(["A", "C", "B"], sel.Sessions);
        Assert.Equal("C", sel.MoveCurrent(-1));         // [C, A, B]
        Assert.Equal(["C", "A", "B"], sel.Sessions);
    }

    [Fact]
    public void MoveCurrent_UsesRingOrder_NotMruOrder()
    {
        var sel = new MediaSessionSelector();
        sel.AddSession("A");
        sel.AddSession("B");
        sel.AddSession("C");
        sel.MarkActive("C");                            // MRU [C, A, B]
        Assert.Equal(["C", "A", "B"], sel.Sessions);
        Assert.Equal("A", sel.MoveCurrent(1));          // halka [A, B, C] → A
        Assert.Equal(["A", "C", "B"], sel.Sessions);
    }

    [Fact]
    public void MoveCurrent_Empty_ReturnsNull()
    {
        var sel = new MediaSessionSelector();
        Assert.Null(sel.MoveCurrent(1));
    }
}
