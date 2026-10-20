using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Projection;

/// <summary>Outcome kind for a window.</summary>
public enum TargetKind
{
    /// <summary>Placed on the stage at a rounded tile frame.</summary>
    Tile = 0,
    /// <summary>Parked off the stage.</summary>
    Park = 1,
    /// <summary>On stage but too small: parked, with a picture to be drawn later.</summary>
    ParkWithPicture = 2,
}

/// <summary>A window's target: a rounded tile frame, or a park frame.</summary>
/// <param name="Kind">Outcome kind.</param>
/// <param name="Rect">Target frame.</param>
public readonly record struct WindowTarget(TargetKind Kind, Rect Rect)
{
    /// <summary>Kind == ParkWithPicture.</summary>
    public bool NeedsPicture => Kind == TargetKind.ParkWithPicture;
}
