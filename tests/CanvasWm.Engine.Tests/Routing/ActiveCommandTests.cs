using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tests.RoutingSpec;

public sealed class ActiveCommandTests
{
    // ---- AC12 mirror ----

    [Fact]
    public void AC12_MirrorFeedsReshow()
    {
        var t = new Rt();
        t.Ready();
        t.Router.WindowMoved(Rt.K("w2"), Rt.R(100, 100, 800, 500));
        var a2 = Rt.A with { Frame = new Rect(1, 0, 1920, 1080) };
        t.Router.DisplaysChanged(new[] { a2, Rt.B });
        t.Clock.Advance(1.5);
        Assert.Empty(t.Router.Engines);
        t.Clock.Advance(0.8);
        var a = t.Router.EngineFor(Rt.KA)!;
        Assert.True(a.IsStarted);
        Assert.Contains(Rt.K("w2"), a.Windows.Select(w => w.Info.Key));
        Assert.Contains(Rt.K("w1"), a.Windows.Select(w => w.Info.Key));
    }

    [Fact]
    public void AC12_WindowAddedDuringGapAppearsInReshownEngine()
    {
        var t = new Rt();
        t.Ready();
        var a2 = Rt.A with { Frame = new Rect(1, 0, 1920, 1080) };
        t.Router.DisplaysChanged(new[] { a2, Rt.B });
        t.Clock.Advance(1.5);
        var w4 = Rt.W("w4", 400, Rt.R(200, 200, 300, 300));
        t.Win.AddWindow(w4.Key, w4.Frame);
        t.Router.WindowAdded(w4);
        Assert.Empty(t.Router.Engines);
        t.Clock.Advance(0.8);
        Assert.Contains(w4.Key, t.Router.EngineFor(Rt.KA)!.Windows.Select(w => w.Info.Key));
    }

    // ---- AC13 active engine ----

    [Fact]
    public void AC13_ActiveEngineSequence()
    {
        var t = new Rt();
        t.Ready();
        var r = t.Router;
        var a = t.EA_;
        var b = t.EB_;
        t.Win.Pointer = new Point(2500, 300);
        Assert.Same(b, r.ActiveEngine);

        r.WindowFocused(Rt.K("w1"));
        r.AppActivated(100, false);
        Assert.Same(a, r.ActiveEngine);

        r.FocusLeftWindows(200);
        r.AppActivated(200, false);
        Assert.Same(b, r.ActiveEngine);

        r.AppActivated(300, false);
        Assert.Same(b, r.ActiveEngine);
        t.Win.Pointer = new Point(10, 10);
        Assert.Same(a, r.ActiveEngine);
        t.Win.Pointer = new Point(-50, -50);
        Assert.Null(r.ActiveEngine);

        r.AppActivated(100, false);
        t.Win.Pointer = new Point(2500, 300);
        b.SetPhase(StagePhase.Overview);
        r.WindowFocused(Rt.K("w1"));
        Assert.Same(b, r.ActiveEngine);
    }

    [Fact]
    public void AC13_FirstOverviewEngineWins()
    {
        var t = new Rt();
        t.Ready();
        t.EA_.SetPhase(StagePhase.Overview);
        t.EB_.SetPhase(StagePhase.Overview);
        Assert.Same(t.EA_, t.Router.ActiveEngine);
    }

    [Fact]
    [Trait("Assumption", "A9")]
    public void AC13_Assumption_A9_NoActivationYetFocusedHolderWins()
    {
        var t = new Rt();
        t.Ready();
        t.Win.Pointer = new Point(2500, 300);
        t.Router.WindowFocused(Rt.K("w1"));
        Assert.Null(t.Router.LastActiveProcessId);
        Assert.Same(t.EA_, t.Router.ActiveEngine);
    }

    // ---- AC14 pointer engine ----

    [Fact]
    public void AC14_PointerEngineHalfOpen()
    {
        var t = new Rt();
        t.Ready();
        Assert.Same(t.EA_, t.Router.EngineAt(new Point(1919.999, 0)));
        Assert.Same(t.EB_, t.Router.EngineAt(new Point(1920, 0)));
        Assert.Null(t.Router.EngineAt(new Point(3200, 0)));
        Assert.Null(t.Router.EngineAt(new Point(0, 1080)));
        t.Win.Pointer = new Point(2500, 300);
        Assert.Same(t.EB_, t.Router.PointerEngine);
    }

    [Fact]
    public void AC14_BeforeReadyNull()
    {
        var t = new Rt();
        foreach (var p in new[] { new Point(0, 0), new Point(1920, 0), new Point(2500, 300) })
        {
            Assert.Null(t.Router.EngineAt(p));
        }

        Assert.Null(t.Router.PointerEngine);
        Assert.Null(t.Router.ActiveEngine);
    }

    // ---- AC15 command routes ----

    private static Rt Prepared()
    {
        var t = new Rt();
        t.Ready();
        t.Win.Pointer = new Point(2500, 300);
        t.Router.WindowFocused(Rt.K("w2"));
        t.Router.WindowFocused(Rt.K("w1"));
        t.Router.AppActivated(100, false);
        t.Win.Pump();
        return t;
    }

    [Fact]
    public void AC15_MoveRightHitsActiveEngineOnly()
    {
        var t = Prepared();
        Assert.Same(t.EA_, t.Router.ActiveEngine);
        // Rig: the started layout puts the empty desktop LEFT of w1's panel, so w1 has no right
        // neighbour. Frame the desktop first (direct engine call) so a tile lies to the right.
        t.EA_.Move(Edge.Left);
        Assert.Equal("desktop", t.EA_.FramedNodeId);
        int ha = t.EA_.FramingHistory.Count, hb = t.EB_.FramingHistory.Count;
        string? fa = t.EA_.FramedNodeId, fb = t.EB_.FramedNodeId;
        var cmd = ShortcutCommand.ForAction(ShortcutAction.Right);
        var route = t.Router.Execute(cmd);
        Assert.Equal(new CommandRoute(Rt.EA, cmd, null, null), route);
        Assert.Equal(ha + 1, t.EA_.FramingHistory.Count);
        Assert.NotEqual(fa, t.EA_.FramedNodeId);
        Assert.Equal(hb, t.EB_.FramingHistory.Count);
        Assert.Equal(fb, t.EB_.FramedNodeId);
    }

    [Fact]
    public void AC15_StepInRoutes()
    {
        var t = Prepared();
        var cmd = ShortcutCommand.ForAction(ShortcutAction.StepIn);
        var toward = new Point(2500, 300);
        Assert.Equal(new CommandRoute(Rt.EB, cmd, toward, null), t.Router.Execute(cmd));

        t.EB_.SetPhase(StagePhase.Overview);
        Assert.Equal(new CommandRoute(Rt.EB, cmd, null, null), t.Router.Execute(cmd));
        t.EB_.SetPhase(StagePhase.Desk);

        t.Router.StepTowardPointer = false;
        // A is active
        Assert.Equal(new CommandRoute(Rt.EA, cmd, null, null), t.Router.Execute(cmd));
    }

    [Fact]
    public void AC15_StepInPointerOffAllDisplaysFallsBackToActive()
    {
        var t = Prepared();
        t.Win.Pointer = new Point(-5, -5);
        var cmd = ShortcutCommand.ForAction(ShortcutAction.StepIn);
        Assert.Equal(new CommandRoute(Rt.EA, cmd, null, null), t.Router.Execute(cmd));
    }

    [Fact]
    [Trait("Assumption", "A5")]
    public void AC15_SplitRoutes()
    {
        var t = Prepared();
        var cmd = ShortcutCommand.ForAction(ShortcutAction.Split);
        Assert.Equal(new CommandRoute(Rt.EB, cmd, null, Rt.K("w2")), t.Router.Execute(cmd));

        t.EB_.FocusLeftWindows(0);
        Assert.Null(t.EB_.FocusedKey);
        Assert.Equal(new CommandRoute(Rt.EB, cmd, null, null), t.Router.Execute(cmd));

        t.Win.Pointer = new Point(-1, -1);
        Assert.Null(t.Router.Execute(cmd));
    }

    [Fact]
    public void AC15_GoToView()
    {
        var t = Prepared();
        var cmd = ShortcutCommand.GoToView(3);
        Assert.Equal(new CommandRoute(Rt.EA, cmd, null, null), t.Router.Execute(cmd));
    }

    private static IEnumerable<ShortcutCommand> AllCommands() =>
        Enum.GetValues<ShortcutAction>().Select(ShortcutCommand.ForAction)
            .Concat(new[] { ShortcutCommand.GoToView(1), ShortcutCommand.SaveView(9) });

    [Fact]
    public void AC15_InvokerReceivesExactlyOneCallPerCommand()
    {
        var t = Prepared();
        var calls = new List<(DisplayEngine E, CommandRoute R)>();
        t.Router.CommandInvoker = (e, r) => calls.Add((e, r));
        int ha = t.EA_.FramingHistory.Count;
        int n = 0;
        foreach (var cmd in AllCommands())
        {
            n++;
            var route = t.Router.Execute(cmd);
            Assert.NotNull(route);
            Assert.Equal(n, calls.Count);
            var (e, r) = calls[^1];
            Assert.Equal(cmd, r.Command);
            Assert.Equal(route, r);
            Assert.Equal(e.Key, r.Engine);
            TargetEngine target = cmd.Action is { } act ? ShortcutCatalog.Info(act).Target : TargetEngine.Active;
            DisplayEngine expected = target switch
            {
                TargetEngine.Active => t.EA_,
                _ => t.EB_,
            };
            Assert.Same(expected, e);
        }

        Assert.Equal(14, calls.Count);
        Assert.Equal(ha, t.EA_.FramingHistory.Count);
    }

    [Fact]
    public void AC15_SplitWithoutFocusNeitherInvokerNorEngine()
    {
        var t = Prepared();
        int n = 0;
        t.Router.CommandInvoker = (_, _) => n++;
        t.EB_.FocusLeftWindows(0);
        var route = t.Router.Execute(ShortcutCommand.ForAction(ShortcutAction.Split));
        Assert.NotNull(route);
        Assert.Null(route!.SplitKey);
        Assert.Equal(0, n);
    }

    // ---- AC16 no target ----

    [Fact]
    public void AC16_NoTargetCases()
    {
        var t = new Rt();
        int n = 0;
        t.Router.CommandInvoker = (_, _) => n++;
        t.Win.Pointer = new Point(10, 10);
        var right = ShortcutCommand.ForAction(ShortcutAction.Right);
        Assert.Null(t.Router.Execute(right));   // before Ready

        t.Ready();
        t.Win.Pointer = new Point(-9, -9);
        Assert.Null(t.Router.Execute(ShortcutCommand.ForAction(ShortcutAction.Split)));

        // rebuild gap
        t.Win.Pointer = new Point(10, 10);
        t.Router.DisplaysChanged(new[] { Rt.A });
        t.Clock.Advance(1.5);
        Assert.Empty(t.Router.Engines);
        Assert.Null(t.Router.Execute(right));
        t.Clock.Advance(0.8);
        Assert.NotNull(t.Router.Execute(right));
        n = 0;

        t.Router.BeginShutdown();
        Assert.Null(t.Router.Execute(right));
        Assert.Equal(0, n);
    }

    [Fact]
    public void AC16_ReentryFromOnChangeThrows()
    {
        var t = Prepared();
        var right = ShortcutCommand.ForAction(ShortcutAction.Right);
        Exception? caught = null;
        t.EA_.OnChange = () =>
        {
            try { t.Router.Execute(right); }
            catch (Exception ex) { caught = ex; }
        };
        t.Router.WindowFocused(Rt.K("w3"));   // causes an A change callback
        Assert.IsType<InvalidOperationException>(caught);
    }
}
