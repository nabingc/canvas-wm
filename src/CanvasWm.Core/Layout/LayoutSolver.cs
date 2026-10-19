using System.Collections.Immutable;
using System.Text;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Layout;

/// <summary>Pure functions that turn a layout tree into an ordered table of unit-square rectangles.</summary>
public static class LayoutSolver
{
    /// <summary>Pin tolerance of Distribute: 1e-9.</summary>
    public const double PinTolerance = 1e-9;

    /// <summary>(0, 0, 1, 1): the rect the root is laid out in.</summary>
    public static Rect UnitRect { get; } = new Rect(0, 0, 1, 1);

    /// <summary>Lays out the tree: weights mode when metrics is null, minimum-aware mode
    /// otherwise. No range entries. Null root gives an empty table.</summary>
    /// <param name="root">Tree root or null.</param>
    /// <param name="metrics">Metrics for minimum-aware mode, or null for weights mode.</param>
    public static LayoutEntries LayoutRects(LayoutNode? root, LayoutMetrics? metrics = null)
    {
        if (root is null)
        {
            return LayoutEntries.Empty;
        }
        var builder = new LayoutEntries.Builder();
        if (metrics is null)
        {
            LayoutByWeights(root, UnitRect, null, null, builder);
        }
        else
        {
            LayoutWithMinimums(root, UnitRect, new Size(metrics.Width, metrics.Height), 1.0, null, metrics, builder);
        }
        return builder.Build();
    }

    /// <summary>LayoutRects plus range entries for every split with at least 3 children.</summary>
    /// <param name="root">Tree root or null.</param>
    /// <param name="metrics">Metrics for minimum-aware mode, or null for weights mode.</param>
    public static LayoutEntries FocusLayout(LayoutNode? root, LayoutMetrics? metrics = null)
    {
        if (root is null)
        {
            return LayoutEntries.Empty;
        }
        var builder = new LayoutEntries.Builder();
        if (metrics is null)
        {
            LayoutByWeights(root, UnitRect, null, null, builder);
        }
        else
        {
            LayoutWithMinimums(root, UnitRect, new Size(metrics.Width, metrics.Height), 1.0, null, metrics, builder);
        }

        foreach (var kv in builder.Snapshot())
        {
            if (kv.Value.Node is not SplitNode e || e.Children.Length < 3)
            {
                continue;
            }
            int n = e.Children.Length;
            var w = RawWeights(e);
            for (int s = 0; s <= n - 2; s++)
            {
                for (int end = s + 2; end <= n; end++)
                {
                    if (s == 0 && end == n)
                    {
                        continue;
                    }
                    AddRange(builder, e, w, s, end);
                }
            }
        }
        return builder.Build();
    }

    /// <summary>Iterative pinning: splits total among items in proportion to weights while
    /// giving every item at least its minimum where possible.</summary>
    /// <param name="total">Total to distribute.</param>
    /// <param name="weights">Proportions.</param>
    /// <param name="mins">Minimum sizes.</param>
    /// <exception cref="ArgumentNullException">weights or mins is null.</exception>
    /// <exception cref="ArgumentException">weights.Count != mins.Count.</exception>
    public static ImmutableArray<double> Distribute(double total, IReadOnlyList<double> weights, IReadOnlyList<double> mins)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentNullException.ThrowIfNull(mins);
        if (weights.Count != mins.Count)
        {
            throw new ArgumentException("weights and mins must have the same length.", nameof(mins));
        }
        int n = weights.Count;
        if (n == 0)
        {
            return ImmutableArray<double>.Empty;
        }
        var pinned = new bool[n];
        var sizes = new double[n];
        while (true)
        {
            double remaining = total;
            double sumW = 0;
            int free = 0;
            for (int i = 0; i < n; i++)
            {
                if (pinned[i])
                {
                    remaining -= mins[i];
                }
                else
                {
                    sumW += weights[i];
                    free++;
                }
            }
            bool anyViolator = false;
            var violators = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (pinned[i])
                {
                    sizes[i] = mins[i];
                    continue;
                }
                sizes[i] = sumW > 0 ? remaining * weights[i] / sumW : remaining / free;
                if (sizes[i] < mins[i] - PinTolerance)
                {
                    violators[i] = true;
                    anyViolator = true;
                }
            }
            if (!anyViolator)
            {
                return ImmutableArray.Create(sizes);
            }
            for (int i = 0; i < n; i++)
            {
                if (violators[i])
                {
                    pinned[i] = true;
                }
            }
        }
    }

    /// <summary>Id of a range entry: range:[ + quoted items joined by "," + ].</summary>
    /// <param name="splitId">Id of the split.</param>
    /// <param name="childIds">Ids of the run's children, in order.</param>
    /// <exception cref="ArgumentNullException">splitId, childIds or any child id is null.</exception>
    public static string RangeId(string splitId, IEnumerable<string> childIds)
    {
        ArgumentNullException.ThrowIfNull(splitId);
        ArgumentNullException.ThrowIfNull(childIds);
        var sb = new StringBuilder("range:[");
        AppendQuoted(sb, splitId);
        foreach (var id in childIds)
        {
            ArgumentNullException.ThrowIfNull(id, nameof(childIds));
            sb.Append(',');
            AppendQuoted(sb, id);
        }
        return sb.Append(']').ToString();
    }

    private static void AppendQuoted(StringBuilder sb, string item)
    {
        sb.Append('"').Append(item.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
    }

    // B5
    private static double[] RawWeights(SplitNode split)
    {
        var w = new double[split.Children.Length];
        for (int i = 0; i < w.Length; i++)
        {
            // SPEC-ASSUMPTION A4: missing weight -> 1.0, bad weight -> 0.001 (layout-tree Normalize rule).
            double v = i < split.Weights.Length ? split.Weights[i] : LayoutNormalizer.MissingWeight;
            if (!(v > 0 && double.IsFinite(v)))
            {
                v = LayoutNormalizer.BadWeightReplacement;
            }
            w[i] = v;
        }
        return w;
    }

    // B19
    private static void AddRange(LayoutEntries.Builder builder, SplitNode e, double[] w, int s, int end)
    {
        var run = e.Children.AsSpan(s, end - s).ToArray();
        string id = RangeId(e.Id, run.Select(c => c.Id));
        double sum = 0;
        for (int j = s; j < end; j++)
        {
            sum += w[j];
        }
        var r = new double[end - s];
        for (int j = s; j < end; j++)
        {
            r[j - s] = w[j] / sum;
        }
        var node = new SplitNode(id, e.Axis, r, run);
        var f = builder.TryGet(run[0].Id, out var fe) ? fe!.Rect : new Rect(0, 0, 0, 0);
        var l = builder.TryGet(run[^1].Id, out var le) ? le!.Rect : new Rect(0, 0, 0, 0);
        var rect = new Rect(f.X, f.Y, l.X + l.Width - f.X, l.Y + l.Height - f.Y);
        builder.Set(id, new LayoutEntry(node, rect, e.Id, null, true));
    }

    private static Rect ChildRect(Rect rect, Axis axis, double acc, double frac) =>
        axis == Axis.X
            ? new Rect(rect.X + rect.Width * acc, rect.Y, rect.Width * frac, rect.Height)
            : new Rect(rect.X, rect.Y + rect.Height * acc, rect.Width, rect.Height * frac);

    // B6-B9. scale is null in pure weights mode; the incoming scale for the B12.1 fallback.
    private static void LayoutByWeights(LayoutNode node, Rect rect, string? parent, double? scale, LayoutEntries.Builder builder)
    {
        builder.Set(node.Id, new LayoutEntry(node, rect, parent, scale, false));
        switch (node)
        {
            case DesktopNode { Child: { } child }:
                LayoutByWeights(child, rect, node.Id, scale, builder);
                break;
            case SplitNode split when split.Children.Length > 0:
                var w = RawWeights(split);
                double total = 0;
                foreach (var v in w)
                {
                    total += v;
                }
                double acc = 0;
                for (int i = 0; i < w.Length; i++)
                {
                    double f = w[i] / total;
                    LayoutByWeights(split.Children[i], ChildRect(rect, split.Axis, acc, f), split.Id, scale, builder);
                    acc += f;
                }
                break;
        }
    }

    private static double Clamp(double m) => double.IsFinite(m) && m > 0 ? m : 0;

    // B10-B15
    private static void LayoutWithMinimums(LayoutNode node, Rect rect, Size size, double scale, string? parent,
                                           LayoutMetrics metrics, LayoutEntries.Builder builder)
    {
        if (node is DesktopNode desk)
        {
            builder.Set(node.Id, new LayoutEntry(node, rect, parent, scale, false));
            if (desk.Child is not null)
            {
                LayoutWithMinimums(desk.Child, rect, size, scale, node.Id, metrics, builder);
            }
            return;
        }
        if (node is not SplitNode split)
        {
            builder.Set(node.Id, new LayoutEntry(node, rect, parent, scale, false));
            return;
        }

        int n = split.Children.Length;
        if (n == 0)
        {
            // SPEC-ASSUMPTION A8: empty split stores the original node and lays out nothing.
            builder.Set(node.Id, new LayoutEntry(node, rect, parent, scale, false));
            return;
        }
        if (size.Width <= 0 || size.Height <= 0)
        {
            // SPEC-ASSUMPTION A7: degenerate size falls back to weights-mode rects with the incoming scale.
            LayoutByWeights(node, rect, parent, scale, builder);
            return;
        }

        var along = split.Axis;
        var cross = along.Cross();
        var mA = new double[n];
        double sumA = 0, maxC = 0;
        for (int i = 0; i < n; i++)
        {
            // SPEC-ASSUMPTION A6: NaN, negative and infinite minimums are treated as 0.
            mA[i] = Clamp(metrics.Min(split.Children[i], along));
            double mC = Clamp(metrics.Min(split.Children[i], cross));
            sumA += mA[i];
            maxC = Math.Max(maxC, mC);
        }
        double alongPx = along == Axis.X ? size.Width : size.Height;
        double crossPx = along == Axis.X ? size.Height : size.Width;
        double k = 1;
        if (sumA > 0)
        {
            k = Math.Min(k, alongPx / sumA);
        }
        if (maxC > 0)
        {
            k = Math.Min(k, crossPx / maxC);
        }
        if (k < 1)
        {
            alongPx /= k;
            crossPx /= k;
        }

        var w = RawWeights(split);
        var sizes = Distribute(alongPx, w, mA);
        var eff = new double[n];
        for (int i = 0; i < n; i++)
        {
            eff[i] = sizes[i] / alongPx;
        }
        var entryNode = new SplitNode(split.Id, split.Axis, eff, split.Children);
        builder.Set(split.Id, new LayoutEntry(entryNode, rect, parent, scale, false));

        double acc = 0;
        for (int i = 0; i < n; i++)
        {
            var childSize = along == Axis.X ? new Size(sizes[i], crossPx) : new Size(crossPx, sizes[i]);
            LayoutWithMinimums(split.Children[i], ChildRect(rect, along, acc, eff[i]), childSize, scale * k,
                               split.Id, metrics, builder);
            acc += eff[i];
        }
    }
}
