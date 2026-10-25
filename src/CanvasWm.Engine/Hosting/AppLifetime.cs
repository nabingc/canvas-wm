using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Hosting;

/// <summary>Why the app is quitting.</summary>
public enum QuitReason
{
    /// <summary>The user chose Quit or closed the host.</summary>
    User = 0,
    /// <summary>The OS session is ending.</summary>
    EndSession = 1,
    /// <summary>A fault was reported.</summary>
    Fault = 2,
}

/// <summary>Process lifetime state.</summary>
public enum LifetimeState
{
    /// <summary>Normal operation.</summary>
    Running = 0,
    /// <summary>The quit sequence started.</summary>
    ShuttingDown = 1,
    /// <summary>finish was called.</summary>
    Finished = 2,
}

/// <summary>The quit and crash sequence (B42-B47). Engine-thread only, except where noted.</summary>
public sealed class AppLifetime
{
    /// <summary>Extra time after the router's shutdown timeout before the safety net finishes
    /// the quit anyway, s: 0.5.</summary>
    public const double GraceSeconds = 0.5;

    /// <summary>Faults logged in full; later ones are only counted: 20.</summary>
    public const int MaxLoggedFaults = 20;

    private readonly EngineRouter _router;
    private readonly HotkeyTriggers _triggers;
    private readonly IClock _clock;
    private readonly Action _stopTracking;
    private readonly Action<int> _finish;
    private readonly Action<string>? _log;
    private int _faultCount;
    private double _quitStart;

    /// <summary>Wires the lifetime to the router's ShutdownCompleted event.</summary>
    /// <param name="router">The router.</param>
    /// <param name="triggers">The hotkey triggers.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="stopTracking">Stops OS window events (the App disposes the tracker).</param>
    /// <param name="finish">Called exactly once with the exit code; the App releases OS
    /// resources and leaves its message loop.</param>
    /// <param name="log">One-line diagnostics; may be null.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public AppLifetime(EngineRouter router, HotkeyTriggers triggers, IClock clock,
                       Action stopTracking, Action<int> finish, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(stopTracking);
        ArgumentNullException.ThrowIfNull(finish);
        _router = router;
        _triggers = triggers;
        _clock = clock;
        _stopTracking = stopTracking;
        _finish = finish;
        _log = log;
        // B43: Finish is always deferred to a later clock run.
        router.ShutdownCompleted += () => _clock.After(0, Finish);
    }

    /// <summary>Current state.</summary>
    public LifetimeState State { get; private set; }

    /// <summary>Reason of the first quit request, or null.</summary>
    public QuitReason? Reason { get; private set; }

    /// <summary>True once any fault was reported (B46).</summary>
    public bool Faulted { get; private set; }

    /// <summary>Exit code passed to finish: 1 when Faulted, else 0. Meaningful once Finished.</summary>
    public int ExitCode { get; private set; }

    /// <summary>True when Finished, the router's shutdown is complete, or the lifetime's own
    /// deadline passed (B45). Safe to poll in a nested message loop.</summary>
    public bool IsQuitComplete =>
        State == LifetimeState.Finished
        || _router.IsShutdownComplete
        || (State == LifetimeState.ShuttingDown
            && _clock.Now >= _quitStart + _router.Options.ShutdownTimeoutSeconds + GraceSeconds);

    /// <summary>Raised once, right after finish returned (or threw).</summary>
    public event Action? Finished;

    /// <summary>Starts the quit sequence; a no-op unless Running (B42).</summary>
    /// <param name="reason">Why.</param>
    public void RequestQuit(QuitReason reason)
    {
        if (State != LifetimeState.Running) return;
        State = LifetimeState.ShuttingDown;
        Reason = reason;
        _quitStart = _clock.Now;
        Step("stop hotkeys", _triggers.Stop);
        Step("stop tracking", _stopTracking);
        Step("begin shutdown", _router.BeginShutdown);
        Step("arm safety net", () => _clock.After(_router.Options.ShutdownTimeoutSeconds + GraceSeconds, Finish));
    }

    /// <summary>Logs the error and quits with exit code 1 (B46).</summary>
    /// <param name="error">The fault.</param>
    /// <exception cref="ArgumentNullException">error is null.</exception>
    public void ReportFault(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (State != LifetimeState.Finished) Faulted = true;
        _faultCount++;
        if (_faultCount <= MaxLoggedFaults)
        {
            _log?.Invoke("fault: " + error);
        }

        if (State == LifetimeState.Running) RequestQuit(QuitReason.Fault);
    }

    private void Step(string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            ReportFault(new InvalidOperationException("quit step '" + name + "' failed", e));
        }
    }

    private void Finish()
    {
        if (State != LifetimeState.ShuttingDown) return;
        State = LifetimeState.Finished;
        ExitCode = Faulted ? 1 : 0;
        _log?.Invoke($"finish: reason {Reason}, exit code {ExitCode}, faults {_faultCount}");
        try
        {
            _finish(ExitCode);
        }
        catch (Exception e)
        {
            _log?.Invoke("finish failed: " + e);
        }

        Finished?.Invoke();
    }
}
