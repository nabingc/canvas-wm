using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.SolverSpec.T;

namespace CanvasWm.Core.Tests.SolverSpec;

public class RangeAndDistributeTests
{
    private static string Rg(string s, params string[] c) => LayoutSolver.RangeId(s, c);

    [Fact]
    public void AC8_ThreeChildren()
    {
        var s = X("s", (P("a"), 0.2), (P("b"), 0.3), (P("c"), 0.5));
        var e = LayoutSolver.FocusLayout(s);
        var ab = "range:[\"s\",\"a\",\"b\"]"; var bc = "range:[\"s\",\"b\",\"c\"]";
        Assert.Equal(new[] { "s", "a", "b", "c", ab, bc }, Keys(e));
        Assert.Equal(6, e.Count);
        var n = Assert.IsType<SplitNode>(e[ab].Node);
        Assert.Equal(ab, n.Id); Assert.Equal(Axis.X, n.Axis);
        Assert.Equal(new[] { "a", "b" }, n.Children.Select(c => c.Id).ToArray());
        Weights(n, 0.4, 0.6);
        R(e, ab, 0, 0, 0.5, 1);
        Assert.Equal("s", e[ab].Parent); Assert.True(e[ab].Virtual); Assert.Null(e[ab].Scale);
        n = Assert.IsType<SplitNode>(e[bc].Node);
        Assert.Equal(new[] { "b", "c" }, n.Children.Select(c => c.Id).ToArray());
        Weights(n, 0.375, 0.625);
        R(e, bc, 0.2, 0, 0.8, 1);
        Assert.False(e.ContainsKey("range:[\"s\",\"a\",\"b\",\"c\"]"));
    }

    [Fact]
    public void AC9_FourChildren()
    {
        var x = LayoutSolver.FocusLayout(Even("s", Axis.X, "a", "b", "c", "d"));
        var ranges = x.Keys.Where(k => k.StartsWith("range:")).ToArray();
        Assert.Equal(new[] { Rg("s", "a", "b"), Rg("s", "a", "b", "c"), Rg("s", "b", "c"), Rg("s", "b", "c", "d"), Rg("s", "c", "d") }, ranges);
        R(x, Rg("s", "a", "b", "c"), 0, 0, 0.75, 1);
        var y = LayoutSolver.FocusLayout(Even("s", Axis.Y, "a", "b", "c", "d"));
        R(y, Rg("s", "b", "c"), 0, 0.25, 1, 0.5);
    }

    [Fact]
    public void AC10_Counts()
    {
        foreach (var (n, r) in new[] { (3, 2), (4, 5), (5, 9) })
        {
            var e = LayoutSolver.FocusLayout(Even("s", Axis.X, Enumerable.Range(0, n).Select(i => "c" + i).ToArray()));
            Assert.Equal(r, e.Values.Count(v => v.Virtual));
            Assert.Equal(1 + n + r, e.Count);
        }
        var tree = X("s", (P("a"), 0.5), (Y("t", (P("b"), 1), (P("c"), 1), (P("d"), 1)), 0.5));
        var f = LayoutSolver.FocusLayout(tree);
        Assert.Equal(new[] { "s", "a", "t", "b", "c", "d" }, f.Keys.Take(6).ToArray());
        Assert.Equal(2, f.Values.Count(v => v.Virtual));
        Assert.All(f.Values.Skip(6), v => { Assert.True(v.Virtual); Assert.Equal("t", v.Parent); });
    }

    [Fact]
    public void AC11_RangeId()
    {
        Assert.Equal("range:[\"s\",\"a\\\"b\\\\c\"]", LayoutSolver.RangeId("s", new[] { "a\"b\\c" }));
        Assert.Equal("range:[\"split-k3\",\"a\",\"b\"]", LayoutSolver.RangeId("split-k3", new[] { "a", "b" }));
        Assert.Equal("range:[\"s\"]", LayoutSolver.RangeId("s", Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => LayoutSolver.RangeId(null!, new[] { "a" }));
        Assert.Throws<ArgumentNullException>(() => LayoutSolver.RangeId("s", null!));
        Assert.Throws<ArgumentNullException>(() => LayoutSolver.RangeId("s", new string[] { null! }));
    }

    private static void SameWithoutVirtual(LayoutNode tree, LayoutMetrics? m)
    {
        var f = LayoutSolver.FocusLayout(tree, m);
        var l = LayoutSolver.LayoutRects(tree, m);
        var fk = f.Where(kv => !kv.Value.Virtual).ToArray();
        Assert.Equal(l.Select(kv => kv.Key).ToArray(), fk.Select(kv => kv.Key).ToArray());
        Assert.Equal(l.Select(kv => kv.Value).ToArray(), fk.Select(kv => kv.Value).ToArray());
        Assert.DoesNotContain(l.Values, v => v.Virtual);
    }

    [Fact]
    public void AC12_LayoutRectsHasNoRanges()
    {
        var t8 = X("s", (P("a"), 0.2), (P("b"), 0.3), (P("c"), 0.5));
        Assert.Equal(new[] { "s", "a", "b", "c" }, Keys(LayoutSolver.LayoutRects(t8)));
        SameWithoutVirtual(t8, null);
        SameWithoutVirtual(Even("s", Axis.X, "a", "b", "c", "d"), null);
        SameWithoutVirtual(X("s", (P("a"), 0.5), (Y("t", (P("b"), 1), (P("c"), 1), (P("d"), 1)), 0.5)), null);
        var t22 = X("s", (P("a"), 0.6), (P("b"), 0.2), (P("c"), 0.2));
        SameWithoutVirtual(t22, null);
        SameWithoutVirtual(t22, M(1000, 600, ("b", Axis.X, 300), ("c", Axis.X, 300)));
        SameWithoutVirtual(t8, M(1000, 600));
    }

    [Fact]
    public void AC13_DistributeExamples()
    {
        Check(1000, new[] { .5, .5 }, new[] { 100.0, 100 }, 500, 500);
        Check(1000, new[] { .8, .2 }, new[] { 0.0, 300 }, 700, 300);
        Check(1000, new[] { .6, .2, .2 }, new[] { 0.0, 300, 300 }, 400, 300, 300);
        Check(1000, new[] { .5, .3, .2 }, new[] { 0.0, 250, 450 }, 300, 250, 450);
    }

    private static void Check(double total, double[] w, double[] m, params double[] exp)
    {
        var r = LayoutSolver.Distribute(total, w, m);
        Assert.Equal(exp.Length, r.Length);
        for (var i = 0; i < exp.Length; i++) Near(exp[i], r[i], 1e-9);
    }

    [Fact]
    public void AC14_DistributeEdges()
    {
        Check(500, new[] { .5, .5 }, new[] { 300.0, 300 }, 300, 300);
        Check(1000, new[] { .5, .5 }, new[] { 500.0000000005, 0 }, 500, 500);
        Check(100, new[] { 0.0, 0 }, new[] { 0.0, 0 }, 50, 50);
        Assert.Empty(LayoutSolver.Distribute(42, Array.Empty<double>(), Array.Empty<double>()));
        Assert.Equal(1e-9, LayoutSolver.PinTolerance);
    }

    [Fact]
    public void AC15_DistributeErrors()
    {
        Assert.Throws<ArgumentNullException>(() => LayoutSolver.Distribute(1, null!, new[] { 1.0 }));
        Assert.Throws<ArgumentNullException>(() => LayoutSolver.Distribute(1, new[] { 1.0 }, null!));
        Assert.Throws<ArgumentException>(() => LayoutSolver.Distribute(1, new[] { 1.0, 1 }, new[] { 0.0, 0, 0 }));
        var w = new[] { 0.8, 0.2 }; var m = new[] { 0.0, 300 };
        LayoutSolver.Distribute(1000, w, m);
        Assert.Equal(new[] { 0.8, 0.2 }, w); Assert.Equal(new[] { 0.0, 300 }, m);
    }
}
