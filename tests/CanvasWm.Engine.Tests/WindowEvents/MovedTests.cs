using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;
using static CanvasWm.Engine.Tests.WindowEventsSpec.Ev;

namespace CanvasWm.Engine.Tests.WindowEventsSpec;

public sealed class MovedTests
{
    private static readonly Rect BTile = R(725, 468, 705, 422);

    private static void NoEffects(Ev ev)
    {
        Assert.Equal(0, ev.dWrites);
        Assert.Equal(0, ev.dPlans);
        Assert.Equal(0, ev.dSaves);
        Assert.Equal(0, ev.dGen);
    }

    [Fact]
    public void AC19_WriteInFlightIsIgnored()
    {
        var ev = Ev.Ac5(pump: false);
        ev.Mark();
        ev.E.WindowMoved(K("c"), R(1, 2, 3, 4));
        NoEffects(ev);
        Assert.Empty(ev.E.SnapBackTimes(K("c")));
        Assert.Equal(R(1, 2, 3, 4), ev.Known("c"));
    }

    [Fact]
    public void AC19_ExpectedEchoIsIgnored()
    {
        var ev = Ev.Ac5();
        ev.AtTime(100.3);
        ev.Mark();
        ev.E.WindowMoved(K("c"), R(1083, 468, 348, 422));
        NoEffects(ev);
        Assert.Empty(ev.E.SnapBackTimes(K("c")));
    }

    [Fact]
    public void AC19_MouseDownIsIgnored()
    {
        var ev = Ev.Ac5();
        ev.AtTime(100.3);
        ev.Win.MouseDown = true;
        ev.Mark();
        ev.E.WindowMoved(K("c"), R(0, 0, 500, 500));
        NoEffects(ev);
        Assert.Empty(ev.E.SnapBackTimes(K("c")));
    }

    [Fact]
    public void AC19_NotStartedOnlyUpdatesKnownFrame()
    {
        var ev = new Ev();
        ev.E.WindowAdded(ev.W("a"));
        ev.Mark();
        ev.E.WindowMoved(K("a"), R(5, 6, 7, 8));
        Assert.Equal(R(5, 6, 7, 8), ev.Known("a"));
        NoEffects(ev);
    }

    [Fact]
    public void AC19_WindowWithoutPanelIsNotManaged()
    {
        var ev = S1();
        ev.Mark();
        ev.Move("n", R(0, 0, 900, 900));
        Assert.Equal(0, ev.dWrites);
        Assert.Empty(ev.E.SnapBackTimes(K("n")));
    }

    private static Ev AtT(double t)
    {
        var ev = S1();
        ev.AtTime(t);
        ev.Mark();
        return ev;
    }

    [Fact]
    public void AC20_SnapBackAndMaximum()
    {
        var ev = AtT(110.0);
        var snap = R(300, 200, 705, 422);
        ev.Move("b", snap);
        Assert.Equal(new[] { 110.0 }, ev.E.SnapBackTimes(K("b")).ToArray());
        Assert.Equal(new List<(string, Rect)> { ("b", BTile) }, ev.OneBatch());
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(0, ev.dSaves);
        ev.Win.Pump();

        ev.AtTime(111.0);
        ev.Move("b", snap);
        Assert.Equal(new[] { 110.0, 111.0 }, ev.E.SnapBackTimes(K("b")).ToArray());
        Assert.Equal(2, ev.Batches().Count);
        ev.Win.Pump();

        ev.AtTime(112.0);
        ev.Mark();
        ev.Move("b", snap);
        Assert.Empty(ev.E.SnapBackTimes(K("b")));
        Assert.Equal(new Size(705, 422), ev.E.WriterState(K("b"))!.Maximum);
        Assert.Equal(0, ev.Lim.PersistCount);
        Assert.Equal(1, ev.dLogs);
        Assert.Contains("b", ev.NewLogs[0]);
        Assert.Equal(new List<(string, Rect)> { ("b", BTile) }, ev.OneBatch());
        Assert.Equal(0, ev.dSaves);
        ev.Win.Pump();

        ev.AtTime(112.9);
        ev.Mark();
        ev.Move("b", snap);
        Assert.Equal(new[] { 112.9 }, ev.E.SnapBackTimes(K("b")).ToArray());
        Assert.Equal(new List<(string, Rect)> { ("b", BTile) }, ev.OneBatch());
        Assert.Equal(0, ev.dGen);
    }

    [Fact]
    public void AC20_WindowBoundaryDropsOldest()
    {
        var ev = AtT(110.0);
        var snap = R(300, 200, 705, 422);
        foreach (var t in new[] { 110.0, 111.0, 113.0 })
        {
            ev.AtTime(t);
            ev.Move("b", snap);
            ev.Win.Pump();
        }

        Assert.Equal(new[] { 111.0, 113.0 }, ev.E.SnapBackTimes(K("b")).ToArray());
        Assert.Null(ev.E.WriterState(K("b"))!.Maximum);
    }

    [Fact]
    public void AC20_SnapBackLimitIsReadFromTuning()
    {
        var ev = AtT(110.0);
        ev.E.Tuning = ev.E.Tuning with { SnapBackLimit = 2 };
        var snap = R(300, 200, 705, 422);
        ev.AtTime(110.0); ev.Move("b", snap); ev.Win.Pump();
        Assert.Null(ev.E.WriterState(K("b"))!.Maximum);
        ev.AtTime(110.5); ev.Move("b", snap); ev.Win.Pump();
        Assert.Equal(new Size(705, 422), ev.E.WriterState(K("b"))!.Maximum);
    }

    [Fact]
    public void AC20_SnapBackWindowIsReadFromTuning()
    {
        var ev = AtT(110.0);
        ev.E.Tuning = ev.E.Tuning with { SnapBackWindowSeconds = 1.0 };
        var snap = R(300, 200, 705, 422);
        ev.AtTime(110.0); ev.Move("b", snap); ev.Win.Pump();
        ev.AtTime(111.0); ev.Move("b", snap); ev.Win.Pump();
        Assert.Equal(new[] { 111.0 }, ev.E.SnapBackTimes(K("b")).ToArray());
    }

    [Fact]
    public void AC21_InsistingLarger()
    {
        var ev = AtT(110.0);
        var big = R(725, 468, 705, 470);
        ev.Move("b", big);
        Assert.Equal(new List<(string, Rect)> { ("b", BTile) }, ev.OneBatch());
        ev.Win.Pump();
        ev.AtTime(110.5);
        ev.Mark();
        ev.Move("b", big);
        Assert.Equal(new List<(string, Rect)> { ("b", BTile) }, ev.OneBatch());
        ev.Win.Pump();
        ev.AtTime(111.0);
        ev.Mark();
        ev.Move("b", big);
        Assert.Equal(new Size(0, 470), ev.E.WriterState(K("b"))!.Minimum);
        Assert.Equal(LimitRules.Learn(null, new Size(0, 470)), ev.Lim.Entries[new AppId("com.x")]);
        var batch = ev.OneBatch();
        var tgt = ev.Tgt("b");
        Assert.Equal(TargetKind.ParkWithPicture, tgt.Kind);
        Assert.Equal(new List<(string, Rect)> { ("b", tgt.Rect) }, batch);
        Assert.True(ev.E.Window(K("b"))!.Parked);
        Assert.Equal(0, ev.dSaves);
    }

    private static Ev Ac22()
    {
        var ev = new Ev();
        ev.E.Start(new[] { ev.W("b"), ev.W("c") });
        return ev;
    }

    [Fact]
    public void AC22_InsistingWithinLimitsLearns()
    {
        var ev = Ac22();
        ev.E.SetLayout(Doc(Y("s", At(P("b"), 610.0 / 865), At(P("c"), 255.0 / 865))), null);
        ev.E.PresentDirectly();
        ev.Land();
        Assert.Equal(R(10, 35, 1420, 600), ev.Tgt("b").Rect);
        foreach (var t in new[] { 110.0, 110.5, 111.0 })
        {
            ev.AtTime(t);
            ev.Move("b", R(10, 35, 1420, 655));
            ev.Win.Pump();
        }

        Assert.Equal(new Size(0, 655), ev.E.WriterState(K("b"))!.Minimum);
        Assert.Equal(new Size(0, 655), ev.Lim.Entries[new AppId("com.x")]);
    }

    [Fact]
    public void AC22_InsistingTooLargeLearnsNothing()
    {
        var ev = Ac22();
        ev.E.SetLayout(Doc(Y("s", At(P("b"), 660.0 / 865), At(P("c"), 205.0 / 865))), null);
        ev.E.PresentDirectly();
        ev.Land();
        Assert.Equal(R(10, 35, 1420, 650), ev.Tgt("b").Rect);
        foreach (var t in new[] { 110.0, 110.5 })
        {
            ev.AtTime(t);
            ev.Move("b", R(10, 35, 1420, 705));
            ev.Win.Pump();
        }

        ev.AtTime(111.0);
        ev.Mark();
        ev.Move("b", R(10, 35, 1420, 705));
        Assert.Null(ev.E.WriterState(K("b"))!.Minimum);
        Assert.Equal(0, ev.Lim.PersistCount);
        Assert.Equal(1, ev.dLogs);
        Assert.Equal(new List<(string, Rect)> { ("b", R(10, 35, 1420, 650)) }, ev.OneBatch());
    }

    private static Ev ZoomedB()
    {
        var ev = AtT(110.0);
        ev.Move("b", R(100, 100, 1340, 800));
        return ev;
    }

    [Fact]
    public void AC23_ZoomRequest()
    {
        var ev = AtT(110.0);
        ev.Move("b", R(100, 100, 1340, 800));
        Assert.Equal("b", ev.E.FramedNodeId);
        Assert.True(ev.E.ZoomedAlone);
        Assert.Null(ev.E.ZoomReturnNodeId);
        Assert.Equal(new string?[] { null }, ev.E.FramingHistory.ToArray());
        Near(110.0, ev.E.LastZoomToggle);
        Assert.Empty(ev.E.SnapBackTimes(K("b")));
        Assert.Empty(ev.Batches());
        Assert.Equal(1, ev.dGen);
        Assert.Equal(0, ev.dSaves);
        Assert.Equal(1, ev.dLogs);

        ev.Clock.Advance(0.034);
        var batch = ev.OneBatch();
        var at = ev.Tgt("a");
        Assert.Equal(TargetKind.Park, at.Kind);
        Assert.Equal(R(1439, 899, 705, 423), at.Rect);
        Assert.Equal(new List<(string, Rect)> { ("a", at.Rect), ("b", R(10, 35, 1420, 855)) }, batch);
        ev.Win.Pump();
        Assert.Empty(ev.Win.FocusRequests);
    }

    [Fact]
    public void AC23_SpacingBlocksZoom()
    {
        var ev = AtT(109.0);
        ev.Move("b", R(100, 100, 1340, 800));
        Near(109.0, ev.E.LastZoomToggle);
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        ev.Clock.Advance(0.2);
        ev.Win.Pump();
        ev.E.FrameNode(null, false);
        ev.E.ReLand();
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        ev.Clock.Advance(0.2);
        ev.Win.Pump();
        Assert.Null(ev.E.FramedNodeId);
        ev.AtTime(110.0);
        ev.Move("b", R(100, 100, 1340, 800));
        Assert.Null(ev.E.FramedNodeId);
        Assert.Equal(new[] { 110.0 }, ev.E.SnapBackTimes(K("b")).ToArray());
        Near(109.0, ev.E.LastZoomToggle);
    }

    [Fact]
    public void AC23_HeightOnlyGrowZooms()
    {
        var ev = AtT(110.0);
        ev.Move("b", R(725, 468, 705, 483));
        Assert.Equal("b", ev.E.FramedNodeId);
    }

    [Fact]
    public void AC23_SlightlyNarrowerHeightGrowSnapsBack()
    {
        var ev = AtT(110.0);
        ev.Move("b", R(725, 468, 702, 483));
        Assert.Null(ev.E.FramedNodeId);
        Assert.Single(ev.E.SnapBackTimes(K("b")));
    }

    private static Ev ZoomedLanded()
    {
        var ev = ZoomedB();
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        ev.Clock.Advance(0.2);
        ev.Win.Pump();
        ev.Mark();
        return ev;
    }

    [Fact]
    [Trait("Assumption", "A15")]
    public void AC24_UnzoomAfterSnapBackSpacing()
    {
        var ev = ZoomedLanded();
        ev.AtTime(110.5);
        ev.Move("b", R(300, 300, 600, 400));
        Assert.Equal(new[] { 110.5 }, ev.E.SnapBackTimes(K("b")).ToArray());
        Assert.Equal(new List<(string, Rect)> { ("b", R(10, 35, 1420, 855)) }, ev.OneBatch());
        Assert.Equal("b", ev.E.FramedNodeId);
        ev.Win.Pump();

        ev.AtTime(111.5);
        ev.Mark();
        ev.Move("b", R(300, 300, 600, 400));
        Assert.Null(ev.E.FramedNodeId);
        Assert.False(ev.E.ZoomedAlone);
        Near(111.5, ev.E.LastZoomToggle);
        Assert.Equal(new string?[] { null, "b" }, ev.E.FramingHistory.ToArray());
        ev.Clock.Advance(0.034);
        Assert.Equal(new List<(string, Rect)> { ("a", R(725, 35, 705, 423)), ("b", R(725, 468, 705, 422)) }, ev.OneBatch());
    }

    [Fact]
    public void AC24_FillsUnzoom()
    {
        var ev = ZoomedLanded();
        ev.AtTime(111.2);
        ev.Move("b", R(0, 25, 1440, 875));
        Assert.Null(ev.E.FramedNodeId);
    }

    [Fact]
    public void AC24_DriftExactlyFourSnapsBack()
    {
        var ev = ZoomedLanded();
        ev.AtTime(111.2);
        ev.Move("b", R(10, 35, 1424, 855));
        Assert.Equal("b", ev.E.FramedNodeId);
        Assert.Single(ev.E.SnapBackTimes(K("b")));
    }

    [Fact]
    [Trait("Assumption", "A15")]
    public void AC24_UnzoomWithoutMemoryFramesNothing()
    {
        var ev = S2();
        ev.E.Move(Edge.Bottom);
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        ev.Clock.Advance(0.2);
        ev.Win.Pump();
        Assert.Equal("b", ev.E.FramedNodeId);
        Assert.False(ev.E.ZoomedAlone);
        ev.AtTime(102.0);
        ev.Move("b", R(300, 300, 600, 400));
        Assert.Null(ev.E.FramedNodeId);
    }

    [Fact]
    public void AC25_ParkedWindowMovedIsReparked()
    {
        var ev = ZoomedLanded();
        Assert.True(ev.E.Window(K("a"))!.Parked);
        ev.Move("a", R(400, 300, 705, 855));
        var tgt = ev.Tgt("a");
        Assert.Equal(TargetKind.Park, tgt.Kind);
        Assert.Equal(new List<(string, Rect)> { ("a", tgt.Rect) }, ev.OneBatch());
        Assert.True(ev.E.Window(K("a"))!.Parked);
        Assert.Empty(ev.E.SnapBackTimes(K("a")));
        Assert.Equal(1, ev.dPlans);
        Assert.Equal(0, ev.dSaves);
    }

    [Fact]
    [Trait("Assumption", "A17")]
    public void A17_SnapBackIsOneBatchWithLearning()
    {
        var ev = AtT(110.0);
        ev.Move("b", R(300, 200, 705, 422));
        Assert.Single(ev.Batches());
        Assert.Single(ev.Win.Writes.Skip(ev.Win.Writes.Count - 1));
        Assert.NotNull(ev.Win.Writes[^1].BatchId);
    }
}
