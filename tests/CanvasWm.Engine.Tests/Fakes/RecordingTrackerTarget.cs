using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tracking;

namespace CanvasWm.Engine.Tests.Fakes;

/// <summary>One recorded target call. Name = method name; the other members carry that method's
/// arguments (Flag = temporary or ownsDesktop); At = clock time when recorded.</summary>
public sealed record TrackerCall(string Name, WindowKey? Key = null, WindowInfo? Info = null,
                                 Rect? Frame = null, string? Title = null, int? ProcessId = null,
                                 bool? Flag = null, double At = 0);

/// <summary>Target that records every call in order.</summary>
public sealed class RecordingTrackerTarget : IWindowTrackerTarget
{
    private readonly IClock _clock;
    private readonly List<TrackerCall> _calls = new();

    /// <summary>Creates a target stamping calls with the clock's time.</summary>
    public RecordingTrackerTarget(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>Display frames; default one 1440 x 900 display.</summary>
    public List<Rect> Displays { get; } = new() { new Rect(0, 0, 1440, 900) };

    /// <summary>Keys reported by Holds.</summary>
    public HashSet<WindowKey> Held { get; } = new();

    /// <summary>Every method call except DisplayFrames and Holds, in order.</summary>
    public IReadOnlyList<TrackerCall> Calls => _calls;

    /// <summary>Argument of the last Ready.</summary>
    public IReadOnlyList<WindowInfo>? ReadyWindows { get; private set; }

    /// <summary>Invoked after each call is recorded.</summary>
    public Action<TrackerCall>? OnCall { get; set; }

    /// <summary>Clears Calls.</summary>
    public void Clear() => _calls.Clear();

    /// <inheritdoc />
    public IReadOnlyList<Rect> DisplayFrames => Displays.ToList();

    /// <inheritdoc />
    public bool Holds(WindowKey key) => Held.Contains(key);

    /// <inheritdoc />
    public void Ready(IReadOnlyList<WindowInfo> windows)
    {
        ReadyWindows = windows.ToList();
        Record(new TrackerCall("Ready"));
    }

    /// <inheritdoc />
    public void WindowAdded(WindowInfo info) => Record(new TrackerCall("WindowAdded", Key: info.Key, Info: info));

    /// <inheritdoc />
    public void WindowRemoved(WindowKey key, bool temporary) =>
        Record(new TrackerCall("WindowRemoved", Key: key, Flag: temporary));

    /// <inheritdoc />
    public void WindowFocused(WindowKey key) => Record(new TrackerCall("WindowFocused", Key: key));

    /// <inheritdoc />
    public void FocusLeftWindows(int processId) => Record(new TrackerCall("FocusLeftWindows", ProcessId: processId));

    /// <inheritdoc />
    public void WindowMoved(WindowKey key, Rect frame) =>
        Record(new TrackerCall("WindowMoved", Key: key, Frame: frame));

    /// <inheritdoc />
    public void WindowTitleChanged(WindowKey key, string title) =>
        Record(new TrackerCall("WindowTitleChanged", Key: key, Title: title));

    /// <inheritdoc />
    public void WindowBecameDockable(WindowKey key) => Record(new TrackerCall("WindowBecameDockable", Key: key));

    /// <inheritdoc />
    public void AppActivated(int processId, bool ownsDesktop) =>
        Record(new TrackerCall("AppActivated", ProcessId: processId, Flag: ownsDesktop));

    /// <inheritdoc />
    public void Release(WindowKey key) => Record(new TrackerCall("Release", Key: key));

    private void Record(TrackerCall call)
    {
        TrackerCall stamped = call with { At = _clock.Now };
        _calls.Add(stamped);
        OnCall?.Invoke(stamped);
    }
}
