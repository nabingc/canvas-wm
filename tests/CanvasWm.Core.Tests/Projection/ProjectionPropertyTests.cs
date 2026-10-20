using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;
using CanvasWm.Core.Projection;
using static CanvasWm.Core.Tests.ProjectionSpec.Pj;

namespace CanvasWm.Core.Tests.ProjectionSpec;

public class ProjectionPropertyTests
{
    private static LayoutNode Gen(Random r, int depth, ref int n)
    {
        if (depth <= 0 || r.NextDouble() < 0.3)
        {
            int id = n++;
            if (r.NextDouble() < 0.2)
            {
                var views = Enumerable.Range(0, r.Next(2, 4)).Select(j => $"k{id}_{j}").ToArray();
                return new PanelNode("p" + id, views, views[r.Next(views.Length)], true);
            }
            return new PanelNode("p" + id, new[] { "k" + id }, "k" + id, false);
        }
        int sid = n++;
        int fan = r.Next(2, 5);
        var kids = new List<LayoutNode>(); var ws = new List<double>();
        for (int i = 0; i < fan; i++) { kids.Add(Gen(r, depth - 1, ref n)); ws.Add(r.NextDouble() * 4 + 0.1); }
        return new SplitNode("s" + sid, r.Next(2) == 0 ? Axis.X : Axis.Y, ws, kids);
    }

    private static void Eq(ProjectionResult a, ProjectionResult b)
    {
        Assert.Equal(a.Placements.ToArray(), b.Placements.ToArray());
        Assert.Equal(a.Targets.OrderBy(k => k.Key, StringComparer.Ordinal).ToArray(),
                     b.Targets.OrderBy(k => k.Key, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void AC27_SeededProperties()
    {
        for (int seed = 1; seed <= 100; seed++)
        {
            var rnd = new Random(seed);
            for (int c = 0; c < 20; c++)
            {
                string ctx = $"seed {seed} case {c}";
                int n = 0;
                var root = LayoutNormalizer.Normalize(Gen(rnd, 4, ref n))!;
                if (rnd.NextDouble() < 0.3) root = new DesktopNode(root);
                var area = R(rnd.Next(-2000, 2001), rnd.Next(-2000, 2001), rnd.Next(300, 3001), rnd.Next(300, 3001));
                double gap = rnd.Next(0, 21);
                var entries = LayoutSolver.FocusLayout(root);
                Rect? framed = null;
                if (rnd.NextDouble() >= 0.3)
                {
                    var all = entries.Values.ToList();
                    framed = all[rnd.Next(all.Count)].Rect;
                }
                var doc = new LayoutDocument(root);
                var facts = new Dictionary<string, WindowFacts>();
                foreach (var k in Keys(doc))
                {
                    var f = new WindowFacts { LastFrameSize = S(rnd.Next(100, 2001), rnd.Next(100, 2001)) };
                    if (rnd.NextDouble() < 0.5) f = f with { Minimum = S(rnd.Next(0, 1501), rnd.Next(0, 1501)) };
                    if (rnd.NextDouble() < 0.2) f = f with { Maximum = S(rnd.Next(100, 3001), rnd.Next(100, 3001)) };
                    facts[k] = f;
                }
                bool fixedMin = rnd.Next(2) == 0;
                var disp = new ParkDisplay(area, area, Array.Empty<Rect>(), new Point(0, 0));
                var view = ProjectionView.AtRest(area, gap, framed);
                var req = new ProjectionRequest(doc, view, disp, facts) { Policy = new ProjectionPolicy { FixedMinimum = fixedMin } };
                var res = ProjectionTargets.Compute(req);

                // P1
                var panels = Nodes(root).OfType<PanelNode>().ToList();
                Assert.True(panels.Select(p => p.Id).SequenceEqual(res.Placements.Select(p => p.PanelId)), ctx + " P1 order");
                for (int i = 0; i < panels.Count; i++) Assert.Equal(panels[i].Selected, res.Placements[i].Key);
                var selected = panels.Select(p => p.Selected!).ToHashSet();
                foreach (var k in res.Targets.Keys) Assert.True(selected.Contains(k), ctx + " P1 non-selected target " + k);
                Assert.Equal(res.Placements.Length, res.Targets.Count);

                foreach (var pl in res.Placements)
                {
                    var t = res.Targets[pl.Key];
                    var fct = facts[pl.Key];
                    // P2
                    Assert.True((t.Kind == TargetKind.Tile) == (pl.OnStage && pl.Fits), ctx + " P2 tile");
                    Assert.True((t.Kind == TargetKind.ParkWithPicture) == (pl.OnStage && !pl.Fits), ctx + " P2 pic");
                    Assert.True((t.Kind == TargetKind.Park) == !pl.OnStage, ctx + " P2 park");
                    if (t.Kind == TargetKind.Tile)
                    {
                        // P3
                        var rc = t.Rect;
                        foreach (var v in new[] { rc.X, rc.Y, rc.Width, rc.Height })
                            Assert.True(v == Math.Round(v), ctx + " P3 integral " + rc);
                        Assert.True(rc.Width >= 0 && rc.Height >= 0, ctx + " P3 nonneg");
                        if (fct.Maximum is null)
                        {
                            var fr = pl.Frame;
                            Assert.True(Math.Abs(rc.X - fr.X) <= 0.5 + 1e-9, ctx + " P3 x");
                            Assert.True(Math.Abs(rc.Y - fr.Y) <= 0.5 + 1e-9, ctx + " P3 y");
                            Assert.True(Math.Abs(rc.X + rc.Width - (fr.X + fr.Width)) <= 0.5 + 1e-9, ctx + " P3 maxx");
                            Assert.True(Math.Abs(rc.Y + rc.Height - (fr.Y + fr.Height)) <= 0.5 + 1e-9, ctx + " P3 maxy");
                        }
                    }
                    else
                    {
                        // P4
                        Assert.True(ParkGeometry.IsParkFrame(disp, t.Rect), ctx + " P4 " + t.Rect);
                    }
                    if (t.Kind == TargetKind.ParkWithPicture)
                    {
                        // P9
                        var m = ScreenProjection.EffectiveMinimum(fixedMin, fct.Minimum, Tuning.Default);
                        Assert.True(t.Rect.Width >= Math.Ceiling(m.Width) && t.Rect.Height >= Math.Ceiling(m.Height),
                            ctx + $" P9 {t.Rect} m {m}");
                    }
                    // P5
                    if (framed is null && pl.Frame.Width > 0 && pl.Frame.Height > 0)
                        Assert.True(ScreenProjection.OnStageFraction(pl.Frame, view.Stage) >= 1 - 1e-9, ctx + " P5 " + pl.Frame);
                }

                // P6
                if (framed is { } fz)
                {
                    var pr = ScreenProjection.Project(view, fz);
                    Near(view.Stage, pr, 1e-6);
                }

                // P7
                foreach (var sp in Nodes(root).OfType<SplitNode>())
                {
                    for (int i = 0; i + 1 < sp.Children.Length; i++)
                    {
                        if (sp.Children[i] is not PanelNode c1 || sp.Children[i + 1] is not PanelNode c2) continue;
                        bool xa = sp.Axis == Axis.X;
                        var u1 = ScreenProjection.Project(view, entries[c1.Id].Rect);
                        var u2 = ScreenProjection.Project(view, entries[c2.Id].Rect);
                        if ((xa ? u1.Width : u1.Height) < view.Gap || (xa ? u2.Width : u2.Height) < view.Gap) continue;
                        var f1 = res.Placements.Single(p => p.PanelId == c1.Id).Frame;
                        var f2 = res.Placements.Single(p => p.PanelId == c2.Id).Frame;
                        double d = xa ? f2.X - (f1.X + f1.Width) : f2.Y - (f1.Y + f1.Height);
                        Assert.True(Math.Abs(d - view.Gap) <= 1e-6, ctx + $" P7 {d} vs {view.Gap}");
                    }
                }

                // P8
                var res2 = ProjectionTargets.Compute(req);
                Eq(res, res2);
                Assert.Equal(new LayoutDocument(root), req.Document);
            }
        }
    }
}
