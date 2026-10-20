using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tests.Fakes;

/// <summary>Deterministic capture fake.</summary>
public sealed class FakeCapture : ICapture
{
    private sealed class State
    {
        public double? Interval;
        public bool Held;
        public int Recaptures;
        public CaptureFrame? Latest;
    }

    private readonly Dictionary<WindowKey, State> _states = new();
    private long _serial;

    /// <inheritdoc />
    public void Start(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_states.ContainsKey(key)) return;
        _states[key] = new State { Recaptures = 1 };
    }

    /// <inheritdoc />
    public void Stop(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _states.Remove(key);
    }

    /// <inheritdoc />
    public void SetRate(WindowKey key, double fps)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!double.IsFinite(fps) || fps <= 0) return;
        if (!_states.TryGetValue(key, out var s)) return;
        double interval = 1.0 / fps;
        if (s.Interval == interval) return;
        s.Interval = interval;
        s.Recaptures++;
    }

    /// <inheritdoc />
    public void Hold(WindowKey key, bool on)
    {
        ArgumentNullException.ThrowIfNull(key);
        // SPEC-ASSUMPTION A9: a change of the hold mark requests one fresh picture.
        if (!_states.TryGetValue(key, out var s) || s.Held == on) return;
        s.Held = on;
        s.Recaptures++;
    }

    /// <inheritdoc />
    public CaptureFrame? Latest(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _states.TryGetValue(key, out var s) ? s.Latest : null;
    }

    /// <summary>True if the window is started.</summary>
    public bool IsStarted(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _states.ContainsKey(key);
    }

    /// <summary>1/fps, or null if no rate or not started.</summary>
    public double? Interval(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _states.TryGetValue(key, out var s) ? s.Interval : null;
    }

    /// <summary>True if the hold mark is set.</summary>
    public bool IsHeld(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _states.TryGetValue(key, out var s) && s.Held;
    }

    /// <summary>Recapture requests since the window was last started.</summary>
    public int RecaptureRequests(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _states.TryGetValue(key, out var s) ? s.Recaptures : 0;
    }

    /// <summary>Simulates an arriving picture; null if the window is not started.</summary>
    public CaptureFrame? Deliver(WindowKey key, Size size)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_states.TryGetValue(key, out var s)) return null;
        var frame = new CaptureFrame(size, ++_serial);
        s.Latest = frame;
        return frame;
    }
}
