using WinFormsTimer = System.Windows.Forms.Timer;

namespace CanvasWm.App;

/// <summary>Serves LoopClock's scheduleWake (B34): a delay of 0 is a host post, a longer delay is
/// one WinForms timer that is restarted on each request. UI thread only.</summary>
internal sealed class UiWake : IDisposable
{
    private readonly Action<Action> _post;
    private readonly WinFormsTimer _timer = new();
    private Action _runDue = static () => { };
    private bool _postPending;
    private bool _disposed;

    internal UiWake(Action<Action> post)
    {
        _post = post;
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            _runDue();
        };
    }

    /// <summary>Connects the clock's RunDue (the clock needs this object's Schedule first).</summary>
    internal void Bind(Action runDue) => _runDue = runDue;

    /// <summary>The scheduleWake delegate: replaces any earlier arming.</summary>
    internal void Schedule(double delaySeconds)
    {
        if (_disposed)
        {
            return;
        }

        if (delaySeconds <= 0)
        {
            _timer.Stop();
            if (!_postPending)
            {
                _postPending = true;
                _post(() =>
                {
                    _postPending = false;
                    if (!_disposed)
                    {
                        _runDue();
                    }
                });
            }

            return;
        }

        // SPEC-ASSUMPTION A2: the WinForms timer has about 16 ms granularity.
        double ms = Math.Ceiling(delaySeconds * 1000.0);
        _timer.Interval = (int)Math.Clamp(ms, 1.0, int.MaxValue);
        _timer.Stop();
        _timer.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
    }
}
