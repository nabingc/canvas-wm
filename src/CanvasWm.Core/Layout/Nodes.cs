using System.Collections.Immutable;

namespace CanvasWm.Core.Layout;

/// <summary>A node of a layout tree: exactly one of SplitNode, PanelNode, DesktopNode.</summary>
public abstract record LayoutNode
{
    private protected LayoutNode(string id)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
    }

    /// <summary>Node id; layout results are keyed by it, so it should be unique within a tree.</summary>
    public string Id { get; init; }

    /// <summary>True when this node is a DesktopNode with no child.</summary>
    public bool IsEmptyDesktop => this is DesktopNode { Child: null };
}

/// <summary>Divides its area among its children along Axis, in proportion to Weights.</summary>
public sealed record SplitNode : LayoutNode
{
    /// <summary>Creates a split.</summary>
    /// <param name="id">Node id.</param>
    /// <param name="axis">Split axis.</param>
    /// <param name="weights">Child weights.</param>
    /// <param name="children">Children in order.</param>
    /// <exception cref="ArgumentNullException">id, weights or children is null, or any child is null.</exception>
    public SplitNode(string id, Axis axis, IEnumerable<double> weights, IEnumerable<LayoutNode> children)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(children);
        Axis = axis;
        Weights = ImmutableArray.CreateRange(weights);
        Children = ImmutableArray.CreateRange(children);
        foreach (var c in Children)
        {
            if (c is null)
            {
                throw new ArgumentNullException(nameof(children), "A child is null.");
            }
        }
    }

    /// <summary>Split axis.</summary>
    public Axis Axis { get; init; }

    /// <summary>One weight per child in a normalised tree. A raw (un-normalised) split may have
    /// fewer or more weights than children and non-positive/non-finite values.</summary>
    public ImmutableArray<double> Weights { get; }

    /// <summary>Children in order.</summary>
    public ImmutableArray<LayoutNode> Children { get; }

    /// <summary>Structural value equality.</summary>
    public bool Equals(SplitNode? other) =>
        other is not null && base.Equals(other) && Axis == other.Axis
        && Equality.SeqEqual(Weights, other.Weights)
        && Equality.SeqEqual(Children, other.Children);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Id, StringComparer.Ordinal);
        h.Add(Axis);
        Equality.AddAll(ref h, Weights);
        Equality.AddAll(ref h, Children);
        return h.ToHashCode();
    }
}

/// <summary>A tab group of one or more windows, identified by window keys.</summary>
public sealed record PanelNode : LayoutNode
{
    /// <summary>Creates a panel.</summary>
    /// <param name="id">Node id.</param>
    /// <param name="views">Ordered window keys.</param>
    /// <param name="selected">Visible tab's window key, or null.</param>
    /// <param name="tabbed">True when CanvasWM owns the tab strip.</param>
    /// <exception cref="ArgumentNullException">id or views is null, or any view is null.</exception>
    public PanelNode(string id, IEnumerable<string> views, string? selected, bool tabbed)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(views);
        Views = ImmutableArray.CreateRange(views);
        foreach (var v in Views)
        {
            if (v is null)
            {
                throw new ArgumentNullException(nameof(views), "A view is null.");
            }
        }
        Selected = selected;
        Tabbed = tabbed;
    }

    /// <summary>Panel holding exactly one window: Id = key, Views = [key], Selected = key, Tabbed = false.</summary>
    /// <param name="windowKey">The window key.</param>
    public static PanelNode ForWindow(string windowKey) =>
        new(windowKey, new[] { windowKey }, windowKey, false);

    /// <summary>Ordered window keys (tabs).</summary>
    public ImmutableArray<string> Views { get; }

    /// <summary>Visible tab's window key, or null.</summary>
    public string? Selected { get; init; }

    /// <summary>True when CanvasWM draws/owns the tab strip; false = OS-native grouping (tracked only).</summary>
    public bool Tabbed { get; init; }

    /// <summary>Tabbed &amp;&amp; Views.Length &gt; 1.</summary>
    public bool HasStrip => Tabbed && Views.Length > 1;

    /// <summary>!Tabbed &amp;&amp; Views.Length &gt; 1.</summary>
    public bool IsNativeGroup => !Tabbed && Views.Length > 1;

    /// <summary>Structural value equality.</summary>
    public bool Equals(PanelNode? other) =>
        other is not null && base.Equals(other)
        && string.Equals(Selected, other.Selected, StringComparison.Ordinal)
        && Tabbed == other.Tabbed
        && Equality.SeqEqual(Views, other.Views);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Id, StringComparer.Ordinal);
        h.Add(Selected, StringComparer.Ordinal);
        h.Add(Tabbed);
        Equality.AddAll(ref h, Views);
        return h.ToHashCode();
    }
}

/// <summary>Container for at most one child; with no child it is an empty tile of free desktop space.</summary>
public sealed record DesktopNode : LayoutNode
{
    /// <summary>"desktop".</summary>
    public const string DefaultId = "desktop";

    /// <summary>Creates a desktop node.</summary>
    /// <param name="child">Optional child.</param>
    /// <param name="id">Defaults to <see cref="DefaultId"/>.</param>
    public DesktopNode(LayoutNode? child = null, string id = DefaultId)
        : base(id)
    {
        Child = child;
    }

    /// <summary>The single child, or null for an empty desktop.</summary>
    public LayoutNode? Child { get; init; }
}
