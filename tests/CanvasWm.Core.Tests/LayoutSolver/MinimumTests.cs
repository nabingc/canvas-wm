using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.SolverSpec.T;

namespace CanvasWm.Core.Tests.SolverSpec;

public class MinimumTests
{
    private static SplitNode Two() => X("s", (P("a"), 0.5), (P("b"), 0.5));
    private static SplitNode Nested() => X("s", (P("a"), 0.5), (Y("t", (P("b"), 0.5), (P("c"), 0.5)), 0.5));

    [Fact]
    public void AC16_MinimumsFit()
    {
        var m = M(1000, 600, ("a", Axis.X, 300), ("b", Axis.X, 300), ("a", Axis.Y, 200), ("b", Axis.Y, 200));
        var e = LayoutSolver.LayoutRects(Two(), m);
        R(e, "a", 0, 0, 0.5, 1); R(e, "b", 0.5, 0, 0.5, 1);
        foreach (var id in new[] { "s", "a", "b" }) { Scale(e, id, 1.0); Assert.False(e[id].Virtual); }
    }

    [Fact]
    public void AC17_Redistribution()
    {
        var s = X("s", (P("a"), 0.8), (P("b"), 0.2));
        var copy = Clone(s);
        var e = LayoutSolver.LayoutRects(s, M(1000, 600, ("b", Axis.X, 300)));
        R(e, "a", 0, 0, 0.7, 1); R(e, "b", 0.7, 0, 0.3, 1);
        var n = Assert.IsType<SplitNode>(e["s"].Node);
        Assert.Equal("s", n.Id); Assert.Equal(Axis.X, n.Axis);
        Assert.Same(s.Children[0], n.Children[0]); Assert.Same(s.Children[1], n.Children[1]);
        Weights(n, 0.7, 0.3);
        Weights(s, 0.8, 0.2);
        Assert.Equal(copy, s);
    }

    [Fact]
    public void AC18_MainAxisOverflow()
    {
        var e = LayoutSolver.LayoutRects(Two(), M(1000, 600, ("a", Axis.X, 800), ("b", Axis.X, 800)));
        R(e, "a", 0, 0, 0.5, 1); R(e, "b", 0.5, 0, 0.5, 1);
        Scale(e, "s", 1.0); Scale(e, "a", 0.625); Scale(e, "b", 0.625);
    }

    [Fact]
    public void AC19_CrossOverflow()
    {
        var e = LayoutSolver.LayoutRects(Two(), M(1000, 600, ("a", Axis.Y, 900)));
        R(e, "a", 0, 0, 0.5, 1); R(e, "b", 0.5, 0, 0.5, 1);
        Scale(e, "a", 2.0 / 3); Scale(e, "b", 2.0 / 3);
    }

    [Fact]
    public void AC20_NestedUsesEnlargedSize()
    {
        var m = M(1000, 600, ("a", Axis.X, 100), ("a", Axis.Y, 100), ("t", Axis.X, 100), ("t", Axis.Y, 800),
            ("b", Axis.X, 100), ("c", Axis.X, 100), ("b", Axis.Y, 400), ("c", Axis.Y, 400));
        var e = LayoutSolver.LayoutRects(Nested(), m);
        Scale(e, "s", 1.0);
        R(e, "a", 0, 0, 0.5, 1); Scale(e, "a", 0.75);
        R(e, "t", 0.5, 0, 0.5, 1); Scale(e, "t", 0.75);
        R(e, "b", 0.5, 0, 0.5, 0.5); R(e, "c", 0.5, 0.5, 0.5, 0.5);
        Scale(e, "b", 0.75); Scale(e, "c", 0.75);
    }

    [Fact]
    public void AC21_Compounding()
    {
        var m = M(1000, 600, ("a", Axis.X, 1000), ("t", Axis.X, 1000), ("b", Axis.Y, 1000), ("c", Axis.Y, 1000));
        var e = LayoutSolver.LayoutRects(Nested(), m);
        Scale(e, "s", 1.0); Scale(e, "a", 0.5); Scale(e, "t", 0.5); Scale(e, "b", 0.3); Scale(e, "c", 0.3);
        R(e, "a", 0, 0, 0.5, 1); R(e, "t", 0.5, 0, 0.5, 1);
        R(e, "b", 0.5, 0, 0.5, 0.5); R(e, "c", 0.5, 0.5, 0.5, 0.5);
    }

    [Fact]
    public void AC22_FocusLayoutWithMinimums()
    {
        var t = X("s", (P("a"), 0.6), (P("b"), 0.2), (P("c"), 0.2));
        var e = LayoutSolver.FocusLayout(t, M(1000, 600, ("b", Axis.X, 300), ("c", Axis.X, 300)));
        R(e, "a", 0, 0, 0.4, 1); R(e, "b", 0.4, 0, 0.3, 1); R(e, "c", 0.7, 0, 0.3, 1);
        var ab = "range:[\"s\",\"a\",\"b\"]"; var bc = "range:[\"s\",\"b\",\"c\"]";
        Weights(e[ab].Node, 4.0 / 7, 3.0 / 7); R(e, ab, 0, 0, 0.7, 1);
        Weights(e[bc].Node, 0.5, 0.5); R(e, bc, 0.4, 0, 0.6, 1);
        foreach (var k in new[] { ab, bc }) { Assert.Null(e[k].Scale); Assert.True(e[k].Virtual); }
    }

    [Fact]
    public void AC23_MinCallContract()
    {
        var calls = new List<(string, Axis)>();
        var m = new LayoutMetrics(1000, 600, (n, a) => { calls.Add((n.Id, a)); return 0; });
        LayoutSolver.LayoutRects(X("s", (P("a"), 1), (Y("t", (P("b"), 1), (P("c"), 1)), 1)), m);
        var exp = new[] { "a", "t", "b", "c" }.SelectMany(i => new[] { (i, Axis.X), (i, Axis.Y) }).OrderBy(x => x.Item1).ThenBy(x => x.Item2);
        Assert.Equal(exp, calls.OrderBy(x => x.Item1).ThenBy(x => x.Item2));

        calls.Clear();
        LayoutSolver.LayoutRects(new DesktopNode(X("s", (P("a"), 1), (P("b"), 1))), m);
        Assert.Equal(4, calls.Count);
        Assert.Equal(new[] { ("a", Axis.X), ("a", Axis.Y), ("b", Axis.X), ("b", Axis.Y) },
            calls.OrderBy(x => x.Item1).ThenBy(x => x.Item2).ToArray());

        var w = LayoutSolver.FocusLayout(Nested());
        Assert.All(w.Values, v => Assert.Null(v.Scale));
    }

    [Fact]
    public void AC24_DesktopScale()
    {
        var e = LayoutSolver.LayoutRects(new DesktopNode(Two()), M(1000, 600, ("a", Axis.X, 1000), ("b", Axis.X, 1000)));
        Scale(e, "desktop", 1.0); Scale(e, "s", 1.0); Scale(e, "a", 0.5); Scale(e, "b", 0.5);
        R(e, "desktop", 0, 0, 1, 1); R(e, "s", 0, 0, 1, 1);
    }

    [Fact]
    [Trait("Assumption", "A6")]
    public void AC25_ClampingBadMinimums()
    {
        var d = new Dictionary<(string, Axis), double>
        {
            [("a", Axis.X)] = double.NaN, [("b", Axis.X)] = -50, [("a", Axis.Y)] = double.PositiveInfinity,
        };
        var m = new LayoutMetrics(1000, 600, (n, a) => d.TryGetValue((n.Id, a), out var v) ? v : 0);
        var e = LayoutSolver.LayoutRects(Two(), m);
        R(e, "a", 0, 0, 0.5, 1); R(e, "b", 0.5, 0, 0.5, 1);
        foreach (var id in new[] { "s", "a", "b" }) Scale(e, id, 1.0);
    }

    [Fact]
    [Trait("Assumption", "A7")]
    public void AC26_DegenerateSize()
    {
        var s = X("s", (P("a"), 0.25), (P("b"), 0.75));
        var called = false;
        var m = new LayoutMetrics(0, 600, (_, _) => { called = true; return 100; });
        var e = LayoutSolver.LayoutRects(s, m);
        R(e, "a", 0, 0, 0.25, 1); R(e, "b", 0.25, 0, 0.75, 1);
        foreach (var id in new[] { "s", "a", "b" }) Scale(e, id, 1.0);
        Assert.Same(s, e["s"].Node);
        Assert.False(called);
        Func<LayoutNode, Axis, double> f = (_, _) => 0;
        Assert.Throws<ArgumentOutOfRangeException>(() => new LayoutMetrics(-1, 1, f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LayoutMetrics(double.NaN, 1, f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LayoutMetrics(1, double.PositiveInfinity, f));
        Assert.Throws<ArgumentNullException>(() => new LayoutMetrics(1, 1, null!));
    }
}
