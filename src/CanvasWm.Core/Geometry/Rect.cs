namespace CanvasWm.Core.Geometry;

/// <summary>Axis-aligned rectangle in device-independent units, y-down.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct Rect(double X, double Y, double Width, double Height);
