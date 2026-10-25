using CanvasWm.Core.Geometry;
using CanvasWm.Core.Parking;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;

namespace CanvasWm.Win32.Windows;

/// <summary>IWindowSystem over user32/dwmapi (B1-B20). Construct on the engine thread.
/// Frame reads, writes and focus run on one private writer thread, in issue order;
/// completions come back through post. Queries run synchronously on the calling (engine)
/// thread and send no window message.</summary>
public sealed class Win32WindowSystem : IWindowSystem, IDisposable
{
    private const string WriterThreadName = "CanvasWm frame writer";

    private readonly Action<Action> _post;
    private readonly IManagedWindowMap _windows;
    private readonly Action<string>? _log;
    private readonly INativeWindowOps _ops;
    private readonly FrameWriteRunner _runner;
    private readonly WriterQueue? _queue;
    private readonly Action<Action>? _runOnWriter;
    private readonly object _gate = new();
    private readonly HashSet<Item> _pending = new();
    private bool _disposed;
    private int _focusState; // 0 = null, 1 = true, 2 = false
    private Point _lastPointer = new(0, 0);

    /// <summary>Creates the window system and starts the writer thread.</summary>
    /// <param name="post">Schedules an action to run LATER on the engine thread, FIFO, never
    /// inline. Must be callable from any thread (the writer thread calls it).</param>
    /// <param name="windows">The managed-window map.</param>
    /// <param name="log">Optional diagnostics sink (focus failures, completion exceptions).
    /// Called on the engine thread.</param>
    public Win32WindowSystem(Action<Action> post, IManagedWindowMap windows, Action<string>? log = null)
        : this(post, windows, log, new NativeWindowOps(), null)
    {
    }

    internal Win32WindowSystem(Action<Action> post, IManagedWindowMap windows, Action<string>? log,
                               INativeWindowOps ops, Action<Action>? runOnWriter = null)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(ops);
        _post = post;
        _windows = windows;
        _log = log;
        _ops = ops;
        _runner = new FrameWriteRunner(ops);
        _runOnWriter = runOnWriter;
        if (runOnWriter is null)
        {
            _queue = new WriterQueue(WriterThreadName);
        }
    }

    /// <summary>Raised (posted, on the engine thread) once per Focus call on a managed key,
    /// before or while the window is raised. For overlays; the Engine does not use it.</summary>
    public event Action<WindowKey>? Raising;

    /// <summary>Outcome of the most recent focus attempt on the writer: null when not attempted.</summary>
    internal bool? LastFocusSucceeded => Volatile.Read(ref _focusState) switch
    {
        1 => true,
        2 => false,
        _ => null,
    };

    /// <summary>Raises the window's app and focuses it (SetForegroundWindow only, B17).</summary>
    /// <param name="key">The window.</param>
    public void Focus(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IsDisposed || _windows.Resolve(key) is not { } id)
        {
            return;
        }

        _post(() =>
        {
            try
            {
                Raising?.Invoke(key);
            }
            catch (Exception ex)
            {
                SafeLog($"Raising handler failed: {ex.GetType().Name}");
            }
        });

        Enqueue(
            () =>
            {
                bool? ok = _runner.Focus(id, () => PostLog($"focus skipped (hung): {key}"));
                Volatile.Write(ref _focusState, ok switch { true => 1, false => 2, _ => 0 });
                if (ok == false)
                {
                    PostLog($"focus refused: {key}");
                }
            },
            () => { });
    }

    /// <summary>Managed windows, frontmost first (B18). A new list each call.</summary>
    /// <returns>The keys.</returns>
    public IReadOnlyList<WindowKey> FrontToBack()
    {
        var result = new List<WindowKey>();
        foreach (nint h in _ops.TopLevelWindows())
        {
            if (_windows.KeyOf(HandleConversion.ToHandle(h)) is { } k)
            {
                result.Add(k);
            }
        }

        return result;
    }

    /// <summary>True when the window is live, visible, not minimised and not cloaked (B18).</summary>
    /// <param name="key">The window.</param>
    /// <returns>Whether it is showing.</returns>
    public bool IsShowing(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_windows.Resolve(key) is not { } id)
        {
            return false;
        }

        nint h = HandleConversion.ToHwnd(id.Handle);
        // SPEC-ASSUMPTION A9: no cache; computed per call.
        return _ops.IsLive(h, id.ProcessId, id.ThreadId) && _ops.IsVisible(h)
            && !_ops.IsMinimized(h) && !_ops.IsCloaked(h);
    }

    /// <summary>Fraction of rect on any display's full frame (B19).</summary>
    /// <param name="rect">The rect, in DIPs.</param>
    /// <returns>The visible fraction.</returns>
    public double VisibleFraction(Rect rect)
    {
        using var dpi = new DpiScope();
        return ParkGeometry.VisibleFraction(rect, DipFrames());
    }

    /// <summary>Reads the window's frame on the writer; completion(null) if unknown or failed (B16).</summary>
    /// <param name="key">The window.</param>
    /// <param name="completion">Receives the frame.</param>
    public void ReadFrame(WindowKey key, Action<Rect?> completion)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(completion);
        if (IsDisposed)
        {
            PostCompletion(() => completion(null));
            return;
        }

        ManagedWindowIdentity? id = _windows.Resolve(key);
        Enqueue(
            () =>
            {
                Rect? r = _runner.Read(id);
                PostCompletion(() => completion(r));
            },
            () => PostCompletion(() => completion(null)));
    }

    /// <summary>Writes one window (a batch of one) and completes with the frame read back (B5-B15).</summary>
    /// <param name="key">The window.</param>
    /// <param name="target">The requested visible frame, in DIPs.</param>
    /// <param name="completion">Receives the actual frame, or null.</param>
    public void SetFrame(WindowKey key, Rect target, Action<Rect?> completion)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(completion);
        if (IsDisposed)
        {
            PostCompletion(() => completion(null));
            return;
        }

        var request = new WriteRequest(key, _windows.Resolve(key), target);
        Enqueue(
            () =>
            {
                Rect? r = RunBatch(new[] { request })[0];
                PostCompletion(() => completion(r));
            },
            () => PostCompletion(() => completion(null)));
    }

    /// <summary>Writes several windows as one batch (B5-B15).</summary>
    /// <param name="writes">The writes.</param>
    /// <param name="completion">Receives one result per entry, in input order.</param>
    /// <exception cref="ArgumentException">An entry has a null Key, or two entries share a key.</exception>
    public void SetFrames(IReadOnlyList<FrameWrite> writes, Action<IReadOnlyList<FrameWriteResult>> completion)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(completion);
        var seen = new HashSet<WindowKey>();
        foreach (FrameWrite w in writes)
        {
            if (w.Key is null)
            {
                throw new ArgumentException("A write has a null key.", nameof(writes));
            }

            if (!seen.Add(w.Key))
            {
                throw new ArgumentException("Two writes share a key.", nameof(writes));
            }
        }

        var keys = new WindowKey[writes.Count];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = writes[i].Key;
        }

        if (IsDisposed)
        {
            PostCompletion(() => completion(Pack(keys, new Rect?[keys.Length])));
            return;
        }

        var requests = new WriteRequest[writes.Count];
        for (int i = 0; i < requests.Length; i++)
        {
            requests[i] = new WriteRequest(keys[i], _windows.Resolve(keys[i]), writes[i].Target);
        }

        Enqueue(
            () =>
            {
                IReadOnlyList<Rect?> results = RunBatch(requests);
                PostCompletion(() => completion(Pack(keys, results)));
            },
            () => PostCompletion(() => completion(Pack(keys, new Rect?[keys.Length]))));
    }

    /// <summary>Always null (B20).</summary>
    /// <param name="key">The window.</param>
    /// <returns>Null.</returns>
    public WindowKey? TabSibling(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return null;
    }

    /// <summary>Current pointer position in DIPs (B20).</summary>
    public Point Pointer
    {
        get
        {
            using var dpi = new DpiScope();
            if (_ops.CursorPos() is not { } c)
            {
                // SPEC-ASSUMPTION A16: a failed cursor read returns the last good value, or (0, 0).
                return _lastPointer;
            }

            nint hm = _ops.MonitorFromPoint(c.X, c.Y);
            Point p = new(c.X, c.Y);
            foreach (MonitorInfo m in _ops.Monitors())
            {
                if (m.Handle == hm)
                {
                    p = DipSpace.ToDip(c.X, c.Y, m.Bounds, m.Dpi);
                    break;
                }
            }

            _lastPointer = p;
            return p;
        }
    }

    /// <summary>True while the primary mouse button is held (B20).</summary>
    public bool MouseDown => _ops.AsyncKeyDown(FrameMath.PrimaryButtonKey(_ops.ButtonsSwapped()));

    /// <summary>Fallback origin for parked windows (B19).</summary>
    public Point ParkingOrigin
    {
        get
        {
            using var dpi = new DpiScope();
            return ParkGeometry.DefaultFallbackOrigin(DipFrames());
        }
    }

    /// <summary>Stops the writer (B4). Idempotent. Does not wait for a running item (A14).</summary>
    public void Dispose()
    {
        List<Item> dropped;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dropped = new List<Item>(_pending);
            _pending.Clear();
        }

        _queue?.Dispose();
        foreach (Item item in dropped)
        {
            item.Drop();
        }
    }

    private bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    private List<Rect> DipFrames()
    {
        IReadOnlyList<MonitorInfo> monitors = _ops.Monitors();
        var frames = new List<Rect>(monitors.Count);
        foreach (MonitorInfo m in monitors)
        {
            frames.Add(m.DipFrame);
        }

        return frames;
    }

    private IReadOnlyList<Rect?> RunBatch(IReadOnlyList<WriteRequest> batch)
    {
        try
        {
            return _runner.Run(batch);
        }
        catch (Exception)
        {
            return new Rect?[batch.Count];
        }
    }

    private static IReadOnlyList<FrameWriteResult> Pack(WindowKey[] keys, IReadOnlyList<Rect?> results)
    {
        var list = new List<FrameWriteResult>(keys.Length);
        for (int i = 0; i < keys.Length; i++)
        {
            list.Add(new FrameWriteResult(keys[i], i < results.Count ? results[i] : null));
        }

        return list;
    }

    /// <summary>One queued work item: runs at most once, or is dropped at most once.</summary>
    private sealed class Item
    {
        private readonly Action _run;
        private readonly Action _drop;
        private int _state; // 0 = queued, 1 = started, 2 = dropped

        internal Item(Action run, Action drop)
        {
            _run = run;
            _drop = drop;
        }

        internal bool TryStart() => Interlocked.CompareExchange(ref _state, 1, 0) == 0;

        internal void Run() => _run();

        internal void Drop()
        {
            if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            {
                _drop();
            }
        }
    }

    private void Enqueue(Action run, Action drop)
    {
        var item = new Item(run, drop);
        lock (_gate)
        {
            if (_disposed)
            {
                item.Drop();
                return;
            }

            _pending.Add(item);
        }

        Action work = () =>
        {
            if (!item.TryStart())
            {
                return;
            }

            lock (_gate)
            {
                _pending.Remove(item);
            }

            try
            {
                using var dpi = new DpiScope();
                item.Run();
            }
            catch (Exception ex)
            {
                PostLog($"writer item failed: {ex.GetType().Name}");
            }
        };

        if (_runOnWriter is not null)
        {
            _runOnWriter(work);
        }
        else if (_queue is not null && !_queue.TryEnqueue(work, item.Drop))
        {
            item.Drop();
        }
    }

    private void PostCompletion(Action invoke)
    {
        _post(() =>
        {
            try
            {
                invoke();
            }
            catch (Exception ex)
            {
                SafeLog($"completion threw {ex.GetType().Name}");
            }
        });
    }

    private void PostLog(string message)
    {
        if (_log is not null)
        {
            _post(() => SafeLog(message));
        }
    }

    private void SafeLog(string message)
    {
        try
        {
            _log?.Invoke(message);
        }
        catch (Exception)
        {
            // A faulty sink must not stop later work.
        }
    }
}
