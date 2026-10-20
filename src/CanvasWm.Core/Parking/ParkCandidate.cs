using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Parking;

/// <summary>One park position. Spot is the frame the window is moved to. Reach is the spot
/// extended vertically and is used only for scoring.</summary>
/// <param name="Spot">The frame the window is moved to.</param>
/// <param name="Reach">The vertically extended rect used for scoring.</param>
public readonly record struct ParkCandidate(Rect Spot, Rect Reach);
