namespace CanvasWm.Core.Projection;

/// <summary>Engine policy values this computation reads (passed in, not read from the
/// Engine). Out-of-range numbers are clamped where they are used.</summary>
public sealed record ProjectionPolicy
{
    /// <summary>All defaults below.</summary>
    public static ProjectionPolicy Default { get; } = new();

    /// <summary>Use Tuning.FixedTileMinimum instead of Tuning.TileMinimumFloor. Default false.</summary>
    public bool FixedMinimum { get; init; } = false;

    /// <summary>Tab padding, DIPs. Default 4.</summary>
    public double TabPadding { get; init; } = 4;

    /// <summary>Largest side inset of a tab strip, DIPs. Default 0.</summary>
    public double TabInset { get; init; } = 0;

    /// <summary>Park off-stage windows at the park-small size. Default false.</summary>
    public bool ParkOutOfViewSmall { get; init; } = false;
}
