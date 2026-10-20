using CanvasWm.Core.Geometry;

namespace CanvasWm.Engine.Services;

/// <summary>One entry of a batched frame write.</summary>
/// <param name="Key">The window.</param>
/// <param name="Target">The requested frame.</param>
public readonly record struct FrameWrite(WindowKey Key, Rect Target);

/// <summary>Outcome of one entry of a batched write.</summary>
/// <param name="Key">The window.</param>
/// <param name="Actual">Frame read back after the write, or null when unknown or failed.</param>
public readonly record struct FrameWriteResult(WindowKey Key, Rect? Actual);
