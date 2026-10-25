using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tracking;
using CanvasWm.Win32.Windows;

namespace CanvasWm.App;

/// <summary>IManagedWindowMap over the tracker's identity lookups (router B43).</summary>
internal sealed class TrackerWindowMap : IManagedWindowMap
{
    /// <summary>Set once the tracker exists; until then nothing resolves.</summary>
    internal WindowTracker? Tracker { get; set; }

    public ManagedWindowIdentity? Resolve(WindowKey key)
    {
        TrackedIdentity? id = Tracker?.IdentityOf(key);
        return id is TrackedIdentity t ? new ManagedWindowIdentity(t.Handle, t.ProcessId, t.ThreadId) : null;
    }

    public WindowKey? KeyOf(ulong handle) => Tracker?.KeyOf(handle);
}
