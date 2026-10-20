using System.Collections.Immutable;

namespace CanvasWm.Core.Projection;

/// <summary>Result of one pass. Immutable.</summary>
public sealed record ProjectionResult
{
    internal ProjectionResult(ImmutableArray<PanelPlacement> placements,
                              IReadOnlyDictionary<string, WindowTarget> targets)
    {
        Placements = placements;
        Targets = targets;
    }

    /// <summary>Placements in production order: tree panels in pre-order, then floating panels
    /// in document order.</summary>
    public ImmutableArray<PanelPlacement> Placements { get; }

    /// <summary>Targets by window key (ordinal). A key with no target is absent.</summary>
    public IReadOnlyDictionary<string, WindowTarget> Targets { get; }
}
