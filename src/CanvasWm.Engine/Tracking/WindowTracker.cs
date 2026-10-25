using System.Globalization;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tracking;

/// <summary>Tracks the manageable OS windows and reports changes to the target. Single-threaded
/// (engine thread). Not reusable after Dispose.</summary>
public sealed class WindowTracker : IDisposable
{
    private sealed class Record
    {
        public Record(WindowInfo info, int threadId, ulong handle)
        {
            Info = info;
            ThreadId = threadId;
            Handle = handle;
        }

        public ulong Handle { get; }

        public WindowInfo Info { get; set; }

        public int ThreadId { get; }
    }

    private readonly IWindowSource _source;
    private readonly IWindowSystem _windows;
    private readonly IClock _clock;
    private readonly IWindowTrackerTarget _target;
    private readonly List<Record> _records = new();
    private readonly Queue<Action> _work = new();
    private readonly HashSet<(int Pid, int Slot)> _pending = new();
    private readonly Dictionary<int, ulong> _lastReported = new();
    private ClassifierPolicy _policy;
    private IDisposable? _subscription;
    private bool _draining;
    private bool _disposed;

    /// <summary>"w" + handle in decimal (invariant culture), e.g. 4211 gives "w4211".</summary>
    /// <param name="handle">The OS window handle.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentOutOfRangeException">handle is 0.</exception>
    public static WindowKey KeyFor(ulong handle)
    {
        // SPEC-ASSUMPTION A13: the window key is "w" + decimal handle.
        if (handle == 0) throw new ArgumentOutOfRangeException(nameof(handle), handle, "Handle must not be 0.");
        return new WindowKey("w" + handle.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Creates an idle tracker. Makes no call on any dependency.</summary>
    /// <param name="source">The OS window source.</param>
    /// <param name="windows">Used only for VisibleFraction in the admission test.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="target">Receives decisions.</param>
    /// <param name="policy">The classifier policy.</param>
    /// <param name="options">null = TrackerOptions.Default.</param>
    public WindowTracker(IWindowSource source, IWindowSystem windows, IClock clock,
                         IWindowTrackerTarget target, ClassifierPolicy policy,
                         TrackerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(policy);
        _source = source;
        _windows = windows;
        _clock = clock;
        _target = target;
        _policy = policy;
        Options = options ?? TrackerOptions.Default;
    }

    /// <summary>The current classifier policy.</summary>
    public ClassifierPolicy Policy => _policy;

    /// <summary>The tracker options.</summary>
    public TrackerOptions Options { get; }

    /// <summary>True after Start (until Dispose).</summary>
    public bool IsStarted { get; private set; }

    /// <summary>True once Ready has been raised.</summary>
    public bool IsReady { get; private set; }

    /// <summary>Snapshot of the tracked windows in admission order (current stored info).</summary>
    public IReadOnlyList<WindowInfo> Tracked => _records.Select(r => r.Info).ToList();

    /// <summary>Stored info for key, or null if not tracked.</summary>
    /// <param name="key">The window key.</param>
    /// <returns>The info or null.</returns>
    public WindowInfo? Find(WindowKey key) => FindRecord(key)?.Info;

    /// <summary>Identity of the tracked window with this key, or null if not tracked (B41).</summary>
    /// <param name="key">The window key.</param>
    /// <returns>The identity or null.</returns>
    /// <exception cref="ArgumentNullException">key is null.</exception>
    public TrackedIdentity? IdentityOf(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Record? r = FindRecord(key);
        return r is null ? null : new TrackedIdentity(r.Handle, r.Info.ProcessId, r.ThreadId);
    }

    /// <summary>Key of the tracked window with this handle, or null if not tracked or 0 (B42).</summary>
    /// <param name="handle">The OS handle.</param>
    /// <returns>The key or null.</returns>
    public WindowKey? KeyOf(ulong handle)
    {
        if (handle == 0) return null;
        foreach (Record r in _records)
        {
            if (r.Handle == handle) return KeyFor(handle);
        }

        return null;
    }

    /// <summary>Subscribe, run the initial full scan, raise Ready, arm the rescan timer.
    /// Second call: no-op.</summary>
    /// <exception cref="ObjectDisposedException">After Dispose.</exception>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsStarted) return;
        IsStarted = true;
        Enqueue(() =>
        {
            _subscription = _source.Subscribe(OnRaw);
            Scan(null);
            // SPEC-ASSUMPTION A8: Ready fires once, synchronously, at the end of the first scan; no deadline timer.
            IsReady = true;
            _target.Ready(Tracked);
            _clock.After(Options.RescanSeconds, OnRescanTimer);
        });
    }

    /// <summary>Run a full scan now (e.g. after a virtual-desktop switch). No-op before Start or
    /// after Dispose.</summary>
    public void Rescan()
    {
        if (!IsStarted || _disposed) return;
        Enqueue(() => Scan(null));
    }

    /// <summary>Replace the classifier policy; after Start, release windows of newly excluded
    /// apps and run a full scan.</summary>
    /// <param name="policy">The new policy.</param>
    public void SetPolicy(ClassifierPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (_disposed) return;
        if (!IsStarted)
        {
            _policy = policy;
            return;
        }

        Enqueue(() =>
        {
            ClassifierPolicy old = _policy;
            _policy = policy;
            // SPEC-ASSUMPTION A12: newly excluded apps get Release and no WindowRemoved.
            var released = _records
                .Where(r => r.Info.App is { } a
                            && !old.ExcludedApps.Contains(a.Value)
                            && policy.ExcludedApps.Contains(a.Value))
                .ToList();
            foreach (Record r in released)
            {
                _records.Remove(r);
                _target.Release(r.Info.Key);
            }

            Scan(null);
        });
    }

    /// <summary>Unsubscribe; later timer callbacks and events do nothing. Idempotent. Makes no
    /// target call.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscription?.Dispose();
        _subscription = null;
        _work.Clear();
    }

    // ---- work queue (B20) ----

    private void Enqueue(Action item)
    {
        if (_disposed) return;
        _work.Enqueue(item);
        if (_draining) return;
        _draining = true;
        try
        {
            while (!_disposed && _work.TryDequeue(out Action? next))
            {
                next();
            }
        }
        finally
        {
            _draining = false;
        }
    }

    private void OnRaw(RawWindowEvent e) => Enqueue(() => Handle(e));

    private void OnRescanTimer() => Enqueue(() =>
    {
        try
        {
            Scan(null);
        }
        finally
        {
            if (!_disposed) _clock.After(Options.RescanSeconds, OnRescanTimer);
        }
    });

    // ---- records ----

    private Record? FindRecord(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        foreach (Record r in _records)
        {
            if (r.Info.Key == key) return r;
        }

        return null;
    }

    private static bool Matches(Record r, OsWindowFacts c) =>
        c.ProcessId == r.Info.ProcessId && c.ThreadId == r.ThreadId;

    private void Admit(OsWindowFacts f, bool dockable)
    {
        string name = string.IsNullOrEmpty(f.AppName) ? Options.FallbackAppName : f.AppName;
        AppId? app = string.IsNullOrEmpty(f.AppId) ? null : new AppId(f.AppId);
        var info = new WindowInfo(KeyFor(f.Handle), f.ProcessId, name, app, f.Title, f.Frame!.Value, dockable);
        _records.Add(new Record(info, f.ThreadId, f.Handle));
        if (IsReady) _target.WindowAdded(info);
    }

    private void Remove(Record r, bool temporary)
    {
        _records.Remove(r);
        if (IsReady) _target.WindowRemoved(r.Info.Key, temporary);
    }

    private void ApplyFate(Record r, TrackedFate fate, OsWindowFacts c)
    {
        switch (fate)
        {
            case TrackedFate.Keep:
                RecheckDockable(r, c);
                break;
            case TrackedFate.RemoveTemporary:
                Remove(r, true);
                break;
            default:
                Remove(r, false);
                break;
        }
    }

    // B17: one-way upgrade.
    private void RecheckDockable(Record r, OsWindowFacts c)
    {
        if (r.Info.Dockable || !WindowClassifier.IsDockable(c)) return;
        r.Info = r.Info with { Dockable = true };
        if (IsReady) _target.WindowBecameDockable(r.Info.Key);
    }

    // ---- scans (B11, B12) ----

    private void Scan(int? pid)
    {
        IReadOnlyList<OsWindowFacts> all = _source.Enumerate();
        var listed = new List<OsWindowFacts>();
        foreach (OsWindowFacts f in all)
        {
            if (pid is null || f.ProcessId == pid.Value) listed.Add(f);
        }

        var seen = new HashSet<ulong>();
        foreach (OsWindowFacts f in listed)
        {
            if (f.Handle == 0) continue;
            // E4: first duplicate wins.
            if (!seen.Add(f.Handle)) continue;
            WindowKey k = KeyFor(f.Handle);
            Record? r = FindRecord(k);
            if (r is not null)
            {
                if (!Matches(r, f))
                {
                    Remove(r, false);
                    r = null;
                }
                else
                {
                    ApplyFate(r, WindowClassifier.Fate(f, _policy), f);
                    continue;
                }
            }

            WindowClassification cls = WindowClassifier.Classify(f, _policy);
            if (cls.IsManaged && PassesAdmission(f.Frame!.Value, k))
            {
                Admit(f, cls.Dockable);
            }
        }

        var missing = _records
            .Where(r => (pid is null || r.Info.ProcessId == pid.Value)
                        && !seen.Contains(HandleOf(r)))
            .ToList();
        foreach (Record r in missing)
        {
            if (!_records.Contains(r)) continue;
            OsWindowFacts? c = _source.Describe(HandleOf(r));
            if (c is null)
            {
                Remove(r, false);
            }
            else if (!Matches(r, c))
            {
                Remove(r, false);
            }
            else
            {
                ApplyFate(r, WindowClassifier.Fate(c, _policy), c);
            }
        }
    }

    private static ulong HandleOf(Record r) => r.Handle;

    private bool PassesAdmission(Rect r, WindowKey k)
    {
        // SPEC-ASSUMPTION A9: the centre-on-display test is half-open.
        double cx = r.X + r.Width / 2;
        double cy = r.Y + r.Height / 2;
        foreach (Rect d in _target.DisplayFrames)
        {
            if (d.X <= cx && cx < d.X + d.Width && d.Y <= cy && cy < d.Y + d.Height) return true;
        }

        if (_target.Holds(k)) return true;
        return _windows.VisibleFraction(r) < Options.OffScreenAdmitBelow;
    }

    // ---- events (B13-B16) ----

    private void Handle(RawWindowEvent e)
    {
        ulong h = e.Handle;
        switch (e.Kind)
        {
            case RawWindowEventKind.Foreground:
                if (h != 0) FocusReport(h, dedupe: false, foreground: true);
                return;
            case RawWindowEventKind.FocusChanged:
                if (h != 0) FocusReport(h, dedupe: true, foreground: false);
                return;
        }

        if (h == 0) return;
        WindowKey key = KeyFor(h);

        switch (e.Kind)
        {
            case RawWindowEventKind.Created:
            case RawWindowEventKind.Shown:
            case RawWindowEventKind.Uncloaked:
            case RawWindowEventKind.Restored:
                ScheduleRechecks(h);
                return;
        }

        Record? r = FindRecord(key);
        if (r is null) return;

        if (e.Kind == RawWindowEventKind.Destroyed)
        {
            Remove(r, false);
            return;
        }

        if (e.Kind == RawWindowEventKind.Minimized)
        {
            Remove(r, true);
            return;
        }

        OsWindowFacts? c = _source.Describe(h);
        // SPEC-ASSUMPTION A10: events about our own process's windows are ignored.
        if (c is { IsOwnProcess: true }) return;
        if (c is not null && !Matches(r, c))
        {
            Remove(r, false);
            return;
        }

        switch (e.Kind)
        {
            case RawWindowEventKind.Hidden:
            case RawWindowEventKind.Cloaked:
                TrackedFate fate = WindowClassifier.Fate(c, _policy);
                if (fate == TrackedFate.RemoveTemporary) Remove(r, true);
                else if (fate == TrackedFate.RemovePermanent) Remove(r, false);
                break;
            case RawWindowEventKind.LocationChanged:
                // A14: scans do not refresh frames; only move events do.
                if (c?.Frame is { } fr && IsFinite(fr) && fr != r.Info.Frame)
                {
                    r.Info = r.Info with { Frame = fr };
                    _target.WindowMoved(r.Info.Key, fr);
                }

                break;
            case RawWindowEventKind.TitleChanged:
                if (c is not null)
                {
                    r.Info = r.Info with { Title = c.Title };
                    _target.WindowTitleChanged(r.Info.Key, c.Title);
                }

                break;
        }
    }

    private static bool IsFinite(Rect r) =>
        double.IsFinite(r.X) && double.IsFinite(r.Y) && double.IsFinite(r.Width) && double.IsFinite(r.Height);

    private void ScheduleRechecks(ulong handle)
    {
        OsWindowFacts? c = _source.Describe(handle);
        if (c is null || c.IsOwnProcess) return;
        int pid = c.ProcessId;
        // SPEC-ASSUMPTION A2: at most one pending re-check per (process, slot).
        Arm(pid, 0, Options.FirstRecheckSeconds);
        Arm(pid, 1, Options.SecondRecheckSeconds);
    }

    private void Arm(int pid, int slot, double delay)
    {
        if (!_pending.Add((pid, slot))) return;
        _clock.After(delay, () =>
        {
            _pending.Remove((pid, slot));
            Enqueue(() => Scan(pid));
        });
    }

    private void FocusReport(ulong h, bool dedupe, bool foreground)
    {
        OsWindowFacts? c = _source.Describe(h);
        if (c is null || c.IsOwnProcess) return;
        int pid = c.ProcessId;
        Record? r = FindRecord(KeyFor(h));
        if (r is not null && !Matches(r, c))
        {
            Remove(r, false);
            r = null;
        }

        if (r is not null) RecheckDockable(r, c);

        if (!(dedupe && _lastReported.TryGetValue(pid, out ulong last) && last == h))
        {
            _lastReported[pid] = h;
            if (r is not null) _target.WindowFocused(r.Info.Key);
            else _target.FocusLeftWindows(pid);
        }

        // SPEC-ASSUMPTION A7: focus call first, then AppActivated; FocusChanged never activates.
        if (foreground) _target.AppActivated(pid, c.OwnsDesktop);
    }
}
