using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.RoutingSpec;

internal sealed class LoadLoggingStore : IDisplayStore
{
    private readonly IDisplayStore _inner;
    public readonly List<EngineKey> Loads = new();

    public LoadLoggingStore(IDisplayStore inner) => _inner = inner;

    public SavedDisplay? Load(EngineKey key)
    {
        Loads.Add(key);
        return _inner.Load(key);
    }

    public void Save(EngineKey key, SavedDisplay display) => _inner.Save(key, display);
}

public sealed class FramesDisplayTests
{
    private static void FramesOn(DisplayEngine e)
    {
        e.MotionActive = true;
        e.SetFramesRunning(true);
    }

    private static void FramesOff(DisplayEngine e)
    {
        e.MotionActive = false;
        e.SetFramesRunning(false);
    }

    private static Rt Pumped()
    {
        var t = new Rt();
        t.Ready();
        t.Win.Pump();
        t.Clock.Advance(0.5);
        t.Win.Pump();
        return t;
    }

    // ---- AC17 ----

    [Fact]
    public void AC17_Aggregation()
    {
        var t = Pumped();
        FramesOn(t.EA_);
        Assert.Equal(new[] { true }, t.FramesEvents);
        Assert.True(t.Router.FramesNeeded);
        FramesOn(t.EB_);
        Assert.Equal(new[] { true }, t.FramesEvents);
        FramesOff(t.EA_);
        Assert.Equal(new[] { true }, t.FramesEvents);
        Assert.True(t.Router.FramesNeeded);
        FramesOff(t.EB_);
        Assert.Equal(new[] { true, false }, t.FramesEvents);
        Assert.False(t.Router.FramesNeeded);
    }

    [Fact]
    [Trait("Assumption", "A10")]
    public void AC17_ChangedSnapshotStopsFramesAtOnce()
    {
        var t = Pumped();
        FramesOn(t.EA_);
        t.FramesEvents.Clear();
        t.Router.DisplaysChanged(new[] { Rt.A with { Frame = new Rect(0, 0, 1900, 1080) }, Rt.B });
        Assert.Equal(new[] { false }, t.FramesEvents);
        Assert.False(t.Router.FramesNeeded);
        t.Clock.Advance(2.3);
        Assert.False(t.Router.IsTornDown);
        Assert.Equal(new[] { false }, t.FramesEvents);
        Assert.False(t.Router.FramesNeeded);
    }

    // ---- AC18 ----

    [Fact]
    public void AC18_TickReachesOnlyRunningEngines()
    {
        var t = Pumped();
        FramesOn(t.EA_);
        double bBefore = t.EB_.LastTick;
        t.Router.Tick(5.0);
        Assert.Equal(5.0, t.EA_.LastTick, 9);
        Assert.Equal(bBefore, t.EB_.LastTick);
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Router.Tick(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => t.Router.Tick(double.PositiveInfinity));
    }

    [Fact]
    public void AC18_NoTickWhileTornDownOrAfterShutdown()
    {
        var t = Pumped();
        FramesOn(t.EA_);
        double before = t.EA_.LastTick;
        t.Router.DisplaysChanged(new[] { Rt.A });
        Assert.True(t.Router.IsTornDown);
        t.Router.Tick(7.0);
        Assert.Equal(before, t.EA_.LastTick);

        var u = Pumped();
        FramesOn(u.EA_);
        double b2 = u.EA_.LastTick;
        u.Router.BeginShutdown();
        u.Router.Tick(9.0);
        Assert.Equal(b2, u.EA_.LastTick);
    }

    // ---- AC19 ----

    [Fact]
    [Trait("Assumption", "A3")]
    public void AC19_UnchangedPathRefreshesWorkAreas()
    {
        var t = Pumped();
        t.Clock.Advance(20 - t.Clock.Now);
        var a2 = Rt.A with { WorkArea = new Rect(0, 0, 1920, 1000) };
        t.Router.DisplaysChanged(new[] { a2, Rt.B });
        Assert.False(t.Router.IsTornDown);
        int calls = t.FactoryCalls.Count, writes = t.Win.Writes.Count;
        t.Clock.Advance(0.3);
        t.Router.DisplaysChanged(new[] { a2, Rt.B });
        t.Clock.Advance(0.1);   // 20.4
        Assert.Equal(Rt.A.WorkArea, t.EA_.VisibleArea);
        t.Clock.Advance(0.299);   // 20.699
        Assert.Equal(Rt.A.WorkArea, t.EA_.VisibleArea);
        Assert.Equal(Rt.A.WorkArea, t.Router.Displays[0].WorkArea);
        t.Clock.Advance(0.001);   // 20.7
        Assert.Equal(new Rect(0, 0, 1920, 1000), t.EA_.VisibleArea);
        Assert.Equal(new Rect(0, 0, 1920, 1000), t.Router.Displays[0].WorkArea);
        Assert.Equal(Rt.B.WorkArea, t.EB_.VisibleArea);
        Assert.Equal(calls, t.FactoryCalls.Count);
        // SPEC PROBLEM (reported): AC19 says "no new window-system write", but engine B26 makes
        // VisibleAreaChanged reframe, so A's own two windows are rewritten (once, A only). The
        // router itself adds nothing: no write for B's window, and no second reframe from the
        // duplicate notification.
        var added = t.Win.Writes.Skip(writes).Select(w => w.Key.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "w1", "w3" }, added);
    }

    [Fact]
    public void AC19_NoWriteBeforeRefreshDue()
    {
        var t = Pumped();
        t.Clock.Advance(20 - t.Clock.Now);
        int writes = t.Win.Writes.Count;
        t.Router.DisplaysChanged(new[] { Rt.A with { WorkArea = new Rect(0, 0, 1920, 1000) }, Rt.B });
        t.Clock.Advance(0.399);
        Assert.Equal(writes, t.Win.Writes.Count);
        Assert.Equal(Rt.A.WorkArea, t.EA_.VisibleArea);
    }

    // ---- AC20 ----

    [Fact]
    public void AC20_ChangedPathUnplugDisplay()
    {
        var mem = new List<LoadLoggingStore>();
        var t = new Rt(wrapStore: s => { var l = new LoadLoggingStore(s); mem.Add(l); return l; });
        t.Ready();   // the start landing is deliberately not pumped: windows keep their original OS frames
        t.Clock.Advance(10 - t.Clock.Now);
        var a1 = Rt.A with { WorkArea = new Rect(0, 0, 1920, 1000) };
        t.Win.Screens = new[] { Rt.A.Frame };
        t.Router.DisplaysChanged(new[] { a1 });
        Assert.True(t.Router.IsTornDown);
        Assert.False(t.Router.FramesNeeded);
        t.Clock.Advance(0.5);
        t.Router.DisplaysChanged(new[] { a1 });
        var oldA = t.EA_;
        var oldB = t.EB_;
        int changed = t.ChangedCount;
        int w0 = t.Win.Writes.Count;
        t.Clock.Advance(1.0);   // 11.5
        Assert.Equal(w0, t.Win.Writes.Count);
        Assert.Equal(2, t.Router.Engines.Count);
        t.Clock.Advance(0.5);   // 12.0
        var added = t.Win.Writes.Skip(w0).ToList();
        Assert.Equal(2, added.Count);
        Assert.Equal(Rt.K("w3"), added[0].Key);
        Assert.Equal(ReleaseGeometry.Cascade(new Rect(0, 0, 1920, 1040), new Size(800, 600), 0), added[0].Target);
        Assert.Equal(Rt.K("w2"), added[1].Key);
        Assert.Equal(ReleaseGeometry.Cascade(new Rect(0, 0, 1920, 1000), new Size(800, 500), 0), added[1].Target);
        Assert.Equal(new[] { oldA, oldB }, t.Retired.ToArray());
        Assert.Empty(t.Router.Engines);
        Assert.True(t.ChangedCount > changed);
        Assert.Equal(new[] { Rt.A.Frame }, t.Router.DisplayFrames);

        // retired engines no longer reach router events
        Assert.Null(oldA.OnChange);
        Assert.Null(oldA.OnNeedsFrames);
        Assert.Null(oldA.Log);
        int ch = t.ChangedCount, fe = t.FramesEvents.Count;
        oldA.MotionActive = true;
        oldA.SetFramesRunning(true);
        Assert.Equal(ch, t.ChangedCount);
        Assert.Equal(fe, t.FramesEvents.Count);

        // gap: mirror only, Execute null
        t.Clock.Advance(0.3);
        var w4 = Rt.W("w4", 400, Rt.R(200, 200, 300, 300));
        t.Win.AddWindow(w4.Key, w4.Frame);
        t.Router.WindowAdded(w4);
        Assert.Empty(t.Router.Engines);
        Assert.Null(t.Router.Execute(CanvasWm.Core.Shortcuts.ShortcutCommand.ForAction(CanvasWm.Core.Shortcuts.ShortcutAction.Right)));
        int calls = t.FactoryCalls.Count;
        t.Clock.Advance(0.49);   // 12.79
        Assert.Equal(calls, t.FactoryCalls.Count);
        Assert.True(t.Router.IsTornDown);
        t.Clock.Advance(0.01);   // 12.8
        Assert.Equal(calls + 1, t.FactoryCalls.Count);
        Assert.Equal((Rt.EA, a1), t.FactoryCalls[^1]);
        Assert.False(t.Router.IsTornDown);
        var na = t.Router.EngineFor(Rt.KA)!;
        Assert.True(na.IsStarted);
        Assert.NotSame(oldA, na);
        Assert.Equal(new[] { "w1", "w2", "w3", "w4" },
            na.Windows.Select(w => w.Info.Key.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(2, mem[0].Loads.Count(k => k == Rt.EA));
    }

    // ---- AC21 ----

    [Fact]
    public void AC21_DpiOnlyChangeRebuilds()
    {
        var t = new Rt().Ready();   // landing not pumped: w3 keeps its off-screen frame
        t.EB_.SetParked(Rt.K("w2"), true);
        var a = t.EA_;
        var b = t.EB_;
        int w0 = t.Win.Writes.Count;
        t.Clock.Advance(10 - t.Clock.Now);
        t.Router.DisplaysChanged(new[] { Rt.A, Rt.B with { Dpi = 168 } });
        Assert.True(t.Router.IsTornDown);
        t.Clock.Advance(1.499);
        Assert.Equal(w0, t.Win.Writes.Count);
        t.Clock.Advance(0.001);
        var added = t.Win.Writes.Skip(w0).ToList();
        Assert.Equal(2, added.Count);
        Assert.Equal(ReleaseGeometry.Cascade(Rt.A.WorkArea, new Size(800, 600), 0), added.Single(x => x.Key == Rt.K("w3")).Target);
        Assert.Equal(ReleaseGeometry.Cascade(Rt.B.WorkArea, new Size(800, 500), 0), added.Single(x => x.Key == Rt.K("w2")).Target);
        Assert.Equal(new[] { a, b }, t.Retired.ToArray());
        Assert.Empty(t.Router.Engines);
        t.Clock.Advance(0.8);
        Assert.Equal(4, t.FactoryCalls.Count);
        Assert.Equal(2, t.Router.Engines.Count);
        Assert.NotSame(a, t.EA_);
        Assert.NotSame(b, t.EB_);
        Assert.Equal(168, t.Router.Displays[1].Dpi);
    }

    // ---- AC22 ----

    [Fact]
    public void AC22_ChangedThenUnchangedLooking()
    {
        var t = Pumped();
        t.Clock.Advance(40 - t.Clock.Now);
        t.Router.DisplaysChanged(new[] { Rt.A with { WorkArea = new Rect(0, 0, 1920, 1000) }, Rt.B });
        t.Clock.Advance(0.2);
        t.Router.DisplaysChanged(new[] { Rt.A, Rt.B with { Dpi = 120 } });
        t.Clock.Advance(0.2);   // 40.4: refresh would have run
        Assert.Equal(Rt.A.WorkArea, t.EA_.VisibleArea);
        Assert.Equal(2, t.Router.Engines.Count);
        t.Clock.Advance(1.29);  // 41.69
        Assert.Equal(2, t.Router.Engines.Count);
        t.Clock.Advance(0.01);  // 41.7
        Assert.Empty(t.Router.Engines);
        Assert.Equal(2, t.Retired.Count);
    }

    [Fact]
    public void AC22_OriginalSetWhileTornDownStillRebuilds()
    {
        var t = new Rt().Ready();
        t.EB_.SetParked(Rt.K("w2"), true);
        t.Clock.Advance(30 - t.Clock.Now);
        t.Router.DisplaysChanged(new[] { Rt.A });
        t.Clock.Advance(0.5);
        int w0 = t.Win.Writes.Count;
        t.Router.DisplaysChanged(new[] { Rt.A, Rt.B });   // the original set again
        Assert.True(t.Router.IsTornDown);
        t.Clock.Advance(1.499);
        Assert.Equal(2, t.Router.Engines.Count);
        t.Clock.Advance(0.001);   // 32.0
        Assert.Empty(t.Router.Engines);
        var added = t.Win.Writes.Skip(w0).ToList();
        // both displays present -> both release into their own areas
        Assert.Equal(ReleaseGeometry.Cascade(Rt.B.WorkArea, new Size(800, 500), 0), added.Single(x => x.Key == Rt.K("w2")).Target);
        Assert.True(t.Router.IsTornDown);
        t.Clock.Advance(0.799);
        Assert.True(t.Router.IsTornDown);
        t.Clock.Advance(0.001);   // 32.8
        Assert.False(t.Router.IsTornDown);
        Assert.Equal(2, t.Router.Engines.Count);
    }

    [Fact]
    [Trait("Assumption", "A2")]
    public void AC22_ThreeSnapshotsOneRebuildWithLast()
    {
        var t = Pumped();
        t.Clock.Advance(0 - 0);
        double t0 = t.Clock.Now;
        t.Router.DisplaysChanged(new[] { Rt.A with { Frame = new Rect(0, 0, 1900, 1080) }, Rt.B });
        t.Clock.Advance(0.5);
        t.Router.DisplaysChanged(new[] { Rt.A with { Frame = new Rect(0, 0, 1910, 1080) }, Rt.B });
        t.Clock.Advance(0.5);
        t.Router.DisplaysChanged(new[] { Rt.A with { Frame = new Rect(0, 0, 1922, 1080) }, Rt.B });
        t.Clock.Advance(1.499);   // t0 + 2.499
        Assert.Equal(2, t.Router.Engines.Count);
        Assert.Empty(t.Retired);
        t.Clock.Advance(0.001);   // t0 + 2.5
        Assert.Equal(2, t.Retired.Count);
        Assert.Equal(1922, t.Router.Displays[0].Frame.Width);
        t.Clock.Advance(0.8);
        t.Clock.Advance(5);
        Assert.Equal(2, t.Retired.Count);
        Assert.Equal(4, t.FactoryCalls.Count);
        Assert.Equal(1922, t.FactoryCalls[2].Display.Frame.Width);
        Assert.True(t.Clock.Now >= t0 + 2.5);
    }

    // ---- AC23 ----

    [Fact]
    [Trait("Assumption", "A11")]
    public void AC23_ReadyWhileTornDownIsDeferred()
    {
        var t = new Rt();
        t.Clock.Advance(1);
        t.Router.DisplaysChanged(new[] { Rt.A with { Frame = new Rect(0, 0, 1900, 1080) }, Rt.B });
        t.Clock.Advance(0.2);
        t.Ready();
        Assert.True(t.Router.IsReady);
        Assert.Empty(t.FactoryCalls);
        Assert.Equal(0, t.StartedCount);
        t.Clock.Advance(1.3);   // 2.5 rebuild
        Assert.Empty(t.FactoryCalls);
        t.Clock.Advance(0.79);  // 3.29
        Assert.Empty(t.FactoryCalls);
        Assert.Equal(0, t.StartedCount);
        t.Clock.Advance(0.01);  // 3.3
        Assert.Equal(2, t.FactoryCalls.Count);
        Assert.Equal(1, t.StartedCount);
        Assert.Equal(1900, t.FactoryCalls[0].Display.Frame.Width);
    }

    // ---- AC24 ----

    [Fact]
    public void AC24_DesktopSeam()
    {
        var t = Pumped();
        var oldA = t.EA_;
        t.Router.DesktopShown(Rt.KA, "d2");
        Assert.True(oldA.IsSuspended);
        var d2 = t.Router.EngineFor(Rt.KA)!;
        Assert.NotSame(oldA, d2);
        Assert.Equal(new EngineKey(Rt.KA, "d2"), d2.Key);
        Assert.True(d2.IsStarted);
        Assert.Equal(new[] { "w1", "w3" }, d2.Windows.Select(w => w.Info.Key.Value).OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal("d2", t.Router.DesktopOf(Rt.KA));
        Assert.Equal(3, t.FactoryCalls.Count);

        t.Router.DesktopShown(Rt.KA, "default");
        Assert.Same(oldA, t.Router.EngineFor(Rt.KA));
        Assert.True(d2.IsSuspended);
        Assert.False(oldA.IsSuspended);
        Assert.Equal(3, t.FactoryCalls.Count);
        Assert.Equal("default", t.Router.DesktopOf(Rt.KA));

        int pending = t.Clock.PendingCount;
        t.Router.DesktopShown(Rt.KA, "default");
        Assert.Equal(3, t.FactoryCalls.Count);
        Assert.False(oldA.IsSuspended);
        t.Router.DesktopShown(new DisplayKey("zzz"), "d3");
        Assert.Equal(3, t.FactoryCalls.Count);
        Assert.Throws<ArgumentException>(() => t.Router.DesktopShown(Rt.KA, "a|b"));
        Assert.Throws<ArgumentException>(() => t.Router.DesktopShown(Rt.KA, ""));
        Assert.Throws<ArgumentNullException>(() => t.Router.DesktopShown(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => t.Router.DesktopShown(Rt.KA, null!));
        Assert.Equal(pending, t.Clock.PendingCount);
    }
}
