using CanvasWm.Core.Geometry;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;
using static CanvasWm.Engine.Tests.FrameWriterSpec.Wr;

namespace CanvasWm.Engine.Tests.FrameWriterSpec;

public sealed class LearnTests
{
    /// <summary>AC11 setup: L3, a and b at targets, c known (725,35,705,855), fake min (0,minH) on c.</summary>
    private static Wr Rig11(double minH = 600) 
    {
        var w = L3C();
        w.Win.SetConstraints(K("c"), min: new Size(0, minH));
        return w;
    }

    /// <summary>Steps 1 to 3 of AC11: write, complete, advance to the recheck read.</summary>
    private static void ToRead(Wr w)
    {
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.At(100.30);
    }

    [Fact]
    public void AC11_full_timeline()
    {
        var w = Rig11();
        w.E.PresentDirectly();
        var wr = w.Writes();
        Assert.Single(wr);
        Assert.Equal(K("c"), wr[0].Key);
        Assert.Equal(R(725, 468, 705, 422), wr[0].Target);
        Assert.NotNull(wr[0].BatchId);

        w.Complete(100.05);
        Assert.Equal(R(725, 468, 705, 600), w.Known("c"));
        Assert.Equal(1, w.Pending);
        w.At(100.30);
        Assert.Equal(new[] { K("c") }, w.Reads().ToArray());

        int renders = w.Renders;
        w.Win.Step();
        Assert.Equal(0, w.S("c").PendingRechecks);
        Assert.Equal(R(725, 468, 705, 600), w.Known("c"));
        Assert.Equal(2, w.S("c").Expected.Length);
        Assert.Equal(new Size(0, 600), w.S("c").Minimum);
        Assert.Equal(new Size(0, 600), w.Lim.Minimum(w.App));
        Assert.Single(w.Logs);
        Assert.Equal(1, w.G);
        Assert.Equal(StagePhase.Desk, w.E.Phase);
        Assert.Equal(1, w.Renders - renders);

        int writes = w.Writes().Count;
        w.Adv(0.034);
        var pass = w.Writes().Skip(writes).ToList();
        Assert.Single(pass);
        Assert.Equal(K("c"), pass[0].Key);
        Assert.Equal(w.PwP(new Size(1001, 600), new Size(200, 600)).Rect, pass[0].Target);
        Assert.NotNull(pass[0].BatchId);
        Assert.Equal(StagePhase.Landing, w.E.Phase);
        Assert.Equal(w.G0 + 1, w.E.PlacedGeneration);
        Near(100.334, w.E.PlacementStarted);
        Assert.Null(w.E.PlacementDone);
        Assert.True(w.E.Window(K("c"))!.Parked);

        w.Complete(100.384);
        Near(100.384, w.E.PlacementDone);
        Assert.Equal(StagePhase.Fading, w.E.Phase);
        Assert.Equal(new Size(0, 600), w.Lim.Minimum(w.App));
        Assert.Equal(1, w.G);

        int saves = w.Saves;
        int reads = w.Reads().Count;
        w.Adv(0.16);
        Assert.Equal(StagePhase.Desk, w.E.Phase);
        Assert.Equal(new[] { K("a"), K("b"), K("c") }, w.Reads().Skip(reads).ToArray());
        Assert.Equal(1, w.Saves - saves);

        int wn = w.Writes().Count;
        w.Win.Pump();
        Assert.Equal(wn, w.Writes().Count);
    }

    [Fact]
    public void AC12_screen_factor_blocks_760()
    {
        var w = Rig11(760);
        ToRead(w);
        w.Win.Step();
        Assert.Null(w.S("c").Minimum);
        Assert.Empty(w.Lim.Entries);
        Assert.Equal(0, w.G);
        Assert.Empty(w.Logs);
        Assert.Equal(0, w.Pending);
        w.At(100.9);
        Assert.True(w.E.IsExpected(K("c"), R(725, 468, 705, 760)));
    }

    [Fact]
    public void AC12_699_learns()
    {
        var w = Rig11(699);
        ToRead(w);
        w.Win.Step();
        Assert.Equal(new Size(0, 699), w.S("c").Minimum);
    }

    [Fact]
    public void AC12_701_does_not()
    {
        var w = Rig11(701);
        ToRead(w);
        w.Win.Step();
        Assert.Null(w.S("c").Minimum);
    }

    [Fact]
    public void AC12_existing_minimum_100_550_becomes_100_600()
    {
        var w = Rig11();
        w.E.SetLimits(K("c"), new Size(100, 550), null, null);
        w.E.PresentDirectly();
        w.Complete(100.05);
        Assert.Equal(new Size(100, 550), w.S("c").Minimum);
        w.At(100.30);
        w.Win.Step();
        Assert.Equal(new Size(100, 600), w.S("c").Minimum);
        Assert.Equal(new Size(100, 600), w.Lim.Minimum(w.App));
    }

    [Fact]
    public void AC12_existing_minimum_100_650_is_lowered_first()
    {
        var w = Rig11();
        w.E.SetLimits(K("c"), new Size(100, 650), null, null);
        // NOTE: PresentDirectly would target height 650 here (own minimum feeds the projection), so the
        // spec's "600 < 648" lowering is only reachable with an explicit tile write of the AC11 target.
        Assert.True(w.E.WriteOne(K("c"), T(725, 468, 705, 422)));
        w.Complete(100.05);
        Assert.Equal(new Size(100, 600), w.S("c").Minimum);
        w.At(100.30);
        w.Win.Step();
        Assert.Equal(new Size(100, 600), w.S("c").Minimum);
    }

    [Fact]
    public void AC12_natural_is_raised_on_qualifying_axis()
    {
        var w = Rig11();
        w.E.SetLimits(K("c"), null, null, new Size(400, 500));
        ToRead(w);
        w.Win.Step();
        Assert.Equal(new Size(400, 600), w.S("c").Natural);
    }

    [Fact]
    public void AC13_smaller_learns_maximum_not_store()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)), ("c", C3) });
        w.Win.SetConstraints(K("b"), max: new Size(300, 10000));
        w.E.PresentDirectly();
        Assert.Equal(R(725, 35, 705, 423), w.Writes()[0].Target);
        w.Complete(100.05);
        w.At(100.30);
        w.Win.Step();
        Assert.Equal(new Size(300, double.PositiveInfinity), w.S("b").Maximum);
        Assert.Empty(w.Lim.Entries);
        Assert.Equal(0, w.Lim.PersistCount);
        Assert.Single(w.Logs);
        Assert.Equal(T(928, 35, 300, 423), w.E.ComputeTargets()[K("b")]);
        Assert.Equal(1, w.G);
        int n = w.Writes().Count;
        w.Adv(0.034);
        var pass = w.Writes().Skip(n).ToList();
        Assert.Single(pass);
        Assert.Equal(K("b"), pass[0].Key);
        Assert.Equal(R(928, 35, 300, 423), pass[0].Target);
    }

    [Fact]
    public void AC14_refusal_kept()
    {
        var w = new Wr(L2(), "a", new[] { ("a", R(10, 35, 1420, 855)), ("b", R(725, 35, 705, 855)) },
            EnginePolicy.Default with { ParkOutOfViewSmall = true });
        w.Win.SetConstraints(K("b"), min: new Size(705, 855));
        var target = w.E.ComputeTargets()[K("b")];
        Assert.Equal(CanvasWm.Core.Parking.ParkGeometry.ParkSmallFrame(w.E.ParkContext(), new Size(705, 855)), target.Rect);
        Assert.True(w.E.WriteOne(K("b"), target));
        w.Complete(100.05);
        Assert.Equal(R(1439, 899, 705, 855), w.Known("b"));
        Assert.Equal(1, w.S("b").PendingRechecks);
        w.At(100.30);
        w.Win.Step();
        Assert.Equal(new Size(705, 0), w.S("b").Minimum);
        Assert.Equal(new Size(705, 0), w.Lim.Minimum(w.App));
        Assert.Equal(2, w.Logs.Count);
        Assert.Equal(0, w.G);
        Assert.Equal(0, w.Pending);
    }

    [Fact]
    public void AC15_park_accepted_lowers_store_and_relands()
    {
        var w = L3AllDiffer();
        w.Lim.Learn(w.App, new Size(800, 600));
        Assert.True(w.E.WriteOne(K("b"), P(1439, 899, 705, 423)));
        w.Complete(100.05);
        Assert.Equal(new Size(705, 423), w.Lim.Minimum(w.App));
        Assert.Equal(1, w.G);
        Assert.Equal(1, w.Pending);
    }

    [Fact]
    public void AC15_own_minimum_blocks_reland()
    {
        var w = L3AllDiffer();
        w.Lim.Learn(w.App, new Size(800, 600));
        w.E.SetLimits(K("b"), new Size(10, 10), null, null);
        Assert.True(w.E.WriteOne(K("b"), P(1439, 899, 705, 423)));
        w.Complete(100.05);
        Assert.Equal(new Size(705, 423), w.Lim.Minimum(w.App));
        Assert.Equal(0, w.G);
    }

    [Fact]
    public void AC15_empty_store_no_reland()
    {
        var w = L3AllDiffer();
        Assert.True(w.E.WriteOne(K("b"), P(1439, 899, 705, 423)));
        w.Complete(100.05);
        Assert.Equal(0, w.G);
    }

    [Fact]
    public void AC16_park_refused_taller_learns_and_repark()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)), ("c", C3) });
        w.Win.SetConstraints(K("b"), min: new Size(0, 600));
        Assert.True(w.E.WriteOne(K("b"), P(1439, 899, 705, 423)));
        w.Complete(100.05);
        Assert.Equal(R(1439, 899, 705, 600), w.Known("b"));
        w.At(100.30);
        w.Win.Step();
        Assert.Equal(new Size(0, 600), w.S("b").Minimum);
        Assert.Equal(new Size(0, 600), w.Lim.Minimum(w.App));
        Assert.Equal(1, w.G);
        int n = w.Writes().Count;
        w.Adv(0.034);
        var pass = w.Writes().Skip(n).Where(x => x.Key == K("b")).ToList();
        Assert.Single(pass);
        Assert.Equal(w.PwP(new Size(1001, 600), new Size(200, 600)).Rect, pass[0].Target);
    }

    [Fact]
    public void AC16_park_smaller_by_30_does_nothing()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)), ("c", C3) });
        w.Lim.Learn(w.App, new Size(800, 600));
        w.Win.SetConstraints(K("b"), max: new Size(675, 393));
        Assert.True(w.E.WriteOne(K("b"), P(1439, 899, 705, 423)));
        w.Complete(100.05);
        Assert.Equal(0, w.S("b").PendingRechecks);
        Assert.Equal(0, w.Pending);
        Assert.Equal(new Size(800, 600), w.Lim.Minimum(w.App));
    }

    [Fact]
    public void AC17_null_read_uses_known_frame()
    {
        var w = Rig11();
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.Win.SetFails(K("c"), true);
        w.At(100.30);
        w.Win.Step();
        Assert.Equal(R(725, 468, 705, 600), w.Known("c"));
        Assert.Equal(new Size(0, 600), w.S("c").Minimum);
    }

    [Fact]
    public void AC17_frame_reset_before_read_learns_nothing()
    {
        var w = Rig11();
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.Win.SetFrameDirect(K("c"), R(725, 468, 705, 422));
        w.At(100.30);
        w.Win.Step();
        Assert.Null(w.S("c").Minimum);
        Assert.Equal(R(725, 468, 705, 422), w.Known("c"));
        Assert.Equal(0, w.G);
    }

    [Fact]
    public void AC17_captured_visible_area_is_used()
    {
        var w = Rig11();
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.E.VisibleAreaChanged(new Rect(0, 25, 1440, 700));
        w.At(100.30);
        w.Win.Pump();
        Assert.Equal(new Size(0, 600), w.S("c").Minimum);
    }

    [Fact]
    public void AC18_suspend_before_fire_stops_recheck()
    {
        var w = Rig11();
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.At(100.1);
        w.E.Suspend();
        w.At(100.30);
        Assert.Empty(w.Reads());
        Assert.Equal(0, w.S("c").PendingRechecks);
        Assert.Null(w.S("c").Minimum);
        Assert.Empty(w.Lim.Entries);
        Assert.Equal(1, w.G);
    }

    [Fact]
    public void AC18_suspend_between_read_and_completion()
    {
        var w = Rig11();
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.At(100.30);
        Assert.Single(w.Reads());
        w.E.Suspend();
        w.Win.SetFrameDirect(K("c"), R(1, 2, 3, 4));
        w.Win.Step();
        Assert.Null(w.S("c").Minimum);
        Assert.Empty(w.Lim.Entries);
        Assert.Equal(R(725, 468, 705, 600), w.Known("c"));
        Assert.Equal(0, w.S("c").PendingRechecks);
    }

    [Fact]
    public void AC18_suspend_between_write_and_completion()
    {
        var w = Rig11();
        w.E.PresentDirectly();
        w.E.Suspend();
        w.Complete(100.05);
        Assert.Equal(R(725, 468, 705, 600), w.Known("c"));
        Assert.Equal(2, w.S("c").Expected.Length);
        Assert.Equal(1, w.S("c").PendingRechecks);
        w.At(100.30);
        Assert.Empty(w.Reads());
        Assert.Equal(0, w.S("c").PendingRechecks);
    }

    [Fact]
    public void AC19_release_all_with_write_in_flight()
    {
        var w = L3AllDiffer();
        Assert.True(w.E.WriteOne(K("b"), P(1439, 899, 705, 423)));
        int done = 0;
        int n = w.Writes().Count;
        w.E.ReleaseAll(null, () => done++);
        w.Win.Pump();
        w.Adv(0);
        Assert.Empty(w.E.Windows);
        Assert.Equal(1, done);
        Assert.Single(w.Writes().Skip(n));
    }

    [Fact]
    public void AC19_release_all_with_pending_recheck()
    {
        var w = Rig11();
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.E.ReleaseAll(null);
        w.Win.Pump();
        int reads = w.Reads().Count;
        w.At(100.5);
        w.Win.Pump();
        Assert.Equal(reads, w.Reads().Count);
        Assert.Empty(w.Lim.Entries);
    }

    [Fact]
    public void AC19_remove_with_pending_recheck_makes_no_read()
    {
        var w = Rig11();
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.E.WindowRemoved(K("c"), false);
        w.At(100.5);
        Assert.Empty(w.Reads());
    }

    [Fact]
    public void AC19_release_drops_writer_state()
    {
        var w = L3At();
        w.E.Release(K("b"));
        Assert.Null(w.E.WriterState(K("b")));
    }
}
