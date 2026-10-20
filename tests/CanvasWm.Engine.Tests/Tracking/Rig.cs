using CanvasWm.Core.Geometry;
using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;
using CanvasWm.Engine.Tracking;

namespace CanvasWm.Engine.Tests.TrackingSpec;

/// <summary>Shared harness for the window-tracker tests.</summary>
internal sealed class Rig
{
    public ManualClock Clock { get; } = new(0);
    public FakeWindowSource Source { get; } = new();
    public FakeWindowSystem Sys { get; } = new();
    public RecordingTrackerTarget Target { get; }
    public WindowTracker Tracker { get; }

    public Rig(ClassifierPolicy? policy = null, TrackerOptions? options = null)
    {
        Target = new RecordingTrackerTarget(Clock);
        Tracker = new WindowTracker(Source, Sys, Clock, Target, policy ?? ClassifierPolicy.Default, options);
    }

    public static OsWindowFacts Std(ulong h, int pid) => new()
    {
        Handle = h, ProcessId = pid, ThreadId = pid * 10, AppId = "app" + pid, AppName = "App" + pid,
        Title = "t" + h, Frame = new Rect(100, 100, 900, 700),
        IsTopLevel = true, HasCaption = true, IsResizable = true, HasMaximizeButton = true,
        IsVisible = true, IsAccessible = true, MaximizeEnabled = true, Cloak = CloakKind.None,
    };

    public static WindowKey K(ulong h) => WindowTracker.KeyFor(h);

    /// <summary>Advance so that Now equals t (up to float rounding).</summary>
    public void AdvanceTo(double t) => Clock.Advance(t - Clock.Now);

    public Rig Put(params OsWindowFacts[] fs)
    {
        foreach (var f in fs) Source.Put(f);
        return this;
    }

    public IEnumerable<TrackerCall> Named(string name) => Target.Calls.Where(c => c.Name == name);

    public static string[] Names(IEnumerable<TrackerCall> calls) => calls.Select(c => c.Name).ToArray();

    /// <summary>Compact "Name:key:flag" description of calls.</summary>
    public static string[] Desc(IEnumerable<TrackerCall> calls) =>
        calls.Select(c => $"{c.Name}:{c.Key?.Value ?? c.ProcessId?.ToString() ?? ""}:{c.Flag?.ToString() ?? ""}").ToArray();
}
