namespace CanvasWm.Engine.Tracking;

/// <summary>OS identity of a tracked window, as recorded at admission.</summary>
/// <param name="Handle">The OS window handle.</param>
/// <param name="ProcessId">Owning process id.</param>
/// <param name="ThreadId">Owning thread id.</param>
public readonly record struct TrackedIdentity(ulong Handle, int ProcessId, int ThreadId);
