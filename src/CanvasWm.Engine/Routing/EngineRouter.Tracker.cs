using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Routing;

public sealed partial class EngineRouter
{
    // ---- IWindowTrackerTarget (B9-B13) ----

    /// <summary>Full frames of the applied displays, in order (a new list each call).</summary>
    public IReadOnlyList<Rect> DisplayFrames => _applied.Select(d => d.Frame).ToArray();

    /// <summary>True if any engine holds key (B11).</summary>
    /// <param name="key">The window.</param>
    /// <returns>Whether it is held.</returns>
    public bool Holds(WindowKey key) => key is not null && Holder(key) is not null;

    private int MirrorIndex(WindowKey key)
    {
        for (int i = 0; i < _mirror.Count; i++)
        {
            if (_mirror[i].Key == key) return i;
        }

        return -1;
    }

    private void MirrorSet(WindowInfo info)
    {
        int i = MirrorIndex(info.Key);
        if (i >= 0) _mirror[i] = info; else _mirror.Add(info);
    }

    /// <summary>The initial scan finished (B10).</summary>
    /// <param name="windows">The tracked windows.</param>
    public void Ready(IReadOnlyList<WindowInfo> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (IsShuttingDown || IsReady) return;
        _mirror.Clear();
        foreach (WindowInfo w in windows)
        {
            ArgumentNullException.ThrowIfNull(w, nameof(windows));
            MirrorSet(w);
        }

        IsReady = true;
        ShowAll();
    }

    /// <summary>A window was admitted (B12).</summary>
    /// <param name="info">The window.</param>
    public void WindowAdded(WindowInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (IsShuttingDown) return;
        MirrorSet(info);
        if (Holder(info.Key) is { } h)
        {
            h.WindowAdded(info);
        }
        else if (OwnerOf(info.Key, info.Frame) is { } o)
        {
            EngineFor(o.Key)?.WindowAdded(info);
        }
    }

    /// <summary>A window was removed (B12).</summary>
    /// <param name="key">The window.</param>
    /// <param name="temporary">True if it may come back.</param>
    public void WindowRemoved(WindowKey key, bool temporary)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IsShuttingDown) return;
        int i = MirrorIndex(key);
        if (i >= 0) _mirror.RemoveAt(i);
        DisplayEngine? h = Holder(key);
        if (LastFocusedKey == key) LastFocusedKey = null;
        h?.WindowRemoved(key, temporary);
    }

    /// <summary>A tracked window took focus (B12).</summary>
    /// <param name="key">The window.</param>
    public void WindowFocused(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IsShuttingDown) return;
        LastFocusedKey = key;
        Holder(key)?.WindowFocused(key);
    }

    /// <summary>Focus moved to an unmanaged window of processId; broadcast to every engine (B13).</summary>
    /// <param name="processId">The process.</param>
    public void FocusLeftWindows(int processId)
    {
        if (IsShuttingDown) return;
        // SPEC-ASSUMPTION A8: LastFocusedKey is kept; only removal or release clears it.
        foreach (DisplayEngine e in _engines.ToArray()) e.FocusLeftWindows(processId);
    }

    /// <summary>A tracked window moved or was resized (B12).</summary>
    /// <param name="key">The window.</param>
    /// <param name="frame">The new frame.</param>
    public void WindowMoved(WindowKey key, Rect frame)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IsShuttingDown) return;
        int i = MirrorIndex(key);
        if (i >= 0) _mirror[i] = _mirror[i] with { Frame = frame };
        Holder(key)?.WindowMoved(key, frame);
    }

    /// <summary>A tracked window's title changed (B12).</summary>
    /// <param name="key">The window.</param>
    /// <param name="title">The title.</param>
    public void WindowTitleChanged(WindowKey key, string title)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(title);
        if (IsShuttingDown) return;
        int i = MirrorIndex(key);
        if (i >= 0) _mirror[i] = _mirror[i] with { Title = title };
        Holder(key)?.WindowTitleChanged(key, title);
    }

    /// <summary>A tracked window became eligible for tiling (B12).</summary>
    /// <param name="key">The window.</param>
    public void WindowBecameDockable(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IsShuttingDown) return;
        int i = MirrorIndex(key);
        if (i >= 0) _mirror[i] = _mirror[i] with { Dockable = true };
        Holder(key)?.WindowBecameDockable(key);
    }

    /// <summary>An application became the foreground application; broadcast to every engine (B13).</summary>
    /// <param name="processId">The process.</param>
    /// <param name="ownsDesktop">Whether the process owns the desktop.</param>
    public void AppActivated(int processId, bool ownsDesktop)
    {
        if (IsShuttingDown) return;
        LastActiveProcessId = processId;
        _lastOwnsDesktop = ownsDesktop;
        foreach (DisplayEngine e in _engines.ToArray()) e.AppActivated(processId, ownsDesktop);
    }

    /// <summary>Hand the window back to the OS and stop managing it (B12).</summary>
    /// <param name="key">The window.</param>
    public void Release(WindowKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (IsShuttingDown) return;
        int i = MirrorIndex(key);
        if (i >= 0) _mirror.RemoveAt(i);
        DisplayEngine? h = Holder(key);
        if (LastFocusedKey == key) LastFocusedKey = null;
        h?.Release(key);
    }
}
