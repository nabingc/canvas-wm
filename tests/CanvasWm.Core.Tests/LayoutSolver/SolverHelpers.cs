using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;

namespace CanvasWm.Core.Tests.SolverSpec;

internal static class T
{
    public const double Tol = 1e-12;

    public static PanelNode P(string key) => PanelNode.ForWindow(key);

    public static SplitNode X(string id, params (LayoutNode, double)[] c) =>
        new(id, Axis.X, c.Select(x => x.Item2), c.Select(x => x.Item1));

    public static SplitNode Y(string id, params (LayoutNode, double)[] c) =>
        new(id, Axis.Y, c.Select(x => x.Item2), c.Select(x => x.Item1));

    public static SplitNode Even(string id, Axis axis, params string[] letters) =>
        new(id, axis, letters.Select(_ => 1.0 / letters.Length), letters.Select(l => (LayoutNode)P(l)));

    public static void R(LayoutEntries e, string id, double x, double y, double w, double h, double tol = Tol)
    {
        Assert.True(e.ContainsKey(id), $"missing {id}");
        var r = e[id].Rect;
        Assert.True(Math.Abs(r.X - x) <= tol && Math.Abs(r.Y - y) <= tol
            && Math.Abs(r.Width - w) <= tol && Math.Abs(r.Height - h) <= tol,
            $"{id}: expected ({x},{y},{w},{h}) actual ({r.X},{r.Y},{r.Width},{r.Height})");
    }

    public static void Scale(LayoutEntries e, string id, double expected, double tol = Tol)
    {
        var s = e[id].Scale;
        Assert.NotNull(s);
        Assert.True(Math.Abs(s!.Value - expected) <= tol, $"{id} scale expected {expected} actual {s}");
    }

    public static void Near(double expected, double actual, double tol = Tol) =>
        Assert.True(Math.Abs(expected - actual) <= tol, $"expected {expected} actual {actual}");

    public static void Weights(LayoutNode n, params double[] w)
    {
        var s = Assert.IsType<SplitNode>(n);
        Assert.Equal(w.Length, s.Weights.Length);
        for (var i = 0; i < w.Length; i++) Near(w[i], s.Weights[i]);
    }

    /// <summary>Metrics with table lookup; unlisted pairs return 0.</summary>
    public static LayoutMetrics M(double w, double h, params (string Id, Axis Axis, double Min)[] table)
    {
        var d = table.ToDictionary(t => (t.Id, t.Axis), t => t.Min);
        return new LayoutMetrics(w, h, (n, a) => d.TryGetValue((n.Id, a), out var v) ? v : 0);
    }

    public static string[] Keys(LayoutEntries e) => e.Keys.ToArray();

    public static LayoutNode Clone(LayoutNode n) => n switch
    {
        SplitNode s => new SplitNode(s.Id, s.Axis, s.Weights.ToArray(), s.Children.Select(Clone).ToArray()),
        PanelNode p => new PanelNode(p.Id, p.Views.ToArray(), p.Selected, p.Tabbed),
        DesktopNode d => new DesktopNode(d.Child is null ? null : Clone(d.Child), d.Id),
        _ => throw new InvalidOperationException(),
    };
}
