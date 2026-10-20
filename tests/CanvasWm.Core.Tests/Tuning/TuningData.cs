using System.Reflection;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Tests.TuningSpec;

internal static class TuningData
{
    public static readonly string[] Pos =
    {
        "CameraMoveSeconds", "CameraEaseRate", "CameraMaxStepSeconds", "LayoutTweenSeconds", "ZoomStepDelta",
        "NativeTabWatchSeconds", "SnapBackWindowSeconds", "ChangingPicturesPerSecond", "MovingPicturesPerSecond",
        "LivePicturesPerSecond", "PreviewPicturesPerSecond", "UnseenPicturesPerSecond", "AwayPicturesPerSecond",
    };

    public static readonly string[] NonNeg =
    {
        "DragStartDistance", "PinchStartDelta", "TouchPinchStartDistance", "TouchPanStartDistance", "TouchPanFactor",
        "PickupSeconds", "DesktopEdgeBandMax", "SeamHitMinWidth", "GapHandleHitSlop", "GapDragRestSeconds",
        "DisplayHandOffDistance", "GroupHoverSeconds", "GroupHoverSlop", "CompactGrabHeight", "SqueezeFloor",
        "GroupCollapseBelow", "FloatEdgePadding", "TabStripContentHeight", "TabStripUnderlap", "TabStripButtonReserve",
        "NativeTabRecheckSeconds", "OverviewTopMargin", "OverviewBottomMargin", "CoverAspectMismatch",
        "UncoverAspectMismatch", "ReflowTimeoutSeconds", "LandingRevealQuietSeconds", "FreshPictureWaitSeconds",
        "FrameRefusalSlack", "FrameRefusalRecheckSeconds",
    };

    public static readonly string[] Band = { "TileEdgeBandFraction" };
    public static readonly string[] Cap = { "FloatMaxFraction" };
    public static readonly string[] SizeNonNeg = { "FixedTileMinimum", "TileMinimumFloor", "CompactSize" };
    public static readonly string[] IntAtLeast1 = { "SnapBackLimit" };
    public static readonly string[] IntNonNeg = { "CollapsedOutlineWidth" };

    public static readonly Dictionary<string, object> Defaults = new()
    {
        ["CameraMoveSeconds"] = 0.5, ["CameraEaseRate"] = 5.5, ["CameraMaxStepSeconds"] = 0.032,
        ["LayoutTweenSeconds"] = 0.46,
        ["DragStartDistance"] = 5.0, ["PinchStartDelta"] = 0.04, ["ZoomStepDelta"] = 0.15,
        ["TouchPinchStartDistance"] = 0.06, ["TouchPanStartDistance"] = 0.03, ["TouchPanFactor"] = 1.3,
        ["PickupSeconds"] = 0.28, ["TileEdgeBandFraction"] = 0.28, ["DesktopEdgeBandMax"] = 64.0,
        ["SeamHitMinWidth"] = 8.0, ["GapHandleHitSlop"] = 5.0, ["GapDragRestSeconds"] = 0.15,
        ["DisplayHandOffDistance"] = 16.0, ["GroupHoverSeconds"] = 0.2, ["GroupHoverSlop"] = 6.0,
        ["FixedTileMinimum"] = new Size(480, 320), ["TileMinimumFloor"] = new Size(200, 120),
        ["CompactSize"] = new Size(380, 260), ["CompactGrabHeight"] = 18.0, ["SqueezeFloor"] = 120.0,
        ["GroupCollapseBelow"] = 120.0, ["CollapsedOutlineWidth"] = 2, ["FloatMaxFraction"] = 2.0 / 3.0,
        ["FloatEdgePadding"] = 16.0,
        ["TabStripContentHeight"] = 20.0, ["TabStripUnderlap"] = 10.0, ["TabStripButtonReserve"] = 72.0,
        ["NativeTabWatchSeconds"] = 0.25, ["NativeTabRecheckSeconds"] = 0.3,
        ["OverviewTopMargin"] = 28.0, ["OverviewBottomMargin"] = 96.0,
        ["ChangingPicturesPerSecond"] = 10.0, ["MovingPicturesPerSecond"] = 3.0,
        ["LivePicturesPerSecond"] = 0.2, ["PreviewPicturesPerSecond"] = 1.0 / 6.0,
        ["UnseenPicturesPerSecond"] = 1.0 / 30.0, ["AwayPicturesPerSecond"] = 1.0 / 60.0,
        ["CoverAspectMismatch"] = 0.01, ["UncoverAspectMismatch"] = 0.005, ["ReflowTimeoutSeconds"] = 1.2,
        ["LandingRevealQuietSeconds"] = 0.6, ["FreshPictureWaitSeconds"] = 0.35, ["FrameRefusalSlack"] = 24.0,
        ["FrameRefusalRecheckSeconds"] = 0.25, ["SnapBackLimit"] = 3, ["SnapBackWindowSeconds"] = 3.0,
    };

    public static PropertyInfo[] InstanceProps() =>
        typeof(Tuning).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract").ToArray();

    public static IEnumerable<string> AllClassed() =>
        Pos.Concat(NonNeg).Concat(Band).Concat(Cap).Concat(SizeNonNeg).Concat(IntAtLeast1).Concat(IntNonNeg);

    /// <summary>Set via the init accessor on a fresh instance (initialiser form) or on a clone (with form).</summary>
    public static Tuning Apply(bool useWith, string name, object value)
    {
        var target = useWith ? Tuning.Default with { } : new Tuning();
        try
        {
            typeof(Tuning).GetProperty(name)!.SetMethod!.Invoke(target, new[] { value });
        }
        catch (TargetInvocationException ex)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
        }
        return target;
    }

    public static IEnumerable<object[]> Rows(IEnumerable<string> names, params object[] values)
    {
        foreach (var n in names)
            foreach (var v in values)
                foreach (var w in new[] { false, true })
                    yield return new[] { n, v, w };
    }
}
