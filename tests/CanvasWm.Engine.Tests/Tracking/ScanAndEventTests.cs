using CanvasWm.Core.Geometry;
using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;
using CanvasWm.Engine.Tracking;

namespace CanvasWm.Engine.Tests.TrackingSpec;

public sealed class ScanAndEventTests
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
    public void AC14_PeriodicRescan()
    {
        var r = Started(Rig.Std(11, 7), Rig.Std(12, 7));
        int e0 = r.Source.EnumerateCount;
        r.AdvanceTo(1);
        r.Source.Put(Rig.Std(10, 7));
        r.AdvanceTo(2.999);
        Assert.Empty(r.Target.Calls);
        Assert.Equal(e0, r.Source.EnumerateCount);
        r.AdvanceTo(3);
        Assert.Equal(e0 + 1, r.Source.EnumerateCount);
        var added = Assert.Single(r.Target.Calls);
        Assert.Equal("WindowAdded", added.Name);
        Assert.Equal(Rig.K(10), added.Key);
        Assert.Equal(3, added.At, 9);

        r.AdvanceTo(6);
        Assert.Equal(e0 + 2, r.Source.EnumerateCount);
        Assert.Single(r.Target.Calls);

        r.Source.Remove(10);
        r.Source.Update(11, f => f with { IsMinimized = true });
        r.Source.Update(12, f => f with { Cloak = CloakKind.Shell });
        r.AdvanceTo(9);
        Assert.Equal(3, r.Target.Calls.Count);
        var removed = r.Target.Calls.Skip(1).ToList();
        Assert.Contains(removed, c => c.Name == "WindowRemoved" && c.Key == Rig.K(10) && c.Flag == false && Math.Abs(c.At - 9) < 1e-9);
        Assert.Contains(removed, c => c.Name == "WindowRemoved" && c.Key == Rig.K(11) && c.Flag == true);
        Assert.DoesNotContain(r.Target.Calls, c => c.Key == Rig.K(12));
        Assert.NotNull(r.Tracker.Find(Rig.K(12)));
    }

    [Fact]
    public void AC14_RescanRearmsAndRescanMethodDoesNotChangeTimer()
    {
        var r = Started();
        Assert.Equal(1, r.Clock.PendingCount);
        r.Clock.Advance(3);
        Assert.Equal(1, r.Clock.PendingCount);
        int e = r.Source.EnumerateCount;
        r.Tracker.Rescan();
        Assert.Equal(e + 1, r.Source.EnumerateCount);
        Assert.Equal(1, r.Clock.PendingCount);
        r.Clock.Advance(3);
        Assert.Equal(e + 2, r.Source.EnumerateCount);
    }

    [Fact]
    public void AC14_RescanBeforeStartIsNoOp()
    {
        var r = new Rig();
        r.Tracker.Rescan();
        Assert.Equal(0, r.Source.EnumerateCount);
        Assert.Empty(r.Target.Calls);
    }

    private static Rig StartedAt10()
    {
        var r = new Rig();
        r.Tracker.Start();
        r.Clock.Advance(10);
        r.Target.Clear();
        return r;
    }

    [Fact]
    public void AC15_RecheckTiming()
    {
        var r = StartedAt10();
        Assert.Equal(10, r.Clock.Now, 9);
        int e0 = r.Source.EnumerateCount;
        r.Source.Put(Rig.Std(20, 7) with { Frame = new Rect(100, 100, 50, 50) });
        r.Source.Raise(RawWindowEventKind.Created, 20);
        Assert.Equal(e0, r.Source.EnumerateCount);

        r.AdvanceTo(10.02);
        r.Source.Update(20, f => f with { Frame = new Rect(100, 100, 900, 700) });
        r.Source.Put(Rig.Std(21, 7));
        Assert.Equal(e0, r.Source.EnumerateCount);

        r.AdvanceTo(10.05);
        Assert.Equal(e0 + 1, r.Source.EnumerateCount);
        Assert.Equal(new[] { "WindowAdded", "WindowAdded" }, Rig.Names(r.Target.Calls));
        Assert.Equal(new[] { Rig.K(21), Rig.K(20) }, r.Target.Calls.Select(c => c.Key!).ToArray());
        Assert.All(r.Target.Calls, c => Assert.Equal(10.05, c.At, 9));

        r.AdvanceTo(10.4);
        Assert.Equal(e0 + 2, r.Source.EnumerateCount);
        Assert.Equal(2, r.Target.Calls.Count);
        r.AdvanceTo(10.45);
        Assert.Equal(e0 + 2, r.Source.EnumerateCount);
    }

    [Fact]
    [Trait("Assumption", "A2")]
    public void AC16_SameProcessCoalesces()
    {
        var r = StartedAt10();
        int e0 = r.Source.EnumerateCount;
        r.Source.Put(Rig.Std(20, 7));
        r.Source.Raise(RawWindowEventKind.Created, 20);
        r.AdvanceTo(10.01);
        r.Source.Raise(RawWindowEventKind.Shown, 20);
        r.AdvanceTo(10.05);
        Assert.Equal(e0 + 1, r.Source.EnumerateCount);
        r.AdvanceTo(10.4);
        Assert.Equal(e0 + 2, r.Source.EnumerateCount);
        r.AdvanceTo(10.45);
        Assert.Equal(e0 + 2, r.Source.EnumerateCount);
    }

    [Fact]
    [Trait("Assumption", "A2")]
    public void AC16_NewFirstSlotWhileSecondPending()
    {
        var r = StartedAt10();
        int e0 = r.Source.EnumerateCount;
        r.Source.Put(Rig.Std(20, 7));
        r.Source.Raise(RawWindowEventKind.Created, 20);
        r.AdvanceTo(10.05);
        Assert.Equal(e0 + 1, r.Source.EnumerateCount);
        r.AdvanceTo(10.1);
        r.Source.Raise(RawWindowEventKind.Shown, 20);
        r.AdvanceTo(10.15);
        Assert.Equal(e0 + 2, r.Source.EnumerateCount);
        r.AdvanceTo(10.4);
        Assert.Equal(e0 + 3, r.Source.EnumerateCount);
        r.AdvanceTo(10.45);
        Assert.Equal(e0 + 3, r.Source.EnumerateCount);
    }

    [Fact]
    [Trait("Assumption", "A2")]
    public void AC16_OtherProcessIsIndependent()
    {
        var r = StartedAt10();
        int e0 = r.Source.EnumerateCount;
        r.Source.Put(Rig.Std(20, 7));
        r.Source.Put(Rig.Std(30, 8));
        r.Source.Raise(RawWindowEventKind.Created, 20);
        r.AdvanceTo(10.01);
        r.Source.Raise(RawWindowEventKind.Created, 30);
        r.AdvanceTo(10.05);
        Assert.Equal(e0 + 1, r.Source.EnumerateCount); // pid 7 only
        r.AdvanceTo(10.06);
        Assert.Equal(e0 + 2, r.Source.EnumerateCount); // pid 8 at 10.06
        r.AdvanceTo(10.45);
        Assert.Equal(e0 + 4, r.Source.EnumerateCount);
    }

    [Fact]
    public void AC16_ScopeIsPerProcess()
    {
        var r = StartedAt10();
        r.Source.Put(Rig.Std(20, 7));
        r.Source.Raise(RawWindowEventKind.Created, 20);
        r.AdvanceTo(10.02);
        r.Source.Put(Rig.Std(30, 8));
        r.AdvanceTo(10.05);
        Assert.DoesNotContain(r.Target.Calls, c => c.Key == Rig.K(30));
        Assert.Contains(r.Target.Calls, c => c.Key == Rig.K(20));
        r.AdvanceTo(10.4);
        Assert.DoesNotContain(r.Target.Calls, c => c.Key == Rig.K(30));
        r.AdvanceTo(11.99);
        Assert.DoesNotContain(r.Target.Calls, c => c.Key == Rig.K(30));
        r.AdvanceTo(12);
        var add = Assert.Single(r.Target.Calls, c => c.Key == Rig.K(30));
        Assert.Equal(12, add.At, 9);
    }

    [Fact]
    public void AC16_PidScopeDoesNotRemoveOtherProcessWindow()
    {
        var r = Started(Rig.Std(30, 8));
        r.AdvanceTo(1);
        r.Source.Update(30, f => f with { IsMinimized = true });
        r.Source.Put(Rig.Std(20, 7));
        r.Source.Raise(RawWindowEventKind.Created, 20);
        r.AdvanceTo(1.5);
        Assert.DoesNotContain(r.Target.Calls, c => c.Key == Rig.K(30));
    }

    [Fact]
    public void AC16_UnknownHandleSchedulesNothing()
    {
        var r = StartedAt10();
        int pending = r.Clock.PendingCount;
        r.Source.Raise(RawWindowEventKind.Created, 999);
        Assert.Equal(pending, r.Clock.PendingCount);
    }

    [Theory]
    [InlineData(RawWindowEventKind.Created)]
    [InlineData(RawWindowEventKind.Shown)]
    [InlineData(RawWindowEventKind.Uncloaked)]
    [InlineData(RawWindowEventKind.Restored)]
    public void AC16_AllAppearKindsSchedule(RawWindowEventKind kind)
    {
        var r = StartedAt10();
        r.Source.Put(Rig.Std(20, 7));
        int pending = r.Clock.PendingCount;
        r.Source.Raise(kind, 20);
        Assert.Equal(pending + 2, r.Clock.PendingCount);
        Assert.Empty(r.Target.Calls); // no immediate admission
    }

    // ---- AC17/18 ----

    private static Rig SevenTracked()
    {
        var fs = Enumerable.Range(1, 7).Select(i => Rig.Std((ulong)i, 7)).ToArray();
        return Started(fs);
    }

    [Fact]
    public void AC17_RemovalEvents()
    {
        var r = SevenTracked();
        var s = r.Source;

        s.Raise(RawWindowEventKind.Destroyed, 1);
        Assert.Equal(new[] { "WindowRemoved:w1:False" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();
        int d = s.DescribeCount;

        s.Raise(RawWindowEventKind.Minimized, 2);
        Assert.Equal(new[] { "WindowRemoved:w2:True" }, Rig.Desc(r.Target.Calls));
        Assert.Equal(d, s.DescribeCount);
        r.Target.Clear();

        s.Update(3, f => f with { IsVisible = false });
        s.Raise(RawWindowEventKind.Hidden, 3);
        Assert.Equal(new[] { "WindowRemoved:w3:True" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();

        s.Remove(4);
        s.Raise(RawWindowEventKind.Hidden, 4);
        Assert.Equal(new[] { "WindowRemoved:w4:False" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();

        s.Raise(RawWindowEventKind.Hidden, 5);
        Assert.Empty(r.Target.Calls);

        s.Update(6, f => f with { Cloak = CloakKind.App });
        s.Raise(RawWindowEventKind.Cloaked, 6);
        Assert.Equal(new[] { "WindowRemoved:w6:True" }, Rig.Desc(r.Target.Calls));
        r.Target.Clear();

        s.Update(7, f => f with { Cloak = CloakKind.Shell });
        s.Raise(RawWindowEventKind.Cloaked, 7);
        Assert.Empty(r.Target.Calls);
        Assert.NotNull(r.Tracker.Find(Rig.K(7)));

        d = s.DescribeCount;
        s.Raise(RawWindowEventKind.Destroyed, 99);
        s.Raise(RawWindowEventKind.Minimized, 99);
        s.Raise(RawWindowEventKind.Hidden, 99);
        s.Raise(RawWindowEventKind.Cloaked, 99);
        s.Raise(RawWindowEventKind.LocationChanged, 99);
        s.Raise(RawWindowEventKind.TitleChanged, 99);
        Assert.Empty(r.Target.Calls);
        Assert.Equal(d, s.DescribeCount);
    }

    [Fact]
    public void AC17_DestroyedAndMinimizedMakeNoDescribe()
    {
        var r = SevenTracked();
        int d = r.Source.DescribeCount;
        r.Source.Raise(RawWindowEventKind.Destroyed, 1);
        r.Source.Raise(RawWindowEventKind.Minimized, 2);
        Assert.Equal(d, r.Source.DescribeCount);
    }

    [Fact]
    public void AC18_Readmission()
    {
        var r = Started(Rig.Std(2, 7), Rig.Std(1, 7));
        r.Source.Raise(RawWindowEventKind.Minimized, 1);
        Assert.Equal(new[] { "WindowRemoved:w1:True" }, Rig.Desc(r.Target.Calls));
        Assert.Null(r.Tracker.Find(Rig.K(1)));
        r.Target.Clear();

        r.Source.Update(1, f => f with { IsMinimized = false });
        double t = r.Clock.Now;
        r.Source.Raise(RawWindowEventKind.Restored, 1);
        Assert.Empty(r.Target.Calls);
        r.AdvanceTo(t + 0.05);
        var a = Assert.Single(r.Target.Calls);
        Assert.Equal("WindowAdded", a.Name);
        Assert.Equal(Rig.K(1), a.Key);
        Assert.Equal(Rig.K(1), r.Tracker.Tracked[^1].Key);
        Assert.Equal(Rig.K(2), r.Tracker.Tracked[0].Key);
    }

    [Fact]
    public void AC18_TwoHiddenInEventOrder()
    {
        var r = Started(Rig.Std(1, 7), Rig.Std(2, 7));
        r.Source.Update(1, f => f with { IsVisible = false });
        r.Source.Update(2, f => f with { IsVisible = false });
        r.Source.Raise(RawWindowEventKind.Hidden, 1);
        r.Source.Raise(RawWindowEventKind.Hidden, 2);
        Assert.Equal(new[] { "WindowRemoved:w1:True", "WindowRemoved:w2:True" }, Rig.Desc(r.Target.Calls));
    }

    // ---- AC19/20 ----

    [Fact]
    public void AC19_Move()
    {
        var r = Started(Rig.Std(1, 7) with { Frame = new Rect(0, 0, 800, 600) });
        r.Source.Raise(RawWindowEventKind.LocationChanged, 1);
        Assert.Empty(r.Target.Calls);

        r.Source.Update(1, f => f with { Frame = new Rect(10, 0, 800, 600) });
        r.Source.Raise(RawWindowEventKind.LocationChanged, 1);
        var m = Assert.Single(r.Target.Calls);
        Assert.Equal("WindowMoved", m.Name);
        Assert.Equal(new Rect(10, 0, 800, 600), m.Frame);
        Assert.Equal(new Rect(10, 0, 800, 600), r.Tracker.Find(Rig.K(1))!.Frame);

        r.Source.Raise(RawWindowEventKind.LocationChanged, 1);
        Assert.Single(r.Target.Calls);

        r.Source.Update(1, f => f with { Frame = new Rect(10, 0, 820, 600) });
        r.Source.Raise(RawWindowEventKind.LocationChanged, 1);
        Assert.Equal(2, r.Target.Calls.Count);

        r.Source.Update(1, f => f with { Frame = null });
        r.Source.Raise(RawWindowEventKind.LocationChanged, 1);
        Assert.Equal(2, r.Target.Calls.Count);

        r.Source.Remove(1);
        r.Source.Raise(RawWindowEventKind.LocationChanged, 1);
        Assert.Equal(2, r.Target.Calls.Count);
    }

    [Fact]
    public void AC19_UntrackedLocationChangeNoCallNoDescribe()
    {
        var r = Started(Rig.Std(1, 7));
        int d = r.Source.DescribeCount;
        r.Source.Raise(RawWindowEventKind.LocationChanged, 55);
        Assert.Equal(d, r.Source.DescribeCount);
        Assert.Empty(r.Target.Calls);
    }

    [Fact]
    public void AC20_Title()
    {
        var r = Started(Rig.Std(1, 7));
        r.Source.Update(1, f => f with { Title = "a" });
        r.Source.Raise(RawWindowEventKind.TitleChanged, 1);
        r.Source.Raise(RawWindowEventKind.TitleChanged, 1);
        Assert.Equal(2, r.Target.Calls.Count);
        Assert.All(r.Target.Calls, c =>
        {
            Assert.Equal("WindowTitleChanged", c.Name);
            Assert.Equal(Rig.K(1), c.Key);
            Assert.Equal("a", c.Title);
        });
        Assert.Equal("a", r.Tracker.Find(Rig.K(1))!.Title);
        r.Target.Clear();
        r.Source.Raise(RawWindowEventKind.TitleChanged, 77);
        Assert.Empty(r.Target.Calls);
    }

    [Fact]
    public void E7_EmptyTitleStoredAndReported()
    {
        var r = Started(Rig.Std(1, 7));
        r.Source.Update(1, f => f with { Title = "" });
        r.Source.Raise(RawWindowEventKind.TitleChanged, 1);
        Assert.Equal("", Assert.Single(r.Target.Calls).Title);
        Assert.Equal("", r.Tracker.Find(Rig.K(1))!.Title);
    }
}
