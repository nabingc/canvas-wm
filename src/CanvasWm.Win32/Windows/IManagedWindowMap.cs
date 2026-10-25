using CanvasWm.Engine.Services;

namespace CanvasWm.Win32.Windows;

/// <summary>Identity of a managed window as recorded when the tracker admitted it.</summary>
/// <param name="Handle">The window handle (HandleConversion form).</param>
/// <param name="ProcessId">Owning process id at admission.</param>
/// <param name="ThreadId">Owning thread id at admission.</param>
public readonly record struct ManagedWindowIdentity(ulong Handle, int ProcessId, int ThreadId);

/// <summary>Key/handle lookup over the managed set. The Win32 adapter never parses window
/// keys; the App composition (#29) implements this over the Engine tracker. Called only on
/// the engine thread; must be cheap and must not call back into the window system.</summary>
public interface IManagedWindowMap
{
    /// <summary>Identity of the managed window with this key, or null if not managed.</summary>
    /// <param name="key">The window key.</param>
    /// <returns>The identity, or null.</returns>
    ManagedWindowIdentity? Resolve(WindowKey key);

    /// <summary>Key of the managed window with this handle, or null if not managed.</summary>
    /// <param name="handle">The window handle (HandleConversion form).</param>
    /// <returns>The key, or null.</returns>
    WindowKey? KeyOf(ulong handle);
}
