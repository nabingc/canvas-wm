using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.DockingSpec.T;
using static CanvasWm.Core.Tests.DockingSpec.Ops;

namespace CanvasWm.Core.Tests.DockingSpec;

public class ModelAndRemoveTests
{
    private static SplitNode Abc() => X("s", (P("a"), .2), (P("b"), .3), (P("c"), .5));
    private static SplitNode NestedTree() => X("s", (P("a"), .5), (Y("t", (P("b"), .5), (P("c"), .5)), .5));

    [Fact]
    public void AC1_EdgeValuesAxisAndLeading()
    {
        Assert.Equal(0, (int)Edge.Left);
        Assert.Equal(1, (int)Edge.Right);
        Assert.Equal(2, (int)Edge.Top);
        Assert.Equal(3, (int)Edge.Bottom);
        Assert.Equal(new[] { Axis.X, Axis.X, Axis.Y, Axis.Y },
            new[] { Edge.Left, Edge.Right, Edge.Top, Edge.Bottom }.Select(e => e.ToAxis()).ToArray());
        Assert.Equal(new[] { true, false, true, false },
            new[] { Edge.Left, Edge.Right, Edge.Top, Edge.Bottom }.Select(e => e.IsLeading()).ToArray());
    }

    [Fact]
    public void AC2_SplitSeamEqualityAndNulls()
    {
        var a = new SplitSeam(Axis.X, new[] { "a", "b" }, new[] { "a" });
        var b = new SplitSeam(Axis.X, new[] { "a", "b" }, new[] { "a" });
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, new SplitSeam(Axis.Y, new[] { "a", "b" }, new[] { "a" }));
        Assert.NotEqual(a, new SplitSeam(Axis.X, new[] { "a", "c" }, new[] { "a" }));
        Assert.NotEqual(a, new SplitSeam(Axis.X, new[] { "b", "a" }, new[] { "a" }));
        Assert.NotEqual(a, new SplitSeam(Axis.X, new[] { "a", "b" }, Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => new SplitSeam(Axis.X, null!, new[] { "a" }));
        Assert.Throws<ArgumentNullException>(() => new SplitSeam(Axis.X, new[] { "a" }, null!));
        Assert.Throws<ArgumentNullException>(() => new SplitSeam(Axis.X, new string[] { null! }, new[] { "a" }));
        Assert.Throws<ArgumentNullException>(() => new SplitSeam(Axis.X, new[] { "a" }, new string[] { null! }));
        Assert.Throws<ArgumentNullException>(() => new DockTargetSpec(null!, Edge.Left));
    }

    [Fact]
    public void AC3_LeafIdsAndPanelsOf()
    {
        Assert.Empty(LayoutEdits.LeafIds(null));
        Assert.Equal(new[] { "a" }, LayoutEdits.LeafIds(P("a")));
        Assert.Equal(new[] { "desktop" }, LayoutEdits.LeafIds(D()));
        Assert.Equal(new[] { "a", "b", "c" },
            LayoutEdits.LeafIds(D(X("s", (P("a"), .5), (Y("t", (P("b"), .5), (P("c"), .5)), .5)))));

        var a = P("a"); var b = P("b");
        var tree = X("s", (a, .5), (Y("t", (b, .5), (D(), .5)), .5));
        var ps = LayoutEdits.PanelsOf(tree);
        Assert.Equal(2, ps.Length);
        Assert.Same(a, ps[0]);
        Assert.Same(b, ps[1]);
        var a2 = P("a");
        Assert.Same(a2, Assert.Single(LayoutEdits.PanelsOf(D(a2))));
        Assert.Empty(LayoutEdits.PanelsOf(null));
    }

    [Fact]
    public void AC4_FindPanelOfWindow()
    {
        var p = new PanelNode("p", new[] { "x", "y" }, "x", true);
        var f = new PanelNode("f", new[] { "y", "z" }, "y", true);
        var h = new PanelNode("h", new[] { "z", "w" }, "z", false);
        var doc = new LayoutDocument(p, new[] { Fl(f) }, new[] { Hd(h) });
        Assert.Same(p, LayoutEdits.FindPanelOfWindow(doc, "y"));
        Assert.Same(f, LayoutEdits.FindPanelOfWindow(doc, "z"));
        Assert.Same(h, LayoutEdits.FindPanelOfWindow(doc, "w"));
        Assert.Null(LayoutEdits.FindPanelOfWindow(doc, "q"));
    }

    [Fact]
    public void AC5_RemoveAdjacent()
    {
        AssertTree(X("s", (P("a"), .2), (P("c"), .8)), Rm(Abc(), "b", false));
        AssertTree(X("s", (P("a"), .2), (P("b"), .8)), Rm(Abc(), "c", false));
        AssertTree(X("s", (P("b"), .5), (P("c"), .5)), Rm(Abc(), "a", false));
    }

    [Fact]
    public void AC6_RemoveSharing()
    {
        AssertTree(X("s", (P("a"), 2.0 / 7), (P("c"), 5.0 / 7)), Rm(Abc(), "b", true));
        AssertTree(X("s", (P("b"), .375), (P("c"), .625)), Rm(Abc(), "a", true));
        AssertTree(X("s", (P("a"), 2.0 / 7), (P("c"), 5.0 / 7)), Rm(Abc(), "b"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AC7_RemoveNestedLeavesCollapse(bool share)
    {
        var r = Rm(NestedTree(), "b", share);
        AssertTree(X("s", (P("a"), .5), (P("c"), .5)), r);
        Assert.DoesNotContain(Walk(r), n => n.Id == "t");
    }

    [Fact]
    public void AC8_DesktopAndRootCases()
    {
        AssertTree(D(), Rm(D(P("a")), "a"));
        var d = D(P("a"));
        Assert.Same(d, Rm(d, "desktop"));
        Assert.Null(Rm(P("a"), "a"));
        Assert.Null(Rm(null, "a"));
        AssertTree(D(), Rm(D(X("s", (P("a"), .5), (P("b"), .5))), "s"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AC9_RemoveSubtreeAndRoot(bool share)
    {
        AssertTree(P("a"), Rm(NestedTree(), "t", share));
        Assert.Null(Rm(NestedTree(), "s", share));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AC10_RemoveAbsentIsSameInstance(bool share)
    {
        var t = NestedTree();
        Assert.Same(t, Rm(t, "zz", share));
    }

    [Fact]
    public void AC11_VanishedNestedChild()
    {
        SplitNode T1() => X("s", (P("a"), .2), (Y("t", (P("b"), 1)), .3), (P("c"), .5));
        AssertTree(X("s", (P("a"), .5), (P("c"), .5)), Rm(T1(), "b", false));
        AssertTree(X("s", (P("a"), 2.0 / 7), (P("c"), 5.0 / 7)), Rm(T1(), "b", true));
        var t2 = X("s", (Y("t", (P("b"), 1)), .3), (P("a"), .2), (P("c"), .5));
        AssertTree(X("s", (P("a"), .5), (P("c"), .5)), Rm(t2, "b", false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AC12_RemovePanel(bool share)
    {
        var hd = new HiddenDesktop("g", Array.Empty<FloatingPanel>(), null, Array.Empty<string>(), 1.0);
        var doc = new LayoutDocument(Abc(), null, null, hd);
        var r = LayoutEdits.RemovePanel(doc, "b", share);
        AssertTree(share ? X("s", (P("a"), 2.0 / 7), (P("c"), 5.0 / 7)) : X("s", (P("a"), .2), (P("c"), .8)), r.Root);
        Assert.Same(hd, r.HiddenDesktop);

        var f1 = P("g1"); var f2 = P("f");
        var root = P("a");
        var docF = new LayoutDocument(root, new[] { Fl(f1, 1), Fl(f2, 2) }, null, hd);
        var rf = LayoutEdits.RemovePanel(docF, "f", share);
        Assert.Same(root, rf.Root);
        Assert.Equal(new[] { "g1" }, rf.Floating.Select(x => x.Panel.Id));
        Assert.Same(hd, rf.HiddenDesktop);

        var docH = new LayoutDocument(root, null, new[] { Hd(P("h1")), Hd(P("h2")) }, hd);
        var rh = LayoutEdits.RemovePanel(docH, "h1", share);
        Assert.Equal(new[] { "h2" }, rh.Hidden.Select(x => x.Panel.Id));
        Assert.Same(hd, rh.HiddenDesktop);

        Assert.Same(docH, LayoutEdits.RemovePanel(docH, "nope", share));
    }
}
