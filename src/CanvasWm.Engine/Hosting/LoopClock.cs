using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Hosting;

/// <summary>The real IClock (B30-B35): time from a monotonic source, delayed actions run
/// when the host's wake fires. Engine-thread only (Now may be read from any thread).</summary>
public sealed class LoopClock : IClock
{
    private sealed record Pending(double Due, long Seq, Action Action);

    private readonly Func<double> _source;
    private readonly Action<double> _scheduleWake;
    private readonly object _timeLock = new();
    private readonly List<Pending> _pending = [];
    private double _last = double.NegativeInfinity;
    private long _counter;
    private double? _requestedWake;
    private bool _inAfter;

    /// <summary>Creates the clock.</summary>
    /// <param name="now">Monotonic seconds (the App passes a high-resolution counter reading).</param>
    /// <param name="scheduleWake">Asks the host to call RunDue on the engine thread after the
    /// given delay in seconds (finite, &gt;= 0). Each request replaces the previous one. It
    /// must never call RunDue synchronously.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public LoopClock(Func<double> now, Action<double> scheduleWake)
    {
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(scheduleWake);
        _source = now;
        _scheduleWake = scheduleWake;
    }

    /// <inheritdoc />
    public double Now
    {
        get
        {
            double v = _source();
            lock (_timeLock)
            {
                if (double.IsFinite(v) && v > _last) _last = v;
                return double.IsNegativeInfinity(_last) ? 0 : _last;
            }
        }
    }

    /// <summary>Number of actions not yet run, including never-due ones.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Earliest finite due time, or null.</summary>
    public double? NextDue
    {
        get
        {
            double? best = null;
            foreach (Pending p in _pending)
            {
                if (double.IsFinite(p.Due) && (best is null || p.Due < best)) best = p.Due;
            }

            return best;
        }
    }

    /// <inheritdoc />
    public void After(double seconds, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        double d = double.IsNaN(seconds) || seconds <= 0 ? 0 : seconds;
        double now = Now;
        double due = now + d;
        _pending.Add(new Pending(due, ++_counter, action));
        if (double.IsFinite(due) && (_requestedWake is null || due < _requestedWake))
        {
            Request(due, now);
        }
    }

    /// <summary>Runs the actions that are due (B33). Called by the host when the wake fires.</summary>
    /// <exception cref="InvalidOperationException">called while After is running (B35).</exception>
    public void RunDue()
    {
        if (_inAfter)
        {
            throw new InvalidOperationException("RunDue must not run inside After; the wake must be asynchronous.");
        }

        _requestedWake = null;
        double t = Now;
        long s = _counter;
        try
        {
            while (true)
            {
                Pending? next = null;
                foreach (Pending p in _pending)
                {
                    if (p.Due <= t && p.Seq <= s
                        && (next is null || p.Due < next.Due || (p.Due == next.Due && p.Seq < next.Seq)))
                    {
                        next = p;
                    }
                }

                if (next is null) break;
                _pending.Remove(next);
                next.Action();
            }
        }
        finally
        {
            if (NextDue is double nd && (_requestedWake is null || nd < _requestedWake))
            {
                Request(nd, Now);
            }
        }
    }

    private void Request(double due, double now)
    {
        _requestedWake = due;
        _inAfter = true;
        try
        {
            _scheduleWake(Math.Max(0, due - now));
        }
        finally
        {
            _inAfter = false;
        }
    }
}
