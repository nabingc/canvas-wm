using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Layout;

/// <summary>New-window placement and dwindle rebuild.</summary>
public static class Placement
{
    /// <summary>Weight of each child of a dwindle split: 0.5.</summary>
    public const double DwindleWeight = 0.5;

    /// <summary>Leftover cascade base x: 0.1.</summary>
    public const double CascadeBaseX = 0.1;

    /// <summary>Leftover cascade base y: 0.1.</summary>
    public const double CascadeBaseY = 0.1;

    /// <summary>Leftover cascade step in x: 0.03.</summary>
    public const double CascadeStepX = 0.03;

    /// <summary>Leftover cascade step in y: 0.04.</summary>
    public const double CascadeStepY = 0.04;

    /// <summary>Leftover floating width: 0.5.</summary>
    public const double CascadeWidth = 0.5;

    /// <summary>Leftover floating height: 0.55.</summary>
    public const double CascadeHeight = 0.55;

    /// <summary>Cascade wraps after this many leftovers: 6.</summary>
    public const int CascadeCycle = 6;

    /// <summary>Edge rule (B7): Right when (anchor.Height / framed.Height) is less than or equal to
    /// stageAspect * (anchor.Width / framed.Width), otherwise Bottom. Ties and +inf go Right;
    /// NaN anywhere gives Bottom. Never returns Left or Top.</summary>
    /// <param name="anchor">Anchor rect.</param>
    /// <param name="framed">Framed rect.</param>
    /// <param name="stageAspect">Stage width / height.</param>
    public static Edge ChooseEdge(Rect anchor, Rect framed, double stageAspect)
    {
        // SPEC-ASSUMPTION A6: literal edge arithmetic, no guard for zero sizes.
        double lhs = anchor.Height / framed.Height;
        double rhs = stageAspect * (anchor.Width / framed.Width);
        return lhs <= rhs ? Edge.Right : Edge.Bottom;
    }

    /// <summary>Anchor id (B4-B5) for placing into the framed node of document.Root, or null when
    /// framedId is not a node of the tree (or the tree is null). focusedKey is optional.</summary>
    /// <param name="document">The document.</param>
    /// <param name="framedId">Id of the framed node.</param>
    /// <param name="focusedKey">Focused window key, or null.</param>
    /// <exception cref="ArgumentNullException">document or framedId is null.</exception>
    public static string? ChooseAnchor(LayoutDocument document, string framedId, string? focusedKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(framedId);
        // SPEC-ASSUMPTION A4: the framed node is found through LayoutRects; virtual (range) entries are not tree nodes.
        var entries = LayoutSolver.LayoutRects(document.Root);
        if (!entries.TryGetValue(framedId, out var framedEntry) || framedEntry.Virtual)
        {
            return null;
        }
        return ChooseAnchorIn(document, framedEntry.Node, focusedKey);
    }

    private static string ChooseAnchorIn(LayoutDocument document, LayoutNode framed, string? focusedKey)
    {
        var panelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in LayoutEdits.PanelsOf(document.Root))
        {
            panelIds.Add(p.Id);
        }

        if (focusedKey is not null)
        {
            var fp = LayoutEdits.FindPanelOfWindow(document, focusedKey);
            if (fp is not null && panelIds.Contains(fp.Id))
            {
                // SPEC-ASSUMPTION A5: subtree containment is tested by id.
                var subtree = new HashSet<string>(StringComparer.Ordinal);
                CollectIds(framed, subtree);
                if (subtree.Contains(fp.Id))
                {
                    return fp.Id;
                }
            }
        }

        var leaves = LayoutEdits.LeafIds(framed);
        for (int i = leaves.Length - 1; i >= 0; i--)
        {
            if (panelIds.Contains(leaves[i]))
            {
                return leaves[i];
            }
        }
        return leaves.Length > 0 ? leaves[^1] : framed.Id;
    }

    private static void CollectIds(LayoutNode node, HashSet<string> ids)
    {
        ids.Add(node.Id);
        switch (node)
        {
            case SplitNode s:
                foreach (var c in s.Children)
                {
                    CollectIds(c, ids);
                }
                break;
            case DesktopNode { Child: { } child }:
                CollectIds(child, ids);
                break;
        }
    }

    /// <summary>Places windowKey beside the anchor inside framedId (B1-B10). Never throws for
    /// non-null arguments. Docked: calls ids.Make once (twice when Dock falls back).
    /// NeedsFloat: does not call ids.</summary>
    /// <param name="document">The document.</param>
    /// <param name="windowKey">Window to place.</param>
    /// <param name="framedId">Id of the framed node.</param>
    /// <param name="focusedKey">Focused window key, or null.</param>
    /// <param name="stageAspect">Stage width / height (computed by the Engine). Any double is
    /// accepted; see B7 for non-finite values.</param>
    /// <param name="ids">Id generator.</param>
    /// <param name="shareRemovedSpace">Passed through to Docking.Dock.</param>
    /// <exception cref="ArgumentNullException">A non-nullable argument is null.</exception>
    public static PlacementOutcome PlaceNew(LayoutDocument document, string windowKey,
                                            string framedId, string? focusedKey,
                                            double stageAspect, IIdGenerator ids,
                                            bool shareRemovedSpace = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(windowKey);
        ArgumentNullException.ThrowIfNull(framedId);
        ArgumentNullException.ThrowIfNull(ids);

        // SPEC-ASSUMPTION A3: a missing rect yields NeedsFloat; the float operation belongs to the caller.
        var rects = LayoutSolver.LayoutRects(document.Root);
        if (!rects.TryGetValue(framedId, out var framed) || framed.Virtual)
        {
            return new PlacementOutcome(PlacementKind.NeedsFloat, document, null, null);
        }

        string anchor = ChooseAnchorIn(document, framed.Node, focusedKey);
        if (!rects.TryGetValue(anchor, out var anchorEntry) || anchorEntry.Virtual)
        {
            return new PlacementOutcome(PlacementKind.NeedsFloat, document, anchor, null);
        }

        var edge = ChooseEdge(anchorEntry.Rect, framed.Rect, stageAspect);
        var doc2 = Docking.Dock(document, windowKey, new DockTargetSpec(anchor, edge, null),
            ids, shareRemovedSpace);
        return new PlacementOutcome(PlacementKind.Docked, doc2, anchor, edge);
    }

    /// <summary>Unit-space rect of the leftover with 0-based index i (B17):
    /// k = i mod 6 (non-negative), (0.1 + 0.03k, 0.1 + 0.04k, 0.5, 0.55).</summary>
    /// <param name="index">Leftover index.</param>
    public static Rect CascadeRect(int index)
    {
        // SPEC-ASSUMPTION A9: a negative index wraps into [0, 5].
        int k = ((index % CascadeCycle) + CascadeCycle) % CascadeCycle;
        return new Rect(CascadeBaseX + CascadeStepX * k, CascadeBaseY + CascadeStepY * k,
            CascadeWidth, CascadeHeight);
    }

    /// <summary>Rebuilds the tree as a dwindle spiral of the panels holding orderedKeys
    /// (B11-B20). Calls ids.Make exactly once per tiled panel, outermost first.</summary>
    /// <param name="document">The document.</param>
    /// <param name="orderedKeys">Window keys in priority order; null elements throw.</param>
    /// <param name="landscape">True: the outermost split is a row (X).</param>
    /// <param name="ids">Id generator.</param>
    /// <exception cref="ArgumentNullException">A non-nullable argument or an element of orderedKeys is null.</exception>
    public static LayoutDocument ArrangeDwindle(LayoutDocument document,
                                                IEnumerable<string> orderedKeys,
                                                bool landscape, IIdGenerator ids)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(orderedKeys);
        ArgumentNullException.ThrowIfNull(ids);

        // SPEC-ASSUMPTION A10: null elements throw before any work.
        var keyList = new List<string>();
        foreach (var k in orderedKeys)
        {
            ArgumentNullException.ThrowIfNull(k, nameof(orderedKeys));
            keyList.Add(k);
        }

        // SPEC-ASSUMPTION A1: visible = held by a tree panel or a floating panel.
        var treePanels = LayoutEdits.PanelsOf(document.Root);
        var visible = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in treePanels)
        {
            visible.UnionWith(p.Views);
        }
        foreach (var f in document.Floating)
        {
            visible.UnionWith(f.Panel.Views);
        }

        var kept = new HashSet<string>(StringComparer.Ordinal);
        var panels = new List<PanelNode>();
        var panelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keyList)
        {
            if (!visible.Contains(key) || !kept.Add(key))
            {
                continue;
            }
            var panel = LayoutEdits.FindPanelOfWindow(document, key);
            if (panel is not null && panelIds.Add(panel.Id))
            {
                panels.Add(panel);
            }
        }

        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in panels)
        {
            included.UnionWith(p.Views);
        }

        var floating = new List<FloatingPanel>();
        foreach (var f in document.Floating)
        {
            if (!f.Panel.Views.Any(included.Contains))
            {
                floating.Add(f);
            }
        }

        // SPEC-ASSUMPTION A8: tree panels with empty views are leftovers and are floated unchanged.
        int index = 0;
        foreach (var p in treePanels)
        {
            if (p.Views.Any(included.Contains))
            {
                continue;
            }
            int z = 1;
            if (floating.Count > 0)
            {
                z = 1 + floating.Max(f => f.Z);
            }
            floating.Add(new FloatingPanel(p, CascadeRect(index), z, null));
            index++;
        }

        // SPEC-ASSUMPTION A11: no tile cap (excluded tile cap); every eligible panel is tiled.
        // SPEC-ASSUMPTION A7: the terminating desktop always has the default id.
        var built = Build(panels, 0, landscape, ids);
        var newRoot = LayoutNormalizer.Normalize(built);
        // SPEC-ASSUMPTION A2: hidden panels and the hidden desktop are carried over unchanged.
        return new LayoutDocument(newRoot, floating, document.Hidden, document.HiddenDesktop);
    }

    private static LayoutNode Build(List<PanelNode> panels, int i, bool horizontal, IIdGenerator ids)
    {
        if (i >= panels.Count)
        {
            return new DesktopNode();
        }
        string id = ids.Make(IdPrefixes.Split);
        var rest = Build(panels, i + 1, !horizontal, ids);
        return new SplitNode(id, horizontal ? Axis.X : Axis.Y,
            new[] { DwindleWeight, DwindleWeight }, new[] { (LayoutNode)panels[i], rest });
    }
}
