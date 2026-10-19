using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Navigation;

namespace CanvasWm.Core.Tests.DirectionalFocusSpec;

internal static class N
{
    public const double Tol = 1e-9;
    public static readonly Edge[] Dirs = { Edge.Left, Edge.Right, Edge.Top, Edge.Bottom };

    public static PanelNode P(string k) => PanelNode.ForWindow(k);

    public static SplitNode X(string id, params LayoutNode[] c) =>
        new(id, Axis.X, c.Select(_ => 1.0), c);

    public static SplitNode Y(string id, params LayoutNode[] c) =>
        new(id, Axis.Y, c.Select(_ => 1.0), c);

    public static SplitNode XW(string id, params (LayoutNode, double)[] c) =>
        new(id, Axis.X, c.Select(x => x.Item2), c.Select(x => x.Item1));

    public static SplitNode YW(string id, params (LayoutNode, double)[] c) =>
        new(id, Axis.Y, c.Select(x => x.Item2), c.Select(x => x.Item1));

    public static LayoutEntries L(LayoutNode root) => LayoutSolver.FocusLayout(root, null);

    public static string? Nb(LayoutEntries e, string id, Edge d) => DirectionalFocus.Neighbour(e, id, d);

    public static string Range(string split, params string[] ids) => LayoutSolver.RangeId(split, ids);

    public static KeyValuePair<string, LayoutEntry> Row(string id, Rect r, string? parent, bool v = false) =>
        new(id, new LayoutEntry(P(id), r, parent, null, v));

    public static void Near(double expected, double actual) =>
        Assert.True(Math.Abs(expected - actual) <= Tol, $"expected {expected} actual {actual}");

    public static double S(LayoutEntries e, string cur, string cand, Edge d) =>
        DirectionalFocus.Score(e[cur].Rect, e[cand].Rect, d, e[cand].Virtual);

    public static bool Adj(LayoutEntries e, string cur, string cand, Edge d) =>
        DirectionalFocus.IsAdjacent(e[cur].Rect, e[cand].Rect, d);

    public static double Ln(double v) => Math.Log(v);
}

public class DirectionalFocusTests
{
    private static readonly double Ln2 = Math.Log(2);

    // ---- AC1
    [Fact]
    public void AC1_TwoPanels()
    {
        var e = N.L(N.X("s", N.P("a"), N.P("b")));
        Assert.Equal("b", N.Nb(e, "a", Edge.Right));
        Assert.Equal(0, N.S(e, "a", "b", Edge.Right));
        Assert.Null(N.Nb(e, "a", Edge.Left));
        Assert.Null(N.Nb(e, "a", Edge.Bottom));
        Assert.Null(N.Nb(e, "a", Edge.Top));
        Assert.Equal("a", N.Nb(e, "b", Edge.Left));
    }

    // ---- AC2
    [Fact]
    public void AC2_NestedColumn()
    {
        var e = N.L(N.X("s", N.P("a"), N.Y("t", N.P("b"), N.P("c"))));
        Assert.Equal("t", N.Nb(e, "a", Edge.Right));
        N.Near(0, N.S(e, "a", "t", Edge.Right));
        N.Near(Ln2 + 0.25, N.S(e, "a", "b", Edge.Right));
        N.Near(Ln2 + 0.25, N.S(e, "a", "c", Edge.Right));
        Assert.Equal("a", N.Nb(e, "b", Edge.Left));
        N.Near(Ln2 + 0.5, N.S(e, "b", "a", Edge.Left));
        Assert.Equal("c", N.Nb(e, "b", Edge.Bottom));
        N.Near(0, N.S(e, "b", "c", Edge.Bottom));
        Assert.Equal("a", N.Nb(e, "t", Edge.Left));
        N.Near(0, N.S(e, "t", "a", Edge.Left));
        Assert.Null(N.Nb(e, "c", Edge.Right));
    }

    // ---- AC3
    [Fact]
    public void AC3_RangeWins()
    {
        var e = N.L(N.Y("r", N.X("t", N.P("a"), N.P("b")), N.X("u", N.P("c"), N.P("d"), N.P("e"), N.P("f"))));
        var cd = N.Range("u", "c", "d");
        Assert.Equal(cd, N.Nb(e, "a", Edge.Bottom));
        Assert.Equal("range:[\"u\",\"c\",\"d\"]", cd);
        N.Near(Ln2 + 0.5, N.S(e, "a", "u", Edge.Bottom));
        N.Near(Ln2 + 0.25, N.S(e, "a", "c", Edge.Bottom));
        N.Near(Ln2 + 0.25, N.S(e, "a", "d", Edge.Bottom));
        N.Near(0.05, N.S(e, "a", cd, Edge.Bottom));
        N.Near(Math.Log(1.5) + 0.25 + 0.05, N.S(e, "a", N.Range("u", "c", "d", "e"), Edge.Bottom));
        N.Near(0.5 + 0.05, N.S(e, "a", N.Range("u", "d", "e"), Edge.Bottom));
        N.Near(Math.Log(1.5) + 0.75 + 0.05, N.S(e, "a", N.Range("u", "d", "e", "f"), Edge.Bottom));
        foreach (var x in new[] { "e", "f", N.Range("u", "e", "f") })
        {
            Assert.True(e.ContainsKey(x), x);
            Assert.False(N.Adj(e, "a", x, Edge.Bottom), x);
        }
    }

    // ---- AC4
    [Fact]
    public void AC4_PenaltyDecides()
    {
        var e = N.L(N.YW("r", (N.X("t", N.P("a"), N.P("b")), 1),
            (N.XW("u", (N.P("c"), 0.45), (N.P("d"), 0.1), (N.P("e"), 0.45)), 1)));
        var cd = N.Range("u", "c", "d");
        Assert.Equal("c", N.Nb(e, "a", Edge.Bottom));
        double sc = N.S(e, "a", "c", Edge.Bottom);
        N.Near(Math.Abs(Math.Log(0.9)) + 0.05, sc);
        N.Near(Math.Log(1.1) + 0.05 + 0.05, N.S(e, "a", cd, Edge.Bottom));
        N.Near(Math.Log(5) + 0.5, N.S(e, "a", "d", Edge.Bottom));
        var s = DirectionalFocus.Score(e["a"].Rect, e[cd].Rect, Edge.Bottom, false);
        N.Near(Math.Log(1.1) + 0.05, s);
        Assert.True(s < sc);
    }

    // ---- AC5
    [Fact]
    public void AC5_TieGoesToTableOrder()
    {
        var e = N.L(N.Y("r", N.XW("t", (N.P("a"), 0.25), (N.P("m"), 0.5), (N.P("b"), 0.25)), N.X("u", N.P("c"), N.P("d"))));
        Assert.Equal("c", N.Nb(e, "m", Edge.Bottom));
        double sc = N.S(e, "m", "c", Edge.Bottom), sd = N.S(e, "m", "d", Edge.Bottom);
        Assert.Equal(0.5, sc);
        Assert.Equal(sc, sd);
        N.Near(Ln2, N.S(e, "m", "u", Edge.Bottom));
    }

    // ---- AC6
    [Fact]
    public void AC6_EmptyDesktopIsTarget()
    {
        var e = N.L(N.X("s", N.P("a"), new DesktopNode()));
        Assert.Equal("desktop", N.Nb(e, "a", Edge.Right));
    }

    // ---- AC7
    [Fact]
    public void AC7_FloatsIgnored()
    {
        var e = N.L(new DesktopNode(N.X("s", N.P("a"), N.P("b"))));
        var rows = e.ToList();
        rows.Add(new("f", new LayoutEntry(N.P("f"), new Rect(0.5, 0.25, 0.5, 0.5), "desktop", null, false)));
        Assert.Equal("b", DirectionalFocus.Neighbour(rows, "a", Edge.Right));

        var rows2 = e.Where(r => r.Key != "b").ToList();
        rows2.Add(new("f", new LayoutEntry(N.P("f"), new Rect(0.5, 0.25, 0.5, 0.5), "other", null, false)));
        Assert.Equal("f", DirectionalFocus.Neighbour(rows2, "a", Edge.Right));
    }

    // ---- AC8
    [Fact]
    public void AC8_NotAdjacent()
    {
        var e = N.L(N.X("s", N.P("a"), N.P("b"), N.P("c")));
        Assert.Equal("b", N.Nb(e, "a", Edge.Right));
        Assert.False(N.Adj(e, "a", "c", Edge.Right));
        N.Near(Ln2 + 0.05, N.S(e, "a", N.Range("s", "b", "c"), Edge.Right));

        var ab = N.Range("s", "a", "b");
        Assert.Equal("c", N.Nb(e, ab, Edge.Right));
        N.Near(Ln2, N.S(e, ab, "c", Edge.Right));
        Assert.False(N.Adj(e, ab, N.Range("s", "b", "c"), Edge.Right));
    }

    // ---- AC9
    private static string? Tol(Rect k, string? kParent = "p")
    {
        var rows = new List<KeyValuePair<string, LayoutEntry>>
        {
            N.Row("cur", new Rect(0, 0, 0.5, 1), "p"),
            N.Row("k", k, kParent),
        };
        return DirectionalFocus.Neighbour(rows, "cur", Edge.Right);
    }

    [Fact]
    public void AC9_Tolerances()
    {
        Assert.Equal("k", Tol(new Rect(0.5000005, 0, 0.5, 1)));
        Assert.Null(Tol(new Rect(0.500002, 0, 0.5, 1)));
        Assert.Equal("k", Tol(new Rect(0.4999995, 0, 0.5, 1)));
        Assert.Null(Tol(new Rect(0.5, 0, 0.5, 1e-6)));
        Assert.Equal("k", Tol(new Rect(0.5, 0, 0.5, 2e-6)));
        Assert.Null(Tol(new Rect(0.5, 1, 0.5, 1)));
    }

    // ---- AC10
    [Fact]
    public void AC10_RootCurrent()
    {
        var e1 = N.L(N.X("s", N.P("a"), N.P("b")));
        var e2 = N.L(new DesktopNode(N.X("s", N.P("a"), N.P("b"))));
        foreach (var d in N.Dirs)
        {
            Assert.Null(N.Nb(e1, "s", d));
            Assert.Null(N.Nb(e2, "desktop", d));
        }
    }

    // ---- AC11
    [Fact]
    public void AC11_UnknownEmptyAndArgumentErrors()
    {
        var e = N.L(N.X("s", N.P("a"), N.P("b")));
        Assert.Null(N.Nb(e, "zzz", Edge.Right));
        foreach (var d in N.Dirs)
        {
            Assert.Null(N.Nb(LayoutEntries.Empty, "a", d));
            Assert.Null(DirectionalFocus.Neighbour(new List<KeyValuePair<string, LayoutEntry>>(), "a", d));
        }
        Assert.Throws<ArgumentNullException>(() => DirectionalFocus.Neighbour((LayoutEntries)null!, "a", Edge.Left));
        Assert.Throws<ArgumentNullException>(() =>
            DirectionalFocus.Neighbour((IEnumerable<KeyValuePair<string, LayoutEntry>>)null!, "a", Edge.Left));
        Assert.Throws<ArgumentNullException>(() => DirectionalFocus.Neighbour(e, null!, Edge.Left));
        Assert.Throws<ArgumentNullException>(() => DirectionalFocus.Neighbour(e.ToList(), null!, Edge.Left));
        var bad = (Edge)4;
        Assert.Throws<ArgumentOutOfRangeException>(() => DirectionalFocus.Neighbour(e, "a", bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => DirectionalFocus.Neighbour(e.ToList(), "a", bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => DirectionalFocus.IsAdjacent(default, default, bad));
        Assert.Throws<ArgumentOutOfRangeException>(() => DirectionalFocus.Score(default, default, bad, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => DirectionalFocus.Neighbour(e, "a", (Edge)(-1)));
    }

    [Fact]
    public void AC11_ValidationOrder_NullEntriesBeforeBadDirection()
    {
        Assert.Throws<ArgumentNullException>(() => DirectionalFocus.Neighbour((LayoutEntries)null!, "a", (Edge)9));
        var e = N.L(N.X("s", N.P("a"), N.P("b")));
        Assert.Throws<ArgumentNullException>(() => DirectionalFocus.Neighbour(e, null!, (Edge)9));
    }

    [Fact]
    public void AC11_NullKeyOrValueRowThrows()
    {
        var good = N.Row("cur", new Rect(0, 0, .5, 1), "p");
        var nullVal = new KeyValuePair<string, LayoutEntry>("x", null!);
        var nullKey = new KeyValuePair<string, LayoutEntry>(null!, good.Value);
        Assert.Throws<ArgumentNullException>(() =>
            DirectionalFocus.Neighbour(new[] { good, nullVal }, "cur", Edge.Right));
        Assert.Throws<ArgumentNullException>(() =>
            DirectionalFocus.Neighbour(new[] { good, nullKey }, "cur", Edge.Right));
        // A7: unknown current with a null row still throws.
        Assert.Throws<ArgumentNullException>(() =>
            DirectionalFocus.Neighbour(new[] { good, nullVal }, "zzz", Edge.Right));
    }

    [Fact]
    [Trait("Assumption", "A7")]
    public void A7_NullRowThrowsEvenWhenCurrentFoundFirst()
    {
        // Spec A7: "throws when reached". Scan always visits every row, so a null row after the
        // current row is still reached during candidate scan.
        var good = N.Row("cur", new Rect(0, 0, .5, 1), "p");
        var nullVal = new KeyValuePair<string, LayoutEntry>("x", null!);
        Assert.Throws<ArgumentNullException>(() =>
            DirectionalFocus.Neighbour(new[] { good, nullVal }, "cur", Edge.Right));
    }

    // ---- AC12
    [Fact]
    public void AC12_OccupiedDesktopSkipped()
    {
        var a = N.Row("a", new Rect(0, 0, .5, 1), "s");
        var d2 = new KeyValuePair<string, LayoutEntry>("d2",
            new LayoutEntry(new DesktopNode(N.P("z"), "d2"), new Rect(.5, 0, .5, 1), "s", null, false));
        var z = N.Row("z", new Rect(.5, 0, .5, 1), "d2");
        Assert.Equal("z", DirectionalFocus.Neighbour(new[] { a, d2, z }, "a", Edge.Right));
        Assert.Null(DirectionalFocus.Neighbour(new[] { a, d2 }, "a", Edge.Right));
    }

    // ---- AC13
    [Fact]
    [Trait("Assumption", "A1")]
    public void AC13_NestedDefaultDesktop()
    {
        var e = N.L(N.X("s", N.P("a"), new DesktopNode(N.P("b"))));
        Assert.Equal("desktop", e["b"].Parent);
        Assert.Null(N.Nb(e, "a", Edge.Right));
    }

    [Fact]
    [Trait("Assumption", "A1")]
    public void A1_NonDefaultDesktopChildNotExcluded()
    {
        var e = N.L(N.X("s", N.P("a"), new DesktopNode(N.P("b"), "d9")));
        Assert.Equal("b", N.Nb(e, "a", Edge.Right));
    }

    [Fact]
    [Trait("Assumption", "A2")]
    public void A2_FloatRowParentDesktopExcludedAfterTree()
    {
        var e = N.L(N.X("s", N.P("a"), N.P("b")));
        var rows = e.ToList();
        rows.Add(N.Row("f", new Rect(0.5, 0, 0.5, 1), DesktopNode.DefaultId));
        Assert.Equal("b", DirectionalFocus.Neighbour(rows, "a", Edge.Right));
    }

    // ---- AC14
    [Fact]
    public void AC14_NullParentNotExcluded()
    {
        var rows = new[]
        {
            N.Row("cur", new Rect(0, 0, .5, 1), "p"),
            N.Row("k", new Rect(.5, 0, .5, 1), null),
        };
        Assert.Equal("k", DirectionalFocus.Neighbour(rows, "cur", Edge.Right));
    }

    // ---- AC15
    [Fact]
    public void AC15_ScoreHelper()
    {
        var c = new Rect(0, 0, .5, .5);
        var k = new Rect(0, .5, .25, .5);
        N.Near(Ln2 + 0.25, DirectionalFocus.Score(c, k, Edge.Bottom, false));
        N.Near(Ln2 + 0.25 + 0.05, DirectionalFocus.Score(c, k, Edge.Bottom, true));
        N.Near(Ln2 + 0.25,
            DirectionalFocus.Score(new Rect(0, 0, .5, 1), new Rect(.5, 0, .5, .5), Edge.Right, false));
    }

    // ---- AC16
    [Fact]
    public void AC16_IsAdjacentAllDirections()
    {
        var c = new Rect(0.25, 0.25, 0.5, 0.5);
        var tiles = new Dictionary<Edge, Rect>
        {
            [Edge.Left] = new(0, 0.25, 0.25, 0.5),
            [Edge.Right] = new(0.75, 0.25, 0.25, 0.5),
            [Edge.Top] = new(0.25, 0, 0.5, 0.25),
            [Edge.Bottom] = new(0.25, 0.75, 0.5, 0.25),
        };
        int t = 0, f = 0;
        foreach (var (side, rect) in tiles)
        {
            foreach (var d in N.Dirs)
            {
                bool r = DirectionalFocus.IsAdjacent(c, rect, d);
                Assert.Equal(d == side, r);
                if (r) t++; else f++;
            }
        }
        Assert.Equal(4, t);
        Assert.Equal(12, f);
    }

    // ---- AC17
    [Fact]
    public void AC17_DegenerateRects()
    {
        var r1 = new[]
        {
            N.Row("cur", new Rect(0, 0, 0, 1), "p"),
            N.Row("k", new Rect(0, 0, .5, 1), "p"),
        };
        Assert.Null(DirectionalFocus.Neighbour(r1, "cur", Edge.Right));

        var k = N.Row("k", new Rect(.5, 0, 0, 1), "p");
        var cur = N.Row("cur", new Rect(0, 0, .5, 1), "p");
        Assert.Null(DirectionalFocus.Neighbour(new[] { cur, k }, "cur", Edge.Right));
        Assert.True(double.IsPositiveInfinity(
            DirectionalFocus.Score(new Rect(0, 0, .5, 1), new Rect(.5, 0, 0, 1), Edge.Right, false)));

        var k2 = N.Row("k2", new Rect(.5, 0, .5, 1), "p");
        Assert.Equal("k2", DirectionalFocus.Neighbour(new[] { cur, k, k2 }, "cur", Edge.Right));
    }

    [Fact]
    public void E6_NaNCoordinatesNotCandidate()
    {
        var cur = N.Row("cur", new Rect(0, 0, .5, 1), "p");
        var k = N.Row("k", new Rect(double.NaN, 0, .5, 1), "p");
        Assert.Null(DirectionalFocus.Neighbour(new[] { cur, k }, "cur", Edge.Right));
    }

    // ---- AC18
    [Fact]
    public void AC18_OverloadEquivalence()
    {
        var trees = new LayoutNode[]
        {
            N.X("s", N.P("a"), N.P("b")),
            N.X("s", N.P("a"), N.Y("t", N.P("b"), N.P("c"))),
            N.Y("r", N.X("t", N.P("a"), N.P("b")), N.X("u", N.P("c"), N.P("d"), N.P("e"), N.P("f"))),
            N.YW("r", (N.X("t", N.P("a"), N.P("b")), 1),
                (N.XW("u", (N.P("c"), 0.45), (N.P("d"), 0.1), (N.P("e"), 0.45)), 1)),
            N.Y("r", N.XW("t", (N.P("a"), 0.25), (N.P("m"), 0.5), (N.P("b"), 0.25)), N.X("u", N.P("c"), N.P("d"))),
            N.X("s", N.P("a"), new DesktopNode()),
            new DesktopNode(N.X("s", N.P("a"), N.P("b"))),
            N.X("s", N.P("a"), N.P("b"), N.P("c")),
        };
        int n = 0;
        foreach (var tree in trees)
        {
            var e = N.L(tree);
            foreach (var id in e.Keys.Concat(new[] { "zzz" }))
            {
                foreach (var d in N.Dirs)
                {
                    Assert.Equal(DirectionalFocus.Neighbour(e.AsEnumerable(), id, d), N.Nb(e, id, d));
                    n++;
                }
            }
        }
        Assert.True(n > 100);
    }

    [Fact]
    public void AC18_DuplicateCurrentRows()
    {
        var rows = new[]
        {
            N.Row("cur", new Rect(0, 0, .5, 1), "p"),
            N.Row("cur", new Rect(.5, 0, .5, 1), "p"),
            N.Row("k", new Rect(.5, 0, .5, 1), "p"),
        };
        Assert.Equal("k", DirectionalFocus.Neighbour(rows, "cur", Edge.Right));
    }

    // ---- AC19
    [Fact]
    public void AC19_NoMemory()
    {
        var tree = N.X("s", N.P("a"), N.Y("t", N.P("b"), N.P("c")));
        var e = N.L(tree);
        Assert.Equal("t", N.Nb(e, "a", Edge.Right));
        foreach (var id in e.Keys)
            foreach (var d in N.Dirs)
                N.Nb(e, id, d);
        Assert.Equal("t", N.Nb(e, "a", Edge.Right));
        Assert.Equal("a", N.Nb(e, "t", Edge.Left));
        Assert.Equal("t", N.Nb(e, "a", Edge.Right));

        var fresh = N.L(tree);
        Assert.Equal(fresh.Keys.ToArray(), e.Keys.ToArray());
        foreach (var k in fresh.Keys)
            Assert.Equal(fresh[k], e[k]);
    }

    // ---- AC20
    [Fact]
    public void AC20_ScaleIgnored()
    {
        var e = N.L(N.Y("r", N.X("t", N.P("a"), N.P("b")), N.X("u", N.P("c"), N.P("d"), N.P("e"), N.P("f"))));
        var scaled = e.Select(r => new KeyValuePair<string, LayoutEntry>(r.Key, r.Value with { Scale = 0.5 })).ToList();
        foreach (var id in e.Keys)
            foreach (var d in N.Dirs)
                Assert.Equal(N.Nb(e, id, d), DirectionalFocus.Neighbour(scaled, id, d));
        Assert.Equal(N.Range("u", "c", "d"), DirectionalFocus.Neighbour(scaled, "a", Edge.Bottom));
    }

    [Fact]
    public void Constants()
    {
        Assert.Equal(1e-6, DirectionalFocus.TouchTolerance);
        Assert.Equal(1e-6, DirectionalFocus.OverlapTolerance);
        Assert.Equal(0.05, DirectionalFocus.VirtualPenalty);
    }
}
