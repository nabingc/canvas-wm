namespace CanvasWm.Core.Frames;

/// <summary>Safe range for window positions in device pixels (B12). Pure.</summary>
public static class PhysicalCoordinates
{
    /// <summary>30000: positions are kept in [-30000, 30000]. That is well inside the 16-bit
    /// range that window messages pack positions into, and away from the minimised sentinel
    /// -32000.</summary>
    public const int Limit = 30000;

    /// <summary>Math.Clamp(value, -Limit, Limit).</summary>
    /// <param name="value">The position in device pixels.</param>
    /// <returns>The clamped position.</returns>
    public static int Clamp(int value) => Math.Clamp(value, -Limit, Limit);
}
