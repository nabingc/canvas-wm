using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.EngineShellSpec;

public sealed record SpecRestore(string Tag) : RestoreTarget;

public sealed class Rig
{
    public static readonly Rect VA = new(0, 25, 1440, 875);
    public readonly ManualClock Clock = new(100);
    public readonly FakeWindowSystem Win = new();
    public readonly FakeCapture Cap = new();
    public readonly RecordingRenderer Ren = new();
    public readonly InMemoryLimitStore Lim = new();
    public readonly InMemoryDisplayStore Disp = new();
    public readonly EngineKey Key = new(new DisplayKey("D1"), "default");
    public readonly List<string> Log = new();
    public readonly DisplayEngine E;
    public readonly EngineServices Services;

    public Rig(EnginePolicy? policy = null, bool wire = true)
    {
        Services = new EngineServices(Win, Cap, Clock, Ren, Lim, Disp);
        E = new DisplayEngine(Key, VA, policy ?? EnginePolicy.Default, Services);
        if (wire) Wire();
    }

    public void Wire()
    {
        E.OnChange = () => Log.Add("change");
        E.OnNeedsFrames = b => Log.Add("nf:" + b);
    }

    public static WindowKey K(string n) => new(n);

    public static WindowInfo W(string n, int pid = 1, string title = "t", Rect? frame = null, bool dockable = false) =>
        new(K(n), pid, "App" + pid, null, title, frame ?? new Rect(100, 100, 800, 600), dockable);

    /// <summary>Registers the window in the fake OS (so writes can apply) and returns it.</summary>
    public WindowInfo OS(WindowInfo w)
    {
        if (Win.FrameOf(w.Key) is null) Win.AddWindow(w.Key, w.Frame);
        return w;
    }

    public Rig StartWith(params WindowInfo[] ws)
    {
        E.Start(ws.Select(OS).ToList());
        return this;
    }

    public string Snap() => string.Join("|",
        Win.Writes.Count, Win.Reads.Count, Win.FocusRequests.Count, Win.PendingCount,
        Ren.Plans.Count, Disp.SaveLog.Count, Clock.PendingCount,
        string.Join(",", Enumerable.Range(1, 12).Select(i => Cap.IsStarted(K("w" + i)) ? Cap.RecaptureRequests(K("w" + i)) : -1)));
}
