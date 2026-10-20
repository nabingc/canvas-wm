using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;
using CanvasWm.Core.Projection;

namespace CanvasWm.Core.Tests.ProjectionSpec;

internal sealed record TestRestore : RestoreTarget;

internal static class Pj
{
    public const double Tol = 1e-9;
    public static readonly Rect A = new(0, 25, 1440, 875);
    public static readonly Rect Stage = new(5, 30, 1430, 865);
    public static readonly Rect DisplayFrame = new(0, 0, 1440, 900);

    public static Rect R(double x, double y, double w, double h) => new(x, y, w, h);
    public static Size S(double w, double h) => new(w, h);

    public static ParkDisplay Display(Rect? area = null) =>
        new(area ?? A, DisplayFrame, Array.Empty<Rect>(), new Point(0, 0));

    public static PanelNode P(string key) => PanelNode.ForWindow(key);

    public static SplitNode X(string id, params (LayoutNode Node, double W)[] c) =>
        new(id, Axis.X, c.Select(t => t.W), c.Select(t => t.Node));

    public static SplitNode Y(string id, params (LayoutNode Node, double W)[] c) =>
        new(id, Axis.Y, c.Select(t => t.W), c.Select(t => t.Node));

    /// <summary>X{s}[a(.5), b(.5)]</summary>
    public static LayoutNode Ab() => X("s", (P("a"), .5), (P("b"), .5));

    /// <summary>X{s1}[a(.5), Y{s2}[b(.5), c(.5)](.5)]</summary>
    public static LayoutNode Abc() =>
        X("s1", (P("a"), .5), (Y("s2", (P("b"), .5), (P("c"), .5)), .5));

    public static void Near(Rect e, Rect a, double tol = Tol)
    {
        Assert.True(Math.Abs(e.X - a.X) <= tol && Math.Abs(e.Y - a.Y) <= tol
                    && Math.Abs(e.Width - a.Width) <= tol && Math.Abs(e.Height - a.Height) <= tol,
            $"expected {e} actual {a}");
    }

    public static void Near(double e, double a, double tol = Tol) =>
        Assert.True(Math.Abs(e - a) <= tol, $"expected {e} actual {a}");

    public static IEnumerable<string> Keys(LayoutDocument doc)
    {
        foreach (var n in Nodes(doc.Root))
            if (n is PanelNode p) foreach (var v in p.Views) yield return v;
        foreach (var f in doc.Floating)
            foreach (var v in f.Panel.Views) yield return v;
    }

    public static IEnumerable<LayoutNode> Nodes(LayoutNode? n)
    {
        if (n is null) yield break;
        yield return n;
        if (n is SplitNode s)
            foreach (var c in s.Children) foreach (var d in Nodes(c)) yield return d;
        if (n is DesktopNode dn && dn.Child is not null)
            foreach (var d in Nodes(dn.Child)) yield return d;
    }

    public static WindowFacts Std => new() { LastFrameSize = S(705, 855) };

    public static Dictionary<string, WindowFacts> Facts(LayoutDocument doc,
        Func<string, WindowFacts?>? f = null)
    {
        var d = new Dictionary<string, WindowFacts>(StringComparer.Ordinal);
        foreach (var k in Keys(doc))
        {
            var w = f is null ? Std : f(k);
            if (w is not null) d[k] = w;
        }
        return d;
    }

    public static ProjectionRequest Req(LayoutNode? root, Rect? framed, Func<string, WindowFacts?>? f = null,
        Rect? area = null, double gap = 10, IEnumerable<FloatingPanel>? floating = null)
    {
        var doc = new LayoutDocument(root, floating);
        var a = area ?? A;
        return new ProjectionRequest(doc, ProjectionView.AtRest(a, gap, framed), Display(a), Facts(doc, f));
    }

    public static ProjectionResult Run(LayoutNode? root, Rect? framed, Func<string, WindowFacts?>? f = null,
        Rect? area = null, double gap = 10, Func<ProjectionRequest, ProjectionRequest>? tweak = null)
    {
        var r = Req(root, framed, f, area, gap);
        if (tweak is not null) r = tweak(r);
        return ProjectionTargets.Compute(r);
    }

    public static PanelPlacement Pl(ProjectionResult r, string id) => r.Placements.Single(p => p.PanelId == id);

    public static Rect Park(Size z, Size? m = null, Rect? area = null) =>
        ParkGeometry.ParkFrame(Display(area), z, m);

    public static WindowFacts With(Func<WindowFacts, WindowFacts> g) => g(Std);
}
