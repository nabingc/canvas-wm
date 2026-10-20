using System.Collections.Immutable;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Services;
using static CanvasWm.Engine.Tests.WindowEventsSpec.Ev;

namespace CanvasWm.Engine.Tests.WindowEventsSpec;

public sealed class StartTests
{
    private static readonly (string, Rect)[] AbBatch =
    {
        ("a", R(725, 35, 705, 423)), ("b", R(725, 468, 705, 422)),
    };

    [Fact]
    public void AC1_FreshStart()
    {
        var ev = new Ev();
        ev.E.Start(new[] { ev.W("a"), ev.W("b"), ev.W("n", false) });
        Assert.Equal(DocAb(), ev.E.Document);
        Assert.Null(ev.E.FramedNodeId);
        Assert.Empty(ev.E.FramingHistory);
        Assert.Equal(AbBatch.ToList(), ev.OneBatch());
        Assert.DoesNotContain(ev.Win.Writes, w => w.Key.Value == "n");
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(DocAb(), ev.LastSaved.Document);
        Assert.Null(ev.LastSaved.FramedNodeId);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(StagePhase.Landing, ev.E.Phase);
        Assert.Equal(1, ev.E.PlacementGeneration);
        Assert.Empty(ev.Win.FocusRequests);
        Assert.Equal(1, ev.dChanges);
    }

    [Fact]
    public void AC1_PumpAndSettle()
    {
        var ev = new Ev();
        ev.E.Start(new[] { ev.W("a"), ev.W("b"), ev.W("n", false) });
        ev.Mark();
        ev.Win.Pump();
        Near(100, ev.E.PlacementDone!.Value);
        Assert.Equal(StagePhase.Fading, ev.E.Phase);
        Assert.Empty(ev.Win.FocusRequests);
        Assert.Null(ev.E.FocusedKey);
        Assert.Null(ev.E.EngineActivatedKey);
        Assert.Equal(double.NegativeInfinity, ev.E.LastFocusedTime(K("a")));
        Assert.Equal(double.NegativeInfinity, ev.E.LastFocusedTime(K("b")));
        Near(100.6, ev.E.SuppressRevealUntil);
        Assert.Null(ev.E.LandingFocus);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(1, ev.dChanges);

        ev.Mark();
        ev.Win.Reads.ToList(); // reads so far
        int reads0 = ev.Win.Reads.Count;
        ev.Clock.Advance(0.2);
        Assert.Equal(StagePhase.Desk, ev.E.Phase);
        Assert.Equal(new[] { K("a"), K("b") }, ev.Win.Reads.Skip(reads0).ToArray());
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(2, ev.Disp.SaveLog.Count);
        Assert.Empty(ev.Win.FocusRequests);
        Assert.NotNull(ev.E.Window(K("n")));
        Assert.Equal(NFrame, ev.Known("n"));
        Assert.Null(ev.Target("n"));
    }

    [Fact]
    public void AC1_VariantFocusBeforeLanding()
    {
        var ev = new Ev();
        ev.E.Start(new[] { ev.W("a"), ev.W("b"), ev.W("n", false) });
        ev.E.WindowFocused(K("b"));
        ev.Win.Pump();
        Assert.Equal(new[] { K("b") }, ev.Win.FocusRequests.ToArray());
        Assert.Equal(K("b"), ev.E.EngineActivatedKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [Trait("Assumption", "A3")]
    public void AC2_IgnoredSavedRecords(int which)
    {
        var ev = new Ev();
        var framings = new Dictionary<string, IReadOnlyList<string>> { ["1"] = new[] { "s" } };
        ev.Disp.Save(ev.Key, which == 0
            ? new SavedDisplay(Doc(X("s", H(P("x")), H(P("y")))), "s", framings)
            : new SavedDisplay(Doc(D)));
        ev.Mark();
        ev.E.Start(new[] { ev.W("a"), ev.W("b") });
        Assert.Equal(DocAb(), ev.E.Document);
        Assert.Equal(AbBatch.ToList(), ev.OneBatch());
        Assert.Null(ev.E.FramedNodeId);
        var s = ev.LastSaved;
        Assert.Empty(s.Framings);
        Assert.Empty(s.Owners);
        Assert.Null(s.DockSpots);
        Assert.Null(s.FloatSizes);
    }

    private static SavedDisplay Partial(string? framed = null, LayoutDocument? doc = null) =>
        new(doc ?? Doc(X("s", H(P("x")), H(P("a")))), framed,
            new Dictionary<string, IReadOnlyList<string>> { ["1"] = new[] { "a" } },
            new Dictionary<WindowKey, string> { [K("a")] = "o" });

    [Fact]
    [Trait("Assumption", "A5")]
    public void AC3_PartlyValidRecord()
    {
        var ev = new Ev();
        ev.Disp.Save(ev.Key, Partial());
        ev.Mark();
        ev.E.Start(new[] { ev.W("a"), ev.W("c") });
        Assert.Equal(Doc(X("n1", H(Y("n2", H(P("a")), H(P("c")))), H(D))), ev.E.Document);
        Assert.Equal(new List<(string, Rect)> { ("a", R(10, 35, 705, 423)), ("c", R(10, 468, 705, 422)) }, ev.OneBatch());
        var s = ev.LastSaved;
        Assert.Equal(new[] { "a" }, s.Framings["1"].ToArray());
        Assert.Equal("o", s.Owners[K("a")]);
    }

    [Fact]
    [Trait("Assumption", "A4")]
    public void AC3_SavedFramedIdKept()
    {
        var ev = new Ev();
        ev.Disp.Save(ev.Key, Partial("a"));
        ev.E.Start(new[] { ev.W("a"), ev.W("c") });
        Assert.Equal("a", ev.E.FramedNodeId);
        Assert.Equal(new[] { "a" }, ev.E.FramedLeaves.ToArray());
    }

    [Fact]
    [Trait("Assumption", "A4")]
    public void AC3_SavedFramedIdStaleIsNull()
    {
        var ev = new Ev();
        ev.Disp.Save(ev.Key, Partial("zzz"));
        ev.E.Start(new[] { ev.W("a"), ev.W("c") });
        Assert.Null(ev.E.FramedNodeId);
    }

    [Fact]
    [Trait("Assumption", "A6")]
    public void AC3_HiddenDesktopGetsNoInsertedDesktop()
    {
        var ev = new Ev();
        var hd = new HiddenDesktop("g", Array.Empty<FloatingPanel>(), null, new[] { "a" }, 0.5);
        var saved = new LayoutDocument(X("s", H(P("x")), H(P("a"))), null, null, hd);
        ev.Disp.Save(ev.Key, new SavedDisplay(saved));
        ev.E.Start(new[] { ev.W("a"), ev.W("c") });
        Assert.Null(LayoutTree.FindDesktop(ev.E.Document.Root));
        Assert.Equal(hd, ev.E.Document.HiddenDesktop);
    }

    [Fact]
    public void AC4_SortOrderIndependentOfListOrder()
    {
        var ev = new Ev();
        ev.E.Start(new[] { ev.W("c"), ev.W("b"), ev.W("a") });
        Assert.Equal(Doc(X("n1", H(D), H(Y("n2", H(P("a")), H(X("n3", H(P("b")), H(P("c")))))))), ev.E.Document);
        Assert.Equal(new List<(string, Rect)>
        {
            ("c", R(1083, 468, 347, 422)), ("b", R(725, 468, 348, 422)), ("a", R(725, 35, 705, 423)),
        }, ev.OneBatch());
    }

    [Fact]
    public void AC4_KeptPanelAndAnchor()
    {
        var ev = new Ev();
        ev.Disp.Save(ev.Key, new SavedDisplay(Doc(X("s", H(P("b")), H(D)))));
        ev.E.Start(new[] { ev.W("a"), ev.W("b") });
        Assert.Equal(Doc(X("s", H(Y("n1", H(P("b")), H(P("a")))), H(D))), ev.E.Document);
    }

    [Fact]
    public void AC4_SecondStartIsInert()
    {
        var ev = new Ev();
        ev.E.Start(new[] { ev.W("a"), ev.W("b") });
        ev.Win.Pump();
        ev.Mark();
        int reads = ev.Win.Reads.Count;
        ev.E.Start(new[] { ev.W("c") });
        Assert.Equal(0, ev.dSaves + ev.dPlans + ev.dFocus + ev.dChanges + ev.dPending + ev.dGen + ev.dWrites + ev.dLogs);
        Assert.Equal(reads, ev.Win.Reads.Count);
        Assert.False(ev.E.Holds(K("c")));
    }

    [Fact]
    [Trait("Assumption", "A7")]
    public void A7_HiddenWindowsStayHiddenAtStart()
    {
        var ev = new Ev();
        var hidden = new HiddenPanel(PanelNode.ForWindow("c"), new EngineShellSpec.SpecRestore("x"));
        ev.Disp.Save(ev.Key, new SavedDisplay(new LayoutDocument(X("s", H(P("a")), H(D)), null, new[] { hidden })));
        ev.E.Start(new[] { ev.W("a"), ev.W("c") });
        Assert.Single(ev.E.Document.Hidden);
        Assert.Null(LayoutEdits.PanelsOf(ev.E.Document.Root).FirstOrDefault(p => p.Views.Contains("c")));
    }

    [Fact]
    public void AC1_StartWhileSuspendedWritesNothing()
    {
        // B8: Start while suspended runs B1-B6 and B7 steps 1-4, with no write.
        var ev = new Ev();
        ev.E.Suspend();
        ev.Mark();
        ev.E.Start(new[] { ev.W("a"), ev.W("b") });
        Assert.Equal(DocAb(), ev.E.Document);
        Assert.Empty(ev.Win.Writes);
        Assert.True(ev.E.IsStarted);
    }
}
