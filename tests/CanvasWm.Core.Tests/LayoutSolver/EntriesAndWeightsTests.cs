using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.SolverSpec.T;

namespace CanvasWm.Core.Tests.SolverSpec;

public class EntriesAndWeightsTests
{
    [Fact]
    public void AC1_EmptyAndNull()
    {
        Assert.Empty(LayoutEntries.Empty);
        Assert.Empty(LayoutSolver.LayoutRects(null));
        Assert.Empty(LayoutSolver.FocusLayout(null));
        var m = M(100, 100);
        Assert.Empty(LayoutSolver.LayoutRects(null, m));
        Assert.Empty(LayoutSolver.FocusLayout(null, m));
        var e = LayoutEntries.Empty;
        Assert.False(e.TryGetValue("x", out _));
        Assert.Throws<KeyNotFoundException>(() => e["x"]);
        Assert.Throws<ArgumentNullException>(() => e[null!]);
        Assert.Throws<ArgumentNullException>(() => e.ContainsKey(null!));
        Assert.Throws<ArgumentNullException>(() => e.TryGetValue(null!, out _));
        Assert.Throws<ArgumentNullException>(() => new LayoutEntry(null!, new Rect(0, 0, 1, 1), null, null, false));
    }

    [Fact]
    public void AC2_TwoPanels()
    {
        var s = X("s", (P("a"), 0.5), (P("b"), 0.5));
        var e = LayoutSolver.FocusLayout(s);
        Assert.Equal(3, e.Count);
        R(e, "s", 0, 0, 1, 1); R(e, "a", 0, 0, 0.5, 1); R(e, "b", 0.5, 0, 0.5, 1);
        Assert.Null(e["s"].Parent);
        Assert.Equal("s", e["a"].Parent);
        Assert.Equal("s", e["b"].Parent);
        Assert.All(e.Values, v => { Assert.Null(v.Scale); Assert.False(v.Virtual); });
        Assert.Same(s, e["s"].Node);
    }

    [Fact]
    public void AC3_VerticalWeights()
    {
        var e = LayoutSolver.FocusLayout(Y("s", (P("a"), 1), (P("b"), 3)));
        R(e, "a", 0, 0, 1, 0.25); R(e, "b", 0, 0.25, 1, 0.75);
    }

    [Fact]
    public void AC4_NestedAndOrder()
    {
        var s = X("s", (P("a"), 0.25), (Y("t", (P("b"), 0.5), (P("c"), 0.5)), 0.75));
        var e = LayoutSolver.LayoutRects(s);
        R(e, "a", 0, 0, 0.25, 1); R(e, "t", 0.25, 0, 0.75, 1);
        R(e, "b", 0.25, 0, 0.75, 0.5); R(e, "c", 0.25, 0.5, 0.75, 0.5);
        Assert.Equal("s", e["t"].Parent); Assert.Equal("t", e["b"].Parent); Assert.Equal("t", e["c"].Parent);
        var exp = new[] { "s", "a", "t", "b", "c" };
        Assert.Equal(exp, Keys(e));
        Assert.Equal(exp, e.Values.Select(v => v.Node.Id).ToArray());
        Assert.Equal(exp, e.Select(kv => kv.Key).ToArray());
        Assert.Equal(exp, e.Select(kv => kv.Value.Node.Id).ToArray());
    }

    [Fact]
    public void AC5_DesktopAndLeaves()
    {
        var d = new DesktopNode(X("s", (P("a"), 0.5), (P("b"), 0.5)));
        var e = LayoutSolver.LayoutRects(d);
        R(e, "desktop", 0, 0, 1, 1); R(e, "s", 0, 0, 1, 1);
        Assert.Null(e["desktop"].Parent);
        Assert.Equal("desktop", e["s"].Parent);
        Assert.Equal("s", e["a"].Parent); Assert.Equal("s", e["b"].Parent);
        var e2 = LayoutSolver.LayoutRects(new DesktopNode());
        Assert.Single(e2); R(e2, "desktop", 0, 0, 1, 1);
        var e3 = LayoutSolver.LayoutRects(P("a"));
        Assert.Single(e3); R(e3, "a", 0, 0, 1, 1); Assert.Null(e3["a"].Parent);
    }

    [Fact]
    public void AC6_DuplicateIds()
    {
        var p1 = new PanelNode("p", new[] { "a" }, "a", false);
        var p2 = new PanelNode("p", new[] { "b" }, "b", false);
        var e = LayoutSolver.LayoutRects(X("s", (p1, 0.5), (p2, 0.5)));
        Assert.Equal(2, e.Count);
        Assert.Equal(new[] { "s", "p" }, Keys(e));
        Assert.Equal(p2, e["p"].Node);
        R(e, "p", 0.5, 0, 0.5, 1);
    }

    [Fact]
    public void AC7_RobustWeights()
    {
        var a = new SplitNode("s", Axis.X, new[] { 1.0 }, new LayoutNode[] { P("a"), P("b"), P("c") });
        var e = LayoutSolver.LayoutRects(a);
        R(e, "a", 0, 0, 1.0 / 3, 1); R(e, "b", 1.0 / 3, 0, 1.0 / 3, 1); R(e, "c", 2.0 / 3, 0, 1.0 / 3, 1);

        e = LayoutSolver.LayoutRects(X("s", (P("a"), 0), (P("b"), 1)));
        R(e, "a", 0, 0, 0.001 / 1.001, 1);

        var c = new SplitNode("s", Axis.X, new[] { double.NaN, 1, 2, 5 }, new LayoutNode[] { P("a"), P("b"), P("c") });
        e = LayoutSolver.LayoutRects(c);
        R(e, "a", 0, 0, 0.001 / 3.001, 1);
        R(e, "b", 0.001 / 3.001, 0, 1 / 3.001, 1);
        R(e, "c", 1.001 / 3.001, 0, 2 / 3.001, 1);

        e = LayoutSolver.LayoutRects(new SplitNode("s", Axis.X, Array.Empty<double>(), Array.Empty<LayoutNode>()));
        Assert.Single(e);
        e = LayoutSolver.LayoutRects(new SplitNode("s", Axis.X, new[] { 1.0 }, new LayoutNode[] { P("a") }));
        R(e, "a", 0, 0, 1, 1);
    }
}
