using System.Collections.Immutable;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;

namespace CanvasWm.Engine.Services;

/// <summary>Remembered dock place of a window: where to restore it and its neighbouring leaf ids.</summary>
public sealed record DockSpot
{
    /// <summary>Creates a dock spot.</summary>
    /// <param name="restore">Restore target.</param>
    /// <param name="neighbourLeaves">Neighbouring leaf ids.</param>
    /// <exception cref="ArgumentNullException">An argument or an element is null.</exception>
    public DockSpot(RestoreTarget restore, IEnumerable<string> neighbourLeaves)
    {
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(neighbourLeaves);
        var b = ImmutableArray.CreateBuilder<string>();
        foreach (var n in neighbourLeaves)
        {
            ArgumentNullException.ThrowIfNull(n, nameof(neighbourLeaves));
            b.Add(n);
        }

        Restore = restore;
        NeighbourLeaves = b.ToImmutable();
    }

    /// <summary>Where to restore the window.</summary>
    public RestoreTarget Restore { get; }

    /// <summary>Neighbouring leaf ids at the time the window left.</summary>
    public ImmutableArray<string> NeighbourLeaves { get; }

    /// <inheritdoc />
    public bool Equals(DockSpot? other) =>
        other is not null
        && Restore.Equals(other.Restore)
        && NeighbourLeaves.SequenceEqual(other.NeighbourLeaves, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Restore);
        foreach (var n in NeighbourLeaves) h.Add(n, StringComparer.Ordinal);
        return h.ToHashCode();
    }
}

/// <summary>Everything persisted for one engine key.</summary>
public sealed record SavedDisplay
{
    /// <summary>Creates a saved display; every input collection is copied.</summary>
    /// <param name="document">The layout document.</param>
    /// <param name="framedNodeId">Id of the framed node, or null for none.</param>
    /// <param name="framings">Saved views: slot name to node ids. Null means empty.</param>
    /// <param name="owners">Window key to owner (an EngineKey value). Null means empty.</param>
    /// <param name="dockSpots">Optional; null means absent and is distinct from empty.</param>
    /// <param name="floatSizes">Optional; null means absent and is distinct from empty.</param>
    /// <exception cref="ArgumentNullException">document is null, or a key/value inside a supplied collection is null.</exception>
    public SavedDisplay(LayoutDocument document,
                        string? framedNodeId = null,
                        IReadOnlyDictionary<string, IReadOnlyList<string>>? framings = null,
                        IReadOnlyDictionary<WindowKey, string>? owners = null,
                        IReadOnlyDictionary<WindowKey, DockSpot>? dockSpots = null,
                        IReadOnlyDictionary<WindowKey, Size>? floatSizes = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        Document = document;
        FramedNodeId = framedNodeId;

        var fb = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
        if (framings is not null)
        {
            foreach (var kv in framings)
            {
                ArgumentNullException.ThrowIfNull(kv.Key, nameof(framings));
                ArgumentNullException.ThrowIfNull(kv.Value, nameof(framings));
                var ab = ImmutableArray.CreateBuilder<string>(kv.Value.Count);
                foreach (var id in kv.Value)
                {
                    ArgumentNullException.ThrowIfNull(id, nameof(framings));
                    ab.Add(id);
                }

                fb[kv.Key] = ab.ToImmutable();
            }
        }

        Framings = fb.ToImmutable();
        Owners = CopyDictionary(owners, nameof(owners)) ?? ImmutableDictionary<WindowKey, string>.Empty;
        DockSpots = CopyDictionary(dockSpots, nameof(dockSpots));
        FloatSizes = CopyDictionary(floatSizes, nameof(floatSizes));
    }

    /// <summary>The layout document.</summary>
    public LayoutDocument Document { get; }

    /// <summary>Id of the framed node, or null.</summary>
    public string? FramedNodeId { get; }

    /// <summary>Saved views by slot name (ordinal keys).</summary>
    public ImmutableDictionary<string, ImmutableArray<string>> Framings { get; }

    /// <summary>Window key to owner.</summary>
    public ImmutableDictionary<WindowKey, string> Owners { get; }

    /// <summary>Dock spots, or null when absent.</summary>
    public ImmutableDictionary<WindowKey, DockSpot>? DockSpots { get; }

    /// <summary>Float sizes, or null when absent.</summary>
    public ImmutableDictionary<WindowKey, Size>? FloatSizes { get; }

    private static ImmutableDictionary<TK, TV>? CopyDictionary<TK, TV>(
        IReadOnlyDictionary<TK, TV>? source, string name) where TK : class
    {
        if (source is null) return null;
        var b = ImmutableDictionary.CreateBuilder<TK, TV>();
        foreach (var kv in source)
        {
            ArgumentNullException.ThrowIfNull(kv.Key, name);
            if (kv.Value is null) throw new ArgumentNullException(name);
            b[kv.Key] = kv.Value;
        }

        return b.ToImmutable();
    }

    private static bool DictEquals<TK, TV>(ImmutableDictionary<TK, TV>? a, ImmutableDictionary<TK, TV>? b,
                                           Func<TV, TV, bool> eq) where TK : notnull
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var o) || !eq(kv.Value, o)) return false;
        }

        return true;
    }

    private static int DictHash<TK, TV>(ImmutableDictionary<TK, TV>? d, Func<TV, int> hash) where TK : notnull
    {
        if (d is null) return -1;
        int sum = d.Count;
        foreach (var kv in d) sum += HashCode.Combine(kv.Key, hash(kv.Value));
        return sum;
    }

    /// <inheritdoc />
    public bool Equals(SavedDisplay? other) =>
        other is not null
        && Document.Equals(other.Document)
        && string.Equals(FramedNodeId, other.FramedNodeId, StringComparison.Ordinal)
        && DictEquals(Framings, other.Framings, (x, y) => x.SequenceEqual(y, StringComparer.Ordinal))
        && DictEquals(Owners, other.Owners, (x, y) => string.Equals(x, y, StringComparison.Ordinal))
        && DictEquals(DockSpots, other.DockSpots, (x, y) => x.Equals(y))
        && DictEquals(FloatSizes, other.FloatSizes, (x, y) => x.Equals(y));

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(Document);
        h.Add(FramedNodeId, StringComparer.Ordinal);
        h.Add(DictHash(Framings, v =>
        {
            var vh = new HashCode();
            foreach (var s in v) vh.Add(s, StringComparer.Ordinal);
            return vh.ToHashCode();
        }));
        h.Add(DictHash(Owners, v => StringComparer.Ordinal.GetHashCode(v)));
        h.Add(DictHash(DockSpots, v => v.GetHashCode()));
        h.Add(DictHash(FloatSizes, v => v.GetHashCode()));
        return h.ToHashCode();
    }
}
