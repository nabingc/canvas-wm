using System.Collections.Immutable;

namespace CanvasWm.Core.Layout;

/// <summary>Element-wise equality helpers for immutable arrays.</summary>
internal static class Equality
{
    public static bool SeqEqual<T>(ImmutableArray<T> a, ImmutableArray<T> b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        var cmp = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Length; i++)
        {
            if (!cmp.Equals(a[i], b[i]))
            {
                return false;
            }
        }
        return true;
    }

    public static void AddAll<T>(ref HashCode hash, ImmutableArray<T> items)
    {
        hash.Add(items.Length);
        foreach (var item in items)
        {
            hash.Add(item);
        }
    }
}
