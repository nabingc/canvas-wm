namespace CanvasWm.Core.Projection;

/// <summary>One breakpoint of a piecewise-linear axis map: unit value From maps to To.</summary>
/// <param name="From">Input unit value.</param>
/// <param name="To">Output unit value.</param>
public readonly record struct StretchBreakpoint(double From, double To);
