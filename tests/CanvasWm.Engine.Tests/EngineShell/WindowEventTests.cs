using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Rendering;
using CanvasWm.Engine.Services;
using static CanvasWm.Engine.Tests.EngineShellSpec.Rig;

namespace CanvasWm.Engine.Tests.EngineShellSpec;

public sealed class WindowEventTests
{
    [Fact]
    public void AC18_DuplicateAddIsRefresh()
    {
        var r = new Rig().StartWith(W("w0"));
        r.E.WindowAdded(W("w1", title: "A"));
        r.E.WindowAdded(W("w2"));
        r.E.SetParked(K("w1"), true);
        int rec = r.Cap.RecaptureRequests(K("w1"));
        r.E.WindowAdded(W("w1", title: "B"));
        Assert.Equal(3, r.E.Windows.Count);
        Assert.Equal(1, r.E.Windows.Select((w, i) => (w, i)).Single(x => x.w.Info.Key == K("w1")).i);
        var m = r.E.Window(K("w1"))!;
        Assert.Equal("B", m.Info.Title);
        Assert.True(m.Parked);
        Assert.Equal(rec, r.Cap.RecaptureRequests(K("w1")));
    }

    [Fact]
    public void AC19_AddBeforeAndAfterStart()
    {
        var r = new Rig();
        r.E.WindowAdded(W("w1"));
        Assert.True(r.E.Holds(K("w1")));
        Assert.True(r.Cap.IsStarted(K("w1")));
        Assert.Empty(r.Disp.SaveLog);
        Assert.Empty(r.Ren.Plans);
        r.E.Start(new List<WindowInfo>());
        int saves = r.Disp.SaveLog.Count, plans = r.Ren.Plans.Count;
        r.E.WindowAdded(W("w2"));
        Assert.Equal(saves + 1, r.Disp.SaveLog.Count);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);
    }

    [Fact]
    public void AC20_UnknownRemoveIsNoOp()
    {
        var r = new Rig().StartWith(W("w1"));
        r.Log.Clear();
        var s = r.Snap();
        r.E.WindowRemoved(K("w9"), false);
        Assert.Equal(s, r.Snap());
        Assert.Empty(r.Log);
    }

    [Fact]
    public void AC21_SuspendedGuard()
    {
        var f = new Rect(100, 100, 800, 600);
        var r = new Rig().StartWith(W("w1", frame: f));
        r.E.Suspend();
        r.Log.Clear();
        var s = r.Snap();
        r.E.WindowAdded(W("w2"));
        r.E.WindowRemoved(K("w1"), false);
        r.E.WindowFocused(K("w1"));
        r.E.WindowMoved(K("w1"), new Rect(1, 2, 3, 4));
        Assert.Equal(new[] { K("w1") }, r.E.Windows.Select(w => w.Info.Key));
        Assert.Equal(f, r.E.Window(K("w1"))!.Info.Frame);
        Assert.Null(r.E.FocusedKey);
        Assert.Equal(s, r.Snap());
        Assert.Empty(r.Log);

        r.E.AppActivated(42, false);
        Assert.Equal(42, r.E.ActiveProcessId);
        Assert.Equal(s, r.Snap());

        r.E.SetPhase(StagePhase.Overview);
        r.E.WindowTitleChanged(K("w1"), "T");
        r.E.WindowBecameDockable(K("w1"));
        r.E.CaptureArrived(K("w1"));
        Assert.Equal("T", r.E.Window(K("w1"))!.Info.Title);
        Assert.True(r.E.Window(K("w1"))!.Info.Dockable);
        Assert.Equal(s, r.Snap());
    }

    [Fact]
    public void AC22_ParkedRemovalWritesBack()
    {
        var r = new Rig().StartWith(W("w1", frame: new Rect(5000, 0, 800, 600)));
        r.E.SetParked(K("w1"), true);
        int writes = r.Win.Writes.Count, saves = r.Disp.SaveLog.Count, plans = r.Ren.Plans.Count;
        r.E.WindowRemoved(K("w1"), false);
        Assert.Equal(writes + 1, r.Win.Writes.Count);
        Assert.Equal((K("w1"), new Rect(320, 162.5, 800, 600)), (r.Win.Writes[^1].Key, r.Win.Writes[^1].Target));
        Assert.False(r.Cap.IsStarted(K("w1")));
        Assert.False(r.E.Holds(K("w1")));
        Assert.Equal(saves + 1, r.Disp.SaveLog.Count);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);
        r.Win.Pump();
        Assert.Equal(writes + 1, r.Win.Writes.Count);
        Assert.Equal(saves + 1, r.Disp.SaveLog.Count);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);

        r.E.WindowAdded(r.OS(W("w2")));
        writes = r.Win.Writes.Count;
        r.E.WindowRemoved(K("w2"), false);
        Assert.Equal(writes, r.Win.Writes.Count);
    }

    [Fact]
    public void AC23_AwayTable()
    {
        var r = new Rig().StartWith(W("w1"), W("w2"), W("w3"));
        r.E.WindowRemoved(K("w1"), true);
        Assert.True(r.E.Away.ContainsKey(K("w1")));
        Assert.Null(r.E.Away[K("w1")]);
        r.E.WindowAdded(W("w1"));
        Assert.Empty(r.E.Away);
        Assert.Null(r.E.Window(K("w1"))!.RememberedSpot);

        var spot = new DockSpot(new SpecRestore("r"), new[] { "l1" });
        r.E.SetRememberedSpot(K("w2"), spot);
        r.E.WindowRemoved(K("w2"), true);
        Assert.Equal(spot, r.E.Away[K("w2")]);
        r.E.WindowAdded(W("w2"));
        Assert.Empty(r.E.Away);
        Assert.Equal(spot, r.E.Window(K("w2"))!.RememberedSpot);

        r.E.WindowRemoved(K("w3"), false);
        Assert.Empty(r.E.Away);

        var n = new Rig();
        n.E.WindowAdded(W("w4"));
        n.E.WindowRemoved(K("w4"), true);
        Assert.Empty(n.E.Away);
        Assert.False(n.E.Holds(K("w4")));

        r.E.WindowRemoved(K("w1"), true);
        r.E.Suspend();
        r.E.WindowAdded(W("w1"));
        Assert.True(r.E.Away.ContainsKey(K("w1")));
        Assert.False(r.E.Holds(K("w1")));
    }

    [Fact]
    public void AC24_Focus()
    {
        var r = new Rig().StartWith(W("w1", 7), W("w2", 8));
        r.Log.Clear();
        int plans = r.Ren.Plans.Count;
        r.E.WindowFocused(K("w2"));
        Assert.Equal(K("w2"), r.E.FocusedKey);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);
        Assert.Equal(new[] { false, true }, r.Ren.Last!.Windows.Select(w => w.Focused));
        Assert.Equal(new[] { "change" }, r.Log);

        r.Log.Clear();
        plans = r.Ren.Plans.Count;
        r.E.WindowFocused(K("w9"));
        Assert.Equal(plans, r.Ren.Plans.Count);
        Assert.Empty(r.Log);

        r.E.WindowRemoved(K("w2"), false);
        Assert.Null(r.E.FocusedKey);
    }

    [Fact]
    public void AC25_Moved()
    {
        var r = new Rig().StartWith(W("w1"));
        var s = r.Snap();
        r.E.WindowMoved(K("w1"), new Rect(10, 20, 300, 200));
        Assert.Equal(new Rect(10, 20, 300, 200), r.E.Window(K("w1"))!.Info.Frame);
        Assert.Equal(s, r.Snap());
    }

    [Fact]
    public void AC26_TitleAndCapture()
    {
        var r = new Rig().StartWith(W("w1"));
        int plans = r.Ren.Plans.Count;
        r.E.WindowTitleChanged(K("w1"), "X");
        Assert.Equal("X", r.E.Window(K("w1"))!.Info.Title);
        Assert.Equal(plans, r.Ren.Plans.Count);
        r.E.CaptureArrived(K("w1"));
        Assert.Equal(plans, r.Ren.Plans.Count);
        r.E.SetPhase(StagePhase.Overview);
        r.E.WindowTitleChanged(K("w1"), "Y");
        Assert.Equal("Y", r.E.Window(K("w1"))!.Info.Title);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);
        r.E.CaptureArrived(K("w1"));
        Assert.Equal(plans + 2, r.Ren.Plans.Count);
        r.E.CaptureArrived(K("w9"));
        Assert.Equal(plans + 2, r.Ren.Plans.Count);
        Assert.Throws<ArgumentNullException>(() => r.E.WindowTitleChanged(K("w1"), null!));
    }

    [Fact]
    public void AC27_AppActivated()
    {
        var r = new Rig().StartWith(W("w1", 7), W("w2", 8));
        r.E.WindowFocused(K("w1"));
        r.E.WindowFocused(K("w2"));
        int Count(Action a) { int n = r.Ren.Plans.Count; a(); return r.Ren.Plans.Count - n; }
        Assert.Equal(1, Count(() => r.E.AppActivated(7, false)));
        Assert.Equal(0, Count(() => r.E.AppActivated(7, false)));
        Assert.Equal(1, Count(() => r.E.AppActivated(8, false)));
        Assert.Equal(1, Count(() => r.E.AppActivated(9, false)));
        Assert.Equal(0, Count(() => r.E.AppActivated(99, false)));
        Assert.Equal(1, Count(() => r.E.AppActivated(99, true)));
        Assert.True(r.E.DesktopOwnerActive);
    }

    [Fact]
    public void AC28_VisibleAreaChanged()
    {
        var r = new Rig().StartWith(W("w1"));
        int plans = r.Ren.Plans.Count;
        r.E.VisibleAreaChanged(VA);
        Assert.Equal(plans, r.Ren.Plans.Count);
        Assert.Equal(0, r.E.ReframeCount);
        var nv = new Rect(0, 0, 1440, 860);
        r.E.VisibleAreaChanged(nv);
        Assert.Equal(nv, r.E.VisibleArea);
        Assert.Equal(1, r.E.ReframeCount);
        Assert.Equal(plans + 1, r.Ren.Plans.Count);

        var n = new Rig();
        n.E.VisibleAreaChanged(nv);
        Assert.Equal(nv, n.E.VisibleArea);
        Assert.Equal(0, n.E.ReframeCount);
        Assert.Empty(n.Ren.Plans);

        r.E.Suspend();
        plans = r.Ren.Plans.Count;
        var nv2 = new Rect(0, 0, 1000, 800);
        r.E.VisibleAreaChanged(nv2);
        Assert.Equal(nv2, r.E.VisibleArea);
        Assert.Equal(1, r.E.ReframeCount);
        Assert.Equal(plans, r.Ren.Plans.Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => r.E.VisibleAreaChanged(new Rect(0, 0, -1, 5)));
        Assert.Throws<ArgumentOutOfRangeException>(() => r.E.VisibleAreaChanged(new Rect(0, double.PositiveInfinity, 5, 5)));
        Assert.Equal(nv2, r.E.VisibleArea);
    }
}
