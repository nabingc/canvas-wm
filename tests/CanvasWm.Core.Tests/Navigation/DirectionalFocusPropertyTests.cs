using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Navigation;

namespace CanvasWm.Core.Tests.DirectionalFocusSpec;

public class DirectionalFocusPropertyTests
{
    private sealed class Gen
    {
        private readonly Random _r;
        private int _n;
        public Gen(int seed) => _r = new Random(seed);

        public LayoutNode Root()
        {
            var body = Node(0, forceContainer: false);
            if (body is DesktopNode) body = new PanelNode("p" + _n++, new[] { "w" + _n }, null, false);
            return _r.Next(2) == 0 ? new DesktopNode(body) : body;
        }

        private LayoutNode Node(int depth, bool forceContainer)
        {
            int roll = _r.Next(10);
            if (depth >= 4 || (!forceContainer && roll < 3 && depth > 0))
            {
                return roll < 2 ? new DesktopNode(null, "e" + _n++) : new PanelNode("p" + _n++, new[] { "w" + _n }, null, false);
            }
            int count = 2 + _r.Next(3);
            var kids = new List<LayoutNode>();
            var ws = new List<double>();
            for (int i = 0; i < count; i++)
            {
                kids.Add(Node(depth + 1, false));
                ws.Add(0.1 + _r.NextDouble() * 2.9);
            }
            return new SplitNode("s" + _n++, _r.Next(2) == 0 ? Axis.X : Axis.Y, ws, kids);
        }
    }

    private static bool Contains(LayoutNode n, string id) =>
        n.Id == id || n switch
        {
            SplitNode s => s.Children.Any(c => Contains(c, id)),
            DesktopNode { Child: not null } d => Contains(d.Child, id),
            _ => false,
        };

    [Fact]
    public void AC21_SeededProperties()
    {
        int nonNull = 0;
        for (int seed = 1; seed <= 200; seed++)
        {
            var root = new Gen(seed).Root();
            var e = LayoutSolver.FocusLayout(root, null);
            var e2 = LayoutSolver.FocusLayout(new Gen(seed).Root(), null);
            var rows = e.ToList();
            foreach (var cur in e.Keys)
            {
                var ce = e[cur];
                foreach (var d in N.Dirs)
                {
                    string ctx = $"seed {seed} cur {cur} dir {d}";
                    var r = DirectionalFocus.Neighbour(e, cur, d);

                    // independent eligibility
                    var eligible = new List<(string Id, double Score)>();
                    foreach (var (key, en) in rows)
                    {
                        if (key == cur) continue;
                        if (en.Parent == "desktop") continue;
                        if (Contains(en.Node, cur) || Contains(ce.Node, key)) continue;
                        if (en.Node is DesktopNode { Child: not null }) continue;
                        if (!DirectionalFocus.IsAdjacent(ce.Rect, en.Rect, d)) continue;
                        eligible.Add((key, DirectionalFocus.Score(ce.Rect, en.Rect, d, en.Virtual)));
                    }

                    if (r is null)
                    {
                        Assert.True(eligible.All(x => !(x.Score < double.PositiveInfinity)), ctx);
                    }
                    else
                    {
                        nonNull++;
                        Assert.True(e.ContainsKey(r), ctx);
                        Assert.NotEqual(cur, r);
                        var re = e[r];
                        Assert.NotEqual("desktop", re.Parent);
                        Assert.False(re.Node is DesktopNode { Child: not null }, ctx);
                        Assert.False(Contains(re.Node, cur), ctx);
                        Assert.False(Contains(ce.Node, r), ctx);
                        Assert.True(DirectionalFocus.IsAdjacent(ce.Rect, re.Rect, d), ctx);
                        double sr = DirectionalFocus.Score(ce.Rect, re.Rect, d, re.Virtual);
                        Assert.True(double.IsFinite(sr), ctx);
                        foreach (var x in eligible) Assert.True(sr <= x.Score, ctx + " min " + x.Id);
                        foreach (var x in eligible)
                        {
                            if (x.Id == r) break;
                            Assert.False(x.Score == sr, ctx + " tie " + x.Id);
                        }
                    }

                    Assert.Equal(r, DirectionalFocus.Neighbour(e, cur, d));
                    Assert.Equal(r, DirectionalFocus.Neighbour(e2, cur, d));
                }
            }
        }
        Assert.True(nonNull > 100, $"generator produced too few neighbours: {nonNull}");
    }
}
