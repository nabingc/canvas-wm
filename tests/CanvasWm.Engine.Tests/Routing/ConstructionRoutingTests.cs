using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.RoutingSpec;

public sealed class ConstructionRoutingTests
{
    [Fact]
    public void AC7_Construction()
    {
        var t = new Rt();
        Assert.Empty(t.FactoryCalls);
        Assert.Equal(0, t.Clock.PendingCount);
        Assert.Empty(t.Router.Engines);
        Assert.Equal(new[] { Rt.A.Frame, Rt.B.Frame }, t.Router.DisplayFrames);
        Assert.Equal("default", t.Router.DesktopOf(Rt.KA));
        Assert.Equal("default", t.Router.DesktopOf(Rt.KB));
        Assert.Null(t.Router.DesktopOf(new DisplayKey("zzz")));
        Assert.False(t.Router.IsReady);
        Assert.False(t.Router.IsTornDown);
        Assert.False(t.Router.IsShuttingDown);
        Assert.False(t.Router.IsShutdownComplete);
        Assert.False(t.Router.FramesNeeded);
        Assert.True(t.Router.StepTowardPointer);
        Assert.Null(t.Router.LastFocusedKey);
        Assert.Null(t.Router.LastActiveProcessId);
        Assert.Same(RouterOptions.Default, t.Router.Options);
    }

    [Fact]
    public void AC7_NullArgumentsAndDuplicates()
    {
        var t = new Rt();
        Func<EngineKey, DisplayInfo, DisplayEngine> f = (k, d) => t.Build(k, d);
        var list = new[] { Rt.A };
        Assert.Throws<ArgumentNullException>(() => new EngineRouter(null!, f, t.Clock, t.Win));
        Assert.Throws<ArgumentNullException>(() => new EngineRouter(list, null!, t.Clock, t.Win));
        Assert.Throws<ArgumentNullException>(() => new EngineRouter(list, f, null!, t.Win));
        Assert.Throws<ArgumentNullException>(() => new EngineRouter(list, f, t.Clock, null!));
        Assert.Throws<ArgumentException>(() => new EngineRouter(new[] { Rt.A, Rt.A }, f, t.Clock, t.Win));
    }

    [Fact]
    public void AC7_OptionsPassedThrough()
    {
        var o = RouterOptions.Default with { ReshowDelaySeconds = 3 };
        var t = new Rt(options: o);
        Assert.Same(o, t.Router.Options);
    }

    [Fact]
    public void AC8_ReadyStartsEnginesLazily()
    {
        var t = new Rt();
        t.Ready();
        Assert.Equal(new[] { (Rt.EA, Rt.A), (Rt.EB, Rt.B) }, t.FactoryCalls);
        var a = t.EA_;
        var b = t.EB_;
        Assert.True(a.IsStarted);
        Assert.True(b.IsStarted);
        Assert.Equal(new[] { Rt.K("w1"), Rt.K("w3") }, a.Windows.Select(w => w.Info.Key).ToArray());
        Assert.Equal(new[] { Rt.K("w2") }, b.Windows.Select(w => w.Info.Key).ToArray());
        Assert.Equal(Rt.A.Frame, a.DisplayFrame);
        Assert.Equal(Rt.B.Frame, b.DisplayFrame);
        Assert.Equal(new[] { Rt.B.Frame }, a.OtherDisplays);
        Assert.Equal(new[] { Rt.A.Frame }, b.OtherDisplays);
        Assert.Equal(1, t.StartedCount);
        Assert.True(t.ChangedCount >= 2);
        Assert.True(t.Router.IsReady);
    }

    [Fact]
    public void AC8_SecondReadyIgnored()
    {
        var t = new Rt();
        t.Ready();
        int calls = t.FactoryCalls.Count, changed = t.ChangedCount;
        t.Router.Ready(new[] { Rt.W("w9", 5, Rt.R(1, 1, 5, 5)) });
        Assert.Equal(calls, t.FactoryCalls.Count);
        Assert.Equal(changed, t.ChangedCount);
        Assert.Equal(1, t.StartedCount);
        Assert.False(t.EA_.Holds(Rt.K("w9")));
    }

    [Fact]
    public void AC8_ActivationBeforeReadyReachesNewEngines()
    {
        var t = new Rt();
        t.Router.AppActivated(100, true);
        t.Ready();
        Assert.Equal(100, t.EA_.ActiveProcessId);
        Assert.Equal(100, t.EB_.ActiveProcessId);
        Assert.True(t.EA_.DesktopOwnerActive);
        Assert.True(t.EB_.DesktopOwnerActive);
    }

    [Fact]
    public void AC8_FactoryWrongKeyThrows()
    {
        var t = new Rt();
        t.FactoryOverride = (k, d) => t.Build(new EngineKey(k.Display, "other"), d);
        Assert.Throws<InvalidOperationException>(() => t.Ready());
        Assert.Empty(t.Router.Engines);
    }

    [Fact]
    public void AC8_FactoryNullThrows()
    {
        var t = new Rt();
        t.FactoryOverride = (_, _) => null;
        Assert.Throws<InvalidOperationException>(() => t.Ready());
    }

    // ---- AC9 owner display ----

    private static DisplayEngine? OwnerOf(Rt t, string name, Rect frame)
    {
        t.Router.WindowAdded(Rt.W(name, 9, frame));
        return t.Router.Engines.FirstOrDefault(e => e.Holds(Rt.K(name)));
    }

    [Fact]
    public void AC9_LargestOverlapWins()
    {
        var t = new Rt();
        t.Ready();
        // (1800,100,400,300): x 1800..2200 -> 120 on A, 280 on B
        Assert.Same(t.EB_, OwnerOf(t, "x", Rt.R(1800, 100, 400, 300)));
    }

    [Fact]
    public void AC9_TieGoesToLowerIndex()
    {
        var t = new Rt();
        t.Ready();
        // x 1720..2120 : 200 on A, 200 on B
        Assert.Same(t.EA_, OwnerOf(t, "x", Rt.R(1720, 100, 400, 300)));
    }

    [Fact]
    [Trait("Assumption", "A6")]
    public void AC9_NoOverlapGoesToMain()
    {
        var t = new Rt();
        t.Ready();
        Assert.Same(t.EA_, OwnerOf(t, "x", Rt.R(9000, 9000, 100, 100)));
    }

    [Fact]
    public void AC9_NoOverlapMainListedSecond()
    {
        var bMain = Rt.B with { IsMain = true };
        var aNot = Rt.A with { IsMain = false };
        var t = new Rt(new[] { aNot, bMain });
        t.Ready();
        Assert.Same(t.EB_, OwnerOf(t, "x", Rt.R(9000, 9000, 100, 100)));
    }

    [Fact]
    public void AC9_HolderFirst()
    {
        var t = new Rt();
        t.Ready();
        // w1 is held by A; announce it again with a frame on B
        t.Router.WindowAdded(Rt.W("w1", 100, Rt.R(2000, 100, 300, 300)));
        Assert.True(t.EA_.Holds(Rt.K("w1")));
        Assert.False(t.EB_.Holds(Rt.K("w1")));
    }

    // ---- AC10 per-window routing ----

    [Fact]
    public void AC10_PerWindowRouting()
    {
        var t = new Rt();
        t.Ready();
        var r = t.Router;
        var w4 = Rt.W("w4", 400, Rt.R(2100, 200, 300, 300));
        t.Win.AddWindow(w4.Key, w4.Frame);
        r.WindowAdded(w4);
        Assert.True(t.EB_.Holds(w4.Key));
        Assert.False(t.EA_.Holds(w4.Key));

        var moved = Rt.R(2100, 150, 700, 400);
        r.WindowMoved(Rt.K("w2"), moved);
        Assert.Equal(moved, t.EB_.Window(Rt.K("w2"))!.Info.Frame);

        r.WindowTitleChanged(Rt.K("w2"), "t2");
        Assert.Equal("t2", t.EB_.Window(Rt.K("w2"))!.Info.Title);

        r.WindowBecameDockable(Rt.K("w3"));
        Assert.True(t.EA_.Window(Rt.K("w3"))!.Info.Dockable);

        r.WindowFocused(Rt.K("w2"));
        Assert.Equal(Rt.K("w2"), t.EB_.FocusedKey);
        Assert.Equal(Rt.K("w2"), r.LastFocusedKey);

        Assert.True(r.Holds(Rt.K("w2")));
        r.WindowRemoved(Rt.K("w2"), false);
        Assert.False(t.EB_.Holds(Rt.K("w2")));
        Assert.Null(r.LastFocusedKey);
        Assert.False(r.Holds(Rt.K("w2")));

        Assert.True(r.Holds(Rt.K("w1")));
        r.Release(Rt.K("w1"));
        Assert.False(t.EA_.Holds(Rt.K("w1")));
        Assert.False(r.Holds(Rt.K("w1")));
    }

    [Fact]
    public void AC10_UnknownKeysChangeNothing()
    {
        var t = new Rt();
        t.Ready();
        var before = t.Router.Engines.Select(e => e.Windows.Select(w => w.Info.Key.Value).ToArray()).ToArray();
        var u = Rt.K("nope");
        t.Router.WindowMoved(u, Rt.R(1, 1, 1, 1));
        t.Router.WindowTitleChanged(u, "x");
        t.Router.WindowBecameDockable(u);
        t.Router.WindowRemoved(u, true);
        t.Router.Release(u);
        t.Router.WindowFocused(u);
        var after = t.Router.Engines.Select(e => e.Windows.Select(w => w.Info.Key.Value).ToArray()).ToArray();
        Assert.Equal(before, after);
        Assert.Null(t.EA_.FocusedKey);
        Assert.Null(t.EB_.FocusedKey);
        Assert.False(t.Router.Holds(u));
    }

    [Fact]
    public void AC10_NullArgumentsThrow()
    {
        var t = new Rt();
        Assert.Throws<ArgumentNullException>(() => t.Router.WindowAdded(null!));
        Assert.Throws<ArgumentNullException>(() => t.Router.WindowFocused(null!));
        Assert.Throws<ArgumentNullException>(() => t.Router.Ready(null!));
        Assert.Throws<ArgumentNullException>(() => t.Router.DisplaysChanged(null!));
    }

    // ---- AC11 broadcasts ----

    [Fact]
    [Trait("Assumption", "A8")]
    public void AC11_Broadcasts()
    {
        var t = new Rt();
        t.Ready();
        t.Router.AppActivated(200, false);
        Assert.Equal(200, t.EA_.ActiveProcessId);
        Assert.Equal(200, t.EB_.ActiveProcessId);
        Assert.Equal(200, t.Router.LastActiveProcessId);

        t.Router.WindowFocused(Rt.K("w1"));
        t.Router.WindowFocused(Rt.K("w2"));
        Assert.Equal(Rt.K("w1"), t.EA_.FocusedKey);
        Assert.Equal(Rt.K("w2"), t.EB_.FocusedKey);
        t.Router.FocusLeftWindows(100);
        Assert.Null(t.EA_.FocusedKey);
        Assert.Null(t.EB_.FocusedKey);
        Assert.Equal(Rt.K("w2"), t.Router.LastFocusedKey);
    }

    [Fact]
    public void AC11_BroadcastBeforeReadyRecordsState()
    {
        var t = new Rt();
        t.Router.WindowFocused(Rt.K("w1"));
        t.Router.AppActivated(5, true);
        t.Router.FocusLeftWindows(5);
        Assert.Equal(Rt.K("w1"), t.Router.LastFocusedKey);
        Assert.Equal(5, t.Router.LastActiveProcessId);
        Assert.Empty(t.FactoryCalls);
    }
}
