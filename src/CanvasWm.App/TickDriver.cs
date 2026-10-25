using System.Diagnostics;
using System.Globalization;
using CanvasWm.Engine.Hosting;
using CanvasWm.Engine.Routing;
using CanvasWm.Win32.Hosting;

namespace CanvasWm.App;

/// <summary>Tick thread over TickPacer, TickCoalescer, DwmComposition and HighResolutionTimer
/// (B67). SetRunning and Dispose touch only thread-safe signals; OnTick runs on the engine thread.</summary>
internal sealed class TickDriver : IDisposable
{
    private readonly EngineRouter _router;
    private readonly LoopClock _clock;
    private readonly Action<Action> _post;
    private readonly Action<string> _log;
    private readonly TickPacer _pacer;
    private readonly TickCoalescer _coalescer = new();
    private readonly ManualResetEventSlim _run = new(false);
    private Thread? _thread;
    private int _resetRequested;
    private volatile bool _stop;

    internal TickDriver(EngineRouter router, LoopClock clock, Action<Action> post, Action<string> log,
                        TickPacingOptions? options = null)
    {
        _router = router;
        _clock = clock;
        _post = post;
        _log = log;
        _pacer = new TickPacer(options);
    }

    /// <summary>Frames needed on or off (from router.FramesNeededChanged).</summary>
    internal void SetRunning(bool running)
    {
        if (_stop)
        {
            return;
        }

        if (running)
        {
            // SPEC-ASSUMPTION A10: the tick thread resets the pacer (flag), not the caller, because the pacer is single-threaded.
            Interlocked.Exchange(ref _resetRequested, 1);
            if (_thread is null)
            {
                _thread = new Thread(Loop)
                {
                    IsBackground = true,
                    Name = "CanvasWm.Tick",
                    Priority = ThreadPriority.AboveNormal,
                };
                _thread.Start();
            }

            _run.Set();
        }
        else
        {
            _run.Reset();
        }
    }

    public void Dispose()
    {
        _stop = true;
        _run.Set();
    }

    private void OnTick()
    {
        _coalescer.Delivered();
        if (_router.FramesNeeded)
        {
            _router.Tick(_clock.Now);
        }
    }

    private void Loop()
    {
        HighResolutionTimer? timer = null;
        bool timerFailed = false;
        double? hz = null;
        bool inPeriod = false;
        long ticks = 0;
        long late = 0;
        long periodStart = 0;
        long lastStep = 0;
        double maxInterval = 0;
        double nominal = 0;
        try
        {
            while (!_stop)
            {
                _run.Wait();
                if (_stop)
                {
                    break;
                }

                try
                {
                    if (Interlocked.Exchange(ref _resetRequested, 0) == 1 || !inPeriod)
                    {
                        _pacer.Reset();
                        hz = DwmComposition.RefreshRate();
                        nominal = _pacer.TimerPeriodSeconds(hz);
                        inPeriod = true;
                        ticks = 0;
                        late = 0;
                        maxInterval = 0;
                        periodStart = Stopwatch.GetTimestamp();
                        lastStep = periodStart;
                    }

                    Step(ref timer, ref timerFailed, hz);
                    long now = Stopwatch.GetTimestamp();
                    double interval = Stopwatch.GetElapsedTime(lastStep, now).TotalSeconds;
                    lastStep = now;
                    ticks++;
                    maxInterval = Math.Max(maxInterval, interval);
                    if (interval > nominal * _pacer.Options.LateFrameFactor)
                    {
                        late++;
                    }

                    if (_run.IsSet && !_stop && _coalescer.TryBeginPost())
                    {
                        _post(OnTick);
                    }
                }
                catch (Exception e)
                {
                    _log("tick: " + e.Message);
                    _pacer.ReportFlush(false, 0);
                    Thread.Sleep(16);
                }

                if (!_run.IsSet && inPeriod)
                {
                    inPeriod = false;
                    double elapsed = Stopwatch.GetElapsedTime(periodStart).TotalSeconds;
                    _log(string.Create(CultureInfo.InvariantCulture,
                        $"frames off: {ticks} ticks in {elapsed:F3} s, max interval {maxInterval * 1000:F1} ms, late {late}, source {_pacer.Source}"));
                }
            }
        }
        finally
        {
            timer?.Dispose();
        }
    }

    private void Step(ref HighResolutionTimer? timer, ref bool timerFailed, double? hz)
    {
        if (_pacer.Source == TickSource.Composition)
        {
            long t0 = Stopwatch.GetTimestamp();
            bool ok = DwmComposition.Flush();
            _pacer.ReportFlush(ok, Stopwatch.GetElapsedTime(t0).TotalSeconds);
            return;
        }

        double period = _pacer.TimerPeriodSeconds(hz);
        if (timer is null && !timerFailed)
        {
            try
            {
                timer = new HighResolutionTimer();
            }
            catch (Exception e)
            {
                timerFailed = true;
                _log("tick: no waitable timer, sleeping instead: " + e.Message);
            }
        }

        if (timer is null || !timer.Wait(period))
        {
            Thread.Sleep((int)Math.Ceiling(period * 1000.0));
        }
    }
}
