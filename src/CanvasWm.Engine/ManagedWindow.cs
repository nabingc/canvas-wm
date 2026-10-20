using CanvasWm.Engine.Services;

namespace CanvasWm.Engine;

/// <summary>Read-only snapshot of one managed window.</summary>
/// <param name="Info">Latest reported info. Info.Frame is the last known frame.</param>
/// <param name="Parked">True while the engine keeps the window parked off the visible stage.</param>
/// <param name="RememberedSpot">Dock spot recovered from the away table on re-add, or null.</param>
public sealed record ManagedWindow(WindowInfo Info, bool Parked, DockSpot? RememberedSpot)
{
    /// <summary>Latest reported info.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public WindowInfo Info { get; init; } = Info ?? throw new ArgumentNullException(nameof(Info));
}
