using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.WindowEventsSpec;

/// <summary>Rig for the engine-window-events spec (spec "Rig" section).</summary>
internal sealed class Ev
{
    public static readonly Rect F0 = new(100, 100, 400, 300);
    public static readonly Rect NFrame = new(200, 200, 400, 300);

    public readonly ManualClock Clock;
    public readonly FakeWindowSystem Win = new();
    public readonly FakeCapture Cap = new();
    public readonly RecordingRenderer Ren = new();
    public readonly InMemoryLimitStore Lim = new();
    public readonly InMemoryDisplayStore Disp = new();
    public readonly EngineKey Key = new(new DisplayKey("D1"), "default");
    public readonly List<string> Logs = new();
    public readonly DisplayEngine E;
    public int Changes;

    private readonly Dictionary<string, int> _pids = new();

    // baselines for deltas
    private int _saves, _plans, _focus, _changes, _pending, _gen, _writes, _logs;

    public Ev(Rect? va = null, EnginePolicy? policy = null, double t0 = 100)
    {
        Clock = new ManualClock(t0);
        var services = new EngineServices(Win, Cap, Clock, Ren, Lim, Disp);
        E = new DisplayEngine(Key, va ?? new Rect(0, 25, 1440, 875), policy ?? EnginePolicy.Default, services);
        E.DisplayFrame = new Rect(0, 0, 1440, 900);
        E.Ids = new SequentialIds();
        E.OnChange = () => Changes++;
        E.Log = s => Logs.Add(s);
        Mark();
    }

    public static WindowKey K(string n) => new(n);
    public static Rect R(double x, double y, double w, double h) => new(x, y, w, h);
    public static LayoutNode D => new DesktopNode();
    public static LayoutNode P(string k) => PanelNode.ForWindow(k);

    public static LayoutNode X(string id, params (LayoutNode N, double W)[] c) =>
        new SplitNode(id, Axis.X, c.Select(x => x.W), c.Select(x => x.N));

    public static LayoutNode Y(string id, params (LayoutNode N, double W)[] c) =>
        new SplitNode(id, Axis.Y, c.Select(x => x.W), c.Select(x => x.N));

    public static (LayoutNode, double) At(LayoutNode n, double w) => (n, w);
    public static (LayoutNode, double) H(LayoutNode n) => (n, .5);

    public static LayoutDocument Doc(LayoutNode? root) => new(root);

    /// <summary>Spec AC1 layout of a,b with the desktop on the left.</summary>
    public static LayoutDocument DocAb(string n1 = "n1", string n2 = "n2") =>
        Doc(X(n1, H(D), H(Y(n2, H(P("a")), H(P("b"))))));

    public WindowInfo W(string k, bool dockable = true, Rect? frame = null)
    {
        var f = frame ?? (k == "n" ? NFrame : F0);
        if (Win.FrameOf(K(k)) is null) Win.AddWindow(K(k), f);
        if (!_pids.TryGetValue(k, out var pid)) _pids[k] = pid = _pids.Count + 1;
        return new WindowInfo(K(k), pid, "X", new AppId("com.x"), "t", f, dockable);
    }

    public int Pid(string k) => _pids[k];

    /// <summary>Spec "Land".</summary>
    public void Land()
    {
        Win.Pump();
        Clock.Advance(0.2);
        Win.Pump();
    }

    public void AtTime(double t) => Clock.Advance(Math.Max(0, t - Clock.Now));

    /// <summary>S1: Start([a, b, n]) then Land.</summary>
    public static Ev S1(EnginePolicy? policy = null, Rect? va = null)
    {
        var ev = new Ev(va, policy);
        ev.E.Start(new[] { ev.W("a"), ev.W("b"), ev.W("n", false) });
        ev.Land();
        ev.Mark();
        return ev;
    }

    /// <summary>S2: S1, then advance to 100.7 and WindowFocused(a).</summary>
    public static Ev S2()
    {
        var ev = S1();
        ev.AtTime(100.7);
        ev.E.WindowFocused(K("a"));
        ev.Mark();
        return ev;
    }

    /// <summary>AC5 state: S1, focus b, add c; windows pumped.</summary>
    public static Ev Ac5(bool pump = true)
    {
        var ev = S1();
        ev.E.WindowFocused(K("b"));
        ev.E.WindowAdded(ev.W("c"));
        if (pump) ev.Win.Pump();
        ev.Mark();
        return ev;
    }

    public void Mark()
    {
        _saves = Disp.SaveLog.Count;
        _plans = Ren.Plans.Count;
        _focus = Win.FocusRequests.Count;
        _changes = Changes;
        _pending = Clock.PendingCount;
        _gen = E.PlacementGeneration;
        _writes = Win.Writes.Count;
        _logs = Logs.Count;
    }

    public int dSaves => Disp.SaveLog.Count - _saves;
    public int dPlans => Ren.Plans.Count - _plans;
    public int dFocus => Win.FocusRequests.Count - _focus;
    public int dChanges => Changes - _changes;
    public int dPending => Clock.PendingCount - _pending;
    public int dGen => E.PlacementGeneration - _gen;
    public int dWrites => Win.Writes.Count - _writes;
    public int dLogs => Logs.Count - _logs;
    public IReadOnlyList<string> NewLogs => Logs.Skip(_logs).ToList();
    public IReadOnlyList<WindowKey> NewFocus => Win.FocusRequests.Skip(_focus).ToList();

    /// <summary>Writes since the last mark, grouped into whole batches.</summary>
    public List<List<(string K, Rect R)>> Batches()
    {
        var res = new List<List<(string, Rect)>>();
        int? cur = null;
        foreach (var w in Win.Writes.Skip(_writes))
        {
            if (w.BatchId is { } b && cur == b) res[^1].Add((w.Key.Value, w.Target));
            else
            {
                res.Add(new List<(string, Rect)> { (w.Key.Value, w.Target) });
                cur = w.BatchId;
            }
        }

        return res;
    }

    public List<(string K, Rect R)> OneBatch()
    {
        var b = Batches();
        Assert.Single(b);
        return b[0];
    }

    public SavedDisplay LastSaved => Disp.SaveLog[^1].Display;
    public WindowTarget? Target(string k) => E.ComputeTargets().TryGetValue(K(k), out var t) ? t : null;
    public WindowTarget Tgt(string k) => E.ComputeTargets()[K(k)];
    public Rect Known(string k) => E.Window(K(k))!.Info.Frame;

    public static void Near(double expected, double actual) =>
        Assert.InRange(actual, expected - 1e-9, expected + 1e-9);

    /// <summary>Simulates an app move: the OS frame changes, then the event arrives.</summary>
    public void Move(string k, Rect f)
    {
        Win.SetFrameDirect(K(k), f);
        E.WindowMoved(K(k), f);
    }

    public IReadOnlyList<string> PanelKeys() =>
        LayoutEdits.PanelsOf(E.Document.Root).SelectMany(p => p.Views).ToList();

    /// <summary>Asserts the document equals expected, comparing weights within 1e-9.</summary>
    public static void SameDoc(LayoutDocument expected, LayoutDocument actual)
    {
        Assert.True(Same(expected.Root, actual.Root), "tree differs");
        Assert.Equal(expected.Floating, actual.Floating);
        Assert.Equal(expected.Hidden, actual.Hidden);
        Assert.Equal(expected.HiddenDesktop, actual.HiddenDesktop);
    }

    private static bool Same(LayoutNode? a, LayoutNode? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is SplitNode sa && b is SplitNode sb)
        {
            if (sa.Id != sb.Id || sa.Axis != sb.Axis || sa.Children.Length != sb.Children.Length) return false;
            for (int i = 0; i < sa.Children.Length; i++)
            {
                if (Math.Abs(sa.Weights[i] - sb.Weights[i]) > 1e-9 || !Same(sa.Children[i], sb.Children[i])) return false;
            }

            return true;
        }

        return a.Equals(b);
    }
}
