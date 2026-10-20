namespace CanvasWm.Core.Geometry;

/// <summary>A point in device-independent units, y-down.</summary>
/// <param name="X">Horizontal coordinate.</param>
/// <param name="Y">Vertical coordinate.</param>
public readonly record struct Point(double X, double Y);
