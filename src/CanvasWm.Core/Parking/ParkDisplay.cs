using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Parking;

/// <summary>The display context of a park computation. All rects are in the shared y-down DIP
/// space. Equality is compiler-generated; OtherDisplays is compared by reference.</summary>
public sealed record ParkDisplay
{
    /// <summary>Creates a display context.</summary>
    /// <param name="visibleArea">The display's work area. Only its Width and Height are used.</param>
    /// <param name="displayFrame">The display's full frame, or null when it is unknown.</param>
    /// <param name="otherDisplays">Rects of the other displays. The display's own frame is not
    /// included. The constructor copies the sequence.</param>
    /// <param name="fallbackOrigin">Origin of the park frame when displayFrame is null.</param>
    /// <exception cref="ArgumentNullException">otherDisplays is null.</exception>
    public ParkDisplay(Rect visibleArea, Rect? displayFrame,
                       IEnumerable<Rect> otherDisplays, Point fallbackOrigin)
    {
        ArgumentNullException.ThrowIfNull(otherDisplays);
        VisibleArea = visibleArea;
        DisplayFrame = displayFrame;
        // SPEC-ASSUMPTION A4: ParkDisplay copies its list into an immutable snapshot.
        OtherDisplays = Array.AsReadOnly(otherDisplays.ToArray());
        FallbackOrigin = fallbackOrigin;
    }

    /// <summary>The display's work area.</summary>
    public Rect VisibleArea { get; }

    /// <summary>The display's full frame, or null when unknown.</summary>
    public Rect? DisplayFrame { get; }

    /// <summary>An immutable snapshot in the order given.</summary>
    public IReadOnlyList<Rect> OtherDisplays { get; }

    /// <summary>Origin of the park frame when DisplayFrame is null.</summary>
    public Point FallbackOrigin { get; }
}
