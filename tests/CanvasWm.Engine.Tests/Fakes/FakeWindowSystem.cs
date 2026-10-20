using CanvasWm.Core.Geometry;
using CanvasWm.Core.Parking;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tests.Fakes;

/// <summary>One recorded frame write.</summary>
/// <param name="Key">The window.</param>
/// <param name="Target">The requested frame.</param>
/// <param name="BatchId">Batch id for SetFrames entries, null for SetFrame.</param>
public readonly record struct FrameWriteRecord(WindowKey Key, Rect Target, int? BatchId);

/// <summary>Deterministic in-memory window system with an explicit operation queue.</summary>
public sealed class FakeWindowSystem : IWindowSystem
{
    private sealed record Op(IReadOnlyList<WindowKey> Keys, Action Run);

    private readonly Dictionary<WindowKey, Rect> _frames = new();
    private readonly Dictionary<WindowKey, bool> _showing = new();
    private readonly Dictionary<WindowKey, (Size? Min, Size? Max)> _constraints = new();
    private readonly HashSet<WindowKey> _refuses = new();
    private readonly HashSet<WindowKey> _fails = new();
    private List<WindowKey> _osOrder = new();
    private readonly List<Op> _queue = new();
    private readonly List<FrameWriteRecord> _writes = new();
    private readonly List<WindowKey> _reads = new();
    private readonly List<WindowKey> _focus = new();
    private int _batchCounter;

    /// <summary>Display full frames; default one 1440x900 screen.</summary>
    public IReadOnlyList<Rect> Screens { get; set; } = new[] { new Rect(0, 0, 1440, 900) };

    /// <summary>When set, overrides <see cref="ParkingOrigin"/>.</summary>
    public Point? ParkingOriginOverride { get; set; }

    /// <inheritdoc />
    public Point Pointer { get; set; }

    /// <inheritdoc />
    public bool MouseDown { get; set; }

    /// <inheritdoc />
    // SPEC-ASSUMPTION A7: ParkingOrigin is ParkGeometry.DefaultFallbackOrigin of the screens.
    public Point ParkingOrigin => ParkingOriginOverride ?? ParkGeometry.DefaultFallbackOrigin(Screens);

    /// <summary>Recorded writes in call order.</summary>
    public IReadOnlyList<FrameWriteRecord> Writes => _writes;

    /// <summary>Recorded reads in call order.</summary>
    public IReadOnlyList<WindowKey> Reads => _reads;

    /// <summary>Recorded focus requests in call order.</summary>
    public IReadOnlyList<WindowKey> FocusRequests => _focus;

    /// <summary>Number of queued operations.</summary>
    public int PendingCount => _queue.Count;

    /// <summary>Adds a managed window as the frontmost.</summary>
    public void AddWindow(WindowKey key, Rect frame, bool showing = true)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_frames.ContainsKey(key)) throw new ArgumentException("Window already managed.", nameof(key));
        _frames[key] = frame;
        _showing[key] = showing;
        _osOrder.Remove(key);
        _osOrder.Insert(0, key);
    }

    /// <summary>Makes the key unmanaged; unknown keys are ignored.</summary>
    public void RemoveWindow(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_frames.Remove(key)) return;
        _showing.Remove(key);
        _constraints.Remove(key);
        _refuses.Remove(key);
        _fails.Remove(key);
        _osOrder.Remove(key);
    }

    /// <summary>Sets whether a managed window is on screen.</summary>
    public void SetShowing(WindowKey key, bool showing)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_frames.ContainsKey(key)) _showing[key] = showing;
    }

    /// <summary>Changes the frame directly, as if the user or app moved it.</summary>
    public void SetFrameDirect(WindowKey key, Rect frame)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_frames.ContainsKey(key)) _frames[key] = frame;
    }

    /// <summary>Sets per-window size bounds applied by writes.</summary>
    public void SetConstraints(WindowKey key, Size? min = null, Size? max = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        _constraints[key] = (min, max);
    }

    /// <summary>When true, writes leave the frame unchanged.</summary>
    public void SetRefusesWrites(WindowKey key, bool refuses)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (refuses) _refuses.Add(key); else _refuses.Remove(key);
    }

    /// <summary>When true, reads and writes complete with null.</summary>
    public void SetFails(WindowKey key, bool fails)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (fails) _fails.Add(key); else _fails.Remove(key);
    }

    /// <summary>Replaces the OS z-order (may include unmanaged keys).</summary>
    public void SetZOrder(IEnumerable<WindowKey> frontToBack)
    {
        ArgumentNullException.ThrowIfNull(frontToBack);
        var list = new List<WindowKey>();
        foreach (var k in frontToBack)
        {
            ArgumentNullException.ThrowIfNull(k, nameof(frontToBack));
            if (!list.Contains(k)) list.Add(k);
        }

        foreach (var k in _osOrder)
        {
            if (_frames.ContainsKey(k) && !list.Contains(k)) list.Add(k);
        }

        _osOrder = list;
    }

    /// <summary>Current frame, or null if unknown.</summary>
    public Rect? FrameOf(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _frames.TryGetValue(key, out var f) ? f : null;
    }

    /// <inheritdoc />
    public void Focus(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        // SPEC-ASSUMPTION A14: Focus only records; it does not reorder windows.
        _focus.Add(key);
    }

    /// <inheritdoc />
    public IReadOnlyList<WindowKey> FrontToBack() => _osOrder.Where(_frames.ContainsKey).ToList();

    /// <inheritdoc />
    public bool IsShowing(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _showing.TryGetValue(key, out var s) && s;
    }

    /// <inheritdoc />
    public double VisibleFraction(Rect rect) => ParkGeometry.VisibleFraction(rect, Screens);

    /// <inheritdoc />
    public WindowKey? TabSibling(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return null;
    }

    /// <inheritdoc />
    public void ReadFrame(WindowKey key, Action<Rect?> completion)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(completion);
        _reads.Add(key);
        _queue.Add(new Op(new[] { key }, () => completion(ApplyRead(key))));
    }

    /// <inheritdoc />
    public void SetFrame(WindowKey key, Rect target, Action<Rect?> completion)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(completion);
        _writes.Add(new FrameWriteRecord(key, target, null));
        _queue.Add(new Op(new[] { key }, () => completion(ApplyWrite(key, target))));
    }

    /// <inheritdoc />
    public void SetFrames(IReadOnlyList<FrameWrite> writes, Action<IReadOnlyList<FrameWriteResult>> completion)
    {
        ArgumentNullException.ThrowIfNull(writes);
        ArgumentNullException.ThrowIfNull(completion);
        var seen = new HashSet<WindowKey>();
        foreach (var w in writes)
        {
            if (w.Key is null) throw new ArgumentException("Entry has a null key.", nameof(writes));
            if (!seen.Add(w.Key)) throw new ArgumentException("Duplicate key in batch.", nameof(writes));
        }

        var copy = writes.ToArray();
        int batch = ++_batchCounter;
        foreach (var w in copy) _writes.Add(new FrameWriteRecord(w.Key, w.Target, batch));
        _queue.Add(new Op(copy.Select(w => w.Key).ToArray(), () =>
        {
            var results = new FrameWriteResult[copy.Length];
            for (int i = 0; i < copy.Length; i++)
            {
                results[i] = new FrameWriteResult(copy[i].Key, ApplyWrite(copy[i].Key, copy[i].Target));
            }

            completion(results);
        }));
    }

    /// <summary>Runs the oldest pending operation; false if none.</summary>
    public bool Step()
    {
        if (_queue.Count == 0) return false;
        var op = _queue[0];
        _queue.RemoveAt(0);
        op.Run();
        return true;
    }

    /// <summary>Runs the oldest pending operation involving key; false if none.</summary>
    public bool Step(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        int i = _queue.FindIndex(o => o.Keys.Contains(key));
        if (i < 0) return false;
        var op = _queue[i];
        _queue.RemoveAt(i);
        op.Run();
        return true;
    }

    /// <summary>Runs operations until the queue is empty and returns how many ran.</summary>
    /// <exception cref="InvalidOperationException">More than 10000 operations would run.</exception>
    public int Pump()
    {
        int n = 0;
        while (_queue.Count > 0)
        {
            if (n >= 10000) throw new InvalidOperationException("Pump exceeded 10000 operations.");
            Step();
            n++;
        }

        return n;
    }

    private Rect? ApplyRead(WindowKey key)
    {
        if (!_frames.TryGetValue(key, out var f) || _fails.Contains(key)) return null;
        return f;
    }

    private Rect? ApplyWrite(WindowKey key, Rect t)
    {
        if (!_frames.TryGetValue(key, out var cur) || _fails.Contains(key)) return null;
        if (!double.IsFinite(t.X) || !double.IsFinite(t.Y) || !double.IsFinite(t.Width) || !double.IsFinite(t.Height))
        {
            return null;
        }

        if (_refuses.Contains(key)) return cur;
        double w = t.Width, h = t.Height;
        if (_constraints.TryGetValue(key, out var c))
        {
            if (c.Min is { } mn) { w = Math.Max(w, mn.Width); h = Math.Max(h, mn.Height); }
            if (c.Max is { } mx) { w = Math.Min(w, mx.Width); h = Math.Min(h, mx.Height); }
        }

        var next = new Rect(t.X, t.Y, w, h);
        _frames[key] = next;
        return next;
    }
}
