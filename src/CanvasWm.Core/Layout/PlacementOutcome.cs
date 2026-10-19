namespace CanvasWm.Core.Layout;

/// <summary>Kind of result of <see cref="Placement.PlaceNew"/>.</summary>
public enum PlacementKind
{
    /// <summary>The window was docked into the tree; Document is the new document.</summary>
    Docked = 0,

    /// <summary>The framed node or the anchor has no layout rect. The caller must float the
    /// window (floating feature, no explicit rect or size). Document is the input instance.</summary>
    NeedsFloat = 1,
}

/// <summary>Result of placing a new window.</summary>
/// <param name="Kind">Docked or NeedsFloat.</param>
/// <param name="Document">Docked: the document after the dock. NeedsFloat: the input
/// document (same instance).</param>
/// <param name="AnchorId">Docked: the anchor node id that was chosen. NeedsFloat: the anchor
/// id when one was chosen but had no rect, otherwise null.</param>
/// <param name="Edge">Docked: the edge passed to Dock (Right or Bottom). NeedsFloat: null.</param>
public sealed record PlacementOutcome(PlacementKind Kind, LayoutDocument Document,
                                      string? AnchorId, Edge? Edge)
{
    /// <summary>The resulting document.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public LayoutDocument Document { get; init; } =
        Document ?? throw new ArgumentNullException(nameof(Document));
}
