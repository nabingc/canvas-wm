using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tests.Fakes;

/// <summary>Clock advanced only by hand.</summary>
public sealed class ManualClock : IClock
{
    private readonly List<(double Due, long Seq, Action Action)> _pending = new();
    private long _seq;

    /// <summary>Creates a clock starting at <paramref name="start"/>.</summary>
    public ManualClock(double start = 0.0)
    {
        Now = start;
    }

    /// <inheritdoc />
    public double Now { get; private set; }

    /// <summary>Number of pending actions (including never-due ones).</summary>
    public int PendingCount => _pending.Count;

    /// <inheritdoc />
    public void After(double seconds, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        // SPEC-ASSUMPTION A15: delay <= 0 or NaN is 0; +infinity never fires.
        double d = double.IsNaN(seconds) || seconds < 0 ? 0 : seconds;
        _pending.Add((Now + d, _seq++, action));
    }

    /// <summary>Advances time, running due actions in due-time then registration order.</summary>
    /// <exception cref="ArgumentOutOfRangeException">seconds is negative or NaN.</exception>
    public void Advance(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        double target = Now + seconds;
        while (true)
        {
            int best = -1;
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                if (p.Due > target) continue;
                if (best < 0 || p.Due < _pending[best].Due
                    || (p.Due == _pending[best].Due && p.Seq < _pending[best].Seq))
                {
                    best = i;
                }
            }

            if (best < 0) break;
            var next = _pending[best];
            _pending.RemoveAt(best);
            Now = Math.Max(Now, next.Due);
            next.Action();
        }

        Now = Math.Max(Now, target);
    }
}
