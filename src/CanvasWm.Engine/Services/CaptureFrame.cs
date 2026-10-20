using CanvasWm.Core.Geometry;

namespace CanvasWm.Engine.Services;

/// <summary>Descriptor of the latest captured picture of a window. The pixels stay in the adapter.</summary>
/// <param name="Size">Picture size in DIPs.</param>
/// <param name="Serial">Increases with every captured picture across all windows.</param>
public readonly record struct CaptureFrame(Size Size, long Serial);
