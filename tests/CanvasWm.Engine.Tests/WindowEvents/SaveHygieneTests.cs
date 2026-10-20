using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Services;
using static CanvasWm.Engine.Tests.WindowEventsSpec.Ev;

namespace CanvasWm.Engine.Tests.WindowEventsSpec;

public sealed class SaveTests
{
    private static void AssertLastSaveIsLive(Ev ev)
    {
        var s = ev.LastSaved;
        Assert.Equal(ev.E.Document, s.Document);
        Assert.Equal(ev.E.FramedNodeId, s.FramedNodeId);
    }

    private static void ZeroSaves(Action<Ev> act)
    {
        var ev = S2();
        act(ev);
        ev.Clock.Advance(0);
        Assert.Equal(0, ev.dSaves);
        ev.Clock.Advance(5);
        ev.Win.Pump();
        Assert.Equal(0, ev.dSaves);
    }

    private static void OneSave(Action<Ev> act)
    {
        var ev = S2();
        act(ev);
        Assert.Equal(1, ev.dSaves);
        ev.Win.Pump();
        ev.Clock.Advance(5);
        ev.Win.Pump();
        Assert.Equal(1, ev.dSaves);
        AssertLastSaveIsLive(ev);
    }

    [Fact] public void AC31_FocusSavesNothing() => ZeroSaves(ev => ev.E.WindowFocused(K("b")));
    [Fact] public void AC31_TitleSavesNothing() => ZeroSaves(ev => ev.E.WindowTitleChanged(K("a"), "x"));
    [Fact] public void AC31_BecameDockableSavesNothing() => ZeroSaves(ev => ev.E.WindowBecameDockable(K("n")));
    [Fact] public void AC31_CaptureSavesNothing() => ZeroSaves(ev => ev.E.CaptureArrived(K("a")));
    [Fact] public void AC31_AppActivatedSavesNothing() => ZeroSaves(ev => ev.E.AppActivated(1, false));

    [Fact]
    public void AC31_SnapBackSavesNothing() =>
        ZeroSaves(ev => ev.Move("b", R(300, 200, 705, 422)));

    [Fact]
    public void AC31_EchoSavesNothing() =>
        ZeroSaves(ev =>
        {
            ev.E.WindowAdded(ev.W("c"));
            ev.Mark();
            ev.E.WindowMoved(K("c"), R(1, 2, 3, 4));
        });

    [Fact]
    public void AC31_ParkedRewriteSavesNothing()
    {
        var ev = S2();
        ev.E.Move(Edge.Bottom);
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        ev.Clock.Advance(0.2);
        ev.Win.Pump();
        ev.Mark();
        ev.Move("a", R(400, 300, 705, 855));
        ev.Win.Pump();
        ev.Clock.Advance(5);
        Assert.Equal(0, ev.dSaves);
    }

    [Fact]
    public void AC31_MoveSavesOnlyAtSettle()
    {
        var ev = S2();
        ev.E.Move(Edge.Bottom);
        Assert.Equal(0, ev.dSaves);
        ev.Clock.Advance(0.034);
        ev.Win.Pump();
        Assert.Equal(0, ev.dSaves);
        ev.Clock.Advance(0.2);
        Assert.Equal(1, ev.dSaves);
        ev.Clock.Advance(5);
        Assert.Equal(1, ev.dSaves);
        AssertLastSaveIsLive(ev);
    }

    [Fact] public void AC31_NewWindowSavesOnce() => OneSave(ev => ev.E.WindowAdded(ev.W("c")));
    [Fact] public void AC31_ManagedWindowAddSavesOnce() => OneSave(ev => ev.E.WindowAdded(ev.W("a")));
    [Fact] public void AC31_RemoveSavesOnce() => OneSave(ev => ev.E.WindowRemoved(K("b"), false));
    [Fact] public void AC31_ArrangeAllSavesOnce() => OneSave(ev => ev.E.ArrangeAll());

    [Fact]
    [Trait("Assumption", "A3")]
    public void AC31_SavedCopyCarriesBaseUnchanged()
    {
        var ev = new Ev();
        var framings = new Dictionary<string, IReadOnlyList<string>> { ["1"] = new[] { "a" } };
        ev.Disp.Save(ev.Key, new SavedDisplay(Doc(X("s", H(P("x")), H(P("a")))), null, framings,
            new Dictionary<WindowKey, string> { [K("a")] = "o" }));
        ev.E.Start(new[] { ev.W("a"), ev.W("c") });
        ev.E.WindowAdded(ev.W("e"));
        ev.Win.Pump();
        var s = ev.LastSaved;
        Assert.Equal(ev.E.Document, s.Document);
        Assert.Equal(new[] { "a" }, s.Framings["1"].ToArray());
        Assert.Equal("o", s.Owners[K("a")]);
        Assert.Contains("e", LayoutEdits.PanelsOf(s.Document.Root).SelectMany(p => p.Views)); // A14: nothing filtered
    }
}

public sealed class HygieneTests
{
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "CanvasWm.slnx"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void AC32_RecipeRStartLandingMakesNoFocusCall()
    {
        var ev = new Ev(t0: 99);
        ev.E.Start(new[] { ev.W("w1", false), ev.W("w2", false) });
        ev.Clock.Advance(1);
        ev.Win.Pump();
        Assert.Empty(ev.Win.FocusRequests);
        Assert.Null(ev.E.FocusedKey);
        Assert.Equal(StagePhase.Desk, ev.E.Phase);
        Assert.Equal(100, ev.Clock.Now);
        Assert.Equal(0, ev.Clock.PendingCount);
    }

    [Fact]
    public void AC33_EngineProjectShape()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "src", "CanvasWm.Engine", "CanvasWm.Engine.csproj"));
        Assert.Single(doc.Descendants("ProjectReference"));
        Assert.EndsWith("CanvasWm.Core.csproj", (string)doc.Descendants("ProjectReference").Single().Attribute("Include")!);
        Assert.Empty(doc.Descendants("PackageReference"));
    }

    private static readonly string[] PublicMembersBefore =
    {
        "Boolean DesktopOwnerActive",
        "Boolean FramesRunning",
        "Boolean Holds(CanvasWm.Engine.Services.WindowKey)",
        "Boolean IsStarted",
        "Boolean IsSuspended",
        "Boolean NeedsFrames",
        "Boolean get_DesktopOwnerActive()",
        "Boolean get_FramesRunning()",
        "Boolean get_IsStarted()",
        "Boolean get_IsSuspended()",
        "Boolean get_NeedsFrames()",
        "CanvasWm.Core.Geometry.Rect VisibleArea",
        "CanvasWm.Core.Geometry.Rect get_VisibleArea()",
        "CanvasWm.Core.Parking.ParkDisplay ParkContext()",
        "CanvasWm.Core.Tuning Tuning",
        "CanvasWm.Core.Tuning get_Tuning()",
        "CanvasWm.Engine.EnginePolicy Policy",
        "CanvasWm.Engine.EnginePolicy get_Policy()",
        "CanvasWm.Engine.EngineServices Services",
        "CanvasWm.Engine.EngineServices get_Services()",
        "CanvasWm.Engine.ManagedWindow Window(CanvasWm.Engine.Services.WindowKey)",
        "CanvasWm.Engine.Services.DisplayKey DisplayKey",
        "CanvasWm.Engine.Services.DisplayKey get_DisplayKey()",
        "CanvasWm.Engine.Services.EngineKey Key",
        "CanvasWm.Engine.Services.EngineKey get_Key()",
        "CanvasWm.Engine.Services.WindowKey FocusedKey",
        "CanvasWm.Engine.Services.WindowKey get_FocusedKey()",
        "CanvasWm.Engine.StagePhase Phase",
        "CanvasWm.Engine.StagePhase get_Phase()",
        "System.Action OnChange",
        "System.Action get_OnChange()",
        "System.Action`1[System.Boolean] OnNeedsFrames",
        "System.Action`1[System.Boolean] get_OnNeedsFrames()",
        "System.Action`1[System.String] Log",
        "System.Action`1[System.String] get_Log()",
        "System.Action`2[CanvasWm.Engine.ManagedWindow,System.Boolean] OnHandOff",
        "System.Action`2[CanvasWm.Engine.ManagedWindow,System.Boolean] get_OnHandOff()",
        "System.Collections.Generic.IReadOnlyDictionary`2[CanvasWm.Engine.Services.WindowKey,CanvasWm.Engine.Services.DockSpot] Away",
        "System.Collections.Generic.IReadOnlyDictionary`2[CanvasWm.Engine.Services.WindowKey,CanvasWm.Engine.Services.DockSpot] get_Away()",
        "System.Collections.Generic.IReadOnlyList`1[CanvasWm.Core.Geometry.Rect] OtherDisplays",
        "System.Collections.Generic.IReadOnlyList`1[CanvasWm.Core.Geometry.Rect] get_OtherDisplays()",
        "System.Collections.Generic.IReadOnlyList`1[CanvasWm.Engine.ManagedWindow] Windows",
        "System.Collections.Generic.IReadOnlyList`1[CanvasWm.Engine.ManagedWindow] get_Windows()",
        "System.Func`2[CanvasWm.Engine.Services.WindowKey,System.Boolean] OnCarryOut",
        "System.Func`2[CanvasWm.Engine.Services.WindowKey,System.Boolean] get_OnCarryOut()",
        "System.Nullable`1[CanvasWm.Core.Geometry.Rect] DisplayFrame",
        "System.Nullable`1[CanvasWm.Core.Geometry.Rect] get_DisplayFrame()",
        "System.Nullable`1[System.Double] TopBarBottom",
        "System.Nullable`1[System.Double] get_TopBarBottom()",
        "System.Nullable`1[System.Int32] ActiveProcessId",
        "System.Nullable`1[System.Int32] get_ActiveProcessId()",
        "System.String DebugState",
        "System.String get_DebugState()",
        "Void .ctor(CanvasWm.Engine.Services.EngineKey, CanvasWm.Core.Geometry.Rect, CanvasWm.Engine.EnginePolicy, CanvasWm.Engine.EngineServices)",
        "Void AppActivated(Int32, Boolean)",
        "Void ArrangeAll()",
        "Void Back()",
        "Void BeginRectZoom(CanvasWm.Core.Geometry.Point)",
        "Void CaptureArrived(CanvasWm.Engine.Services.WindowKey)",
        "Void EndRectZoom(CanvasWm.Core.Geometry.Point)",
        "Void FloatAll()",
        "Void ForgetFraming(Int32)",
        "Void Forward()",
        "Void GoToFraming(Int32)",
        "Void Move(CanvasWm.Core.Layout.Edge)",
        "Void MoveRectZoom(CanvasWm.Core.Geometry.Point)",
        "Void NativeMouseUp(CanvasWm.Core.Geometry.Point)",
        "Void NativePointerDragged(CanvasWm.Core.Geometry.Point)",
        "Void OpenOverviewByTouch()",
        "Void Pinch(Double, CanvasWm.Core.Geometry.Point, CanvasWm.Engine.GesturePhase, Boolean, Boolean)",
        "Void Redraw()",
        "Void Release(CanvasWm.Engine.Services.WindowKey)",
        "Void ReleaseAll(System.Nullable`1[CanvasWm.Core.Geometry.Rect], System.Action)",
        "Void Resume(System.Collections.Generic.IReadOnlyList`1[CanvasWm.Engine.Services.WindowInfo])",
        "Void Save()",
        "Void SaveFraming(Int32)",
        "Void Scroll(Double, Double, Boolean, Boolean, CanvasWm.Core.Geometry.Point, CanvasWm.Engine.GesturePhase, Boolean)",
        "Void SetDesktopHidden(Boolean)",
        "Void ShowDesktop()",
        "Void ShowEverything()",
        "Void SplitForNewWindow(CanvasWm.Engine.Services.WindowKey)",
        "Void Start(System.Collections.Generic.IReadOnlyList`1[CanvasWm.Engine.Services.WindowInfo])",
        "Void StepIn(CanvasWm.Core.Geometry.Point)",
        "Void StepInto(System.String)",
        "Void StepOut()",
        "Void Suspend()",
        "Void Tick(Double)",
        "Void ToggleDesktop()",
        "Void ToggleDesktopHidden()",
        "Void ToggleDocked(CanvasWm.Engine.Services.WindowKey)",
        "Void ToggleMaximize(CanvasWm.Engine.Services.WindowKey)",
        "Void ToggleOverview()",
        "Void TouchGestureEnded()",
        "Void TouchPinchPan(Double, CanvasWm.Core.Geometry.Point, Double, Double)",
        "Void VisibleAreaChanged(CanvasWm.Core.Geometry.Rect)",
        "Void WindowAdded(CanvasWm.Engine.Services.WindowInfo)",
        "Void WindowBecameDockable(CanvasWm.Engine.Services.WindowKey)",
        "Void WindowFocused(CanvasWm.Engine.Services.WindowKey)",
        "Void WindowMoved(CanvasWm.Engine.Services.WindowKey, CanvasWm.Core.Geometry.Rect)",
        "Void WindowRemoved(CanvasWm.Engine.Services.WindowKey, Boolean)",
        "Void WindowTitleChanged(CanvasWm.Engine.Services.WindowKey, System.String)",
        "Void ZoomIn(System.Nullable`1[CanvasWm.Core.Geometry.Point])",
        "Void ZoomInto(System.String)",
        "Void ZoomOut()",
        "Void set_DisplayFrame(System.Nullable`1[CanvasWm.Core.Geometry.Rect])",
        "Void set_Log(System.Action`1[System.String])",
        "Void set_OnCarryOut(System.Func`2[CanvasWm.Engine.Services.WindowKey,System.Boolean])",
        "Void set_OnChange(System.Action)",
        "Void set_OnHandOff(System.Action`2[CanvasWm.Engine.ManagedWindow,System.Boolean])",
        "Void set_OnNeedsFrames(System.Action`1[System.Boolean])",
        "Void set_OtherDisplays(System.Collections.Generic.IReadOnlyList`1[CanvasWm.Core.Geometry.Rect])",
        "Void set_Policy(CanvasWm.Engine.EnginePolicy)",
        "Void set_TopBarBottom(System.Nullable`1[System.Double])",
        "Void set_Tuning(CanvasWm.Core.Tuning)"
    };

    [Fact]
    public void AC33_PublicSurfaceOfDisplayEngineUnchanged()
    {
        var now = typeof(DisplayEngine).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.ToString()!).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(PublicMembersBefore, now);
    }

    [Fact]
    public void AC33_PolicyShape()
    {
        var props = typeof(EnginePolicy).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.Name != "EqualityContract").ToList();
        Assert.Equal(38, props.Count);
        Assert.Contains(props, p => p.Name == "FloatNewWindowsOnDesktop");
        Assert.DoesNotContain(props, p => p.Name == "MaxDocked" + "Tiles");
    }

    [Fact]
    public void AC33_WindowEventsIsNotPublicAndConstantsMatch()
    {
        Assert.False(typeof(WindowEvents).IsPublic);
        Assert.Equal(
            new[] { 1.0, 1.0, 1.0, 0.5, 100, 60, 2, 100, 0.92, 0.90, 4, 2, 0.8, 480, 320 },
            new[]
            {
                WindowEvents.RevealQuietSeconds, WindowEvents.CloseQuietSeconds, WindowEvents.ZoomToggleSpacingSeconds,
                WindowEvents.ResizedThreshold, WindowEvents.GrowWidthMargin, WindowEvents.GrowHeightMargin,
                WindowEvents.GrowKeepSlack, WindowEvents.ShrinkMargin, WindowEvents.FillsWidthFraction,
                WindowEvents.FillsHeightFraction, WindowEvents.UnzoomDrift, WindowEvents.InsistSizeSlack,
                WindowEvents.InsistScreenFactor, WindowEvents.DesktopSlotMinWidth, WindowEvents.DesktopSlotMinHeight,
            });
    }

    [Fact]
    public void AC33_EventsSourceReadsTuningNotLiterals()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "CanvasWm.Engine", "DisplayEngine.Events.cs"));
        var code = Regex.Replace(text, @"//.*|/\*[\s\S]*?\*/", "");
        Assert.DoesNotMatch(@"(?<![\w.])0\.6(?![\d])", code);
        Assert.DoesNotMatch(@"SnapBack\w*\s*(<|>|==|<=|>=)\s*3", code);
        Assert.Contains("SnapBackLimit", code);
        Assert.Contains("SnapBackWindowSeconds", code);
        Assert.Contains("LandingRevealQuietSeconds", code);
    }

    [Fact]
    public void AC33_ForbiddenNamesAbsent()
    {
        string a = "Trellis" + "WM", b = "Licen" + "sing", c = "MaxDocked" + "Tiles";
        foreach (var dir in new[] { "src", "tests" })
        {
            foreach (var f in Directory.EnumerateFiles(Path.Combine(RepoRoot(), dir), "*", SearchOption.AllDirectories))
            {
                var sep = Path.DirectorySeparatorChar;
                if (f.Contains(sep + "bin" + sep) || f.Contains(sep + "obj" + sep)) continue;
                var text = File.ReadAllText(f);
                Assert.False(text.Contains(a) || text.Contains(b) || text.Contains(c), f);
            }
        }
    }
}
