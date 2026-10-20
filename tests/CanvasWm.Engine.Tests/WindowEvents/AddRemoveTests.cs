using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;
using static CanvasWm.Engine.Tests.WindowEventsSpec.Ev;

namespace CanvasWm.Engine.Tests.WindowEventsSpec;

public sealed class AddTests
{
    private static LayoutDocument DocAbc() =>
        Doc(X("n1", H(D), H(Y("n2", H(P("a")), H(X("n3", H(P("b")), H(P("c"))))))));

    [Fact]
    [Trait("Assumption", "A13")]
    public void AC5_NewWindowTilesBesideFocused()
    {
        var ev = S1();
        ev.E.WindowFocused(K("b"));
        Assert.Equal(K("b"), ev.E.FocusedKey);
        Near(100.6, ev.E.SuppressRevealUntil); // 100.2 < 100.6: no reveal
        Assert.Null(ev.E.FramedNodeId);
        ev.Mark();
        ev.E.WindowAdded(ev.W("c"));
        Assert.Equal(DocAbc(), ev.E.Document);
        Assert.Equal(new List<(string, Rect)> { ("b", R(725, 468, 348, 422)), ("c", R(1083, 468, 347, 422)) }, ev.OneBatch());
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(DocAbc(), ev.LastSaved.Document);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(1, ev.dChanges);
        Assert.Equal(0, ev.dFocus);
        Assert.Equal(1, ev.dPending);
        ev.Clock.Advance(0);
        Assert.Equal(new[] { K("c") }, ev.Win.FocusRequests.ToArray());
    }

    [Fact]
    public void AC6_FlagOnDesktopSlot()
    {
        var ev = S1();
        ev.E.Policy = ev.E.Policy with { FloatNewWindowsOnDesktop = true };
        Assert.True(ev.E.DesktopSlotUsable());
        var before = ev.E.Document;
        ev.Mark();
        ev.E.WindowAdded(ev.W("c"));
        Assert.Equal(before, ev.E.Document);
        Assert.Empty(ev.Batches());
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(0, ev.dPending);
        Assert.NotNull(ev.E.Window(K("c")));
        Assert.Null(ev.Target("c"));
    }

    [Fact]
    public void AC6_NarrowAreaTiles()
    {
        var ev = S1(new EnginePolicy { FloatNewWindowsOnDesktop = true }, new Rect(0, 25, 900, 875));
        Assert.False(ev.E.DesktopSlotUsable());
        var before = ev.E.Document;
        ev.E.WindowAdded(ev.W("c"));
        Assert.NotEqual(before, ev.E.Document);
        Assert.Contains("c", ev.PanelKeys());
    }

    [Fact]
    public void AC6_FlagNotAppliedAtStart()
    {
        var ev = new Ev(policy: new EnginePolicy { FloatNewWindowsOnDesktop = true });
        ev.E.Start(new[] { ev.W("a"), ev.W("b") });
        Assert.Equal(DocAb(), ev.E.Document);
    }

    [Fact]
    public void AC6_DefaultsAndRelayoutTrigger()
    {
        var p = EnginePolicy.Default;
        Assert.False(p.FloatNewWindowsOnDesktop);
        Assert.False(EnginePolicy.RequiresRelayout(p, p with { FloatNewWindowsOnDesktop = true }));
    }

    [Fact]
    public void AC7_NonDockableNewWindowIsLeftAlone()
    {
        var ev = S1();
        var before = ev.E.Document;
        ev.Mark();
        ev.E.WindowAdded(ev.W("d", false));
        Assert.Equal(before, ev.E.Document);
        Assert.Empty(ev.Batches());
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(0, ev.dPending);
        Assert.Equal(0, ev.dFocus);
    }

    [Fact]
    public void AC7_RootNullFloats()
    {
        var ev = S1();
        ev.E.SetLayout(new LayoutDocument(null), null);
        ev.Mark();
        ev.E.WindowAdded(ev.W("d"));
        Assert.Equal(new LayoutDocument(null), ev.E.Document);
        Assert.Empty(ev.Batches());
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(0, ev.dPending);
    }

    [Fact]
    [Trait("Assumption", "A10")]
    public void AC8_FramedSubNode()
    {
        var ev = S1();
        ev.E.SetLayout(ev.E.Document, "n2");
        ev.AtTime(100.7);
        ev.E.WindowFocused(K("a"));
        ev.Mark();
        ev.E.WindowAdded(ev.W("c"));
        Assert.Equal(Doc(X("n1", H(D), H(Y("n2", H(X("n3", H(P("a")), H(P("c")))), H(P("b")))))), ev.E.Document);
        Assert.Equal("n2", ev.E.FramedNodeId);
        Assert.Equal(new List<(string, Rect)>
        {
            ("a", R(10, 35, 705, 423)), ("b", R(10, 468, 1420, 422)), ("c", R(725, 35, 705, 423)),
        }, ev.OneBatch());
    }

    [Fact]
    public void AC9_DuplicateAdd()
    {
        var ev = S1();
        var before = ev.E.Document;
        ev.Mark();
        ev.E.WindowAdded(ev.W("a"));
        Assert.Equal(before, ev.E.Document);
        Assert.Empty(ev.Batches());
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(0, ev.dPending);
    }

    [Fact]
    [Trait("Assumption", "A1")]
    public void AC9_HiddenPanelIsDroppedAndWindowTiled()
    {
        var ev = S1();
        ev.E.WindowAdded(ev.W("c", false));
        var d = ev.E.Document;
        ev.E.SetLayout(new LayoutDocument(d.Root, null,
            new[] { new HiddenPanel(PanelNode.ForWindow("c"), new EngineShellSpec.SpecRestore("x")) }), null);
        ev.E.WindowAdded(ev.W("c"));
        Assert.Empty(ev.E.Document.Hidden);
        Assert.Contains("c", ev.PanelKeys());
    }

    [Fact]
    [Trait("Assumption", "A1")]
    public void AC9_RememberedSpotIsIgnored()
    {
        LayoutDocument Run(bool withSpot)
        {
            var ev = S1();
            ev.AtTime(100.7);
            ev.E.WindowFocused(K("b"));
            if (withSpot) ev.E.SetRememberedSpot(K("a"), new DockSpot(new EngineShellSpec.SpecRestore("x"), new[] { "b" }));
            ev.E.WindowRemoved(K("a"), true);
            ev.E.WindowAdded(ev.W("a"));
            return ev.E.Document;
        }

        Assert.Equal(Run(false), Run(true));
    }
}

public sealed class RemoveTests
{
    private static Ev Ac10State()
    {
        var ev = Ev.Ac5();
        ev.Clock.Advance(0);
        ev.Win.Pump();
        ev.Mark();
        return ev;
    }

    [Fact]
    public void AC10_Close()
    {
        var ev = Ac10State();
        Assert.Equal(K("b"), ev.E.FocusedKey);
        ev.E.WindowRemoved(K("b"), false);
        Assert.Equal(Doc(X("n1", H(D), H(Y("n2", H(P("a")), H(P("c")))))), ev.E.Document);
        Assert.Equal(new List<(string, Rect)> { ("c", R(725, 468, 705, 422)) }, ev.OneBatch());
        Assert.Null(ev.E.FocusedKey);
        Near(ev.Clock.Now + 1.0, ev.E.CloseQuietUntil);
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(1, ev.dChanges);
        Assert.False(ev.Cap.IsStarted(K("b")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AC11_SharingVersusAdjacent(bool share)
    {
        var ev = new Ev(policy: EnginePolicy.Default with { ShareRemovedSpace = share });
        ev.E.Start(new[] { ev.W("a"), ev.W("b"), ev.W("c") });
        double t = 1.0 / 3;
        ev.E.SetLayout(Doc(Y("t", H(X("s", (P("a"), t), (P("b"), t), (P("c"), t))), H(D))), null);
        ev.Land();
        ev.Mark();
        ev.E.WindowRemoved(K("b"), false);
        var s = (SplitNode)((SplitNode)ev.E.Document.Root!).Children[0];
        Assert.Equal("s", s.Id);
        if (share)
        {
            Near(.5, s.Weights[0]);
            Near(.5, s.Weights[1]);
            Assert.Equal(new List<(string, Rect)> { ("a", R(10, 35, 705, 423)), ("c", R(725, 35, 705, 423)) }, ev.OneBatch());
        }
        else
        {
            Near(1.0 / 3, s.Weights[0]);
            Near(2.0 / 3, s.Weights[1]);
            Assert.Equal(new List<(string, Rect)> { ("a", R(10, 35, 467, 423)), ("c", R(487, 35, 943, 423)) }, ev.OneBatch());
        }
    }

    [Fact]
    [Trait("Assumption", "A1")]
    public void AC12_TemporaryRemovalMinimum()
    {
        var ev = S1();
        ev.E.WindowRemoved(K("a"), true);
        Assert.Equal(Doc(X("n1", H(D), H(P("b")))), ev.E.Document);
        Assert.Empty(ev.E.Document.Hidden);
        Assert.Equal(new Dictionary<WindowKey, DockSpot?> { [K("a")] = null }, ev.E.Away.ToDictionary(k => k.Key, k => k.Value));
        Assert.Equal(new List<(string, Rect)> { ("b", R(725, 35, 705, 855)) }, ev.OneBatch());
        Assert.Null(ev.E.FocusedKey);
        Assert.Equal(1, ev.dSaves);
        ev.Win.Pump();
        ev.AtTime(100.7);
        ev.E.WindowFocused(K("b"));
        ev.Mark();
        ev.E.WindowAdded(ev.W("a"));
        Assert.Empty(ev.E.Away);
        Assert.Equal(Doc(X("n1", H(D), H(Y("n3", H(P("b")), H(P("a")))))), ev.E.Document);
        Assert.Equal(new List<(string, Rect)> { ("b", R(725, 35, 705, 423)), ("a", R(725, 468, 705, 422)) }, ev.OneBatch());
        ev.Clock.Advance(0);
        Assert.Equal(K("a"), ev.Win.FocusRequests[^1]);
    }

    [Fact]
    [Trait("Assumption", "A6")]
    public void AC13_NavigatorSyncOnRemoval()
    {
        var ev = S1();
        ev.E.SetLayout(ev.E.Document, "n2");
        ev.E.WindowRemoved(K("b"), false);
        Assert.Equal("a", ev.E.FramedNodeId);
        Assert.Equal(new[] { "a" }, ev.E.FramedLeaves.ToArray());
        Assert.Empty(ev.E.FramingHistory);

        ev = S1();
        ev.E.SetLayout(ev.E.Document, "b");
        ev.E.WindowRemoved(K("b"), false);
        Assert.Null(ev.E.FramedNodeId);

        ev = S1();
        ev.E.SetLayout(ev.E.Document, "n1");
        ev.E.WindowRemoved(K("b"), false);
        Assert.Equal("n1", ev.E.FramedNodeId);
    }

    [Fact]
    public void AC13_RemovingWindowWithoutPanel()
    {
        var ev = S1();
        var before = ev.E.Document;
        ev.Mark();
        ev.E.WindowRemoved(K("n"), false);
        Assert.Equal(before, ev.E.Document);
        Assert.Empty(ev.Batches());
        Assert.Equal(1, ev.dSaves);
        Near(ev.Clock.Now + 1, ev.E.CloseQuietUntil);
    }

    [Fact]
    public void AC13_BeforeStartCloseQuietUntouched()
    {
        var ev = new Ev();
        ev.E.WindowAdded(ev.W("w"));
        ev.E.WindowRemoved(K("w"), false);
        Assert.Equal(double.NegativeInfinity, ev.E.CloseQuietUntil);
    }

    [Fact]
    [Trait("Assumption", "A18")]
    public void A18_ReleaseRemovesFromLayoutAndCommits()
    {
        var ev = S1();
        ev.Mark();
        ev.E.Release(K("b"));
        Assert.DoesNotContain("b", ev.PanelKeys());
        Assert.Equal(1, ev.dSaves);
        Near(ev.Clock.Now + 1, ev.E.CloseQuietUntil);
    }

    [Fact]
    public void B20_ReleaseWhileSuspendedWritesNothing()
    {
        var ev = S1();
        ev.E.Suspend();
        ev.Mark();
        ev.E.Release(K("b"));
        Assert.DoesNotContain("b", ev.PanelKeys());
        Assert.Equal(1, ev.dSaves);
        Assert.Equal(1, ev.dPlans);
        // parked-window write-back is not a layout write; no layout batch is issued
        Assert.DoesNotContain(ev.Win.Writes.Skip(ev.Win.Writes.Count - ev.dWrites), w => w.Key.Value == "a");
    }
}
