using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Layout;

/// <summary>One row of a layout result.</summary>
/// <param name="Node">The node laid out. In minimum-aware mode a split's entry carries a
/// rebuilt SplitNode with effective weights; a range entry carries a synthetic split.</param>
/// <param name="Rect">Rectangle as fractions of the unit square (y-down, origin top-left).</param>
/// <param name="Parent">Id of the parent node; null for the root.</param>
/// <param name="Scale">Accumulated shrink factor in minimum-aware mode; null in weights mode
/// and for range entries.</param>
/// <param name="Virtual">True only for range entries.</param>
public sealed record LayoutEntry(LayoutNode Node, Rect Rect, string? Parent, double? Scale, bool Virtual)
{
    /// <summary>The node laid out.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public LayoutNode Node { get; init; } = Node ?? throw new ArgumentNullException(nameof(Node));
}
