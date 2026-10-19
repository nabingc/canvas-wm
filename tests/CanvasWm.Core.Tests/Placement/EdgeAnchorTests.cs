using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Tests.DockingSpec;
using static CanvasWm.Core.Tests.DockingSpec.T;

namespace CanvasWm.Core.Tests.PlacementSpec;

public class EdgeAnchorTests
{
    private static readonly Rect Unit = new(0, 0, 1, 1);

    [Fact]
    public void AC1_ChooseEdge_FindingsExamples()
    {
        Assert.Equal(Edge.Bottom, Placement.ChooseEdge(new Rect(0, 0, .5, 1), Unit, 1.6));
        Assert.Equal(Edge.Right, Placement.ChooseEdge(new Rect(.5, .5, .5, .5), Unit, 1.6));
    }

    [Fact]
    public void AC2_ChooseEdge_TieAndZoomed()
    {
        Assert.Equal(Edge.Right, Placement.ChooseEdge(Unit, Unit, 1));
        Assert.Equal(Edge.Bottom, Placement.ChooseEdge(Unit, Unit, 0.9));
        var a = new Rect(.5, .5, .5, .5);
        var f = new Rect(.5, 0, .5, 1);
        Assert.Equal(Edge.Right, Placement.ChooseEdge(a, f, 0.5));
        Assert.Equal(Edge.Bottom, Placement.ChooseEdge(a, f, 0.4));
    }

    [Fact]
    public void AC3_ChooseEdge_NonFiniteAndZero()
    {
        Assert.Equal(Edge.Bottom, Placement.ChooseEdge(Unit, Unit, double.NaN));
        Assert.Equal(Edge.Right, Placement.ChooseEdge(Unit, Unit, double.PositiveInfinity));
        Assert.Equal(Edge.Bottom, Placement.ChooseEdge(Unit, Unit, 0));
        var z = new Rect(0, 0, 1, 0);
        Assert.Equal(Edge.Bottom, Placement.ChooseEdge(z, z, 1.6));
        foreach (var asp in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1, 1e300 })
        {
            var e = Placement.ChooseEdge(z, Unit, asp);
            Assert.True(e is Edge.Right or Edge.Bottom);
        }
    }

    [Fact]
    public void AC4_ChooseAnchor_Basics()
    {
        var doc = Doc(X("s", (P("a"), .5), (D(), .5)));
        Assert.Equal("a", Placement.ChooseAnchor(doc, "s", "a"));
        Assert.Equal("a", Placement.ChooseAnchor(doc, "s", null));
        Assert.Equal("desktop", Placement.ChooseAnchor(Doc(D()), "desktop", null));
        Assert.Equal("a", Placement.ChooseAnchor(Doc(D(P("a"))), "desktop", null));
        Assert.Equal("b", Placement.ChooseAnchor(Doc(P("b")), "b", null));
    }

    [Fact]
    public void AC5_ChooseAnchor_Nested()
    {
        var doc = Doc(X("s", (P("a"), .5), (Y("t", (P("b"), .5), (P("c"), .5)), .5)));
        Assert.Equal("c", Placement.ChooseAnchor(doc, "t", "a"));
        Assert.Equal("b", Placement.ChooseAnchor(doc, "t", "b"));
        Assert.Equal("c", Placement.ChooseAnchor(doc, "s", null));
        Assert.Null(Placement.ChooseAnchor(doc, "zz", null));
        Assert.Null(Placement.ChooseAnchor(LayoutDocument.Empty, "desktop", null));
    }

    [Fact]
    public void AC6_ChooseAnchor_IgnoresNonTreeFocus()
    {
        var root = X("s", (P("a"), .5), (P("b"), .5));
        var withFloat = new LayoutDocument(root, new[] { Fl(P("f")) });
        Assert.Equal("b", Placement.ChooseAnchor(withFloat, "s", "f"));
        var withHidden = new LayoutDocument(root, null, new[] { Hd(P("f")) });
        Assert.Equal("b", Placement.ChooseAnchor(withHidden, "s", "f"));
        Assert.Equal("b", Placement.ChooseAnchor(Doc(root), "s", "q"));
        var tab = new PanelNode("p", new[] { "x", "y" }, "x", true);
        var tdoc = Doc(X("s", (tab, .5), (P("a"), .5)));
        Assert.Equal("p", Placement.ChooseAnchor(tdoc, "s", "y"));
    }

    [Fact]
    public void AC6_ChooseAnchor_FocusOutsideFramedIgnored()
    {
        var doc = Doc(X("s", (P("a"), .5), (Y("t", (P("b"), .5), (P("c"), .5)), .5)));
        Assert.Equal("c", Placement.ChooseAnchor(doc, "t", "a"));
    }

    [Fact]
    public void AC7_ChooseAnchor_SplitWithNoChildren()
    {
        var empty = new SplitNode("s", Axis.X, Array.Empty<double>(), Array.Empty<LayoutNode>());
        Assert.Equal("s", Placement.ChooseAnchor(Doc(empty), "s", null));
    }

    [Fact]
    public void E2_ChooseAnchor_RangeIdIsNull()
    {
        var doc = Doc(X("s", (P("a"), .5), (P("b"), .5)));
        var entries = LayoutSolver.LayoutRects(doc.Root);
        var ranges = entries.Where(kv => kv.Value.Virtual).Select(kv => kv.Key).ToList();
        foreach (var id in ranges)
        {
            Assert.Null(Placement.ChooseAnchor(doc, id, null));
            var o = Placement.PlaceNew(doc, "w", id, null, 1.6, new SequenceIdGenerator());
            Assert.Equal(PlacementKind.NeedsFloat, o.Kind);
        }
    }
}
