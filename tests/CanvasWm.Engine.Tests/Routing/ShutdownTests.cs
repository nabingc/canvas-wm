using CanvasWm.Core.Geometry;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.RoutingSpec;

internal sealed class HookStore : IDisplayStore
{
    private readonly IDisplayStore _inner;
    public Action<EngineKey>? OnSave;

    public HookStore(IDisplayStore inner) => _inner = inner;

    public SavedDisplay? Load(EngineKey key) => _inner.Load(key);

    public void Save(EngineKey key, SavedDisplay display)
    {
        OnSave?.Invoke(key);
        _inner.Save(key, display);
    }
}

internal sealed class DroppingClock : IClock
{
    private readonly IClock _inner;
    public int Dropped;

    public DroppingClock(IClock inner) => _inner = inner;

    public double Now => _inner.Now;

    public void After(double seconds, Action action) => Dropped++;
}

public sealed class ShutdownTests
{
    /// <summary>Ready, landing pumped, then A's two windows parked so they are selected for release.</summary>
    private static Rt Settled(Func<IDisplayStore, IDisplayStore>? wrapStore = null, Func<ManualClock, IClock>? wrapClock = null)
    {
        var t = new Rt(wrapStore: wrapStore, wrapClock: wrapClock);
        t.Ready();
        t.Win.Pump();
        t.Clock.Advance(0.2);
        t.Win.Pump();
        t.Clock.Advance(50 - t.Clock.Now);
        t.EA_.SetParked(Rt.K("w1"), true);
        t.EA_.SetParked(Rt.K("w3"), true);
        return t;
    }

    [Fact]
    public void AC25_SaveBeforeReleaseThenCompletes()
    {
        HookStore? hook = null;
        var t = Settled(s => hook = new HookStore(s));
        var events = new List<string>();
        int writesAtStart = t.Win.Writes.Count;
        hook!.OnSave = k =>
        {
            Assert.All(t.Router.Engines, e => Assert.True(e.IsStarted));
            Assert.Equal(writesAtStart, t.Win.Writes.Count);
            events.Add("save:" + k.Value);
        };
        t.Router.ShutdownCompleted += () => events.Add("done");

        t.Router.BeginShutdown();
        Assert.True(t.Router.IsShuttingDown);
        Assert.Equal(new[] { "save:A|default", "save:B|default" }, events);
        Assert.Equal(2, t.Win.Writes.Count - writesAtStart);
        Assert.False(t.Router.IsShutdownComplete);
        Assert.Equal(0, t.CompletedCount);
        Assert.False(t.Router.FramesNeeded);

        t.Clock.Advance(0);   // B's completion
        Assert.False(t.Router.IsShutdownComplete);
        Assert.Equal(0, t.CompletedCount);
        t.Win.Pump();         // A's two completions
        Assert.True(t.Router.IsShutdownComplete);
        Assert.Equal(1, t.CompletedCount);
        Assert.Equal(50, t.Clock.Now, 9);
        t.Clock.Advance(5);
        Assert.Equal(1, t.CompletedCount);
    }

    [Fact]
    public void AC25_DeadlineVariant()
    {
        var t = Settled();
        t.Router.BeginShutdown();
        t.Clock.Advance(0);
        t.Clock.Advance(1.999);
        Assert.False(t.Router.IsShutdownComplete);
        Assert.Equal(0, t.CompletedCount);
        t.Clock.Advance(52.0 - t.Clock.Now);
        Assert.True(t.Router.IsShutdownComplete);
        Assert.Equal(1, t.CompletedCount);
        Assert.Equal(52.0, t.Clock.Now, 9);
        t.Win.Pump();   // a later pump raises nothing
        Assert.Equal(1, t.CompletedCount);
    }

    [Fact]
    public void AC25_PollingReadsTrueAtDeadlineEvenIfClockActionDropped()
    {
        DroppingClock? dc = null;
        var t = Settled(wrapClock: c => dc = new DroppingClock(c));
        t.Router.BeginShutdown();
        Assert.True(dc!.Dropped >= 1);
        t.Clock.Advance(51.999 - t.Clock.Now);
        Assert.False(t.Router.IsShutdownComplete);
        t.Clock.Advance(52.0 - t.Clock.Now);
        Assert.True(t.Router.IsShutdownComplete);
        Assert.Equal(0, t.CompletedCount);
    }

    [Fact]
    public void AC26_SecondBeginShutdownIsNoop()
    {
        var t = Settled();
        t.Router.BeginShutdown();
        int writes = t.Win.Writes.Count, saves = t.Mem.SaveLog.Count, pending = t.Clock.PendingCount;
        t.Router.BeginShutdown();
        Assert.Equal(writes, t.Win.Writes.Count);
        Assert.Equal(saves, t.Mem.SaveLog.Count);
        Assert.Equal(pending, t.Clock.PendingCount);
    }

    [Fact]
    public void AC26_ZeroEnginesCompletesInsideTheCall()
    {
        var t = new Rt();
        int seen = -1;
        t.Router.ShutdownCompleted += () => seen = t.Router.Engines.Count;
        t.Router.BeginShutdown();
        Assert.Equal(1, t.CompletedCount);
        Assert.Equal(0, seen);
        Assert.True(t.Router.IsShutdownComplete);
        t.Clock.Advance(10);
        Assert.Equal(1, t.CompletedCount);
    }

    [Fact]
    public void AC26_PendingRebuildNeverRuns()
    {
        var t = Settled();
        t.Router.DisplaysChanged(new[] { Rt.A });
        t.Router.BeginShutdown();
        t.Clock.Advance(10);
        Assert.Empty(t.Retired);
        Assert.Equal(2, t.Router.Engines.Count);
        Assert.Equal(2, t.FactoryCalls.Count);
        Assert.Equal(2, t.Router.Displays.Count);
    }

    [Fact]
    public void AC26_InertAfterShutdown()
    {
        var t = Settled();
        t.Router.BeginShutdown();
        var a = t.EA_;
        int writes = t.Win.Writes.Count, saves = t.Mem.SaveLog.Count, calls = t.FactoryCalls.Count, changed = t.ChangedCount;
        var focusBefore = a.FocusedKey;
        var keysBefore = a.Windows.Select(w => w.Info.Key.Value).ToArray();
        t.Router.WindowFocused(Rt.K("w1"));
        t.Router.WindowAdded(Rt.W("w9", 9, Rt.R(1, 1, 5, 5)));
        t.Router.WindowMoved(Rt.K("w1"), Rt.R(0, 0, 5, 5));
        t.Router.WindowRemoved(Rt.K("w1"), false);
        t.Router.Release(Rt.K("w3"));
        t.Router.AppActivated(77, true);
        t.Router.FocusLeftWindows(1);
        t.Router.DisplaysChanged(new[] { Rt.A });
        t.Router.DesktopShown(Rt.KA, "d2");
        t.Router.Tick(99);
        t.Router.Ready(new[] { Rt.W1 });
        Assert.Null(t.Router.Execute(ShortcutCommand.ForAction(ShortcutAction.Right)));
        Assert.Equal(focusBefore, a.FocusedKey);
        Assert.Equal(keysBefore, a.Windows.Select(w => w.Info.Key.Value).ToArray());
        Assert.False(a.Holds(Rt.K("w9")));
        Assert.NotEqual(77, a.ActiveProcessId);
        Assert.Equal(calls, t.FactoryCalls.Count);
        Assert.Equal(writes, t.Win.Writes.Count);
        Assert.Equal(saves, t.Mem.SaveLog.Count);
        Assert.Equal(changed, t.ChangedCount);
        Assert.Equal("default", t.Router.DesktopOf(Rt.KA));
        Assert.Equal(2, t.Router.Engines.Count);
        Assert.False(t.Router.IsTornDown);
    }

    [Fact]
    public void AC26_ReadyAfterShutdownDoesNothing()
    {
        var t = new Rt();
        t.Router.BeginShutdown();
        t.Router.Ready(new[] { Rt.W1 });
        Assert.False(t.Router.IsReady);
        Assert.Empty(t.FactoryCalls);
        Assert.Equal(0, t.StartedCount);
    }

    [Fact]
    [Trait("Assumption", "A7")]
    public void AC26_RemovedDisplayReleasesIntoPendingMainWorkArea()
    {
        var t = new Rt();
        t.Ready();   // landing not pumped: OS frames unchanged
        t.EB_.SetParked(Rt.K("w2"), true);
        var a1 = Rt.A with { WorkArea = new Rect(0, 0, 1920, 1000) };
        t.Router.DisplaysChanged(new[] { a1 });
        int w0 = t.Win.Writes.Count;
        t.Router.BeginShutdown();
        var w2 = t.Win.Writes.Skip(w0).Single(w => w.Key == Rt.K("w2"));
        Assert.Equal(ReleaseGeometry.Cascade(new Rect(0, 0, 1920, 1000), new Size(800, 500), 0), w2.Target);
        var w3 = t.Win.Writes.Skip(w0).Single(w => w.Key == Rt.K("w3"));
        Assert.Equal(ReleaseGeometry.Cascade(new Rect(0, 0, 1920, 1040), new Size(800, 600), 0), w3.Target);
    }

    [Fact]
    public void AC26_PresentDisplayReleasesIntoOwnWorkAreaWhenNoPending()
    {
        var t = new Rt();
        t.Ready();
        t.EB_.SetParked(Rt.K("w2"), true);
        int w0 = t.Win.Writes.Count;
        t.Router.BeginShutdown();
        var w2 = t.Win.Writes.Skip(w0).Single(w => w.Key == Rt.K("w2"));
        Assert.Equal(ReleaseGeometry.Cascade(Rt.B.WorkArea, new Size(800, 500), 0), w2.Target);
    }
}
