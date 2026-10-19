namespace CanvasWm.Core.Layout;

/// <summary>Bottom-up pass that restores tree invariants after structural edits.</summary>
public static class LayoutNormalizer
{
    /// <summary>Weight used by Normalize for a missing weight.</summary>
    public const double MissingWeight = 1.0;

    /// <summary>Replacement used by Normalize for a non-positive or non-finite weight.</summary>
    public const double BadWeightReplacement = 0.001;

    /// <summary>Per-weight tolerance of the no-change shortcut.</summary>
    public const double NoChangeTolerance = 1e-12;

    /// <summary>Restores tree invariants bottom-up. Returns null when the node should be removed.
    /// Returns the same instance when nothing changed.</summary>
    /// <param name="node">The node to normalise.</param>
    /// <exception cref="ArgumentNullException">node is null.</exception>
    public static LayoutNode? Normalize(LayoutNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node switch
        {
            PanelNode p => p.Views.Length == 0 ? null : p,
            DesktopNode d => NormalizeDesktop(d),
            SplitNode s => NormalizeSplit(s),
            _ => node,
        };
    }

    private static LayoutNode NormalizeDesktop(DesktopNode d)
    {
        if (d.Child is null)
        {
            return d;
        }
        var n = Normalize(d.Child);
        if (ReferenceEquals(n, d.Child))
        {
            return d;
        }
        return new DesktopNode(n, d.Id);
    }

    private static LayoutNode? NormalizeSplit(SplitNode s)
    {
        var nodes = new List<LayoutNode>();
        var weights = new List<double>();
        for (int i = 0; i < s.Children.Length; i++)
        {
            var n = Normalize(s.Children[i]);
            if (n is null)
            {
                continue;
            }
            // SPEC-ASSUMPTION A3: missing weights are 1.0; never an index error.
            double w = i < s.Weights.Length ? s.Weights[i] : MissingWeight;
            if (!(w > 0 && double.IsFinite(w)))
            {
                // SPEC-ASSUMPTION A4: Normalize uses 0.001 for bad weights (Sanitize uses 1.0).
                w = BadWeightReplacement;
            }
            if (n is SplitNode ns && ns.Axis == s.Axis)
            {
                double sum = 0;
                foreach (var cw in ns.Weights)
                {
                    sum += cw;
                }
                for (int j = 0; j < ns.Children.Length; j++)
                {
                    nodes.Add(ns.Children[j]);
                    weights.Add(w * ns.Weights[j] / sum);
                }
            }
            else
            {
                nodes.Add(n);
                weights.Add(w);
            }
        }

        if (nodes.Count == 0)
        {
            return null;
        }
        if (nodes.Count == 1)
        {
            return nodes[0];
        }

        double total = 0;
        foreach (var w in weights)
        {
            total += w;
        }
        if (!double.IsFinite(total))
        {
            // SPEC-ASSUMPTION A6: max-scaling guard for sums that overflow to infinity.
            double max = weights.Max();
            for (int i = 0; i < weights.Count; i++)
            {
                weights[i] /= max;
            }
            total = 0;
            foreach (var w in weights)
            {
                total += w;
            }
        }
        for (int i = 0; i < weights.Count; i++)
        {
            weights[i] /= total;
        }

        // SPEC-ASSUMPTION A3: a weights/children length mismatch is never "unchanged".
        bool same = nodes.Count == s.Children.Length && s.Weights.Length == s.Children.Length;
        for (int k = 0; same && k < nodes.Count; k++)
        {
            same = nodes[k].Equals(s.Children[k])
                && Math.Abs(weights[k] - s.Weights[k]) <= NoChangeTolerance;
        }
        return same ? s : new SplitNode(s.Id, s.Axis, weights, nodes);
    }
}
