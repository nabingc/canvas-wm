using System.Collections.Immutable;

namespace CanvasWm.Core.Layout;

/// <summary>Pure tree edits used by docking.</summary>
public static class LayoutEdits
{
    /// <summary>Lower clamp of the insert fraction: 0.1.</summary>
    public const double MinFraction = 0.1;

    /// <summary>Upper clamp of the insert fraction: 0.9.</summary>
    public const double MaxFraction = 0.9;

    /// <summary>Fraction every caller uses: 0.5.</summary>
    public const double DefaultFraction = 0.5;

    /// <summary>Suffix of the derived id of a cross-axis seam group: "-range".</summary>
    public const string RangeSuffix = "-range";

    private static double W(SplitNode s, int i) =>
        i < s.Weights.Length ? s.Weights[i] : LayoutNormalizer.MissingWeight;

    // ---------------------------------------------------------------- queries

    /// <summary>Leaf ids in order. Null gives empty.</summary>
    /// <param name="node">The node, or null.</param>
    public static ImmutableArray<string> LeafIds(LayoutNode? node)
    {
        var b = ImmutableArray.CreateBuilder<string>();
        CollectLeaves(node, b);
        return b.ToImmutable();
    }

    private static void CollectLeaves(LayoutNode? node, ImmutableArray<string>.Builder b)
    {
        switch (node)
        {
            case null:
                return;
            case DesktopNode { Child: null } d:
                b.Add(d.Id);
                return;
            case DesktopNode d:
                CollectLeaves(d.Child, b);
                return;
            case SplitNode s:
                foreach (var c in s.Children)
                {
                    CollectLeaves(c, b);
                }
                return;
            default:
                b.Add(node.Id);
                return;
        }
    }

    /// <summary>All PanelNodes of the tree in depth-first pre-order. Null gives empty.</summary>
    /// <param name="node">The node, or null.</param>
    public static ImmutableArray<PanelNode> PanelsOf(LayoutNode? node)
    {
        var b = ImmutableArray.CreateBuilder<PanelNode>();
        CollectPanels(node, b);
        return b.ToImmutable();
    }

    private static void CollectPanels(LayoutNode? node, ImmutableArray<PanelNode>.Builder b)
    {
        switch (node)
        {
            case PanelNode p:
                b.Add(p);
                break;
            case DesktopNode d:
                CollectPanels(d.Child, b);
                break;
            case SplitNode s:
                foreach (var c in s.Children)
                {
                    CollectPanels(c, b);
                }
                break;
        }
    }

    /// <summary>The panel whose Views contain windowKey: tree first (pre-order), then Floating,
    /// then Hidden (list order). Null when none.</summary>
    /// <param name="document">The document.</param>
    /// <param name="windowKey">The window key.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static PanelNode? FindPanelOfWindow(LayoutDocument document, string windowKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(windowKey);
        // SPEC-ASSUMPTION A5: search tree, then floating, then hidden; first match wins.
        foreach (var p in PanelsOf(document.Root))
        {
            if (p.Views.Contains(windowKey, StringComparer.Ordinal))
            {
                return p;
            }
        }
        foreach (var f in document.Floating)
        {
            if (f.Panel.Views.Contains(windowKey, StringComparer.Ordinal))
            {
                return f.Panel;
            }
        }
        foreach (var h in document.Hidden)
        {
            if (h.Panel.Views.Contains(windowKey, StringComparer.Ordinal))
            {
                return h.Panel;
            }
        }
        return null;
    }

    // ---------------------------------------------------------------- remove

    /// <summary>Removes the node with this id (any kind except a desktop) and its subtree.
    /// shareRemovedSpace = true: freed weight is shared by all siblings in proportion;
    /// false: it goes to the adjacent sibling. Returns the same instance when nothing
    /// matched; null when the whole tree was removed.</summary>
    /// <param name="node">The tree, or null.</param>
    /// <param name="id">Id to remove.</param>
    /// <param name="shareRemovedSpace">Who inherits the freed space.</param>
    /// <exception cref="ArgumentNullException">id is null.</exception>
    public static LayoutNode? RemoveNode(LayoutNode? node, string id, bool shareRemovedSpace = true)
    {
        ArgumentNullException.ThrowIfNull(id);
        return RemoveCore(node, id, shareRemovedSpace);
    }

    private static LayoutNode? RemoveCore(LayoutNode? node, string id, bool share)
    {
        if (node is null)
        {
            return null;
        }
        if (node.Id == id)
        {
            return node is DesktopNode ? node : null;
        }
        switch (node)
        {
            case DesktopNode d:
            {
                if (d.Child is null)
                {
                    return d;
                }
                if (d.Child is not DesktopNode && d.Child.Id == id)
                {
                    return new DesktopNode(null, d.Id);
                }
                var r = RemoveCore(d.Child, id, share);
                if (r is null)
                {
                    return new DesktopNode(null, d.Id);
                }
                // SPEC-ASSUMPTION A9: a nested desktop is never removed by id.
                return ReferenceEquals(r, d.Child) ? d : new DesktopNode(r, d.Id);
            }
            case SplitNode s:
                return RemoveFromSplit(s, id, share);
            default:
                return node;
        }
    }

    private static LayoutNode? RemoveFromSplit(SplitNode s, string id, bool share)
    {
        int k = -1;
        for (int i = 0; i < s.Children.Length; i++)
        {
            if (s.Children[i].Id == id && s.Children[i] is not DesktopNode)
            {
                k = i;
                break;
            }
        }

        if (k >= 0)
        {
            var children = new List<LayoutNode>();
            var weights = new List<double>();
            double wk = W(s, k);
            for (int i = 0; i < s.Children.Length; i++)
            {
                if (i == k)
                {
                    continue;
                }
                children.Add(s.Children[i]);
                weights.Add(W(s, i));
            }
            if (children.Count == 0)
            {
                return null;
            }
            if (!share)
            {
                weights[Math.Min(k, children.Count - 1)] += wk;
            }
            return LayoutNormalizer.Normalize(new SplitNode(s.Id, s.Axis, weights, children));
        }

        var kept = new List<LayoutNode>();
        var keptW = new List<double>();
        bool changed = false;
        double carry = 0;
        for (int i = 0; i < s.Children.Length; i++)
        {
            var c = s.Children[i];
            double w = W(s, i);
            var r = RemoveCore(c, id, share);
            if (r is null)
            {
                changed = true;
                if (share)
                {
                    continue;
                }
                // SPEC-ASSUMPTION A16: a vanished child's weight goes to the previous survivor, else the next.
                if (keptW.Count > 0)
                {
                    keptW[^1] += w;
                }
                else
                {
                    carry += w;
                }
                continue;
            }
            if (!ReferenceEquals(r, c))
            {
                changed = true;
            }
            kept.Add(r);
            keptW.Add(w + carry);
            carry = 0;
        }
        if (!changed)
        {
            return s;
        }
        if (kept.Count == 0)
        {
            return null;
        }
        return LayoutNormalizer.Normalize(new SplitNode(s.Id, s.Axis, keptW, kept));
    }

    /// <summary>Removes the panel with this id from the tree (RemoveNode with the given variant),
    /// else from Floating, else from Hidden. Not found gives the same document instance.</summary>
    /// <param name="document">The document.</param>
    /// <param name="panelId">Panel id.</param>
    /// <param name="shareRemovedSpace">Who inherits the freed space in the tree.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static LayoutDocument RemovePanel(LayoutDocument document, string panelId,
                                             bool shareRemovedSpace = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(panelId);
        // SPEC-ASSUMPTION A7: the removal variant is a parameter defaulting to sharing.
        foreach (var p in PanelsOf(document.Root))
        {
            if (p.Id == panelId)
            {
                return document with { Root = RemoveCore(document.Root, panelId, shareRemovedSpace) };
            }
        }
        if (document.Floating.Any(f => f.Panel.Id == panelId))
        {
            return new LayoutDocument(document.Root,
                document.Floating.Where(f => f.Panel.Id != panelId),
                document.Hidden, document.HiddenDesktop);
        }
        if (document.Hidden.Any(h => h.Panel.Id == panelId))
        {
            return new LayoutDocument(document.Root, document.Floating,
                document.Hidden.Where(h => h.Panel.Id != panelId), document.HiddenDesktop);
        }
        return document;
    }

    // ---------------------------------------------------------------- insert beside

    /// <summary>Wraps the node whose id is targetId in a new split (id splitId) together with
    /// newNode on the given edge, newNode taking fraction (clamped to [0.1, 0.9]) of the
    /// target's space; then normalises. Null node gives newNode. Target missing gives the
    /// same instance.</summary>
    /// <param name="node">The tree, or null.</param>
    /// <param name="targetId">Id of the target node.</param>
    /// <param name="newNode">Node to insert.</param>
    /// <param name="edge">Edge of the target.</param>
    /// <param name="splitId">Id of the new split.</param>
    /// <param name="fraction">Share of the target's space for newNode.</param>
    /// <exception cref="ArgumentNullException">targetId, newNode or splitId is null.</exception>
    public static LayoutNode? InsertBeside(LayoutNode? node, string targetId, LayoutNode newNode,
                                           Edge edge, string splitId,
                                           double fraction = DefaultFraction)
    {
        ArgumentNullException.ThrowIfNull(targetId);
        ArgumentNullException.ThrowIfNull(newNode);
        ArgumentNullException.ThrowIfNull(splitId);
        if (node is null)
        {
            return newNode;
        }
        // SPEC-ASSUMPTION A8: NaN fraction gives 0.5; infinities clamp to the bounds.
        double f = double.IsNaN(fraction) ? DefaultFraction : Math.Clamp(fraction, MinFraction, MaxFraction);
        return InsertCore(node, targetId, newNode, edge, splitId, f);
    }

    private static LayoutNode? InsertCore(LayoutNode node, string targetId, LayoutNode newNode,
                                          Edge edge, string splitId, double f)
    {
        if (node.Id == targetId)
        {
            var wrapped = edge.IsLeading()
                ? new SplitNode(splitId, edge.ToAxis(), new[] { f, 1 - f }, new[] { newNode, node })
                : new SplitNode(splitId, edge.ToAxis(), new[] { 1 - f, f }, new[] { node, newNode });
            return LayoutNormalizer.Normalize(wrapped);
        }
        switch (node)
        {
            case DesktopNode { Child: { } child } d:
            {
                var r = InsertCore(child, targetId, newNode, edge, splitId, f);
                return ReferenceEquals(r, child) ? d : new DesktopNode(r, d.Id);
            }
            case SplitNode s:
            {
                bool changed = false;
                var children = new List<LayoutNode>();
                var weights = new List<double>();
                for (int i = 0; i < s.Children.Length; i++)
                {
                    var c = s.Children[i];
                    var r = InsertCore(c, targetId, newNode, edge, splitId, f);
                    if (!ReferenceEquals(r, c))
                    {
                        changed = true;
                    }
                    if (r is not null)
                    {
                        children.Add(r);
                        weights.Add(W(s, i));
                    }
                }
                if (!changed)
                {
                    return s;
                }
                if (children.Count == 0)
                {
                    return null;
                }
                return LayoutNormalizer.Normalize(new SplitNode(s.Id, s.Axis, weights, children));
            }
            default:
                return node;
        }
    }

    // ---------------------------------------------------------------- seams

    /// <summary>Dock target for divider index of split: Id = split.Id, Edge = Left (X) or
    /// Top (Y), Seam = (split.Axis, LeafIds(split), leaf ids of the first index children).</summary>
    /// <param name="split">The split.</param>
    /// <param name="index">Divider index; clamped to [0, child count].</param>
    /// <exception cref="ArgumentNullException">split is null.</exception>
    public static DockTargetSpec SeamTarget(SplitNode split, int index)
    {
        ArgumentNullException.ThrowIfNull(split);
        // SPEC-ASSUMPTION A11: negative index clamps to 0.
        int i = Math.Clamp(index, 0, split.Children.Length);
        var before = new List<string>();
        for (int k = 0; k < i; k++)
        {
            before.AddRange(LeafIds(split.Children[k]));
        }
        return new DockTargetSpec(split.Id,
            split.Axis == Axis.X ? Edge.Left : Edge.Top,
            new SplitSeam(split.Axis, LeafIds(split), before));
    }

    /// <summary>Inserts newNode at the seam. No live seam leaf gives the same instance.</summary>
    /// <param name="root">The tree, or null.</param>
    /// <param name="seam">The seam snapshot.</param>
    /// <param name="newNode">Node to insert.</param>
    /// <param name="splitId">Id of the new split.</param>
    /// <exception cref="ArgumentNullException">seam, newNode or splitId is null.</exception>
    public static LayoutNode? InsertAtSeam(LayoutNode? root, SplitSeam seam, LayoutNode newNode,
                                           string splitId)
    {
        ArgumentNullException.ThrowIfNull(seam);
        ArgumentNullException.ThrowIfNull(newNode);
        ArgumentNullException.ThrowIfNull(splitId);
        if (root is null)
        {
            return root;
        }
        var seamLeaves = new HashSet<string>(seam.Leaves, StringComparer.Ordinal);
        var live = new HashSet<string>(LeafIds(root).Where(seamLeaves.Contains), StringComparer.Ordinal);
        if (live.Count == 0)
        {
            return root;
        }
        var before = new HashSet<string>(seam.Before, StringComparer.Ordinal);
        Edge edge = live.Any(before.Contains)
            ? (seam.Axis == Axis.X ? Edge.Right : Edge.Bottom)
            : (seam.Axis == Axis.X ? Edge.Left : Edge.Top);
        var ctx = new SeamContext(seam, before, live, edge, newNode, splitId);
        var r = SeamInsert(root, ctx);
        var n = LayoutNormalizer.Normalize(r);
        return n ?? root;
    }

    private sealed record SeamContext(SplitSeam Seam, HashSet<string> Before, HashSet<string> Live,
                                      Edge Edge, LayoutNode NewNode, string SplitId);

    private static LayoutNode SeamInsert(LayoutNode node, SeamContext c) => node switch
    {
        SplitNode s => SeamInsertSplit(s, c),
        DesktopNode { Child: { } child } d
            when !c.Live.Contains(d.Id) && LeafIds(child).ToHashSet(StringComparer.Ordinal).IsSupersetOf(c.Live)
            => new DesktopNode(SeamInsert(child, c), d.Id),
        _ => SeamLeaf(node, c),
    };

    private static LayoutNode SeamLeaf(LayoutNode node, SeamContext c) =>
        InsertBeside(node, node.Id, c.NewNode, c.Edge, c.SplitId, DefaultFraction) ?? node;

    private static LayoutNode SeamInsertSplit(SplitNode s, SeamContext c)
    {
        int n = s.Children.Length;
        var leafSets = new HashSet<string>[n];
        for (int i = 0; i < n; i++)
        {
            leafSets[i] = LeafIds(s.Children[i]).ToHashSet(StringComparer.Ordinal);
        }

        // Descend.
        for (int i = 0; i < n; i++)
        {
            if (leafSets[i].IsSupersetOf(c.Live))
            {
                var kids = s.Children.ToArray();
                kids[i] = SeamInsert(kids[i], c);
                return new SplitNode(s.Id, s.Axis, s.Weights, kids);
            }
        }

        // Span.
        int first = -1, last = -1;
        for (int i = 0; i < n; i++)
        {
            if (leafSets[i].Overlaps(c.Live))
            {
                if (first < 0)
                {
                    first = i;
                }
                last = i;
            }
        }
        if (first < 0)
        {
            // SPEC-ASSUMPTION A4: unreachable spanning fallback is treated as the leaf case.
            return SeamLeaf(s, c);
        }

        int m = last - first + 1;
        double total = 0;
        for (int i = first; i <= last; i++)
        {
            total += W(s, i);
        }

        if (s.Axis == c.Seam.Axis)
        {
            // SPEC-ASSUMPTION A3: insert before the first range child holding a non-"before" leaf, else after the range.
            int p = last + 1;
            for (int i = first; i <= last; i++)
            {
                if (leafSets[i].Any(l => !c.Before.Contains(l)))
                {
                    p = i;
                    break;
                }
            }
            var kids = new List<LayoutNode>();
            var ws = new List<double>();
            for (int i = 0; i < n; i++)
            {
                if (i == p)
                {
                    kids.Add(c.NewNode);
                    ws.Add(total / (m + 1));
                }
                double w = W(s, i);
                if (i >= first && i <= last)
                {
                    w *= (double)m / (m + 1);
                }
                kids.Add(s.Children[i]);
                ws.Add(w);
            }
            if (p == n)
            {
                kids.Add(c.NewNode);
                ws.Add(total / (m + 1));
            }
            return new SplitNode(s.Id, s.Axis, ws, kids);
        }
        else
        {
            // SPEC-ASSUMPTION A2: cross-axis seam groups the range as splitId + "-range" and inserts beside it.
            var rangeKids = new List<LayoutNode>();
            var rangeW = new List<double>();
            for (int i = first; i <= last; i++)
            {
                rangeKids.Add(s.Children[i]);
                rangeW.Add(W(s, i) / total);
            }
            var group = new SplitNode(c.SplitId + RangeSuffix, s.Axis, rangeW, rangeKids);
            var g = InsertBeside(group, group.Id, c.NewNode, c.Edge, c.SplitId, DefaultFraction) ?? group;
            var kids = new List<LayoutNode>();
            var ws = new List<double>();
            for (int i = 0; i < n; i++)
            {
                if (i == first)
                {
                    kids.Add(g);
                    ws.Add(total);
                }
                else if (i > first && i <= last)
                {
                    continue;
                }
                else
                {
                    kids.Add(s.Children[i]);
                    ws.Add(W(s, i));
                }
            }
            return new SplitNode(s.Id, s.Axis, ws, kids);
        }
    }
}
