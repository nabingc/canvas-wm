using System.Reflection;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;
using CanvasWm.Engine.Tracking;

namespace CanvasWm.Engine.Tests.TrackingSpec;

public sealed class FocusPolicyDisposeTests
{
    private static Rig Started(params OsWindowFacts[] fs)
    {
        var r = new Rig();
        r.Put(fs);
        r.Tracker.Start();
        r.Target.Clear();
        return r;
    }

    [Fact]
    [Trait("Assumption", "A7")]
    public void AC21_FocusDeduplication()
    {
        var r = Started(Rig.Std(1, 7), Rig.Std(2, 7), Rig.Std(30, 7) with { IsToolWindow = true });
        var s = r.Source;

        s.Raise(RawWindowEventKind.FocusChanged, 1);
        s.Raise(RawWindowEventKind.FocusChanged, 1);
        Assert.Equal(new[] { "WindowFocused:w1:" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();

        s.Raise(RawWindowEventKind.Foreground, 1);
        Assert.Equal(new[] { "WindowFocused:w1:", "AppActivated:7:False" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();

        s.Raise(RawWindowEventKind.FocusChanged, 1);
        Assert.Empty(r.Target.Calls);

        s.Raise(RawWindowEventKind.FocusChanged, 2);
        Assert.Equal(new[] { "WindowFocused:w2:" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();

        s.Raise(RawWindowEventKind.FocusChanged, 1);
        Assert.Equal(new[] { "WindowFocused:w1:" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();

        s.Raise(RawWindowEventKind.FocusChanged, 30);
        Assert.Equal(new[] { "FocusLeftWindows:7:" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();

        s.Raise(RawWindowEventKind.FocusChanged, 30);
        Assert.Empty(r.Target.Calls);
    }

    [Fact]
    [Trait("Assumption", "A7")]
    public void AC21_FocusChangedNeverActivatesApp()
    {
        var r = Started(Rig.Std(1, 7));
        r.Source.Raise(RawWindowEventKind.FocusChanged, 1);
        Assert.DoesNotContain(r.Target.Calls, c => c.Name == "AppActivated");
    }

    [Fact]
    public void AC22_ForegroundVariants()
    {
        var r = Started(Rig.Std(1, 7),
            Rig.Std(40, 9) with { OwnsDesktop = true, IsShellWindow = true },
            Rig.Std(50, 3) with { IsOwnProcess = true });
        var s = r.Source;

        s.Raise(RawWindowEventKind.Foreground, 40);
        Assert.Equal(new[] { "FocusLeftWindows:9:", "AppActivated:9:True" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();

        s.Raise(RawWindowEventKind.Foreground, 0);
        s.Raise(RawWindowEventKind.FocusChanged, 0);
        s.Raise(RawWindowEventKind.Foreground, 12345);
        s.Raise(RawWindowEventKind.Foreground, 50);
        Assert.Empty(r.Target.Calls);

        s.Raise(RawWindowEventKind.Foreground, 1);
        s.Raise(RawWindowEventKind.Foreground, 1);
        s.Raise(RawWindowEventKind.Foreground, 1);
        Assert.Equal(
            Enumerable.Repeat(new[] { "WindowFocused:w1:", "AppActivated:7:False" }, 3).SelectMany(x => x).ToArray(),
            Rig.Desc(r.Target.Calls));
    }

    [Fact]
    public void AC23_DockableUpgradeOneWay()
    {
        var r = Started(Rig.Std(7, 7) with { HasMaximizeButton = false });
        Assert.False(r.Tracker.Find(Rig.K(7))!.Dockable);
        r.Source.Update(7, f => f with { HasMaximizeButton = true });
        r.Source.Raise(RawWindowEventKind.FocusChanged, 7);
        Assert.Equal(new[] { "WindowBecameDockable:w7:", "WindowFocused:w7:" }, Rig.Desc(r.Target.Calls));
        Assert.True(r.Tracker.Find(Rig.K(7))!.Dockable);
        r.Target.Clear();

        r.Source.Update(7, f => f with { HasMaximizeButton = false });
        r.Source.Raise(RawWindowEventKind.Foreground, 7);
        r.Tracker.Rescan();
        Assert.DoesNotContain(r.Target.Calls, c => c.Name == "WindowBecameDockable");
        Assert.True(r.Tracker.Find(Rig.K(7))!.Dockable);
    }

    [Fact]
    public void AC23_RescanUpgrades_AndNothingBeforeReady()
    {
        var r = new Rig();
        r.Put(Rig.Std(8, 7) with { HasMaximizeButton = false });
        r.Tracker.Start();
        Assert.DoesNotContain(r.Target.Calls.TakeWhile(c => c.Name != "Ready"), c => c.Name == "WindowBecameDockable");
        Assert.False(r.Target.ReadyWindows![0].Dockable);
        r.Source.Update(8, f => f with { HasMaximizeButton = true });
        r.Clock.Advance(3);
        Assert.Equal(new[] { "Ready", "WindowBecameDockable" }, Rig.Names(r.Target.Calls));
        Assert.Equal(Rig.K(8), r.Target.Calls[1].Key);
    }

    [Fact]
    public void AC24_HandleReuseSeenByEvent()
    {
        var r = Started(Rig.Std(1, 7));
        r.Source.Put(Rig.Std(1, 8));
        r.Source.Raise(RawWindowEventKind.LocationChanged, 1);
        Assert.Equal(new[] { "WindowRemoved:w1:False" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();
        r.Clock.Advance(3);
        var a = Assert.Single(r.Target.Calls);
        Assert.Equal("WindowAdded", a.Name);
        Assert.Equal(Rig.K(1), a.Key);
        Assert.Equal(8, a.Info!.ProcessId);
    }

    [Fact]
    public void AC24_HandleReuseSeenByScan()
    {
        var r = Started(Rig.Std(1, 7));
        r.Source.Put(Rig.Std(1, 8));
        r.Tracker.Rescan();
        Assert.Equal(2, r.Target.Calls.Count);
        Assert.Equal("WindowRemoved", r.Target.Calls[0].Name);
        Assert.False(r.Target.Calls[0].Flag);
        Assert.Equal("WindowAdded", r.Target.Calls[1].Name);
        Assert.Equal(8, r.Target.Calls[1].Info!.ProcessId);
    }

    [Fact]
    public void AC24_ThreadChangeAlsoCountsAsReuse()
    {
        var r = Started(Rig.Std(1, 7));
        r.Source.Update(1, f => f with { ThreadId = 99 });
        r.Source.Raise(RawWindowEventKind.TitleChanged, 1);
        Assert.Equal(new[] { "WindowRemoved:w1:False" }, Rig.Desc(r.Target.Calls));
    }

    [Fact]
    public void E5_PidScanMissesReusedHandleThenDescribeRemoves()
    {
        var r = Started(Rig.Std(1, 7));
        r.Source.Put(Rig.Std(1, 8));
        r.Source.Put(Rig.Std(2, 7));
        r.Source.Raise(RawWindowEventKind.Created, 2); // pid 7 re-check at +0.05
        r.Clock.Advance(0.05);
        Assert.Contains(r.Target.Calls, c => c.Name == "WindowRemoved" && c.Key == Rig.K(1) && c.Flag == false);
    }

    [Fact]
    [Trait("Assumption", "A12")]
    public void AC25_Exclusions()
    {
        var r = Started(Rig.Std(2, 8), Rig.Std(1, 7));
        int e = r.Source.EnumerateCount;
        r.Tracker.SetPolicy(ClassifierPolicy.Default.WithExcludedApps(new[] { "app7" }));
        Assert.Equal(new[] { "Release:w1:" }, Rig.Desc(r.Target.Calls));
        Assert.Equal(new[] { Rig.K(2) }, r.Tracker.Tracked.Select(w => w.Key).ToArray());
        Assert.Equal(e + 1, r.Source.EnumerateCount);
        r.Target.Clear();

        r.Tracker.SetPolicy(ClassifierPolicy.Default);
        Assert.Equal(new[] { "WindowAdded:w1:" }, Rig.Desc(r.Target.Calls));
    }

    [Fact]
    public void AC25_SetPolicyBeforeStart()
    {
        var r = new Rig();
        r.Put(Rig.Std(1, 7), Rig.Std(2, 8));
        r.Tracker.SetPolicy(ClassifierPolicy.Default.WithExcludedApps(new[] { "app7" }));
        Assert.Empty(r.Target.Calls);
        Assert.Equal(0, r.Source.EnumerateCount);
        r.Tracker.Start();
        Assert.Equal(new[] { Rig.K(2) }, r.Tracker.Tracked.Select(w => w.Key).ToArray());
    }

    [Fact]
    public void AC25_MinimumSizeChangeRemovesTemporarily()
    {
        var r = Started(Rig.Std(1, 7), Rig.Std(2, 8));
        r.Tracker.SetPolicy(ClassifierPolicy.Default with { MinimumSize = new Size(1000, 1000) });
        Assert.Equal(2, r.Target.Calls.Count);
        Assert.All(r.Target.Calls, c => { Assert.Equal("WindowRemoved", c.Name); Assert.True(c.Flag); });
        Assert.Empty(r.Tracker.Tracked);
    }

    [Fact]
    public void AC25_EqualPolicyStillScans()
    {
        var r = Started();
        int e = r.Source.EnumerateCount;
        r.Tracker.SetPolicy(ClassifierPolicy.Default);
        Assert.Equal(e + 1, r.Source.EnumerateCount);
    }

    [Fact]
    public void AC26_ReentrantRescanIsQueued()
    {
        var r = new Rig();
        r.Tracker.Start();
        r.Put(Rig.Std(1, 7), Rig.Std(2, 7));
        r.Target.OnCall = c => { if (c.Name == "WindowAdded") r.Tracker.Rescan(); };
        int e = r.Source.EnumerateCount;
        r.Clock.Advance(3);
        Assert.Equal(1, r.Source.MaxEnumerateDepth);
        Assert.Equal(2, r.Named("WindowAdded").Count());
        Assert.Equal(e + 1 + 2, r.Source.EnumerateCount); // timer scan + one queued rescan per add
    }

    [Fact]
    public void AC26_EnumerateHookReentrancyIsQueued()
    {
        var r = new Rig();
        r.Tracker.Start();
        bool once = false;
        r.Source.OnEnumerate = () =>
        {
            if (once) return;
            once = true;
            r.Tracker.Rescan();
        };
        r.Tracker.Rescan();
        Assert.Equal(1, r.Source.MaxEnumerateDepth);
    }

    [Fact]
    public void AC26_EventRaisedFromTargetCallRunsAfter()
    {
        var r = new Rig();
        r.Tracker.Start();
        r.Put(Rig.Std(1, 7));
        bool done = false;
        r.Target.OnCall = c =>
        {
            if (c.Name == "WindowAdded" && !done)
            {
                done = true;
                r.Source.Raise(RawWindowEventKind.FocusChanged, 1);
            }
        };
        r.Clock.Advance(3);
        Assert.Equal(new[] { "WindowAdded", "WindowFocused" }, Rig.Names(r.Target.Calls.Where(c => c.Name != "Ready")));
    }

    [Fact]
    public void AC27_Dispose()
    {
        var r = Started(Rig.Std(1, 7));
        r.Source.Put(Rig.Std(20, 7));
        r.Source.Raise(RawWindowEventKind.Created, 20); // pending re-checks
        r.Tracker.Dispose();
        Assert.Equal(0, r.Source.SubscriberCount);
        int e = r.Source.EnumerateCount;
        r.Clock.Advance(10);
        Assert.Equal(e, r.Source.EnumerateCount);
        Assert.Empty(r.Target.Calls);
        r.Tracker.Rescan();
        r.Tracker.SetPolicy(ClassifierPolicy.Default.WithExcludedApps(new[] { "app7" }));
        Assert.Equal(e, r.Source.EnumerateCount);
        Assert.Empty(r.Target.Calls);
        r.Tracker.Dispose();
        r.Source.Raise(RawWindowEventKind.Destroyed, 1);
        Assert.Empty(r.Target.Calls);
    }

    [Fact]
    [Trait("Assumption", "A10")]
    public void AC28_OwnProcessEventsMakeNoCall()
    {
        var r = Started(Rig.Std(1, 7));
        r.Source.Put(Rig.Std(60, 3) with { IsOwnProcess = true });
        int pending = r.Clock.PendingCount;
        foreach (var kind in Enum.GetValues<RawWindowEventKind>())
            r.Source.Raise(kind, 60);
        Assert.Empty(r.Target.Calls);
        Assert.Equal(pending, r.Clock.PendingCount);
        r.Tracker.Rescan();
        Assert.Null(r.Tracker.Find(Rig.K(60)));
    }

    [Fact]
    public void AC28_ZeroHandleSchedulesNothingAndIsNeverAdmitted()
    {
        var r = Started();
        r.Source.Put(Rig.Std(0, 7));
        int pending = r.Clock.PendingCount;
        r.Source.Raise(RawWindowEventKind.Created, 0);
        Assert.Equal(pending, r.Clock.PendingCount);
        r.Tracker.Rescan();
        Assert.Empty(r.Tracker.Tracked);
        Assert.Empty(r.Target.Calls);
    }

    [Fact]
    public void AC29_FakeWindowSourceContract()
    {
        var s = new FakeWindowSource();
        var a = Rig.Std(1, 7);
        var b = Rig.Std(2, 7);
        s.Put(a);
        s.Put(b);
        Assert.Equal(new[] { b, a }, s.Enumerate());
        var a2 = a with { Title = "new" };
        s.Put(a2);
        Assert.Equal(new[] { b, a2 }, s.Enumerate());
        s.SetZOrder(new ulong[] { 1 });
        Assert.Equal(new[] { a2, b }, s.Enumerate());
        s.Remove(1);
        Assert.Null(s.Describe(1));
        Assert.Equal(new[] { b }, s.Enumerate());
        Assert.NotSame(s.Enumerate(), s.Enumerate());
    }

    [Fact]
    public void AC29_SubscribersAndCounters()
    {
        var s = new FakeWindowSource();
        var log = new List<string>();
        var d1 = s.Subscribe(e => log.Add("1:" + e.Handle));
        var d2 = s.Subscribe(e => log.Add("2:" + e.Handle));
        Assert.Equal(2, s.SubscriberCount);
        s.Raise(RawWindowEventKind.Created, 5);
        Assert.Equal(new[] { "1:5", "2:5" }, log);
        d1.Dispose();
        d1.Dispose();
        log.Clear();
        s.Raise(RawWindowEventKind.Created, 6);
        Assert.Equal(new[] { "2:6" }, log);
        Assert.Equal(1, s.SubscriberCount);
        d2.Dispose();

        s.Enumerate();
        s.Enumerate();
        s.Describe(1);
        Assert.Equal(2, s.EnumerateCount);
        Assert.Equal(1, s.DescribeCount);
        s.OnEnumerate = () => { s.OnEnumerate = null; s.Enumerate(); };
        s.Enumerate();
        Assert.Equal(2, s.MaxEnumerateDepth);
    }

    [Fact]
    public void AC29_RecordingTargetStampsClockAndCopiesDisplays()
    {
        var clock = new ManualClock(0);
        var t = new RecordingTrackerTarget(clock);
        clock.Advance(2.5);
        t.WindowFocused(new WindowKey("x"));
        Assert.Equal(2.5, t.Calls[0].At, 9);
        Assert.Equal(new Rect(0, 0, 1440, 900), Assert.Single(t.Displays));
        var copy = (List<Rect>)t.DisplayFrames;
        copy.Clear();
        Assert.Single(t.Displays);
        Assert.Single(t.DisplayFrames);
        IWindowTrackerTarget tt = t;
        IWindowSource ss = new FakeWindowSource();
        Assert.NotNull(tt);
        Assert.NotNull(ss);
    }

    [Fact]
    public void AC30_PublicSurface()
    {
        Assert.True(typeof(IWindowSource).IsPublic);
        Assert.Equal("CanvasWm.Engine.Services", typeof(IWindowSource).Namespace);
        foreach (var t in new[] { typeof(WindowTracker), typeof(IWindowTrackerTarget), typeof(TrackerOptions) })
        {
            Assert.True(t.IsPublic);
            Assert.Equal("CanvasWm.Engine.Tracking", t.Namespace);
        }

        Assert.DoesNotContain(typeof(WindowTracker).Assembly.GetReferencedAssemblies(), n => n.Name!.Contains("Win32"));
    }
}
