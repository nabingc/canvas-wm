namespace CanvasWm.Core.Layout;

/// <summary>Whole-document repair pass.</summary>
public static class LayoutSanitizer
{
    /// <summary>Replacement used by Sanitize for a missing, non-positive or non-finite weight.</summary>
    public const double RepairWeight = 1.0;

    /// <summary>Whole-document repair: drops unknown and duplicate windows, duplicate panel ids
    /// and extra desktops, repairs weights, then normalises the tree.</summary>
    /// <param name="document">The document to repair.</param>
    /// <param name="isKnown">Caller's predicate: true if a window key still refers to a live window.</param>
    /// <exception cref="ArgumentNullException">document or isKnown is null.</exception>
    public static LayoutDocument Sanitize(LayoutDocument document, Func<string, bool> isKnown)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(isKnown);
        var ctx = new Context(isKnown);

        var root = document.Root is null ? null : ctx.Repair(document.Root);
        if (root is not null)
        {
            root = LayoutNormalizer.Normalize(root);
        }

        var floating = new List<FloatingPanel>();
        foreach (var f in document.Floating)
        {
            var p = ctx.FixPanel(f.Panel);
            if (p is not null)
            {
                floating.Add(f with { Panel = p });
            }
        }

        var hidden = new List<HiddenPanel>();
        foreach (var h in document.Hidden)
        {
            var p = ctx.FixPanel(h.Panel);
            if (p is not null)
            {
                hidden.Add(h with { Panel = p });
            }
        }

        // SPEC-ASSUMPTION A8: the hidden desktop record is copied unchanged.
        return new LayoutDocument(root, floating, hidden, document.HiddenDesktop);
    }

    private sealed class Context
    {
        private readonly Func<string, bool> _isKnown;
        private readonly HashSet<string> _seenPanels = new(StringComparer.Ordinal);
        private readonly HashSet<string> _seenKeys = new(StringComparer.Ordinal);
        private bool _desktopKept;

        public Context(Func<string, bool> isKnown) => _isKnown = isKnown;

        public PanelNode? FixPanel(PanelNode panel)
        {
            if (!_seenPanels.Add(panel.Id))
            {
                return null;
            }
            var kept = new List<string>();
            foreach (var v in panel.Views)
            {
                if (!_seenKeys.Contains(v) && _isKnown(v))
                {
                    _seenKeys.Add(v);
                    kept.Add(v);
                }
            }
            if (kept.Count == 0)
            {
                return null;
            }
            string? selected = panel.Selected is not null && kept.Contains(panel.Selected, StringComparer.Ordinal)
                ? panel.Selected
                : kept[0];
            return new PanelNode(panel.Id, kept, selected, panel.Tabbed);
        }

        public LayoutNode? Repair(LayoutNode node)
        {
            switch (node)
            {
                case PanelNode p:
                    return FixPanel(p);
                case DesktopNode d:
                    return RepairDesktop(d);
                case SplitNode s:
                    return RepairSplit(s);
                default:
                    return null;
            }
        }

        private LayoutNode? RepairDesktop(DesktopNode d)
        {
            if (!_desktopKept)
            {
                // SPEC-ASSUMPTION A7: flag is set before repairing the child.
                _desktopKept = true;
                var child = d.Child is null ? null : Repair(d.Child);
                if (child is DesktopNode)
                {
                    child = null;
                }
                return new DesktopNode(child, d.Id);
            }
            return d.Child is null ? null : Repair(d.Child);
        }

        private LayoutNode? RepairSplit(SplitNode s)
        {
            var nodes = new List<LayoutNode>();
            var weights = new List<double>();
            for (int i = 0; i < s.Children.Length; i++)
            {
                var n = Repair(s.Children[i]);
                if (n is null)
                {
                    continue;
                }
                double w = i < s.Weights.Length ? s.Weights[i] : RepairWeight;
                if (!(w > 0 && double.IsFinite(w)))
                {
                    // SPEC-ASSUMPTION A4: Sanitize repairs bad weights to 1.0 (Normalize uses 0.001).
                    w = RepairWeight;
                }
                nodes.Add(n);
                weights.Add(w);
            }
            if (nodes.Count == 0)
            {
                return null;
            }
            return LayoutNormalizer.Normalize(new SplitNode(s.Id, s.Axis, weights, nodes));
        }
    }
}
