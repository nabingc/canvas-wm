using CanvasWm.Engine.Hosting;
using CanvasWm.Engine.Rendering;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tests.HostingSpec;

public sealed class ClockAndTickTests
{
    private sealed class Rig
    {
        public double Time;
        public readonly List<double> Wakes = new();
        public readonly LoopClock Clock;
        public Rig() => Clock = new LoopClock(() => Time, Wakes.Add);
    }

    // ---- AC11 ----

    [Fact]
    public void AC11_SchedulingAndEarlyWake()
    {
        var r = new Rig();
        var ran = new List<string>();
        r.Clock.After(1, () => ran.Add("a"));
        Assert.Equal(new[] { 1.0 }, r.Wakes);
        r.Clock.After(0.5, () => ran.Add("b"));
        Assert.Equal(new[] { 1.0, 0.5 }, r.Wakes);
        r.Clock.After(2, () => ran.Add("c"));
        Assert.Equal(2, r.Wakes.Count);
        Assert.Empty(ran);

        r.Time = 0.4;
        r.Clock.RunDue();
        Assert.Empty(ran);
        Assert.Equal(3, r.Wakes.Count);
        Assert.Equal(0.1, r.Wakes[^1], 1e-9);

        r.Time = 0.5;
        r.Clock.RunDue();
        Assert.Equal(new[] { "b" }, ran);
        Assert.Equal(0.5, r.Wakes[^1], 1e-9);

        r.Wakes.Clear();
        r.Time = 2.0;
        r.Clock.RunDue();
        Assert.Equal(new[] { "b", "a", "c" }, ran);
        Assert.Empty(r.Wakes);
        Assert.Equal(0, r.Clock.PendingCount);
    }

    [Fact]
    public void AC11_SameDueRunsInRegistrationOrder()
    {
        var r = new Rig();
        var ran = new List<int>();
        for (int i = 0; i < 4; i++) { int k = i; r.Clock.After(1, () => ran.Add(k)); }
        r.Time = 1;
        r.Clock.RunDue();
        Assert.Equal(new[] { 0, 1, 2, 3 }, ran);
    }

    [Fact]
    public void AC11_NegativeNaNAndInfinity()
    {
        var r = new Rig();
        var ran = new List<string>();
        r.Clock.After(-1, () => ran.Add("x"));
        r.Clock.After(double.NaN, () => ran.Add("y"));
        Assert.Equal(0.0, r.Wakes[0]);
        r.Clock.RunDue();
        Assert.Equal(new[] { "x", "y" }, ran);

        var r2 = new Rig();
        r2.Clock.After(double.PositiveInfinity, () => { });
        Assert.Empty(r2.Wakes);
        Assert.Equal(1, r2.Clock.PendingCount);
        Assert.Null(r2.Clock.NextDue);
    }

    [Fact]
    public void AC11_ActionAddedDuringRunWaitsForNextWake()
    {
        var r = new Rig();
        var ran = new List<string>();
        r.Clock.After(0, () => { ran.Add("a"); r.Clock.After(0, () => ran.Add("w")); });
        r.Wakes.Clear();
        r.Clock.RunDue();
        Assert.Equal(new[] { "a" }, ran);
        Assert.Equal(new[] { 0.0 }, r.Wakes);
        r.Clock.RunDue();
        Assert.Equal(new[] { "a", "w" }, ran);
    }

    [Fact]
    public void AC11_ThrowingActionPropagatesAndRearms()
    {
        var r = new Rig();
        var ran = new List<string>();
        r.Clock.After(0, () => throw new InvalidOperationException("boom"));
        r.Clock.After(0, () => ran.Add("later"));
        r.Clock.After(5, () => ran.Add("far"));
        r.Wakes.Clear();
        Assert.Throws<InvalidOperationException>(() => r.Clock.RunDue());
        Assert.Equal(2, r.Clock.PendingCount);
        Assert.NotEmpty(r.Wakes);
        Assert.Equal(0.0, r.Wakes[^1], 1e-9);
        r.Clock.RunDue();
        Assert.Equal(new[] { "later" }, ran);
    }

    [Fact]
    public void AC11_SynchronousWakeIsRejected()
    {
        LoopClock? c = null;
        c = new LoopClock(() => 0, _ => c!.RunDue());
        Assert.Throws<InvalidOperationException>(() => c.After(1, () => { }));
    }

    [Fact]
    public void AC11_NowIsMonotonicAndNanSafe()
    {
        double v = 5;
        var c = new LoopClock(() => v, _ => { });
        Assert.Equal(5, c.Now);
        v = 4;
        Assert.Equal(5, c.Now);
        v = double.NaN;
        Assert.Equal(5, c.Now);
        var first = new LoopClock(() => double.NaN, _ => { });
        Assert.Equal(0, first.Now);
        Assert.Throws<ArgumentNullException>(() => c.After(1, null!));
    }

    // ---- AC12 ----

    [Fact]
    public void AC12_OptionsDefaultsAndValidation()
    {
        var d = TickPacingOptions.Default;
        Assert.Equal(0.002, d.FastFlushSeconds);
        Assert.Equal(5, d.FastFlushLimit);
        Assert.Equal(60, d.FallbackRefreshHz);
        Assert.Equal(20, d.MinRefreshHz);
        Assert.Equal(1000, d.MaxRefreshHz);
        Assert.Equal(1.5, d.LateFrameFactor);

        foreach (var bad in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickPacingOptions { FastFlushSeconds = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickPacingOptions { FallbackRefreshHz = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickPacingOptions { MinRefreshHz = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickPacingOptions { MaxRefreshHz = bad });
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new TickPacingOptions { FastFlushLimit = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new TickPacingOptions { MinRefreshHz = 61 });
    }

    [Fact]
    public void AC12_LateFrameFactorValidation()
    {
        foreach (var bad in new[] { 0.99, 0, -1, double.NaN, double.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => new TickPacingOptions { LateFrameFactor = bad });
        Assert.Equal(1.0, new TickPacingOptions { LateFrameFactor = 1.0 }.LateFrameFactor);
        Assert.Equal(2.5, new TickPacingOptions { LateFrameFactor = 2.5 }.LateFrameFactor);
    }

    [Fact]
    public void AC12_PacerSwitchesAfterFiveSuspect()
    {
        var p = new TickPacer();
        Assert.Equal(TickSource.Composition, p.Source);
        Assert.Equal(0, p.SuspectCount);
        p.ReportFlush(true, 0.001);
        p.ReportFlush(false, 0.016);
        p.ReportFlush(true, double.NaN);
        p.ReportFlush(true, 0.0019);
        Assert.Equal(TickSource.Composition, p.Source);
        Assert.Equal(4, p.SuspectCount);
        p.ReportFlush(true, 0.016);
        Assert.Equal(0, p.SuspectCount);
        for (int i = 0; i < 5; i++) p.ReportFlush(true, 0.0);
        Assert.Equal(TickSource.Timer, p.Source);
        p.ReportFlush(true, 0.016);
        p.ReportFlush(true, 0.016);
        Assert.Equal(TickSource.Timer, p.Source);
        p.Reset();
        Assert.Equal(TickSource.Composition, p.Source);
        Assert.Equal(0, p.SuspectCount);
    }

    [Fact]
    public void AC12_TimerPeriod()
    {
        var p = new TickPacer();
        Assert.Equal(1.0 / 144, p.TimerPeriodSeconds(144), 1e-12);
        Assert.Equal(1.0 / 20, p.TimerPeriodSeconds(20), 1e-12);
        foreach (double? hz in new double?[] { null, 0, 10, 5000, double.NaN })
            Assert.Equal(1.0 / 60, p.TimerPeriodSeconds(hz), 1e-12);
    }

    [Fact]
    public void AC12_CoalescerSequential()
    {
        var c = new TickCoalescer();
        Assert.False(c.IsOutstanding);
        Assert.True(c.TryBeginPost());
        Assert.True(c.IsOutstanding);
        Assert.False(c.TryBeginPost());
        c.Delivered();
        Assert.True(c.TryBeginPost());
    }

    [Fact]
    public void AC12_CoalescerConcurrentExactlyOne()
    {
        var c = new TickCoalescer();
        int wins = 0;
        using var start = new ManualResetEventSlim(false);
        var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
        {
            start.Wait();
            for (int i = 0; i < 10_000; i++)
                if (c.TryBeginPost()) Interlocked.Increment(ref wins);
        })).ToArray();
        foreach (var t in threads) t.Start();
        start.Set();
        foreach (var t in threads) t.Join();
        Assert.Equal(1, wins);
    }

    // ---- AC13 ----

    [Fact]
    public void AC13_NullServices()
    {
        NullRenderer.Instance.Render(RenderPlan.Empty);
        Assert.Throws<ArgumentNullException>(() => NullRenderer.Instance.Render(null!));
        var c = NullCapture.Instance;
        var k = new WindowKey("w");
        c.Start(k);
        Assert.Null(c.Latest(k));
        c.SetRate(k, 30);
        c.Hold(k, true);
        c.Stop(k);
        Assert.Throws<ArgumentNullException>(() => c.Start(null!));
        Assert.Throws<ArgumentNullException>(() => c.Stop(null!));
        Assert.Throws<ArgumentNullException>(() => c.SetRate(null!, 1));
        Assert.Throws<ArgumentNullException>(() => c.Hold(null!, true));
        Assert.Throws<ArgumentNullException>(() => c.Latest(null!));
        Assert.Same(NullRenderer.Instance, NullRenderer.Instance);
        Assert.Same(NullCapture.Instance, NullCapture.Instance);
    }
}
