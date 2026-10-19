using CanvasWm.Core.Layout;

namespace CanvasWm.Core.Tests.LayoutTreeSpec;

internal static class H
{
    public const double Tol = 1e-9;

    public static PanelNode P(string key) => PanelNode.ForWindow(key);

    public static PanelNode E(string id) => new(id, Array.Empty<string>(), null, false);

    public static SplitNode S(string id, Axis axis, params (LayoutNode Node, double Weight)[] c) =>
        new(id, axis, c.Select(x => x.Weight), c.Select(x => x.Node));

    public static SplitNode X(string id, params (LayoutNode, double)[] c) => S(id, Axis.X, c);

    public static SplitNode Y(string id, params (LayoutNode, double)[] c) => S(id, Axis.Y, c);

    public static void AssertWeights(LayoutNode? node, params double[] expected)
    {
        var s = Assert.IsType<SplitNode>(node);
        Assert.Equal(expected.Length, s.Weights.Length);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.True(Math.Abs(expected[i] - s.Weights[i]) <= Tol, $"weight[{i}] expected {expected[i]} actual {s.Weights[i]}");
        }
    }

    public static void AssertChildIds(LayoutNode? node, params string[] ids)
    {
        var s = Assert.IsType<SplitNode>(node);
        Assert.Equal(ids, s.Children.Select(c => c.Id).ToArray());
    }

    public static IEnumerable<LayoutNode> Walk(LayoutNode? n)
    {
        if (n is null) yield break;
        yield return n;
        if (n is SplitNode s)
            foreach (var c in s.Children)
                foreach (var d in Walk(c)) yield return d;
        if (n is DesktopNode d2 && d2.Child is not null)
            foreach (var d in Walk(d2.Child)) yield return d;
    }

    public static LayoutNode Clone(LayoutNode n) => n switch
    {
        SplitNode s => new SplitNode(s.Id, s.Axis, s.Weights.ToArray(), s.Children.Select(Clone).ToArray()),
        PanelNode p => new PanelNode(p.Id, p.Views.ToArray(), p.Selected, p.Tabbed),
        DesktopNode d => new DesktopNode(d.Child is null ? null : Clone(d.Child), d.Id),
        _ => throw new InvalidOperationException(),
    };

    public static LayoutNode Gen(Random r, ref int counter, int depth, bool allowDesktop = true)
    {
        var roll = r.NextDouble();
        if (depth <= 0 || roll < 0.3)
        {
            var id = counter++;
            if (r.NextDouble() < 0.2) return E("e" + id);
            if (allowDesktop && r.NextDouble() < 0.06) return new DesktopNode(null, "desk" + id);
            return P("w" + id);
        }
        if (allowDesktop && roll < 0.35)
        {
            var id = counter++;
            return new DesktopNode(Gen(r, ref counter, depth - 1, allowDesktop), "desk" + id);
        }
        var sid = "s" + counter++;
        var n = r.Next(0, 5);
        var kids = new List<LayoutNode>();
        var ws = new List<double>();
        for (var i = 0; i < n; i++)
        {
            kids.Add(Gen(r, ref counter, depth - 1, allowDesktop));
            ws.Add(r.Next(6) switch
            {
                0 => r.NextDouble() * 5 + 0.1,
                1 => 0,
                2 => -1,
                3 => double.NaN,
                4 => double.PositiveInfinity,
                _ => r.NextDouble() * 5 + 0.1,
            });
        }
        if (r.NextDouble() < 0.15 && ws.Count > 0) { var keep = r.Next(ws.Count); ws.RemoveRange(keep, ws.Count - keep); }
        if (r.NextDouble() < 0.05) ws.Add(2.5);
        return new SplitNode(sid, r.Next(2) == 0 ? Axis.X : Axis.Y, ws, kids);
    }
}
