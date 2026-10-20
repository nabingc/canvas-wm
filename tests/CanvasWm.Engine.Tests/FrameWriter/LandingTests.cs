using CanvasWm.Core.Geometry;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;
using static CanvasWm.Engine.Tests.FrameWriterSpec.Wr;

namespace CanvasWm.Engine.Tests.FrameWriterSpec;

public sealed class LandingTests
{
    [Fact]
    public void AC21_reland_at_rest_schedules_delayed_pass()
    {
        var w = L3AllDiffer();
        w.E.ReLand();
        Assert.Equal(1, w.G);
        Assert.Equal(StagePhase.Desk, w.E.Phase);
        Assert.Equal(1, w.Renders);
        Assert.Equal(1, w.Pending);
        Assert.Empty(w.Writes());
        w.At(100.033);
        Assert.Empty(w.Writes());
        w.At(100.034);
        var wr = w.Writes();
        Assert.Equal(3, wr.Count);
        Assert.Equal(new[] { K("a"), K("b"), K("c") }, wr.Select(x => x.Key).ToArray());
        Assert.Single(wr.Select(x => x.BatchId).Distinct());
    }

    [Fact]
    public void AC21_two_relands_coalesce()
    {
        var w = L3AllDiffer();
        w.E.ReLand();
        w.At(100.02);
        w.E.ReLand();
        Assert.Equal(2, w.G);
        w.At(100.05);
        Assert.Empty(w.Writes());
        w.At(100.054);
        Assert.Equal(3, w.Writes().Count);
        Assert.Single(w.Writes().Select(x => x.BatchId).Distinct());
    }

    [Fact]
    public void AC21_reland_then_suspend_never_writes()
    {
        var w = L3AllDiffer();
        w.E.ReLand();
        w.At(100.01);
        w.E.Suspend();
        w.At(105);
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void AC21_reland_suspended_or_not_started_is_inert()
    {
        var w = L3AllDiffer();
        w.E.Suspend();
        int g = w.E.PlacementGeneration, r = w.Ren.Plans.Count, p = w.Clock.PendingCount;
        w.E.ReLand();
        Assert.Equal(g, w.E.PlacementGeneration);
        Assert.Equal(r, w.Ren.Plans.Count);
        Assert.Equal(p, w.Clock.PendingCount);

        var f = new Wr(null, null, Array.Empty<(string, Rect)>());
        var e = new DisplayEngine(f.Key, VA, EnginePolicy.Default,
            new EngineServices(f.Win, f.Cap, f.Clock, f.Ren, f.Lim, f.Disp));
        int plans = f.Ren.Plans.Count, pend = f.Clock.PendingCount;
        e.ReLand();
        Assert.Equal(0, e.PlacementGeneration);
        Assert.Equal(pend, f.Clock.PendingCount);
        Assert.Equal(plans, f.Ren.Plans.Count);
    }

    [Theory]
    [InlineData(StagePhase.Landing)]
    [InlineData(StagePhase.Moving)]
    public void AC22_moving_or_landing_places_at_once(StagePhase ph)
    {
        var w = L3AllDiffer();
        w.E.SetPhase(ph);
        w.E.ReLand();
        Assert.Equal(3, w.Writes().Count);
        Assert.Equal(0, w.G);
        Assert.Equal(w.E.PlacementGeneration, w.E.PlacedGeneration);
    }

    [Fact]
    public void AC22_fading_bumps_and_delays()
    {
        var w = L3AllDiffer();
        w.E.SetPhase(StagePhase.Fading);
        w.E.ReLand();
        Assert.Equal(1, w.G);
        Assert.Empty(w.Writes());
        w.Adv(0.034);
        Assert.Equal(3, w.Writes().Count);
    }

    [Fact]
    public void AC22_overview_does_nothing()
    {
        var w = L3AllDiffer();
        w.E.SetPhase(StagePhase.Overview);
        w.E.ReLand();
        Assert.Empty(w.Writes());
        Assert.Equal(0, w.G);
        Assert.Equal(0, w.Pending);
    }

    [Fact]
    public void AC22_note_reland_in_overview_does_not_render()
    {
        var w = L3AllDiffer();
        w.E.SetPhase(StagePhase.Overview);
        w.E.ReLand();
        Assert.Equal(0, w.Renders);
    }

    [Fact]
    public void AC23_landing_counting()
    {
        var w = L3AllDiffer();
        w.E.PlaceWindows(w.E.PlacementGeneration);
        var wr = w.Writes();
        Assert.Equal(3, wr.Count);
        Assert.Single(wr.Select(x => x.BatchId).Distinct());
        Assert.Equal(StagePhase.Landing, w.E.Phase);
        Near(100, w.E.PlacementStarted);
        w.Changes = 0;
        w.Complete(100.08);
        Near(100.08, w.E.PlacementDone);
        Assert.Equal(StagePhase.Fading, w.E.Phase);
        Assert.Equal(1, w.Pending);
        Assert.Equal(1, w.Changes);
        w.At(100.23);
        Assert.Equal(StagePhase.Fading, w.E.Phase);
        w.At(100.24);
        Assert.Equal(StagePhase.Desk, w.E.Phase);
    }

    [Fact]
    public void AC23_suspend_before_completion_abandons_landing()
    {
        var w = L3AllDiffer();
        w.E.PlaceWindows(w.E.PlacementGeneration);
        w.At(100.07);
        w.E.Suspend();
        w.Complete(100.08);
        Assert.Null(w.E.PlacementDone);
        Assert.Equal(StagePhase.Desk, w.E.Phase);
        foreach (var n in new[] { "a", "b", "c" })
        {
            Assert.Equal(2, w.S(n).Expected.Length);
        }

        Assert.Equal(R(10, 35, 705, 855), w.Known("a"));
        Assert.Equal(R(725, 35, 705, 423), w.Known("b"));
        Assert.Equal(R(725, 468, 705, 422), w.Known("c"));
        Assert.Equal(0, w.Pending);
    }

    [Fact]
    public void AC23_nothing_to_write_lands_inside_call()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", B3), ("c", C3) });
        w.E.PlaceWindows(w.E.PlacementGeneration);
        Assert.Equal(StagePhase.Fading, w.E.Phase);
        Near(100, w.E.PlacementDone);
    }

    [Fact]
    public void AC23_same_generation_twice_only_latest_pass_finishes()
    {
        var w = L3AllDiffer();
        int g = w.E.PlacementGeneration;
        w.E.PlaceWindows(g);
        w.E.PlaceWindows(g);
        Assert.Single(w.Writes().Select(x => x.BatchId).Distinct());
        Assert.Equal(3, w.Writes().Count);
        Near(100, w.E.PlacementDone);
        Assert.Equal(StagePhase.Fading, w.E.Phase);
        Assert.Equal(1, w.Pending);
        w.Complete(100.08);
        Near(100, w.E.PlacementDone);
        Assert.Equal(1, w.Pending);
    }

    private static Wr Landed(Wr w)
    {
        w.E.PlaceWindows(w.E.PlacementGeneration);
        w.Complete(100.08);
        return w;
    }

    [Fact]
    public void AC24_settle_reads_in_order_saves_renders_once()
    {
        var w = Landed(L3AllDiffer());
        int saves = w.Saves, renders = w.Renders;
        w.Adv(0.16);
        Near(100.24, w.Clock.Now);
        Assert.Equal(StagePhase.Desk, w.E.Phase);
        Assert.Equal(new[] { K("a"), K("b"), K("c") }, w.Reads().ToArray());
        Assert.Equal(1, w.Saves - saves);
        Assert.Equal(1, w.Renders - renders);
    }

    [Fact]
    public void AC24_cover_fade_changes_settle_time()
    {
        var w = Landed(L3AllDiffer(EnginePolicy.Default with { CoverFade = 0.3 }));
        w.At(100.39);
        Assert.Equal(StagePhase.Fading, w.E.Phase);
        w.At(100.40);
        Assert.Equal(StagePhase.Desk, w.E.Phase);
    }

    [Fact]
    public void AC24_reland_during_fading_cancels_old_settle()
    {
        var w = Landed(L3AllDiffer());
        w.At(100.1);
        w.E.ReLand();
        w.At(100.24);
        Assert.Empty(w.Reads());
        Assert.NotEqual(StagePhase.Desk, w.E.Phase);
    }

    [Fact]
    public void AC24_windows_without_target_are_not_read()
    {
        var w = new Wr(L3(), null, new[] { ("a", R(10, 35, 600, 855)), ("b", B3), ("z", R(0, 0, 50, 50)), ("c", C3) });
        w.E.PlaceWindows(w.E.PlacementGeneration);
        w.Complete(100.08);
        w.Adv(0.16);
        Assert.Equal(new[] { K("a"), K("b"), K("c") }, w.Reads().ToArray());
    }

    // ---------- AC25 / AC26 ----------

    private static Wr Drift()
    {
        var w = new Wr(L2(), "a", new[] { ("a", R(10, 35, 705, 855)), ("b", R(725, 35, 705, 855)) });
        w.E.ReLand();
        w.Adv(0.034);
        w.At(100.084);
        w.Win.Step();
        w.At(100.244);
        return w;
    }

    [Fact]
    public void AC25_parked_window_that_crept_back_is_reparked()
    {
        var w = Drift();
        Assert.Equal(new[] { K("a"), K("b") }, w.Reads().ToArray());
        int n = w.Writes().Count;
        w.Win.SetFrameDirect(K("b"), R(1200, 400, 705, 855));
        w.Win.Step();
        w.Win.Step();
        Assert.Equal(R(1200, 400, 705, 855), w.Known("b"));
        var added = w.Writes().Skip(n).ToList();
        Assert.Single(added);
        Assert.Equal(new FrameWriteRecord(K("b"), R(1439, 899, 705, 855), null), added[0]);
    }

    private static void Guard(Action<Wr> prepare, Rect moved)
    {
        var w = Drift();
        int n = w.Writes().Count;
        w.Win.SetFrameDirect(K("b"), moved);
        Rect before = w.Known("b");
        prepare(w);
        w.Win.Step();
        w.Win.Step();
        Assert.Equal(before, w.Known("b"));
        Assert.Equal(n, w.Writes().Count);
        Assert.DoesNotContain(w.Writes().Skip(n), x => x.BatchId is null && x.Key == K("b") && x.Target == R(1439, 899, 705, 855));
    }

    private static readonly Rect Moved = R(1200, 400, 705, 855);

    [Fact]
    public void AC25_guard_generation_bumped() => Guard(w => w.E.ReLand(), Moved);

    [Fact]
    public void AC25_guard_phase_landing() => Guard(w => w.E.SetPhase(StagePhase.Landing), Moved);

    [Fact]
    public void AC25_guard_interaction_active() => Guard(w => w.E.InteractionActive = true, Moved);

    [Fact]
    public void AC25_guard_write_in_flight()
    {
        var w = Drift();
        w.Win.SetFrameDirect(K("b"), Moved);
        bool issued = w.E.WriteOne(K("b"), P(1439, 899, 705, 850));
        Assert.True(issued);
        int n = w.Writes().Count;
        Rect before = w.Known("b");
        w.Win.Step(K("a"));
        w.Win.Step(K("b")); // oldest op for b is the drift read
        Assert.Equal(before, w.Known("b"));
        Assert.Equal(n, w.Writes().Count);
    }

    [Fact]
    public void AC25_guard_rests_parked() => Guard(_ => { }, R(1439, 860, 705, 855));

    [Fact]
    public void AC25_guard_barely_visible() => Guard(_ => { }, R(1430, 890, 705, 855));

    [Fact]
    public void AC26_tile_drift_logs_once_without_update_or_write()
    {
        var w = Drift();
        int n = w.Writes().Count;
        w.Win.SetFrameDirect(K("a"), R(16, 35, 1420, 855));
        w.Win.Step();
        w.Win.Step();
        Assert.Single(w.Logs);
        Assert.Contains("a", w.Logs[0]);
        Assert.Equal(R(10, 35, 1420, 855), w.Known("a"));
        Assert.Equal(n, w.Writes().Count);
    }

    [Fact]
    public void AC26_delta_4_does_not_log()
    {
        var w = Drift();
        w.Win.SetFrameDirect(K("a"), R(14, 35, 1420, 855));
        w.Win.Step();
        w.Win.Step();
        Assert.Empty(w.Logs);
    }
}
