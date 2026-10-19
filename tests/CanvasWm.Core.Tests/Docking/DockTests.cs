using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.DockingSpec.T;
using static CanvasWm.Core.Tests.DockingSpec.Ops;

namespace CanvasWm.Core.Tests.DockingSpec;

public class DockTests
{
    private static SplitNode Ab() => X("s", (P("a"), .5), (P("b"), .5));
    private static SplitNode Abc2() => X("s", (P("a"), .2), (P("b"), .3), (P("c"), .5));

    [Fact]
    public void AC30_DockToSeam()
    {
        var s = X("s", (P("a"), .5), (P("b"), .25), (P("c"), .25));
        var g = new SequenceIdGenerator();
        var r = Dock(Doc(s), "a", LayoutEdits.SeamTarget(s, 2), g);
        AssertTree(X("s", (P("b"), 1.0 / 3), (P("a"), 1.0 / 3), (P("c"), 1.0 / 3)), r.Root);
        Assert.Equal(1, g.Calls);
    }

    [Fact]
    public void AC31_DockBesideSibling()
    {
        var g = new SequenceIdGenerator();
        var r = Dock(Doc(Ab()), "a", Beside("b", Edge.Right), g);
        AssertTree(X("n1", (P("b"), .5), (P("a"), .5)), r.Root);
        Assert.Equal(1, g.Calls);
        Assert.All(g.Prefixes, p => Assert.Equal(IdPrefixes.Split, p));
    }

    [Fact]
    public void AC32_SelfDockAndMissingTargetFallBack()
    {
        var g = new SequenceIdGenerator();
        var r = Dock(Doc(Ab()), "a", Beside("a", Edge.Right), g);
        AssertTree(X("n2", (P("b"), .5), (P("a"), .5)), r.Root);
        Assert.Equal(2, g.Calls);

        var g2 = new SequenceIdGenerator();
        var r2 = Dock(Doc(Ab()), "a", Beside("zz", Edge.Left), g2);
        AssertTree(X("n2", (P("b"), .5), (P("a"), .5)), r2.Root);
        Assert.Equal(2, g2.Calls);
    }

    [Fact]
    public void AC33_FreshWindowWithDeadSeam()
    {
        var g = new SequenceIdGenerator();
        var seamY = new DockTargetSpec("t", Edge.Top, new SplitSeam(Axis.Y, new[] { "p", "q" }, new[] { "p" }));
        var r = Dock(Doc(Ab()), "c", seamY, g);
        AssertTree(Y("n2", (Ab(), .5), (P("c"), .5)), r.Root);
        Assert.Equal(2, g.Calls);

        var g2 = new SequenceIdGenerator();
        var seamX = new DockTargetSpec("t", Edge.Top, new SplitSeam(Axis.X, new[] { "p", "q" }, new[] { "p" }));
        var r2 = Dock(Doc(Ab()), "c", seamX, g2);
        AssertTree(X("n2", (P("a"), .25), (P("b"), .25), (P("c"), .5)), r2.Root);
    }

    [Fact]
    public void AC34_EmptyDocument()
    {
        var g = new SequenceIdGenerator();
        var r = Dock(LayoutDocument.Empty, "w", Beside("x", Edge.Right), g);
        Assert.Equal(P("w"), r.Root);
        Assert.Equal(1, g.Calls);
    }

    [Fact]
    public void AC35_ShareVersusAdjacent()
    {
        var r1 = Dock(Doc(Abc2()), "b", Beside("c", Edge.Bottom), new SequenceIdGenerator(), false);
        AssertTree(X("s", (P("a"), .2), (Y("n1", (P("c"), .5), (P("b"), .5)), .8)), r1.Root);
        var r2 = Dock(Doc(Abc2()), "b", Beside("c", Edge.Bottom), new SequenceIdGenerator(), true);
        AssertTree(X("s", (P("a"), 2.0 / 7), (Y("n1", (P("c"), .5), (P("b"), .5)), 5.0 / 7)), r2.Root);
        var r3 = Dock(Doc(Abc2()), "b", Beside("c", Edge.Bottom), new SequenceIdGenerator());
        AssertTree(r2.Root, r3.Root);
    }

    [Fact]
    public void AC36_NeighbourAndCollapsedTarget()
    {
        var r = Dock(Doc(Abc2()), "a", Beside("b", Edge.Right), new SequenceIdGenerator());
        AssertTree(X("s", (P("b"), .1875), (P("a"), .1875), (P("c"), .625)), r.Root);

        var t = X("s", (P("a"), .5), (Y("t", (P("b"), .5), (P("c"), .5)), .5));
        var g = new SequenceIdGenerator();
        var r2 = Dock(Doc(t), "b", Beside("t", Edge.Right), g);
        AssertTree(X("n2", (P("a"), .25), (P("c"), .25), (P("b"), .5)), r2.Root);
        Assert.Equal(2, g.Calls);
    }

    [Fact]
    public void AC37_TabGroupMovesWhole()
    {
        var pp = new PanelNode("p", new[] { "x", "y" }, "y", true);
        var doc = Doc(X("s", (pp, .5), (P("a"), .5)));
        var r = Dock(doc, "x", Beside("a", Edge.Bottom), new SequenceIdGenerator());
        AssertTree(Y("n1", (P("a"), .5), (pp, .5)), r.Root);
        Assert.Same(pp, ((SplitNode)r.Root!).Children[1]);

        var f = new PanelNode("f", new[] { "y", "z" }, "y", true);
        var docF = new LayoutDocument(P("a"), new[] { Fl(f) });
        var rf = Dock(docF, "y", Beside("a", Edge.Right), new SequenceIdGenerator());
        AssertTree(X("n1", (P("a"), .5), (f, .5)), rf.Root);
        Assert.Same(f, ((SplitNode)rf.Root!).Children[1]);
        Assert.Empty(rf.Floating);

        var docH = new LayoutDocument(P("a"), null, new[] { Hd(f) });
        var rh = Dock(docH, "y", Beside("a", Edge.Right), new SequenceIdGenerator());
        AssertTree(X("n1", (P("a"), .5), (f, .5)), rh.Root);
        Assert.Empty(rh.Hidden);
    }

    [Fact]
    public void AC38_Desktop()
    {
        var r = Dock(Doc(D(P("a"))), "a", Beside("desktop", Edge.Right), new SequenceIdGenerator());
        AssertTree(X("n1", (D(), .5), (P("a"), .5)), r.Root);
        var r2 = Dock(Doc(D(Ab())), "a", Beside("b", Edge.Bottom), new SequenceIdGenerator());
        AssertTree(D(Y("n1", (P("b"), .5), (P("a"), .5))), r2.Root);
    }

    [Fact]
    public void AC39_DockUnit()
    {
        var hd = new HiddenDesktop(null, Array.Empty<FloatingPanel>(), null, Array.Empty<string>(), null);
        var fl = new[] { Fl(P("f")) };
        var hi = new[] { Hd(P("h")) };
        var doc = new LayoutDocument(Ab(), fl, hi, hd);

        var g = new SequenceIdGenerator();
        var r = Unit(doc, P("c"), Beside("b", Edge.Right), g);
        AssertTree(X("s", (P("a"), .5), (P("b"), .25), (P("c"), .25)), r.Root);
        Assert.Equal(1, g.Calls);
        Assert.Equal(doc.Floating, r.Floating);
        Assert.Equal(doc.Hidden, r.Hidden);
        Assert.Same(hd, r.HiddenDesktop);

        var g2 = new SequenceIdGenerator();
        var r2 = Unit(doc, P("c"), Beside("zz", Edge.Right), g2);
        AssertTree(X("n2", (P("a"), .25), (P("b"), .25), (P("c"), .5)), r2.Root);
        Assert.Equal(2, g2.Calls);

        var g3 = new SequenceIdGenerator();
        var r3 = Unit(new LayoutDocument(null, fl, hi, hd), P("c"), Beside("zz", Edge.Right), g3);
        Assert.Equal(P("c"), r3.Root);
        Assert.Equal(1, g3.Calls);
        Assert.Equal(fl, r3.Floating);
        Assert.Same(hd, r3.HiddenDesktop);
    }

    [Fact]
    public void AC40_NullArguments()
    {
        var doc = Doc(Ab());
        var g = new SequenceIdGenerator();
        var tg = Beside("a", Edge.Left);
        var seam = new SplitSeam(Axis.X, new[] { "a" }, new[] { "a" });
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.RemoveNode(Ab(), null!));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.RemovePanel(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.RemovePanel(doc, null!));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.InsertBeside(Ab(), null!, P("n"), Edge.Left, "i"));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.InsertBeside(Ab(), "a", null!, Edge.Left, "i"));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.InsertBeside(Ab(), "a", P("n"), Edge.Left, null!));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.SeamTarget(null!, 0));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.InsertAtSeam(Ab(), null!, P("n"), "i"));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.InsertAtSeam(Ab(), seam, null!, "i"));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.InsertAtSeam(Ab(), seam, P("n"), null!));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.FindPanelOfWindow(null!, "a"));
        Assert.Throws<ArgumentNullException>(() => LayoutEdits.FindPanelOfWindow(doc, null!));
        Assert.Throws<ArgumentNullException>(() => Docking.Dock(null!, "a", tg, g));
        Assert.Throws<ArgumentNullException>(() => Docking.Dock(doc, null!, tg, g));
        Assert.Throws<ArgumentNullException>(() => Docking.Dock(doc, "a", null!, g));
        Assert.Throws<ArgumentNullException>(() => Docking.Dock(doc, "a", tg, null!));
        Assert.Throws<ArgumentNullException>(() => Docking.DockUnit(null!, P("c"), tg, g));
        Assert.Throws<ArgumentNullException>(() => Docking.DockUnit(doc, null!, tg, g));
        Assert.Throws<ArgumentNullException>(() => Docking.DockUnit(doc, P("c"), null!, g));
        Assert.Throws<ArgumentNullException>(() => Docking.DockUnit(doc, P("c"), tg, null!));
    }
}
