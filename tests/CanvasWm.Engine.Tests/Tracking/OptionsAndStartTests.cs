using System.Reflection;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;
using CanvasWm.Engine.Tracking;

namespace CanvasWm.Engine.Tests.TrackingSpec;

public sealed class OptionsAndStartTests
{
    [Fact]
    [Trait("Assumption", "A13")]
    public void AC9_KeyFor()
    {
        Assert.Equal("w4211", WindowTracker.KeyFor(4211).Value);
        Assert.Equal("w18446744073709551615", WindowTracker.KeyFor(ulong.MaxValue).Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowTracker.KeyFor(0));
    }

    [Fact]
    public void AC10_Defaults()
    {
        var d = TrackerOptions.Default;
        Assert.Equal(0.05, d.FirstRecheckSeconds);
        Assert.Equal(0.4, d.SecondRecheckSeconds);
        Assert.Equal(3.0, d.RescanSeconds);
        Assert.Equal(0.05, d.OffScreenAdmitBelow);
        Assert.Equal("App", d.FallbackAppName);
        Assert.Same(TrackerOptions.Default, TrackerOptions.Default);
    }

    [Fact]
    [Trait("Assumption", "A8")]
    public void AC10_ExactlyFivePublicInstanceProperties()
    {
        var props = typeof(TrackerOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "FallbackAppName", "FirstRecheckSeconds", "OffScreenAdmitBelow", "RescanSeconds", "SecondRecheckSeconds" }, props);
    }

    public static TheoryData<string, double> BadSeconds()
    {
        var d = new TheoryData<string, double>();
        foreach (var n in new[] { "FirstRecheckSeconds", "SecondRecheckSeconds", "RescanSeconds" })
            foreach (var v in new[] { 0, -1, double.NaN, double.PositiveInfinity })
                d.Add(n, v);
        return d;
    }

    private static TrackerOptions Set(string name, double v) => name switch
    {
        "FirstRecheckSeconds" => new TrackerOptions { FirstRecheckSeconds = v },
        "SecondRecheckSeconds" => new TrackerOptions { SecondRecheckSeconds = v },
        "RescanSeconds" => new TrackerOptions { RescanSeconds = v },
        "OffScreenAdmitBelow" => new TrackerOptions { OffScreenAdmitBelow = v },
        _ => throw new InvalidOperationException(),
    };

    [Theory]
    [MemberData(nameof(BadSeconds))]
    public void AC10_BadSecondsThrow(string name, double v)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Set(name, v));
        Assert.Equal(name, ex.ParamName);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void AC10_BadOffScreenThrows(double v)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => Set("OffScreenAdmitBelow", v));
        Assert.Equal("OffScreenAdmitBelow", ex.ParamName);
    }

    [Fact]
    public void AC10_OffScreenBoundsAndFallbackName()
    {
        Assert.Equal(0, Set("OffScreenAdmitBelow", 0).OffScreenAdmitBelow);
        Assert.Equal(1, Set("OffScreenAdmitBelow", 1).OffScreenAdmitBelow);
        Assert.Throws<ArgumentException>(() => new TrackerOptions { FallbackAppName = "" });
        Assert.Throws<ArgumentNullException>(() => new TrackerOptions { FallbackAppName = null! });
    }

    [Fact]
    public void AC11_ConstructionMakesNoCalls()
    {
        var r = new Rig();
        Assert.Equal(0, r.Source.EnumerateCount);
        Assert.Equal(0, r.Source.DescribeCount);
        Assert.Equal(0, r.Source.SubscriberCount);
        Assert.Empty(r.Target.Calls);
        Assert.Equal(0, r.Clock.PendingCount);
        Assert.False(r.Tracker.IsStarted);
        Assert.False(r.Tracker.IsReady);
    }

    [Fact]
    public void AC11_NullArgumentsThrow()
    {
        var c = new ManualClock();
        var s = new FakeWindowSource();
        var w = new FakeWindowSystem();
        var t = new RecordingTrackerTarget(c);
        var p = ClassifierPolicy.Default;
        Assert.Throws<ArgumentNullException>(() => new WindowTracker(null!, w, c, t, p));
        Assert.Throws<ArgumentNullException>(() => new WindowTracker(s, null!, c, t, p));
        Assert.Throws<ArgumentNullException>(() => new WindowTracker(s, w, null!, t, p));
        Assert.Throws<ArgumentNullException>(() => new WindowTracker(s, w, c, null!, p));
        Assert.Throws<ArgumentNullException>(() => new WindowTracker(s, w, c, t, null!));
        Assert.Same(TrackerOptions.Default, new WindowTracker(s, w, c, t, p).Options);
    }

    [Fact]
    public void E1_SetPolicyNullThrows_NoStateChange()
    {
        var r = new Rig();
        Assert.Throws<ArgumentNullException>(() => r.Tracker.SetPolicy(null!));
        Assert.Same(ClassifierPolicy.Default, r.Tracker.Policy);
    }

    [Fact]
    [Trait("Assumption", "A8")]
    public void AC12_Start()
    {
        var r = new Rig();
        r.Source.Put(Rig.Std(4, 9) with { IsResizable = false });
        r.Source.Put(Rig.Std(3, 8) with { Frame = new Rect(100, 100, 100, 60) });
        r.Source.Put(Rig.Std(2, 7) with { AppName = null, AppId = "" });
        r.Source.Put(Rig.Std(1, 7));
        Assert.Equal(new ulong[] { 1, 2, 3, 4 }, r.Source.Enumerate().Select(f => f.Handle).ToArray());
        int enumBefore = r.Source.EnumerateCount;

        r.Tracker.Start();

        Assert.Equal(1, r.Source.SubscriberCount);
        Assert.Equal(enumBefore + 1, r.Source.EnumerateCount);
        var call = Assert.Single(r.Target.Calls);
        Assert.Equal("Ready", call.Name);
        Assert.Equal(0, call.At, 9);
        var ready = r.Target.ReadyWindows!;
        Assert.Equal(new[] { "w1", "w2", "w4" }, ready.Select(w => w.Key.Value).ToArray());
        Assert.Equal("App", ready[1].AppName);
        Assert.Null(ready[1].App);
        Assert.Equal(new AppId("app7"), ready[0].App);
        Assert.Equal("t1", ready[0].Title);
        Assert.Equal(new Rect(100, 100, 900, 700), ready[0].Frame);
        Assert.True(ready[0].Dockable);
        Assert.False(ready[2].Dockable);
        Assert.True(r.Tracker.IsReady);
        Assert.True(r.Tracker.IsStarted);
        Assert.Equal(ready, r.Tracker.Tracked);
        Assert.Equal(1, r.Clock.PendingCount);

        r.Clock.Advance(3);
        Assert.Single(r.Target.Calls);
        int e = r.Source.EnumerateCount;
        int pend = r.Clock.PendingCount;
        r.Tracker.Start();
        Assert.Single(r.Target.Calls);
        Assert.Equal(e, r.Source.EnumerateCount);
        Assert.Equal(pend, r.Clock.PendingCount);
        Assert.Equal(1, r.Source.SubscriberCount);
    }

    [Fact]
    public void AC12_StartAfterDisposeThrows()
    {
        var r = new Rig();
        r.Tracker.Dispose();
        Assert.Throws<ObjectDisposedException>(() => r.Tracker.Start());
    }

    [Fact]
    public void AC12_FallbackNameOptionUsed()
    {
        var r = new Rig(options: new TrackerOptions { FallbackAppName = "Zed" });
        r.Source.Put(Rig.Std(1, 7) with { AppName = null });
        r.Tracker.Start();
        Assert.Equal("Zed", r.Tracker.Tracked[0].AppName);
    }

    [Fact]
    public void AC12_NoTargetCallsBeforeReady_ExceptReady()
    {
        var r = new Rig();
        r.Put(Rig.Std(1, 7), Rig.Std(2, 7) with { HasMaximizeButton = false });
        r.Target.OnCall = c => Assert.Equal("Ready", c.Name);
        r.Tracker.Start();
        Assert.Equal(new[] { "Ready" }, Rig.Names(r.Target.Calls));
    }

    // ---- AC13 admission ----

    private static Rig AdmitRig(Rect frame, Action<Rig>? setup = null)
    {
        var r = new Rig();
        r.Put(Rig.Std(1, 7) with { Frame = frame });
        setup?.Invoke(r);
        r.Tracker.Start();
        return r;
    }

    [Fact]
    public void AC13_OffScreenFullyInvisibleIsAdmitted()
    {
        var r = AdmitRig(new Rect(-800, 300, 600, 400));
        Assert.Equal(0.0, r.Sys.VisibleFraction(new Rect(-800, 300, 600, 400)), 9);
        Assert.Single(r.Tracker.Tracked);
    }

    [Fact]
    public void AC13_TenPercentVisibleIsNotAdmitted()
    {
        var f = new Rect(-540, 300, 600, 400);
        var r = AdmitRig(f);
        Assert.Equal(0.1, r.Sys.VisibleFraction(f), 9);
        Assert.Empty(r.Tracker.Tracked);
    }

    [Fact]
    public void AC13_ThreePercentVisibleIsAdmitted()
    {
        var f = new Rect(-582, 300, 600, 400);
        var r = AdmitRig(f);
        Assert.Equal(0.03, r.Sys.VisibleFraction(f), 9);
        Assert.Single(r.Tracker.Tracked);
    }

    [Fact]
    public void AC13_HeldTenPercentWindowIsAdmitted()
    {
        var r = AdmitRig(new Rect(-540, 300, 600, 400), x => x.Target.Held.Add(Rig.K(1)));
        Assert.Single(r.Tracker.Tracked);
    }

    [Fact]
    [Trait("Assumption", "A9")]
    public void AC13_CentreExactlyOnDisplayRightEdgeIsOffDisplay()
    {
        var f = new Rect(1140, 100, 600, 400);
        var r = AdmitRig(f);
        Assert.Equal(0.5, r.Sys.VisibleFraction(f), 9);
        Assert.Empty(r.Tracker.Tracked);
    }

    [Fact]
    [Trait("Assumption", "A9")]
    public void AC13_CentreOnLeftEdgeIsOnDisplay()
    {
        // centre x = 0 -> inside half-open [0,1440)
        var r = AdmitRig(new Rect(-300, 100, 600, 400));
        Assert.Single(r.Tracker.Tracked);
    }

    [Fact]
    public void AC13_NoDisplaysAtAll_E3()
    {
        var r = new Rig();
        r.Target.Displays.Clear();
        r.Sys.Screens = Array.Empty<Rect>();
        r.Put(Rig.Std(1, 7));
        r.Tracker.Start();
        Assert.Single(r.Tracker.Tracked);
    }

    [Fact]
    public void AC13_RejectedWindowAdmittedByNextRescanAfterHeld()
    {
        var r = AdmitRig(new Rect(-540, 300, 600, 400));
        Assert.Empty(r.Tracker.Tracked);
        r.Target.Held.Add(Rig.K(1));
        r.Clock.Advance(3);
        Assert.Equal(new[] { "Ready", "WindowAdded" }, Rig.Names(r.Target.Calls));
        Assert.Equal(3, r.Target.Calls[1].At, 9);
    }
}
