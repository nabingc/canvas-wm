using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.LayoutTreeSpec.H;

namespace CanvasWm.Core.Tests.LayoutTreeSpec;

public sealed class SanitizeTests
{
    private static LayoutDocument San(LayoutNode? root, Func<string, bool>? known = null,
        IEnumerable<FloatingPanel>? fl = null, IEnumerable<HiddenPanel>? hid = null, HiddenDesktop? hd = null) =>
        LayoutSanitizer.Sanitize(new LayoutDocument(root, fl, hid, hd), known ?? (_ => true));

    private static PanelNode Pn(string id, string[] views, string? sel, bool tabbed = false) => new(id, views, sel, tabbed);

    [Fact]
    public void AC22_UnknownWindows()
    {
        var root = X("o",
            (Pn("p1", new[] { "b", "c" }, "b"), 0.5),
            (Pn("p2", new[] { "c", "d" }, "c"), 0.5));
        var r = San(root, k => k != "b").Root;
        AssertWeights(r, 0.5, 0.5);
        var s = (SplitNode)r!;
        var p1 = Assert.IsType<PanelNode>(s.Children[0]);
        var p2 = Assert.IsType<PanelNode>(s.Children[1]);
        Assert.Equal("p1", p1.Id); Assert.Equal(new[] { "c" }, p1.Views.ToArray()); Assert.Equal("c", p1.Selected);
        Assert.Equal("p2", p2.Id); Assert.Equal(new[] { "d" }, p2.Views.ToArray()); Assert.Equal("d", p2.Selected);
    }

    [Fact]
    public void AC23_DuplicatePanelIds()
    {
        var root = X("o", (Pn("p", new[] { "a" }, "a"), 1), (Pn("p", new[] { "b" }, "b"), 1));
        var r = San(root).Root;
        var p = Assert.IsType<PanelNode>(r);
        Assert.Equal(new[] { "a" }, p.Views.ToArray());
    }

    [Fact]
    public void AC24_Desktops()
    {
        var a = P("a"); var b = P("b");
        var r = San(X("o", (new DesktopNode(a, "d1"), 0.5), (new DesktopNode(b, "d2"), 0.5))).Root;
        AssertWeights(r, 0.5, 0.5);
        var s = (SplitNode)r!;
        var d = Assert.IsType<DesktopNode>(s.Children[0]);
        Assert.Equal("d1", d.Id); Assert.Equal(a, d.Child);
        Assert.Equal(b, s.Children[1]);

        var r2 = San(X("o", (new DesktopNode(a, "d1"), 0.5), (new DesktopNode(null, "d2"), 0.5))).Root;
        var d2 = Assert.IsType<DesktopNode>(r2);
        Assert.Equal("d1", d2.Id); Assert.Equal(a, d2.Child);
    }

    [Fact]
    public void AC25_NestedDesktop()
    {
        var r = San(new DesktopNode(new DesktopNode(P("a"), "inner"), "outer")).Root;
        var d = Assert.IsType<DesktopNode>(r);
        Assert.Equal("outer", d.Id);
        Assert.Equal(P("a"), d.Child);
        // generator part is in NormalizeTests.AC21_SanitizeProperty_AlsoHolds
    }

    [Fact]
    public void AC26_Weights()
    {
        AssertWeights(San(X("x", (P("a"), 0), (P("b"), 1))).Root, 0.5, 0.5);
        var e = new SplitNode("x", Axis.X, Array.Empty<double>(), new LayoutNode[] { P("a"), P("b") });
        AssertWeights(San(e).Root, 0.5, 0.5);
        AssertWeights(San(X("x", (P("a"), double.NaN), (P("b"), 3))).Root, 0.25, 0.75);
    }

    [Fact]
    public void AC27_FloatingHidden()
    {
        var rect = new Rect(1, 2, 3, 4); var size = new Size(9, 8);
        var fl = new[]
        {
            new FloatingPanel(Pn("f", new[] { "a", "z" }, "a"), rect, 7, size),
            new FloatingPanel(Pn("tp", new[] { "q" }, "q"), rect, 1),
        };
        var hid = new[] { new HiddenPanel(Pn("h", new[] { "a" }, "a"), new Rt()) };
        var root = X("x", (Pn("tp", new[] { "a" }, "a"), 1), (P("b"), 1));
        var d = San(root, null, fl, hid);
        Assert.Empty(d.Hidden);
        var f = Assert.Single(d.Floating);
        Assert.Equal("f", f.Panel.Id);
        Assert.Equal(new[] { "z" }, f.Panel.Views.ToArray());
        Assert.Equal("z", f.Panel.Selected);
        Assert.Equal(rect, f.Rect); Assert.Equal(7, f.Z); Assert.Equal(size, f.Size);
        var s = (SplitNode)d.Root!;
        Assert.Equal(new[] { "a" }, ((PanelNode)s.Children[0]).Views.ToArray());
    }

    [Fact]
    public void AC27_HiddenKeptWithRestoreUnchanged()
    {
        var rt = new Rt();
        var d = San(P("a"), null, null, new[] { new HiddenPanel(Pn("h", new[] { "x", "x" }, "x"), rt) });
        var h = Assert.Single(d.Hidden);
        Assert.Same(rt, h.Restore);
        Assert.Equal(new[] { "x" }, h.Panel.Views.ToArray());
    }

    [Fact]
    public void AC28_DuplicateKeyInPanel()
    {
        var r = San(Pn("p", new[] { "a", "a", "b" }, null, true)).Root;
        var p = Assert.IsType<PanelNode>(r);
        Assert.Equal(new[] { "a", "b" }, p.Views.ToArray());
        Assert.Equal("a", p.Selected);
        Assert.True(p.Tabbed);
    }

    [Fact]
    public void AC29_HiddenDesktopUntouched()
    {
        var hd = new HiddenDesktop("g",
            new[] { new FloatingPanel(Pn("fx", new[] { "gone" }, "gone"), default, 0) }, null, new[] { "n" }, 2);
        var d = San(null, k => k != "gone", null, null, hd);
        Assert.Null(d.Root);
        Assert.True(ReferenceEquals(hd, d.HiddenDesktop) || hd.Equals(d.HiddenDesktop));
        Assert.Equal(hd, d.HiddenDesktop);
    }

    [Fact]
    public void AC30_Nulls()
    {
        Assert.Throws<ArgumentNullException>(() => LayoutSanitizer.Sanitize(null!, _ => true));
        Assert.Throws<ArgumentNullException>(() => LayoutSanitizer.Sanitize(LayoutDocument.Empty, null!));
    }

    private sealed record Rt : RestoreTarget;
}
