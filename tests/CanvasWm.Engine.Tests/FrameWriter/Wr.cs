using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;
using CanvasWm.Core.Projection;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.FrameWriterSpec;

/// <summary>Rig for the frame-writer spec (spec "Rig" section).</summary>
internal sealed class Wr
{
    public static readonly Rect VA = new(0, 25, 1440, 875);
    public readonly ManualClock Clock = new(99); // migrated for #27 (AC32): recipe R step 2
    public readonly FakeWindowSystem Win = new();
    public readonly FakeCapture Cap = new();
    public readonly RecordingRenderer Ren = new();
    public readonly InMemoryLimitStore Lim = new();
    public readonly InMemoryDisplayStore Disp = new();
    public readonly EngineKey Key = new(new DisplayKey("D1"), "default");
    public readonly AppId App = new("com.x");
    public readonly List<string> Logs = new();
    public int Changes;
    public readonly DisplayEngine E;
    public readonly int G0, W0, R0, S0, P0, Pl0, Wp0;

    public static WindowKey K(string n) => new(n);
    public static Rect R(double x, double y, double w, double h) => new(x, y, w, h);
    public static WindowTarget T(double x, double y, double w, double h) => new(TargetKind.Tile, new Rect(x, y, w, h));
    public static WindowTarget P(double x, double y, double w, double h) => new(TargetKind.Park, new Rect(x, y, w, h));

    public static LayoutNode Pn(string n) => PanelNode.ForWindow(n);
    public static LayoutDocument L2() => new(new SplitNode("s", Axis.X, new[] { .5, .5 }, new[] { Pn("a"), Pn("b") }));

    public static LayoutDocument L3() => new(new SplitNode("s1", Axis.X, new[] { .5, .5 }, new LayoutNode[]
    {
        Pn("a"),
        new SplitNode("s2", Axis.Y, new[] { .5, .5 }, new[] { Pn("b"), Pn("c") }),
    }));

    public static readonly Rect A3 = R(10, 35, 705, 855);
    public static readonly Rect B3 = R(725, 35, 705, 423);
    public static readonly Rect C3 = R(725, 468, 705, 422);

    public WindowInfo Info(string n, Rect f, bool noApp = false) =>
        new(K(n), 1, "X", noApp ? null : App, "t", f, false); // migrated for #27 (AC32): recipe R step 1

    /// <param name="wins">windows in registration order with known (and OS) frames.</param>
    public Wr(LayoutDocument? doc, string? framed, (string N, Rect F)[] wins, EnginePolicy? policy = null,
              SavedDisplay? saved = null, string[]? noApp = null)
    {
        var services = new EngineServices(Win, Cap, Clock, Ren, Lim, Disp);
        E = new DisplayEngine(Key, VA, policy ?? EnginePolicy.Default, services);
        E.DisplayFrame = new Rect(0, 0, 1440, 900);
        E.Ids = new CanvasWm.Engine.Tests.WindowEventsSpec.SequentialIds(); // migrated for #27 (AC32)
        E.OnChange = () => Changes++;
        E.Log = s => Logs.Add(s);
        if (saved is not null) Disp.Save(Key, saved);
        var infos = new List<WindowInfo>();
        foreach (var (n, f) in wins)
        {
            Win.AddWindow(K(n), f);
            infos.Add(Info(n, f, noApp?.Contains(n) == true));
        }

        E.Start(infos);
        Clock.Advance(1); // migrated for #27 (AC32): recipe R step 2
        Win.Pump();
        if (saved is null && doc is not null) E.SetLayout(doc, framed);
        G0 = E.PlacementGeneration;
        W0 = Win.Writes.Count;
        R0 = Win.Reads.Count;
        S0 = Disp.SaveLog.Count;
        P0 = Clock.PendingCount;
        Pl0 = Ren.Plans.Count;
        Wp0 = Win.PendingCount;
        Changes = 0;
        Logs.Clear();
    }

    public static Wr L3At(EnginePolicy? policy = null) =>
        new(L3(), null, new[] { ("a", A3), ("b", R(725, 35, 705, 855)), ("c", C3) }, policy);

    /// <summary>L3, a and b at targets, c known (725,35,705,855).</summary>
    public static Wr L3C(EnginePolicy? policy = null) =>
        new(L3(), null, new[] { ("a", A3), ("b", B3), ("c", R(725, 35, 705, 855)) }, policy);

    /// <summary>L3 with all three windows tall (all differ from targets).</summary>
    public static Wr L3AllDiffer(EnginePolicy? policy = null) =>
        new(L3(), null, new[] { ("a", R(10, 35, 600, 855)), ("b", R(725, 35, 705, 855)), ("c", R(725, 35, 705, 855)) }, policy);

    public List<FrameWriteRecord> Writes() => Win.Writes.Skip(W0).ToList();
    public List<WindowKey> Reads() => Win.Reads.Skip(R0).ToList();
    public int Saves => Disp.SaveLog.Count - S0;
    public int Renders => Ren.Plans.Count - Pl0;
    public int Pending => Clock.PendingCount - P0;
    public int G => E.PlacementGeneration - G0;

    public void Adv(double dt) => Clock.Advance(dt + 1e-12);

    /// <summary>Advance to the absolute time t, then run one fake-OS operation.</summary>
    public void Complete(double t)
    {
        At(t);
        Win.Step();
    }

    public void At(double t) => Clock.Advance(Math.Max(0, t - Clock.Now) + 1e-12);

    public FrameWriterState S(string n) => E.WriterState(K(n))!;
    public Rect Known(string n) => E.Window(K(n))!.Info.Frame;

    public WindowTarget PwP(Size z, Size m) =>
        new(TargetKind.ParkWithPicture, ParkGeometry.ParkFrame(E.ParkContext(), z, m));

    public static void Near(double expected, double? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual!.Value, expected - 1e-9, expected + 1e-9);
    }
}
