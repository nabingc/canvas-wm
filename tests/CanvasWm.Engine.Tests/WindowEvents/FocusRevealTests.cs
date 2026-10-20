using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Projection;
using static CanvasWm.Engine.Tests.WindowEventsSpec.Ev;

namespace CanvasWm.Engine.Tests.WindowEventsSpec;

public sealed class FocusRevealTests
{
    /// <summary>AC15 step 1: S2, Move(Bottom), land. b framed, a parked, now ~100.934.</summary>
    private static Ev AfterStep1()
    {
        var ev = S2();
        ev.E.Move(Edge.Bottom);
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        ev.Clock.Advance(0.2);
        ev.Win.Pump();
        return ev;
    }

    [Fact]
    public void AC14_NoRevealOnStage()
    {
        var ev = S1();
        ev.AtTime(100.7);
        ev.Mark();
        ev.E.WindowFocused(K("b"));
        Assert.Equal(K("b"), ev.E.FocusedKey);
        Near(100.7, ev.E.LastFocusedTime(K("b"))!.Value);
        Assert.Equal(1, ev.dPlans);
        Assert.Empty(ev.Batches());
        Assert.Equal(0, ev.dPending);
        Assert.Equal(0, ev.dSaves);
        Assert.Null(ev.E.FramedNodeId);
        Near(100.6, ev.E.SuppressRevealUntil);
    }

    [Fact]
    public void AC15_Reveal()
    {
        var ev = S2();
        Assert.Equal(K("a"), ev.E.FocusedKey);
        Near(100.7, ev.E.LastFocusedTime(K("a"))!.Value);
        Assert.Null(ev.E.EngineActivatedKey);

        // step 1
        ev.E.Move(Edge.Bottom);
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        Assert.Empty(ev.Win.FocusRequests);
        Assert.Equal(K("a"), ev.E.FocusedKey);
        Near(101.334, ev.E.SuppressRevealUntil);
        ev.Clock.Advance(0.2);
        ev.Win.Pump();
        Assert.Equal("b", ev.E.FramedNodeId);
        Assert.True(ev.E.Window(K("a"))!.Parked);
        Assert.Equal(StagePhase.Desk, ev.E.Phase);

        // step 2
        ev.AtTime(101.0);
        ev.Mark();
        ev.E.WindowFocused(K("a"));
        Assert.Equal("b", ev.E.FramedNodeId);
        Assert.Equal(0, ev.dPending);
        Assert.Equal(0, ev.dGen);

        // step 3
        ev.AtTime(101.5);
        ev.Mark();
        ev.E.WindowFocused(K("a"));
        Near(102.5, ev.E.SuppressRevealUntil);
        Assert.Equal(K("a"), ev.E.LandingFocus);
        Assert.Equal("a", ev.E.FramedNodeId);
        Assert.Equal("b", ev.E.FramingHistory[^1]);
        Assert.Equal(1, ev.dGen);
        Assert.Equal(1, ev.dPending);
        Assert.Equal(1, ev.dLogs);
        Assert.Contains("a", ev.NewLogs[0]);
        Assert.Equal(0, ev.dSaves);

        // step 4
        ev.Clock.Advance(0.034);
        var batch = ev.OneBatch();
        Assert.Contains(("a", R(10, 35, 1420, 855)), batch);
        var bt = ev.Tgt("b");
        Assert.Equal(TargetKind.Park, bt.Kind);
        Assert.Contains(("b", bt.Rect), batch);

        // step 5
        ev.Win.Pump();
        Assert.Equal(K("a"), ev.Win.FocusRequests[^1]);
        Assert.Equal(K("a"), ev.E.EngineActivatedKey);
        Near(102.134, ev.E.SuppressRevealUntil);
        Assert.Null(ev.E.LandingFocus);
    }

    [Fact]
    public void AC16_CloseQuiet()
    {
        var ev = AfterStep1();
        ev.AtTime(102.0);
        ev.E.WindowRemoved(K("n"), false);
        Near(103.0, ev.E.CloseQuietUntil);
        ev.AtTime(102.4);
        ev.E.WindowFocused(K("a"));
        Assert.Equal("b", ev.E.FramedNodeId);
        ev.E.WindowFocused(K("a"));
        ev.Win.Pump();
        ev.AtTime(ev.E.CloseQuietUntil);
        ev.E.WindowFocused(K("a"));
        Assert.Equal("a", ev.E.FramedNodeId);
    }

    [Fact]
    public void AC16_BoundaryAtSuppressRevealUntil()
    {
        var ev = AfterStep1();
        ev.AtTime(ev.E.SuppressRevealUntil);
        Assert.Equal(ev.E.SuppressRevealUntil, ev.Clock.Now);
        ev.E.WindowFocused(K("a"));
        Assert.Equal("a", ev.E.FramedNodeId);
    }

    [Fact]
    public void AC16_NotAtRestKeepsPendingReveal()
    {
        var ev = AfterStep1();
        ev.AtTime(102.0);
        ev.E.InteractionActive = true;
        ev.Mark();
        ev.E.WindowFocused(K("a"));
        Assert.Equal(K("a"), ev.E.PendingReveal);
        Assert.Equal("b", ev.E.FramedNodeId);
        Assert.Equal(0, ev.dPending);
    }

    [Fact]
    [Trait("Assumption", "A11")]
    public void AC17_EngineActivatedMarker()
    {
        var ev = AfterStep1();
        ev.AtTime(102.0);
        int? pidBefore = ev.E.ActiveProcessId;
        ev.Mark();
        ev.E.Activate(K("a"));
        Assert.Equal(new[] { K("a") }, ev.NewFocus.ToArray());
        Assert.Equal(K("a"), ev.E.FocusedKey);
        Assert.Equal(K("a"), ev.E.EngineActivatedKey);
        Near(102.0, ev.E.LastFocusedTime(K("a"))!.Value);
        Assert.Equal(pidBefore, ev.E.ActiveProcessId);

        ev.E.WindowFocused(K("a"));
        Assert.Equal("b", ev.E.FramedNodeId);
        Assert.Null(ev.E.EngineActivatedKey);

        ev.AtTime(102.1);
        ev.E.WindowFocused(K("a"));
        Assert.Equal("a", ev.E.FramedNodeId);

        int n = ev.Win.FocusRequests.Count;
        ev.E.Activate(K("zz"));
        Assert.Equal(n, ev.Win.FocusRequests.Count);
    }

    private static Ev Setup(string scenario)
    {
        var ev = S1();
        switch (scenario)
        {
            case "i":
                ev.AtTime(100.3); ev.E.WindowFocused(K("a")); ev.E.LandingFocus = K("b"); break;
            case "ii":
                ev.AtTime(100.3); ev.E.WindowFocused(K("a")); ev.E.LandingFocus = K("n"); break;
            case "iii":
                ev.AtTime(100.3); ev.E.WindowFocused(K("b"));
                ev.AtTime(100.4); ev.E.WindowFocused(K("n"));
                Assert.Equal(K("n"), ev.E.FocusedKey);
                Assert.Null(ev.E.LandingFocus);
                break;
            case "iv":
                ev.E.SetLayout(Doc(D), null); break;
            case "v":
                break;
            case "vi":
                ev.AtTime(100.3); ev.E.WindowFocused(K("n")); break;
            case "tie":
                ev.AtTime(100.3); ev.E.WindowFocused(K("a")); ev.E.WindowFocused(K("b")); ev.E.WindowFocused(K("n")); break;
        }

        ev.AtTime(100.7);
        ev.Mark();
        return ev;
    }

    [Theory]
    [InlineData("i", "b")]
    [InlineData("ii", "a")]
    [InlineData("iii", "b")]
    [InlineData("iv", null)]
    [InlineData("v", null)]
    [InlineData("vi", null)]
    [InlineData("tie", "a")]
    [Trait("Assumption", "A12")]
    public void AC18_FocusAfterLandingChoice(string scenario, string? expected)
    {
        var ev = Setup(scenario);
        var focusedBefore = ev.E.FocusedKey;
        var activatedBefore = ev.E.EngineActivatedKey;
        ev.E.FocusAfterLanding();
        if (expected is null)
        {
            Assert.Equal(0, ev.dFocus);
            Assert.Equal(focusedBefore, ev.E.FocusedKey);
            Assert.Equal(activatedBefore, ev.E.EngineActivatedKey);
        }
        else
        {
            Assert.Equal(new[] { K(expected) }, ev.NewFocus.ToArray());
        }

        if (scenario == "v") Assert.Null(ev.E.FocusedKey);
        if (scenario == "vi") Assert.Equal(K("n"), ev.E.FocusedKey);
        Near(101.3, ev.E.SuppressRevealUntil);
        Assert.Null(ev.E.LandingFocus);
    }
}
