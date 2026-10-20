using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Projection;
using static CanvasWm.Engine.Tests.WindowEventsSpec.Ev;

namespace CanvasWm.Engine.Tests.WindowEventsSpec;

public sealed class ArrangeAllTests
{
    /// <summary>AC5 state after Pump, plus non-dockable d and z-order [c, d, a, b].</summary>
    private static Ev State()
    {
        var ev = Ev.Ac5();
        ev.E.WindowAdded(ev.W("d", false));
        ev.Win.SetZOrder(new[] { K("c"), K("d"), K("a"), K("b") });
        ev.Mark();
        return ev;
    }

    [Fact]
    public void AC26_ArrangeAll()
    {
        var ev = State();
        ev.Mark();
        ev.E.ArrangeAll();
        Assert.Equal(Doc(X("n4", H(P("c")), H(Y("n5", H(P("a")), H(X("n6", H(P("b")), H(D))))))), ev.E.Document);
        Assert.Null(ev.E.FramedNodeId);
        Assert.Equal(new List<(string, Rect)> { ("c", R(10, 35, 705, 855)) }, ev.OneBatch());
        Assert.Equal(R(725, 35, 705, 423), ev.Tgt("a").Rect);
        Assert.Equal(ev.Known("a"), ev.Tgt("a").Rect);
        Assert.Equal(R(725, 468, 348, 422), ev.Tgt("b").Rect);
        Assert.Equal(ev.Known("b"), ev.Tgt("b").Rect);
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(1, ev.dChanges);
        Assert.Equal(0, ev.dPending);
        Assert.DoesNotContain("d", ev.PanelKeys());
    }

    [Fact]
    public void AC26_FramedNodeIsClearedAndRecorded()
    {
        var ev = State();
        ev.E.SetLayout(ev.E.Document, "n2");
        ev.E.ArrangeAll();
        Assert.Null(ev.E.FramedNodeId);
        Assert.Equal("n2", ev.E.FramingHistory[^1]);
    }

    [Fact]
    public void AC26_InOverviewHistoryUntouchedNoWrites()
    {
        var ev = State();
        ev.E.SetLayout(ev.E.Document, "n2");
        ev.E.SetPhase(StagePhase.Overview);
        var hist = ev.E.FramingHistory.ToArray();
        ev.Mark();
        ev.E.ArrangeAll();
        Assert.Equal(hist, ev.E.FramingHistory.ToArray());
        Assert.Empty(ev.Batches());
        Assert.Equal(1, ev.dSaves);
        // SPEC-PROBLEM (reported): AC26 says "one render", but Commit step 3.2 renders once and step 3.5
        // (PresentLayout in Overview) renders once more, so the literal Commit definition gives 2.
        Assert.Equal(2, ev.dPlans);
    }

    [Fact]
    public void AC27_FrontToBackOrder()
    {
        var ev = State();
        ev.Win.RemoveWindow(K("a"));
        ev.Win.RemoveWindow(K("c"));
        ev.Win.RemoveWindow(K("d"));
        // spec says FrontToBack() == [b]; non-dockable n is still in the fake OS, and ArrangeAll ignores it
        Assert.Equal(new[] { K("b"), K("n") }, ev.Win.FrontToBack().ToArray());
        ev.E.ArrangeAll();
        Assert.Equal(Doc(X("n4", H(P("b")), H(Y("n5", H(P("a")), H(X("n6", H(P("c")), H(D))))))), ev.E.Document);
    }

    [Fact]
    public void AC27_NonDockableRefreshBecomesFloat()
    {
        var ev = State();
        ev.E.WindowAdded(ev.W("a", false));
        ev.E.ArrangeAll();
        Assert.Equal(Doc(X("n4", H(P("c")), H(Y("n5", H(P("b")), H(D))))).Root, ev.E.Document.Root);
        Assert.Equal(new[] { new FloatingPanel(P("a") as PanelNode ?? throw new InvalidOperationException(), Placement.CascadeRect(0), 1) },
            ev.E.Document.Floating.ToArray());
    }

    [Fact]
    public void AC27_UnmanagedKeysAreKeptAfterFrontToBack()
    {
        var ev = State();
        ev.E.SetLayout(Doc(X("s", H(P("b")), H(P("zz")))), null);
        ev.E.ArrangeAll();
        Assert.Equal(new[] { "b", "zz" }, ev.PanelKeys().ToArray());
    }

    [Fact]
    public void AC27_NotStartedOrSuspendedDoesNothing()
    {
        var ev = new Ev();
        ev.Mark();
        ev.E.ArrangeAll();
        Assert.Equal(0, ev.dSaves + ev.dPlans + ev.dWrites + ev.dPending);

        var s = State();
        s.E.Suspend();
        var doc = s.E.Document;
        s.Mark();
        s.E.ArrangeAll();
        Assert.Equal(doc, s.E.Document);
        Assert.Equal(0, s.dSaves + s.dPlans + s.dWrites + s.dPending);
    }
}

public sealed class MoveTests
{
    [Fact]
    [Trait("Assumption", "A12")]
    public void AC28_MoveFromFocusedWindow()
    {
        var ev = S2();
        var doc = ev.E.Document;
        ev.Mark();
        ev.E.Move(Edge.Bottom);
        Assert.Equal("b", ev.E.FramedNodeId);
        Assert.Equal(new string?[] { null }, ev.E.FramingHistory.ToArray());
        Assert.Empty(ev.Batches());
        Assert.Equal(1, ev.dGen);
        Assert.Equal(0, ev.dSaves);
        Assert.Equal(doc, ev.E.Document);

        ev.Clock.Advance(0.034);
        var batch = ev.OneBatch();
        Assert.Equal(TargetKind.Park, ev.Tgt("a").Kind);
        Assert.Equal(new List<(string, Rect)> { ("a", R(1439, 899, 705, 423)), ("b", R(10, 35, 1420, 855)) }, batch);
        ev.Win.Pump();
        Assert.Equal(0, ev.dFocus);
        Assert.Equal(K("a"), ev.E.FocusedKey);
        Assert.Null(ev.E.EngineActivatedKey);
        Near(101.334, ev.E.SuppressRevealUntil);
        ev.Mark();
        ev.Clock.Advance(0.2);
        Assert.Equal(1, ev.dSaves);
        Assert.Equal("b", ev.LastSaved.FramedNodeId);
    }

    [Fact]
    public void AC28_VariantPreviouslyFocusedNeighbourGetsFocus()
    {
        var ev = S1();
        ev.AtTime(100.65);
        ev.E.WindowFocused(K("b"));
        ev.AtTime(100.7);
        ev.E.WindowFocused(K("a"));
        ev.E.Move(Edge.Bottom);
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        Assert.Equal(new[] { K("b") }, ev.Win.FocusRequests.ToArray());
        Assert.Equal(K("b"), ev.E.FocusedKey);
    }

    [Fact]
    public void AC29_NoNeighbourIsNoOp()
    {
        var ev = S2();
        var hist = ev.E.FramingHistory.ToArray();
        ev.Mark();
        ev.E.Move(Edge.Right);
        Assert.Equal(0, ev.dGen + ev.dPlans + ev.dWrites + ev.dPending + ev.dSaves);
        Assert.Equal(hist, ev.E.FramingHistory.ToArray());
        Assert.Null(ev.E.FramedNodeId);
    }

    [Fact]
    public void AC29_MoveLeftFramesDesktop()
    {
        var ev = S2();
        ev.E.Move(Edge.Left);
        Assert.Equal("desktop", ev.E.FramedNodeId);
        ev.Clock.Advance(0.034);
        var batch = ev.OneBatch();
        Assert.Equal(TargetKind.Park, ev.Tgt("a").Kind);
        Assert.Equal(TargetKind.Park, ev.Tgt("b").Kind);
        Assert.Contains(("a", ev.Tgt("a").Rect), batch);
        Assert.Contains(("b", ev.Tgt("b").Rect), batch);
        ev.Mark();
        ev.Win.Pump();
        Assert.Equal(0, ev.dFocus);
        Near(ev.Clock.Now + 0.6, ev.E.SuppressRevealUntil);
    }

    [Fact]
    public void AC29_NothingFocusedOrNoPanel()
    {
        var ev = S1();
        ev.Mark();
        ev.E.Move(Edge.Top);
        Assert.Equal(0, ev.dGen + ev.dPlans + ev.dWrites + ev.dPending);

        ev.E.WindowFocused(K("n"));
        ev.Mark();
        ev.E.Move(Edge.Top);
        Assert.Equal(0, ev.dGen + ev.dPlans + ev.dWrites + ev.dPending);
    }

    [Fact]
    public void AC29_NotStartedOrSuspended()
    {
        var ev = new Ev();
        ev.Mark();
        ev.E.Move(Edge.Left);
        Assert.Equal(0, ev.dGen + ev.dPlans + ev.dWrites + ev.dPending + ev.dSaves);

        var s = S2();
        s.E.Suspend();
        s.Mark();
        s.E.Move(Edge.Bottom);
        Assert.Equal(0, s.dGen + s.dPlans + s.dWrites + s.dPending + s.dSaves);
        Assert.Null(s.E.FramedNodeId);
    }

    [Fact]
    public void AC29_UndefinedEdgeThrows()
    {
        var ev = S2();
        Assert.Throws<ArgumentOutOfRangeException>(() => ev.E.Move((Edge)99));
        Assert.Null(ev.E.FramedNodeId);
        Assert.Empty(ev.E.FramingHistory);
    }

    [Fact]
    public void AC30_MoveBackUp()
    {
        var ev = S2();
        ev.E.Move(Edge.Bottom);
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        ev.Clock.Advance(0.2);
        ev.Win.Pump();
        ev.E.Move(Edge.Top);
        Assert.Equal("a", ev.E.FramedNodeId);
        Assert.Equal(new string?[] { null, "b" }, ev.E.FramingHistory.ToArray());
    }
}
