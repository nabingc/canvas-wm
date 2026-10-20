using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Rendering;
using CanvasWm.Engine.Services;
using static CanvasWm.Engine.Tests.EngineShellSpec.Rig;

namespace CanvasWm.Engine.Tests.EngineShellSpec;

public sealed class LifecycleTests
{
    private static Rig FramesRunningRig(params WindowInfo[] ws)
    {
        var r = new Rig().StartWith(ws);
        r.E.SetFramesRunning(true);
        r.Log.Clear();
        return r;
    }

    [Fact]
    public void AC5_ReentryIsRejected_ButOtherEnginesAllowed()
    {
        var r = FramesRunningRig(W("w1"));
        var other = new DisplayEngine(new EngineKey(new DisplayKey("D2"), "default"), VA2, EnginePolicy.Default, r.Services);
        var errors = new List<Exception?>();
        bool suspendedInside = false, runningInside = true, otherOk = false;
        r.E.OnNeedsFrames = on =>
        {
            suspendedInside = r.E.IsSuspended;
            runningInside = r.E.FramesRunning;
            void Try(Action a)
            {
                try { a(); errors.Add(null); } catch (Exception ex) { errors.Add(ex); }
            }

            Try(() => r.E.Suspend());
            Try(() => r.E.WindowAdded(W("w9")));
            Try(() => r.E.Tick(101));
            Try(() => r.E.ToggleOverview());
            Try(() => r.E.Policy = r.E.Policy with { Gap = 3 });
            other.Start(new List<WindowInfo>());
            otherOk = other.IsStarted;
        };

        r.E.Suspend();
        Assert.Equal(5, errors.Count);
        Assert.All(errors, x => Assert.IsType<InvalidOperationException>(x));
        Assert.True(suspendedInside);
        Assert.False(runningInside);
        Assert.True(otherOk);
        Assert.Null(r.E.Window(K("w9")));
        r.E.Resume(new List<WindowInfo>());
        Assert.False(r.E.IsSuspended);
    }

    private static readonly Rect VA2 = new(1440, 0, 800, 600);

    [Fact]
    public void AC6_OrderingAndThrowingHandler()
    {
        var r = FramesRunningRig(W("w1"));
        r.E.Suspend();
        Assert.Equal(new[] { "nf:False", "change" }, r.Log);

        var t = FramesRunningRig(W("w1"));
        t.E.OnNeedsFrames = _ => throw new FormatException("boom");
        Assert.Throws<FormatException>(() => t.E.Suspend());
        Assert.True(t.E.IsSuspended);
        Assert.DoesNotContain("change", t.Log);
        var ex = Record.Exception(() => t.E.Resume(new List<WindowInfo>()));
        Assert.IsNotType<InvalidOperationException>(ex);
    }

    [Fact]
    public void AC7_NoCallbacksScenario()
    {
        var r = new Rig(wire: false);
        var w1 = r.OS(W("w1", 7)); var w2 = r.OS(W("w2", 8));
        r.E.Start(new[] { w1 });
        r.E.WindowAdded(w2);
        r.E.WindowFocused(K("w1"));
        r.E.SetFramesRunning(true);
        r.E.Policy = r.E.Policy with { Gap = 11 };
        r.E.Suspend();
        r.E.Resume(new[] { w1, w2 });
        r.E.SetFramesRunning(true);
        r.E.Tick(101);
        r.E.WindowRemoved(K("w2"), true);
        r.E.AppActivated(7, false);
        r.E.ReleaseAll(null, () => { });
        r.Clock.Advance(1);
        Assert.False(r.E.IsStarted);
    }

    [Fact]
    public void AC8_Start()
    {
        var r = new Rig();
        r.E.Start(new[] { r.OS(W("w1")), r.OS(W("w2", frame: new Rect(5, 6, 7, 8))) });
        Assert.True(r.E.IsStarted);
        Assert.Equal(new[] { K("w1"), K("w2") }, r.E.Windows.Select(w => w.Info.Key));
        Assert.True(r.Cap.IsStarted(K("w1")));
        Assert.True(r.Cap.IsStarted(K("w2")));
        Assert.Single(r.Ren.Plans);
        Assert.Equal(new RenderPlan(new[]
        {
            new WindowPlan(K("w1"), new Rect(100, 100, 800, 600), false),
            new WindowPlan(K("w2"), new Rect(5, 6, 7, 8), false),
        }), r.Ren.Last);
        Assert.Equal(new[] { "change" }, r.Log);
        Assert.Equal((r.Key, new SavedDisplay(LayoutDocument.Empty)), r.Disp.SaveLog[^1]);

        var r2 = new Rig();
        var saved = new SavedDisplay(LayoutDocument.Empty, framedNodeId: "n1");
        r2.Disp.Save(r2.Key, saved);
        int before = r2.Disp.SaveLog.Count;
        r2.E.Start(new List<WindowInfo>());
        Assert.Equal(before + 1, r2.Disp.SaveLog.Count);
        Assert.Equal((r2.Key, saved), r2.Disp.SaveLog[^1]);
    }

    [Fact]
    public void AC9_StartIdempotenceAndValidation()
    {
        var r = new Rig().StartWith(W("w1"));
        r.Log.Clear();
        var snap = r.Snap();
        r.E.Start(new[] { r.OS(W("w3")) });
        Assert.Equal(snap, r.Snap());
        Assert.Empty(r.Log);
        Assert.False(r.E.Holds(K("w3")));

        var r2 = new Rig();
        r2.E.Start(new[] { W("w1", title: "A"), W("w1", title: "B") });
        Assert.Single(r2.E.Windows);
        Assert.Equal("B", r2.E.Window(K("w1"))!.Info.Title);

        var r3 = new Rig();
        r3.E.Start(new List<WindowInfo>());
        Assert.True(r3.E.IsStarted);

        var r4 = new Rig();
        var s = r4.Snap();
        Assert.Throws<ArgumentNullException>(() => r4.E.Start(null!));
        Assert.Throws<ArgumentNullException>(() => r4.E.Start(new WindowInfo[] { W("w1"), null! }));
        Assert.False(r4.E.IsStarted);
        Assert.Equal(s, r4.Snap());
    }

    [Fact]
    public void AC10_Suspend()
    {
        var r = new Rig().StartWith(W("w1"));
        r.E.MotionActive = true;
        r.E.SetFramesRunning(true);
        r.Log.Clear();
        int gen = r.E.PlacementGeneration, saves = r.Disp.SaveLog.Count, plans = r.Ren.Plans.Count;
        int writes = r.Win.Writes.Count, pend = r.Win.PendingCount;
        r.E.SetPhase(StagePhase.Moving);
        r.E.Suspend();
        Assert.True(r.E.IsSuspended);
        Assert.Equal(StagePhase.Desk, r.E.Phase);
        Assert.False(r.E.MotionActive);
        Assert.Equal(gen + 1, r.E.PlacementGeneration);
        Assert.Equal(saves + 1, r.Disp.SaveLog.Count);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);
        Assert.Equal(1, r.Log.Count(x => x == "nf:False"));
        Assert.Equal(writes, r.Win.Writes.Count);
        Assert.Equal(pend, r.Win.PendingCount);

        r.Log.Clear();
        var snap = r.Snap();
        r.E.Suspend();
        Assert.Equal(snap, r.Snap());
        Assert.Empty(r.Log);
        Assert.Equal(gen + 1, r.E.PlacementGeneration);

        var q = new Rig().StartWith(W("w1"));
        q.Log.Clear();
        q.E.Suspend();
        Assert.DoesNotContain("nf:False", q.Log);
    }

    [Fact]
    public void AC11_ResumeGuards()
    {
        var r = new Rig().StartWith(W("w1"));
        r.Log.Clear();
        var snap = r.Snap();
        r.E.Resume(new[] { W("w1"), W("w5") });
        Assert.Equal(snap, r.Snap());
        Assert.Empty(r.Log);
        Assert.False(r.E.Holds(K("w5")));

        r.E.Suspend();
        Assert.Throws<ArgumentNullException>(() => r.E.Resume(new WindowInfo[] { W("w1"), null! }));
        Assert.Throws<ArgumentNullException>(() => r.E.Resume(null!));
        Assert.True(r.E.IsSuspended);
    }

    [Fact]
    public void AC12_ResumeReconciles()
    {
        var r = new Rig().StartWith(W("w1", title: "A"), W("w2"), W("w4"));
        r.E.SetParked(K("w4"), true);
        r.E.Suspend();
        int writes = r.Win.Writes.Count, rf = r.E.ReframeCount;
        r.E.Resume(new[] { r.OS(W("w1", title: "B")), r.OS(W("w3")) });
        Assert.Equal(new[] { K("w1"), K("w4"), K("w3") }, r.E.Windows.Select(w => w.Info.Key));
        Assert.False(r.Cap.IsStarted(K("w2")));
        Assert.True(r.E.Window(K("w4"))!.Parked);
        Assert.DoesNotContain(r.Win.Writes.Skip(writes), x => x.Key == K("w4"));
        Assert.True(r.Cap.IsStarted(K("w3")));
        Assert.Equal("B", r.E.Window(K("w1"))!.Info.Title);
        Assert.Equal(rf + 1, r.E.ReframeCount);
        Assert.False(r.E.IsSuspended);
    }

    [Fact]
    public void AC13_ReleaseAllWorkedExample()
    {
        var r = new Rig();
        r.StartWith(W("w1", frame: new Rect(-3000, 0, 500, 300)), W("w2", frame: new Rect(5000, 0, 1400, 900)),
            W("w3", frame: new Rect(0, 2000, 900, 700)), W("w4", frame: new Rect(100, 100, 800, 600)));
        r.E.SetParked(K("w1"), true);
        r.E.WindowFocused(K("w4"));
        r.E.SetFramesRunning(false);
        int writes = r.Win.Writes.Count, saves = r.Disp.SaveLog.Count, gen = r.E.PlacementGeneration;
        int done = 0;
        r.E.ReleaseAll(null, () => done++);
        var nw = r.Win.Writes.Skip(writes).ToList();
        // NOTE: spec says "rect equality exact" but 0.7 * 1440 is 1007.9999999999999 in IEEE doubles,
        // so the 1008 oracle can only be matched within 1e-9 (reported as a spec precision issue).
        var expected = new[]
        {
            (K("w1"), new Rect(60, 65, 640, 420)),
            (K("w2"), new Rect(92, 97, 1008, 612.5)),
            (K("w3"), new Rect(124, 129, 900, 612.5)),
        };
        Assert.Equal(expected.Length, nw.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Item1, nw[i].Key);
            Assert.Equal(expected[i].Item2.X, nw[i].Target.X, 9);
            Assert.Equal(expected[i].Item2.Y, nw[i].Target.Y, 9);
            Assert.Equal(expected[i].Item2.Width, nw[i].Target.Width, 9);
            Assert.Equal(expected[i].Item2.Height, nw[i].Target.Height, 9);
        }
        foreach (var n in new[] { "w1", "w2", "w3", "w4" }) Assert.False(r.Cap.IsStarted(K(n)));
        Assert.Empty(r.E.Windows);
        Assert.Empty(r.E.Away);
        Assert.Null(r.E.FocusedKey);
        Assert.False(r.E.IsStarted);
        Assert.Equal(StagePhase.Desk, r.E.Phase);
        Assert.Equal(RenderPlan.Empty, r.Ren.Last);
        Assert.Equal(gen + 1, r.E.PlacementGeneration);
        Assert.Equal(saves, r.Disp.SaveLog.Count);
        Assert.Equal(0, done);
        r.Win.Step(); r.Win.Step();
        Assert.Equal(0, done);
        r.Win.Step();
        Assert.Equal(1, done);
        r.Win.Pump(); r.Clock.Advance(1);
        Assert.Equal(1, done);
    }

    [Fact]
    public void AC14_SelectionThresholdAndTargetArea()
    {
        var r = new Rig().StartWith(W("w1", frame: new Rect(1040, 0, 800, 600)), W("w2", frame: new Rect(1041, 0, 800, 600)));
        int writes = r.Win.Writes.Count;
        r.E.ReleaseAll(null);
        var nw = r.Win.Writes.Skip(writes).ToList();
        Assert.Single(nw);
        Assert.Equal(K("w2"), nw[0].Key);

        var p = new Rig().StartWith(W("w1", frame: new Rect(0, 0, 300, 200)));
        p.E.SetParked(K("w1"), true);
        p.E.ReleaseAll(new Rect(2000, 0, 1000, 800));
        Assert.Equal(new Rect(2060, 40, 640, 420), p.Win.Writes[^1].Target);

        var n = new Rig();
        int done = 0;
        n.E.ReleaseAll(null, () => done++);
        Assert.Equal(0, done);
        n.Clock.Advance(0);
        Assert.Equal(1, done);
        n.Clock.Advance(5);
        Assert.Equal(1, done);

        var v = new Rig().StartWith(W("w1", frame: new Rect(5000, 0, 100, 100)));
        int d2 = 0;
        v.E.ReleaseAll(null, () => d2++);
        v.Win.RemoveWindow(K("w1"));
        v.Win.Pump();
        Assert.Equal(1, d2);

        var bad = new Rig().StartWith(W("w1"));
        var snap = bad.Snap();
        Assert.Throws<ArgumentOutOfRangeException>(() => bad.E.ReleaseAll(new Rect(0, 0, double.NaN, 10)));
        Assert.True(bad.E.IsStarted);
        Assert.Single(bad.E.Windows);
        Assert.Equal(snap, bad.Snap());
    }

    [Fact]
    public void AC16_Release()
    {
        var r = new Rig().StartWith(W("w1"), W("w2"));
        var snap = r.Snap();
        r.E.Release(K("w9"));
        Assert.Equal(snap, r.Snap());

        r.E.SetParked(K("w1"), true);
        int writes = r.Win.Writes.Count;
        r.E.Release(K("w1"));
        var nw = r.Win.Writes.Skip(writes).ToList();
        Assert.Single(nw);
        Assert.Equal((K("w1"), new Rect(320, 162.5, 800, 600)), (nw[0].Key, nw[0].Target));
        Assert.False(r.E.Holds(K("w1")));
        Assert.False(r.Cap.IsStarted(K("w1")));

        writes = r.Win.Writes.Count;
        r.E.Release(K("w2"));
        Assert.Equal(writes, r.Win.Writes.Count);
        Assert.False(r.E.Holds(K("w2")));

        r.E.WindowAdded(W("w3"));
        r.E.Suspend();
        r.E.Release(K("w3"));
        Assert.False(r.E.Holds(K("w3")));

        var q = new Rig().StartWith(W("w1"));
        q.E.ReleaseAll(null);
        q.E.Start(new[] { W("w5") });
        Assert.True(q.E.IsStarted);
        Assert.True(q.E.Holds(K("w5")));
    }

    [Fact]
    public void AC17_Save()
    {
        var r = new Rig();
        r.E.Save();
        Assert.Empty(r.Disp.SaveLog);
        r.StartWith(W("w1"));
        int n = r.Disp.SaveLog.Count;
        r.E.Save();
        Assert.Equal(n + 1, r.Disp.SaveLog.Count);
        Assert.Equal(r.Key, r.Disp.SaveLog[^1].Key);
        r.E.Suspend();
        n = r.Disp.SaveLog.Count;
        r.E.Save();
        Assert.Equal(n + 1, r.Disp.SaveLog.Count);
        r.E.ReleaseAll(null);
        n = r.Disp.SaveLog.Count;
        r.E.Save();
        Assert.Equal(n + 1, r.Disp.SaveLog.Count);
    }

    [Fact]
    public void AC31_TickAndFrames()
    {
        var r = new Rig();
        r.Clock.Advance(5);
        r.E.SetFramesRunning(true);
        Assert.Equal(105, r.E.LastTick);
        Assert.Throws<ArgumentOutOfRangeException>(() => r.E.Tick(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.E.Tick(double.PositiveInfinity));
        r.E.Tick(50);
        Assert.Equal(0, r.E.LastTickDelta);
        Assert.Equal(50, r.E.LastTick);

        var s = new Rig().StartWith(W("w1"));
        s.E.Suspend();
        int plans = s.Ren.Plans.Count;
        double lt = s.E.LastTick;
        s.E.Tick(200);
        Assert.Equal(plans, s.Ren.Plans.Count);
        Assert.Equal(lt, s.E.LastTick);

        var f = FramesRunningRig(W("w1"));
        f.E.ReleaseAll(null);
        Assert.Equal(1, f.Log.Count(x => x == "nf:False"));
    }

    [Fact]
    public void AC30_FramesWorkedExample()
    {
        var r = new Rig().StartWith(W("w1"));
        r.Log.Clear();
        r.E.SetFramesRunning(true);
        Assert.Equal(new[] { "nf:True" }, r.Log);
        Assert.Equal(100, r.E.LastTick);
        r.E.SetFramesRunning(true);
        Assert.Single(r.Log);
        r.E.MotionActive = true;
        int plans = r.Ren.Plans.Count;
        r.E.Tick(100.016);
        Assert.Equal(0.016, r.E.LastTickDelta, 9);
        Assert.Equal(100.016, r.E.LastTick, 9);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);
        Assert.True(r.E.FramesRunning);
        Assert.Single(r.Log);
        r.E.MotionActive = false;
        plans = r.Ren.Plans.Count;
        r.E.Tick(100.032);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);
        Assert.Equal(new[] { "nf:True", "nf:False" }, r.Log);
        Assert.False(r.E.FramesRunning);
        plans = r.Ren.Plans.Count;
        r.E.Tick(100.048);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);
        Assert.Equal(2, r.Log.Count);
    }

    [Fact]
    public void AC33_RedrawAlwaysRendersOnce()
    {
        var r = new Rig();
        r.E.Redraw();
        Assert.Single(r.Ren.Plans);
        r.StartWith(W("w1"));
        int n = r.Ren.Plans.Count;
        r.E.Redraw();
        Assert.Equal(n + 1, r.Ren.Plans.Count);
        r.E.Suspend();
        n = r.Ren.Plans.Count;
        r.E.Redraw();
        Assert.Equal(n + 1, r.Ren.Plans.Count);
    }
}
