using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;

namespace CanvasWm.Core.Navigation;

/// <summary>Pure directional neighbour search over a layout entry table.
/// Moving in a direction picks another space to frame; it never edits a tree.</summary>
public static class DirectionalFocus
{
    /// <summary>Tolerance of the shared-edge test: |gap| must be strictly below 1e-6.</summary>
    public const double TouchTolerance = 1e-6;

    /// <summary>Overlap across the direction must be strictly greater than 1e-6.</summary>
    public const double OverlapTolerance = 1e-6;

    /// <summary>Added to the score of virtual (range) entries: 0.05. This is a penalty, so
    /// it makes them less likely to win.</summary>
    public const double VirtualPenalty = 0.05;

    /// <summary>Id of the best neighbour of <paramref name="currentId"/> in
    /// <paramref name="direction"/>, or null if there is none. Entries are scanned in table
    /// order (B1-B14).</summary>
    /// <param name="entries">Entry table, normally LayoutSolver.FocusLayout(root, null),
    /// later plus floating entries (A2).</param>
    /// <param name="currentId">Id of the current node (any kind, including a range id).
    /// An id with no entry gives null.</param>
    /// <param name="direction">Left, Right, Top (up) or Bottom (down).</param>
    /// <exception cref="ArgumentNullException">entries or currentId is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">direction is not a defined Edge value.</exception>
    public static string? Neighbour(LayoutEntries entries, string currentId, Edge direction)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return Neighbour((IEnumerable<KeyValuePair<string, LayoutEntry>>)entries, currentId, direction);
    }

    /// <summary>Same search over an explicit ordered sequence of (id, entry) rows. Use it when a
    /// table is assembled outside the solver, for example by tests or by a later feature that
    /// appends floating entries. Neighbour(LayoutEntries, ...) gives the same result as calling
    /// this overload on the table's enumeration (B15).</summary>
    /// <param name="rows">Ordered rows.</param>
    /// <param name="currentId">Id of the current node.</param>
    /// <param name="direction">Search direction.</param>
    /// <exception cref="ArgumentNullException">rows or currentId is null, or a row has a null
    /// key or a null value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">direction is not a defined Edge value.</exception>
    // SPEC-ASSUMPTION A3: rows overload exists instead of a public LayoutEntries factory.
    public static string? Neighbour(IEnumerable<KeyValuePair<string, LayoutEntry>> rows,
                                    string currentId, Edge direction)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(currentId);
        ValidateDirection(direction);

        // SPEC-ASSUMPTION A7: null keys/values throw ArgumentNullException when the row is reached.
        var list = new List<KeyValuePair<string, LayoutEntry>>();
        foreach (var row in rows)
        {
            if (row.Key is null || row.Value is null)
            {
                throw new ArgumentNullException(nameof(rows), "A row has a null key or value.");
            }
            list.Add(row);
        }

        LayoutEntry? current = null;
        foreach (var row in list)
        {
            if (string.Equals(row.Key, currentId, StringComparison.Ordinal))
            {
                current = row.Value;
                break;
            }
        }
        if (current is null)
        {
            return null;
        }

        Rect c = current.Rect;
        double best = double.PositiveInfinity;
        string? bestId = null;
        foreach (var row in list)
        {
            string key = row.Key;
            LayoutEntry entry = row.Value;

            if (string.Equals(key, currentId, StringComparison.Ordinal))
            {
                continue; // B3
            }
            // SPEC-ASSUMPTION A1: literal "desktop" id comparison, not the tree's desktop id (also excludes floats, A2).
            if (string.Equals(entry.Parent, DesktopNode.DefaultId, StringComparison.Ordinal))
            {
                continue; // B4
            }
            if (SubtreeContains(entry.Node, currentId) || SubtreeContains(current.Node, key))
            {
                continue; // B5
            }
            if (entry.Node is DesktopNode { Child: not null })
            {
                continue; // B6
            }
            if (!IsAdjacent(c, entry.Rect, direction))
            {
                continue; // B7, B8
            }

            double score = Score(c, entry.Rect, direction, entry.Virtual);
            if (score < best)
            {
                best = score;
                bestId = key;
            }
        }
        return bestId;
    }

    /// <summary>Geometric part of the filter: candidate shares current's edge on the
    /// direction side (B7) and overlaps it across the direction (B8).</summary>
    /// <param name="current">Current rect.</param>
    /// <param name="candidate">Candidate rect.</param>
    /// <param name="direction">Search direction.</param>
    /// <exception cref="ArgumentOutOfRangeException">direction is not a defined Edge value.</exception>
    public static bool IsAdjacent(Rect current, Rect candidate, Edge direction)
    {
        ValidateDirection(direction);
        Rect c = current;
        Rect k = candidate;
        double cMaxX = c.X + c.Width, cMaxY = c.Y + c.Height;
        double kMaxX = k.X + k.Width, kMaxY = k.Y + k.Height;

        bool touch = direction switch
        {
            Edge.Left => Math.Abs(kMaxX - c.X) < TouchTolerance,
            Edge.Right => Math.Abs(k.X - cMaxX) < TouchTolerance,
            Edge.Top => Math.Abs(kMaxY - c.Y) < TouchTolerance,
            _ => Math.Abs(k.Y - cMaxY) < TouchTolerance,
        };
        if (!touch)
        {
            return false;
        }

        double overlap = direction is Edge.Left or Edge.Right
            ? Math.Min(kMaxY, cMaxY) - Math.Max(k.Y, c.Y)
            : Math.Min(kMaxX, cMaxX) - Math.Max(k.X, c.X);
        return overlap > OverlapTolerance;
    }

    /// <summary>Score of a candidate (lower is better, B9). Does not check adjacency.
    /// May return +Infinity or NaN for degenerate rects (E5).</summary>
    /// <param name="current">Current rect.</param>
    /// <param name="candidate">Candidate rect.</param>
    /// <param name="direction">Search direction.</param>
    /// <param name="isVirtual">True when the candidate is a range entry.</param>
    /// <exception cref="ArgumentOutOfRangeException">direction is not a defined Edge value.</exception>
    public static double Score(Rect current, Rect candidate, Edge direction, bool isVirtual)
    {
        ValidateDirection(direction);
        Rect c = current;
        Rect k = candidate;
        double kc, cc, cext;
        if (direction is Edge.Left or Edge.Right)
        {
            kc = k.Y + k.Height / 2;
            cc = c.Y + c.Height / 2;
            cext = c.Height;
        }
        else
        {
            kc = k.X + k.Width / 2;
            cc = c.X + c.Width / 2;
            cext = c.Width;
        }
        double score = Math.Abs(Math.Log(k.Width / c.Width))
                     + Math.Abs(Math.Log(k.Height / c.Height))
                     + Math.Abs(kc - cc) / cext
                     + (isVirtual ? VirtualPenalty : 0);
        return score;
    }

    private static void ValidateDirection(Edge direction)
    {
        if (direction < Edge.Left || direction > Edge.Bottom)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Undefined Edge value.");
        }
    }

    private static bool SubtreeContains(LayoutNode node, string id)
    {
        if (string.Equals(node.Id, id, StringComparison.Ordinal))
        {
            return true;
        }
        switch (node)
        {
            case SplitNode split:
                foreach (var child in split.Children)
                {
                    if (SubtreeContains(child, id))
                    {
                        return true;
                    }
                }
                return false;
            case DesktopNode { Child: not null } desktop:
                return SubtreeContains(desktop.Child, id);
            default:
                return false;
        }
    }
}
