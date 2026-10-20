using System.Reflection;
using System.Xml.Linq;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using static CanvasWm.Engine.Tests.EngineShellSpec.Rig;

namespace CanvasWm.Engine.Tests.EngineShellSpec;

public sealed class StubsArchitectureTests
{
    private static readonly string[] StubSigs =
    {
        "FloatAll()", "ToggleDocked(WindowKey)", "SplitForNewWindow(WindowKey)",
        "ToggleMaximize(WindowKey)", "NativePointerDragged(Point)", "NativeMouseUp(Point)",
        "ShowEverything()", "StepOut()", "StepIn(Point)", "StepInto(String)", "Back()", "Forward()",
        "ZoomIn(Point)", "ZoomOut()", "ZoomInto(String)", "GoToFraming(Int32)", "SaveFraming(Int32)",
        "ForgetFraming(Int32)", "ShowDesktop()", "SetDesktopHidden(Boolean)", "ToggleDesktop()",
        "ToggleDesktopHidden()", "Scroll(Double,Double,Boolean,Boolean,Point,GesturePhase,Boolean)",
        "Pinch(Double,Point,GesturePhase,Boolean,Boolean)", "TouchPinchPan(Double,Point,Double,Double)",
        "TouchGestureEnded()", "BeginRectZoom(Point)", "MoveRectZoom(Point)", "EndRectZoom(Point)",
        "ToggleOverview()", "OpenOverviewByTouch()",
    };

    private static string Sig(MethodInfo m) => m.Name + "(" + string.Join(",",
        m.GetParameters().Select(p => Nullable.GetUnderlyingType(p.ParameterType)?.Name ?? p.ParameterType.Name)) + ")";

    [Fact]
    public void AC32_StubsSignaturesAndNoOp()
    {
        var actual = typeof(DisplayEngine).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(Sig).ToHashSet();
        Assert.Equal(31, StubSigs.Length); // migrated for #27 (AC32): Move/ArrangeAll are no longer stubs
        Assert.Contains("Move(Edge)", actual);
        Assert.Contains("ArrangeAll()", actual);
        foreach (var s in StubSigs) Assert.Contains(s, actual);

        var r = new Rig().StartWith(W("w1"));
        r.Clock.Advance(1); // migrated for #27 (AC32): recipe R step 2, let the start landing settle
        r.Win.Pump();
        r.E.WindowFocused(K("w1"));
        r.Log.Clear();
        var snap = r.Snap();
        var k = K("w1");
        var pt = new Point(3, 4);
        var e = r.E;
        e.FloatAll(); e.ToggleDocked(k); e.SplitForNewWindow(k);
        e.ToggleMaximize(null); e.ToggleMaximize(k); e.NativePointerDragged(pt); e.NativeMouseUp(pt);
        e.ShowEverything(); e.StepOut(); e.StepIn(pt); e.StepInto(null!); e.Back(); e.Forward();
        e.ZoomIn(null); e.ZoomIn(pt); e.ZoomOut(); e.ZoomInto("n"); e.GoToFraming(5); e.SaveFraming(-3);
        e.ForgetFraming(99); e.ShowDesktop(); e.SetDesktopHidden(true); e.ToggleDesktop(); e.ToggleDesktopHidden();
        e.Scroll(1, 2, true, false, pt, GesturePhase.Began, false);
        e.Pinch(1.5, pt, GesturePhase.Changed, true, false);
        e.TouchPinchPan(1, pt, 2, 3); e.TouchGestureEnded(); e.BeginRectZoom(pt); e.MoveRectZoom(pt); e.EndRectZoom(pt);
        e.ToggleOverview(); e.OpenOverviewByTouch();
        Assert.Equal(snap, r.Snap());
        Assert.Empty(r.Log);
        Assert.Equal(0, e.ReframeCount);
        Assert.Single(e.Windows);
        Assert.Equal(k, e.FocusedKey);
        Assert.Equal(StagePhase.Desk, e.Phase);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CanvasWm.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void AC34_EngineProjectShapeAndForbiddenNames()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "CanvasWm.Engine", "CanvasWm.Engine.csproj"));
        Assert.Equal("net10.0", doc.Descendants("TargetFramework").Single().Value);
        var refs = doc.Descendants("ProjectReference").ToList();
        Assert.Single(refs);
        Assert.EndsWith("CanvasWm.Core.csproj", (string)refs[0].Attribute("Include")!);
        Assert.Empty(doc.Descendants("PackageReference"));
        Assert.Contains(doc.Descendants("InternalsVisibleTo"), x => (string?)x.Attribute("Include") == "CanvasWm.Engine.Tests");

        var bad = new[] { "Gate", "Locked", "Lesson", "Accomplish" };
        var names = typeof(DisplayEngine).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name);
        Assert.DoesNotContain(names, n => bad.Any(n.Contains));

        string w1 = "Trellis" + "WM", w2 = "Licen" + "sing";
        foreach (var dir in new[] { "src", "tests" })
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot(), dir), "*", SearchOption.AllDirectories))
            {
                var sep = Path.DirectorySeparatorChar;
                if (f.Contains(sep + "bin" + sep) || f.Contains(sep + "obj" + sep)) continue;
                var text = File.ReadAllText(f);
                Assert.False(text.Contains(w1) || text.Contains(w2), f);
            }
        }
    }
}
