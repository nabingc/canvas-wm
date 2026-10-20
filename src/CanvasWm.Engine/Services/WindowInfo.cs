using CanvasWm.Core.Geometry;

namespace CanvasWm.Engine.Services;

/// <summary>Description of a window as the adapter reports it.</summary>
/// <param name="Key">Window identity.</param>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="AppName">Display name of the application.</param>
/// <param name="App">Application identity, or null for none.</param>
/// <param name="Title">Window title.</param>
/// <param name="Frame">Current frame.</param>
/// <param name="Dockable">Whether the window is eligible for tiling.</param>
public sealed record WindowInfo(WindowKey Key, int ProcessId, string AppName, AppId? App,
                                string Title, Rect Frame, bool Dockable)
{
    /// <summary>Window identity.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public WindowKey Key { get; init; } = Key ?? throw new ArgumentNullException(nameof(Key));

    /// <summary>Display name of the application.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public string AppName { get; init; } = AppName ?? throw new ArgumentNullException(nameof(AppName));

    /// <summary>Window title.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public string Title { get; init; } = Title ?? throw new ArgumentNullException(nameof(Title));
}
