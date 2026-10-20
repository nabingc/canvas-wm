using CanvasWm.Core;
using System.Reflection;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;
using static CanvasWm.Engine.Tests.FrameWriterSpec.Wr;

namespace CanvasWm.Engine.Tests.FrameWriterSpec;

public sealed class WiringTests
{
    private static string Snap(Wr w) =>
        $"{w.Win.Writes.Count}|{w.Win.Reads.Count}|{w.Win.PendingCount}|{w.Ren.Plans.Count}|{w.Disp.SaveLog.Count}|{w.Clock.PendingCount}|{w.Changes}|{w.Logs.Count}";

    [Fact]
    public void AC1_Tuning_default_is_Tuning_Default()
    {
        var w = L3At();
        Assert.True(ReferenceEquals(w.E.Tuning, Tuning.Default));
    }

    [Fact]
    public void AC1_Tuning_set_reads_back_with_no_side_effect()
    {
        var w = L3At();
        string before = Snap(w);
        var t = Tuning.Default with { FrameRefusalSlack = 10 };
        w.E.Tuning = t;
        Assert.Same(t, w.E.Tuning);
        Assert.Equal(before, Snap(w));
    }

    [Fact]
    public void AC1_Tuning_null_throws_and_keeps_value()
    {
        var w = L3At();
        Assert.Throws<ArgumentNullException>(() => w.E.Tuning = null!);
        Assert.Same(Tuning.Default, w.E.Tuning);
    }

    [Fact]
    public void AC1_Tuning_set_from_OnChange_throws()
    {
        var w = L3At();
        Exception? caught = null;
        w.E.OnChange = () =>
        {
            try { w.E.Tuning = Tuning.Default with { FrameRefusalSlack = 1 }; }
            catch (Exception e) { caught = e; }
        };
        w.E.WindowFocused(K("a"));
        Assert.IsType<InvalidOperationException>(caught);
        Assert.Same(Tuning.Default, w.E.Tuning);
    }

    [Theory]
    [InlineData(433, 1)]
    [InlineData(432, 0)]
    public void AC1_slack_10_drives_recheck_threshold(double minHeight, int rechecks)
    {
        var w = L3C();
        w.E.Tuning = Tuning.Default with { FrameRefusalSlack = 10 };
        w.Win.SetConstraints(K("c"), min: new Size(0, minHeight));
        Assert.True(w.E.WriteOne(K("c"), T(725, 468, 705, 422)));
        w.Complete(100.05);
        Assert.Equal(rechecks, w.S("c").PendingRechecks);
        Assert.Equal(rechecks, w.Pending);
    }

    [Fact]
    public void AC2_L3_targets()
    {
        var w = L3At();
        var t = w.E.ComputeTargets();
        Assert.Equal(3, t.Count);
        Assert.Equal(T(10, 35, 705, 855), t[K("a")]);
        Assert.Equal(T(725, 35, 705, 423), t[K("b")]);
        Assert.Equal(T(725, 468, 705, 422), t[K("c")]);
    }

    [Fact]
    public void AC2_L2F_targets()
    {
        var w = new Wr(L2(), "a", new[] { ("a", A3), ("b", R(725, 35, 705, 855)) });
        var t = w.E.ComputeTargets();
        Assert.Equal(T(10, 35, 1420, 855), t[K("a")]);
        Assert.Equal(P(1439, 899, 705, 855), t[K("b")]);
    }

    [Fact]
    public void AC2_gap_20()
    {
        var w = new Wr(L2(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)) },
            EnginePolicy.Default with { Gap = 20 });
        Assert.Equal(T(20, 45, 690, 835), w.E.ComputeTargets()[K("a")]);
    }

    [Fact]
    public void AC2_empty_document_gives_empty_map()
    {
        var w = new Wr(LayoutDocument.Empty, null, new[] { ("a", A3) });
        Assert.Empty(w.E.ComputeTargets());
    }

    [Fact]
    public void AC2_unmanaged_layout_key_is_absent()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", B3) });
        var t = w.E.ComputeTargets();
        Assert.Equal(2, t.Count);
        Assert.DoesNotContain(K("c"), t.Keys);
    }

    [Fact]
    public void AC2_start_loads_document_and_framed_id_from_saved_record()
    {
        var saved = new SavedDisplay(L2(), "a");
        var w = new Wr(null, null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)) }, saved: saved);
        Assert.Equal(saved.Document, w.E.Document);
        Assert.Equal("a", w.E.FramedNodeId);
        var t = w.E.ComputeTargets();
        Assert.Equal(T(10, 35, 1420, 855), t[K("a")]);
        Assert.Equal(P(1439, 899, 705, 855), t[K("b")]);
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void AC2_document_empty_and_framed_null_before_start()
    {
        var w = new Wr(null, null, Array.Empty<(string, Rect)>());
        Assert.Equal(LayoutDocument.Empty, w.E.Document);
        Assert.Null(w.E.FramedNodeId);
    }

    [Fact]
    public void AC2_own_maximum_clamps_target()
    {
        var w = L3At();
        w.E.SetLimits(K("b"), null, new Size(300, double.PositiveInfinity), null);
        Assert.Equal(T(928, 35, 300, 423), w.E.ComputeTargets()[K("b")]);
    }

    [Fact]
    public void AC2_park_small_uses_ShownSize_not_park_small_known_size()
    {
        var w = new Wr(L2(), "a", new[] { ("a", A3), ("b", R(700, 35, 705, 855)) },
            EnginePolicy.Default with { ParkOutOfViewSmall = true });
        Assert.True(w.E.WriteOne(K("b"), T(725, 35, 705, 855)));
        w.Complete(100.05);
        Assert.Equal(new Size(705, 855), w.S("b").ShownSize);
        var expected = ParkGeometry.ParkSmallFrame(w.E.ParkContext(), new Size(705, 855));
        var target = w.E.ComputeTargets()[K("b")];
        Assert.Equal(expected, target.Rect);

        Assert.True(w.E.WriteOne(K("b"), target));
        w.Complete(100.06);
        Assert.True(w.Known("b").Width < 705 && w.Known("b").Height < 855);
        Assert.Equal(new Size(705, 855), w.S("b").ShownSize);
        Assert.Equal(expected, w.E.ComputeTargets()[K("b")].Rect);
    }

    // ---------- AC27 ----------

    [Fact]
    public void AC27_direct_at_rest()
    {
        var w = L3C();
        w.E.PresentLayout();
        Assert.Single(w.Writes());
        Assert.NotNull(w.Writes()[0].BatchId);
        Assert.Equal(0, w.G);
    }

    [Fact]
    public void AC27_motion_active_relands()
    {
        var w = L3C();
        w.E.MotionActive = true;
        w.E.PresentLayout();
        Assert.Equal(1, w.G);
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void AC27_interaction_active_relands()
    {
        var w = L3C();
        w.E.InteractionActive = true;
        w.E.PresentLayout();
        Assert.Equal(1, w.G);
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void AC27_overview_renders_only()
    {
        var w = L3C();
        w.E.SetPhase(StagePhase.Overview);
        w.E.PresentLayout();
        Assert.Equal(1, w.Renders);
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void AC27_suspended_does_nothing()
    {
        var w = L3C();
        w.E.Suspend();
        string s = Snap(w);
        int g = w.E.PlacementGeneration;
        w.E.PresentLayout();
        Assert.Equal(s, Snap(w));
        Assert.Equal(g, w.E.PlacementGeneration);
    }

    [Fact]
    public void AC27_focus_request_is_deferred_to_clock()
    {
        var w = L3C();
        w.E.PresentDirectly(K("a"));
        Assert.Empty(w.Win.FocusRequests);
        w.Adv(0);
        Assert.Equal(K("a"), w.Win.FocusRequests[^1]);
    }

    [Fact]
    public void AC27_no_focus_action_when_already_focused()
    {
        var w = L3C();
        w.E.WindowFocused(K("a"));
        int p = w.Clock.PendingCount;
        w.E.PresentDirectly(K("a"));
        Assert.Equal(p, w.Clock.PendingCount);
        w.Adv(0);
        Assert.Empty(w.Win.FocusRequests);
    }

    [Fact]
    public void AC27_no_focus_when_window_removed_before_action()
    {
        var w = L3C();
        w.E.PresentDirectly(K("a"));
        w.E.WindowRemoved(K("a"), false);
        w.Adv(0);
        Assert.Empty(w.Win.FocusRequests);
    }

    [Fact]
    public void AC27_CanPresentDirectly_matches_B1()
    {
        var w = L3C();
        Assert.True(w.E.CanPresentDirectly);
        w.E.InteractionActive = true;
        Assert.False(w.E.CanPresentDirectly);
        w.E.InteractionActive = false;
        w.E.MotionActive = true;
        Assert.False(w.E.CanPresentDirectly);
        w.E.MotionActive = false;
        foreach (var ph in new[] { StagePhase.Moving, StagePhase.Landing, StagePhase.Fading, StagePhase.Overview })
        {
            w.E.SetPhase(ph);
            Assert.False(w.E.CanPresentDirectly);
        }

        w.E.SetPhase(StagePhase.Desk);
        Assert.True(w.E.CanPresentDirectly);
        w.E.Suspend();
        Assert.False(w.E.CanPresentDirectly);

        var fresh = new Wr(null, null, Array.Empty<(string, Rect)>());
        var notStarted = new DisplayEngine(fresh.Key, VA, EnginePolicy.Default,
            new EngineServices(fresh.Win, fresh.Cap, fresh.Clock, fresh.Ren, fresh.Lim, fresh.Disp));
        Assert.False(notStarted.CanPresentDirectly);
    }

    // ---------- AC28 ----------

    [Fact]
    public void AC28_visible_area_change_presents_directly()
    {
        var w = new Wr(L2(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)) });
        int rc = w.E.ReframeCount;
        w.E.VisibleAreaChanged(new Rect(0, 25, 1440, 800));
        Assert.Equal(rc + 1, w.E.ReframeCount);
        var wr = w.Writes();
        Assert.Equal(2, wr.Count);
        Assert.Equal(new FrameWriteRecord(K("a"), R(10, 35, 705, 780), wr[0].BatchId), wr[0]);
        Assert.Equal(new FrameWriteRecord(K("b"), R(725, 35, 705, 780), wr[0].BatchId), wr[1]);
        Assert.NotNull(wr[0].BatchId);
        Assert.Equal(1, w.Renders);
        Assert.Equal(0, w.G);
    }

    [Fact]
    public void AC28_gap_change_presents()
    {
        var w = new Wr(L2(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)) });
        w.E.Policy = w.E.Policy with { Gap = 20 };
        var wr = w.Writes();
        Assert.Equal(2, wr.Count);
        Assert.Equal(R(20, 45, 690, 835), wr[0].Target);
        Assert.Equal(R(730, 45, 690, 835), wr[1].Target);
        Assert.Equal(wr[0].BatchId, wr[1].BatchId);
    }

    [Fact]
    public void AC28_non_layout_policy_change_writes_nothing()
    {
        var w = new Wr(L2(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)) });
        w.E.Policy = w.E.Policy with { TabTint = 0.5 };
        Assert.Empty(w.Writes());
    }

    [Fact]
    public void AC28_resume_presents_directly_for_changed_window()
    {
        var w = new Wr(L2(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)) });
        w.E.Suspend();
        w.E.Resume(new[] { w.Info("a", A3), w.Info("b", R(700, 35, 705, 855)) });
        var wr = w.Writes();
        Assert.Single(wr);
        Assert.Equal(K("b"), wr[0].Key);
        Assert.Equal(R(725, 35, 705, 855), wr[0].Target);
    }

    [Fact]
    public void AC28_resume_on_not_started_engine_renders_once_without_write()
    {
        var w = new Wr(null, null, Array.Empty<(string, Rect)>());
        var e = new DisplayEngine(w.Key, VA, EnginePolicy.Default,
            new EngineServices(w.Win, w.Cap, w.Clock, w.Ren, w.Lim, w.Disp));
        e.Suspend();
        int plans = w.Ren.Plans.Count;
        e.Resume(Array.Empty<WindowInfo>());
        Assert.Equal(plans + 1, w.Ren.Plans.Count);
        Assert.Empty(w.Win.Writes);
    }

    // ---------- AC29 ----------

    [Fact]
    public void AC29_landing_completion_delivers_OnChange_once_after_state_change()
    {
        var w = L3AllDiffer();
        w.E.PlaceWindows(w.E.PlacementGeneration);
        var phases = new List<StagePhase>();
        w.E.OnChange = () => phases.Add(w.E.Phase);
        w.Complete(100.08);
        Assert.Equal(new[] { StagePhase.Fading }, phases);
    }

    [Fact]
    public void AC29_Suspend_inside_OnChange_throws()
    {
        var w = L3AllDiffer();
        w.E.PlaceWindows(w.E.PlacementGeneration);
        Exception? caught = null;
        w.E.OnChange = () =>
        {
            try { w.E.Suspend(); } catch (Exception e) { caught = e; }
        };
        w.Complete(100.08);
        Assert.IsType<InvalidOperationException>(caught);
    }

    [Fact]
    public void AC29_WriteOne_inside_Log_throws()
    {
        var w = L3C();
        Exception? caught = null;
        w.E.Log = _ =>
        {
            try { w.E.WriteOne(K("a"), T(0, 0, 10, 10)); } catch (Exception e) { caught = e; }
        };
        w.Win.SetConstraints(K("c"), min: new Size(0, 600));
        w.E.PresentDirectly();
        w.Complete(100.05);
        w.At(100.30);
        w.Win.Step();
        Assert.IsType<InvalidOperationException>(caught);
    }

    [Fact]
    public void AC29_PresentDirectly_without_state_change_fires_OnChange_once()
    {
        var w = new Wr(L3(), null, new[] { ("a", A3), ("b", B3), ("c", C3) });
        w.E.PresentDirectly();
        Assert.Equal(1, w.Changes);
    }

    // ---------- AC31 ----------

    private static string Root()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CanvasWm.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    [Fact]
    public void AC31_visibility_and_constants()
    {
        var asm = typeof(DisplayEngine).Assembly;
        Assert.False(asm.GetType("CanvasWm.Engine.FrameWriting")!.IsPublic);
        Assert.False(asm.GetType("CanvasWm.Engine.FrameWriterState")!.IsPublic);
        Assert.False(asm.GetType("CanvasWm.Engine.ExpectedFrame")!.IsPublic);
        var tuning = typeof(DisplayEngine).GetProperty("Tuning", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(tuning);
        Assert.Equal(
            new[] { 1.5, 2.0, 2.0, 2.0, 0.8, 1.0, 4.0, 0.02, 0.034 },
            new[]
            {
                FrameWriting.EchoWindowSeconds, FrameWriting.EchoTolerance, FrameWriting.AcceptedParkSlack,
                FrameWriting.OwnMinimumSlack, FrameWriting.RefusalScreenFactor, FrameWriting.RetargetTolerance,
                FrameWriting.DriftTileLogThreshold, FrameWriting.SettleExtraSeconds, FrameWriting.ReLandDelaySeconds,
            });
    }

    [Fact]
    public void AC31_engine_references_only_core()
    {
        var xml = File.ReadAllText(Path.Combine(Root(), "src", "CanvasWm.Engine", "CanvasWm.Engine.csproj"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xml, "<ProjectReference"));
        Assert.Contains("CanvasWm.Core", xml);
        Assert.DoesNotContain("PackageReference", xml);
    }

    [Fact]
    public void AC31_writer_sources_have_no_tuning_literals()
    {
        foreach (var f in new[] { "DisplayEngine.Writer.cs", "FrameWriting.cs", "FrameWriterState.cs" })
        {
            string text = File.ReadAllText(Path.Combine(Root(), "src", "CanvasWm.Engine", f));
            string code = System.Text.RegularExpressions.Regex.Replace(text, @"//[^\n]*", "");
            Assert.DoesNotMatch(@"(?<![\w.])24(?![\w.])", code);
            Assert.DoesNotContain("0.25", code);
            Assert.DoesNotContain("0.05", code);
        }
    }

    [Fact]
    public void AC31_no_forbidden_words_in_src_or_tests()
    {
        string a = "Trellis" + "WM", b = "Licens" + "ing";
        foreach (var dir in new[] { "src", "tests" })
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(Root(), dir), "*", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                    || f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")) continue;
                string text;
                try { text = File.ReadAllText(f); } catch { continue; }
                Assert.False(text.Contains(a, StringComparison.OrdinalIgnoreCase), f);
                Assert.False(text.Contains(b, StringComparison.OrdinalIgnoreCase), f);
            }
        }
    }

    [Fact]
    public void AC31_no_member_or_type_name_contains_Probe()
    {
        string needle = "Pro" + "be";
        foreach (var t in typeof(DisplayEngine).Assembly.GetTypes())
        {
            Assert.DoesNotContain(needle, t.Name, StringComparison.OrdinalIgnoreCase);
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                           | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(needle, m.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
