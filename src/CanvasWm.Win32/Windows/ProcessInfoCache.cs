namespace CanvasWm.Win32.Windows;

/// <summary>What is known about one application process.</summary>
internal sealed record ProcessInfo(string? AppId, string? AppName, int? IntegrityRid);

/// <summary>Per-process cache keyed by (pid, creation time), pruned per Enumerate (B11.5).
/// Guarded by a lock. No timer.</summary>
// SPEC-ASSUMPTION A15: per-process cache keyed by (pid, creation time), pruned per Enumerate.
internal sealed class ProcessInfoCache
{
    private readonly object _gate = new();
    private readonly Dictionary<(int Pid, ulong Created), ProcessInfo> _entries = [];

    internal bool TryGet(int pid, ulong created, out ProcessInfo? info)
    {
        lock (_gate)
        {
            return _entries.TryGetValue((pid, created), out info);
        }
    }

    internal void Set(int pid, ulong created, ProcessInfo info)
    {
        lock (_gate)
        {
            _entries[(pid, created)] = info;
        }
    }

    /// <summary>Removes entries whose pid is not in seen.</summary>
    internal void Prune(HashSet<int> seen)
    {
        lock (_gate)
        {
            foreach ((int Pid, ulong Created) key in _entries.Keys.ToArray())
            {
                if (!seen.Contains(key.Pid))
                {
                    _entries.Remove(key);
                }
            }
        }
    }

    internal int Count
    {
        get { lock (_gate) { return _entries.Count; } }
    }
}
