using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.RoutingSpec;

/// <summary>Rig of the engine-router spec: displays A and B, windows w1..w3, real engines with fakes.</summary>
internal sealed class Rt
{
    public static readonly DisplayKey KA = new("A");
    public static readonly DisplayKey KB = new("B");
    public static readonly EngineKey EA = new(KA, "default");
    public static readonly EngineKey EB = new(KB, "default");
    public static readonly DisplayInfo A = new(KA, new Rect(0, 0, 1920, 1080), new Rect(0, 0, 1920, 1040), 96, true);
    public static readonly DisplayInfo B = new(KB, new Rect(1920, 0, 1280, 720), new Rect(1920, 0, 1280, 680), 144, false);

    public readonly ManualClock Clock;
    public readonly FakeWindowSystem Win = new();
    public readonly FakeCapture Cap = new();
    public readonly InMemoryLimitStore Lim = new();
    public readonly InMemoryDisplayStore Mem = new();
    public IDisplayStore Store;
    public IClock RouterClock;
    public readonly List<(EngineKey Key, DisplayInfo Display)> FactoryCalls = new();
    public readonly List<DisplayEngine> Created = new();
    public readonly EngineRouter Router;

    public int StartedCount, ChangedCount;
    public readonly List<bool> FramesEvents = new();
    public readonly List<DisplayEngine> Retired = new();
    public int CompletedCount;
    public readonly List<string> Logs = new();
    public Func<EngineKey, DisplayInfo, DisplayEngine?>? FactoryOverride;

    public static WindowInfo W(string n, int pid, Rect f, bool dockable = true) =>
        new(new WindowKey(n), pid, "App" + pid, null, "t", f, dockable);

    public static readonly WindowInfo W1 = W("w1", 100, new Rect(100, 100, 800, 600));
    public static readonly WindowInfo W2 = W("w2", 200, new Rect(2000, 100, 800, 500));
    public static readonly WindowInfo W3 = W("w3", 100, new Rect(5000, 100, 800, 600));
    public static WindowKey K(string n) => new(n);

    public Rt(IReadOnlyList<DisplayInfo>? displays = null, RouterOptions? options = null, bool registerWindows = true,
              Func<IDisplayStore, IDisplayStore>? wrapStore = null, Func<ManualClock, IClock>? wrapClock = null)
    {
        Clock = new ManualClock(0);
        Store = wrapStore?.Invoke(Mem) ?? Mem;
        RouterClock = wrapClock?.Invoke(Clock) ?? Clock;
        displays ??= new[] { A, B };
        Win.Screens = displays.Select(d => d.Frame).ToList();
        if (registerWindows)
        {
            foreach (var w in new[] { W1, W2, W3 }) Win.AddWindow(w.Key, w.Frame);
        }

        Router = new EngineRouter(displays, Factory, RouterClock, Win, options);
        Router.Started += () => StartedCount++;
        Router.Changed += () => ChangedCount++;
        Router.FramesNeededChanged += b => FramesEvents.Add(b);
        Router.EngineRetired += e => Retired.Add(e);
        Router.ShutdownCompleted += () => CompletedCount++;
        Router.Log += s => Logs.Add(s);
    }

    private DisplayEngine Factory(EngineKey key, DisplayInfo d)
    {
        FactoryCalls.Add((key, d));
        if (FactoryOverride is { } f) return f(key, d)!;
        var e = Build(key, d);
        Created.Add(e);
        return e;
    }

    public DisplayEngine Build(EngineKey key, DisplayInfo d) =>
        new(key, d.WorkArea, EnginePolicy.Default,
            new EngineServices(Win, Cap, Clock, new RecordingRenderer(), Lim, Store));

    public Rt Ready(params WindowInfo[] ws)
    {
        Router.Ready(ws.Length == 0 ? new[] { W1, W2, W3 } : ws);
        return this;
    }

    public DisplayEngine EA_ => Router.EngineFor(KA)!;
    public DisplayEngine EB_ => Router.EngineFor(KB)!;

    public static Rect R(double x, double y, double w, double h) => new(x, y, w, h);
}
