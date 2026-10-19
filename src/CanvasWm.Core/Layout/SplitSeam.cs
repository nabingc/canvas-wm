using System.Collections.Immutable;

namespace CanvasWm.Core.Layout;

/// <summary>Snapshot of one divider of a split, by leaf ids (not by split id).</summary>
public sealed record SplitSeam
{
    /// <summary>Creates a seam snapshot.</summary>
    /// <param name="axis">Axis of the split the seam belongs to.</param>
    /// <param name="leaves">All leaf ids of that split, in order.</param>
    /// <param name="before">Leaf ids that lay before the divider.</param>
    /// <exception cref="ArgumentNullException">leaves or before is null, or any element is null.</exception>
    public SplitSeam(Axis axis, IEnumerable<string> leaves, IEnumerable<string> before)
    {
        ArgumentNullException.ThrowIfNull(leaves);
        ArgumentNullException.ThrowIfNull(before);
        Axis = axis;
        Leaves = ImmutableArray.CreateRange(leaves);
        Before = ImmutableArray.CreateRange(before);
        foreach (var l in Leaves)
        {
            if (l is null)
            {
                throw new ArgumentNullException(nameof(leaves), "A leaf id is null.");
            }
        }
        foreach (var b in Before)
        {
            if (b is null)
            {
                throw new ArgumentNullException(nameof(before), "A leaf id is null.");
            }
        }
    }

    /// <summary>Axis of the split the seam belongs to.</summary>
    public Axis Axis { get; init; }

    /// <summary>All leaf ids of that split, in order, at snapshot time.</summary>
    public ImmutableArray<string> Leaves { get; }

    /// <summary>Leaf ids that lay before the divider (left of it for X, above it for Y).</summary>
    public ImmutableArray<string> Before { get; }

    /// <summary>Structural value equality.</summary>
    public bool Equals(SplitSeam? other) =>
        other is not null && Axis == other.Axis
        && Equality.SeqEqual(Leaves, other.Leaves)
        && Equality.SeqEqual(Before, other.Before);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Axis);
        Equality.AddAll(ref h, Leaves);
        Equality.AddAll(ref h, Before);
        return h.ToHashCode();
    }
}
