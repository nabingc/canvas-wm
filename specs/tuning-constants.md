# Spec: tuning-constants

## Goal

One immutable Core record, `Tuning`, holds the internal "feel" numbers that
later features read. These are animation lengths, gesture thresholds, drop-zone
bands, size floors, capture rates and robustness timeouts. The studied app
compiles them in as fixed constants. CanvasWM makes them an **injectable
immutable record** with one `Default` instance, so tests and Windows-specific
tuning can override single values with `with` expressions (findings rule 1).

Consumers read the record and do not repeat the literal (findings rule 2).
This feature only defines the record, its defaults and its validation. It
adds no algorithm that uses the values.

Engine wiring is **intent only** and is not part of this feature: a later
feature (expected #13 engine-tiling-loop) will let `DisplayEngine` receive a
`Tuning` next to its `EnginePolicy`. That feature decides whether it comes
through a constructor overload or a property. `EnginePolicy` and
`DisplayEngine` are not changed here.

Out of scope:
- **Deferred constants (role Unknown):** `parkSettle` (60), `splitSettles`
  (1), `stripTuck` (0.14). They are not modelled. A feature that identifies
  one of them adds the property then.
- **Values that are already `EnginePolicy` fields:** `dropSettle` 0.15
  (see `EnginePolicy.DropHold`), `desktopEdgeBand` 0.2 (see
  `EnginePolicy.DesktopEdge`) and `dockedDim` 0.25 (see
  `EnginePolicy.DimOpacity`). Policy wins, and they are cross-referenced
  only (A2).
- **Park geometry constants** (sliver 1, reach 60, tolerance 1, visible
  threshold 0.05, small divisor 3). These stay in `ParkOptions` (#6).
- **Pure algorithm literals** that are not tuning constants (findings rule 6):
  camera fallback step 0.016 s, tween exponent 5, refusal factor 0.8 (corrected per findings/engine-frame-writer.md), zoom
  request 0.92/0.90 and 100/60, reveal suppression 1 s on close-quiet and
  window focus, on-stage 0.995, fits slack 0.5, park visible fraction 0.05,
  settle delay `CoverFade + 0.02`, start-when-captured deadline 1 s. These
  stay in their own feature specs.
- The formulas that use the values (camera curve, layout tween, drop bands,
  cover hysteresis, picture-rate selection). They belong to their consumer
  features. They are listed under "Consumer notes" for traceability only.

## Placement

- Project: `src/CanvasWm.Core` (`net10.0`, BCL only). No new project or
  package references, no I/O and no OS access.
- Namespace: **`CanvasWm.Core`** (the root namespace of the project). The
  type `CanvasWm.Core.Tuning` does not clash with any namespace, because no
  namespace `CanvasWm.Core.Tuning` exists or may be added (A1).
- File: `src/CanvasWm.Core/Tuning.cs`. It goes in the project root folder so
  that the namespace matches the folder.
- Tests:
  - `tests/CanvasWm.Core.Tests/Tuning/TuningDefaultsTests.cs`,
    `TuningValidationTests.cs` and `TuningShapeTests.cs`. Test namespaces
    must not be `CanvasWm.Core.Tuning` (for example, use
    `CanvasWm.Core.Tests.TuningTests`).
  - `tests/CanvasWm.Engine.Tests/TuningPolicySeparationTests.cs`. It is in
    Engine.Tests because it needs both `Tuning` and `EnginePolicy`.
- Reuses, unchanged: `CanvasWm.Core.Geometry.Size` (positional
  `readonly record struct Size(double Width, double Height)`).
- Related, unchanged: `CanvasWm.Engine.EnginePolicy` (specs/engine-inbound-api.md)
  and `CanvasWm.Core.Parking.ParkOptions` (specs/park-geometry.md).
- **No existing type in Core or Engine is changed.**
- Units: distances and sizes are in DIPs, which the studied app calls points
  (1 pt = 1 DIP, A8). Times are in seconds. A fraction is a unitless ratio.
  Rates are in pictures per second. A factor is a unitless multiplier.
- No text in `src/` or `tests/` may contain the words `TrellisWM` or
  `Licensing`. The studied app's identifiers (the mapping table below) appear
  only in this spec, never in code or XML docs.

## Public API

```csharp
namespace CanvasWm.Core;

using CanvasWm.Core.Geometry;

/// <summary>Internal tuning constants: animation lengths, gesture thresholds, drop bands,
/// size floors, capture rates and robustness timeouts. These are not user settings; those are in
/// the Engine's policy record. Immutable: change single values with a with-expression.
/// The defaults are the values observed in the studied app on macOS. Compiler-generated value
/// equality.</summary>
public sealed record Tuning
{
    /// <summary>The default tuning. Equal to new Tuning(). The same instance every time.</summary>
    public static Tuning Default { get; } = new();

    // ---------- Camera and layout animation ----------
    /// <summary>Length of one camera move, seconds. Default 0.5.</summary>
    public double CameraMoveSeconds { get; init; }            // 0.5
    /// <summary>Ease-out steepness of a camera move (exponential rate, per normalised time unit).
    /// Default 5.5.</summary>
    public double CameraEaseRate { get; init; }               // 5.5
    /// <summary>Largest time step one animation frame may advance the camera, seconds.
    /// Default 0.032.</summary>
    public double CameraMaxStepSeconds { get; init; }         // 0.032
    /// <summary>Length of a layout tween (tiles sliding to new frames), seconds. Default 0.46.</summary>
    public double LayoutTweenSeconds { get; init; }           // 0.46

    // ---------- Pointer and touchpad gestures ----------
    /// <summary>Pointer travel before a press becomes a drag, DIPs. Default 5.</summary>
    public double DragStartDistance { get; init; }            // 5
    /// <summary>Pinch magnitude (zoom-factor delta) before a pinch starts zooming. Default 0.04.</summary>
    public double PinchStartDelta { get; init; }              // 0.04
    /// <summary>Zoom-factor change per discrete zoom step. Default 0.15.</summary>
    public double ZoomStepDelta { get; init; }                // 0.15
    /// <summary>Two-finger spread before a raw touch pinch is recognised, in normalised touchpad
    /// units. Default 0.06. Tuned for macOS raw trackpad touches; Windows touchpad input uses
    /// different units and needs calibration.</summary>
    public double TouchPinchStartDistance { get; init; }      // 0.06  (macOS-specific)
    /// <summary>Two-finger travel before a raw touch pan is recognised, in normalised touchpad
    /// units. Default 0.03. macOS-specific (see TouchPinchStartDistance).</summary>
    public double TouchPanStartDistance { get; init; }        // 0.03  (macOS-specific)
    /// <summary>Multiplier from touch travel to camera pan (factor). Default 1.3.
    /// macOS-specific (see TouchPinchStartDistance).</summary>
    public double TouchPanFactor { get; init; }               // 1.3   (macOS-specific)

    // ---------- Drag, drop and groups ----------
    /// <summary>Length of the lift animation when a window is picked up, seconds. Default 0.28.</summary>
    public double PickupSeconds { get; init; }                // 0.28
    /// <summary>Fraction of a tile, measured from each edge, that counts as the
    /// "insert beside on this edge" drop zone. Default 0.28.</summary>
    public double TileEdgeBandFraction { get; init; }         // 0.28
    /// <summary>Cap on the desktop-edge drop band, DIPs. The band is the policy's desktop-edge
    /// fraction times the stage extent, capped at this value. Default 64.</summary>
    public double DesktopEdgeBandMax { get; init; }           // 64
    /// <summary>Minimum grab width of a split seam for resize hit-testing, DIPs. Default 8.</summary>
    public double SeamHitMinWidth { get; init; }              // 8
    /// <summary>Extra hit-test reach around a gap handle, DIPs. Default 5.</summary>
    public double GapHandleHitSlop { get; init; }             // 5
    /// <summary>Pause after a seam drag before it counts as at rest, seconds. Default 0.15.</summary>
    public double GapDragRestSeconds { get; init; }           // 0.15
    /// <summary>Distance from a display edge within which a dragged window is handed to the
    /// neighbouring display, DIPs. Default 16.</summary>
    public double DisplayHandOffDistance { get; init; }       // 16
    /// <summary>Hover time over a window before a dragged window joins it as a tab group,
    /// seconds. Default 0.2.</summary>
    public double GroupHoverSeconds { get; init; }            // 0.2
    /// <summary>Pointer movement allowed during that hover without restarting it, DIPs.
    /// Default 6.</summary>
    public double GroupHoverSlop { get; init; }               // 6

    // ---------- Sizes and floors ----------
    /// <summary>Tile minimum used when the policy's fixed-minimum switch is on, DIPs.
    /// Default 480 x 320.</summary>
    public Size FixedTileMinimum { get; init; }               // (480, 320)
    /// <summary>Tile minimum floor used when the fixed-minimum switch is off, DIPs.
    /// Default 200 x 120.</summary>
    public Size TileMinimumFloor { get; init; }               // (200, 120)
    /// <summary>Compact presentation size; a lifted window is scaled toward it while moved,
    /// DIPs. Default 380 x 260.</summary>
    public Size CompactSize { get; init; }                    // (380, 260)
    /// <summary>Height of the grab area in compact presentation, DIPs. Default 18.</summary>
    public double CompactGrabHeight { get; init; }            // 18
    /// <summary>Smallest extent a tile may be squeezed to when squeezing below minimums is
    /// allowed, DIPs. Default 120.</summary>
    public double SqueezeFloor { get; init; }                 // 120
    /// <summary>A group whose tile is below this extent collapses to an outline (when the policy
    /// collapses small groups), DIPs. Default 120.</summary>
    public double GroupCollapseBelow { get; init; }           // 120
    /// <summary>Stroke width of a collapsed group's outline, whole DIPs. Default 2.</summary>
    public int CollapsedOutlineWidth { get; init; }           // 2
    /// <summary>Largest fraction of the desktop a floating window may take. Default 2/3.</summary>
    public double FloatMaxFraction { get; init; }             // 2.0 / 3.0
    /// <summary>Inset kept between a floating window and the desktop edge, DIPs. Default 16.</summary>
    public double FloatEdgePadding { get; init; }             // 16

    // ---------- Tab strips ----------
    /// <summary>Content height of a tab strip, DIPs. Default 20.</summary>
    public double TabStripContentHeight { get; init; }        // 20
    /// <summary>How far a tab strip tucks under the window's top edge, DIPs. Default 10.
    /// Measured against a macOS title bar; may need retuning on Windows.</summary>
    public double TabStripUnderlap { get; init; }             // 10    (macOS-specific)
    /// <summary>Space reserved in a tab strip for the window's title-bar buttons, DIPs.
    /// Default 72. On macOS the buttons are on the leading (left) side; Windows caption buttons
    /// are on the right.</summary>
    public double TabStripButtonReserve { get; init; }        // 72    (macOS-specific)
    /// <summary>Period of the native-tab watcher, seconds. Default 0.25. macOS-specific: Windows
    /// has no native window tabs.</summary>
    public double NativeTabWatchSeconds { get; init; }        // 0.25  (macOS-specific)
    /// <summary>Delay before native tab groups are re-checked after a window is added, removed or
    /// focused, seconds. Default 0.3. macOS-specific.</summary>
    public double NativeTabRecheckSeconds { get; init; }      // 0.3   (macOS-specific)

    // ---------- Overview ----------
    /// <summary>Top margin of the overview layout, DIPs. Default 28.</summary>
    public double OverviewTopMargin { get; init; }            // 28
    /// <summary>Bottom margin of the overview layout, DIPs. Default 96.</summary>
    public double OverviewBottomMargin { get; init; }         // 96

    // ---------- Live picture capture rates (pictures per second; interval = 1 / rate) ----------
    /// <summary>Rate for a window whose picture is actively changing. Default 10.</summary>
    public double ChangingPicturesPerSecond { get; init; }    // 10
    /// <summary>Rate while the camera or stage is moving. Default 3.</summary>
    public double MovingPicturesPerSecond { get; init; }      // 3
    /// <summary>At-rest rate for a visible window on the stage. Default 0.2 (one per 5 s).</summary>
    public double LivePicturesPerSecond { get; init; }        // 0.2
    /// <summary>At-rest rate for a window shown only as a preview. Default 1/6 (one per 6 s).</summary>
    public double PreviewPicturesPerSecond { get; init; }     // 1.0 / 6.0
    /// <summary>At-rest rate for a window not currently seen. Default 1/30 (one per 30 s).</summary>
    public double UnseenPicturesPerSecond { get; init; }      // 1.0 / 30.0
    /// <summary>At-rest rate for a window far away. Default 1/60 (one per minute).</summary>
    public double AwayPicturesPerSecond { get; init; }        // 1.0 / 60.0

    // ---------- Covers, placement and robustness ----------
    /// <summary>A cover is shown when a window's aspect ratio differs from its target's by more than
    /// this fraction. Default 0.01.</summary>
    public double CoverAspectMismatch { get; init; }          // 0.01
    /// <summary>The cover is removed once the mismatch is within this fraction (hysteresis).
    /// Default 0.005.</summary>
    public double UncoverAspectMismatch { get; init; }        // 0.005
    /// <summary>Give-up time for reflow: covers are dropped once shapes have rested longer than
    /// this, and float settling re-checks after it, seconds. Default 1.2.</summary>
    public double ReflowTimeoutSeconds { get; init; }         // 1.2
    /// <summary>After a landing focuses a window, focus-driven reveal reactions are suppressed for
    /// this long, seconds. Default 0.6.</summary>
    public double LandingRevealQuietSeconds { get; init; }    // 0.6
    /// <summary>Longest wait for fresh pictures after real-window placement finished, seconds.
    /// Default 0.35.</summary>
    public double FreshPictureWaitSeconds { get; init; }      // 0.35
    /// <summary>Tolerance when comparing a written frame with the frame the app actually took,
    /// DIPs. Default 24.</summary>
    public double FrameRefusalSlack { get; init; }            // 24
    /// <summary>Delay before re-reading a possibly refused frame, seconds. Default 0.25.</summary>
    public double FrameRefusalRecheckSeconds { get; init; }   // 0.25
    /// <summary>Number of snap-backs within SnapBackWindowSeconds after which the engine accepts
    /// the app's own size. Default 3.</summary>
    public int SnapBackLimit { get; init; }                   // 3
    /// <summary>Time window over which snap-backs are counted, seconds. Default 3.</summary>
    public double SnapBackWindowSeconds { get; init; }        // 3
}
```

Notes on the shape:
- The trailing comments give the default of each property. The implementation
  sets it as the backing-field initialiser. Each `init` accessor validates
  its value (B4) and stores it, following the same pattern as `ParkOptions`.
- Every property has an XML doc. The doc gives the meaning, the unit (when
  the unit is not in the name), the default and, for the seven
  macOS-specific members, a platform note.
- **50 instance properties** in total: 45 `double`, 2 `int`, 3 `Size`. The
  only other public member is `static Default`. There are no methods beyond
  the compiler-generated record members, and no public fields.

### Mapping to the studied constants (spec only; never in code)

| Group | Studied name | `Tuning` property | Type | Default | Conf. |
|---|---|---|---|---|---|
| Camera | cameraDuration | CameraMoveSeconds | double | 0.5 | Observed |
| Camera | cameraDecay | CameraEaseRate | double | 5.5 | Observed |
| Camera | cameraStepCap | CameraMaxStepSeconds | double | 0.032 | Observed |
| Camera | layoutEase | LayoutTweenSeconds | double | 0.46 | Observed |
| Gestures | dragThreshold | DragStartDistance | double | 5 | Inferred |
| Gestures | pinchThreshold | PinchStartDelta | double | 0.04 | Inferred |
| Gestures | pinchStep | ZoomStepDelta | double | 0.15 | Inferred |
| Gestures | touchPinchStart | TouchPinchStartDistance | double | 0.06 | Inferred, macOS |
| Gestures | touchPanStart | TouchPanStartDistance | double | 0.03 | Inferred, macOS |
| Gestures | touchPanScale | TouchPanFactor | double | 1.3 | Inferred, macOS |
| DragDrop | pickup | PickupSeconds | double | 0.28 | Inferred |
| DragDrop | dropSettle | — excluded, see `EnginePolicy.DropHold` | | 0.15 | Inferred |
| DragDrop | tileEdgeBand | TileEdgeBandFraction | double | 0.28 | Observed |
| DragDrop | desktopEdgeBand | — excluded, see `EnginePolicy.DesktopEdge` | | 0.2 | Observed |
| DragDrop | desktopEdgeMax | DesktopEdgeBandMax | double | 64 | Observed |
| DragDrop | minSeamHitWidth | SeamHitMinWidth | double | 8 | Inferred |
| DragDrop | gapHandleReach | GapHandleHitSlop | double | 5 | Inferred |
| DragDrop | gapDragRest | GapDragRestSeconds | double | 0.15 | Inferred |
| DragDrop | handoffWithin | DisplayHandOffDistance | double | 16 | Inferred |
| DragDrop | groupHold | GroupHoverSeconds | double | 0.2 | Inferred |
| DragDrop | groupHoldSlop | GroupHoverSlop | double | 6 | Inferred |
| DragDrop | splitSettles | — deferred (Unknown) | | 1 | Unknown |
| Sizes | fixedMinimum | FixedTileMinimum | Size | 480 × 320 | Observed |
| Sizes | usableFloor | TileMinimumFloor | Size | 200 × 120 | Observed |
| Sizes | compact | CompactSize | Size | 380 × 260 | Inferred |
| Sizes | compactGrabY | CompactGrabHeight | double | 18 | Inferred |
| Sizes | squeezeFloor | SqueezeFloor | double | 120 | Inferred |
| Sizes | collapseBelow | GroupCollapseBelow | double | 120 | Inferred |
| Sizes | collapsedOutline | CollapsedOutlineWidth | int | 2 | Inferred |
| Sizes | floatCap | FloatMaxFraction | double | 2/3 | Observed/Inferred |
| Sizes | floatPadding | FloatEdgePadding | double | 16 | Inferred |
| Sizes | dockedDim | — excluded, see `EnginePolicy.DimOpacity` | | 0.25 | Inferred |
| TabStrip | tabContentHeight | TabStripContentHeight | double | 20 | Inferred |
| TabStrip | tabStripUnderlap | TabStripUnderlap | double | 10 | Inferred, macOS |
| TabStrip | tabLightsLead | TabStripButtonReserve | double | 72 | Inferred, macOS |
| TabStrip | stripTuck | — deferred (Unknown) | | 0.14 | Unknown |
| TabStrip | nativeTabWatch | NativeTabWatchSeconds | double | 0.25 | Observed, macOS |
| TabStrip | nativeTabRecheck | NativeTabRecheckSeconds | double | 0.3 | Observed, macOS |
| Overview | overviewTop | OverviewTopMargin | double | 28 | Inferred |
| Overview | overviewBottom | OverviewBottomMargin | double | 96 | Inferred |
| Pictures | pictureRateChanging | ChangingPicturesPerSecond | double | 10 | Inferred |
| Pictures | pictureRateMoving | MovingPicturesPerSecond | double | 3 | Inferred |
| Pictures | pictureRateLive | LivePicturesPerSecond | double | 0.2 | Observed/Inferred |
| Pictures | pictureRatePreview | PreviewPicturesPerSecond | double | 1/6 | Observed/Inferred |
| Pictures | pictureRateUnseen | UnseenPicturesPerSecond | double | 1/30 | Observed/Inferred |
| Pictures | pictureRateAway | AwayPicturesPerSecond | double | 1/60 | Observed/Inferred |
| Robustness | coverBeyond | CoverAspectMismatch | double | 0.01 | Observed/Inferred |
| Robustness | uncoverWithin | UncoverAspectMismatch | double | 0.005 | Observed/Inferred |
| Robustness | reflowTimeout | ReflowTimeoutSeconds | double | 1.2 | Observed |
| Robustness | placementWait | LandingRevealQuietSeconds | double | 0.6 | Observed |
| Robustness | captureWait | FreshPictureWaitSeconds | double | 0.35 | Observed/Inferred |
| Robustness | refusalSlack | FrameRefusalSlack | double | 24 | Observed |
| Robustness | refusalRecheck | FrameRefusalRecheckSeconds | double | 0.25 | Observed |
| Robustness | insistCount | SnapBackLimit | int | 3 | Observed/Inferred |
| Robustness | insistWindow | SnapBackWindowSeconds | double | 3 | Observed/Inferred |
| Robustness | parkSettle | — deferred (Unknown) | | 60 | Unknown |

The table has 56 rows: 50 modelled, 3 excluded as policy, 3 deferred.

## Behaviour

- **B1 (defaults).** `new Tuning()` has exactly the defaults in the mapping
  table. Each `double` default is the C# literal written there. The ratios
  are `2.0 / 3.0`, `1.0 / 6.0`, `1.0 / 30.0` and `1.0 / 60.0`, evaluated as
  C# `double` division (A4). Sizes are `new Size(w, h)`.
- **B2 (single default).** `Tuning.Default` is created once. Every read
  returns the same instance, and it equals `new Tuning()`.
- **B3 (immutability).** Every instance property has a `get` accessor and an
  `init` accessor only. No instance or static state can change after
  construction. `Default` is a get-only static property. The record has no
  public fields and no mutable static fields. A `with` expression creates a
  new instance. The source instance, including `Default`, is unchanged.
- **B4 (validation).** Every `init` accessor validates its value before
  storing it. An invalid value throws `ArgumentOutOfRangeException` with
  `ParamName` equal to the property name (`nameof(Property)`), and no
  instance is produced. This applies to object initialisers and to `with`
  expressions alike. The validation classes are:

  | Class | Rule (value accepted when…) | Properties |
  |---|---|---|
  | **Pos** | finite and `> 0` | CameraMoveSeconds, CameraEaseRate, CameraMaxStepSeconds, LayoutTweenSeconds, ZoomStepDelta, NativeTabWatchSeconds, SnapBackWindowSeconds, ChangingPicturesPerSecond, MovingPicturesPerSecond, LivePicturesPerSecond, PreviewPicturesPerSecond, UnseenPicturesPerSecond, AwayPicturesPerSecond |
  | **NonNeg** | finite and `>= 0` | DragStartDistance, PinchStartDelta, TouchPinchStartDistance, TouchPanStartDistance, TouchPanFactor, PickupSeconds, DesktopEdgeBandMax, SeamHitMinWidth, GapHandleHitSlop, GapDragRestSeconds, DisplayHandOffDistance, GroupHoverSeconds, GroupHoverSlop, CompactGrabHeight, SqueezeFloor, GroupCollapseBelow, FloatEdgePadding, TabStripContentHeight, TabStripUnderlap, TabStripButtonReserve, NativeTabRecheckSeconds, OverviewTopMargin, OverviewBottomMargin, CoverAspectMismatch, UncoverAspectMismatch, ReflowTimeoutSeconds, LandingRevealQuietSeconds, FreshPictureWaitSeconds, FrameRefusalSlack, FrameRefusalRecheckSeconds |
  | **Band** | `0 <= v <= 0.5` (NaN fails) | TileEdgeBandFraction |
  | **Cap** | `0 < v <= 1` (NaN fails) | FloatMaxFraction |
  | **SizeNonNeg** | `Width` and `Height` both finite and `>= 0` | FixedTileMinimum, TileMinimumFloor, CompactSize |
  | **IntAtLeast1** | `>= 1` | SnapBackLimit |
  | **IntNonNeg** | `>= 0` | CollapsedOutlineWidth |

  The class sizes are 13 Pos, 30 NonNeg, 1 Band, 1 Cap, 3 SizeNonNeg and
  2 integer properties, which makes 50. Write the comparisons so that NaN is
  rejected, for example `!(double.IsFinite(v) && v > 0)` → throw. Negative
  zero (`-0.0`) passes the `>= 0` checks and is stored as given.
- **B5 (no cross-field rules).** No rule relates two properties. For example,
  `UncoverAspectMismatch > CoverAspectMismatch`, or `TileMinimumFloor` larger
  than `FixedTileMinimum`, is accepted (A6).
- **B6 (equality).** Equality and hashing are the compiler-generated record
  members over all 50 values. NaN never occurs, because B4 rejects it. Two
  instances with equal values are `==` and have equal hash codes.
- **B7 (purity).** `Tuning` performs no I/O. It does not read the
  environment, the clock or any configuration source. Loading tuning from a
  file is not part of Core. If it is ever done, it happens in App and
  produces a `Tuning` value.
- **B8 (separation from policy and park options).** No `Tuning` property
  name equals a public instance property name of `EnginePolicy` or
  `ParkOptions`. A behaviour that both policy and tuning could govern reads
  the policy (findings rule 5):
  - `EnginePolicy.FixedMinimum` (bool) selects between `FixedTileMinimum`
    and `TileMinimumFloor`.
  - `EnginePolicy.SqueezeBelowMinimum` enables `SqueezeFloor`.
  - `EnginePolicy.CollapseSmallGroups` enables `GroupCollapseBelow`.
  - The desktop-edge drop band is
    `min(EnginePolicy.DesktopEdge × extent, Tuning.DesktopEdgeBandMax)`.
  - The drop hover time is `EnginePolicy.DropHold`.
  - The docked dim amount is `EnginePolicy.DimOpacity`.

### Consumer notes (informative; implemented by the named later features)

These notes record how consumers are expected to use the values, so that
their specs read the record instead of literals. Nothing here is implemented
or tested in #8.
- Camera (#15): `t = elapsed / CameraMoveSeconds`. Progress is
  `(1 − e^(−k·t)) / (1 − e^(−k))` with `k = CameraEaseRate` for `t < 1`, and
  1 otherwise. The frame step is `min(dt, CameraMaxStepSeconds)`. A
  `dt <= 0` uses the literal 0.016. Worked example 5: elapsed 0.25 →
  ≈ 0.939913. Worked example 6.
- Layout tween (#15): `u = clamp(elapsed / LayoutTweenSeconds, 0, 1)` and
  progress is `1 − (1 − u)^5`. The tween ends once `LayoutTweenSeconds` has
  passed (engine-inbound-api B38). Worked example 7.
- Drop bands (drag/drop feature): tile edge band = `TileEdgeBandFraction`.
  The desktop band is as in B8. Worked example 8: width 1430 → 64, width
  200 → 40.
- Covers (#16): show the cover when the mismatch is
  `> CoverAspectMismatch`. Remove it when the mismatch is
  `<= UncoverAspectMismatch`. In between, the current state is kept. Worked
  example 9.
- Float cap (#13): floating width ≤ `FloatMaxFraction × desktop width`.
  Worked example 10: 1440 → 960.
- Capture (#16): `ICapture` rates are in pictures per second
  (engine-interfaces rule 24), and the interval is `1 / rate`. Worked
  example 4. The mapping from window state to rate belongs to #16.
- Frame refusal and snap-backs (#13): `FrameRefusalSlack`,
  `FrameRefusalRecheckSeconds`, `SnapBackLimit` within
  `SnapBackWindowSeconds`.
- Landing (#13/#16): `LandingRevealQuietSeconds` (0.6) is the suppression
  after a **landing** focus. A plain window focus keeps its own 1 s literal.
  `NativeTabRecheckSeconds` is 0.3, not 1.2 (see "Reported for review").

## Edge cases & errors

- **E1.** `new Tuning { CameraMoveSeconds = 0 }` throws, because a Pos
  property is used as a divisor or period. `new Tuning { PickupSeconds = 0 }`
  is accepted (no lift animation).
- **E2.** `Default with { X = invalid }` throws, and `Default` stays equal to
  `new Tuning()`.
- **E3.** Very large finite values (for example `1e9`) are accepted by every
  double class except Band and Cap. Whether a large value makes sense is up
  to the consumer.
- **E4.** `int` properties: `SnapBackLimit = 0` throws, and
  `CollapsedOutlineWidth = -1` throws. `int.MaxValue` is accepted for both.
- **E5.** A `Size` with one invalid component (for example
  `new Size(480, double.NaN)` or `new Size(-1, 320)`) throws.
  `new Size(0, 0)` is accepted.
- **E6.** `-0.0` is accepted by the NonNeg, Band and SizeNonNeg classes. It
  is rejected by Pos and Cap, because `-0.0 > 0` is false.

## Acceptance criteria

All criteria are [mac] xUnit tests. They use no OS access and no TrellisWM.
Double defaults are compared with exact `==` against the C# expression in the
mapping table. Derived intervals are compared within `1e-12` (absolute).

- **AC1 [mac] Defaults.** For both `Tuning.Default` and `new Tuning()`, each
  of the 50 properties equals its default from the mapping table (one
  assertion each):
  - CameraMoveSeconds 0.5, CameraEaseRate 5.5, CameraMaxStepSeconds 0.032,
    LayoutTweenSeconds 0.46;
  - DragStartDistance 5, PinchStartDelta 0.04, ZoomStepDelta 0.15,
    TouchPinchStartDistance 0.06, TouchPanStartDistance 0.03,
    TouchPanFactor 1.3;
  - PickupSeconds 0.28, TileEdgeBandFraction 0.28, DesktopEdgeBandMax 64,
    SeamHitMinWidth 8, GapHandleHitSlop 5, GapDragRestSeconds 0.15,
    DisplayHandOffDistance 16, GroupHoverSeconds 0.2, GroupHoverSlop 6;
  - FixedTileMinimum (480, 320), TileMinimumFloor (200, 120),
    CompactSize (380, 260), CompactGrabHeight 18, SqueezeFloor 120,
    GroupCollapseBelow 120, CollapsedOutlineWidth 2,
    FloatMaxFraction 2.0/3.0, FloatEdgePadding 16;
  - TabStripContentHeight 20, TabStripUnderlap 10,
    TabStripButtonReserve 72, NativeTabWatchSeconds 0.25,
    NativeTabRecheckSeconds 0.3;
  - OverviewTopMargin 28, OverviewBottomMargin 96;
  - ChangingPicturesPerSecond 10, MovingPicturesPerSecond 3,
    LivePicturesPerSecond 0.2, PreviewPicturesPerSecond 1.0/6.0,
    UnseenPicturesPerSecond 1.0/30.0, AwayPicturesPerSecond 1.0/60.0;
  - CoverAspectMismatch 0.01, UncoverAspectMismatch 0.005,
    ReflowTimeoutSeconds 1.2, LandingRevealQuietSeconds 0.6,
    FreshPictureWaitSeconds 0.35, FrameRefusalSlack 24,
    FrameRefusalRecheckSeconds 0.25, SnapBackLimit 3,
    SnapBackWindowSeconds 3.

  (B1; findings B1–B8, worked examples 1–3)
- **AC2 [mac] Corrected values.** These values were wrong in the old study
  table and are pinned: `LandingRevealQuietSeconds == 0.6`,
  `ReflowTimeoutSeconds == 1.2`, `ZoomStepDelta == 0.15`,
  `GapDragRestSeconds == 0.15`, `NativeTabRecheckSeconds == 0.3`. Interval
  checks within `1e-12`:
  - `1 / LivePicturesPerSecond` = 5;
  - `1 / PreviewPicturesPerSecond` = 6;
  - `1 / UnseenPicturesPerSecond` = 30;
  - `1 / AwayPicturesPerSecond` = 60;
  - `1 / ChangingPicturesPerSecond` = 0.1;
  - `1 / MovingPicturesPerSecond` = 1/3;
  - `FloatMaxFraction × 1440` = 960.

  (findings worked examples 1, 4, 10; study disagreements)
- **AC3 [mac] Default instance.** `ReferenceEquals(Tuning.Default,
  Tuning.Default)` is true. `Tuning.Default == new Tuning()` is true, and the
  hash codes are equal. (B2, B6)
- **AC4 [mac] Shape (reflection).** `typeof(Tuning)`:
  - is a sealed class in namespace `CanvasWm.Core`, in the `CanvasWm.Core`
    assembly;
  - has exactly 50 public instance properties, excluding the compiler's
    `EqualityContract`. Their name set equals the 50 names of the mapping
    table exactly. Their types are 45 `double`, 2 `int`
    (`CollapsedOutlineWidth`, `SnapBackLimit`) and 3
    `CanvasWm.Core.Geometry.Size` (`FixedTileMinimum`, `TileMinimumFloor`,
    `CompactSize`);
  - has exactly one public static property, `Default`, of type `Tuning` with
    no setter;
  - has no public fields and no non-readonly static fields (static fields
    are checked with `BindingFlags.NonPublic` too; compiler-generated
    backing fields of `Default` must be `IsInitOnly`);
  - has no assembly type and no namespace named `CanvasWm.Core.Tuning`;
  - has no property whose name is one of the excluded or deferred concepts:
    no name contains `Settle`, `Tuck` or `Dim`, and no name equals
    `DropHold`, `DesktopEdge` or `DesktopEdgeBandFraction`.

  (B3, B8; A1, A2)
- **AC5 [mac] Init-only.** Every public instance property has a public getter
  and a setter whose return parameter carries the required modifier
  `System.Runtime.CompilerServices.IsExternalInit` (that is, it is
  init-only). No property has a plain public setter. (B3)
- **AC6 [mac] With-expressions.** `var t = Tuning.Default with {
  CameraMoveSeconds = 0.25, SnapBackLimit = 5, CompactSize = new Size(300,
  200) }` gives:
  - `t.CameraMoveSeconds == 0.25`, `t.SnapBackLimit == 5` and
    `t.CompactSize == (300, 200)`;
  - every other property of `t` equal to `Default`'s (compared by
    reflection over all 50);
  - `t != Tuning.Default`;
  - `Tuning.Default` still equal to `new Tuning()`.

  `t with { CameraMoveSeconds = 0.5, SnapBackLimit = 3, CompactSize = new
  Size(380, 260) }` equals `Tuning.Default`. (B3, B6)
- **AC7 [mac] Validation rejects.** Each case below throws
  `ArgumentOutOfRangeException` whose `ParamName` is the property name. Each
  case is run both as an object initialiser and as `Tuning.Default with {…}`.
  Afterwards `Tuning.Default == new Tuning()` still holds. The cases are
  theory data over the B4 table:
  - every Pos, NonNeg, Band and Cap property: `double.NaN`,
    `double.PositiveInfinity`, `double.NegativeInfinity` and `-0.001`;
  - every Pos property: `0` and `-0.0`;
  - TileEdgeBandFraction: `0.5000001` and `1`;
  - FloatMaxFraction: `0`, `-0.0` and `1.0000001`;
  - each Size property: `new Size(NaN, 1)`, `new Size(1, NaN)`,
    `new Size(-1, 1)`, `new Size(1, -1)` and
    `new Size(double.PositiveInfinity, 1)`;
  - SnapBackLimit: `0` and `-1`;
  - CollapsedOutlineWidth: `-1`.

  (B4; E1, E2, E4, E5, E6)
- **AC8 [mac] Validation accepts boundaries.** Each case below is accepted
  and reads back the value it was given:
  - every NonNeg property: `0` and `1e9`;
  - every Pos property: `double.Epsilon` and `1e9`;
  - TileEdgeBandFraction: `0` and `0.5`;
  - FloatMaxFraction: `double.Epsilon` and `1`;
  - each Size property: `new Size(0, 0)`;
  - SnapBackLimit: `1` and `int.MaxValue`;
  - CollapsedOutlineWidth: `0`.

  `PickupSeconds = -0.0` is accepted. Cross-field "inverted" settings are
  accepted: `UncoverAspectMismatch = 0.02` with `CoverAspectMismatch` left at
  0.01, and `TileMinimumFloor = new Size(600, 400)`. (B4, B5; E1, E3, E6)
- **AC9 [mac] Validation class coverage.** The test's theory data for AC7 and
  AC8 lists each of the 50 properties in exactly one B4 class. A
  reflection-based meta-test asserts that the union of the class lists equals
  the property set from AC4, with no duplicates. (B4)
- **AC10 [mac] Separation from policy and park options** (in
  `tests/CanvasWm.Engine.Tests`). The set of public instance property names
  of `Tuning` is disjoint from that of `CanvasWm.Engine.EnginePolicy` and
  from that of `CanvasWm.Core.Parking.ParkOptions`. `EnginePolicy.Default`
  still has `DropHold == 0.15`, `DesktopEdge == 0.2` and
  `DimOpacity == 0.25` (cross-check, unchanged). (B8; A2)
- **AC11 [mac] Architecture and hygiene.**
  - `dotnet build CanvasWm.slnx` exits 0 with 0 warnings;
  - `CanvasWm.Core.csproj` project and package references are unchanged
    (still none);
  - the only added `src/` file is `src/CanvasWm.Core/Tuning.cs`. No
    existing file under `src/` is modified (in particular `Size.cs`,
    `ParkOptions.cs` and `EnginePolicy.cs`);
  - `Tuning.cs` contains no `System.IO`, `System.Net`, `Environment.`,
    `File.`, `DateTime` or `Stopwatch`;
  - no file in `src/` or `tests/` contains `TrellisWM` or `Licensing`;
  - the existing architecture tests (Core references nothing; Engine
    references only Core) still pass.

  (B7; CLAUDE.md)

Count: **11 [mac]** (AC1–AC11), **0 [windows]**. Windows retuning of the
macOS-specific values is checked when the consuming features (#15 touch
gestures, #20 tab strips) land.

## Assumptions

- **A1. Namespace `CanvasWm.Core`, file at the project root.** This avoids
  the type/namespace clash that `CanvasWm.Core.Tuning.Tuning` would cause,
  and it matches folder to namespace. Consumers write `using CanvasWm.Core;`.
- **A2. Policy-equal constants are excluded.** *Needs human review.* This
  follows the approved scope (policy wins). Findings rule 5 notes that
  `dropSettle` reads as *settle after a drop*, while `EnginePolicy.DropHold`
  is *hover before a drop target engages*, so they may be different knobs.
  The same may be true of `desktopEdgeBand`, which the drop resolver uses as
  a literal. If a later feature shows that a separate knob exists, that
  feature adds it (for example `DropSettleSeconds = 0.15`). `dockedDim` is
  very likely the same knob as `DimOpacity`.
- **A3. `FixedTileMinimum` is kept, not excluded.** Its value (a size) is
  not a policy field. The policy's `FixedMinimum` is a bool that selects it.
  The property is renamed so the two names do not collide (AC10).
- **A4. Ratio defaults are written as C# divisions** (`2.0 / 3.0`,
  `1.0 / 6.0`, `1.0 / 30.0`, `1.0 / 60.0`). *Needs confirmation by
  re-analyst.* The findings give these as fractions, and we assume that the
  decoded bits are the nearest double to each ratio, which is what C#
  produces. If a decoded bit pattern differs, the default becomes that exact
  hexadecimal value and AC1 changes with it.
- **A5. Validation ranges are CanvasWM choices** (B4). *Needs human review.*
  - Pos covers values used as a divisor, a period, a cap or a rate, where 0
    means divide-by-zero, a busy loop or a frozen camera. Two of these are
    judgement calls: `ZoomStepDelta` (0 would make zoom steps do nothing)
    and `SnapBackWindowSeconds`.
  - NonNeg covers distances, delays and thresholds, where 0 is a meaningful
    "off" or "immediate".
  - The tile edge band is capped at 0.5 because beyond that the two
    opposite edge zones overlap.
  - The float cap is limited to `(0, 1]` because a window cannot take more
    than the desktop.
  - Touch distances get no upper bound, because Windows touchpad units
    differ.
  - `-0.0` passes the `>= 0` checks by IEEE semantics.
- **A6. No cross-field validation.** `init` accessors run in source order of
  the initialiser, so checks that relate two properties would depend on that
  order and would break `with` chains. The hysteresis order
  (uncover ≤ cover) and floor ≤ fixed minimum are left to consumers. A
  `Validate()` method can be added later if a need appears.
- **A7. macOS-specific constants are kept with their macOS values**:
  `TouchPinchStartDistance`, `TouchPanStartDistance`, `TouchPanFactor`,
  `TabStripUnderlap`, `TabStripButtonReserve`, `NativeTabWatchSeconds` and
  `NativeTabRecheckSeconds`. They are kept for parity and documented. On
  Windows:
  - the button reserve applies on the **right** (decided by #20);
  - native-tab timers are unused, because there are no native tabs
    (engine-inbound-api A8);
  - touch values need calibration against precision-touchpad input (#15).

  Changing these defaults for Windows is a later, human-approved decision.
- **A8. Points map 1:1 to DIPs.** Per-monitor DPI scaling happens in Win32
  and never in the record.
- **A9. Property names follow the findings' meanings, including Inferred
  ones.** Examples: `GroupHoverSeconds` and `DisplayHandOffDistance`. If a
  later feature finds a different meaning, it may rename a property. It must
  report the rename, because it changes this spec.
- **A10. Engine wiring is deferred.** The intent is that a later feature
  (expected #13) gives `DisplayEngine` a `Tuning` (default
  `Tuning.Default`) next to `EnginePolicy`. That feature also replaces the
  literals already specified elsewhere that equal Tuning values, such as the
  0.46 s tween end in engine-inbound-api B38, with reads of the record.
  Nothing is wired in #8.
- **A11. The integer members stay `int`** (`SnapBackLimit`,
  `CollapsedOutlineWidth`), as observed, even though an outline width would
  naturally be a `double`. A consumer that draws with it converts it.

## Reported for review (no change to earlier specs or findings)

- `findings/engine-tiling-loop.md` (rule 25 and its constants list) states a
  native-tab recheck of 1.2 s. The tuning findings correct this to **0.3 s**
  (`NativeTabRecheckSeconds`). It also notes that the reveal suppression
  after a **landing** focus is 0.6 s (`LandingRevealQuietSeconds`). The #13
  spec should use these values.
- `specs/engine-inbound-api.md` B38 says "end the layout tween after
  0.46 s". This is consistent with `LayoutTweenSeconds`. #15 should read the
  record.
- `specs/park-geometry.md` keeps reach 60 as a park literal, which is
  consistent with `parkSettle` being deferred (findings rule 6).

## Traceability

| Spec item | Findings rule / section |
|---|---|
| Goal, B2, B3, AC3, AC5, AC6 | rule 1 (fixed, immutable, injectable `Default`) |
| Goal, A10, Consumer notes | rule 2 (inlined; consumers read the record) |
| Public API types, AC4, A11 | rule 3 (51 double / 2 int / 3 size → 45/2/3 after exclusions) |
| Units, A8 | rule 4; Windows notes (points → DIPs) |
| B1, AC1, A4 | tables B1–B8; worked examples 1–3 |
| AC2 | worked examples 1, 4, 10; study-vs-binary disagreements |
| B8, AC10, A2, A3, exclusions | rule 5 (section C) |
| Out of scope (literals, park reach), Reported for review | rule 6 (section D); engine-tiling-loop correction |
| Deferred list | Unknowns (`parkSettle`, `splitSettles`, `stripTuck`) |
| A7 | Windows notes (macOS-specific constants) |
| B4, B5, AC7–AC9, A5, A6 | CanvasWM choice (approved scope: validation) |
| B7, AC11 | CLAUDE.md architecture (Core, no I/O) |
| Consumer notes | worked examples 4–10; tables B1, B3, B4, B7, B8 |
