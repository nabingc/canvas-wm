# Spec: engine-projection-targets

## Goal

Feature #25 (carved out of #13). One pure Core computation turns a layout
document into a **target for each visible window**:

1. **Project** each tile's unit-square rect onto the display's **stage** (the
   work area shrunk by half a gap on every side). A **camera rect** and a
   **stretch** decide what part of unit space fills the stage. At rest both
   come from the framed node: its short side is stretched so that it becomes a
   square, and the camera is that square.
2. Shrink each docked tile by half a gap on every side (floats get no inset,
   but get an aspect fit and a grow-to-minimum).
3. Classify each panel's selected window: **on stage** (≥ 99.5 % of its frame
   inside the stage; floats > 5 % when the desktop slot is usable) and
   **fits** (each axis ≥ minimum − 0.5).
4. Choose the outcome per window: **Tile** (frame clamped to a known maximum,
   centred, edges rounded), **Park** (off stage; park-geometry frame of its
   last size, no minimum), **ParkWithPicture** (on stage but too small; park
   at the tile scaled up uniformly to the minimum, with that minimum passed to
   park geometry so the park size never drops below it; flagged so a later
   feature can draw a picture), or **no target**.
5. Report, per panel, the tab-strip rect at rest (data only; drawing is the
   renderer's job).

The computation is **camera-ready**: it takes the camera rect and stretch as
inputs. #15 (camera/motion) will supply animated values; this feature only
provides the at-rest helper.

Out of scope (deferred, each with its owner):
- Collapsed-group detection and the rects/pictures of collapsed panels (#28).
  Here: an input set of collapsed panel ids, empty until #28.
- Writing frames, skip rules, echo handling (#26); window events (#27).
- Animated camera / visual stretch, strip reveal fraction, strip override
  rects (#15 / #20). At rest: reveal 1, no override.
- Drawing pictures / covers (#15 / #16). Here: a flag only.
- Floating-panel unit rects (floating feature) and the floating 0.98 ×
  natural-size minimum branch (A9, A10).
- **Excluded by human decision:** the studied app's "skip-store" side path in
  the too-small branch (parking at the clamped tile size when the window has
  no own minimum but a stored per-app minimum exists). CanvasWM always parks
  at the scaled-up preview size. No per-app limit store input exists here.

## Placement

- Project: `src/CanvasWm.Core` (`net10.0`, BCL only). No new project or
  package references. No I/O, no OS access, no Engine types.
- Namespace (new): **`CanvasWm.Core.Projection`**. No type in it may be named
  `Projection` (namespace/type clash).
- Suggested files: `src/CanvasWm.Core/Projection/StretchBreakpoint.cs`,
  `Stretch.cs`, `ProjectionView.cs`, `ProjectionPolicy.cs`, `WindowFacts.cs`,
  `ProjectionRequest.cs`, `PanelPlacement.cs`, `WindowTarget.cs`,
  `ProjectionResult.cs`, `ScreenProjection.cs`, `ProjectionTargets.cs`.
- Tests: `tests/CanvasWm.Core.Tests/Projection/` — suggested
  `StretchTests.cs`, `ScreenProjectionTests.cs`, `ProjectionTargetsTests.cs`,
  `ProjectionPropertyTests.cs`.
- Depends on (all **unchanged**):
  - `specs/layout-tree.md`: `Rect`, `Size`, `LayoutNode`, `SplitNode`,
    `PanelNode` (`Selected`, `Views`, `HasStrip`), `DesktopNode`,
    `FloatingPanel`, `LayoutDocument`.
  - `specs/layout-solver.md`: `LayoutSolver.LayoutRects`, `LayoutEntries`,
    `LayoutEntry` (weights-mode unit rects, looked up by panel id).
  - `specs/park-geometry.md`: `ParkGeometry.ParkFrame`,
    `ParkGeometry.ParkSmallFrame`, `ParkGeometry.IntersectionArea`,
    `ParkDisplay`, `ParkOptions`.
  - `specs/tuning-constants.md`: `Tuning.TileMinimumFloor`,
    `Tuning.FixedTileMinimum`, `Tuning.TabStripContentHeight`.
- Engine values (gap, fixed-minimum, tab padding, tab inset, park-small,
  desktop-slot usable) are passed as plain values. The Engine (#26) maps
  `EnginePolicy` onto `ProjectionView.Gap` and `ProjectionPolicy`, and
  `WindowKey.Value` onto the string keys used here.
- Units: DIPs, y-down, origin top-left of the virtual desktop. "Round" means
  `Math.Round(v, MidpointRounding.AwayFromZero)` everywhere in this spec.

## Public API

All public types and members carry XML docs. String keys are compared
**ordinally**. A non-nullable reference argument that is null throws
`ArgumentNullException`.

```csharp
namespace CanvasWm.Core.Projection;

using System.Collections.Immutable;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Parking;

/// <summary>One breakpoint of a piecewise-linear axis map: unit value From maps to To.</summary>
public readonly record struct StretchBreakpoint(double From, double To);

/// <summary>Per-axis piecewise-linear maps of unit space (B3, B4). Immutable.
/// Equality: structural (breakpoint lists compared element-wise).</summary>
public sealed record Stretch
{
    /// <summary>No breakpoints on either axis: the identity map.</summary>
    public static Stretch Identity { get; }

    /// <exception cref="ArgumentNullException">x or y is null.</exception>
    /// <exception cref="ArgumentException">a list has a non-finite value, or its From values
    /// are not strictly increasing.</exception>
    public Stretch(IEnumerable<StretchBreakpoint> x, IEnumerable<StretchBreakpoint> y);

    public ImmutableArray<StretchBreakpoint> X { get; }
    public ImmutableArray<StretchBreakpoint> Y { get; }

    /// <summary>Maps a unit x value through X (B3).</summary>
    public double MapX(double value);
    /// <summary>Maps a unit y value through Y (B3).</summary>
    public double MapY(double value);

    /// <summary>Piecewise-linear map through a breakpoint list (B3). Empty list → value.</summary>
    public static double Map(ImmutableArray<StretchBreakpoint> breakpoints, double value);

    /// <summary>The at-rest stretch that makes the framed rect square (B4). Null, degenerate or
    /// already square → Identity.</summary>
    public static Stretch ForFraming(Rect? framed);
}

/// <summary>The camera-ready projection inputs for one display (B1, B2, B5). Immutable.</summary>
public sealed record ProjectionView
{
    /// <summary>Stretch tolerance: |F.w − F.h| at or below this is square. 1e-9.</summary>
    public const double SquareTolerance = 1e-9;

    /// <param name="workArea">Work area of the display, DIPs. Not validated.</param>
    /// <param name="gap">Gap between tiles, DIPs. NaN, ±∞ or negative is stored as 0 (B1).</param>
    /// <param name="camera">Camera rect in unit space.</param>
    /// <param name="stretch">Stretch applied to unit values before the camera.</param>
    /// <exception cref="ArgumentOutOfRangeException">camera has a non-finite component, or its
    /// Width or Height is not &gt; 0.</exception>
    /// <exception cref="ArgumentNullException">stretch is null.</exception>
    public ProjectionView(Rect workArea, double gap, Rect camera, Stretch stretch);

    public Rect WorkArea { get; }
    public double Gap { get; }
    public Rect Camera { get; }
    public Stretch Stretch { get; }
    /// <summary>WorkArea inset by Gap/2 on each side (B2).</summary>
    public Rect Stage { get; }

    /// <summary>At-rest view: Stretch.ForFraming(framed) and
    /// ScreenProjection.CameraForFraming(framed) (B4, B5).</summary>
    /// <param name="framed">Unit rect of the framed node (weights-mode entry, range entries
    /// allowed), or null when nothing is framed.</param>
    public static ProjectionView AtRest(Rect workArea, double gap, Rect? framed);
}

/// <summary>Engine policy values this computation reads (passed in, not read from the
/// Engine). Plain values; out-of-range numbers are clamped where they are used (B12).</summary>
public sealed record ProjectionPolicy
{
    /// <summary>All defaults below (they equal EnginePolicy's defaults).</summary>
    public static ProjectionPolicy Default { get; }
    /// <summary>Use Tuning.FixedTileMinimum instead of Tuning.TileMinimumFloor. Default false.</summary>
    public bool FixedMinimum { get; init; } = false;
    /// <summary>Tab padding, DIPs. Default 4.</summary>
    public double TabPadding { get; init; } = 4;
    /// <summary>Largest side inset of a tab strip, DIPs. Default 0.</summary>
    public double TabInset { get; init; } = 0;
    /// <summary>Park off-stage windows at the park-small size. Default false.</summary>
    public bool ParkOutOfViewSmall { get; init; } = false;
}

/// <summary>What is known about one managed window. All members optional.</summary>
public sealed record WindowFacts
{
    /// <summary>Facts with every member null.</summary>
    public static WindowFacts None { get; }
    /// <summary>Size of the window's last known frame (park size, B20).</summary>
    public Size? LastFrameSize { get; init; }
    /// <summary>The window's own (learned) minimum size.</summary>
    public Size? Minimum { get; init; }
    /// <summary>The window's maximum size.</summary>
    public Size? Maximum { get; init; }
    /// <summary>Natural size; used only for the aspect fit of floating frames (B9).</summary>
    public Size? Natural { get; init; }
}

/// <summary>Everything one projection pass needs.</summary>
public sealed record ProjectionRequest
{
    /// <param name="document">The display's layout document.</param>
    /// <param name="view">Work area, gap, camera and stretch.</param>
    /// <param name="parkDisplay">Display context for park frames.</param>
    /// <param name="windows">Managed windows by key. A placement whose key is absent gets no
    /// target (B17). The constructor snapshots the dictionary with an ordinal comparer.</param>
    /// <exception cref="ArgumentNullException">any argument, or any dictionary value, is null.</exception>
    public ProjectionRequest(LayoutDocument document, ProjectionView view,
                             ParkDisplay parkDisplay,
                             IReadOnlyDictionary<string, WindowFacts> windows);

    public LayoutDocument Document { get; }
    public ProjectionView View { get; }
    public ParkDisplay ParkDisplay { get; }
    public IReadOnlyDictionary<string, WindowFacts> Windows { get; }

    /// <summary>Default ProjectionPolicy.Default. Null → ArgumentNullException.</summary>
    public ProjectionPolicy Policy { get; init; }
    /// <summary>Default Tuning.Default. Null → ArgumentNullException.</summary>
    public Tuning Tuning { get; init; }
    /// <summary>Park options; null → ParkOptions.Default.</summary>
    public ParkOptions? ParkOptions { get; init; }
    /// <summary>Panel ids of collapsed groups (#28). Default empty. Snapshotted, ordinal.
    /// Null → ArgumentNullException.</summary>
    public IReadOnlySet<string> CollapsedPanels { get; init; }
    /// <summary>True when the desktop slot is usable (engine rule). Default false.</summary>
    public bool DesktopSlotUsable { get; init; }
    /// <summary>Unit rects of floating panels by panel id (supplied by the floating feature).
    /// Default empty; a floating panel without a rect here gets no placement (A9).
    /// Snapshotted, ordinal. Null → ArgumentNullException.</summary>
    public IReadOnlyDictionary<string, Rect> FloatingUnitRects { get; init; }
    /// <summary>Weights-mode entries to look panel rects up in. Null (default) →
    /// LayoutSolver.LayoutRects(Document.Root) is computed.</summary>
    public LayoutEntries? Entries { get; init; }
}

/// <summary>One placement per panel (B10–B16).</summary>
/// <param name="PanelId">The panel's id.</param>
/// <param name="Key">The panel's selected window key.</param>
/// <param name="Frame">Un-rounded, un-clamped frame (B7 docked / B9 floating).</param>
/// <param name="Floating">True for a floating panel.</param>
/// <param name="OnStage">B13.</param>
/// <param name="Fits">B15.</param>
/// <param name="Strip">Tab-strip rect at rest, or null when the panel has no strip (B12).</param>
/// <param name="StripFullHeight">Full strip height 2·TabPadding + TabStripContentHeight for
/// a panel with a strip; 0 otherwise.</param>
public sealed record PanelPlacement(string PanelId, string Key, Rect Frame, bool Floating,
                                    bool OnStage, bool Fits, Rect? Strip, double StripFullHeight);

/// <summary>Outcome kind for a window.</summary>
public enum TargetKind { Tile = 0, Park = 1, ParkWithPicture = 2 }

/// <summary>A window's target: a rounded tile frame, or a park frame.</summary>
public readonly record struct WindowTarget(TargetKind Kind, Rect Rect)
{
    /// <summary>Kind == ParkWithPicture.</summary>
    public bool NeedsPicture { get; }
}

/// <summary>Result of one pass. Immutable.</summary>
public sealed record ProjectionResult
{
    /// <summary>Placements in production order: tree panels in pre-order, then floating panels
    /// in document order (B10).</summary>
    public ImmutableArray<PanelPlacement> Placements { get; }
    /// <summary>Targets by window key (ordinal). A key with no target is absent (B10, B11, B17).</summary>
    public IReadOnlyDictionary<string, WindowTarget> Targets { get; }
}

/// <summary>Pure geometry helpers of the projection.</summary>
public static class ScreenProjection
{
    public const double OnStageThreshold = 0.995;          // B13
    public const double FloatingOnStageThreshold = 0.05;   // B13 (strict >)
    public const double FitsSlack = 0.5;                   // B15
    public const double GrowSlack = 0.5;                   // B14
    public const double StripHeightFraction = 0.15;        // B12
    public const double StripInsetThreshold = 120;         // B12
    public const double DegeneratePreviewExtent = 1;       // B21
    /// <summary>(800, 600): park/preview size when nothing better is known (B20, B21).</summary>
    public static Size FallbackWindowSize { get; }

    /// <summary>(A.x + g/2, A.y + g/2, max(0, A.w − g), max(0, A.h − g)); g clamped by B1.</summary>
    public static Rect Stage(Rect workArea, double gap);
    /// <summary>At-rest camera (B5).</summary>
    public static Rect CameraForFraming(Rect? framed);
    /// <summary>Un-inset projected rect of a unit rect (B6).</summary>
    public static Rect Project(ProjectionView view, Rect unit);
    /// <summary>Project, then inset by Gap/2 on each side (B7).</summary>
    public static Rect DockedFrame(ProjectionView view, Rect unit);
    /// <summary>Rect inset by gap/2 on each side, extents clamped at 0 (B7).</summary>
    public static Rect InsetHalfGap(Rect rect, double gap);
    /// <summary>Aspect fit to a natural size (B9 step 2). Invalid natural or empty rect → rect.</summary>
    public static Rect FitAspect(Rect rect, Size natural);
    /// <summary>Grow-to-minimum (B14).</summary>
    public static Rect GrowToMinimum(Rect frame, Size minimum, Rect stage);
    /// <summary>area(frame ∩ stage) / area(frame); 0 for zero/negative/non-finite area (B13).</summary>
    public static double OnStageFraction(Rect frame, Rect stage);
    /// <summary>Minimum m of B15: per axis max(floor, own).</summary>
    public static Size EffectiveMinimum(bool fixedMinimum, Size? own, Tuning tuning);
    /// <summary>frame.w ≥ m.w − 0.5 and frame.h ≥ m.h − 0.5 (B15).</summary>
    public static bool Fits(Rect frame, Size minimum);
    /// <summary>Maximum clamp, centred on both axes (B19). Null maximum → frame.</summary>
    public static Rect ClampToMaximum(Rect frame, Size? maximum);
    /// <summary>Edge rounding (B18).</summary>
    public static Rect RoundEdges(Rect rect);
    /// <summary>Preview (scaled-up) park size (B21).</summary>
    public static Size PreviewSize(Rect frame, Size minimum, Size? lastFrameSize);
    /// <summary>2·tp + tuning.TabStripContentHeight, tp = TabPadding clamped to ≥ 0 (B12).</summary>
    public static double StripFullHeight(double tabPadding, Tuning tuning);
    /// <summary>Strip rect at rest (reveal 1) for a frame (B12).</summary>
    public static Rect StripRect(Rect frame, double fullHeight, double tabInset);
}

/// <summary>The projection pass.</summary>
public static class ProjectionTargets
{
    /// <summary>Computes placements and targets (B10–B22).</summary>
    /// <exception cref="ArgumentNullException">request is null.</exception>
    public static ProjectionResult Compute(ProjectionRequest request);
}
```

`Tuning` is `CanvasWm.Core.Tuning`. `ProjectionResult` may have an internal
constructor; tests only read it.

## Behaviour

Notation: for a rect `R`, `R.MaxX = R.X + R.Width`, `R.MaxY = R.Y + R.Height`.
`g` = `ProjectionView.Gap` (already clamped). `S` = the view's stretch,
`C` = the view's camera, `st` = the view's stage. `m` = the minimum of B15
for the placement's key.

### A. View, stage, stretch, camera

- **B1 (gap clamp).** `ProjectionView` stores `gap` as given when it is finite
  and `≥ 0`, else `0`. `Stage(workArea, gap)` applies the same clamp. The work
  area is not validated.
- **B2 (stage).** `Stage = (A.X + g/2, A.Y + g/2, max(0, A.Width − g),
  max(0, A.Height − g))`. It is always the whole work area of the display,
  never the framed node's rect, and does not depend on the camera.
- **B3 (piecewise-linear map).** `Map(bp, v)` with `n = bp.Length`:
  - `n == 0` → `v`;
  - `v ≤ bp[0].From` → `v + (bp[0].To − bp[0].From)`;
  - `v ≥ bp[n−1].From` → `v + (bp[n−1].To − bp[n−1].From)`;
  - otherwise, for the `i` with `bp[i].From ≤ v < bp[i+1].From`:
    `bp[i].To + (v − bp[i].From) · (bp[i+1].To − bp[i].To) / (bp[i+1].From − bp[i].From)`.
  (`n == 1` is therefore a constant shift.) The `Stretch` constructor rejects
  non-finite values and From values that are not strictly increasing (A2).
- **B4 (stretch from framing).** `ForFraming(F)` returns `Identity` when `F`
  is null, has a non-finite component, `F.Width ≤ 0`, `F.Height ≤ 0`, or
  `|F.Width − F.Height| ≤ 1e-9`. Otherwise the shorter side is stretched to
  the longer side's length on that axis only:
  - `F.Width < F.Height` → `X = [(F.X, F.X), (F.MaxX, F.X + F.Height)]`, `Y` empty;
  - `F.Width > F.Height` → `Y = [(F.Y, F.Y), (F.MaxY, F.Y + F.Width)]`, `X` empty.
  Values at or before the framed rect are unchanged; values at or after it
  move by `longer − shorter`.
- **B5 (camera at rest).** `CameraForFraming(F)`: null, non-finite,
  `Width ≤ 0` or `Height ≤ 0` → `(0, 0, 1, 1)`; otherwise
  `(F.X, F.Y, s, s)` with `s = max(F.Width, F.Height)` (F mapped through
  `ForFraming(F)`; a square F gives F itself). `AtRest(A, gap, F)` =
  `new ProjectionView(A, gap, CameraForFraming(F), Stretch.ForFraming(F))`.

### B. Unit rect → screen frame

- **B6 (projection).** For unit rect `R`:
  `x = st.X + st.Width · (S.MapX(R.X) − C.X) / C.Width`,
  `y = st.Y + st.Height · (S.MapY(R.Y) − C.Y) / C.Height`,
  `w = st.Width · (S.MapX(R.MaxX) − S.MapX(R.X)) / C.Width`,
  `h = st.Height · (S.MapY(R.MaxY) − S.MapY(R.Y)) / C.Height`.
  Axes are scaled independently, so the camera fills the stage exactly.
- **B7 (docked frame).** `InsetHalfGap(r, g) = (r.X + g/2, r.Y + g/2,
  max(0, r.Width − g), max(0, r.Height − g))` (gap clamped as B1);
  `DockedFrame = InsetHalfGap(Project(view, R), view.Gap)`.
- **B8 (unit rect source).** A tree panel's unit rect is
  `entries[panel.Id].Rect`, where `entries` is `request.Entries` or, when
  null, `LayoutSolver.LayoutRects(document.Root)`. No per-window rect
  override exists at rest.
- **B9 (floating frame).** For a floating panel with unit rect
  `U = FloatingUnitRects[panel.Id]`:
  1. `f = Project(view, U)` (no inset);
  2. `FitAspect`: if `facts.Natural` is present with finite `Width, Height > 0`,
     and `f.Width > 0`, `f.Height > 0`: `a = nat.Width / nat.Height`; if
     `a < f.Width / f.Height` → width becomes `a · f.Height` and
     `x += (f.Width − newWidth) / 2`; otherwise height becomes `f.Width / a`
     (x, y, width unchanged);
  3. unless the panel id is in `CollapsedPanels`:
     `f = GrowToMinimum(f, m, st)`.

### C. Placements

- **B10 (what gets a placement).** Walk `document.Root` in pre-order
  (children in order; a `DesktopNode` is not itself placed, its child is
  walked). Each `PanelNode` met yields one placement, then each
  `document.Floating` entry in list order. **No placement** for: desktop
  nodes; hidden panels and the hidden-desktop record (never walked); a panel
  whose `Selected` is null or not in its `Views` (A6); a tree panel with no
  entry in `entries`; a floating panel with no `FloatingUnitRects` entry.
  Windows in no placed panel get no target and are absent from `Targets`.
- **B11 (selected only).** A placement's `Key` is the panel's `Selected`.
  The other views of the panel (tabbed or native group) get no target.
- **B12 (tab strip).** For a panel with `HasStrip` (tabbed and > 1 view):
  - `StripFullHeight = H = 2·tp + tuning.TabStripContentHeight`, with
    `tp = TabPadding` when finite and `≥ 0`, else 0 (defaults → 28);
  - strip height `s = min(0.15 · frame.Height, H)`;
  - side inset `i = clamp((frame.Width − 120)/2, 0, ti)` when
    `ti = TabInset` is finite and `> 0`, else `i = 0`;
  - `Strip = (frame.X + i, frame.Y, frame.Width − 2i, s)` (reveal fraction 1).
  `frame` is the placement frame (B7/B9, un-rounded). The window's frame is
  **not** reduced by the strip. Panels without a strip: `Strip = null`,
  `StripFullHeight = 0`.
- **B13 (on stage).** `OnStageFraction(f, st)` = `0` unless `f.Width` and
  `f.Height` are both finite and `> 0`; otherwise
  `ParkGeometry.IntersectionArea(f, st) / (f.Width · f.Height)`. `OnStage`:
  - panel id in `CollapsedPanels` → false;
  - else `fraction ≥ 0.995` → true;
  - else floating and `DesktopSlotUsable` and `fraction > 0.05` → true;
  - else false.
  Uses the un-rounded, un-clamped frame.
- **B14 (grow to minimum, floats only).** If (`f.Width < m.Width − 0.5` or
  `f.Height < m.Height − 0.5`) and `f.Width > 0` and `f.Height > 0`:
  `k = max(m.Width / f.Width, m.Height / f.Height)`, then
  `k = min(k, st.Width / f.Width, st.Height / f.Height)`. If `k > 1`: new
  size `(f.Width·k, f.Height·k)`, `x = f.X + f.Width/2 − newWidth/2`,
  `y = f.Y`; then `x = max(st.X, min(x, st.MaxX − newWidth))`,
  `y = max(st.Y, min(y, st.MaxY − newHeight))`. Otherwise the frame is
  unchanged.
- **B15 (minimum and fits).** `EffectiveMinimum(fixed, own, tuning)` per axis
  = `max(floor, ownAxis)` where floor = `tuning.FixedTileMinimum` when
  `fixed`, else `tuning.TileMinimumFloor`, and `ownAxis` = the own minimum's
  component when present, finite and `> 0`, else 0. `Fits(f, m)` =
  `f.Width ≥ m.Width − 0.5 && f.Height ≥ m.Height − 0.5`. No gap, no strip
  height, no squeeze policy enters. Own minimum = `facts.Minimum` of the key
  (no facts → none). Floats use the same rule (A10).
- **B16 (record).** `PanelPlacement(panel.Id, Selected, frame, floating,
  OnStage, Fits, Strip, StripFullHeight)`.

### D. Targets

- **B17 (who gets a target).** Placements are processed in order. A
  placement gets a target only when `request.Windows` contains its key and no
  earlier placement already produced a target for that key (first wins, A7).
  `facts = request.Windows[key]`.
- **B18 (edge rounding).** `RoundEdges(r)`: `x' = Round(r.X)`,
  `y' = Round(r.Y)`, `w' = max(0, Round(r.MaxX) − x')`,
  `h' = max(0, Round(r.MaxY) − y')`. Whole DIPs, halves away from zero.
- **B19 (Tile).** `OnStage && Fits` → `Kind = Tile`,
  `Rect = RoundEdges(ClampToMaximum(frame, facts.Maximum))`.
  `ClampToMaximum(f, M)`: per axis, when `M`'s component is finite and `> 0`
  and smaller than the frame extent, the extent becomes `M`'s component and
  the origin moves by half the difference (centred on both axes); otherwise
  the axis is unchanged. This is the only branch that rounds.
- **B20 (Park).** `!OnStage` → `Kind = Park`. Size
  `z = facts.LastFrameSize ?? FallbackWindowSize (800 × 600)`. Rect =
  `ParkGeometry.ParkSmallFrame(parkDisplay, z, parkOptions)` when
  `Policy.ParkOutOfViewSmall`, else
  `ParkGeometry.ParkFrame(parkDisplay, z, minimum: null, parkOptions)`.
  **No minimum** is passed in this branch.
- **B21 (ParkWithPicture).** `OnStage && !Fits` → `Kind = ParkWithPicture`,
  Rect = `ParkGeometry.ParkFrame(parkDisplay, PreviewSize(frame, m,
  facts.LastFrameSize), minimum: m, parkOptions)` — `m` is the same
  effective minimum used by the fits test (B15); never park-small.
  `PreviewSize(f, m, last)`: if `f.Width ≤ 1` or `f.Height ≤ 1` (or either
  is not finite) → `last ?? (800, 600)`; else `k = max(1, m.Width / f.Width,
  m.Height / f.Height)` and the result is `(Round(f.Width·k),
  Round(f.Height·k))`. Park geometry (park-geometry B5) first fits the
  preview within the work area, then raises each side to `Ceiling(m)`; so the
  park size never drops below the minimum, but its aspect ratio may change
  when the preview is larger than the work area (A4).
- **B22 (floats).** Floating placements go through B19–B21 like tiles; an
  on-stage float keeps its fitted/grown frame, clamped and rounded.

### E. Purity

- **B23.** `Compute` and all helpers are pure: no I/O, no clock, no
  randomness, no static mutable state; inputs are not modified; equal inputs
  give equal results.

## Edge cases & errors

- **E1.** `ProjectionView` with a camera of zero/negative/non-finite width or
  height (or any non-finite component) → `ArgumentOutOfRangeException`.
  Non-finite or negative gap → 0.
- **E2.** `Stretch` with From values not strictly increasing, or any NaN/±∞
  → `ArgumentException`.
- **E3.** Zero-extent stage (work area not larger than the gap): every frame
  has zero width or height → fraction 0 → nothing is on stage → all targeted
  windows park.
- **E4.** Zero-area frames (e.g. a projected tile thinner than the gap) are
  never on stage (fraction 0), so `ParkWithPicture`'s degenerate-preview
  fallback is reachable only through a direct `PreviewSize` call.
- **E5.** `Document.Root == null` or an empty desktop root → no tree
  placements; floats are still placed if they have unit rects.
- **E6.** Duplicate window keys across panels: every panel still gets its
  placement; only the first placement per key yields a target.
- **E7.** Facts with NaN/negative `Minimum` components count as 0 (B15);
  NaN/≤ 0 `Maximum` components are ignored (B19); invalid `Natural` skips the
  aspect fit (B9).
- **E8.** Null arguments, null `init` values for `Policy`, `Tuning`,
  `CollapsedPanels`, `FloatingUnitRects`, or a null value inside `Windows`
  → `ArgumentNullException`.
- **E9.** A `ParkWithPicture` park frame may be larger than the work area
  (minimum raised after the fit). It is still a valid park frame
  (`ParkGeometry.IsParkFrame` recognises it through its second test).

## Acceptance criteria

All criteria are [mac] xUnit tests in `tests/CanvasWm.Core.Tests` with no OS
access. Common setup unless stated: work area `A = (0, 25, 1440, 875)`, gap
10 → stage `(5, 30, 1430, 865)`; `ParkDisplay(visibleArea = A, displayFrame =
(0, 0, 1440, 900), others = [], fallback (0, 0))`; default policy, tuning and
park options; every window key in the tree has `WindowFacts` with
`LastFrameSize = (705, 855)` and nothing else. Trees: letters =
`PanelNode.ForWindow(letter)`, `X{s}[a(.5), b(.5)]` = split id `s`. "P(z)"
means `ParkGeometry.ParkFrame(display, z, null)`, which in the common setup is
`(1439, 899, z.Width, z.Height)` when `z` fits in 1440 × 875. "P(z, m)" means
`ParkGeometry.ParkFrame(display, z, m)`. Un-rounded values: tolerance `1e-9`;
rounded targets and park frames: exact.

### Stretch, view, helpers
- **AC1** [mac] `Stage((0,25,1440,875), 10) == (5,30,1430,865)`;
  `Stage(A, 0) == A`; gap `NaN`, `−3`, `+∞` → stored `Gap == 0` and stage
  `== A`; `Stage((0,0,6,6), 10) == (5,5,0,0)`. `new ProjectionView(A, 10,
  camera, Stretch.Identity)` throws `ArgumentOutOfRangeException` for camera
  `(0,0,0,1)`, `(0,0,1,−1)`, `(0,0,NaN,1)`. (B1, B2, E1)
- **AC2** [mac] `Stretch.ForFraming`: null, `(0,0,0,1)`, `(0,0,.5,.5)` and
  `(0,0,.5,.5+1e-10)` → `Identity`; `(0,0,.5,1)` → `X == [(0,0),(.5,1)]`,
  `Y` empty; `(.5,0,.5,1)` → `X == [(.5,.5),(1,1.5)]`; `(0,0,1,.5)` →
  `Y == [(0,0),(.5,1)]`, `X` empty. With `X = [(.5,.5),(1,1.5)]`:
  `MapX(0) == 0`, `MapX(.5) == .5`, `MapX(.75) == 1`, `MapX(1) == 1.5`,
  `MapX(1.2) == 1.7`; `MapY(v) == v`. Single breakpoint `[(0, .25)]` → every
  value shifted by `.25`. `new Stretch([(1,0),(1,2)], [])`,
  `[(1,0),(0,2)]` and `[(NaN,0)]` throw `ArgumentException`; two `Stretch`
  instances built from equal lists are `Equals`. (B3, B4, E2)
- **AC3** [mac] `CameraForFraming`: null → `(0,0,1,1)`; `(0,0,.5,1)` →
  `(0,0,1,1)`; `(.5,0,.5,1)` → `(.5,0,1,1)`; `(.5,0,.5,.5)` → `(.5,0,.5,.5)`;
  `(0,0,0,1)` → `(0,0,1,1)`. For each valid non-null F above,
  `Project(AtRest(A, 10, F), F) == stage` (tolerance 1e-9). (B5, B6)

### Projection (worked examples 1–7)
- **AC4** [mac] Ex. 1: `X{s}[a(.5), b(.5)]`, nothing framed
  (`AtRest(A, 10, null)`): placements `a` frame `(10,35,705,855)`, `b`
  `(725,35,705,855)`, both `OnStage`, `Fits`; targets `a` Tile
  `(10,35,705,855)`, `b` Tile `(725,35,705,855)`. Placements order `[a, b]`.
- **AC5** [mac] Ex. 2: same tree, framed `a = (0,0,.5,1)`: `a` frame
  `(10,35,1420,855)`, Tile `(10,35,1420,855)`; `b` frame `(1440,35,705,855)`,
  `OnStage == false`, target Park `P(705,855) = (1439,899,705,855)`.
- **AC6** [mac] Ex. 3: `X{s1}[a(.5), Y{s2}[b(.5), c(.5)](.5)]`, framed
  `s2 = (.5,0,.5,1)`: `b` frame `(10,35,1420,422.5)` → Tile
  `(10,35,1420,423)`; `c` frame `(10,467.5,1420,422.5)` → Tile
  `(10,468,1420,422)` (so `c.Y − (b.Y + b.Height) == 10`); `a` frame
  `(−705,35,705,855)` → Park.
- **AC7** [mac] Ex. 4: same tree, framed `b = (.5,0,.5,.5)`: the view's
  stretch equals `Identity`, camera `(.5,0,.5,.5)`; `b` Tile
  `(10,35,1420,855)`; `c` frame `(10,900,1420,855)` → Park; `a` frame
  `(−1420,35,1420,1720)` → Park.
- **AC8** [mac] Ex. 5: `Y{s}[t(.5), u(.5)]`, framed `t = (0,0,1,.5)`: `t`
  Tile `(10,35,1420,855)`; `u` frame `(10,900,1420,422.5)` → Park.
  (Corrected at test stage: `u` keeps its unit height 0.5 → 865·0.5 − 10.)
- **AC9** [mac] Ex. 6: `A = (0,25,1441,875)`, tree of AC4: `a` frame
  `(10,35,705.5,855)` → Tile `(10,35,706,855)`; `b` frame
  `(725.5,35,705.5,855)` → Tile `(726,35,705,855)`.
- **AC10** [mac] Ex. 7: `new ProjectionView(A, 10, (.1,0,1,1),
  Stretch.Identity)`, tree of AC4: `a` frame `(−133,35,705,855)`,
  `OnStageFraction == 567/705` (≈ 0.80426), Park — also with
  `DesktopSlotUsable = true` (docked panels never use the 5 % rule); `b`
  frame `(582,35,705,855)`, Tile `(582,35,705,855)`.

### On stage
- **AC11** [mac] Ex. 8: `OnStageFraction((733,35,705,855), stage) ==
  702/705` (≥ 0.995); `(734,35,705,855)` → `701/705` (< 0.995). Zero width,
  negative height, NaN width → 0; a frame touching the stage only at an edge
  → 0. (B13, E4)
- **AC12** [mac] Ex. 9: document with no root and one floating panel
  `FloatingPanel(ForWindow("f"), …)` (facts: `LastFrameSize = (705,855)`);
  `FloatingUnitRects = {f: (.9375,0,.5,.5)}`; identity view: frame
  `(1345.625,30,715,432.5)`, `Floating == true`, fraction `0.125`. With
  `DesktopSlotUsable` → `OnStage`, target Tile `(1346,30,715,433)`. Without
  → Park `P(705,855)`. With unit rect `(.984375,0,.5,.5)` (fraction
  `0.03125`) and the slot usable → not on stage, Park. (B9, B13, B22)

### Fits and pictures
- **AC13** [mac] Ex. 10: `m = EffectiveMinimum(false, null, Tuning.Default)
  == (200,120)`; `Fits((0,0,190,400), m) == false`;
  `PreviewSize((0,0,190,400), m, null) == (200, 421)`;
  `Fits((0,0,199.6,400), m) == true`; `Fits((0,0,199.5,119.5), m) == true`;
  `Fits((0,0,199.4,400), m) == false`. (B15, B21)
- **AC14** [mac] Ex. 11: `EffectiveMinimum(true, null, Tuning.Default) ==
  (480,320)`; `Fits((725,468,348,206), (480,320)) == false`;
  `PreviewSize((725,468,348,206), (480,320), null) == (541, 320)`.
  `EffectiveMinimum(true, null, Tuning.Default with { FixedTileMinimum =
  new Size(500,100) }) == (500,100)`.
- **AC15** [mac] Ex. 12: `EffectiveMinimum(false, (400,300), …) ==
  (400,300)`; `EffectiveMinimum(false, (150,500), …) == (200,500)`;
  `EffectiveMinimum(false, (NaN,−5), …) == (200,120)`;
  `PreviewSize((0,0,348,206), (400,300), null) == (507, 300)`.
  `PreviewSize((0,0,1,400), m, (640,480)) == (640,480)`;
  `PreviewSize((0,0,1,400), m, null) == (800,600)`;
  `PreviewSize((0,0,500,300), (200,120), null) == (500,300)` (k = 1).
- **AC16** [mac] End-to-end pictures (B21, E9):
  - (a) `X{s}[a(.5), Y{t}[b(.5), c(.5)](.5)]`, nothing framed, `b` facts
    `Minimum = (0, 500)`: `b` frame `(725,35,705,422.5)`, `OnStage`,
    `Fits == false`, `m = (200,500)`, preview `(834,500)`; target
    `ParkWithPicture` with `NeedsPicture == true` and rect
    `P((834,500), (200,500)) = (1439,899,834,500)` (fit leaves 834 × 500
    unchanged; the minimum raises nothing). With `ParkOutOfViewSmall = true`
    the rect is unchanged (park-small is never used for pictures). `a` and
    `c` are Tile with `NeedsPicture == false`.
  - (b) AC4 tree, `a` facts `Minimum = (800,600)`: `a` frame
    `(10,35,705,855)`, `OnStage`, `Fits == false` (705 < 799.5),
    `m = (800,600)`; preview `k = 800/705`, size `(800, 970)`
    (855·800/705 = 970.21 → 970); `ParkSize` fits it into 1440 × 875 →
    `(722, 875)` (s = 875/970; 800·s = 721.65 → 722), then raises each side
    to `Ceiling(m)` → `(800, 875)`; target `ParkWithPicture`
    `(1439,899,800,875)`. (Without the minimum it would be 722 × 875.)
  - (c) AC4 tree, `a` facts `Minimum = (1600,600)`: `m = (1600,600)`;
    preview `k = 1600/705`, size `(1600, 1940)` (1940.43 → 1940); fit →
    `(722, 875)` (s = 875/1940; 1600·s = 721.65 → 722); raised →
    `(1600, 875)`; target `ParkWithPicture` `(1439,899,1600,875)` — wider
    than the work area (E9); `ParkGeometry.IsParkFrame(display, rect)` is
    true.
- **AC17** [mac] Ex. 13: `ClampToMaximum((10,35,705,855), (600,700)) ==
  (62.5,112.5,600,700)`; `RoundEdges` of it `== (63,113,600,700)`;
  end-to-end, AC4 with `a` facts `Maximum = (600,700)` → `a` Tile
  `(63,113,600,700)`. `ClampToMaximum` with `(800, 0)` or `(NaN, 900)` →
  frame unchanged; with `(800, 700)` → `(10,112.5,705,700)`; with null →
  unchanged. (B19)

### Tab strip
- **AC18** [mac] Ex. 14: tree `X{s}[P(.5), b(.5)]` with
  `P = new PanelNode("P", ["p","q"], "p", tabbed: true)`, policy
  `TabPadding = 4, TabInset = 8`, facts for `p`, `q`, `b`: placement `P` has
  `PanelId == "P"`, `Key == "p"`, frame `(10,35,705,855)`,
  `StripFullHeight == 28`, `Strip == (18,35,689,28)`; target `p` Tile
  `(10,35,705,855)`; `q` has no target. With default policy
  (`TabInset = 0`): `Strip == (10,35,705,28)`. The same panel with
  `tabbed: false` (native group) → `Strip == null`, `StripFullHeight == 0`,
  `q` still has no target. `b`'s placement has `Strip == null`. (B11, B12)
- **AC19** [mac] Ex. 15: `StripRect((10,35,705,100), 28, 8) ==
  (18,35,689,15)`; `StripRect((0,0,130,855), 28, 8) == (5,0,120,28)`;
  `StripRect((0,0,100,855), 28, 8) == (0,0,100,28)`; tabInset `−3` or NaN →
  inset 0. `StripFullHeight(4, Tuning.Default) == 28`,
  `StripFullHeight(−1, Tuning.Default) == 20`, `StripFullHeight(4,
  Tuning.Default with { TabStripContentHeight = 30 }) == 38`. (B12)

### Nothing to place and other no-target cases
- **AC20** [mac] Ex. 16: `new LayoutDocument(new DesktopNode())` with facts
  for `k` → `Placements` empty, `Targets` empty. Also: (a) tree
  `Desktop{X{s}[a, b]}` → placements `a`, `b` only (no desktop placement),
  frames as AC4; (b) a hidden panel `h` with facts → no placement, no
  target; (c) AC4 with no facts for `b` → `b` placement present, `Targets`
  has only `a`; (d) a panel with `Selected == null`, and one with `Selected`
  not in `Views` → no placement; (e) a floating panel without a
  `FloatingUnitRects` entry → no placement; (f) AC4 with
  `CollapsedPanels = {"a"}` → `a.OnStage == false`, target Park
  `P(705,855)`; `b` Tile. (B10, B11, B13, B17, E5)
- **AC21** [mac] Rounding: `RoundEdges((0.5,−0.5,1,1)) == (1,−1,1,2)`;
  `RoundEdges((2.5,0,0.2,0)) == (3,0,0,0)`;
  `RoundEdges((−1.5,0,1,1)) == (−2,0,1,1)`;
  `RoundEdges((10,35,705.5,855)) == (10,35,706,855)`. (B18)

### Floats, parking, misc
- **AC22** [mac] `FitAspect((100,50,400,300), (800,400)) ==
  (100,50,400,200)`; with natural `(300,600)` → `(225,50,150,300)`; natural
  `(0,600)`, or a rect of width 0 → unchanged. `GrowToMinimum` with stage
  `(5,30,1430,865)` and `m = (200,120)`: `(100,50,100,60)` →
  `(50,50,200,120)`; `(1400,800,100,60)` → `(1235,775,200,120)`;
  `(199.6,50,199.6,120)` → unchanged; with `m = (3000,120)`:
  `(100,50,100,60)` → `(5,37,1430,858)`. End-to-end: a floating `f` with
  unit rect `(.1,.1,.05,.05)` (projected `(148,116.5,71.5,43.25)`) → frame
  `(83.75, 116.5, 200, 43.25·200/71.5)`, `Fits == true`,
  `OnStage == true`, Tile; with `CollapsedPanels = {"f"}` → frame
  `(148,116.5,71.5,43.25)` (not grown), `OnStage == false`, Park; with
  `CollapsedPanels = {"f"}` and `f` facts `Natural = (100,100)` → frame
  `(162.125,116.5,43.25,43.25)`. (B9, B14)
- **AC23** [mac] Parking: AC5 with `ParkOutOfViewSmall = true` → `b` Park
  `ParkGeometry.ParkSmallFrame(display, (705,855)) == (1439,899,240,292)`;
  AC5 with `b` facts `LastFrameSize == null` → Park `P(800,600)`. Off-stage
  parking ignores the minimum: AC5 with `b` facts `Minimum = (1600,600)` and
  `LastFrameSize = (705,855)` → Park `(1439,899,705,855)` (not 1600 wide).
  A work area `(0,25,1440,10)` (zero-height stage) → every targeted window is
  Park (no Tile, no ParkWithPicture). (B20, E3, E4)
- **AC24** [mac] Duplicate key: tree `X{s}[P1(.5), P2(.5)]` where
  `P1 = new PanelNode("p1", ["k"], "k", false)`,
  `P2 = new PanelNode("p2", ["k"], "k", false)` → two placements (`p1`,
  `p2`), one target `k` = Tile `(10,35,705,855)` (from `p1`). (B17, E6)
- **AC25** [mac] Entries input: for the AC6 tree and view,
  `Entries = LayoutSolver.FocusLayout(root)` (with range entries) gives a
  result equal to `Entries = null` (same placements in the same order, same
  targets). Supplying `Entries = LayoutSolver.LayoutRects(a)` (a lone panel
  `a`, rect `(0,0,1,1)`) for the AC4 document → placement `a` frame
  `(10,35,1420,855)`; `b` has no entry → no placement. (B8, B10)
- **AC26** [mac] Errors: `Compute(null!)`; `new ProjectionRequest(…)` with
  each argument null; a `Windows` dictionary with a null value;
  `Policy = null!`, `Tuning = null!`, `CollapsedPanels = null!`,
  `FloatingUnitRects = null!`; `new Stretch(null!, [])`;
  `new ProjectionView(A, 10, (0,0,1,1), null!)` → all
  `ArgumentNullException`. After `Compute`, the request's document equals a
  copy taken before the call. Mutating the source dictionaries/sets after
  constructing the request does not change the result. (E8, B23)

### Property test
- **AC27** [mac] Seeded property test: `new Random(seed)` for seeds 1–100,
  20 cases each. Generate a normalised tree (depth ≤ 4, fan-out 2–4, random
  axis and positive weights, unique panel ids, unique window keys across all
  views, ~20 % of panels tabbed with 2–3 views, optional desktop root
  wrapper), a work area with integer X, Y in `[−2000, 2000]` and W, H in
  `[300, 3000]`, an integer gap in `[0, 20]`, a framed rect that is null
  (30 %) or a random entry's rect from `LayoutSolver.FocusLayout`, facts for
  every view (minimum present 50 % with components in `[0, 1500]`, maximum
  present 20 % in `[100, 3000]`, last size in `[100, 2000]`), random
  `FixedMinimum`; `ParkDisplay` with the work area and a display frame of the
  same rect. Check:
  - **P1** placements = exactly the tree's panels in pre-order; each key is
    the panel's selected view; no non-selected view key is in `Targets`;
    `Targets.Count` = number of placements.
  - **P2** Kind ⇔ flags: Tile iff `OnStage && Fits`; ParkWithPicture iff
    `OnStage && !Fits`; Park iff `!OnStage`.
  - **P3** Tile rects have integral components and `Width, Height ≥ 0`; when
    the window has no maximum, each rounded edge is within 0.5 of the
    frame's edge.
  - **P4** every Park / ParkWithPicture rect `r` satisfies
    `ParkGeometry.IsParkFrame(display, r)`.
  - **P5** framed null: every placement whose frame has positive area has
    `OnStageFraction ≥ 1 − 1e-9`.
  - **P6** framed F non-null: `Project(view, F)` equals the stage within
    1e-6 per component.
  - **P7** for consecutive children `c_i`, `c_{i+1}` of a split that are both
    panels, whose projected extents along the split axis are `≥ g`: the
    distance between their docked frames along that axis is `g` within 1e-6.
  - **P8** two calls give equal results; inputs unchanged.
  - **P9** every ParkWithPicture rect has `Width ≥ Ceiling(m.Width)` and
    `Height ≥ Ceiling(m.Height)` for that window's effective minimum `m`.
  (B2–B7, B10–B21, B23)

### Architecture
- **AC28** [mac] `dotnet build CanvasWm.slnx` exits 0 with 0 warnings;
  `CanvasWm.Core.csproj` has no new references; the existing architecture
  tests pass (Core references no CanvasWm assembly); no existing file under
  `src/` is changed (new files only, in `src/CanvasWm.Core/Projection/`); no
  file in `src/` or `tests/` contains the words `TrellisWM` or `Licensing`.

Count: **28 [mac]** (AC1–AC28), **0 [windows]**. Windows-side effects
(DIP → pixel re-rounding, DWM borders, actual parking) are verified with
#26 and #14.

## Assumptions

- **A1. New namespace and type names.** `CanvasWm.Core.Projection` with
  `PanelPlacement` (not `Placement`, which already exists as
  `CanvasWm.Core.Layout.Placement`). No existing type changes.
- **A2. General stretch semantics.** Outside the breakpoints the map is a
  constant shift by the nearest breakpoint's `To − From` (for the at-rest
  stretch the leading shift is 0, matching "unchanged before"). Breakpoint
  From values must be strictly increasing (throw otherwise). #15 must supply
  stretches in this shape. *Review.*
- **A3. Degenerate framing.** A framed rect with zero/negative/non-finite
  extent behaves as "nothing framed" (identity stretch, unit camera), instead
  of a degenerate camera. CanvasWM choice.
- **A4. Picture park frame keeps the minimum (human decision).** In the
  on-stage-but-too-small case the window's effective minimum `m` (the same
  one the fits test uses: max(floor or fixed, learned)) is passed as the
  `minimum` argument of `ParkGeometry.ParkFrame`, so the scaled-up picture
  size is never shrunk back below it. This matches the studied app, which
  passes the window's minimum in this branch. Consequence: when the preview
  exceeds the work area, park geometry fits it and then raises each side to
  `Ceiling(m)`, so the park frame can change aspect ratio and can be larger
  than the work area (AC16 b, c). The off-stage Park case still passes no
  minimum (earlier human decision, B20).
- **A5. Unknown last frame size → 800 × 600** for plain parking (same
  fallback as the preview). Normally the Engine always knows it.
- **A6. Panels with null or foreign `Selected`** get no placement (Sanitize
  normally prevents this; behaviour was not traced).
- **A7. Duplicate window keys: first placement wins** the target (tree
  before floats). Sanitize normally prevents duplicates.
- **A8. Collapsed panels park without a picture flag here** (Kind `Park`,
  because they are not on stage, rule 19). Findings rule 21 says collapsed
  windows are "parked with pictures"; #28 decides how collapsed panels are
  drawn and may add that (additive change). *Needs human review.*
- **A9. Floating unit rects are an input.** Floats are placed only when the
  caller supplies their unit rect (`FloatingUnitRects`); until the floating
  feature exists that map is empty, so floating windows get no target and
  are left alone. *Review.*
- **A10. Floating minimum.** Floats use the tile minimum rule (B15); the
  studied app's separate 0.98 × natural-size branch is deferred to the
  floating feature.
- **A11. Grow-to-minimum keeps floats inside the stage**; the findings call
  it the "floating area" (Inferred). The stage is used here.
- **A12. Maximum clamp ignores non-positive/non-finite components** per axis
  (B19). CanvasWM choice for invalid input.
- **A13. Tab strip over the window's top band** is reported as data only. On
  Windows, drawing it there would cover caption buttons; a Windows renderer
  may instead reserve space above the window (#20 decision, to be flagged as
  a deviation there). This spec does not reduce the window frame.
- **A14. Gap clamp in Core.** NaN/negative/∞ gap → 0 rather than throwing,
  because `EnginePolicy` values are unvalidated and "consumers clamp".
- **A15. Entries are weights-mode.** The Engine should pass its cached
  `FocusLayout` table (range entries are harmless); minimum-mode entries are
  not expected here.

## Traceability

| Spec item | Findings rule / example |
|---|---|
| ProjectionView, B1, B2, AC1, A14 | 1, 2 |
| Stretch, B3, B4, AC2, A2, A3 | 3, 4 |
| B5, AtRest, AC3 | 5; human decision (camera-ready, at-rest helper) |
| B6, B7, AC4–AC10 | 6, 7; Ex. 1–7 |
| B8, AC25, A15 | 8 (no override at rest — human decision) |
| B9, B14, AC12, AC22, A9–A11 | 9, 14 |
| B10, B11, AC20, AC24, A6, A7 | 10, 11; Ex. 16 |
| B12, AC18, AC19, A13 | 12; Ex. 14, 15; human decision (strip at rest) |
| B13, AC10–AC12, AC20(f), A8 | 13, 21; Ex. 7–9; human decision (collapsed set) |
| B15, AC13–AC15 | 15; Ex. 10–12 |
| B16, PanelPlacement | 16 |
| B17, B19, AC17, A12 | 17; Ex. 13 |
| B18, AC9, AC21 | 18; Ex. 6 |
| B20, AC5, AC23, A5 | 19; human decision (no minimum off stage) |
| B21, E9, AC13–AC16, AC27 P9, A4 | 20 (studied app passes the minimum); human decisions (scaled-up size; minimum passed for pictures; skip-store path excluded) |
| B22, AC12 | 22 |
| B23, AC26, AC27 | Core purity (CLAUDE.md) |
| AC28 | CLAUDE.md architecture |
