using System.Text;
using CanvasWm.Core;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;
using CanvasWm.Engine.Rendering;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine;

/// <summary>The window manager for one (display, virtual desktop). Single-threaded: every member
/// is called on the UI thread, and every callback and service completion arrives on it.</summary>
public sealed partial class DisplayEngine
{
    private readonly List<ManagedWindow> _windows = new();
    private readonly Dictionary<WindowKey, DockSpot?> _away = new();
    private readonly Dictionary<int, WindowKey> _lastFocused = new();
    private readonly Queue<Action> _queue = new();
    private IReadOnlyList<Rect> _otherDisplays = Array.Empty<Rect>();
    private EnginePolicy _policy;
    private Rect _visibleArea;
    private Rect? _displayFrame;
    private double? _topBarBottom;
    private SavedDisplay? _saved;
    private bool _inCall;
    private bool _delivering;
    private bool _changeRequested;
    private readonly Dictionary<WindowKey, WriterRecord> _writer = new();
    private Tuning _tuning = Tuning.Default;
    private Action? _onChange;
    private Action<bool>? _onNeedsFrames;
    private Action<ManagedWindow, bool>? _onHandOff;
    private Func<WindowKey, bool>? _onCarryOut;
    private Action<string>? _log;

    /// <summary>Creates an idle engine (not started, not suspended, phase Desk). Makes no service
    /// call other than reading Clock.Now. Does not load the saved layout.</summary>
    /// <param name="key">The engine key.</param>
    /// <param name="visibleArea">The display's work area.</param>
    /// <param name="policy">The initial policy.</param>
    /// <param name="services">The services bundle.</param>
    /// <exception cref="ArgumentNullException">key, policy or services is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">visibleArea invalid.</exception>
    public DisplayEngine(EngineKey key, Rect visibleArea, EnginePolicy policy, EngineServices services)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        ValidateRect(visibleArea, nameof(visibleArea));
        _visibleArea = visibleArea;
        LastTick = services.Clock.Now;
    }

    // ---------- identity and display context ----------

    /// <summary>The engine key.</summary>
    public EngineKey Key { get; }

    /// <summary>Key.Display.</summary>
    public DisplayKey DisplayKey => Key.Display;

    /// <summary>The services bundle.</summary>
    public EngineServices Services { get; }

    /// <summary>The display's work area.</summary>
    public Rect VisibleArea => _visibleArea;

    /// <summary>Full frame of this engine's display; null until the app sets it.</summary>
    public Rect? DisplayFrame
    {
        get => _displayFrame;
        set { EnsureNotInCallback(); _displayFrame = value; }
    }

    /// <summary>Full frames of every other display (own display excluded). Copied on set.
    /// Default empty.</summary>
    /// <exception cref="ArgumentNullException">On set with null.</exception>
    public IReadOnlyList<Rect> OtherDisplays
    {
        get => _otherDisplays;
        set
        {
            EnsureNotInCallback();
            ArgumentNullException.ThrowIfNull(value);
            _otherDisplays = Array.AsReadOnly(value.ToArray());
        }
    }

    /// <summary>y of the bottom edge of a system bar along the top of this display, or null when
    /// the platform has none (always null on Windows).</summary>
    public double? TopBarBottom
    {
        get => _topBarBottom;
        set { EnsureNotInCallback(); _topBarBottom = value; }
    }

    /// <summary>Park context from VisibleArea, DisplayFrame, OtherDisplays and
    /// Services.Windows.ParkingOrigin, evaluated now.</summary>
    /// <returns>The park display context.</returns>
    public ParkDisplay ParkContext() =>
        new(VisibleArea, DisplayFrame, OtherDisplays, Services.Windows.ParkingOrigin);

    // ---------- policy ----------

    /// <summary>Current policy. Setting a changed value re-frames (relayout settings) or redraws,
    /// but only while started and not suspended.</summary>
    /// <exception cref="ArgumentNullException">On set with null.</exception>
    public EnginePolicy Policy
    {
        get => _policy;
        set => Run(() =>
        {
            ArgumentNullException.ThrowIfNull(value);
            var old = _policy;
            _policy = value;
            // SPEC-ASSUMPTION A12: policy changes while suspended (or not started) are stored only.
            if (old == value || !IsStarted || IsSuspended) return;
            if (EnginePolicy.RequiresRelayout(old, value)) Reframe();
            else Render();
        });
    }

    // ---------- outbound callbacks ----------

    /// <summary>State shown in menus changed.</summary>
    public Action? OnChange { get => _onChange; set { EnsureNotInCallback(); _onChange = value; } }

    /// <summary>The frames-running flag changed; true = start calling Tick each display refresh,
    /// false = stop. Fired only on transitions.</summary>
    public Action<bool>? OnNeedsFrames { get => _onNeedsFrames; set { EnsureNotInCallback(); _onNeedsFrames = value; } }

    /// <summary>A window was moved to another display (dragged = moved by a pointer drag). Raised by
    /// multi-display support only.</summary>
    public Action<ManagedWindow, bool>? OnHandOff { get => _onHandOff; set { EnsureNotInCallback(); _onHandOff = value; } }

    /// <summary>Asks whether another display accepts a window being dragged out; null handler =
    /// false. Queried by multi-display support only.</summary>
    public Func<WindowKey, bool>? OnCarryOut { get => _onCarryOut; set { EnsureNotInCallback(); _onCarryOut = value; } }

    /// <summary>Diagnostic text. No content contract.</summary>
    public Action<string>? Log { get => _log; set { EnsureNotInCallback(); _log = value; } }

    // ---------- inspectors ----------

    /// <summary>True after Start until ReleaseAll ("adopted").</summary>
    public bool IsStarted { get; private set; }

    /// <summary>True after Suspend until Resume ("frozen").</summary>
    public bool IsSuspended { get; private set; }

    /// <summary>The stage phase.</summary>
    public StagePhase Phase { get; private set; }

    /// <summary>The focused window, or null.</summary>
    public WindowKey? FocusedKey { get; private set; }

    /// <summary>Managed windows in registration order (first add).</summary>
    public IReadOnlyList<ManagedWindow> Windows => Array.AsReadOnly(_windows.ToArray());

    /// <summary>Snapshot for key, or null if not managed.</summary>
    /// <param name="key">The window.</param>
    /// <returns>The snapshot or null.</returns>
    public ManagedWindow? Window(WindowKey key)
    {
        int i = key is null ? -1 : IndexOf(key);
        return i < 0 ? null : _windows[i];
    }

    /// <summary>True if key is managed.</summary>
    /// <param name="key">The window.</param>
    /// <returns>Whether it is managed.</returns>
    public bool Holds(WindowKey key) => key is not null && IndexOf(key) >= 0;

    /// <summary>Temporarily removed windows and their remembered spots (value may be null).</summary>
    public IReadOnlyDictionary<WindowKey, DockSpot?> Away => new Dictionary<WindowKey, DockSpot?>(_away);

    /// <summary>Process id of the frontmost app as last reported, or null before any report.</summary>
    public int? ActiveProcessId { get; private set; }

    /// <summary>The frontmost app owns the desktop (Explorer desktop on Windows).</summary>
    public bool DesktopOwnerActive { get; private set; }

    /// <summary>The frames-running flag.</summary>
    public bool FramesRunning { get; private set; }

    /// <summary>Whether anything still needs per-frame ticks.</summary>
    public bool NeedsFrames => MotionActive;

    /// <summary>Human-readable dump; contains Key.Value. No format contract.</summary>
    public string DebugState
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append("Engine ").Append(Key.Value)
              .Append(" started=").Append(IsStarted).Append(" suspended=").Append(IsSuspended)
              .Append(" phase=").Append(Phase).Append(" focused=").Append(FocusedKey?.Value ?? "-")
              .Append(" windows=").Append(_windows.Count).Append(" away=").Append(_away.Count)
              .Append(" frames=").Append(FramesRunning);
            return sb.ToString();
        }
    }

    // ---------- internal extension points ----------

    /// <summary>Incremented by ReleaseAll, Suspend and re-land; pending frame-write
    /// sequences compare it and abandon themselves when it changed.</summary>
    internal int PlacementGeneration { get; private set; }

    /// <summary>Number of re-frames requested.</summary>
    internal int ReframeCount { get; private set; }

    /// <summary>Time base of the last tick.</summary>
    internal double LastTick { get; private set; }

    /// <summary>dt computed by the most recent Tick; 0 before any tick.</summary>
    internal double LastTickDelta { get; private set; }

    /// <summary>Stand-in for the motion conditions of NeedsFrames.</summary>
    internal bool MotionActive { get; set; }

    /// <summary>Phase setter for later features and tests. No render, no callback.</summary>
    /// <param name="phase">The new phase.</param>
    internal void SetPhase(StagePhase phase) => Phase = phase;

    /// <summary>Sets/clears the parked flag of a managed window; unknown key: nothing.</summary>
    /// <param name="key">The window.</param>
    /// <param name="parked">The new flag.</param>
    internal void SetParked(WindowKey key, bool parked)
    {
        int i = IndexOf(key);
        if (i >= 0) _windows[i] = _windows[i] with { Parked = parked };
    }

    /// <summary>Sets a managed record's RememberedSpot; unknown key: nothing.</summary>
    /// <param name="key">The window.</param>
    /// <param name="spot">The spot.</param>
    internal void SetRememberedSpot(WindowKey key, DockSpot? spot)
    {
        int i = IndexOf(key);
        if (i >= 0) _windows[i] = _windows[i] with { RememberedSpot = spot };
    }

    /// <summary>Turn the frames-running flag on/off (edge-triggered).</summary>
    /// <param name="on">The new flag.</param>
    internal void SetFramesRunning(bool on)
    {
        if (on == FramesRunning) return;
        if (on)
        {
            LastTick = Services.Clock.Now;
            FramesRunning = true;
            Enqueue(() => _onNeedsFrames?.Invoke(true));
        }
        else
        {
            FramesRunning = false;
            Enqueue(() => _onNeedsFrames?.Invoke(false));
        }

        if (!_inCall) Deliver();
    }

    // ---------- lifecycle ----------

    /// <summary>Adopts the given windows, loads the saved record and renders. Does nothing if already started.</summary>
    /// <param name="windows">The current windows.</param>
    /// <exception cref="ArgumentNullException">The list or an element is null.</exception>
    public void Start(IReadOnlyList<WindowInfo> windows) => Run(() =>
    {
        ValidateList(windows);
        if (IsStarted) return;
        foreach (var w in windows) Register(w);
        _saved = Services.Displays.Load(Key) ?? new SavedDisplay(LayoutDocument.Empty);
        Document = _saved.Document;
        FramedNodeId = _saved.FramedNodeId;
        IsStarted = true;
        Render();
        Save0();
    });

    /// <summary>Freezes the engine: no window events are processed until Resume.</summary>
    public void Suspend() => Run(() =>
    {
        if (IsSuspended) return;
        PlacementGeneration++;
        MotionActive = false;
        Phase = StagePhase.Desk;
        IsSuspended = true;
        Save0();
        Render();
        SetFramesRunning(false);
    });

    /// <summary>Unfreezes the engine and reconciles with the given current windows.</summary>
    /// <param name="windows">The current windows.</param>
    /// <exception cref="ArgumentNullException">The list or an element is null.</exception>
    public void Resume(IReadOnlyList<WindowInfo> windows) => Run(() =>
    {
        ValidateList(windows);
        if (!IsSuspended) return;
        IsSuspended = false;
        var present = new HashSet<WindowKey>(windows.Select(w => w.Key));
        foreach (var m in _windows.ToArray())
        {
            if (!present.Contains(m.Info.Key) && !m.Parked) RemoveCore(m.Info.Key, false);
        }

        foreach (var w in windows) AddCore(w);
        Reframe();
    });

    /// <summary>Hands every managed window back to the OS and forgets all state.</summary>
    /// <param name="into">Area to release windows into; null = VisibleArea.</param>
    /// <param name="completion">Runs once after every release write has reported back.</param>
    /// <exception cref="ArgumentOutOfRangeException">into is invalid.</exception>
    public void ReleaseAll(Rect? into, Action? completion = null) => Run(() =>
    {
        if (into is { } r) ValidateRect(r, nameof(into));
        PlacementGeneration++;
        Rect area = into ?? VisibleArea;

        // SPEC-ASSUMPTION A2: selected = parked OR visible fraction < 0.5 (strict).
        var selected = _windows
            .Where(m => m.Parked || Services.Windows.VisibleFraction(m.Info.Frame) < 0.5)
            .ToList();
        int remaining = selected.Count;
        for (int i = 0; i < selected.Count; i++)
        {
            var f = selected[i].Info.Frame;
            var target = ReleaseGeometry.Cascade(area, new Size(f.Width, f.Height), i);
            Services.Windows.SetFrame(selected[i].Info.Key, target, _ =>
            {
                if (--remaining == 0) completion?.Invoke();
            });
        }

        foreach (var m in _windows) Services.Capture.Stop(m.Info.Key);
        _windows.Clear();
        _writer.Clear();
        // SPEC-ASSUMPTION A18: ReleaseAll clears the away table.
        _away.Clear();
        _lastFocused.Clear();
        FocusedKey = null;
        IsStarted = false;
        Phase = StagePhase.Desk;
        MotionActive = false;
        Services.Renderer.Render(RenderPlan.Empty);
        SetFramesRunning(false);

        // SPEC-ASSUMPTION A4: with nothing to move, completion is scheduled via Clock.After(0).
        if (selected.Count == 0 && completion is not null) Services.Clock.After(0, completion);
    });

    /// <summary>Hands one window back to the OS and stops managing it, even while suspended.</summary>
    /// <param name="key">The window.</param>
    public void Release(WindowKey key) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(key);
        int i = IndexOf(key);
        if (i < 0) return;
        if (_windows[i].Parked)
        {
            WriteBackOnScreen(_windows[i]);
            _windows[i] = _windows[i] with { Parked = false };
        }

        // SPEC-ASSUMPTION A7: Release ignores the suspended guard.
        RemoveCore(key, false);
    });

    /// <summary>Persists the saved record. Does nothing until Start has run.</summary>
    public void Save() => Run(Save0);

    // ---------- window events ----------

    /// <summary>A window appeared or its info changed.</summary>
    /// <param name="info">The window info.</param>
    /// <exception cref="ArgumentNullException">info is null.</exception>
    public void WindowAdded(WindowInfo info) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(info);
        AddCore(info);
    });

    /// <summary>A window went away.</summary>
    /// <param name="key">The window.</param>
    /// <param name="temporary">True when the window is expected back (for example minimised).</param>
    public void WindowRemoved(WindowKey key, bool temporary) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IsSuspended) return;
        RemoveCore(key, temporary);
    });

    /// <summary>A window took focus.</summary>
    /// <param name="key">The window.</param>
    public void WindowFocused(WindowKey key) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(key);
        int i = IndexOf(key);
        if (IsSuspended || i < 0) return;
        FocusedKey = key;
        _lastFocused[_windows[i].Info.ProcessId] = key;
        Render();
    });

    /// <summary>A window's frame changed.</summary>
    /// <param name="key">The window.</param>
    /// <param name="frame">The new frame.</param>
    public void WindowMoved(WindowKey key, Rect frame) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(key);
        int i = IndexOf(key);
        if (IsSuspended || i < 0) return;
        _windows[i] = _windows[i] with { Info = _windows[i].Info with { Frame = frame } };
    });

    /// <summary>A window's title changed.</summary>
    /// <param name="key">The window.</param>
    /// <param name="title">The new title.</param>
    /// <exception cref="ArgumentNullException">title is null.</exception>
    public void WindowTitleChanged(WindowKey key, string title) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(title);
        int i = IndexOf(key);
        if (i < 0) return;
        _windows[i] = _windows[i] with { Info = _windows[i].Info with { Title = title } };
        if (Phase != StagePhase.Desk && !IsSuspended) Render();
    });

    /// <summary>A window became eligible for tiling.</summary>
    /// <param name="key">The window.</param>
    public void WindowBecameDockable(WindowKey key) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(key);
        int i = IndexOf(key);
        if (i < 0) return;
        _windows[i] = _windows[i] with { Info = _windows[i].Info with { Dockable = true } };
    });

    /// <summary>A fresh capture picture arrived.</summary>
    /// <param name="key">The window.</param>
    public void CaptureArrived(WindowKey key) => Run(() =>
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IndexOf(key) < 0) return;
        if (Phase != StagePhase.Desk && !IsSuspended) Render();
    });

    /// <summary>The frontmost application changed.</summary>
    /// <param name="processId">Process id of the frontmost app.</param>
    /// <param name="ownsDesktop">Whether that app owns the desktop.</param>
    public void AppActivated(int processId, bool ownsDesktop) => Run(() =>
    {
        int? previous = ActiveProcessId;
        bool previousDesktop = DesktopOwnerActive;
        ActiveProcessId = processId;
        DesktopOwnerActive = ownsDesktop;
        if (IsSuspended) return;
        if (LastFocusedOf(processId) != LastFocusedOf(previous) || ownsDesktop != previousDesktop) Render();
    });

    /// <summary>The display's work area changed.</summary>
    /// <param name="visibleArea">The new work area.</param>
    /// <exception cref="ArgumentOutOfRangeException">visibleArea is invalid.</exception>
    public void VisibleAreaChanged(Rect visibleArea) => Run(() =>
    {
        ValidateRect(visibleArea, nameof(visibleArea));
        if (visibleArea == _visibleArea) return;
        _visibleArea = visibleArea;
        if (IsStarted && !IsSuspended) Reframe();
    });

    // ---------- frames ----------

    /// <summary>Advances time. now: absolute seconds on the same time base as IClock.Now (not a delta).</summary>
    /// <param name="now">Absolute seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">now is NaN or infinite.</exception>
    public void Tick(double now) => Run(() =>
    {
        if (!double.IsFinite(now)) throw new ArgumentOutOfRangeException(nameof(now));
        // SPEC-ASSUMPTION A14: Tick while suspended does nothing.
        if (IsSuspended) return;
        LastTickDelta = Math.Max(0, now - LastTick);
        LastTick = now;
        Render();
        if (FramesRunning && !NeedsFrames) SetFramesRunning(false);
    });

    /// <summary>Re-render (for example after a settings change).</summary>
    public void Redraw() => Run(Render);

    // ---------- internals ----------

    private int IndexOf(WindowKey key)
    {
        for (int i = 0; i < _windows.Count; i++)
        {
            if (_windows[i].Info.Key == key) return i;
        }

        return -1;
    }

    private WindowKey? LastFocusedOf(int? pid) =>
        pid is { } p && _lastFocused.TryGetValue(p, out var k) ? k : null;

    private static void ValidateList(IReadOnlyList<WindowInfo> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        foreach (var w in windows) ArgumentNullException.ThrowIfNull(w, nameof(windows));
    }

    // SPEC-ASSUMPTION A20: bad rects throw rather than being ignored.
    private static void ValidateRect(Rect r, string name)
    {
        if (!double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.Width)
            || !double.IsFinite(r.Height) || r.Width < 0 || r.Height < 0)
        {
            throw new ArgumentOutOfRangeException(name);
        }
    }

    private void Register(WindowInfo info)
    {
        int i = IndexOf(info.Key);
        if (i >= 0)
        {
            _windows[i] = _windows[i] with { Info = info };
        }
        else
        {
            _windows.Add(new ManagedWindow(info, false, null));
            _writer[info.Key] = new WriterRecord();
            // SPEC-ASSUMPTION A17: capture starts when a window is first registered.
            Services.Capture.Start(info.Key);
        }

        if (_away.Remove(info.Key, out var spot))
        {
            int j = IndexOf(info.Key);
            _windows[j] = _windows[j] with { RememberedSpot = spot };
        }
    }

    private void AddCore(WindowInfo info)
    {
        if (IsSuspended) return;
        Register(info);
        if (!IsStarted) return;
        Save0();
        Render();
    }

    private void RemoveCore(WindowKey key, bool temporary)
    {
        int i = IndexOf(key);
        if (i < 0) return;
        var record = _windows[i];
        if (IsStarted && temporary) _away[key] = record.RememberedSpot;
        _windows.RemoveAt(i);
        _writer.Remove(key);
        if (record.Parked) WriteBackOnScreen(record);
        Services.Capture.Stop(key);
        if (FocusedKey == key) FocusedKey = null;
        foreach (var pid in _lastFocused.Where(kv => kv.Value == key).Select(kv => kv.Key).ToList())
        {
            _lastFocused.Remove(pid);
        }

        if (!IsStarted) return;
        Save0();
        Render();
    }

    private void WriteBackOnScreen(ManagedWindow record)
    {
        var f = record.Info.Frame;
        Services.Windows.SetFrame(record.Info.Key,
            ReleaseGeometry.Centre(VisibleArea, new Size(f.Width, f.Height)), _ => { });
    }

    private void Save0()
    {
        // SPEC-ASSUMPTION A11: Save is a no-op until Start has loaded the record.
        if (_saved is not null) Services.Displays.Save(Key, _saved);
    }

    private void Reframe()
    {
        ReframeCount++;
        // SPEC-ASSUMPTION A10: re-frame now presents (direct at rest, else re-land).
        if (IsStarted && !IsSuspended) PresentLayout();
        else Render();
    }

    private void Render()
    {
        // SPEC-ASSUMPTION A15: shell plan lists managed windows at last known frames with focus flag.
        Services.Renderer.Render(new RenderPlan(
            _windows.Select(w => new WindowPlan(w.Info.Key, w.Info.Frame, w.Info.Key == FocusedKey))));
    }

    // ---------- callback delivery and re-entry guard ----------

    private void EnsureNotInCallback()
    {
        if (_delivering)
        {
            throw new InvalidOperationException(
                "A mutating member was called on this engine from inside one of its callbacks.");
        }
    }

    private void Enqueue(Action action) => _queue.Enqueue(action);

    private void Deliver()
    {
        if (_delivering) return;
        _delivering = true;
        try
        {
            while (_queue.Count > 0) _queue.Dequeue()();
        }
        finally
        {
            _queue.Clear();
            _delivering = false;
        }
    }

    private (bool, bool, StagePhase, WindowKey?, EnginePolicy, HashSet<WindowKey>) Snapshot() =>
        (IsStarted, IsSuspended, Phase, FocusedKey, _policy,
         new HashSet<WindowKey>(_windows.Select(w => w.Info.Key)));

    // SPEC-ASSUMPTION A13: OnChange trigger set is started, suspended, phase, focus, policy, key set.
    private void Run(Action body)
    {
        EnsureNotInCallback();
        // Nested internal calls do not open a second wrapper.
        if (_inCall)
        {
            body();
            return;
        }

        _changeRequested = false;
        var before = Snapshot();
        _inCall = true;
        try
        {
            body();
            var after = Snapshot();
            if (before.Item1 != after.Item1 || before.Item2 != after.Item2 || before.Item3 != after.Item3
                || before.Item4 != after.Item4 || before.Item5 != after.Item5
                || !before.Item6.SetEquals(after.Item6) || _changeRequested)
            {
                Enqueue(() => _onChange?.Invoke());
            }
        }
        catch
        {
            _queue.Clear();
            throw;
        }
        finally
        {
            _inCall = false;
            _changeRequested = false;
        }

        Deliver();
    }
}
