# Feature: park-geometry

## Summary
"Parking" is how TrellisWM gets a real window out of sight without hiding or
minimising it: the window is moved so that it hangs off the edge of its own
display, overlapping that display by exactly one point, at a corner or side
chosen so it does not land on any *other* display. Park geometry is a small
pure computation: (1) a park **size** (the window's size shrunk to fit the
display's visible area, never below its minimum; optionally a "small" size
that fits one third of the visible area), (2) five **position candidates**
around the display frame, each scored by how much of a slightly enlarged
"reach" rectangle overlaps the other displays, and (3) two **recognisers**:
"is this frame exactly a park frame?" and "has this window come to rest at its
park spot (within tolerance)?".

Scope: this finding covers the geometry only (Core). *Which* windows are
parked and *when* is decided by the engine's per-pass target computation
(`Engine.realTargets`), summarised in rules 22–26 for context; it belongs to
the Engine spec, not this Core feature.

## Behaviour

### Inputs the geometry needs (per display engine)
1. `visibleArea` — the display's usable area (below the menu bar / dock), a
   rect. Only its **width and height** are used by park geometry.
2. `displayFrame` — the display's full frame, an *optional* rect. When it is
   present, candidates are built around it. When absent, see rule 13.
3. `otherDisplays` — list of frames of the **other** displays (the display's
   own frame is not in this list). Candidates are scored only against these.
4. All rects are global, **y-down** (top-left origin), in points (macOS
   global "top-left" coordinates; the adapter converts AppKit's y-up screen
   frames by `y = primaryHeight − maxY`).

### Park size — `parkSize(size, minimum?)`
5. Fit-to-visible: compute `sx = visible.w / size.w` and `sy = visible.h /
   size.h`, `s = min(sx, sy)`. If `s < 1`, the size becomes
   `(round(size.w·s), round(size.h·s))` with round-half-away-from-zero;
   otherwise the size is unchanged (never enlarged). Aspect ratio is kept.
6. Degenerate-axis guard: an axis whose window dimension is ≤ 1 pt is not used
   as a divisor; in its place the raw visible extent is compared (which is
   always ≫ 1 for a real display, so in practice that axis simply does not
   constrain the scale). Treat as "skip axes ≤ 1 pt".
7. Minimum floor (only if a minimum is given): each dimension becomes
   `max(fitted, ceil(minimum))`. Applied *after* the fit, so a large minimum
   can make the park size exceed the visible area. Minimum is the window's
   learned/declared minimum size (`WindowLimits.minimum`), or none.
8. Positions are **not** rounded by park geometry; only sizes are.

### Park-small size (policy `parkOutOfViewSmall`)
9. When the user setting "park out-of-view windows small" is on, the caller
   first shrinks the window's current size to fit **one third of the visible
   area in each dimension** (`visible.w / 3`, `visible.h / 3`), aspect kept,
   same rounding, only shrinking, same ≤ 1 pt axis guard; then calls the park
   frame computation with **no minimum**. So the "park-small factor" is
   **1/3 of the visible area per axis**, not a fixed scale of the window.
   (The engine also records which windows were parked small in a key set,
   `Engine.parkedSmall`.)

### Position candidates (displayFrame present)
Let `D = displayFrame` (minX, minY, maxX, maxY), park size `W × H`,
`REACH = 60`.
10. Five candidates, in this order; each is a **spot** (the rect the window is
    moved to) and a **reach** (spot widened vertically, used only for scoring):

    | # | where | spot (x, y, w, h) | reach (x, y, w, h) |
    |---|---|---|---|
    | 0 | below-right, diagonal | (D.maxX−1, D.maxY−1, W, H) | (D.maxX−1, D.maxY−60, W, H+60) |
    | 1 | below-left, diagonal | (D.minX−W+1, D.maxY−1, W, H) | (D.minX−W+1, D.maxY−60, W, H+60) |
    | 2 | right side, top-aligned | (D.maxX−1, D.minY, W, H) | (D.maxX−1, D.minY, W, H+60) |
    | 3 | left side, top-aligned | (D.minX−W+1, D.minY, W, H) | (D.minX−W+1, D.minY, W, H+60) |
    | 4 | below, left-aligned | (D.minX, D.maxY−1, W, H) | (D.minX, D.maxY−60, W, H+60) |

    Every spot overlaps its own display by exactly **1 pt** (a 1×1 corner for
    0/1, a 1-pt column for 2/3, a 1-pt row for 4). The window is never placed
    fully outside its display.
11. Reach models how far the OS may push the window back toward the screen
    (macOS keeps part of a window reachable): for the "below" candidates it
    extends the spot 59 pt upward and 1 pt downward; for the side candidates
    it extends the spot 60 pt downward.
12. Scoring: for each candidate in order, score = Σ over `otherDisplays` of
    area(reach ∩ display) (empty intersection = 0). Return the **first
    candidate whose score is exactly 0**. If all five are non-zero, return the
    candidate with the **smallest** score; ties keep the earlier candidate
    (strict less-than). If `otherDisplays` is empty, candidate 0 is returned.
    The returned frame is the **spot**, never the reach.

### Fallback origin (displayFrame absent)
13. If the engine has no display frame, the park frame is
    `(origin.x, origin.y, W, H)` where `origin` comes from the window-system
    seam (`parkingOrigin`); no candidates or scoring. The macOS adapter's
    origin is: among all screens (converted to y-down), the one with the
    largest maxX (first wins ties); origin = (its maxX − 1, its maxY − 1),
    i.e. the bottom-right corner of the right-most screen, overlapping it by
    1 pt. With no screens at all it returns (10000, 10000).

### Recognising a park frame — `isParkFrame(frame)`
14. True if `frame` **exactly equals** (all four components, exact floating
    equality) `parkFrame(frame.size, minimum: none)`; otherwise true if it
    exactly equals `parkFrame(frame.size, minimum: frame.size)` (i.e. the park
    frame for that size with no shrink below its own size). Otherwise false.
15. It is purely geometric against the *current* display set; it does not
    consult remembered state. A frame parked before the displays changed will
    generally no longer be recognised (Inferred consequence).

### Accepting a parked window as "at rest" — `restsParked(actual, at: target)`
16. False if `|actual.w − target.w| ≥ 1` or `|actual.h − target.h| ≥ 1`.
17. Otherwise true if the window system reports the actual frame's
    **visible fraction < 0.05** (less than 5 % of its area on any screen).
18. Otherwise true only if `|actual.x − target.x| < 1` **and**
    `target.y − 61 ≤ actual.y ≤ target.y + 1` (the OS may have pulled it up
    by up to ~60 pt, mirroring the reach); else false.
19. Visible fraction (macOS adapter): Σ over all screens (full frames, y-down)
    of area(rect ∩ screen) ÷ (rect.w · rect.h); 0 if the rect has zero width
    or height. Overlapping screens would be double-counted (Inferred; screens
    don't overlap in practice).
20. Use (Observed): after a frame write whose *target* was a park frame
    (`isParkFrame(target)`), if the actual frame's visible fraction is
    **> 0.05** and `restsParked(actual, target)` is false, the engine treats
    the park as refused/unsettled and reacts (retry/log path). At exactly 0.05
    it is accepted.
21. At startup / re-adoption, a known window whose reported frame differs from
    its remembered frame by > 2 pt in any component counts as "moved by the
    user", **unless** the window is remembered as parked (`ManagedWindow.parked`)
    and the reported frame is a park frame. So parked detection in that path
    combines remembered state with rule 14.

### Context: who gets parked (Engine, not Core)
22. Each layout pass the engine computes a real target frame for every managed
    window; windows that are not to be shown in the current view get a park
    frame instead of their tile frame (Inferred from `realTargets`, which calls
    the park computation from five places).
23. Plain out-of-view window: park size from its current frame size, minimum
    = its `WindowLimits.minimum` (if known).
24. Out-of-view with park-small on: rule 9 size, no minimum.
25. Some paths first adjust the size before parking: windows laid out below a
    floor (200×120, or 480×320 for some windows — `Feel.usableFloor` /
    `fixedMinimum`, combined with a learned minimum) are scaled **up**
    (aspect kept) to meet it; preview-sized windows use the engine's preview
    size. Exact selection conditions are Unknown (flags in the placement
    record not named).
26. Overview mode also parks some windows (`overviewFrames` calls the same
    computation). Details Unknown; out of scope.

## Data model
- `Rect { x, y, w, h }` (double, y-down, global points); `Size { w, h }`.
- Park inputs: `visibleArea: Rect`, `displayFrame: Rect?`,
  `otherDisplays: [Rect]`, `fallbackOrigin: Point` (from the window-system
  seam, only used when `displayFrame` is absent).
- `ParkCandidate { spot: Rect, reach: Rect }` — five per request.
- Constants: reach extension 60; overlap sliver 1; rest tolerance 1 (x, w, h)
  and −61/+1 (y); visible-fraction threshold 0.05; small fraction 1/3;
  fallback origin (10000, 10000) when there are no screens.
- Per-window state used around parking (Engine): `parked: Bool`,
  membership in a "parked small" set, `limits.minimum: Size?`.

## Worked examples
Common: `D = (0, 0, 1440, 900)`, `visibleArea = (0, 25, 1440, 875)`.

1. **Single display, fits.** others = []; size 1000×700, no minimum →
   s = min(1.44, 1.25) ≥ 1, size unchanged → candidate 0 →
   **(1439, 899, 1000, 700)**.
2. **Too big.** size 2000×1000, no min → s = min(0.72, 0.875) = 0.72 →
   1440×720 → **(1439, 899, 1440, 720)**.
   Same with minimum 1500×800 → (max(1440,1500), max(720,800)) →
   **(1439, 899, 1500, 800)**. Minimum 1499.2×799.5 → ceil → 1500×800 (same).
3. **Park small.** size 1000×700, small on: limit 480×291.667 →
   s = min(0.48, 0.41667) = 0.41667 → (416.67, 291.67) → **417×292** →
   frame (1439, 899, 417, 292) (others = []).
4. **Display to the right.** others = [(1440, 0, 1920, 1080)]; size 800×600.
   c0 reach (1439, 840, 800, 660) ∩ other = 799 × 240 = 191 760 ≠ 0.
   c1 spot (−799, 899, 800, 600), reach (−799, 840, 800, 660) ∩ other = ∅ → 0
   → **(−799, 899, 800, 600)**.
5. **Surrounded (all candidates visible).** D = (0,0,1000,800);
   others = R (1000,0,1000,800), L (−1000,0,1000,800), B (0,800,1000,800);
   size 500×400.
   c0 reach (999,740,500,460): R 499×60 = 29 940, B 1×400 = 400 → 30 340.
   c1 reach (−499,740,500,460): L 29 940, B 400 → 30 340.
   c2 reach (999,0,500,460): R 499×460 = 229 540.
   c3 reach (−499,0,500,460): L 229 540.
   c4 reach (0,740,500,460): B 500×400 = 200 000.
   Minimum 30 340 tied c0/c1 → earlier wins → **(999, 799, 500, 400)**.
6. **Display below.** others = [(0, 900, 1440, 900)]; size 800×600.
   c0 reach (1439,840,800,660) ∩ other = 1 × 600 = 600; c1 reach
   (−799,840,800,660) ∩ other = 1 × 600 = 600; c2 reach (1439,0,800,660):
   y 0..660 vs 900.. → 0 → **(1439, 0, 800, 600)**.
7. **No display frame.** displayFrame absent, seam origin (2559, 1439), size
   800×600 → **(2559, 1439, 800, 600)** (no scoring).
8. **isParkFrame** (D, visible as above, others = []):
   (1439, 899, 1000, 700) → true (rule 14 first test).
   (1439, 899, 2000, 1000) → first test gives (1439,899,1440,720) ≠; second
   test with minimum 2000×1000 gives (1439,899,2000,1000) = → true.
   (1438, 899, 1000, 700) → false. (1439, 899, 1000.5, 700) → first test
   (1439,899,1000.5,700)? s ≥ 1 so unchanged → true (sizes need not be
   integers when not scaled); (1439, 899.5, 1000, 700) → false.
9. **restsParked**, target (1439, 899, 1000, 700):
   actual (1439, 899, 990, 700) → false (width differs by 10).
   actual (1300, 600, 1000, 700), visible 140×300 / 700 000 = 0.06 → not
   < 0.05; dx = 139 → false.
   actual (1439.5, 860, 1000, 700), visible 0.5×40 / 700 000 ≈ 0.00003 →
   true.
   Small window, target (1439, 899, 10, 100): actual (1439, 840, 10, 100),
   visible 1×60 / 1000 = 0.06 → position test: dx 0, 840 ≥ 838 → true;
   actual (1439, 830, 10, 100), visible 0.07 → 830 < 838 → false.

## Evidence
- Rules 1–8, 10–12: `Engine.parkFrame(_:minimum:)` 0x100029b3c —
  `evidence/asm-park-geometry.json` (full disassembly), `dec-parkFrame.json`;
  engine field offsets (visibleArea 0x20, displayFrame 0x40 as `Rect?` with
  tag byte 0x60, otherDisplays 0x78) in `evidence/park-geometry-offsets.json`;
  constants ±60.0 (0x404e…/0xc04e…), ±1.0, rounding `frinta` (sizes),
  `frintp` (minimum); scoring loop via `SpacesModel.Rect.intersection`.
- Rule 4 (y-down): `Screens.parkingOrigin()` 0x1001a6c74 converts each
  `NSScreen.frame` to `y = primaryHeight − maxY` —
  `evidence/asm-parkingOrigin.json`; `Screens.topLeft` symbols.
- Rule 9: `Engine.realTargets()` 0x100036fd0, branch on
  `policy.parkOutOfViewSmall` (Policy+0x79 → Engine+0xf9) at 0x100037358,
  divisor 3.0 at 0x10003712c used at 0x100037370/0x10003737c, then
  `parkFrame(…, minimum: nil)` at 0x1000374f8 —
  `evidence/asm-realTargets.json`.
- Rule 13: `parkFrame` tag check at 0x100029c48 then call through
  `WindowSystem` witness slot 0x28 = `parkingOrigin` (witness table dump in
  `park-geometry-offsets.json`); `Screens.parkingOrigin()` max-maxX selection,
  −1 offsets, 10000.0 fallback (0x40c3880000000000) — `asm-parkingOrigin.json`.
- Rules 14–15: `Engine.isParkFrame(_:)` 0x10002a108 —
  `asm-park-geometry.json`, `dec-isParkFrame.json`.
- Rules 16–18: `Engine.restsParked(_:at:)` 0x10002a014 (witness slot 0x30 =
  `visibleFraction`, threshold 0.05 = 0x3fa999999999999a, tolerances 1.0 and
  −60−1) — `asm-park-geometry.json`, `dec-restsParked.json` (note: that
  decompile shows the 0.05 constant incorrectly; trust the disassembly).
- Rule 19: `Screens.visibleFraction(of:displays:)` 0x1001a5d10 —
  `evidence/dec-park-callers-batch.json`.
- Rule 20: closure in `Engine.checkPlacement()` 0x1000574d4, calls at
  0x100057694 (`isParkFrame`), 0x1000576d0 (`visibleFraction`, `b.le 0.05`),
  0x100057720 (`restsParked`) — from full objdump (no separate file;
  summarised in `park-geometry-offsets.json` constants).
- Rule 21: closure #3 in `Engine.start(with:)` 0x1000389e0 (2.0 tolerance,
  `ManagedWindow.parked` at +0xd0, `isParkFrame`) —
  `evidence/asm-start-isParkFrame-closure.json`.
- Rules 22–26: `realTargets` call sites 0x1000374f8, 0x1000377d0,
  0x10003783c, 0x1000381e8, 0x100038458 (constants 200/120/480/320,
  `previewSize`, `limits.minimum` at ManagedWindow+0x80); `overviewFrames`
  0x10003e3c8 — `asm-realTargets.json`.
- Study §3.0 (structure, names), `findings/solution-skeleton.md` (seam).

## Confidence
| Rule | Label |
|---|---|
| 1–3 inputs and their roles | Observed (field names + usage); meaning of `otherDisplays` = other displays' **full frames** Inferred from name |
| 4 y-down | Observed (adapter conversion); that engine rects use the same space Inferred (consistent with all arithmetic) |
| 5, 7, 8 size fit / minimum / rounding | Observed |
| 6 degenerate axis | Observed (behaviour), simplification to "skip axis" Inferred |
| 9 park-small = 1/3 of visible area | Observed |
| 10 candidate coordinates and order | Observed |
| 11 reach rationale | Inferred (values Observed) |
| 12 scoring, early exit, tie-break, empty list | Observed |
| 13 fallback origin | Observed (engine use + macOS adapter) |
| 14 isParkFrame | Observed |
| 15 display-change consequence | Inferred |
| 16–18 restsParked | Observed |
| 19 visible fraction | Observed (sum/area); overlap double-count Inferred |
| 20 checkPlacement use | Observed (condition); what happens next Unknown |
| 21 start/adoption predicate | Observed (meaning "moved by user" Inferred) |
| 22 who gets parked | Inferred |
| 23–24 sizes per path | Observed (arguments); triggering conditions Inferred |
| 25–26 | Unknown details |

## Unknowns
- **Exact "who is parked when" conditions.** `realTargets` could not be
  decompiled (provider returned empty pseudocode); the branches depend on two
  unnamed flags in the placement record (+0x31, +0x32). Needed by the Engine
  spec, not Core.
- **What `otherDisplays` holds** (full frames vs visible areas, and whether
  the coordinator includes displays on other Spaces/mirrored displays). The
  setter is reached only through the class vtable; the writer was not traced.
- **When `displayFrame` is nil** in practice (startup? headless tests?) —
  only the fallback behaviour is known.
- `Feel.parkSettle` (60) is not referenced by the park functions; its role
  (likely a settle delay before re-checking parked windows) is Unknown.
- What the engine does after a refused park (rule 20) — retry count, give-up
  flag (`coverGaveUp`?, `insistCount`?) — belongs to robustness/frame-write
  features.
- `Engine.parkedSmall` usage beyond being a key set (e.g. whether un-parking
  restores the pre-park size from it).

## Windows notes
- Coordinates: CanvasWM Core is y-down DIP with origin at the virtual-desktop
  top-left; TrellisWM's space is y-down with origin at the primary display's
  top-left. The algorithm only uses relative positions (no position rounding),
  so it ports unchanged; feed monitor rects (`MONITORINFO.rcMonitor` for
  `displayFrame`/`otherDisplays`, `rcWork` for `visibleArea`) in the same DIP
  space.
- The 1-pt overlap exists because macOS resists fully off-screen windows and
  pushes them back (hence `reach`/the −61 tolerance). Win32 lets windows sit
  fully off-screen and does not pull them back, so the reach/tolerance are
  harmless but the sliver is optional. If kept, note that on Windows 10/11 the
  outer ~7 px of a window is an invisible DWM resize border: a 1-px overlap of
  `GetWindowRect` may be entirely invisible yet still hit-testable for resize
  at the screen edge. Prefer doing geometry on `DWMWA_EXTENDED_FRAME_BOUNDS`
  rects so the "1 pt" refers to visible pixels, or choose a sliver of 0.
- Mixed-DPI: a window parked across the edge of one monitor stays associated
  with that monitor (most of it is off all monitors); candidates that touch
  another monitor are exactly what the scoring avoids, which also avoids
  `WM_DPICHANGED` resizing. Re-run parking when the monitor set changes
  (`WM_DISPLAYCHANGE`), since `isParkFrame` is relative to the current set.
- Visible fraction on Windows: compute the same Σ intersection / area over
  `EnumDisplayMonitors` rects; it is a pure function and can live in Core.
- Maximised/minimised windows must be restored before parking (cannot be
  moved); app minimums come from `WM_GETMINMAXINFO`-learned limits.
- Crash-safe restore of parked windows is feature #14 (out of scope here) but
  essential on Windows: an off-screen window left behind after a crash is
  unreachable without tooling.
- Recommended split: Core = `ParkSize`, `ParkCandidates`, `ParkFrame`
  (with fallback origin as an input), `IsParkFrame`, `RestsParked`,
  `VisibleFraction`. Engine = choosing which windows to park and with which
  size path (rules 22–26) and the refusal handling (rule 20).
