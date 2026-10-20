using System.Collections.Immutable;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;
using CanvasWm.Core.Projection;
using static CanvasWm.Core.Tests.ProjectionSpec.Pj;

namespace CanvasWm.Core.Tests.ProjectionSpec;

public class ProjectionTargetsTests
{
    private static WindowTarget Tile(double x, double y, double w, double h) => new(TargetKind.Tile, R(x, y, w, h));
    private static WindowTarget Pk(Rect r) => new(TargetKind.Park, r);

    private static FloatingPanel Float(string key) => new(P(key), R(0, 0, 1, 1), 0);

    [Fact]
    public void AC4_Example1_NothingFramed()
    {
        var r = Run(Ab(), null);
        Assert.Equal(new[] { "a", "b" }, r.Placements.Select(p => p.PanelId).ToArray());
        Near(R(10, 35, 705, 855), Pl(r, "a").Frame);
        Near(R(725, 35, 705, 855), Pl(r, "b").Frame);
        Assert.All(r.Placements, p => { Assert.True(p.OnStage); Assert.True(p.Fits); });
        Assert.Equal(Tile(10, 35, 705, 855), r.Targets["a"]);
        Assert.Equal(Tile(725, 35, 705, 855), r.Targets["b"]);
    }

    [Fact]
    public void AC5_Example2_FramedA()
    {
        var r = Run(Ab(), R(0, 0, .5, 1));
        Near(R(10, 35, 1420, 855), Pl(r, "a").Frame);
        Assert.Equal(Tile(10, 35, 1420, 855), r.Targets["a"]);
        Near(R(1440, 35, 705, 855), Pl(r, "b").Frame);
        Assert.False(Pl(r, "b").OnStage);
        Assert.Equal(R(1439, 899, 705, 855), Park(S(705, 855)));
        Assert.Equal(Pk(R(1439, 899, 705, 855)), r.Targets["b"]);
    }

    [Fact]
    public void AC6_Example3_NestedY()
    {
        var r = Run(Abc(), R(.5, 0, .5, 1));
        Near(R(10, 35, 1420, 422.5), Pl(r, "b").Frame);
        Near(R(10, 467.5, 1420, 422.5), Pl(r, "c").Frame);
        Near(R(-705, 35, 705, 855), Pl(r, "a").Frame);
        Assert.Equal(Tile(10, 35, 1420, 423), r.Targets["b"]);
        Assert.Equal(Tile(10, 468, 1420, 422), r.Targets["c"]);
        var b = r.Targets["b"].Rect; var c = r.Targets["c"].Rect;
        Assert.Equal(10, c.Y - (b.Y + b.Height));
        Assert.Equal(TargetKind.Park, r.Targets["a"].Kind);
    }

    [Fact]
    public void AC7_Example4_FramedSquareB()
    {
        var root = Abc();
        var b = LayoutSolver.LayoutRects(root)["b"].Rect;
        Assert.Equal(R(.5, 0, .5, .5), b);
        var req = Req(root, b);
        Assert.Equal(Stretch.Identity, req.View.Stretch);
        Assert.Equal(R(.5, 0, .5, .5), req.View.Camera);
        var r = ProjectionTargets.Compute(req);
        Assert.Equal(Tile(10, 35, 1420, 855), r.Targets["b"]);
        Near(R(10, 900, 1420, 855), Pl(r, "c").Frame);
        Assert.Equal(TargetKind.Park, r.Targets["c"].Kind);
        Near(R(-1420, 35, 1420, 1720), Pl(r, "a").Frame);
        Assert.Equal(TargetKind.Park, r.Targets["a"].Kind);
    }

    [Fact]
    public void AC8_Example5_YSplit()
    {
        var root = Y("s", (P("t"), .5), (P("u"), .5));
        var r = Run(root, R(0, 0, 1, .5));
        Assert.Equal(Tile(10, 35, 1420, 855), r.Targets["t"]);
        // SPEC DISCREPANCY: spec says (10,900,1420,855); B3/B6 give height 865*0.5/1 - 10 = 422.5
        // (u keeps its 0.5 unit extent beyond the framed rect; cf. AC6 where a keeps width 705).
        Near(R(10, 900, 1420, 422.5), Pl(r, "u").Frame);
        Assert.Equal(TargetKind.Park, r.Targets["u"].Kind);
    }

    [Fact]
    public void AC9_Example6_HalfPixel()
    {
        var r = Run(Ab(), null, area: R(0, 25, 1441, 875));
        Near(R(10, 35, 705.5, 855), Pl(r, "a").Frame);
        Near(R(725.5, 35, 705.5, 855), Pl(r, "b").Frame);
        Assert.Equal(Tile(10, 35, 706, 855), r.Targets["a"]);
        Assert.Equal(Tile(726, 35, 705, 855), r.Targets["b"]);
    }

    [Fact]
    public void AC10_Example7_PartiallyOffStage()
    {
        foreach (var usable in new[] { false, true })
        {
            var req = Req(Ab(), null) with
            {
                DesktopSlotUsable = usable,
            };
            req = new ProjectionRequest(req.Document, new ProjectionView(A, 10, R(.1, 0, 1, 1), Stretch.Identity),
                req.ParkDisplay, req.Windows) { DesktopSlotUsable = usable };
            var r = ProjectionTargets.Compute(req);
            Near(R(-133, 35, 705, 855), Pl(r, "a").Frame);
            Near(567.0 / 705, ScreenProjection.OnStageFraction(Pl(r, "a").Frame, req.View.Stage));
            Assert.False(Pl(r, "a").OnStage);
            Assert.Equal(TargetKind.Park, r.Targets["a"].Kind);
            Near(R(582, 35, 705, 855), Pl(r, "b").Frame);
            Assert.Equal(Tile(582, 35, 705, 855), r.Targets["b"]);
        }
    }

    [Fact]
    public void AC12_Example9_Floating()
    {
        var fp = new[] { Float("f") };
        ProjectionRequest Make(Rect unit, bool usable)
        {
            var doc = new LayoutDocument(null, fp);
            return new ProjectionRequest(doc, ProjectionView.AtRest(A, 10, null), Display(), Facts(doc))
            {
                FloatingUnitRects = new Dictionary<string, Rect> { ["f"] = unit },
                DesktopSlotUsable = usable,
            };
        }
        var u = R(.9375, 0, .5, .5);
        var r = ProjectionTargets.Compute(Make(u, true));
        var p = Pl(r, "f");
        Near(R(1345.625, 30, 715, 432.5), p.Frame);
        Assert.True(p.Floating);
        Near(0.125, ScreenProjection.OnStageFraction(p.Frame, Stage));
        Assert.True(p.OnStage);
        Assert.Equal(Tile(1346, 30, 715, 433), r.Targets["f"]);

        var r2 = ProjectionTargets.Compute(Make(u, false));
        Assert.False(Pl(r2, "f").OnStage);
        Assert.Equal(Pk(Park(S(705, 855))), r2.Targets["f"]);

        var u3 = R(.984375, 0, .5, .5);
        var r3 = ProjectionTargets.Compute(Make(u3, true));
        Near(0.03125, ScreenProjection.OnStageFraction(Pl(r3, "f").Frame, Stage));
        Assert.False(Pl(r3, "f").OnStage);
        Assert.Equal(TargetKind.Park, r3.Targets["f"].Kind);
    }

    [Fact]
    public void AC16a_PictureFromOwnMinimum()
    {
        var root = X("s", (P("a"), .5), (Y("t", (P("b"), .5), (P("c"), .5)), .5));
        WindowFacts? F(string k) => k == "b" ? With(w => w with { Minimum = S(0, 500) }) : Std;
        var r = Run(root, null, F);
        var b = Pl(r, "b");
        Near(R(725, 35, 705, 422.5), b.Frame);
        Assert.True(b.OnStage); Assert.False(b.Fits);
        Assert.Equal(S(200, 500), ScreenProjection.EffectiveMinimum(false, S(0, 500), Tuning.Default));
        Assert.Equal(S(834, 500), ScreenProjection.PreviewSize(b.Frame, S(200, 500), S(705, 855)));
        var t = r.Targets["b"];
        Assert.Equal(TargetKind.ParkWithPicture, t.Kind);
        Assert.True(t.NeedsPicture);
        Assert.Equal(R(1439, 899, 834, 500), t.Rect);
        Assert.Equal(Park(S(834, 500), S(200, 500)), t.Rect);
        var small = Run(root, null, F, tweak: q => q with { Policy = new ProjectionPolicy { ParkOutOfViewSmall = true } });
        Assert.Equal(t, small.Targets["b"]);
        Assert.Equal(TargetKind.Tile, r.Targets["a"].Kind);
        Assert.Equal(TargetKind.Tile, r.Targets["c"].Kind);
        Assert.False(r.Targets["a"].NeedsPicture);
        Assert.False(r.Targets["c"].NeedsPicture);
    }

    [Fact]
    [Trait("Assumption", "A4")]
    public void AC16b_PictureKeepsMinimum()
    {
        var r = Run(Ab(), null, k => k == "a" ? With(w => w with { Minimum = S(800, 600) }) : Std);
        var a = Pl(r, "a");
        Near(R(10, 35, 705, 855), a.Frame);
        Assert.True(a.OnStage); Assert.False(a.Fits);
        Assert.Equal(S(800, 970), ScreenProjection.PreviewSize(a.Frame, S(800, 600), S(705, 855)));
        Assert.Equal(new WindowTarget(TargetKind.ParkWithPicture, R(1439, 899, 800, 875)), r.Targets["a"]);
    }

    [Fact]
    [Trait("Assumption", "A4")]
    public void AC16c_PictureWiderThanWorkArea()
    {
        var r = Run(Ab(), null, k => k == "a" ? With(w => w with { Minimum = S(1600, 600) }) : Std);
        Assert.Equal(new WindowTarget(TargetKind.ParkWithPicture, R(1439, 899, 1600, 875)), r.Targets["a"]);
        Assert.True(ParkGeometry.IsParkFrame(Display(), r.Targets["a"].Rect));
    }

    [Fact]
    public void AC18_TabStrip()
    {
        LayoutNode Tree(bool tabbed) => X("s", (new PanelNode("P", new[] { "p", "q" }, "p", tabbed), .5), (P("b"), .5));
        var pol = new ProjectionPolicy { TabPadding = 4, TabInset = 8 };
        var r = Run(Tree(true), null, tweak: q => q with { Policy = pol });
        var p = Pl(r, "P");
        Assert.Equal("p", p.Key);
        Near(R(10, 35, 705, 855), p.Frame);
        Assert.Equal(28, p.StripFullHeight);
        Near(R(18, 35, 689, 28), p.Strip!.Value);
        Assert.Equal(Tile(10, 35, 705, 855), r.Targets["p"]);
        Assert.False(r.Targets.ContainsKey("q"));
        Assert.Null(Pl(r, "b").Strip);

        var d = Run(Tree(true), null);
        Near(R(10, 35, 705, 28), Pl(d, "P").Strip!.Value);

        var n = Run(Tree(false), null, tweak: q => q with { Policy = pol });
        Assert.Null(Pl(n, "P").Strip);
        Assert.Equal(0, Pl(n, "P").StripFullHeight);
        Assert.False(n.Targets.ContainsKey("q"));
    }

    private sealed record Hidden : RestoreTarget;

    [Fact]
    public void AC20_NothingToPlaceAndNoTargets()
    {
        // Ex 16
        var e = ProjectionTargets.Compute(new ProjectionRequest(new LayoutDocument(new DesktopNode()),
            ProjectionView.AtRest(A, 10, null), Display(),
            new Dictionary<string, WindowFacts> { ["k"] = Std }));
        Assert.Empty(e.Placements); Assert.Empty(e.Targets);

        // (a)
        var ra = Run(new DesktopNode(Ab()), null);
        Assert.Equal(new[] { "a", "b" }, ra.Placements.Select(p => p.PanelId).ToArray());
        Near(R(10, 35, 705, 855), Pl(ra, "a").Frame);
        Near(R(725, 35, 705, 855), Pl(ra, "b").Frame);

        // (b) hidden panel
        var doc = new LayoutDocument(Ab(), null, new[] { new HiddenPanel(P("h"), new Hidden()) });
        var facts = Facts(doc); facts["h"] = Std;
        var rb = ProjectionTargets.Compute(new ProjectionRequest(doc, ProjectionView.AtRest(A, 10, null), Display(), facts));
        Assert.DoesNotContain(rb.Placements, p => p.PanelId == "h");
        Assert.False(rb.Targets.ContainsKey("h"));

        // (c) no facts for b
        var rc = Run(Ab(), null, k => k == "b" ? null : Std);
        Assert.Equal(2, rc.Placements.Length);
        Assert.Equal(new[] { "a" }, rc.Targets.Keys.ToArray());

        // (d) null / foreign selected
        var tree = X("s", (P("a"), .4), (new PanelNode("n", new[] { "x" }, null, false), .3),
            (new PanelNode("z", new[] { "y" }, "nope", false), .3));
        var rd = Run(tree, null);
        Assert.Equal(new[] { "a" }, rd.Placements.Select(p => p.PanelId).ToArray());

        // (e) float without rect
        var doc2 = new LayoutDocument(null, new[] { Float("f") });
        var re = ProjectionTargets.Compute(new ProjectionRequest(doc2, ProjectionView.AtRest(A, 10, null),
            Display(), Facts(doc2)));
        Assert.Empty(re.Placements);

        // (f) collapsed
        var rf = Run(Ab(), null, tweak: q => q with { CollapsedPanels = new HashSet<string> { "a" } });
        Assert.False(Pl(rf, "a").OnStage);
        Assert.Equal(Pk(Park(S(705, 855))), rf.Targets["a"]);
        Assert.Equal(TargetKind.Tile, rf.Targets["b"].Kind);
    }

    [Fact]
    public void AC22_FloatEndToEnd()
    {
        ProjectionResult Go(Func<WindowFacts, WindowFacts>? fx, bool collapsed)
        {
            var doc = new LayoutDocument(null, new[] { Float("f") });
            var facts = Facts(doc, _ => fx is null ? Std : fx(Std));
            var req = new ProjectionRequest(doc, ProjectionView.AtRest(A, 10, null), Display(), facts)
            {
                FloatingUnitRects = new Dictionary<string, Rect> { ["f"] = R(.1, .1, .05, .05) },
                CollapsedPanels = collapsed ? new HashSet<string> { "f" } : new HashSet<string>(),
            };
            return ProjectionTargets.Compute(req);
        }
        var g = Go(null, false);
        var p = Pl(g, "f");
        Near(R(83.75, 116.5, 200, 43.25 * 200 / 71.5), p.Frame);
        Assert.True(p.Fits); Assert.True(p.OnStage);
        Assert.Equal(TargetKind.Tile, g.Targets["f"].Kind);

        var c = Go(null, true);
        Near(R(148, 116.5, 71.5, 43.25), Pl(c, "f").Frame);
        Assert.False(Pl(c, "f").OnStage);
        Assert.Equal(TargetKind.Park, c.Targets["f"].Kind);

        var n = Go(w => w with { Natural = S(100, 100) }, true);
        Near(R(162.125, 116.5, 43.25, 43.25), Pl(n, "f").Frame);
    }

    [Fact]
    public void AC23_Parking()
    {
        var small = Run(Ab(), R(0, 0, .5, 1), tweak: q => q with { Policy = new ProjectionPolicy { ParkOutOfViewSmall = true } });
        Assert.Equal(R(1439, 899, 240, 292), ParkGeometry.ParkSmallFrame(Display(), S(705, 855)));
        Assert.Equal(Pk(R(1439, 899, 240, 292)), small.Targets["b"]);

        var noLast = Run(Ab(), R(0, 0, .5, 1), k => k == "b" ? new WindowFacts() : Std);
        Assert.Equal(Pk(Park(S(800, 600))), noLast.Targets["b"]);

        var minr = Run(Ab(), R(0, 0, .5, 1), k => k == "b" ? With(w => w with { Minimum = S(1600, 600) }) : Std);
        Assert.Equal(Pk(R(1439, 899, 705, 855)), minr.Targets["b"]);

        var thin = R(0, 25, 1440, 10);
        var rz = Run(Ab(), null, area: thin);
        Assert.Equal(2, rz.Targets.Count);
        Assert.All(rz.Targets.Values, t => Assert.Equal(TargetKind.Park, t.Kind));
        var rz2 = Run(Abc(), R(.5, 0, .5, 1), area: thin);
        Assert.All(rz2.Targets.Values, t => Assert.Equal(TargetKind.Park, t.Kind));
    }

    [Fact]
    public void AC24_DuplicateKey()
    {
        var root = X("s", (new PanelNode("p1", new[] { "k" }, "k", false), .5),
            (new PanelNode("p2", new[] { "k" }, "k", false), .5));
        var r = Run(root, null);
        Assert.Equal(new[] { "p1", "p2" }, r.Placements.Select(p => p.PanelId).ToArray());
        Assert.Single(r.Targets);
        Assert.Equal(Tile(10, 35, 705, 855), r.Targets["k"]);
    }

    [Fact]
    public void AC25_EntriesInput()
    {
        var root = Abc();
        var framed = LayoutSolver.LayoutRects(root)["s2"].Rect;
        var baseR = Run(root, framed);
        var withRange = Run(root, framed, tweak: q => q with { Entries = LayoutSolver.FocusLayout(root) });
        Assert.Equal(baseR.Placements.ToArray(), withRange.Placements.ToArray());
        Assert.Equal(baseR.Targets.OrderBy(k => k.Key).ToArray(), withRange.Targets.OrderBy(k => k.Key).ToArray());

        var lone = Run(Ab(), null, tweak: q => q with { Entries = LayoutSolver.LayoutRects(P("a")) });
        Assert.Single(lone.Placements);
        Near(R(10, 35, 1420, 855), Pl(lone, "a").Frame);
    }

    [Fact]
    public void AC26_ErrorsAndPurity()
    {
        Assert.Throws<ArgumentNullException>(() => ProjectionTargets.Compute(null!));
        var doc = new LayoutDocument(Ab());
        var view = ProjectionView.AtRest(A, 10, null);
        var disp = Display();
        var win = new Dictionary<string, WindowFacts>();
        Assert.Throws<ArgumentNullException>(() => new ProjectionRequest(null!, view, disp, win));
        Assert.Throws<ArgumentNullException>(() => new ProjectionRequest(doc, null!, disp, win));
        Assert.Throws<ArgumentNullException>(() => new ProjectionRequest(doc, view, null!, win));
        Assert.Throws<ArgumentNullException>(() => new ProjectionRequest(doc, view, disp, null!));
        Assert.Throws<ArgumentNullException>(() =>
            new ProjectionRequest(doc, view, disp, new Dictionary<string, WindowFacts> { ["a"] = null! }));
        var req = new ProjectionRequest(doc, view, disp, win);
        Assert.Throws<ArgumentNullException>(() => req with { Policy = null! });
        Assert.Throws<ArgumentNullException>(() => req with { Tuning = null! });
        Assert.Throws<ArgumentNullException>(() => req with { CollapsedPanels = null! });
        Assert.Throws<ArgumentNullException>(() => req with { FloatingUnitRects = null! });
        Assert.Throws<ArgumentNullException>(() => new Stretch(null!, Array.Empty<StretchBreakpoint>()));
        Assert.Throws<ArgumentNullException>(() => new ProjectionView(A, 10, R(0, 0, 1, 1), null!));

        // document unchanged, snapshotting
        var copy = new LayoutDocument(Ab());
        var facts = new Dictionary<string, WindowFacts> { ["a"] = Std, ["b"] = Std };
        var collapsed = new HashSet<string>();
        var floats = new Dictionary<string, Rect>();
        var rq = new ProjectionRequest(doc, view, disp, facts) { CollapsedPanels = collapsed, FloatingUnitRects = floats };
        var before = ProjectionTargets.Compute(rq);
        Assert.Equal(copy, doc);
        facts.Remove("a"); collapsed.Add("b"); floats["zz"] = R(0, 0, 1, 1);
        var after = ProjectionTargets.Compute(rq);
        Assert.Equal(before.Placements.ToArray(), after.Placements.ToArray());
        Assert.Equal(before.Targets.OrderBy(k => k.Key).ToArray(), after.Targets.OrderBy(k => k.Key).ToArray());
        Assert.Equal(2, after.Targets.Count);
    }
}
