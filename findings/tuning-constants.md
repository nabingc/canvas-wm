# Feature: tuning-constants (`Feel`-equivalent settings record)

## Summary
TrellisWM keeps its internal "feel" numbers — animation lengths, gesture
thresholds, drop-zone bands, size floors, capture rates, robustness timeouts —
in one namespace (`Feel`) of 56 fixed constants. They are compile-time
constants: never written, never per-display, never user-editable, and the
compiler copied each value directly into the code that uses it. This feature
is a single immutable Core record that holds these 56 values with their
decoded defaults, so every later feature reads its tuning from one place.

## Behaviour

### A. Nature of the record
1. **Fixed, global, immutable.** `Feel` is a case-less namespace with 56
   static read-only properties; no setter, no initialiser, no instance, no
   per-display copy exists. Nothing at runtime changes them. The Windows
   record should still be an injectable immutable record (one instance,
   `Default`) so tests can vary values — that is a design choice, not
   behaviour.
2. **Inlined at use sites.** None of the 56 getters is ever called; every
   consumer embeds the literal value. Consequence: a literal found in an
   algorithm may or may not "be" the Feel constant (rule 6). For CanvasWM,
   consumers should read the record rather than repeat the literal.
3. **Value types.** 51 are `double`, 2 are `int` (`insistCount`,
   `collapsedOutline`), 3 are sizes (w × h doubles: `compact`,
   `usableFloor`, `fixedMinimum`).
4. **Units** (convention used below): sizes/distances in points (DIP on
   Windows); times in seconds; "fraction" = unitless 0–1 ratio;
   "fps" = pictures per second; "factor" = unitless multiplier.

### B. The constants (decoded binary values; the table in study §3.1 is superseded)

Legend for "Consumer": where the value is seen used (from the literal scan
and earlier findings). "MacOnly" = tied to a macOS-specific mechanism.

#### B1. Camera and layout animation
| Name | Type | Unit | Default | Meaning / consumer | Conf. |
|---|---|---|---|---|---|
| cameraDuration | double | s | 0.5 | Length of a camera move. `CameraCurve.step` normalises elapsed time by it (t = elapsed / 0.5). | Observed |
| cameraDecay | double | rate | 5.5 | Ease-out steepness of a camera move: progress = (1 − e^(−5.5·t)) / (1 − e^(−5.5)) for t < 1, else 1. | Observed |
| cameraStepCap | double | s | 0.032 | Largest time step a single animation frame may advance the camera (frame hitches do not jump). A step ≤ 0 is replaced by 0.016 (literal, not in Feel). | Observed |
| layoutEase | double | s | 0.46 | Length of a layout tween (tiles sliding to new rects). Progress = 1 − (1 − u)^5 with u = elapsed/0.46 clamped to [0,1]; `tick` ends the tween once 0.46 s passed (engine-inbound-api rule 26). | Observed |

#### B2. Pointer and trackpad gestures
| Name | Type | Unit | Default | Meaning / consumer | Conf. |
|---|---|---|---|---|---|
| dragThreshold | double | pt | 5 | Pointer travel before a press becomes a drag. | Inferred (name; value too common to trace) |
| pinchThreshold | double | factor delta | 0.04 | Pinch magnitude before a pinch starts zooming; seen in `Engine.pinch`. | Inferred |
| pinchStep | double | factor delta | 0.15 | Zoom change per discrete pinch/zoom step. Value shared with other 0.15 constants, so consumer not isolated. | Inferred |
| touchPinchStart | double | normalized touch distance | 0.06 | Raw two-finger spread before a touch pinch is recognised; seen in `Coordinator.deskTouch`. MacOnly (raw trackpad touches). | Inferred |
| touchPanStart | double | normalized touch distance | 0.03 | Two-finger travel before a touch pan is recognised; seen in `Coordinator.deskTouch`. MacOnly. | Inferred |
| touchPanScale | double | factor | 1.3 | Multiplier from touch travel to camera pan. MacOnly. | Inferred |

#### B3. Drag, drop and groups
| Name | Type | Unit | Default | Meaning / consumer | Conf. |
|---|---|---|---|---|---|
| pickup | double | s | 0.28 | Duration of the "lift" animation when a window is picked up. | Inferred (name) |
| dropSettle | double | s | 0.15 | Settle time after a drop before re-layout proceeds. Same value as Policy `DropHold` (see C). | Inferred |
| tileEdgeBand | double | fraction | 0.28 | Fraction of a tile, measured from each edge, that counts as an "insert beside on this edge" drop zone; passed as the band to the drop-edge test in `DropResolver`. | Observed (value passed to `dropEdge(band:)`) |
| desktopEdgeBand | double | fraction | 0.2 | Drop band at the desktop/stage edge = 0.2 × the stage's extent on that axis … | Observed (in `DropResolver`) |
| desktopEdgeMax | double | pt | 64 | … capped at 64 pt. Also seen in `Engine.tabDragged`. | Observed |
| minSeamHitWidth | double | pt | 8 | Minimum grab width of a split seam (gap between tiles) for resize hits (docking.md mentions, out of its scope). | Inferred |
| gapHandleReach | double | pt | 5 | Extra reach around a gap handle for hit testing. | Inferred |
| gapDragRest | double | s | 0.15 | Pause after a gap (seam) drag before it is treated as at rest / committed. | Inferred |
| handoffWithin | double | pt | 16 | Distance from a display edge within which a dragged window is handed to the neighbouring display's engine. | Inferred |
| groupHold | double | s | 0.2 | Hover time over a window before a dragged window groups with it (tab group); value seen in `Engine.holdForGroup`. | Inferred |
| groupHoldSlop | double | pt | 6 | Movement allowed during that hover without restarting the timer. | Inferred |
| splitSettles | double | count/s | 1 | Unknown; name suggests how long/how many layout passes a new split needs to settle. Not traced (value 1 too common). | Unknown |

#### B4. Sizes and floors
| Name | Type | Unit | Default | Meaning / consumer | Conf. |
|---|---|---|---|---|---|
| fixedMinimum | size | pt | 480 × 320 | Tile minimum used when Policy `FixedMinimum` is on (layout-solver, engine-tiling-loop rule on tile minimums). | Observed |
| usableFloor | size | pt | 200 × 120 | Default tile minimum floor when `FixedMinimum` is off (layout-solver; park-geometry). | Observed |
| compact | size | pt | 380 × 260 | Size threshold/target for compact presentation; seen in `MoveSession.animate` (lifted window scaled toward 380 × 260 while moved). | Inferred |
| compactGrabY | double | pt | 18 | Height of the grab area in compact presentation. | Inferred |
| squeezeFloor | double | pt | 120 | Smallest a tile may be squeezed to when squeezing below minimums (Policy `SqueezeBelowMinimum`). | Inferred |
| collapseBelow | double | pt | 120 | Group collapses to an outline when its tile is below this size (Policy `CollapseSmallGroups`); value seen in `Scene.collapsedGroups`. | Inferred |
| collapsedOutline | int | pt | 2 | Stroke width of a collapsed group's outline. | Inferred |
| floatCap | double | fraction | 2/3 (0.6666…) | Largest fraction of the desktop a floating window may take; seen in `Arrangement.floatingSize`, `Engine.floatRect`, `Engine.liveFloatRect`. | Observed (used) / Inferred (axis) |
| floatPadding | double | pt | 16 | Inset kept between a floating window and the desktop edge. | Inferred |
| dockedDim | double | fraction | 0.25 | Dim amount for docked/background tiles. Same value as Policy `DimOpacity` (see C). | Inferred |

#### B5. Tab strips
| Name | Type | Unit | Default | Meaning / consumer | Conf. |
|---|---|---|---|---|---|
| tabContentHeight | double | pt | 20 | Content height of a tab strip. | Inferred |
| tabStripUnderlap | double | pt | 10 | How far the tab strip tucks under the window's top edge. | Inferred |
| tabLightsLead | double | pt | 72 | Leading space reserved for the window's title-bar buttons (macOS "traffic lights"); value seen in `Engine.tabPressed`, `resolveCandidate`. MacOnly (Windows buttons sit on the right). | Inferred |
| stripTuck | double | fraction/s | 0.14 | Tucked-strip parameter (Policy `TuckStrips`). Same value as Policy `CoverFade`; consumer not isolated. | Unknown |
| nativeTabWatch | double | s | 0.25 | Period of the native-tab watcher scheduled on adopt (engine-tiling-loop rule 23). MacOnly. | Observed |
| nativeTabRecheck | double | s | 0.3 | Delay before re-checking native tab groups after a window is added, removed or focused (clock-scheduled 0.3 s in `windowAdded`, `windowRemoved`, `windowFocused`). MacOnly. | Observed (delay); Inferred (target is the tab recheck) |

#### B6. Overview
| Name | Type | Unit | Default | Meaning / consumer | Conf. |
|---|---|---|---|---|---|
| overviewTop | double | pt | 28 | Top margin of the overview layout. | Inferred |
| overviewBottom | double | pt | 96 | Bottom margin of the overview (room for the minimap/strip). | Inferred |

#### B7. Live pictures (capture rates)
| Name | Type | Unit | Default | Meaning / consumer | Conf. |
|---|---|---|---|---|---|
| pictureRateChanging | double | fps | 10 | Capture rate for a window whose picture is actively changing (e.g. being resized under a cover). | Inferred |
| pictureRateMoving | double | fps | 3 | Capture rate while the camera/stage is moving. | Inferred |
| pictureRateLive | double | fps | 0.2 | At-rest rate for a visible, on-stage window (one picture per 5 s). Selected in `Engine.restPictureRates`. | Observed (selection) / Inferred (state) |
| pictureRatePreview | double | fps | 1/6 ≈ 0.1667 | At-rest rate for a preview-only window (one per 6 s). `restPictureRates`. | Observed / Inferred |
| pictureRateUnseen | double | fps | 1/30 ≈ 0.0333 | At-rest rate for a window not currently seen (one per 30 s). `restPictureRates`. | Observed / Inferred |
| pictureRateAway | double | fps | 1/60 ≈ 0.0167 | At-rest rate for a window far away / on another space (one per minute). `restPictureRates` chooses between Unseen and Away by a flag. | Observed / Inferred |

The fps unit comes from the capture service contract (engine-interfaces rule
24: `setRate(key, fps)` stores 1/fps).

#### B8. Covers, placement and robustness
| Name | Type | Unit | Default | Meaning / consumer | Conf. |
|---|---|---|---|---|---|
| coverBeyond | double | fraction | 0.01 | A cover (picture stand-in) is shown when a window's aspect ratio differs from its target's by more than 1 %; `Engine.updateCovers`. | Observed (compare) / Inferred (meaning) |
| uncoverWithin | double | fraction | 0.005 | The cover is removed once the mismatch is within 0.5 % (hysteresis with coverBeyond); `Engine.updateCovers`. | Observed / Inferred |
| reflowTimeout | double | s | 1.2 | Give-up time: covers are dropped once shapes have rested for more than 1.2 s (`updateCovers`, compares now − shapesRestingSince); `settleFloat` schedules its follow-up after 1.2 s. | Observed |
| placementWait | double | s | 0.6 | After a landing focuses a window, focus-driven "reveal" reactions are suppressed for 0.6 s (`focusAfterLanding` sets suppressRevealUntil = now + 0.6). | Observed |
| captureWait | double | s | 0.35 | Once real-window placement has finished, the engine waits at most 0.35 s for fresh pictures before moving on (`Engine.advance`, now − placementDone > 0.35). | Observed (compare) / Inferred (meaning) |
| refusalSlack | double | pt | 24 | Tolerance when comparing a written frame with the frame the app actually took (engine-tiling-loop rules 16–18). | Observed |
| refusalRecheck | double | s | 0.25 | Delay before re-reading a possibly refused frame (engine-tiling-loop rule 16). | Observed |
| insistCount | int | count | 3 | Number of snap-backs within insistWindow after which the engine accepts the app's size (engine-tiling-loop rule 38). | Observed (value) / Inferred (accounting) |
| insistWindow | double | s | 3 | Time window for insistCount. | Observed / Inferred |
| parkSettle | double | ? | 60 | Unknown role. The value 60 appears in `parkFrame` (park reach, see C/rule 6), `appResized` (zoom-request height margin) and `releaseAll`; since constants are inlined, which of these (if any) is `parkSettle` cannot be told. | Unknown |

### C. Separation from user-facing Policy (#23 `EnginePolicy`) — no duplication
5. These Feel constants are **not** Policy fields. Overlaps where a Feel
   value equals a Policy default (keep both; they are different knobs unless
   later evidence merges them):
   - `dropSettle` 0.15 ↔ `EnginePolicy.DropHold` 0.15 (user "drop hold"
     time). Policy is the user-tunable one; the drop code reads the policy.
     Whether `dropSettle` is a separate settle delay or a leftover default is
     Unknown.
   - `desktopEdgeBand` 0.2 ↔ `EnginePolicy.DesktopEdge` 0.2. `DropResolver`
     uses the literal 0.2 (with the 64 cap), not a policy load, at the site
     inspected. Unknown whether the policy value overrides it elsewhere.
   - `dockedDim` 0.25 ↔ `EnginePolicy.DimOpacity` 0.25 (likely same visual
     knob; Policy wins for rendering).
   - `stripTuck` 0.14 ↔ `EnginePolicy.CoverFade` 0.14 — numeric coincidence
     only as far as known.
   - `fixedMinimum` (size 480 × 320) ↔ `EnginePolicy.FixedMinimum` (bool):
     the bool selects between `fixedMinimum` and `usableFloor`.
   - `collapseBelow` / `squeezeFloor` are thresholds for the Policy switches
     `CollapseSmallGroups` / `SqueezeBelowMinimum`.

### D. Literals inside already-specified algorithms
6. Because Feel values are inlined, a literal equal to a Feel value may be
   either. Known cases:
   - Park reach **60** (park-geometry): equals `parkSettle` 60. Name
     ("settle", a time-like word) argues it is a different constant; treat
     the reach as a park-geometry literal and keep `parkSettle` with role
     Unknown. Park overlap sliver **1** and rest tolerance **1** are not Feel
     values (no Feel constant equals 1 except `splitSettles`).
   - Refusal slack **24** and recheck **0.25 s** in the frame-write path are
     the Feel values `refusalSlack` / `refusalRecheck` (names and values
     match the behaviour) — consumers should read the record.
   - Insist **3 within 3 s** = `insistCount` / `insistWindow`.
   - Layout tween **0.46 s** in `tick` = `layoutEase`.
   - Not in Feel (pure literals of their algorithms): camera fallback step
     0.016 s; layout-tween ease exponent 5; refusal "0.9" factor; zoom
     request 0.92/0.90 and 100/60; close-quiet and window-focus reveal
     suppression 1 s; onStage 0.995; fits slack 0.5; park visible fraction
     0.05; settle delay coverFade + 0.02; startWhenCaptured deadline 1 s.
     These stay in their own feature specs.

## Data model
- `Tuning` (neutral name for `Feel`): immutable record, one `Default`
  instance, 56 members as listed in B (51 doubles, 2 ints, 3 sizes). No
  per-display or runtime mutation. Size members are `(Width, Height)` in DIP.
- Grouping suggestion mirroring B: Camera, Gestures, DragDrop, Sizes,
  TabStrip, Overview, PictureRates, Robustness. Grouping is for readability
  only; TrellisWM keeps one flat namespace.

## Worked examples
1. `Default.PlacementWait` = 0.6; `Default.ReflowTimeout` = 1.2;
   `Default.DropSettle` = 0.15; `Default.PinchStep` = 0.15;
   `Default.GapDragRest` = 0.15; `Default.NativeTabRecheck` = 0.3
   (each differs from the old study table).
2. `Default.FixedMinimum` = (480, 320); `UsableFloor` = (200, 120);
   `Compact` = (380, 260).
3. `Default.InsistCount` = 3 (int); `CollapsedOutline` = 2 (int).
4. Picture rates, as intervals: Live 0.2 fps → 5 s; Preview 1/6 → 6 s;
   Unseen 1/30 → 30 s; Away 1/60 → 60 s; Changing 10 → 0.1 s; Moving 3 →
   ≈ 0.333 s.
5. Camera curve at elapsed 0.25 s (t = 0.5): (1 − e^−2.75)/(1 − e^−5.5)
   = (1 − 0.063928)/0.995913 ≈ 0.939913. At elapsed ≥ 0.5 s → 1.
6. Camera step with dt 0.1 → advances 0.032; dt 0 or negative → 0.016.
7. Layout tween at elapsed 0.23 s (u = 0.5) → 1 − 0.5^5 = 0.96875; at 0 →
   0; at ≥ 0.46 → 1; negative elapsed → 0.
8. Desktop edge band: stage width 1430 → min(0.2 × 1430, 64) = 64; width
   200 → 40.
9. Cover hysteresis: aspect mismatch 0.012 → cover; while covered,
   mismatch 0.007 → stays covered; 0.004 → uncovered.
10. Float cap: desktop 1440 wide → floating width ≤ 960.

## Evidence
- Rule 1, 3 (record nature, count, types): `symbols/all-defined.demangled.txt`
  lines 4213–4440 (56 `static Feel.x` with getter, addressor, property
  descriptor; no setter/modify/init); type codes in
  `symbols/feel-constants.tsv`.
- Rule 2 (inlining): no `bl` to any of the 112 Feel getter/addressor
  addresses in the full `__text` disassembly; the scan
  `tools/feel-consumers.mjs` → `evidence/feel-literal-consumers.txt`.
- All B values: `symbols/feel-constants.tsv` (decoded by `tools/feel.mjs`
  from getters 0x100090240–0x1000906f8).
- B1: `CameraCurve.step` 0x1000927c0 (0.032 cap, 0.016 fallback, ×2,
  −5.5, divisor 0.995913), `LayoutTween.progress` 0x100092750 (÷0.46,
  pow 5), `Engine.tick` 0x10003b9e8 — `evidence/asm-feel-consumers.txt`.
- B3: `DropResolver.resolveUnscoped` 0x100098a54 (0.28 → `dropEdge(band:)`)
  and 0x100099780 (0.2 × extent vs 64); `Engine.holdForGroup` 0x10009e0f4
  (0.2); `MoveSession.animate` 0x1000974b8 (380/260).
- B4: layout-solver.md (floors), engine-tiling-loop.md rule on minimums;
  floatCap sites 0x100016b9c, 0x10009f5b0, 0x1000e9084.
- B5: native-tab delays 0x1000eaa24 (windowRemoved), 0x1000ebc90
  (windowAdded), 0x1000f06f4 (windowFocused) — `clock.after(0.3, …)`;
  watcher 0.25 from engine-tiling-loop rule 23.
- B7: `Engine.restPictureRates` 0x1000d0870–0x1000d08b4; engine-interfaces
  rule 24 (fps contract).
- B8: `updateCovers` 0x1000d2084 (0.01), 0x1000d21a8 (0.005), 0x1000d3164
  (now − shapesRestingSince 0xd78 > 1.2); `settleFloat` 0x1000e4a7c
  (after 1.2); `focusAfterLanding` 0x10003f898 (suppressRevealUntil 0xdd0 =
  now + 0.6); `advance` 0x10003bd1c (now − placementDone 0xb50 > 0.35);
  field names from `evidence/engine-field-offsets.txt`; refusal/insist from
  findings/engine-tiling-loop.md (rules 16–18, 38).
- C: findings/engine-inbound-api.md rule 23, `src/CanvasWm.Engine/EnginePolicy.cs`.
- D: park reach — `evidence/asm-park-geometry.json` (`parkFrame` ±60 at
  0x100029cd8/0x100029ce8); literal-scan hits for 60 in `appResized`,
  `releaseAll`.

## Confidence
- Values and types of all 56 constants: **Observed** (exact decode).
- Immutable/global/non-per-display: **Observed**.
- Consumer and meaning: Observed for cameraDuration, cameraDecay,
  cameraStepCap, layoutEase, tileEdgeBand, desktopEdgeBand/Max, fixedMinimum,
  usableFloor, floatCap (site), nativeTabWatch, nativeTabRecheck (delay),
  pictureRate Live/Preview/Unseen/Away (selection), coverBeyond,
  uncoverWithin, reflowTimeout, placementWait, captureWait (compare),
  refusalSlack, refusalRecheck, insistCount/Window.
- **Inferred** (name + plausible site) for the rest of the gesture, drag,
  size, tab and overview constants.
- **Unknown**: splitSettles, stripTuck, parkSettle roles.

## Unknowns
- `parkSettle` (60): role and unit. Coincides with the park reach and the
  zoom-request height margin; inlining hides which literal is which.
- `splitSettles` (1) and `stripTuck` (0.14): meaning and consumer.
- Whether `dropSettle`/`desktopEdgeBand`/`dockedDim` are overridden by the
  equal-valued Policy fields at runtime or are independent constants.
- Exact pictureRate state mapping (which window states get Changing/Moving
  vs Live/Preview/Unseen/Away) — belongs to the capture/rendering feature.
- Common-valued constants (dragThreshold 5, gapHandleReach 5,
  minSeamHitWidth 8, floatPadding 16, handoffWithin 16, groupHoldSlop 6,
  pickup 0.28, pinchStep 0.15) could not be tied to a site by value scan.

## Study-vs-binary disagreements (binary wins)
| Constant | Study §3.1 | Decoded |
|---|---|---|
| placementWait | 1.2 | **0.6** |
| dropSettle | 1.2 | **0.15** |
| pinchStep | 1.2 ("zoom factor per step") | **0.15** (a delta, not a factor) |
| gapDragRest | 1.2 | **0.15** |
| nativeTabRecheck | 1.2 | **0.3** |
| pictureRate* | "Unknown units / intervals" | fps (1/fps = interval) |
| study note "units Inferred from names" | — | camera/tween/cover/placement/capture units now Observed by use |

Also corrects an earlier finding: findings/engine-tiling-loop.md (rule 25
and its constants list) says the native-tab recheck is **1.2 s**; the code
schedules **0.3 s**. Its "reveal suppress 1 s" is correct for
`windowFocused`, but after a landing focus the suppression is **0.6 s**
(`placementWait`).

## Windows notes
- MacOnly constants (keep in the record for parity, but consumers on
  Windows may ignore or retune): `tabLightsLead` (left traffic lights;
  Windows caption buttons are on the right — reserve on the right instead),
  `nativeTabWatch`/`nativeTabRecheck` (macOS native window tabs; no Win32
  equivalent), `touchPinchStart`/`touchPanStart`/`touchPanScale` (raw
  trackpad touches; Windows precision-touchpad gestures arrive as
  `WM_POINTER`/DirectManipulation with different units — needs calibration),
  `tabStripUnderlap` (relative to a macOS title bar).
- Point units → DIPs; per-monitor DPI scaling happens in Win32, not in the
  record.
- Picture rates map to capture intervals for Windows.Graphics.Capture /
  DWM thumbnails; DWM thumbnails are live and need no rate, so the rates
  matter only if bitmap capture is used.
- `cameraStepCap` 0.032 assumes a vsync tick; drive `tick` from a
  monitor-rate timer (e.g. `DwmFlush`/composition) — same constant applies.
