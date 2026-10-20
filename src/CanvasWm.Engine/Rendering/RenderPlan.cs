using System.Collections.Immutable;
using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Rendering;

/// <summary>One window's entry in a render plan.</summary>
/// <param name="Key">The window.</param>
/// <param name="Frame">Its frame.</param>
/// <param name="Focused">Whether it is focused.</param>
// SPEC-ASSUMPTION A16: Key, Frame, Focused is the smallest useful placeholder.
public sealed record WindowPlan(WindowKey Key, Rect Frame, bool Focused)
{
    /// <summary>The window.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public WindowKey Key { get; init; } = Key ?? throw new ArgumentNullException(nameof(Key));
}

/// <summary>Complete, immutable description of everything to draw on one display. Placeholder.</summary>
public sealed record RenderPlan
{
    /// <summary>A plan with no windows.</summary>
    public static RenderPlan Empty { get; } = new(Array.Empty<WindowPlan>());

    /// <summary>Creates a plan.</summary>
    /// <param name="windows">The window entries.</param>
    /// <exception cref="ArgumentNullException">windows or an element is null.</exception>
    public RenderPlan(IEnumerable<WindowPlan> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var b = ImmutableArray.CreateBuilder<WindowPlan>();
        foreach (var w in windows)
        {
            ArgumentNullException.ThrowIfNull(w, nameof(windows));
            b.Add(w);
        }

        Windows = b.ToImmutable();
    }

    /// <summary>The window entries.</summary>
    public ImmutableArray<WindowPlan> Windows { get; }

    /// <inheritdoc />
    public bool Equals(RenderPlan? other) => other is not null && Windows.SequenceEqual(other.Windows);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var w in Windows) h.Add(w);
        return h.ToHashCode();
    }
}
