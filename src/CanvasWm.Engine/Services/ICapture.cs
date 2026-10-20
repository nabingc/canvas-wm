namespace CanvasWm.Engine.Services;

/// <summary>Live-picture capture. All members are called on the engine thread; none throws for unknown keys.</summary>
public interface ICapture
{
    /// <summary>Register the window for capture and request a first picture.</summary>
    /// <param name="key">The window.</param>
    void Start(WindowKey key);

    /// <summary>Unregister the window and drop its picture, rate and hold mark.</summary>
    /// <param name="key">The window.</param>
    void Stop(WindowKey key);

    /// <summary>Capture rate in pictures per second. Ignored if not finite and positive, or not started.</summary>
    /// <param name="key">The window.</param>
    /// <param name="fps">Pictures per second.</param>
    void SetRate(WindowKey key, double fps);

    /// <summary>Set or clear the "picture stands in for the window" mark; a change requests a fresh picture.</summary>
    /// <param name="key">The window.</param>
    /// <param name="on">The new mark.</param>
    void Hold(WindowKey key, bool on);

    /// <summary>Descriptor of the latest picture, or null if none or not started.</summary>
    /// <param name="key">The window.</param>
    /// <returns>The descriptor or null.</returns>
    CaptureFrame? Latest(WindowKey key);
}
