using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.LayoutTreeSpec.H;

namespace CanvasWm.Core.Tests.LayoutTreeSpec;

public sealed class ModelTests
{
    [Fact]
    public void AC1_Axis()
    {
        Assert.Equal("x", Axis.X.ToRaw());
        Assert.Equal("y", Axis.Y.ToRaw());
        Assert.Equal(Axis.Y, Axis.X.Cross());
        Assert.Equal(Axis.X, Axis.Y.Cross());
        Assert.True(AxisExtensions.TryParseRaw("x", out var a)); Assert.Equal(Axis.X, a);
        Assert.True(AxisExtensions.TryParseRaw("y", out a)); Assert.Equal(Axis.Y, a);
        Assert.False(AxisExtensions.TryParseRaw("X", out _));
        Assert.False(AxisExtensions.TryParseRaw("z", out _));
        Assert.False(AxisExtensions.TryParseRaw("", out _));
        Assert.False(AxisExtensions.TryParseRaw(null, out _));
    }

    [Fact]
    public void AC2_ForWindow()
    {
        var p = PanelNode.ForWindow("w1");
        Assert.Equal("w1", p.Id);
        Assert.Equal(new[] { "w1" }, p.Views.ToArray());
        Assert.Equal("w1", p.Selected);
        Assert.False(p.Tabbed);
        Assert.False(p.HasStrip);
        Assert.False(p.IsNativeGroup);
    }

    [Theory]
    [InlineData(2, true, true, false)]
    [InlineData(2, false, false, true)]
    [InlineData(1, true, false, false)]
    [InlineData(1, false, false, false)]
    [InlineData(0, true, false, false)]
    [InlineData(0, false, false, false)]
    public void AC3_TruthTable(int n, bool tabbed, bool strip, bool native)
    {
        var views = new[] { "a", "b" }.Take(n).ToArray();
        var p = new PanelNode("p", views, null, tabbed);
        Assert.Equal(strip, p.HasStrip);
        Assert.Equal(native, p.IsNativeGroup);
    }

    [Fact]
    public void AC4_Desktop()
    {
        var d = new DesktopNode();
        Assert.Equal("desktop", d.Id);
        Assert.Null(d.Child);
        Assert.True(d.IsEmptyDesktop);
        var a = P("a");
        Assert.False(new DesktopNode(a).IsEmptyDesktop);
        Assert.False(a.IsEmptyDesktop);
        Assert.False(X("x", (a, 1), (P("b"), 1)).IsEmptyDesktop);
    }

    private static SplitNode Tree(double w1 = 0.5, Axis ax = Axis.X, string id = "o", string view = "b", string? sel = "b", bool tabbed = false, string pid = "b") =>
        new(id, ax, new[] { w1, 0.5 }, new LayoutNode[]
        {
            P("a"),
            new SplitNode("i", Axis.Y, new[] { 1.0, double.NaN }, new LayoutNode[] { new PanelNode(pid, new[] { view }, sel, tabbed), P("c") }),
        });

    [Fact]
    public void AC5_Equality()
    {
        var t1 = Tree(); var t2 = Tree();
        Assert.Equal(t1, t2);
        Assert.Equal(t1.GetHashCode(), t2.GetHashCode());
        Assert.NotEqual(t1, Tree(w1: 0.4));
        Assert.NotEqual(t1, Tree(ax: Axis.Y));
        Assert.NotEqual(t1, Tree(id: "o2"));
        Assert.NotEqual(t1, Tree(view: "bb", sel: "bb", pid: "b"));
        Assert.NotEqual(t1, Tree(sel: null));
        Assert.NotEqual(t1, Tree(tabbed: true));
        // inner weight change
        var inner = new SplitNode("i", Axis.Y, new[] { 2.0, double.NaN }, new LayoutNode[] { P("b"), P("c") });
        Assert.NotEqual(t1, new SplitNode("o", Axis.X, new[] { 0.5, 0.5 }, new LayoutNode[] { P("a"), inner }));

        var fl = new FloatingPanel(P("f"), new Rect(1, 2, 3, 4), 5, new Size(6, 7));
        var d1 = new LayoutDocument(t1, new[] { fl });
        var d2 = new LayoutDocument(Tree(), new[] { new FloatingPanel(P("f"), new Rect(1, 2, 3, 4), 5, new Size(6, 7)) });
        Assert.Equal(d1, d2);
        Assert.Equal(d1.GetHashCode(), d2.GetHashCode());
        Assert.NotEqual(d1, new LayoutDocument(t1));

        var h1 = new HiddenDesktop("g", new[] { fl }, null, new[] { "n" }, 1.5);
        var h2 = new HiddenDesktop("g", new[] { fl }, null, new[] { "n" }, 1.5);
        Assert.Equal(h1, h2);
        Assert.Equal(h1.GetHashCode(), h2.GetHashCode());
        Assert.NotEqual(h1, new HiddenDesktop("g", new[] { fl }, null, new[] { "m" }, 1.5));
    }

    [Fact]
    public void AC6_Constructors()
    {
        Assert.Throws<ArgumentNullException>(() => PanelNode.ForWindow(null!));
        Assert.Throws<ArgumentNullException>(() => new PanelNode(null!, new[] { "a" }, null, false));
        Assert.Throws<ArgumentNullException>(() => new PanelNode("p", null!, null, false));
        Assert.Throws<ArgumentNullException>(() => new PanelNode("p", new string[] { null! }, null, false));
        Assert.Throws<ArgumentNullException>(() => new SplitNode(null!, Axis.X, new double[0], new LayoutNode[0]));
        Assert.Throws<ArgumentNullException>(() => new SplitNode("s", Axis.X, null!, new LayoutNode[0]));
        Assert.Throws<ArgumentNullException>(() => new SplitNode("s", Axis.X, new double[0], null!));
        Assert.Throws<ArgumentNullException>(() => new SplitNode("s", Axis.X, new double[0], new LayoutNode[] { null! }));
        Assert.Throws<ArgumentNullException>(() => new DesktopNode(null, null!));
        Assert.Throws<ArgumentNullException>(() => new FloatingPanel(null!, default, 0));
        Assert.Throws<ArgumentNullException>(() => new HiddenPanel(null!, new Dummy()));
        Assert.Throws<ArgumentNullException>(() => new HiddenPanel(P("a"), null!));
        Assert.Throws<ArgumentNullException>(() => new HiddenDesktop(null, null!, null, new string[0], null));
        Assert.Throws<ArgumentNullException>(() => new HiddenDesktop(null, new FloatingPanel[0], null, null!, null));

        var s = new SplitNode("s", Axis.X, new[] { double.NaN, -1 }, new LayoutNode[0]);
        Assert.Empty(s.Children);
        _ = new SplitNode("s", Axis.X, Array.Empty<double>(), new LayoutNode[] { P("a"), P("b") });
        _ = new PanelNode("p", Array.Empty<string>(), "zzz", true);

        var d = new LayoutDocument(null);
        Assert.Empty(d.Floating);
        Assert.Empty(d.Hidden);
        Assert.Null(d.HiddenDesktop);
        Assert.Null(LayoutDocument.Empty.Root);
        Assert.Empty(LayoutDocument.Empty.Floating);
    }

    private sealed record Dummy : RestoreTarget;
}
