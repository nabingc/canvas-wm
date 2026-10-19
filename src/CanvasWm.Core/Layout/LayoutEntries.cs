using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace CanvasWm.Core.Layout;

/// <summary>Immutable, insertion-ordered map from node id to entry.</summary>
public sealed class LayoutEntries : IReadOnlyDictionary<string, LayoutEntry>
{
    private readonly string[] _keys;
    private readonly LayoutEntry[] _values;
    private readonly Dictionary<string, int> _index;

    private LayoutEntries(string[] keys, LayoutEntry[] values, Dictionary<string, int> index)
    {
        _keys = keys;
        _values = values;
        _index = index;
    }

    /// <summary>The empty table.</summary>
    public static LayoutEntries Empty { get; } =
        new(Array.Empty<string>(), Array.Empty<LayoutEntry>(), new Dictionary<string, int>(StringComparer.Ordinal));

    /// <summary>Number of entries.</summary>
    public int Count => _keys.Length;

    /// <summary>Gets the entry for an id.</summary>
    /// <exception cref="KeyNotFoundException">id not present.</exception>
    /// <exception cref="ArgumentNullException">id is null.</exception>
    public LayoutEntry this[string id]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(id);
            if (_index.TryGetValue(id, out int i))
            {
                return _values[i];
            }
            throw new KeyNotFoundException($"No layout entry with id '{id}'.");
        }
    }

    /// <summary>True when the id is present.</summary>
    /// <exception cref="ArgumentNullException">id is null.</exception>
    public bool ContainsKey(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _index.ContainsKey(id);
    }

    /// <summary>Looks up an entry.</summary>
    /// <exception cref="ArgumentNullException">id is null.</exception>
    public bool TryGetValue(string id, [MaybeNullWhen(false)] out LayoutEntry value)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_index.TryGetValue(id, out int i))
        {
            value = _values[i];
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Ids in insertion order.</summary>
    public IEnumerable<string> Keys => _keys;

    /// <summary>Entries in insertion order.</summary>
    public IEnumerable<LayoutEntry> Values => _values;

    /// <summary>Enumerates (id, entry) pairs in insertion order.</summary>
    public IEnumerator<KeyValuePair<string, LayoutEntry>> GetEnumerator()
    {
        for (int i = 0; i < _keys.Length; i++)
        {
            yield return new KeyValuePair<string, LayoutEntry>(_keys[i], _values[i]);
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Mutable builder used by the solver.</summary>
    internal sealed class Builder
    {
        private readonly List<string> _keys = new();
        private readonly List<LayoutEntry> _values = new();
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

        public void Set(string id, LayoutEntry entry)
        {
            if (_index.TryGetValue(id, out int i))
            {
                _values[i] = entry; // SPEC-ASSUMPTION A3: re-setting an id keeps its original position.
            }
            else
            {
                _index[id] = _keys.Count;
                _keys.Add(id);
                _values.Add(entry);
            }
        }

        public bool TryGet(string id, out LayoutEntry? entry)
        {
            if (_index.TryGetValue(id, out int i))
            {
                entry = _values[i];
                return true;
            }
            entry = null;
            return false;
        }

        public List<KeyValuePair<string, LayoutEntry>> Snapshot()
        {
            var list = new List<KeyValuePair<string, LayoutEntry>>(_keys.Count);
            for (int i = 0; i < _keys.Count; i++)
            {
                list.Add(new(_keys[i], _values[i]));
            }
            return list;
        }

        public LayoutEntries Build() =>
            _keys.Count == 0 ? Empty : new LayoutEntries(_keys.ToArray(), _values.ToArray(), new(_index, StringComparer.Ordinal));
    }
}
