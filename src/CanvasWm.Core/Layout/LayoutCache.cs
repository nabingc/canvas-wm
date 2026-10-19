namespace CanvasWm.Core.Layout;

/// <summary>Caller-owned most-recently-computed cache of layout results keyed by
/// structural document equality. Not thread-safe.</summary>
public sealed class LayoutCache
{
    /// <summary>Maximum number of documents kept: 6.</summary>
    public const int Capacity = 6;

    private readonly Func<LayoutDocument, LayoutEntries> _compute;
    private readonly List<(LayoutDocument Doc, LayoutEntries Result)> _items = new();

    /// <summary>Cache whose compute function is doc =&gt; LayoutSolver.FocusLayout(doc.Root, null).</summary>
    public LayoutCache()
        : this(doc => LayoutSolver.FocusLayout(doc.Root, null))
    {
    }

    /// <summary>Cache with a custom compute function.</summary>
    /// <param name="compute">Computes the entries for a document.</param>
    /// <exception cref="ArgumentNullException">compute is null.</exception>
    public LayoutCache(Func<LayoutDocument, LayoutEntries> compute)
    {
        ArgumentNullException.ThrowIfNull(compute);
        _compute = compute;
    }

    /// <summary>Returns the cached result for a structurally equal document, else computes,
    /// caches and returns it.</summary>
    /// <param name="document">The document.</param>
    /// <exception cref="ArgumentNullException">document is null.</exception>
    /// <exception cref="InvalidOperationException">compute returned null.</exception>
    public LayoutEntries GetOrCompute(LayoutDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var (doc, result) in _items)
        {
            if (doc.Equals(document))
            {
                return result;
            }
        }
        var computed = _compute(document)
            ?? throw new InvalidOperationException("The compute function returned null.");
        _items.Insert(0, (document, computed));
        if (_items.Count > Capacity)
        {
            _items.RemoveAt(_items.Count - 1);
        }
        return computed;
    }
}
