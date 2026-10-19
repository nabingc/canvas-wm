using CanvasWm.Core.Layout;
using static CanvasWm.Core.Tests.SolverSpec.T;

namespace CanvasWm.Core.Tests.SolverSpec;

public class PropertyTests
{
    private static LayoutNode Gen(Random r, int depth, ref int id)
    {
        if (depth >= 4 || (depth > 0 && r.NextDouble() < 0.6) )
            return P("p" + id++);
        var sid = "s" + id++;
        int n = r.Next(2, 6);
        var raw = Enumerable.Range(0, n).Select(_ => 0.1 + r.NextDouble()).ToArray();
        var sum = raw.Sum();
        var kids = new List<LayoutNode>();
        for (var i = 0; i < n; i++) kids.Add(Gen(r, depth + 1, ref id));
        return new SplitNode(sid, r.Next(2) == 0 ? Axis.X : Axis.Y, raw.Select(x => x / sum), kids);
    }

    private static int Count(LayoutNode n) => n switch
    {
        SplitNode s => 1 + s.Children.Sum(Count),
        DesktopNode d => 1 + (d.Child is null ? 0 : Count(d.Child)),
        _ => 1,
    };

    [Fact]
    public void AC27_SeededProperty()
    {
        var r = new Random(20240607);
        for (var iter = 0; iter < 400; iter++)
        {
            var id = 0;
            LayoutNode root = Gen(r, 0, ref id);
            if (r.Next(2) == 0) root = new DesktopNode(root);
            var count = Count(root);
            var table = new Dictionary<(string, Axis), double>();
            void Fill(LayoutNode n)
            {
                table[(n.Id, Axis.X)] = r.NextDouble() * 1500;
                table[(n.Id, Axis.Y)] = r.NextDouble() * 1500;
                if (n is SplitNode s) foreach (var c in s.Children) Fill(c);
                if (n is DesktopNode { Child: { } ch }) Fill(ch);
            }
            Fill(root);
            var w = 100 + r.NextDouble() * 3900; var h = 100 + r.NextDouble() * 3900;
            var m = new LayoutMetrics(w, h, (n, a) => table[(n.Id, a)]);
            var copy = Clone(root);
            foreach (var metrics in new LayoutMetrics?[] { null, m })
            {
                var e = LayoutSolver.LayoutRects(root, metrics);
                Assert.Equal(count, e.Count);
                Assert.Equal(count, e.Keys.Distinct().Count());
                foreach (var entry in e.Values)
                {
                    if (metrics is null) Assert.Null(entry.Scale);
                    else
                    {
                        Assert.NotNull(entry.Scale);
                        Assert.InRange(entry.Scale!.Value, double.Epsilon, 1.0);
                        if (entry.Parent is not null) Assert.True(entry.Scale.Value <= e[entry.Parent].Scale!.Value + 1e-15);
                    }
                    if (entry.Node is SplitNode sn)
                    {
                        var rect = entry.Rect;
                        bool x = sn.Axis == Axis.X;
                        double pos = x ? rect.X : rect.Y;
                        double end = pos + (x ? rect.Width : rect.Height);
                        foreach (var c in sn.Children)
                        {
                            var cr = e[c.Id].Rect;
                            Near(pos, x ? cr.X : cr.Y, 1e-9);
                            Near(x ? rect.Y : rect.X, x ? cr.Y : cr.X, 1e-9);
                            Near(x ? rect.Height : rect.Width, x ? cr.Height : cr.Width, 1e-9);
                            pos = (x ? cr.X + cr.Width : cr.Y + cr.Height);
                        }
                        Near(end, pos, 1e-9);
                    }
                }
                Assert.Equal(copy, root);
                var e2 = LayoutSolver.LayoutRects(root, metrics);
                Assert.Equal(e.Select(kv => kv.Key).ToArray(), e2.Select(kv => kv.Key).ToArray());
                Assert.Equal(e.Select(kv => kv.Value).ToArray(), e2.Select(kv => kv.Value).ToArray());
            }
        }
    }
}

public class CacheTests
{
    private static LayoutDocument D(string k) => new(P(k));

    private sealed class Counter
    {
        public int N;
        public LayoutEntries Compute(LayoutDocument d) { N++; return LayoutSolver.FocusLayout(d.Root); }
    }

    [Fact]
    public void AC28_HitAndDefault()
    {
        var c = new Counter(); var cache = new LayoutCache(c.Compute);
        var d = D("a");
        var r1 = cache.GetOrCompute(d); var r2 = cache.GetOrCompute(d);
        Assert.Same(r1, r2); Assert.Equal(1, c.N);
        Assert.Same(r1, cache.GetOrCompute(D("a"))); Assert.Equal(1, c.N);
        var def = new LayoutCache().GetOrCompute(d);
        var exp = LayoutSolver.FocusLayout(d.Root, null);
        Assert.Equal(exp.ToArray(), def.ToArray());
    }

    [Fact]
    public void AC29_EvictionOldest()
    {
        var c = new Counter(); var cache = new LayoutCache(c.Compute);
        var ds = Enumerable.Range(1, 7).Select(i => D("d" + i)).ToArray();
        var first = cache.GetOrCompute(ds[0]);
        for (var i = 1; i < 7; i++) cache.GetOrCompute(ds[i]);
        Assert.Equal(7, c.N);
        var again = cache.GetOrCompute(ds[0]);
        Assert.Equal(8, c.N); Assert.NotSame(first, again);
        cache.GetOrCompute(ds[6]);
        Assert.Equal(8, c.N);
    }

    [Fact]
    public void AC30_HitDoesNotReorder()
    {
        var c = new Counter(); var cache = new LayoutCache(c.Compute);
        var d = Enumerable.Range(1, 7).Select(i => D("d" + i)).ToArray();
        var seq = new[] { 1, 2, 3, 4, 5, 6, 1, 7, 2, 1, 2 };
        var exp = new[] { 1, 2, 3, 4, 5, 6, 6, 7, 7, 8, 9 };
        var firstD2 = (LayoutEntries?)null;
        for (var i = 0; i < seq.Length; i++)
        {
            var res = cache.GetOrCompute(d[seq[i] - 1]);
            if (i == 1) firstD2 = res;
            if (i == 8) Assert.Same(firstD2, res);
            Assert.Equal(exp[i], c.N);
        }
    }

    [Fact]
    public void AC31_IndependenceNullsAndNullCompute()
    {
        var c1 = new Counter(); var c2 = new Counter();
        var a = new LayoutCache(c1.Compute); var b = new LayoutCache(c2.Compute);
        var d = D("a");
        a.GetOrCompute(d); b.GetOrCompute(d);
        Assert.Equal(1, c2.N);
        Assert.Throws<ArgumentNullException>(() => a.GetOrCompute(null!));
        Assert.Throws<ArgumentNullException>(() => new LayoutCache(null!));

        var n = 0;
        var cache = new LayoutCache(doc => n++ == 0 ? null! : LayoutSolver.FocusLayout(doc.Root));
        Assert.Throws<InvalidOperationException>(() => cache.GetOrCompute(d));
        var t = cache.GetOrCompute(d);
        Assert.Equal(2, n);
        Assert.Same(t, cache.GetOrCompute(d));
        Assert.Equal(2, n);
    }
}

public class ArchitectureTests
{
    [Fact]
    public void AC32_CoreCsprojHasNoReferences()
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (var d = new DirectoryInfo(dir); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "src", "CanvasWm.Core", "CanvasWm.Core.csproj");
            if (File.Exists(p)) { path = p; break; }
        }
        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        Assert.DoesNotContain("PackageReference", text);
        Assert.DoesNotContain("ProjectReference", text);
    }
}
