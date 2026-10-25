using CanvasWm.Core.Frames;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tracking;

namespace CanvasWm.Engine.Routing;

/// <summary>Coordinator over the per-(display, desktop) engines. Single-threaded: every member
/// is called on the engine (UI) thread, and every clock action and engine callback arrives there.
/// Implements the tracker target. Not reusable after BeginShutdown.</summary>
public sealed partial class EngineRouter : IWindowTrackerTarget
{
    private readonly Func<EngineKey, DisplayInfo, DisplayEngine> _factory;
    private readonly IClock _clock;
    private readonly IWindowSystem _windows;
    private readonly List<DisplayEngine> _engines = new();
    private readonly HashSet<DisplayEngine> _startedBefore = new();
    private readonly List<WindowInfo> _mirror = new();
    private readonly Dictionary<DisplayKey, string> _desktops = new();
    private List<DisplayInfo> _applied;
    private List<DisplayInfo> _latest;
    private string _appliedSignature;
    private int _generation;
    private bool _lastOwnsDesktop;
    private bool _startedRaised;
    private bool _lastFramesNeeded;
    private bool _complete;
    private double _deadline = double.PositiveInfinity;

    /// <summary>Creates an idle router (B3). Creates no engine, calls neither the factory nor the
    /// clock nor the window system.</summary>
    /// <param name="displays">Initial display snapshot (validated as in DisplaysChanged).</param>
    /// <param name="engineFactory">Creates the engine for (key, display). Must return an engine
    /// whose Key equals key, built with display.WorkArea as its visible area.</param>
    /// <param name="clock">Delays (debounce, re-show, quit deadline).</param>
    /// <param name="windows">Read for Pointer only.</param>
    /// <param name="options">null = RouterOptions.Default.</param>
    /// <exception cref="ArgumentNullException">A required argument or a display is null.</exception>
    /// <exception cref="ArgumentException">Two displays share a key.</exception>
    public EngineRouter(IReadOnlyList<DisplayInfo> displays,
                        Func<EngineKey, DisplayInfo, DisplayEngine> engineFactory,
                        IClock clock, IWindowSystem windows, RouterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(engineFactory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(windows);
        _applied = ValidateDisplays(displays);
        _latest = _applied;
        _factory = engineFactory;
        _clock = clock;
        _windows = windows;
        Options = options ?? RouterOptions.Default;
        _appliedSignature = DisplayInfo.SignatureOf(_applied);
        foreach (DisplayInfo d in _applied) _desktops[d.Key] = EngineKey.SingleDesktopId;
        StepTowardPointer = true;
    }

    // ---- state ----

    /// <summary>The router timings.</summary>
    public RouterOptions Options { get; }

    /// <summary>The applied display list, in snapshot order (copy).</summary>
    public IReadOnlyList<DisplayInfo> Displays => _applied.ToArray();

    /// <summary>Existing engines in creation order (copy).</summary>
    public IReadOnlyList<DisplayEngine> Engines => _engines.ToArray();

    /// <summary>The engine of the desktop currently shown on that display, or null.</summary>
    /// <param name="display">The display.</param>
    /// <returns>The engine or null.</returns>
    public DisplayEngine? EngineFor(DisplayKey display)
    {
        if (display is null || !_desktops.TryGetValue(display, out string? desktop)) return null;
        var key = new EngineKey(display, desktop);
        foreach (DisplayEngine e in _engines)
        {
            if (e.Key == key) return e;
        }

        return null;
    }

    /// <summary>Desktop id currently shown on the display (EngineKey.SingleDesktopId until #35);
    /// null for an unknown display.</summary>
    /// <param name="display">The display.</param>
    /// <returns>The desktop id or null.</returns>
    public string? DesktopOf(DisplayKey display) =>
        display is not null && _desktops.TryGetValue(display, out string? d) ? d : null;

    /// <summary>True after Ready was received.</summary>
    public bool IsReady { get; private set; }

    /// <summary>True from a changed-display notification until the following re-show (B15).</summary>
    public bool IsTornDown { get; private set; }

    /// <summary>True after BeginShutdown.</summary>
    public bool IsShuttingDown { get; private set; }

    /// <summary>True once shutdown completed: all releases reported or the deadline passed (B33).</summary>
    public bool IsShutdownComplete => _complete || (IsShuttingDown && _clock.Now >= _deadline);

    /// <summary>Key of the last window reported focused (B12).</summary>
    public WindowKey? LastFocusedKey { get; private set; }

    /// <summary>Process id of the last activated app, or null before any.</summary>
    public int? LastActiveProcessId { get; private set; }

    /// <summary>User preference "step in toward the pointer" (#7 B4). Default true.</summary>
    public bool StepTowardPointer { get; set; }

    /// <summary>Aggregated frame request (B29).</summary>
    public bool FramesNeeded => ComputeFramesNeeded();

    /// <summary>Active engine per B23, or null.</summary>
    public DisplayEngine? ActiveEngine
    {
        get
        {
            List<DisplayEngine> shown = ShownEngines();
            foreach (DisplayEngine e in shown)
            {
                if (e.Phase == StagePhase.Overview) return e;
            }

            if (LastFocusedKey is { } fk && Holder(fk) is { } h
                && (LastActiveProcessId is null || h.Window(fk)?.Info.ProcessId == LastActiveProcessId))
            {
                // SPEC-ASSUMPTION A9: with no activation yet, the holder of the last focused window wins.
                return h;
            }

            if (LastActiveProcessId is { } pid)
            {
                foreach (DisplayEngine e in shown)
                {
                    foreach (ManagedWindow w in e.Windows)
                    {
                        if (w.Info.ProcessId == pid) return e;
                    }
                }
            }

            return PointerEngine;
        }
    }

    /// <summary>Engine under the window system's pointer (B24), or null.</summary>
    public DisplayEngine? PointerEngine => EngineAt(_windows.Pointer);

    /// <summary>Engine shown on the display whose frame contains point (B24), or null.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The engine or null.</returns>
    public DisplayEngine? EngineAt(Point point)
    {
        foreach (DisplayInfo d in _applied)
        {
            Rect f = d.Frame;
            if (f.X <= point.X && point.X < f.X + f.Width && f.Y <= point.Y && point.Y < f.Y + f.Height)
            {
                return EngineFor(d.Key);
            }
        }

        return null;
    }

    // ---- outbound events ----

    /// <summary>Raised once, after the first successful show of desktops (B8).</summary>
    public event Action? Started;

    /// <summary>An engine reported a menu-relevant change, or the engine set changed (B30).</summary>
    public event Action? Changed;

    /// <summary>FramesNeeded changed (edge-triggered, B29).</summary>
    public event Action<bool>? FramesNeededChanged;

    /// <summary>An engine was dropped by a rebuild, after its ReleaseAll (B18); the App disposes
    /// its per-display resources.</summary>
    public event Action<DisplayEngine>? EngineRetired;

    /// <summary>Raised exactly once when shutdown completes (B33).</summary>
    public event Action? ShutdownCompleted;

    /// <summary>Diagnostics (engine Log callbacks forwarded, router notes). No content contract.</summary>
    public event Action<string>? Log;

    /// <summary>Test seam: when set, Execute calls this instead of the DisplayEngine method.</summary>
    internal Action<DisplayEngine, CommandRoute>? CommandInvoker { get; set; }

    // ---- helpers ----

    private static List<DisplayInfo> ValidateDisplays(IReadOnlyList<DisplayInfo> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var copy = new List<DisplayInfo>(displays.Count);
        var seen = new HashSet<DisplayKey>();
        foreach (DisplayInfo d in displays)
        {
            ArgumentNullException.ThrowIfNull(d, nameof(displays));
            if (!seen.Add(d.Key)) throw new ArgumentException("Duplicate display key.", nameof(displays));
            copy.Add(d);
        }

        return copy;
    }

    private DisplayEngine? Holder(WindowKey key)
    {
        foreach (DisplayEngine e in _engines)
        {
            if (e.Holds(key)) return e;
        }

        return null;
    }

    private List<DisplayEngine> ShownEngines()
    {
        var list = new List<DisplayEngine>();
        foreach (DisplayInfo d in _applied)
        {
            if (EngineFor(d.Key) is { } e) list.Add(e);
        }

        return list;
    }

    private bool ComputeFramesNeeded()
    {
        if (IsTornDown || IsShuttingDown) return false;
        foreach (DisplayEngine e in _engines)
        {
            if (e.FramesRunning) return true;
        }

        return false;
    }

    private void RecomputeFrames()
    {
        bool now = ComputeFramesNeeded();
        if (now == _lastFramesNeeded) return;
        _lastFramesNeeded = now;
        FramesNeededChanged?.Invoke(now);
    }

    private static Rect? FallbackArea(IReadOnlyList<DisplayInfo> list)
    {
        if (list.Count == 0) return null;
        foreach (DisplayInfo d in list)
        {
            if (d.IsMain) return d.WorkArea;
        }

        return list[0].WorkArea;
    }

    private static bool Contains(IReadOnlyList<DisplayInfo> list, DisplayKey key)
    {
        foreach (DisplayInfo d in list)
        {
            if (d.Key == key) return true;
        }

        return false;
    }

    /// <summary>Owner display of a window (B6).</summary>
    private DisplayInfo? OwnerOf(WindowKey key, Rect frame)
    {
        if (Holder(key) is { } h)
        {
            foreach (DisplayInfo d in _applied)
            {
                if (d.Key == h.DisplayKey) return d;
            }
        }

        if (_applied.Count == 0) return null;
        int main = _applied.FindIndex(d => d.IsMain);
        if (main < 0) main = 0;
        int i = DisplayChoice.ForRect(frame, _applied.Select(d => d.Frame).ToList(), main);
        return i < 0 ? null : _applied[i];
    }

    // ---- engines (B5, B7, B8) ----

    private DisplayEngine GetOrCreate(DisplayInfo d)
    {
        if (EngineFor(d.Key) is { } existing) return existing;
        var key = new EngineKey(d.Key, _desktops[d.Key]);
        DisplayEngine? e = _factory(key, d);
        if (e is null || e.Key != key)
        {
            throw new InvalidOperationException("The engine factory returned no engine or an engine with another key.");
        }

        e.DisplayFrame = d.Frame;
        e.OtherDisplays = _applied.Where(o => o.Key != d.Key).Select(o => o.Frame).ToList();
        e.OnNeedsFrames = _ => RecomputeFrames();
        e.OnChange = () => Changed?.Invoke();
        e.Log = s => Log?.Invoke(s);
        if (LastActiveProcessId is { } pid) e.AppActivated(pid, _lastOwnsDesktop);
        _engines.Add(e);
        Changed?.Invoke();
        return e;
    }

    private void ShowOne(DisplayInfo d)
    {
        DisplayEngine e = GetOrCreate(d);
        var list = new List<WindowInfo>();
        foreach (WindowInfo w in _mirror)
        {
            if (OwnerOf(w.Key, w.Frame) is { } o && o.Key == d.Key) list.Add(w);
        }

        if (_startedBefore.Contains(e))
        {
            e.Resume(list);
        }
        else
        {
            _startedBefore.Add(e);
            e.Start(list);
        }
    }

    private void ShowAll()
    {
        if (!IsReady || IsTornDown || IsShuttingDown) return;
        foreach (DisplayInfo d in _applied.ToArray()) ShowOne(d);
        if (!_startedRaised)
        {
            _startedRaised = true;
            Started?.Invoke();
        }
    }

    // ---- Tick ----

    /// <summary>Calls Tick(now) on every shown engine whose frames are running (B31).</summary>
    /// <param name="now">Absolute seconds on the clock's time base.</param>
    /// <exception cref="ArgumentOutOfRangeException">now is NaN or infinite.</exception>
    public void Tick(double now)
    {
        if (!double.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
        if (IsTornDown || IsShuttingDown) return;
        foreach (DisplayEngine e in ShownEngines())
        {
            if (e.FramesRunning) e.Tick(now);
        }
    }
}
