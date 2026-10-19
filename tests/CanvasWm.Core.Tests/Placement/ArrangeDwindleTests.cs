using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Tests.DockingSpec;
using static CanvasWm.Core.Tests.DockingSpec.T;

namespace CanvasWm.Core.Tests.PlacementSpec;

public class ArrangeDwindleTests
{
    private const double Tol12 = 1e-12;

    private static LayoutDocument Run(LayoutDocument d, string[] keys, bool landscape, SequenceIdGenerator g)
    {
        var snap = CloneDoc(d);
        var r = Placement.ArrangeDwindle(d, keys, landscape, g);
        Assert.Equal(snap, d);
        return r;
    }

    private static void AssertRect(Rect e, Rect a)
    {
        Assert.True(Math.Abs(e.X - a.X) < Tol && Math.Abs(e.Y - a.Y) < Tol
            && Math.Abs(e.Width - a.Width) < Tol && Math.Abs(e.Height - a.Height) < Tol, $"{e} vs {a}");
    }

    private static void AssertFloat(FloatingPanel f, string id, Rect r, int z)
    {
        Assert.Equal(id, f.Panel.Id);
        Assert.Equal(r, f.Rect);
        Assert.Equal(z, f.Z);
        Assert.Null(f.Size);
    }

    [Fact]
    public void AC19_NoKeys_AllFloat()
    {
        var g = new SequenceIdGenerator();
        var r = Run(Doc(X("s", (P("a"), .5), (P("b"), .5))), Array.Empty<string>(), true, g);
        AssertTree(D(), r.Root);
        Assert.Equal(2, r.Floating.Length);
        AssertFloat(r.Floating[0], "a", new Rect(.1, .1, .5, .55), 1);
        AssertFloat(r.Floating[1], "b", new Rect(.1 + .03, .1 + .04, .5, .55), 2);
        Assert.Equal(0, g.Calls);
    }

    [Fact]
    public void AC20_SinglePanel()
    {
        var g = new SequenceIdGenerator();
        var r = Run(Doc(P("a")), new[] { "a" }, true, g);
        AssertTree(X("n1", (P("a"), .5), (D(), .5)), r.Root);
        var e = LayoutSolver.LayoutRects(r.Root);
        AssertRect(new Rect(0, 0, .5, 1), e["a"].Rect);
        AssertRect(new Rect(.5, 0, .5, 1), e["desktop"].Rect);
        Assert.Equal(1, g.Calls);
    }

    [Fact]
    public void AC21_ThreePanels()
    {
        var third = 1.0 / 3;
        var doc = Doc(X("s", (P("a"), third), (P("b"), third), (P("c"), third)));
        var g = new SequenceIdGenerator();
        var r = Run(doc, new[] { "a", "b", "c" }, true, g);
        AssertTree(X("n1", (P("a"), .5), (Y("n2", (P("b"), .5), (X("n3", (P("c"), .5), (D(), .5)), .5)), .5)), r.Root);
        var e = LayoutSolver.LayoutRects(r.Root);
        AssertRect(new Rect(0, 0, .5, 1), e["a"].Rect);
        AssertRect(new Rect(.5, 0, .5, .5), e["b"].Rect);
        AssertRect(new Rect(.5, .5, .25, .5), e["c"].Rect);
        AssertRect(new Rect(.75, .5, .25, .5), e["desktop"].Rect);
        Assert.Equal(new[] { "n1", "n2", "n3" }, Walk(r.Root).OfType<SplitNode>().Select(s => s.Id).ToArray());
        Assert.Equal(3, g.Calls);
        Assert.All(g.Prefixes, p => Assert.Equal(IdPrefixes.Split, p));

        var r2 = Run(doc, new[] { "a", "b", "c" }, false, new SequenceIdGenerator());
        AssertTree(Y("n1", (P("a"), .5), (X("n2", (P("b"), .5), (Y("n3", (P("c"), .5), (D(), .5)), .5)), .5)), r2.Root);
    }

    [Fact]
    public void AC22_TabGroupMovesAsWhole()
    {
        var pan = new PanelNode("p", new[] { "a", "b" }, "a", true);
        var g = new SequenceIdGenerator();
        var r = Run(Doc(X("s", (pan, .5), (P("c"), .5))), new[] { "b", "c", "a" }, true, g);
        AssertTree(X("n1", (pan, .5), (Y("n2", (P("c"), .5), (D(), .5)), .5)), r.Root);
        var first = ((SplitNode)r.Root!).Children[0];
        Assert.Same(pan, first);
        Assert.Equal(2, g.Calls);
    }

    [Fact]
    public void AC23_HiddenDuplicatesUnknownDropped()
    {
        var hidden = new[] { Hd(P("h")) };
        var doc = new LayoutDocument(P("a"), null, hidden);
        var g = new SequenceIdGenerator();
        var r = Run(doc, new[] { "a", "a", "h", "z" }, true, g);
        AssertTree(X("n1", (P("a"), .5), (D(), .5)), r.Root);
        Assert.Equal(1, g.Calls);
        Assert.True(r.Hidden.SequenceEqual(doc.Hidden));
        Assert.Empty(r.Floating);
    }

    [Fact]
    public void AC24_LeftoversAndCascade()
    {
        var third = 1.0 / 3;
        var r = Run(Doc(X("s", (P("a"), third), (P("x"), third), (P("y"), third))), new[] { "a" }, true,
            new SequenceIdGenerator());
        AssertTree(X("n1", (P("a"), .5), (D(), .5)), r.Root);
        Assert.Equal(2, r.Floating.Length);
        AssertFloat(r.Floating[0], "x", new Rect(.1, .1, .5, .55), 1);
        AssertFloat(r.Floating[1], "y", new Rect(.13, .14, .5, .55), 2);

        var xs = new[] { .1, .13, .16, .19, .22, .25, .1, .13 };
        var ys = new[] { .1, .14, .18, .22, .26, .30, .1, .14 };
        for (var i = 0; i < 8; i++)
        {
            var c = Placement.CascadeRect(i);
            Assert.True(Math.Abs(c.X - xs[i]) < Tol12, $"x {i}");
            Assert.True(Math.Abs(c.Y - ys[i]) < Tol12, $"y {i}");
            Assert.Equal(.5, c.Width);
            Assert.Equal(.55, c.Height);
        }
        Assert.Equal(Placement.CascadeRect(5), Placement.CascadeRect(-1));
    }

    [Fact]
    public void AC25_KeptFloatsAndZ()
    {
        var g5 = new FloatingPanel(P("g"), new Rect(1, 2, 3, 4), 5);
        var q = new FloatingPanel(P("q"), new Rect(7, 8, 9, 10), 3, new Size(300, 200));
        var doc = new LayoutDocument(P("x"), new[] { g5, q });
        var r = Run(doc, new[] { "g" }, true, new SequenceIdGenerator());
        AssertTree(X("n1", (P("g"), .5), (D(), .5)), r.Root);
        Assert.Same(g5.Panel, ((SplitNode)r.Root!).Children[0]);
        Assert.Equal(2, r.Floating.Length);
        Assert.Equal(q, r.Floating[0]);
        AssertFloat(r.Floating[1], "x", new Rect(.1, .1, .5, .55), 4);
    }

    [Fact]
    public void AC26_SevenLeftoversAndNegativeZ()
    {
        var kids = new List<(LayoutNode, double)> { (P("k"), 1.0 / 8) };
        for (var i = 1; i <= 7; i++) kids.Add((P("l" + i), 1.0 / 8));
        var doc = Doc(X("s", kids.ToArray()));
        var r = Run(doc, new[] { "k" }, true, new SequenceIdGenerator());
        Assert.Equal(7, r.Floating.Length);
        Assert.Equal(Enumerable.Range(1, 7), r.Floating.Select(f => f.Z));
        AssertRect(new Rect(.1, .1, .5, .55), r.Floating[6].Rect);
        Assert.Equal(Placement.CascadeRect(6), r.Floating[6].Rect);

        var neg = new FloatingPanel(P("n"), new Rect(0, 0, 1, 1), -3);
        var doc2 = new LayoutDocument(doc.Root, new[] { neg });
        var r2 = Run(doc2, new[] { "k" }, true, new SequenceIdGenerator());
        Assert.Equal(neg, r2.Floating[0]);
        Assert.Equal(-2, r2.Floating[1].Z);
    }

    [Fact]
    public void AC27_HiddenDesktopAndDesktopIds()
    {
        var hd = new HiddenDesktop("grp", Array.Empty<FloatingPanel>(), null, new[] { "n" }, 0.3);
        var doc = new LayoutDocument(P("a"), null, null, hd);
        var r = Run(doc, new[] { "a" }, true, new SequenceIdGenerator());
        Assert.Equal(hd, r.HiddenDesktop);
        Assert.Same(hd, r.HiddenDesktop);

        var r2 = Run(Doc(X("s", (P("a"), .5), (new DesktopNode(null, "home"), .5))), new[] { "a" }, true,
            new SequenceIdGenerator());
        AssertTree(X("n1", (P("a"), .5), (D(), .5)), r2.Root);
        Assert.Equal("desktop", Walk(r2.Root).OfType<DesktopNode>().Single().Id);

        var r3 = Run(Doc(D(X("s", (P("a"), .5), (P("b"), .5)))), new[] { "b" }, true, new SequenceIdGenerator());
        AssertTree(X("n1", (P("b"), .5), (D(), .5)), r3.Root);
        Assert.Single(r3.Floating);
        AssertFloat(r3.Floating[0], "a", Placement.CascadeRect(0), 1);
    }

    [Fact]
    public void AC28_NullsAndUnchangedInputs()
    {
        var g = new SequenceIdGenerator();
        var d = Doc(P("a"));
        Assert.Throws<ArgumentNullException>(() => Placement.ArrangeDwindle(d, new string[] { "a", null! }, true, g));
        Assert.Throws<ArgumentNullException>(() => Placement.ArrangeDwindle(null!, new[] { "a" }, true, g));
        Assert.Throws<ArgumentNullException>(() => Placement.ArrangeDwindle(d, null!, true, g));
        Assert.Throws<ArgumentNullException>(() => Placement.ArrangeDwindle(d, new[] { "a" }, true, null!));
        Assert.Equal(0, g.Calls);
    }

    [Fact]
    public void E3_NullRootBuildsFromFloating()
    {
        var doc = new LayoutDocument(null, new[] { Fl(P("f"), 1) });
        var g = new SequenceIdGenerator();
        var r = Run(doc, new[] { "f" }, true, g);
        AssertTree(X("n1", (P("f"), .5), (D(), .5)), r.Root);
        var r2 = Run(LayoutDocument.Empty, new[] { "f" }, true, new SequenceIdGenerator());
        AssertTree(D(), r2.Root);
    }

    [Fact]
    public void AC30_NoTileLimitParameterOrConstant()
    {
        var m = typeof(Placement).GetMethod(nameof(Placement.ArrangeDwindle))!;
        Assert.Equal(4, m.GetParameters().Length);
        var names = typeof(Placement).GetFields().Select(f => f.Name.ToLowerInvariant());
        Assert.DoesNotContain(names, n => n.Contains("max") || n.Contains("cap") || n.Contains("limit"));
        var csproj = File.ReadAllText(FindUp("src/CanvasWm.Core/CanvasWm.Core.csproj"));
        Assert.DoesNotContain("PackageReference", csproj);
        Assert.DoesNotContain("ProjectReference", csproj);
    }

    private static string FindUp(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var p = Path.Combine(dir.FullName, rel);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(rel);
    }
}
