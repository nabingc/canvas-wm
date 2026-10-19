using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.LayoutTreeSpec.H;

namespace CanvasWm.Core.Tests.LayoutTreeSpec;

public sealed class NormalizeTests
{
    private const double Inf = double.PositiveInfinity;

    [Fact]
    public void AC7_MergeSameAxis()
    {
        var n = X("o", (P("a"), 0.5), (X("i", (P("b"), 0.5), (P("c"), 0.5)), 0.5));
        var r = LayoutNormalizer.Normalize(n);
        AssertWeights(r, 0.5, 0.25, 0.25);
        AssertChildIds(r, "a", "b", "c");
        Assert.Equal("o", r!.Id);
        Assert.DoesNotContain(Walk(r), x => x.Id == "i");
    }

    [Fact]
    public void AC8_MergeWeighted()
    {
        var r = LayoutNormalizer.Normalize(X("o", (P("a"), 0.3), (X("i", (P("b"), 1), (P("c"), 3)), 0.7)));
        AssertWeights(r, 0.3, 0.175, 0.525);
    }

    [Fact]
    public void AC9_SingleChildCollapse()
    {
        var r = LayoutNormalizer.Normalize(X("o", (P("a"), 0.5), (Y("y", (P("b"), 1)), 0.5)));
        AssertWeights(r, 0.5, 0.5);
        AssertChildIds(r, "a", "b");
    }

    [Fact]
    public void AC10_EmptyRemovedAndMerge()
    {
        var inner = X("i", (P("b"), 1), (P("c"), 1));
        var n = X("o", (Y("y", (E("e"), 0.5), (inner, 0.5)), 0.5), (P("a"), 0.5));
        var r = LayoutNormalizer.Normalize(n);
        AssertWeights(r, 0.25, 0.25, 0.5);
        AssertChildIds(r, "b", "c", "a");
        Assert.Equal("o", r!.Id);
    }

    [Fact]
    public void AC11_ZeroWeight()
    {
        var r = LayoutNormalizer.Normalize(Y("y", (P("a"), 2), (P("b"), 0)));
        AssertWeights(r, 2 / 2.001, 0.001 / 2.001);
        AssertWeights(r, 0.9995002499, 0.0004997501);
    }

    [Fact]
    public void AC12_ShortWeights()
    {
        var n = new SplitNode("x", Axis.X, new[] { 1.0 }, new LayoutNode[] { P("a"), P("b"), P("c") });
        var r = LayoutNormalizer.Normalize(n);
        AssertWeights(r, 1.0 / 3, 1.0 / 3, 1.0 / 3);
    }

    [Fact]
    public void AC13_BadWeights()
    {
        AssertWeights(LayoutNormalizer.Normalize(X("x", (P("a"), -1), (P("b"), double.NaN))), 0.5, 0.5);
        AssertWeights(LayoutNormalizer.Normalize(X("x", (P("a"), Inf), (P("b"), 1))), 0.001 / 1.001, 1 / 1.001);
    }

    [Fact]
    public void AC14_AllEmpty()
    {
        var n = X("x", (E("e"), 1), (E("e2"), 1));
        Assert.Null(LayoutNormalizer.Normalize(n));
        var d = LayoutSanitizer.Sanitize(new LayoutDocument(n), _ => true);
        Assert.Null(d.Root);
    }

    [Fact]
    public void AC15_DesktopEmptiedChild()
    {
        var r = LayoutNormalizer.Normalize(new DesktopNode(X("x", (E("e"), 1)), "dk"));
        var d = Assert.IsType<DesktopNode>(r);
        Assert.Equal("dk", d.Id);
        Assert.Null(d.Child);
    }

    [Fact]
    public void AC16_NoChangeShortcut()
    {
        var n = X("x", (P("a"), 0.5), (P("b"), 0.5));
        Assert.Same(n, LayoutNormalizer.Normalize(n));
        var n2 = X("x", (P("a"), 0.5), (P("b"), 0.5 + 1e-13));
        Assert.Same(n2, LayoutNormalizer.Normalize(n2));
        var n3 = X("x7", (P("a"), 0.5), (P("b"), 0.6));
        var r = LayoutNormalizer.Normalize(n3);
        Assert.NotSame(n3, r);
        Assert.Equal("x7", r!.Id);
        AssertWeights(r, 0.5 / 1.1, 0.6 / 1.1);
    }

    [Fact]
    public void AC17_SameInstances()
    {
        var y = Y("y", (P("a"), 0.4), (new DesktopNode(), 0.6));
        Assert.Same(y, LayoutNormalizer.Normalize(y));
        var d = new DesktopNode();
        Assert.Same(d, LayoutNormalizer.Normalize(d));
        var a = P("a");
        Assert.Same(a, LayoutNormalizer.Normalize(a));
        Assert.Null(LayoutNormalizer.Normalize(E("e")));
        var odd = new PanelNode("p", new[] { "a" }, "zzz", false);
        Assert.Same(odd, LayoutNormalizer.Normalize(odd));
    }

    [Fact]
    public void AC18_LengthMismatch()
    {
        var n = new SplitNode("x", Axis.X, new[] { 0.5, 0.5, 0.7 }, new LayoutNode[] { P("a"), P("b") });
        var r = LayoutNormalizer.Normalize(n);
        Assert.NotSame(n, r);
        AssertWeights(r, 0.5, 0.5);
        var m = new SplitNode("x", Axis.X, new[] { 0.5 }, new LayoutNode[] { P("a"), P("b") });
        var r2 = LayoutNormalizer.Normalize(m);
        Assert.NotSame(m, r2);
        AssertWeights(r2, 1.0 / 3, 2.0 / 3);
    }

    [Fact]
    public void AC19_Overflow()
    {
        var r = LayoutNormalizer.Normalize(X("x", (P("a"), 1e308), (P("b"), 1e308)));
        AssertWeights(r, 0.5, 0.5);
    }

    [Fact]
    public void AC20_SingleChildNaN()
    {
        var r = LayoutNormalizer.Normalize(X("x", (P("a"), 0.5), (Y("y", (P("b"), double.NaN)), 0.5)));
        AssertWeights(r, 0.5, 0.5);
        AssertChildIds(r, "a", "b");
    }

    [Fact]
    public void E4_SingleChildReplacesSplit()
    {
        var a = P("a");
        Assert.Same(a, LayoutNormalizer.Normalize(X("x", (a, double.NaN))));
    }

    [Fact]
    public void AC21_Property()
    {
        var rnd = new Random(12345);
        var nonNull = 0;
        for (var t = 0; t < 1000; t++)
        {
            var c = 0;
            var input = Gen(rnd, ref c, 5);
            var copy = Clone(input);
            var r = LayoutNormalizer.Normalize(input);
            Assert.Equal(copy, input);
            if (r is null) continue;
            nonNull++;
            var kinds = Walk(input).Select(n => (n.GetType(), n.Id)).ToHashSet();
            foreach (var n in Walk(r))
            {
                Assert.Contains((n.GetType(), n.Id), kinds);
                if (n is SplitNode s)
                {
                    Assert.True(s.Children.Length >= 2);
                    Assert.Equal(s.Children.Length, s.Weights.Length);
                    Assert.All(s.Weights, w => Assert.True(w > 0 && double.IsFinite(w)));
                    Assert.True(Math.Abs(s.Weights.Sum() - 1) < 1e-9);
                    Assert.DoesNotContain(s.Children, ch => ch is SplitNode cs && cs.Axis == s.Axis);
                    var orig = Walk(input).OfType<SplitNode>().First(x => x.Id == s.Id);
                    Assert.Equal(orig.Axis, s.Axis);
                }
                if (n is PanelNode p) Assert.NotEmpty(p.Views);
            }
            foreach (var d in Walk(input).OfType<DesktopNode>())
                Assert.Contains(Walk(r), n => n is DesktopNode && n.Id == d.Id);
            Assert.Same(r, LayoutNormalizer.Normalize(r));
        }
        Assert.True(nonNull > 100);
    }

    [Fact]
    public void AC21_SanitizeProperty_AlsoHolds()
    {
        var rnd = new Random(777);
        for (var t = 0; t < 500; t++)
        {
            var c = 0;
            var input = Gen(rnd, ref c, 5);
            var doc = LayoutSanitizer.Sanitize(new LayoutDocument(input), _ => true);
            Assert.True(Walk(doc.Root).OfType<DesktopNode>().Count() <= 1);
            // AC25
            var found = LayoutTree.FindDesktop(doc.Root);
            Assert.Equal(Walk(doc.Root).OfType<DesktopNode>().FirstOrDefault(), found);
            if (doc.Root is not null) Assert.Same(doc.Root, LayoutNormalizer.Normalize(doc.Root));
        }
    }

    [Fact]
    public void Normalize_NullThrows() =>
        Assert.Throws<ArgumentNullException>(() => LayoutNormalizer.Normalize(null!));
}
