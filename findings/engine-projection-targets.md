# Feature: engine-projection-targets

Sub-feature #25, carved out of `findings/engine-tiling-loop.md` (#13), part A
and B (rules 1–11 there). Pure: belongs in `CanvasWm.Core`. This file
supersedes those rules where they differ (tab strip, the "does not fit"
branch, floating on-stage, collapsed groups).

## Summary
The layout tree lives in a unit square. One projection turns each tile into a
screen rectangle: the framed node (or the whole layout) is mapped onto the
**stage** (the work area shrunk by half a gap on every side), after first
stretching the framed node's short side so it is square in unit space; each
tile is then shrunk by half a gap on every side. Every panel produces one
**placement** for its visible (selected) window: its frame, whether that frame
is on the stage (at least 99.5 % of its area inside), and whether it is at
least the window's minimum size. The target for a window is then: its tile
frame (clamped to a known maximum and centred, then edge-rounded) when on
stage and big enough; otherwise an off-screen **park** rectangle — with a
"needs picture" flag when the tile is on stage but too small.

## Behaviour

### A. Inputs (camera-ready)
1. **Projection inputs** are: work area (DIP rect of one display), gap `g`
   (policy, default 10), a **camera rect** in unit space, and a **stretch**
   (two lists of breakpoints, one per axis). At rest the camera and stretch
   are derived from the framed node (rules 3–4). A future camera/motion
   feature (#15) replaces exactly these two inputs (it supplies an animated
   camera rect and its own "visual" stretch); nothing else changes.
2. **Stage** = work area inset by `g/2` on each side:
   `(A.x + g/2, A.y + g/2, max(0, A.w − g), max(0, A.h − g))`. The stage is
   always the whole work area of the display, never the framed node's rect.

### B. Stretch and camera at rest
3. **Framed rect F** = unit rect of the navigator's framed node (from the
   weights-only layout, layout-solver rule 30; virtual range entries are valid
   framing targets). Nothing framed → F absent.
4. **Stretch from framing.** Identity when F is absent, has `w ≤ 0` or
   `h ≤ 0`, or `|F.w − F.h| ≤ 1e-9`. Otherwise the **shorter side of F is
   stretched to the length of its longer side**, along that side's axis only:
   - `F.w < F.h` → x-axis breakpoints `[(F.x, F.x), (F.x+F.w, F.x+F.h)]`;
   - `F.w > F.h` → y-axis breakpoints `[(F.y, F.y), (F.y+F.h, F.y+F.w)]`.
   The map is piecewise linear between breakpoints and a constant shift
   outside them: values at or before the first breakpoint are unchanged;
   values at or after the last move by `(longer − shorter)`.
   Purpose (Inferred): the framed node becomes a unit-space square, so the
   camera always covers a square of unit space; tiles beside the framed node
   keep their unit size and therefore land just outside the stage instead of
   being squashed into it.
5. **Camera C** = F mapped through the stretch (a square of side
   `max(F.w, F.h)` anchored at `(F.x, F.y)`); unit square `(0,0,1,1)` when F
   is absent.

### C. Tile → screen frame
6. For a unit rect R, with stretch map S and camera C:
   `x = stage.x + stage.w·(S(R.x) − C.x)/C.w`,
   `y = stage.y + stage.h·(S(R.y) − C.y)/C.h`,
   `w = stage.w·(S(R.x+R.w) − S(R.x))/C.w`,
   `h = stage.h·(S(R.y+R.h) − S(R.y))/C.h`.
   Each axis is scaled independently, so the camera (a unit square) fills
   the non-square stage exactly. This "un-inset projected rect" is also what
   the collapse test (rule 19) and moving tab strips use.
7. **Docked tile frame** = the projected rect inset by `g/2` on each side:
   `x += g/2, y += g/2, w = max(0, w − g), h = max(0, h − g)`. Result: the
   framed node's outer edge sits exactly one gap inside the work area and
   neighbouring tiles are exactly one gap apart.
8. The unit rect used is the **panel's** entry rect (looked up by panel id).
   An optional per-window **rect override** input (unit rect keyed by window
   key) replaces it when present; the real-target computation passes none.
9. **Floating panels** use the same mapping with **no gap inset**. If the
   window has a known natural size (aspect `a = natural.w/natural.h`) the
   frame is fitted to that aspect: if `a < w/h` the width becomes `a·h` and
   the frame is centred horizontally; otherwise the height becomes `w/a`
   keeping the top edge. Then, unless the panel is collapsed, a frame
   smaller than the minimum is grown (rule 14 "grow to minimum") and kept
   inside the floating area. (Detail belongs to the floating feature.)

### D. Placements (one per panel)
10. Placements are produced for every panel in the tree (in tree order) and
    for every floating panel. **Not** produced for: the desktop node (empty or
    not — only a panel inside it), hidden panels, the hidden-desktop record,
    and windows that are in no panel. Those windows get **no target** and are
    not touched by this feature.
11. **One placement per panel, keyed by its `selected` window.** In a panel
    with several views (tabbed or native group) only the selected window has
    a placement; the other views get no target here (tabs feature handles
    them; Inferred).
12. **Tab strip (panels that draw their own strip: tabbed and > 1 view).**
    The window frame is **not** reduced — the selected window gets the full
    tile frame (rule 7). A separate **strip rect** is attached to the
    placement for the renderer:
    - full strip height `H_full = 2·tabPadding + 20` (tabPadding default 4 →
      28);
    - strip height `s = min(0.15 × frame.h, H_full)`;
    - side inset `i = clamp((frame.w − 120)/2, 0, tabInset)` (0 when
      tabInset ≤ 0);
    - strip rect = `(frame.x + i, frame.y, frame.w − 2i, show·s)` where
      `show` ∈ [0,1] is a per-window reveal fraction (1 when not animating).
    So the strip is drawn over the top band of the window's own frame
    (where the title bar is). Minimum height for layout (layout-solver rule
    33) does add `H_full`, but the fits test (rule 15) does not.
13. **onStage** (per placement):
    - panel belongs to a collapsed group (rule 19) → false;
    - otherwise `area(frame ∩ stage) / area(frame) ≥ 0.995`, using the
      un-rounded, un-clamped tile frame from rule 7/9 and the stage from
      rule 2; zero-area frame or no intersection → fraction 0;
    - if that fails and the panel is **floating** and the desktop slot is
      usable (engine-tiling-loop rule 27) → `fraction > 0.05`.
14. **Grow to minimum** (`atLeast`, floats only): if the frame is smaller
    than the minimum by more than 0.5 on an axis, it is scaled uniformly by
    `k = max(min.w/w, min.h/h)`, but `k` is capped so the result is no larger
    than the stage (`k ≤ stage.w/w`, `k ≤ stage.h/h`); if `k > 1` the new
    frame keeps the old horizontal centre and the old top edge, then is
    shifted to lie inside the stage.
15. **fits** = `frame.w ≥ m.w − 0.5` and `frame.h ≥ m.h − 0.5`, where the
    minimum `m` (per axis) = `max(floor, ownMinimum)`:
    - floor = **TileMinimumFloor 200 × 120**, or **FixedTileMinimum
      480 × 320** when `policy.fixedMinimum` is on;
    - ownMinimum = the window's current per-window minimum (`WindowLimits.
      minimum`, seeded from the LimitStore and raised by refusals); absent →
      0 × 0.
    No gap, no tab-strip height and no `squeezeBelowMinimum` enter this test.
    (Floating windows with a known natural size have a separate 0.98 ×
    natural branch; floating feature.)
16. Placement record = key, frame (rule 7/9), floating flag, onStage, fits,
    optional strip rect and the strip's full height.

### E. Targets (per placement whose key is a managed window)
17. **onStage and fits → tile target.** If the window has a known maximum
    size, width and height are each clamped to it and the smaller frame is
    centred on **both** axes inside the tile. Then edges are rounded
    (rule 18). This is the only branch that rounds.
18. **Edge rounding** (`pixelRounded`): round the left and top edges and the
    right and bottom edges independently to the nearest whole unit, halves
    away from zero; `w = max(0, round(x+w) − round(x))`, same for h. Units
    are DIPs (macOS points). Adjacent tiles keep an exact gap after rounding.
19. **Not onStage → park.** Park rect = park-geometry `parkFrame(size, no
    minimum)` with size = the window's last known frame size; or, with
    `policy.parkOutOfViewSmall`, that size shrunk (never grown) so it fits
    in one third of the visible area on each axis, rounded. **Human
    decision: no minimum is passed for any park size in CanvasWM.**
20. **onStage but not fits → park with picture ("cover").** The window is
    parked and the placement is flagged *needs picture*; drawing the picture
    is the cover feature (#15/#16) — here it is only a flag. Park size:
    - **preview size** = the tile frame scaled up uniformly by
      `k = max(1, m.w/w, m.h/h)` (m from rule 15), each side rounded;
      degenerate tile (w ≤ 1 or h ≤ 1) → the window's last known frame size,
      or 800 × 600 if unknown;
    - original detail (Observed, see Unknowns): when the window has **no**
      own minimum and the LimitStore has one for its app, the clamped,
      rounded tile is used as the park size instead of the preview size if
      it is ≥ 200 wide, ≥ 120 high and smaller than the stored minimum by
      more than 2 on an axis. CanvasWM may use the preview size always.
    - TrellisWM passes the own minimum to `parkFrame` here; per the human
      decision CanvasWM passes none.
21. **Collapsed groups** (`policy.collapseSmallGroups`, default true; only
    while a node is framed): a group of panels whose un-inset projected rect
    is narrower or shorter than **120** is collapsed; its panels are never
    on stage (rule 13), so their windows are parked with pictures. Grouping
    rules are a separate feature; this feature needs only a "collapsed
    panel ids" input set.
22. Floating targets go through rule 17 too (on-stage floats keep their
    fitted frame, rounded).

## Data model
- `ProjectionInput { WorkArea: Rect, Gap: double, Camera: Rect (unit),
  StretchX: (from,to)[], StretchY: (from,to)[] }` + helper
  `AtRest(workArea, gap, framedUnitRect?)` building camera and stretch.
- `Placement { Key, Frame: Rect, Floating: bool, OnStage: bool, Fits: bool,
  Strip: Rect?, StripFullHeight: double }`.
- `TargetKind { Tile, Park, ParkWithPicture }`; `Target { Key, Rect, Kind }`.
- Per-window inputs: last known frame size, `WindowLimits { Minimum?,
  Maximum?, Natural? }`, LimitStore minimum for its app (optional), strip
  show fraction (default 1).
- Policy inputs: gap, fixedMinimum, tabPadding, tabInset, parkOutOfViewSmall,
  collapseSmallGroups (via the collapsed-ids set).
- Constants: inset g/2; stretch tolerance 1e-9; onStage 0.995; floating
  on-stage 0.05; fits slack 0.5; floors 200×120 / 480×320; strip
  `2·tabPadding+20`, 0.15 of tile height, side-inset threshold 120;
  collapse 120; atLeast slack 0.5; preview fallback 800×600; park-small 1/3;
  store-minimum slack 2.

## Worked examples
Common: work area A = (0, 25, 1440, 875), gap 10 → stage (5, 30, 1430, 865).
No maximum, no own minimum, fixedMinimum off unless stated. "raw" = before
rounding.

Projection
1. Tree `X(s)[a .5, b .5]`, nothing framed: camera (0,0,1,1), identity.
   a → (10, 35, 705, 855), b → (725, 35, 705, 855). Both on stage, fit;
   targets identical after rounding.
2. Same tree, framed a = (0,0,.5,1): w < h → x breakpoints [(0,0),(.5,1)];
   camera (0,0,1,1). a: S(0)=0, S(.5)=1 → projected (5, 30, 1430, 865) →
   frame (10, 35, 1420, 855), on stage. b: S(.5)=1, S(1)=1.5 → projected
   (1435, 30, 715, 865) → frame (1440, 35, 705, 855) → no overlap with the
   stage → park (park-geometry, size = b's last frame).
3. Tree `X(s1)[a .5, Y(s2)[b .5, c .5] .5]`, framed s2 = (.5,0,.5,1):
   breakpoints [(.5,.5),(1,1.5)], camera (.5,0,1,1).
   b (.5,0,.5,.5) → raw (10, 35, 1420, 422.5) → target (10, 35, 1420, 423);
   c (.5,.5,.5,.5) → raw (10, 467.5, 1420, 422.5) → target (10, 468, 1420,
   422) (gap between them stays 10); a (0,0,.5,1) → projected x = 5 +
   1430·(0 − .5) = −710 → frame (−705, 35, 705, 855) → park.
4. Same tree, framed b = (.5,0,.5,.5) (square → identity), camera = b.
   b → (10, 35, 1420, 855); c → frame (10, 900, 1420, 855) → park;
   a → projected (−1425, 30, 1430, 1730) → frame (−1420, 35, 1420, 1720) →
   park (no overlap with the stage).
5. Tree `Y(s)[t .5, u .5]`, framed t = (0,0,1,.5): w > h → y breakpoints
   [(0,0),(.5,1)]; t → (10, 35, 1420, 855); u → y = 30 + 865 = 895 → park.
6. Rounding with odd width: A = (0, 25, 1441, 875), tree of example 1:
   a raw (10, 35, 705.5, 855) → (10, 35, 706, 855); b raw (725.5, 35, 705.5,
   855) → (726, 35, 705, 855).
7. Camera override (motion input, no stretch), camera (.1,0,1,1), tree of
   example 1: a → frame (−133, 35, 705, 855): inside part x ∈ [5, 572] →
   567/705 = 0.804 → not on stage → park. b → (582, 35, 705, 855) → fully
   inside → tile.

On-stage threshold
8. Frame (733, 35, 705, 855) — right edge 1438, stage right 1435: inside
   702/705 = 0.99574 ≥ 0.995 → on stage. Frame (734, …) → 701/705 = 0.99433
   → not on stage.
9. Floating panel frame overlapping the stage by 10 % with the desktop slot
   usable → on stage (> 0.05); same with desktop not usable → not on stage.

Fits and pictures
10. Tile 190 × 400, floor 200×120: 190 < 199.5 → not fits → park with
    picture; preview k = max(1, 200/190, 120/400) = 1.05263 → 200 × 421.
    Tile 199.6 × 400 → fits.
11. fixedMinimum on, tile (725, 468, 348, 206): 348 < 479.5 → not fits;
    k = max(1, 480/348, 320/206) = 1.55340 → 541 × 320 park with picture.
12. Own minimum 400 × 300, tile 348 × 206: m = (400, 300); not fits;
    k = max(1, 1.14943, 1.45631) → 507 × 300.
13. Tile (10, 35, 705, 855) fits; maximum 600 × 700 → (62.5, 112.5, 600,
    700) → rounded (63, 113, 600, 700).

Tab strip (tabPadding 4, tabInset 8)
14. Tabbed panel with views [p, q], selected p, unit (0,0,.5,1) with nothing
    framed: p target (10, 35, 705, 855) (unchanged by the strip); strip
    s = min(0.15·855, 28) = 28, i = min((705−120)/2, 8) = 8 → strip
    (18, 35, 689, 28). q: no target.
15. Same panel in a tile of height 100: s = min(15, 28) = 15.

Nothing to place
16. Fresh document (root = empty desktop), window k not in any panel: no
    placements, no targets; k is left where it is.

## Evidence
Directory `~/Documents/trelliswm-re/evidence/`; summary in
`engine-projection-targets-notes.txt` (new).
- Rules 1–2, 5: `Engine.destinationScene` 0x10002d270
  (`dec-tiling-destinationScene.json`); camera/stretch swap during motion
  (`visualStretch`) noted there.
- Rules 3–4: `Engine.cameraRect(for:overview:)` 0x10002c9ec
  (`asm-tiling-cameraRect.txt`); `Stretch.init(framing:)` 0x100093d54,
  `Stretch.map` 0x100093e80 (`asm-tiling-stretch.txt`); `Projection.init`
  0x100092568 (`dec-tiling-projInit.json`).
- Rules 6–7, 9: `Scene.frame(of:rect:floating:)` 0x1000e1738
  (`asm-tiling-sceneFrame.txt`; stage +0xb0, camera +0xe0, gap from policy
  copy +0x100); `Scene.fit(aspect:in:)` 0x1000e1980.
- Rules 8, 10–16: `Scene.placements(rects:stripTiles:)` 0x1000df120
  (`asm-projection-placements.txt`, `dec-tiling-placements.json`): panelsOf
  + floating list, selected-key reads, `hasStrip` and 2·tabPadding+20 at
  0x1000df890, 0.15 factor, `(w−120)/2` vs tabInset, onStage 0.995 at
  0x1000dfe94, floating 0.05 at 0x1000dfec4, −0.5 fits compares
  0x1000dfe40–0x1000dff40.
- Rule 14: `Scene.atLeast` 0x1000e1cb0 (`asm-projection-helpers.txt`).
- Rule 15: `Scene.minimum(_:floating:)` 0x1000e00b0
  (`dec-tiling-sceneMinimum.json`: 200/120, 480/320, limits minimum,
  0.98·natural).
- Rule 12 helpers: `Scene.stripHeight(for:full:)` 0x1000e19d8,
  `Policy.tabStripHeight` 0x1000901b0, `Policy.tabLead` 0x100091434.
- Rules 17–20: `Engine.realTargets` 0x100036fd0
  (`asm-realTargets.json`, `asm-projection-realTargets-excerpt.txt`):
  branches 0x10003726c/0x100037278, maximum clamp 0x10003767c and
  `Engine.constrained` 0x1000570a0, park 0x1000374f8 (minimum nil),
  park-small 0x100037358–0x1000374e4, not-fits branch 0x10003739c–
  0x10003783c (own-minimum test, LimitStore call, −2, 200, 120),
  `Engine.previewSize` 0x100056eac (800, 600 fallback);
  `Rect.pixelRounded` 0x1001136b8 (`asm-tiling-pixelRounded.txt`).
- Rule 21: `Scene.collapsedGroups(framed:)` 0x100026c08 and its `small`
  closure 0x100026c6c (120 on projected width/height); collapsed lookup in
  placements 0x1000dfdd8.
- Park geometry: `findings/park-geometry.md`; minimum floors names:
  `findings/tuning-constants.md` (usableFloor, fixedMinimum).

## Confidence
| Rule | Label |
|---|---|
| 1 camera-ready inputs | Observed (scene fields); "#15 replaces only these" Inferred |
| 2 stage | Observed |
| 3 framed rect source | Observed |
| 4 stretch | Observed; purpose Inferred |
| 5 camera | Observed |
| 6–7 mapping, inset | Observed |
| 8 panel rect + override | Observed |
| 9 floats | Observed (fit, atLeast call); float-area clamp Inferred |
| 10 what gets placements | Observed (panel iteration); "untouched windows" Inferred |
| 11 selected only | Observed (key reads); non-selected handling Inferred |
| 12 strip | Observed (formulas, frame unchanged); "covers title bar" Inferred; role of `show` Inferred |
| 13 onStage | Observed |
| 14 atLeast | Observed |
| 15 fits / minimum | Observed |
| 16 record | Observed (field offsets) |
| 17 max clamp both axes | Observed |
| 18 rounding | Observed |
| 19 park | Observed; no-minimum = human decision |
| 20 preview size | Observed; LimitStore sub-branch Observed constants, meaning Inferred |
| 21 collapse | Observed threshold and gate; grouping Unknown |
| 22 | Inferred |

## Unknowns
- **Collapsed-group grouping**: which small tiles form one group, and the
  rect each collapsed panel gets, were not traced (`collapsedGroups`,
  `collapsedPanels`). Only the 120 threshold and "never on stage" are known.
  Recommend a separate feature; pass an empty set until then.
- **Not-fits LimitStore sub-branch**: an engine-level set of app ids (engine
  offset 0xb30) skips the store lookup, and the "park at tile size when
  smaller than the stored minimum" choice has no clear purpose; reading is
  from register flow only. A runtime check with an app that has a stored
  minimum but no per-window minimum would confirm.
- **Strip `show` fraction and `stripTiles`**: the per-window reveal fraction
  dictionary and the `stripTiles` override are only populated by the
  renderer/animation path (not traced). At rest assume show = 1, no override.
- Non-selected tab windows: no target from this computation; whether the
  tabs feature parks or hides them was not traced here.
- Panel with no `selected` value: behaviour not traced (sanitize normally
  guarantees one).

## Windows notes
- Work area = monitor `rcWork` in DIPs; all math stays in DIPs and the
  rounding of rule 18 is to whole DIPs. Re-round in physical pixels in the
  Win32 adapter (at 125 %/150 % a whole DIP is not a whole pixel); the
  engine's ±2 echo tolerance absorbs the difference.
- Frames are visible-frame rects; the adapter must add the DWM invisible
  border when calling `SetWindowPos`.
- Minimums: seed `WindowLimits.Minimum`/`Maximum` from `WM_GETMINMAXINFO`
  (`ptMinTrackSize`, `ptMaxTrackSize`) in DIPs, so the own-minimum path of
  rule 20 is the normal case and the LimitStore sub-branch rarely matters.
- Tab strips: Windows apps own their caption; drawing CanvasWM's strip over
  the top band of the window (rule 12) would cover the caption buttons. A
  Windows spec may prefer to reserve the strip above the window instead —
  that is a deliberate deviation and should be flagged as such.
- "Park with picture" needs DWM thumbnails; until the cover feature exists
  the flag can be ignored by the writer or the window can simply be parked.
- Parking may be fully off-screen on Windows (park-geometry notes).
