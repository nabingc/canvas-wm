using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Tests.DockingSpec;
using static CanvasWm.Core.Tests.DockingSpec.T;

namespace CanvasWm.Core.Tests.PlacementSpec;

public class PlaceNewTests
{
    private static PlacementOutcome Run(LayoutDocument d, string key, string framed, string? focus, double a,
        SequenceIdGenerator g, bool share = true)
    {
        var snap = CloneDoc(d);
        var o = Placement.PlaceNew(d, key, framed, focus, a, g, share);
        Assert.Equal(snap, d);
        return o;
    }

    private static SequenceIdGenerator G() => new();

    [Fact]
    public void AC8_EmptyDesktop()
    {
        var g = G();
        var o = Run(Doc(D()), "w", "desktop", null, 1.6, g);
        Assert.Equal(PlacementKind.Docked, o.Kind);
        Assert.Equal("desktop", o.AnchorId);
        Assert.Equal(Edge.Right, o.Edge);
        AssertTree(X("n1", (D(), .5), (P("w"), .5)), o.Document.Root);
        Assert.Equal(1, g.Calls);
        Assert.All(g.Prefixes, p => Assert.Equal(IdPrefixes.Split, p));

        g = G();
        o = Run(Doc(D()), "w", "desktop", null, 0.75, g);
        Assert.Equal(Edge.Bottom, o.Edge);
        AssertTree(Y("n1", (D(), .5), (P("w"), .5)), o.Document.Root);
        Assert.Equal(1, g.Calls);
    }

    [Fact]
    public void AC9_AnchorWithDesktop()
    {
        foreach (var focus in new string?[] { "a", null })
        {
            var o = Run(Doc(X("s", (P("a"), .5), (D(), .5))), "b", "s", focus, 1.6, G());
            Assert.Equal(Edge.Bottom, o.Edge);
            Assert.Equal("a", o.AnchorId);
            AssertTree(X("s", (Y("n1", (P("a"), .5), (P("b"), .5)), .5), (D(), .5)), o.Document.Root);
        }
    }

    [Fact]
    public void AC10_Chain()
    {
        var g = G();
        var o = Run(Doc(X("s", (P("a"), .5), (P("b"), .5))), "c", "s", "b", 1.6, g);
        AssertTree(X("s", (P("a"), .5), (Y("n1", (P("b"), .5), (P("c"), .5)), .5)), o.Document.Root);
        var o2 = Run(o.Document, "d", "s", "c", 1.6, g);
        Assert.Equal(Edge.Right, o2.Edge);
        AssertTree(X("s", (P("a"), .5), (Y("n1", (P("b"), .5), (X("n2", (P("c"), .5), (P("d"), .5)), .5)), .5)),
            o2.Document.Root);
    }

    [Fact]
    public void AC11_FramedLeafVsSplit()
    {
        var doc = Doc(X("s", (P("a"), .5), (P("b"), .5)));
        var o = Run(doc, "c", "s", null, 1.6, G());
        AssertTree(X("s", (P("a"), .5), (Y("n1", (P("b"), .5), (P("c"), .5)), .5)), o.Document.Root);
        o = Run(doc, "c", "b", null, 1.6, G());
        Assert.Equal(Edge.Right, o.Edge);
        AssertTree(X("s", (P("a"), .5), (P("b"), .25), (P("c"), .25)), o.Document.Root);
        Assert.DoesNotContain(Walk(o.Document.Root), n => n.Id == "n1" && n is SplitNode);
    }

    [Fact]
    public void AC12_NestedFrame()
    {
        var doc = Doc(X("s", (P("a"), .5), (Y("t", (P("b"), .5), (P("c"), .5)), .5)));
        var o = Run(doc, "e", "t", "a", 1.6, G());
        Assert.Equal("c", o.AnchorId);
        Assert.Equal(Edge.Right, o.Edge);
        AssertTree(X("s", (P("a"), .5), (Y("t", (P("b"), .5), (X("n1", (P("c"), .5), (P("e"), .5)), .5)), .5)),
            o.Document.Root);
    }

    [Fact]
    public void AC13_FloatingFocusIgnored()
    {
        var f = Fl(P("f"), 3);
        var doc = new LayoutDocument(X("s", (P("a"), .5), (P("b"), .5)), new[] { f });
        var o = Run(doc, "c", "s", "f", 1.6, G());
        AssertTree(X("s", (P("a"), .5), (Y("n1", (P("b"), .5), (P("c"), .5)), .5)), o.Document.Root);
        Assert.Single(o.Document.Floating);
        Assert.Equal(f, o.Document.Floating[0]);
    }

    [Fact]
    public void AC14_ReplaceAnchorItself()
    {
        var g = G();
        var o = Run(Doc(X("s", (P("a"), .5), (P("b"), .5))), "a", "s", "a", 1.6, g);
        Assert.Equal(PlacementKind.Docked, o.Kind);
        Assert.Equal(Edge.Bottom, o.Edge);
        Assert.Equal("a", o.AnchorId);
        AssertTree(X("n2", (P("b"), .5), (P("a"), .5)), o.Document.Root);
        Assert.Equal(2, g.Calls);
    }

    [Fact]
    public void AC15_NeedsFloat()
    {
        var g = G();
        var d1 = Doc(X("s", (P("a"), .5), (P("b"), .5)));
        var o = Placement.PlaceNew(d1, "w", "zz", null, 1.6, g);
        Assert.Equal(PlacementKind.NeedsFloat, o.Kind);
        Assert.Same(d1, o.Document);
        Assert.Null(o.AnchorId);
        Assert.Null(o.Edge);

        o = Placement.PlaceNew(LayoutDocument.Empty, "w", "desktop", null, 1.6, g);
        Assert.Equal(PlacementKind.NeedsFloat, o.Kind);
        Assert.Same(LayoutDocument.Empty, o.Document);

        var d2 = new LayoutDocument(d1.Root, new[] { Fl(P("f")) });
        o = Placement.PlaceNew(d2, "w", "f", null, 1.6, g);
        Assert.Equal(PlacementKind.NeedsFloat, o.Kind);
        Assert.Same(d2, o.Document);
        Assert.Null(o.AnchorId);
        Assert.Equal(0, g.Calls);
    }

    [Fact]
    public void AC16_ExistingFloatingWindow()
    {
        var doc = new LayoutDocument(X("s", (P("a"), .5), (P("b"), .5)), new[] { Fl(P("w"), 2) });
        var o = Run(doc, "w", "s", null, 1.6, G());
        AssertTree(X("s", (P("a"), .5), (Y("n1", (P("b"), .5), (P("w"), .5)), .5)), o.Document.Root);
        Assert.Empty(o.Document.Floating);

        var tab = new PanelNode("p", new[] { "x", "y" }, "y", true);
        var fl = new FloatingPanel(tab, new Rect(0, 0, 10, 10), 1);
        var doc2 = new LayoutDocument(X("s", (P("a"), .5), (P("b"), .5)), new[] { fl });
        var o2 = Run(doc2, "x", "s", null, 1.6, G());
        var panels = LayoutEdits.PanelsOf(o2.Document.Root);
        Assert.Contains(panels, p => ReferenceEquals(p, tab));
        var placed = panels.Single(p => p.Id == "p");
        Assert.Equal(new[] { "x", "y" }, placed.Views.ToArray());
        Assert.Equal("y", placed.Selected);
        Assert.True(placed.Tabbed);
        Assert.DoesNotContain(panels, p => p.Id == "x");
        Assert.Empty(o2.Document.Floating);
    }

    [Fact]
    public void AC17_PreRemovalMeasurement()
    {
        var doc = Doc(Y("s", (P("a"), .5), (X("t", (P("b"), .5), (P("c"), .5)), .5)));
        var o = Run(doc, "c", "s", "b", 1.6, G());
        Assert.Equal("b", o.AnchorId);
        Assert.Equal(Edge.Right, o.Edge);
        AssertTree(Y("s", (P("a"), .5), (X("n1", (P("b"), .5), (P("c"), .5)), .5)), o.Document.Root);
    }

    [Fact]
    public void AC18_ShareRemovedSpacePassedThrough()
    {
        var doc = Doc(X("s", (P("a"), .2), (P("b"), .3), (P("c"), .5)));
        var o = Run(doc, "b", "c", null, 0.5, G(), false);
        AssertTree(X("s", (P("a"), .2), (Y("n1", (P("c"), .5), (P("b"), .5)), .8)), o.Document.Root);
        o = Run(doc, "b", "c", null, 0.5, G(), true);
        AssertTree(X("s", (P("a"), 2.0 / 7), (Y("n1", (P("c"), .5), (P("b"), .5)), 5.0 / 7)), o.Document.Root);
    }

    [Fact]
    public void E1_NullArguments()
    {
        var d = Doc(D());
        var g = G();
        Assert.Throws<ArgumentNullException>(() => Placement.ChooseAnchor(null!, "x", null));
        Assert.Throws<ArgumentNullException>(() => Placement.ChooseAnchor(d, null!, null));
        Assert.Throws<ArgumentNullException>(() => Placement.PlaceNew(null!, "w", "desktop", null, 1, g));
        Assert.Throws<ArgumentNullException>(() => Placement.PlaceNew(d, null!, "desktop", null, 1, g));
        Assert.Throws<ArgumentNullException>(() => Placement.PlaceNew(d, "w", null!, null, 1, g));
        Assert.Throws<ArgumentNullException>(() => Placement.PlaceNew(d, "w", "desktop", null, 1, null!));
        Assert.Throws<ArgumentNullException>(() => new PlacementOutcome(PlacementKind.Docked, null!, null, null));
        Assert.Equal(0, g.Calls);
    }
}
