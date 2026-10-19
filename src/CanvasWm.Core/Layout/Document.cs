using System.Collections.Immutable;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Layout;

/// <summary>Target to restore a hidden panel/desktop to. Concrete kinds are defined by the
/// hide/restore feature; this feature only carries the value through unchanged.</summary>
// SPEC-ASSUMPTION A11: empty abstract placeholder; concrete kinds belong to the hide/restore feature.
public abstract record RestoreTarget
{
    /// <summary>Creates a restore target.</summary>
    protected RestoreTarget()
    {
    }
}

/// <summary>A panel floating above the tiled tree.</summary>
/// <param name="Panel">The panel.</param>
/// <param name="Rect">Floating rectangle.</param>
/// <param name="Z">Z order.</param>
/// <param name="Size">Optional remembered size.</param>
public sealed record FloatingPanel(PanelNode Panel, Rect Rect, int Z, Size? Size = null)
{
    /// <summary>The panel.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public PanelNode Panel { get; init; } = Panel ?? throw new ArgumentNullException(nameof(Panel));
}

/// <summary>A hidden panel and where to restore it.</summary>
/// <param name="Panel">The panel.</param>
/// <param name="Restore">Restore target.</param>
public sealed record HiddenPanel(PanelNode Panel, RestoreTarget Restore)
{
    /// <summary>The panel.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public PanelNode Panel { get; init; } = Panel ?? throw new ArgumentNullException(nameof(Panel));

    /// <summary>Where to restore the panel.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public RestoreTarget Restore { get; init; } = Restore ?? throw new ArgumentNullException(nameof(Restore));
}

/// <summary>Record of a hidden desktop slot.</summary>
public sealed record HiddenDesktop
{
    /// <summary>Creates a hidden desktop record.</summary>
    /// <param name="group">Group name.</param>
    /// <param name="floats">Floating panels.</param>
    /// <param name="restore">Restore target.</param>
    /// <param name="neighbourLeaves">Neighbour leaf ids.</param>
    /// <param name="weight">Remembered weight.</param>
    public HiddenDesktop(string? group, IEnumerable<FloatingPanel> floats, RestoreTarget? restore,
                         IEnumerable<string> neighbourLeaves, double? weight)
    {
        ArgumentNullException.ThrowIfNull(floats);
        ArgumentNullException.ThrowIfNull(neighbourLeaves);
        Group = group;
        Floats = ImmutableArray.CreateRange(floats);
        Restore = restore;
        NeighbourLeaves = ImmutableArray.CreateRange(neighbourLeaves);
        Weight = weight;
    }

    /// <summary>Group name.</summary>
    public string? Group { get; init; }

    /// <summary>Floating panels.</summary>
    public ImmutableArray<FloatingPanel> Floats { get; }

    /// <summary>Restore target.</summary>
    public RestoreTarget? Restore { get; init; }

    /// <summary>Neighbour leaf ids.</summary>
    public ImmutableArray<string> NeighbourLeaves { get; }

    /// <summary>Remembered weight.</summary>
    public double? Weight { get; init; }

    /// <summary>Structural value equality.</summary>
    public bool Equals(HiddenDesktop? other) =>
        other is not null
        && string.Equals(Group, other.Group, StringComparison.Ordinal)
        && Equals(Restore, other.Restore)
        && Weight.Equals(other.Weight)
        && Equality.SeqEqual(Floats, other.Floats)
        && Equality.SeqEqual(NeighbourLeaves, other.NeighbourLeaves);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Group, StringComparer.Ordinal);
        h.Add(Restore);
        h.Add(Weight);
        Equality.AddAll(ref h, Floats);
        Equality.AddAll(ref h, NeighbourLeaves);
        return h.ToHashCode();
    }
}

/// <summary>One display's layout: an optional tree plus floating and hidden panels.</summary>
public sealed record LayoutDocument
{
    /// <summary>Creates a document.</summary>
    /// <param name="root">Tree root, or null.</param>
    /// <param name="floating">null means empty.</param>
    /// <param name="hidden">null means empty.</param>
    /// <param name="hiddenDesktop">Hidden desktop record, or null.</param>
    public LayoutDocument(LayoutNode? root,
                          IEnumerable<FloatingPanel>? floating = null,
                          IEnumerable<HiddenPanel>? hidden = null,
                          HiddenDesktop? hiddenDesktop = null)
    {
        Root = root;
        Floating = floating is null ? ImmutableArray<FloatingPanel>.Empty : ImmutableArray.CreateRange(floating);
        Hidden = hidden is null ? ImmutableArray<HiddenPanel>.Empty : ImmutableArray.CreateRange(hidden);
        HiddenDesktop = hiddenDesktop;
    }

    /// <summary>Document with no tree and empty lists.</summary>
    public static LayoutDocument Empty { get; } = new LayoutDocument(root: null);

    /// <summary>Tree root, or null.</summary>
    public LayoutNode? Root { get; init; }

    /// <summary>Floating panels.</summary>
    public ImmutableArray<FloatingPanel> Floating { get; }

    /// <summary>Hidden panels.</summary>
    public ImmutableArray<HiddenPanel> Hidden { get; }

    /// <summary>Hidden desktop record, or null.</summary>
    public HiddenDesktop? HiddenDesktop { get; init; }

    /// <summary>Structural value equality.</summary>
    public bool Equals(LayoutDocument? other) =>
        other is not null
        && Equals(Root, other.Root)
        && Equals(HiddenDesktop, other.HiddenDesktop)
        && Equality.SeqEqual(Floating, other.Floating)
        && Equality.SeqEqual(Hidden, other.Hidden);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Root);
        h.Add(HiddenDesktop);
        Equality.AddAll(ref h, Floating);
        Equality.AddAll(ref h, Hidden);
        return h.ToHashCode();
    }
}
