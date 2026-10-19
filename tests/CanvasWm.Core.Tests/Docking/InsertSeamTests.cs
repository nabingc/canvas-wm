using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.DockingSpec.T;
using static CanvasWm.Core.Tests.DockingSpec.Ops;

namespace CanvasWm.Core.Tests.DockingSpec;

public class InsertSeamTests
{
    private static SplitNode Ab() => X("s", (P("a"), .5), (P("b"), .5));
    private static SplitNode NestedTree() => X("s", (P("a"), .5), (Y("t", (P("b"), .5), (P("c"), .5)), .5));
    private static PanelNode N() => P("n");
    private static SplitSeam Sm(Axis ax, string[] leaves, string[] before) => new(ax, leaves, before);

    [Fact]
    public void AC13_InsertOnEachEdge()
    {
        AssertTree(X("n1", (P("a"), .5), (N(), .5)), Ins(P("a"), "a", N(), Edge.Right));
        AssertTree(X("n1", (N(), .5), (P("a"), .5)), Ins(P("a"), "a", N(), Edge.Left));
        AssertTree(Y("n1", (N(), .5), (P("a"), .5)), Ins(P("a"), "a", N(), Edge.Top));
        AssertTree(Y("n1", (P("a"), .5), (N(), .5)), Ins(P("a"), "a", N(), Edge.Bottom));
    }

    [Fact]
    public void AC14_FractionClamping()
    {
        AssertTree(X("n1", (P("a"), .9), (N(), .1)), Ins(P("a"), "a", N(), Edge.Right, "n1", 0.05));
        AssertTree(X("n1", (P("a"), .1), (N(), .9)), Ins(P("a"), "a", N(), Edge.Right, "n1", 0.95));
        AssertTree(X("n1", (P("a"), .5), (N(), .5)), Ins(P("a"), "a", N(), Edge.Right, "n1", double.NaN));
        AssertTree(X("n1", (P("a"), .1), (N(), .9)), Ins(P("a"), "a", N(), Edge.Right, "n1", double.PositiveInfinity));
        AssertTree(X("n1", (P("a"), .9), (N(), .1)), Ins(P("a"), "a", N(), Edge.Right, "n1", double.NegativeInfinity));
    }

    [Fact]
    public void AC15_InsertInSplit()
    {
        var r = Ins(Ab(), "b", N(), Edge.Right);
        AssertTree(X("s", (P("a"), .5), (P("b"), .25), (N(), .25)), r);
        Assert.DoesNotContain(Walk(r), n => n.Id == "n1");
        AssertTree(X("s", (P("a"), .5), (Y("n1", (P("b"), .5), (N(), .5)), .5)), Ins(Ab(), "b", N(), Edge.Bottom));
    }

    [Fact]
    public void AC16_InsertAtSplitTarget()
    {
        var r = Ins(Ab(), "s", N(), Edge.Right);
        AssertTree(X("n1", (P("a"), .25), (P("b"), .25), (N(), .5)), r);
        Assert.DoesNotContain(Walk(r), n => n.Id == "s");
        var t = X("s", (P("a"), 1.0 / 3), (P("b"), 1.0 / 3), (P("c"), 1.0 / 3));
        AssertTree(X("s", (P("a"), 1.0 / 3), (P("b"), 1.0 / 6), (N(), 1.0 / 6), (P("c"), 1.0 / 3)),
            Ins(t, "b", N(), Edge.Right));
    }

    [Fact]
    public void AC17_DesktopTargets()
    {
        AssertTree(Y("s", (P("a"), .6), (X("n1", (D(), .5), (N(), .5)), .4)),
            Ins(Y("s", (P("a"), .6), (D(), .4)), "desktop", N(), Edge.Right));
        AssertTree(D(X("n1", (P("a"), .5), (N(), .5))), Ins(D(P("a")), "a", N(), Edge.Right));
        AssertTree(X("n1", (N(), .5), (D(P("a")), .5)), Ins(D(P("a")), "desktop", N(), Edge.Left));
    }

    [Fact]
    public void AC18_MissingTargetAndEmptyTree()
    {
        var t = Ab();
        Assert.Same(t, Ins(t, "zz", N(), Edge.Right));
        var n = N();
        Assert.Same(n, Ins(null, "a", n, Edge.Right));
    }

    [Fact]
    public void AC19_DeepTarget()
    {
        AssertTree(X("s", (P("a"), .5), (Y("t", (P("b"), .5), (X("n1", (P("c"), .5), (N(), .5)), .5)), .5)),
            Ins(NestedTree(), "c", N(), Edge.Right));
        AssertTree(X("s", (P("a"), .5), (Y("t", (P("b"), .5), (P("c"), .25), (N(), .25)), .5)),
            Ins(NestedTree(), "c", N(), Edge.Bottom));
    }

    [Fact]
    public void AC20_SeamTarget()
    {
        var s = X("s", (P("a"), 1), (P("b"), 1), (P("c"), 1));
        Assert.Equal(new DockTargetSpec("s", Edge.Left, Sm(Axis.X, new[] { "a", "b", "c" }, new[] { "a" })),
            LayoutEdits.SeamTarget(s, 1));
        Assert.Empty(LayoutEdits.SeamTarget(s, 0).Seam!.Before);
        Assert.Equal(new[] { "a", "b", "c" }, LayoutEdits.SeamTarget(s, 3).Seam!.Before);
        Assert.Empty(LayoutEdits.SeamTarget(s, -1).Seam!.Before);
        Assert.Equal(new[] { "a", "b", "c" }, LayoutEdits.SeamTarget(s, 7).Seam!.Before);
        var y = Y("t", (P("a"), 1), (P("b"), 1));
        Assert.Equal(Edge.Top, LayoutEdits.SeamTarget(y, 1).Edge);
        Assert.Equal(Axis.Y, LayoutEdits.SeamTarget(y, 1).Seam!.Axis);
        var m = X("s", (P("a"), 1), (Y("t", (P("b"), 1), (P("c"), 1)), 1));
        var tg = LayoutEdits.SeamTarget(m, 1);
        Assert.Equal(new[] { "a", "b", "c" }, tg.Seam!.Leaves);
        Assert.Equal(new[] { "a" }, tg.Seam.Before);
    }

    [Fact]
    public void AC21_SeamBetweenTwo()
    {
        AssertTree(X("s", (P("a"), 1.0 / 3), (N(), 1.0 / 3), (P("b"), 1.0 / 3)),
            Seam(Ab(), Sm(Axis.X, new[] { "a", "b" }, new[] { "a" }), N()));
    }

    [Fact]
    public void AC22_SeamWithThreeLive()
    {
        var t = X("s", (P("a"), .5), (P("b"), .25), (P("c"), .25));
        AssertTree(X("s", (P("a"), .375), (N(), .25), (P("b"), .1875), (P("c"), .1875)),
            Seam(t, Sm(Axis.X, new[] { "a", "b", "c" }, new[] { "a" }), N()));
    }

    [Fact]
    public void AC23_SeamWithDeadLeaf()
    {
        var t = X("s", (P("b"), .6), (P("c"), .4));
        AssertTree(X("s", (P("b"), .4), (N(), 1.0 / 3), (P("c"), 4.0 / 15)),
            Seam(t, Sm(Axis.X, new[] { "a", "b", "c" }, new[] { "a", "b" }), N()));
    }

    [Fact]
    public void AC24_SingleLeafRoot()
    {
        AssertTree(X("n1", (N(), .5), (P("b"), .5)), Seam(P("b"), Sm(Axis.X, new[] { "a", "b" }, new[] { "a" }), N()));
        AssertTree(X("n1", (P("b"), .5), (N(), .5)), Seam(P("b"), Sm(Axis.X, new[] { "b", "c" }, new[] { "b" }), N()));
        AssertTree(Y("n1", (N(), .5), (P("b"), .5)), Seam(P("b"), Sm(Axis.Y, new[] { "a", "b" }, new[] { "a" }), N()));
    }

    [Fact]
    public void AC25_CrossAxis()
    {
        var t = Y("u", (P("b"), .5), (P("c"), .25), (P("d"), .25));
        var expected = Y("u",
            (X("n1", (N(), .5), (Y("n1-range", (P("b"), 2.0 / 3), (P("c"), 1.0 / 3)), .5)), .75),
            (P("d"), .25));
        AssertTree(expected, Seam(t, Sm(Axis.X, new[] { "a", "b", "c" }, new[] { "a" }), N()));
    }

    [Fact]
    public void AC26_CrossAxisWholeSplit()
    {
        var t = Y("u", (P("b"), .5), (P("c"), .5));
        var r = Seam(t, Sm(Axis.X, new[] { "b", "c" }, new[] { "b" }), N());
        AssertTree(X("n1", (Y("n1-range", (P("b"), .5), (P("c"), .5)), .5), (N(), .5)), r);
        Assert.DoesNotContain(Walk(r), n => n.Id == "u");
    }

    [Fact]
    public void AC27_Descend()
    {
        var seam = Sm(Axis.Y, new[] { "b", "c" }, new[] { "b" });
        var expected = X("s", (P("a"), .5), (Y("t", (P("b"), 1.0 / 3), (N(), 1.0 / 3), (P("c"), 1.0 / 3)), .5));
        AssertTree(expected, Seam(NestedTree(), seam, N()));
        AssertTree(D(expected), Seam(D(NestedTree()), seam, N()));
    }

    [Fact]
    public void AC28_OuterEdges()
    {
        AssertTree(X("s", (N(), 1.0 / 3), (P("a"), 1.0 / 3), (P("b"), 1.0 / 3)),
            Seam(Ab(), Sm(Axis.X, new[] { "a", "b" }, Array.Empty<string>()), N()));
        AssertTree(X("s", (P("a"), 1.0 / 3), (P("b"), 1.0 / 3), (N(), 1.0 / 3)),
            Seam(Ab(), Sm(Axis.X, new[] { "a", "b" }, new[] { "a", "b" }), N()));
    }

    [Fact]
    public void AC29_NoLiveLeaf()
    {
        var t = Ab();
        Assert.Same(t, Seam(t, Sm(Axis.X, new[] { "p", "q" }, new[] { "p" }), N()));
        Assert.Null(Seam(null, Sm(Axis.X, new[] { "p", "q" }, new[] { "p" }), N()));
    }
}
