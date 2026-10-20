namespace CanvasWm.Engine.Services;

/// <summary>Monotonic time and delayed actions.</summary>
public interface IClock
{
    /// <summary>Seconds since an arbitrary epoch; never decreases.</summary>
    double Now { get; }

    /// <summary>Run action once on the engine thread after the given delay. No cancellation.</summary>
    /// <param name="seconds">Delay in seconds.</param>
    /// <param name="action">The action.</param>
    void After(double seconds, Action action);
}
