using CanvasWm.Core.Geometry;

namespace CanvasWm.Engine;

/// <summary>Where released windows go. Pure.</summary>
public static class ReleaseGeometry
{
    /// <summary>Largest fraction of the area a released window may span.</summary>
    public const double MaxAreaFraction = 0.7;
    /// <summary>Minimum released width.</summary>
    public const double MinWidth = 640;
    /// <summary>Minimum released height.</summary>
    public const double MinHeight = 420;
    /// <summary>Horizontal offset of slot 0 from the area origin.</summary>
    public const double OffsetX = 60;
    /// <summary>Vertical offset of slot 0 from the area origin.</summary>
    public const double OffsetY = 40;
    /// <summary>Cascade step per slot.</summary>
    public const double Step = 32;
    /// <summary>Number of slots before the cascade wraps.</summary>
    public const int Cycle = 8;

    /// <summary>Cascade slot <paramref name="index"/> (0-based) for a window of <paramref name="size"/> released into <paramref name="area"/>.</summary>
    /// <param name="area">The target area.</param>
    /// <param name="size">The window's current size.</param>
    /// <param name="index">The slot index.</param>
    /// <returns>The frame.</returns>
    /// <exception cref="ArgumentOutOfRangeException">index &lt; 0.</exception>
    public static Rect Cascade(Rect area, Size size, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        double k = Step * (index % Cycle);
        double width = Math.Min(MaxAreaFraction * area.Width, Math.Max(size.Width, MinWidth));
        double height = Math.Min(MaxAreaFraction * area.Height, Math.Max(size.Height, MinHeight));
        return new Rect(area.X + OffsetX + k, area.Y + OffsetY + k, width, height);
    }

    /// <summary><paramref name="size"/> centred in <paramref name="area"/> (no clamping).</summary>
    /// <param name="area">The area.</param>
    /// <param name="size">The size.</param>
    /// <returns>The frame.</returns>
    public static Rect Centre(Rect area, Size size) =>
        new(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2,
            size.Width, size.Height);
}
