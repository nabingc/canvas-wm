using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tracking;

/// <summary>Receives the tracker's decisions. Implemented by a router over the per-display
/// engines. All calls are made on the engine thread.</summary>
public interface IWindowTrackerTarget
{
    /// <summary>Full frames of every display, used for the admission centre test.</summary>
    IReadOnlyList<Rect> DisplayFrames { get; }

    /// <summary>True if any engine currently holds key (for example parked by an earlier session).</summary>
    /// <param name="key">The window.</param>
    /// <returns>Whether it is held.</returns>
    bool Holds(WindowKey key);

    /// <summary>Exactly once: the initial scan finished; windows = tracked set, admission order.</summary>
    /// <param name="windows">The tracked windows.</param>
    void Ready(IReadOnlyList<WindowInfo> windows);

    /// <summary>A window was admitted.</summary>
    /// <param name="info">The window.</param>
    void WindowAdded(WindowInfo info);

    /// <summary>A window was removed.</summary>
    /// <param name="key">The window.</param>
    /// <param name="temporary">True if it may come back.</param>
    void WindowRemoved(WindowKey key, bool temporary);

    /// <summary>A tracked window took focus.</summary>
    /// <param name="key">The window.</param>
    void WindowFocused(WindowKey key);

    /// <summary>Focus moved to an unmanaged window (or no window) of processId.</summary>
    /// <param name="processId">The process.</param>
    void FocusLeftWindows(int processId);

    /// <summary>A tracked window moved or was resized.</summary>
    /// <param name="key">The window.</param>
    /// <param name="frame">The new frame.</param>
    void WindowMoved(WindowKey key, Rect frame);

    /// <summary>A tracked window's title changed.</summary>
    /// <param name="key">The window.</param>
    /// <param name="title">The title.</param>
    void WindowTitleChanged(WindowKey key, string title);

    /// <summary>A tracked window became eligible for tiling.</summary>
    /// <param name="key">The window.</param>
    void WindowBecameDockable(WindowKey key);

    /// <summary>An application became the foreground application.</summary>
    /// <param name="processId">The process.</param>
    /// <param name="ownsDesktop">Whether the process owns the desktop.</param>
    void AppActivated(int processId, bool ownsDesktop);

    /// <summary>Hand the window back to the OS and stop managing it (newly excluded app).</summary>
    /// <param name="key">The window.</param>
    void Release(WindowKey key);
}
