using CanvasWm.Core.Geometry;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;
using static CanvasWm.Engine.Tests.FrameWriterSpec.Wr;

namespace CanvasWm.Engine.Tests.FrameWriterSpec;

public sealed class WriteTests
{
    private static Wr Ac3()
    {
        var w = L3C();
        // swap: a, c at target, b known (725,35,705,855)
        return w;
    }

    private static Wr Ac3Rig() =>
        new(L3(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)), ("c", C3) });

    [Fact]
    public void AC3_direct_pass_bookkeeping_and_completion()
    {
        var w = Ac3Rig();
        w.E.PresentDirectly();
        var wr = w.Writes();
        Assert.Single(wr);
        Assert.Equal(K("b"), wr[0].Key);
        Assert.Equal(R(725, 35, 705, 423), wr[0].Target);
        Assert.NotNull(wr[0].BatchId);
        var s = w.S("b");
        Assert.Equal(R(725, 35, 705, 423), s.Requested);
        Assert.Equal(1, s.WritesInFlight);
        Assert.Single(s.Expected);
        Assert.Equal(R(725, 35, 705, 423), s.Expected[0].Frame);
        Near(100, s.Expected[0].Time);
        Assert.Equal(new Size(705, 423), s.ShownSize);
        Assert.False(w.E.Window(K("b"))!.Parked);
        Assert.Equal(1, w.Saves);
        Assert.Equal(1, w.Renders);
        Assert.Equal(1, w.Changes);
        Assert.Equal(0, w.G);
        Assert.Equal(0, w.Pending);

        w.Complete(100.05);
        s = w.S("b");
        Assert.Equal(0, s.WritesInFlight);
        Assert.Equal(2, s.Expected.Length);
        Assert.Equal(R(725, 35, 705, 423), s.Expected[1].Frame);
        Near(100.05, s.Expected[1].Time);
        Assert.Equal(R(725, 35, 705, 423), w.Known("b"));
        Assert.Equal(0, s.PendingRechecks);
        Assert.Equal(0, w.Pending);
        Assert.Empty(w.Lim.Entries);
        Assert.Equal(0, w.Lim.PersistCount);
        Assert.Equal(StagePhase.Desk, w.E.Phase);
    }

    [Fact]
    public void AC4_inflight_skip_and_distinct_target_write()
    {
        var w = Ac3Rig();
        w.E.PresentDirectly();
        w.At(100.01);
        w.E.PresentDirectly();
        Assert.Single(w.Writes());

        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 600)));
        var wr = w.Writes();
        Assert.Equal(2, wr.Count);
        Assert.Null(wr[1].BatchId);
        Assert.Equal(2, w.S("b").WritesInFlight);
        Assert.Equal(R(725, 35, 705, 600), w.S("b").Requested);
        Assert.False(w.E.WriteOne(K("b"), T(725, 35, 705, 600)));
        Assert.Equal(2, w.Writes().Count);

        w.Complete(100.05);
        Assert.Equal(0, w.S("b").PendingRechecks);
        w.Complete(100.06);
        Assert.Equal(0, w.S("b").PendingRechecks);
        Assert.Equal(0, w.Pending);
        Assert.Equal(0, w.S("b").WritesInFlight);
        Assert.Equal(R(725, 35, 705, 600), w.Known("b"));
    }

    [Fact]
    public void AC5a_all_at_target_still_saves_renders_and_fires_OnChange()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", B3), ("c", C3) });
        w.E.PresentDirectly();
        Assert.Empty(w.Writes());
        Assert.Equal(1, w.Saves);
        Assert.Equal(1, w.Renders);
        Assert.Equal(1, w.Changes);
    }

    private static Wr PRig(Rect known, bool parked)
    {
        var w = new Wr(null, null, new[] { ("p", known) });
        if (parked) w.E.SetParked(K("p"), true);
        return w;
    }

    [Fact]
    public void AC5b_parked_window_near_target_is_skipped()
    {
        var w = PRig(R(100, 80, 200, 200), true);
        Assert.False(w.E.WriteOne(K("p"), P(100, 100, 200, 200)));
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void AC5b_parked_window_far_from_target_is_written()
    {
        var w = PRig(R(100, 30, 200, 200), true);
        Assert.True(w.E.WriteOne(K("p"), P(100, 100, 200, 200)));
    }

    [Fact]
    public void AC5b_not_parked_window_is_written()
    {
        var w = PRig(R(100, 80, 200, 200), false);
        Assert.True(w.E.WriteOne(K("p"), P(100, 100, 200, 200)));
    }

    [Theory]
    [InlineData(TargetKind.Park)]
    [InlineData(TargetKind.ParkWithPicture)]
    public void AC5b_offscreen_parked_window_is_skipped(TargetKind kind)
    {
        var w = PRig(R(1439, 860, 705, 855), true);
        Assert.False(w.E.WriteOne(K("p"), new WindowTarget(kind, R(1439, 899, 705, 855))));
    }

    [Fact]
    public void AC5c_except_skips()
    {
        var w = Ac3Rig();
        int n = w.E.WriteTargets(new Dictionary<WindowKey, WindowTarget> { [K("b")] = T(725, 35, 705, 423) },
            new HashSet<WindowKey> { K("b") });
        Assert.Equal(0, n);
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void AC5d_unmanaged_targets_ignored()
    {
        var w = Ac3Rig();
        int n = w.E.WriteTargets(new Dictionary<WindowKey, WindowTarget> { [K("zz")] = T(0, 0, 10, 10) });
        Assert.Equal(0, n);
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void AC6_parked_flag_and_shown_size()
    {
        var w = Ac3Rig();
        Assert.True(w.E.WriteOne(K("b"), P(1439, 899, 705, 423)));
        Assert.True(w.E.Window(K("b"))!.Parked);
        Assert.Null(w.S("b").ShownSize);
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 423)));
        Assert.False(w.E.Window(K("b"))!.Parked);
        Assert.Equal(new Size(705, 423), w.S("b").ShownSize);
    }

    [Fact]
    public void AC6_expected_list_prunes_on_write()
    {
        var w = Ac3Rig();
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 400)));
        w.At(100.6);
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 410)));
        w.At(102);
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 420)));
        var e = w.S("b").Expected;
        Assert.Equal(2, e.Length);
        Assert.Equal(R(725, 35, 705, 410), e[0].Frame);
        Near(100.6, e[0].Time);
        Assert.Equal(R(725, 35, 705, 420), e[1].Frame);
        Near(102, e[1].Time);
    }

    [Fact]
    public void AC6_batch_in_registration_order_and_results_processed_in_order()
    {
        var w = new Wr(L3(), null, new[] { ("c", R(725, 35, 705, 855)), ("a", R(10, 35, 600, 855)), ("b", R(725, 35, 705, 855)) });
        w.Win.SetConstraints(K("b"), min: new Size(0, 600));
        w.Win.SetConstraints(K("c"), min: new Size(0, 600));
        w.E.PresentDirectly();
        var wr = w.Writes();
        Assert.Equal(new[] { K("c"), K("a"), K("b") }, wr.Select(x => x.Key).ToArray());
        Assert.Single(wr.Select(x => x.BatchId).Distinct());
        w.Win.Step();
        // c and b both over-large (refusal): rechecks are scheduled in processing order.
        w.Adv(0.25);
        Assert.Equal(new[] { K("c"), K("b") }, w.Reads().ToArray());
    }

    [Fact]
    public void AC7_echo_matching_after_completion()
    {
        var w = Ac3Rig();
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.At(100.9);
        int ops = w.Win.PendingCount + w.Win.Writes.Count + w.Win.Reads.Count;
        Assert.True(w.E.IsExpected(K("b"), R(726, 35, 706, 423)));
        Assert.True(w.E.IsExpected(K("b"), R(726, 35, 706, 423)));
        Assert.True(w.E.IsExpected(K("b"), R(727, 35, 705, 423)));
        Assert.False(w.E.IsExpected(K("b"), R(727.5, 35, 705, 423)));
        Assert.Equal(ops, w.Win.PendingCount + w.Win.Writes.Count + w.Win.Reads.Count);
        w.At(101.56);
        Assert.False(w.E.IsExpected(K("b"), R(725, 35, 705, 423)));
        Assert.Empty(w.S("b").Expected);
    }

    [Fact]
    public void AC7_boundary_1_5_seconds()
    {
        var w = Ac3Rig();
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 423)));
        w.Clock.Advance(1.5);
        Assert.True(w.E.IsExpected(K("b"), T(725, 35, 705, 423).Rect));
        w.At(101.75);
        Assert.Single(w.S("b").Expected);
        Assert.False(w.E.IsExpected(K("b"), T(725, 35, 705, 423).Rect));
        Assert.Empty(w.S("b").Expected);
    }

    [Fact]
    public void AC7_unknown_key_false()
    {
        var w = Ac3Rig();
        Assert.False(w.E.IsExpected(K("zz"), R(0, 0, 1, 1)));
    }

    [Fact]
    public void AC8_failed_write_changes_nothing_but_counter()
    {
        var w = Ac3Rig();
        w.Win.SetFails(K("b"), true);
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 423)));
        w.Complete(100.05);
        var s = w.S("b");
        Assert.Equal(0, s.WritesInFlight);
        Assert.Equal(R(725, 35, 705, 855), w.Known("b"));
        Assert.Single(s.Expected);
        Assert.Equal(0, s.PendingRechecks);
        Assert.Equal(0, w.Pending);
        Assert.Empty(w.Lim.Entries);
        Assert.Equal(0, w.Lim.PersistCount);

        w.Win.SetFails(K("b"), false);
        w.E.PresentDirectly();
        Assert.Contains(w.Writes(), x => x.Key == K("b") && x.Target == R(725, 35, 705, 423));
    }

    [Fact]
    public void AC8_remove_and_readd_before_completion()
    {
        var w = Ac3Rig();
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 423)));
        w.E.WindowRemoved(K("b"), false);
        var f = R(300, 300, 400, 400);
        w.E.WindowAdded(w.Info("b", f));
        w.Complete(100.05);
        Assert.Equal(f, w.Known("b"));
        var s = w.S("b");
        Assert.Equal(0, s.WritesInFlight);
        Assert.Empty(s.Expected);
        Assert.Empty(w.Lim.Entries);
    }

    [Fact]
    public void AC8_refresh_keeps_writer_state()
    {
        var w = Ac3Rig();
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 423)));
        w.E.SetLimits(K("b"), new Size(1, 2), new Size(3, 4), new Size(5, 6));
        w.E.WindowAdded(w.Info("b", R(1, 2, 3, 4)));
        var s = w.S("b");
        Assert.Equal(R(725, 35, 705, 423), s.Requested);
        Assert.Single(s.Expected);
        Assert.Equal(new Size(1, 2), s.Minimum);
        Assert.Equal(new Size(3, 4), s.Maximum);
        Assert.Equal(new Size(5, 6), s.Natural);
        Assert.Equal(1, s.WritesInFlight);
    }

    [Fact]
    [Trait("Assumption", "A2")]
    public void AC9_own_minimum_lowered_and_store_observed()
    {
        var w = Ac3Rig();
        w.E.SetLimits(K("a"), new Size(500, 400), null, null);
        w.Lim.Learn(w.App, new Size(900, 900));
        Assert.True(w.E.WriteOne(K("a"), T(10, 35, 450, 855)));
        w.Complete(100.05);
        Assert.Equal(new Size(450, 400), w.S("a").Minimum);
        Assert.Equal(new Size(450, 855), w.Lim.Minimum(w.App));
        Assert.Equal(0, w.S("a").PendingRechecks);
        Assert.Equal(0, w.Pending);
    }

    [Fact]
    public void AC9_within_two_unchanged()
    {
        var w = Ac3Rig();
        w.E.SetLimits(K("a"), new Size(460, 400), null, null);
        Assert.True(w.E.WriteOne(K("a"), T(10, 35, 459, 855)));
        w.Complete(100.05);
        Assert.Equal(new Size(460, 400), w.S("a").Minimum);
    }

    [Fact]
    public void AC9_null_app_is_harmless()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)), ("c", C3) }, noApp: new[] { "b" });
        w.Lim.Learn(w.App, new Size(900, 900));
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 423)));
        w.Complete(100.05);
        Assert.Equal(new Size(900, 900), w.Lim.Minimum(w.App));
        Assert.Equal(1, w.Lim.PersistCount);
    }

    [Theory]
    [InlineData(446, 0)]
    [InlineData(447, 1)]
    public void AC10_larger_threshold(double minHeight, int rechecks)
    {
        var w = L3C();
        w.Win.SetConstraints(K("c"), min: new Size(0, minHeight));
        Assert.True(w.E.WriteOne(K("c"), T(725, 468, 705, 422)));
        w.Complete(100.05);
        Assert.Equal(rechecks, w.S("c").PendingRechecks);
        Assert.Equal(rechecks, w.Pending);
        if (rechecks == 1)
        {
            w.At(100.30 - 0.001);
            Assert.Empty(w.Reads());
            w.At(100.30);
            Assert.Single(w.Reads());
        }
    }

    [Theory]
    [InlineData(681, 0)]
    [InlineData(680, 1)]
    public void AC10_smaller_threshold(double maxWidth, int rechecks)
    {
        var w = L3C();
        w.Win.SetConstraints(K("c"), max: new Size(maxWidth, 10000));
        Assert.True(w.E.WriteOne(K("c"), T(725, 468, 705, 422)));
        w.Complete(100.05);
        Assert.Equal(rechecks, w.S("c").PendingRechecks);
    }

    [Fact]
    public void AC10_position_only_difference_no_recheck()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", B3), ("c", R(0, 0, 705, 422)) });
        w.Win.SetRefusesWrites(K("c"), true);
        Assert.True(w.E.WriteOne(K("c"), T(725, 468, 705, 422)));
        w.Complete(100.05);
        Assert.Equal(0, w.S("c").PendingRechecks);
        Assert.Equal(0, w.Pending);
    }

    [Fact]
    public void AC10_mixed_axes_is_larger_and_learns_735_0()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", B3), ("c", R(725, 468, 735, 392)) });
        w.Win.SetRefusesWrites(K("c"), true);
        Assert.True(w.E.WriteOne(K("c"), T(725, 468, 705, 422)));
        w.Complete(100.05);
        Assert.Equal(1, w.S("c").PendingRechecks);
        w.At(100.30);
        w.Win.Step();
        Assert.Equal(new Size(735, 0), w.S("c").Minimum);
    }

    [Fact]
    public void AC20_learning_off()
    {
        var w = L3C();
        w.Lim.Learn(w.App, new Size(900, 900));
        w.Win.SetConstraints(K("c"), min: new Size(0, 600));
        w.E.WriteTargets(new Dictionary<WindowKey, WindowTarget> { [K("c")] = T(725, 468, 705, 422) }, learns: false);
        w.Complete(100.05);
        Assert.Equal(R(725, 468, 705, 600), w.Known("c"));
        Assert.Equal(2, w.S("c").Expected.Length);
        Assert.Equal(new Size(900, 900), w.Lim.Minimum(w.App));
        Assert.Equal(0, w.S("c").PendingRechecks);
        Assert.Null(w.S("c").Minimum);
    }
}
