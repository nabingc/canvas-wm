using System.Reflection;
using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;
using static CanvasWm.Engine.Tests.EngineShellSpec.Rig;

namespace CanvasWm.Engine.Tests.EngineShellSpec;

public sealed class ConstructionPolicyTests
{
    [Fact]
    public void AC1_InitialState_AndConstructorValidation()
    {
        var r = new Rig();
        var e = r.E;
        Assert.False(e.IsStarted);
        Assert.False(e.IsSuspended);
        Assert.Equal(StagePhase.Desk, e.Phase);
        Assert.Empty(e.Windows);
        Assert.False(e.FramesRunning);
        Assert.False(e.NeedsFrames);
        Assert.Equal(100, e.LastTick);
        Assert.Null(e.DisplayFrame);
        Assert.Empty(e.OtherDisplays);
        Assert.Null(e.TopBarBottom);
        Assert.Equal(r.Key.Display, e.DisplayKey);
        Assert.Null(e.FocusedKey);
        Assert.Empty(e.Away);
        Assert.Null(e.ActiveProcessId);
        Assert.False(e.DesktopOwnerActive);
        Assert.Equal(VA, e.VisibleArea);
        Assert.Contains(r.Key.Value, e.DebugState);
        Assert.Equal("0|0|0|0|0|0|0|-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1", r.Snap());
        Assert.Empty(r.Log);

        var s = r.Services;
        Assert.Throws<ArgumentNullException>(() => new DisplayEngine(null!, VA, EnginePolicy.Default, s));
        Assert.Throws<ArgumentNullException>(() => new DisplayEngine(r.Key, VA, null!, s));
        Assert.Throws<ArgumentNullException>(() => new DisplayEngine(r.Key, VA, EnginePolicy.Default, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayEngine(r.Key, new Rect(double.NaN, 0, 10, 10), EnginePolicy.Default, s));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayEngine(r.Key, new Rect(0, 0, -1, 10), EnginePolicy.Default, s));
        Assert.Throws<ArgumentNullException>(() => new EngineServices(null!, r.Cap, r.Clock, r.Ren, r.Lim, r.Disp));
        Assert.Throws<ArgumentNullException>(() => new EngineServices(r.Win, null!, r.Clock, r.Ren, r.Lim, r.Disp));
        Assert.Throws<ArgumentNullException>(() => new EngineServices(r.Win, r.Cap, null!, r.Ren, r.Lim, r.Disp));
        Assert.Throws<ArgumentNullException>(() => new EngineServices(r.Win, r.Cap, r.Clock, null!, r.Lim, r.Disp));
        Assert.Throws<ArgumentNullException>(() => new EngineServices(r.Win, r.Cap, r.Clock, r.Ren, null!, r.Disp));
        Assert.Throws<ArgumentNullException>(() => new EngineServices(r.Win, r.Cap, r.Clock, r.Ren, r.Lim, null!));
    }

    [Fact]
    public void AC2_DisplayContext_And_ParkContext()
    {
        var r = new Rig();
        var before = r.Snap();
        var list = new List<Rect> { new(1440, 0, 800, 600), new(-800, 0, 800, 600) };
        r.E.DisplayFrame = new Rect(0, 0, 1440, 900);
        r.E.OtherDisplays = list;
        list.Clear();
        r.E.TopBarBottom = 24;
        Assert.Equal(new Rect(0, 0, 1440, 900), r.E.DisplayFrame);
        Assert.Equal(new[] { new Rect(1440, 0, 800, 600), new Rect(-800, 0, 800, 600) }, r.E.OtherDisplays);
        Assert.Equal(24, r.E.TopBarBottom);
        Assert.Equal(before, r.Snap());
        Assert.Empty(r.Log);
        Assert.Throws<ArgumentNullException>(() => r.E.OtherDisplays = null!);

        var pc = r.E.ParkContext();
        Assert.Equal(VA, pc.VisibleArea);
        Assert.Equal(new Rect(0, 0, 1440, 900), pc.DisplayFrame);
        Assert.Equal(r.E.OtherDisplays, pc.OtherDisplays);
        Assert.Equal(r.Win.ParkingOrigin, pc.FallbackOrigin);
    }

    [Fact]
    public void AC3_PolicyDefaults()
    {
        var p = EnginePolicy.Default;
        Assert.Equal(p, new EnginePolicy());
        Assert.Equal(10, p.Gap);
        Assert.False(p.FixedMinimum);
        Assert.False(p.SqueezeBelowMinimum);
        Assert.True(p.ShareRemovedSpace);
        Assert.Null(p.MaxDockedTiles);
        Assert.True(p.CollapseSmallGroups);
        Assert.True(p.SoleFloatTakesDesktop);
        Assert.False(p.ParkOutOfViewSmall);
        Assert.True(p.TabsAllowed);
        Assert.Equal(TabStyle.Standard, p.TabStyle);
        Assert.Equal(0, (int)p.TabStyle);
        Assert.Equal(0, p.TabTint);
        Assert.Equal(4, p.TabPadding);
        Assert.True(p.TabCloseButtons);
        Assert.True(p.TabCloseOnRight);
        Assert.Equal(0, p.TabInset);
        Assert.False(p.TuckStrips);
        Assert.False(p.GesturesLand);
        Assert.True(p.TouchPinchPan);
        Assert.False(p.InterruptLandings);
        Assert.False(p.KeepPushed);
        Assert.False(p.PreviewDrags);
        Assert.True(p.DeskGapHandles);
        Assert.Equal(0.15, p.DropHold);
        Assert.False(p.ReflowWhileDragging);
        Assert.True(p.SavedViewsAllowed);
        Assert.False(p.OverviewSurfaced);
        Assert.Equal(0.2, p.DesktopEdge);
        Assert.Equal(PreviewStyle.Standard, p.PreviewStyle);
        Assert.Equal(1, (int)p.PreviewStyle);
        Assert.Equal(0.72, p.TintOpacity);
        Assert.Equal(0.14, p.CoverFade);
        Assert.True(p.CoverIcon);
        Assert.Equal(8, p.WindowCornerRadius);
        Assert.Equal(1, (int)p.ProxyScale);
        Assert.Equal(1, (int)p.ResizeCover);
        Assert.Equal(12, p.BlurRadius);
        Assert.True(p.DimDocked);
        Assert.Equal(0.25, p.DimOpacity);
        Assert.Equal(16, p.DimBlur);

        var props = typeof(EnginePolicy).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(x => x.Name != "EqualityContract").ToList();
        Assert.Equal(38, props.Count);
        Assert.DoesNotContain(props, x => x.Name == "TabLights");
    }

    [Fact]
    public void AC4_RequiresRelayout()
    {
        var d = EnginePolicy.Default;
        Assert.True(EnginePolicy.RequiresRelayout(d, d with { Gap = 12 }));
        Assert.True(EnginePolicy.RequiresRelayout(d, d with { FixedMinimum = true }));
        Assert.True(EnginePolicy.RequiresRelayout(d, d with { TabPadding = 6 }));
        Assert.False(EnginePolicy.RequiresRelayout(d, d with { TabTint = 0.5 }));
        Assert.False(EnginePolicy.RequiresRelayout(d, d with { DimOpacity = 0.5 }));
        Assert.False(EnginePolicy.RequiresRelayout(d, d with { ShareRemovedSpace = false }));
        Assert.False(EnginePolicy.RequiresRelayout(d, new EnginePolicy()));
        Assert.Throws<ArgumentNullException>(() => EnginePolicy.RequiresRelayout(null!, d));
        Assert.Throws<ArgumentNullException>(() => EnginePolicy.RequiresRelayout(d, null!));
    }

    [Fact]
    public void AC29_PolicySet()
    {
        var r = new Rig().StartWith(W("w1"));
        r.Log.Clear();
        int renders = r.Ren.Plans.Count, writes = r.Win.Writes.Count;

        r.E.Policy = r.E.Policy with { TabTint = 0.5 };
        Assert.Equal(renders + 1, r.Ren.Plans.Count);
        Assert.Equal(0, r.E.ReframeCount);
        Assert.Equal(writes, r.Win.Writes.Count);
        Assert.Equal(new[] { "change" }, r.Log);

        r.Log.Clear();
        r.E.Policy = r.E.Policy with { Gap = 12 };
        Assert.Equal(1, r.E.ReframeCount);
        Assert.Equal(new[] { "change" }, r.Log);

        r.Log.Clear();
        renders = r.Ren.Plans.Count;
        r.E.Policy = r.E.Policy with { };
        Assert.Equal(renders, r.Ren.Plans.Count);
        Assert.Empty(r.Log);
        Assert.Throws<ArgumentNullException>(() => r.E.Policy = null!);

        // not started
        var n = new Rig();
        n.E.Policy = n.E.Policy with { Gap = 20, TabTint = 1 };
        Assert.Equal(20, n.E.Policy.Gap);
        Assert.Empty(n.Ren.Plans);
        Assert.Equal(0, n.E.ReframeCount);
        Assert.Equal(new[] { "change" }, n.Log);

        // suspended
        var s = new Rig().StartWith(W("w1"));
        s.E.Suspend();
        s.Log.Clear();
        int rc = s.Ren.Plans.Count;
        s.E.Policy = s.E.Policy with { Gap = 30 };
        Assert.Equal(30, s.E.Policy.Gap);
        Assert.Equal(rc, s.Ren.Plans.Count);
        Assert.Equal(0, s.E.ReframeCount);
        Assert.Equal(new[] { "change" }, s.Log);
    }

    [Fact]
    public void AC15_ReleaseGeometry()
    {
        var s = new Size(300, 200);
        Assert.Equal(ReleaseGeometry.Cascade(VA, s, 0), ReleaseGeometry.Cascade(VA, s, 8));
        Assert.Equal(ReleaseGeometry.Cascade(VA, s, 1), ReleaseGeometry.Cascade(VA, s, 9));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReleaseGeometry.Cascade(VA, s, -1));
        Assert.Equal(new Rect(60, 65, 640, 420), ReleaseGeometry.Cascade(VA, s, 0));
        Assert.Equal(new Rect(92, 97, 640, 420), ReleaseGeometry.Cascade(VA, s, 1));
        Assert.Equal(new Rect(320, 162.5, 800, 600), ReleaseGeometry.Centre(VA, new Size(800, 600)));
        Assert.Equal(new Rect(-50, 25, 200, 50), ReleaseGeometry.Centre(new Rect(0, 0, 100, 100), new Size(200, 50)));
        // E3: large window limited to 0.7 of area
        var big = ReleaseGeometry.Cascade(new Rect(0, 0, 1000, 1000), new Size(5000, 5000), 0);
        Assert.Equal(new Rect(60, 40, 700, 700), big);
    }
}
