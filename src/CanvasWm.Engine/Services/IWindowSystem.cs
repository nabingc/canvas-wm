using CanvasWm.Core.Geometry;

namespace CanvasWm.Engine.Services;

/// <summary>The OS window system. All members are called on the engine (UI) thread.
/// Completions are invoked on that thread, exactly once, never inside the call that started the operation.</summary>
public interface IWindowSystem
{
    /// <summary>Fire-and-forget: bring the window's app forward and focus the window.
    /// Unknown key: no effect.</summary>
    /// <param name="key">The window.</param>
    void Focus(WindowKey key);

    /// <summary>Managed windows, frontmost first. Never null; a new snapshot each call.</summary>
    /// <returns>The keys.</returns>
    IReadOnlyList<WindowKey> FrontToBack();

    /// <summary>True if the window is currently on screen. Unknown key: false.</summary>
    /// <param name="key">The window.</param>
    /// <returns>Whether it is showing.</returns>
    bool IsShowing(WindowKey key);

    /// <summary>Fraction of rect that lies on any display's full frame.</summary>
    /// <param name="rect">The rect.</param>
    /// <returns>The visible fraction.</returns>
    double VisibleFraction(Rect rect);

    /// <summary>Reads the window's current frame; completion(null) if unknown or failed.</summary>
    /// <param name="key">The window.</param>
    /// <param name="completion">Receives the frame.</param>
    // SPEC-ASSUMPTION A1: callbacks (Action), not Task, for frame reads and writes.
    void ReadFrame(WindowKey key, Action<Rect?> completion);

    /// <summary>Requests a new frame; completion receives the frame read back after the write, or null.</summary>
    /// <param name="key">The window.</param>
    /// <param name="target">The requested frame.</param>
    /// <param name="completion">Receives the actual frame.</param>
    void SetFrame(WindowKey key, Rect target, Action<Rect?> completion);

    /// <summary>Writes several windows as one batch; completion receives one result per entry, in input order.</summary>
    /// <param name="writes">The writes.</param>
    /// <param name="completion">Receives the results.</param>
    /// <exception cref="ArgumentException">An entry has a null Key, or two entries share a key.</exception>
    void SetFrames(IReadOnlyList<FrameWrite> writes, Action<IReadOnlyList<FrameWriteResult>> completion);

    /// <summary>A managed window that is a native OS tab sibling of key, or null.</summary>
    /// <param name="key">The window.</param>
    /// <returns>The sibling or null.</returns>
    WindowKey? TabSibling(WindowKey key);

    /// <summary>Current pointer position.</summary>
    Point Pointer { get; }

    /// <summary>True while the primary mouse button is held.</summary>
    bool MouseDown { get; }

    /// <summary>Fallback origin for parked windows when the Engine has no display frame.</summary>
    Point ParkingOrigin { get; }
}
