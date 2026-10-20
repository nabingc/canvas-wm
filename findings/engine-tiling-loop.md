# Feature: engine-tiling-loop

## Summary
On one display, the engine keeps the layout tree in a unit square and turns it
into real window frames with one projection: the node the "camera" is framing
fills the work area (minus half a gap on each side), every tile is inset by
half a gap, and edges are rounded to whole points. Tiles that land fully on
the stage and are at least as big as the window's minimum get their tile
frame; everything else is parked off-screen. Window events edit the tree
(place next to the focused window, float onto a visible desktop slot, hide or
remove on close) and then rewrite only the windows whose target changed. The
engine checks each write's read-back frame, absorbs the echoes of its own
writes, snaps back windows that apps move on their own, and learns minimum or
maximum sizes when an app refuses a size by more than 24 points.

## Behaviour

### A. Projection: unit rects → screen rects
1. **Stage.** stage = work area inset by gap/2 on every side:
   `(area.x + g/2, area.y + g/2, max(0, area.w − g), max(0, area.h − g))`,
   g = policy gap (default 10). Same stage as `stageAspect` in
   new-window-placement rule 9.
2. **Camera.** At rest the camera is the unit rect of the navigator's
   **framed** node (layout-solver entries, weights mode, no metrics); when
   nothing is framed it is the whole unit square `(0,0,1,1)`.
3. **Stretch (square-ing the framed area).** If the framed rect is not square
   (|w − h| > 1e-9, both > 0), its shorter side is stretched to equal its
   longer side: for w < h the x axis is mapped piecewise-linearly so that
   `[x, x+w]` becomes `[x, x+h]` (points right of the framed area shift by
   `h − w`, points left are unchanged); for w > h the same on the y axis.
   Square or absent framing → identity. The camera and every tile are mapped
   through the same stretch.
4. **Tile frame.** For a tile with unit rect R and stretched camera C:
   `x = stage.x + stage.w·(S(R.x) − C.x)/C.w`, `y` likewise with heights,
   `w = stage.w·(S(R.maxX) − S(R.x))/C.w`, `h` likewise. Then the tile is
   inset by g/2 on each side: `x += g/2, y += g/2, w = max(0, w − g),
   h = max(0, h − g)`. Net effect: the framed node fills the stage exactly,
   and neighbouring tiles are separated by exactly one gap; the outer margin
   to the work-area edge is also one gap.
5. **Floating panels** use the same mapping but **no gap inset**; if the
   window has a known natural size its rect is fitted to that aspect ratio
   (width reduced and centred horizontally, or height reduced keeping the top).
   (Floating detail belongs to the floating feature.)
6. **Rounding (`pixelRounded`).** The final target rounds the **edges**, not
   the size: `x' = round(x)`, `y' = round(y)`, `w' = max(0, round(x+w) − x')`,
   `h' = max(0, round(y+h) − y')`, round-half-away-from-zero. Adjacent tiles
   therefore keep an exact gap after rounding.
7. Tab strips: a panel that draws its own strip (tabbed, ≥ 2 views) loses
   strip height from its window frame (tabs feature; not needed for
   one-window panels).

### B. Placement classification (per window, per pass)
8. **onStage** = area(tile frame ∩ stage) / area(tile frame) ≥ **0.995**.
9. **fits** = tile frame w ≥ min.w − 0.5 and h ≥ min.h − 0.5, where
   min = per axis max(floor, learned minimum of that window); floor = 200×120,
   or 480×320 when `policy.fixedMinimum`. Unknown learned minimum = 0×0.
10. **Real target** (`realTargets`):
    - onStage and fits → the tile frame; if the window has a known
      **maximum** size, width/height are clamped to it and the frame is
      centred inside the tile; then rounded (rule 6).
    - not onStage → a **park frame** (park-geometry): size = the window's
      last known frame size, no minimum passed (or the 1/3-of-visible-area
      "park small" size when `parkOutOfViewSmall`).
    - onStage but not fits → the window is also parked and its tile is drawn
      with a picture instead (cover/preview feature #15/#16; see Windows
      notes).
11. Consequence on one monitor: with nothing framed, every tile is on stage
    and written; when a sub-node is framed (after `move(toward:)`,
    `zoomInto`, a reveal), tiles outside it fall off the stage and are parked.

### C. Writing frames
12. **No batch API**: one asynchronous `SetFrame(key, target)` per window,
    issued in target-dictionary order (no ordering guarantee).
13. **Skip rules** when writing a target set:
    - keys in the caller's `except` set;
    - a parked window whose target is its park frame and which is already
      there (within the park recognisers);
    - target equal to the window's last **requested** target;
    - a window that already has a write **in flight** (writesInFlight ≥ 1);
      it is reconciled when that write completes or by the next pass.
14. **Before each write**: the target is pushed onto the window's
    "expected frames" list with the current time; writesInFlight += 1;
    requested = target. When the write shrinks the window below its last
    shown size, the capture picture is held (cover feature).
15. **Expected-frame list**: entries older than **1.5 s** are dropped; a
    reported frame is "expected" if all four components are within **2.0** of
    any remaining entry. Read-back frames are also added. `windowMoved`
    ignores expected frames (they are echoes of the engine's own writes).
16. **On completion** (callback with read-back actual or null):
    writesInFlight −= 1; null → nothing more. Otherwise the actual is
    recorded as expected and as the window's known frame, then:
    - actual within **24** (refusalSlack) of target on both axes → accepted.
      If actual ≥ target − 2 on both axes the limit store is told the app
      accepted this size (`observe`), which can lower a stored minimum
      (`lowerMinimum`, rule 18).
    - actual larger than target by > 24 on an axis, or smaller by > 24 →
      a **refusal recheck** is scheduled after **0.25 s** (refusalRecheck).
17. **Refusal recheck**: the frame is read again. If it still exceeds the
    target by > 24, the window's minimum is raised to the actual size on the
    exceeding axes and stored with `LimitStore.Learn` (per app). If it is
    still smaller by > 24, the window's maximum is lowered to the actual on
    those axes (kept per window). Then targets are recomputed; if this
    window's target changed, the display re-lands (all targets rewritten
    with a new placement generation). With a raised minimum the tile usually
    no longer "fits" and the window is parked (rule 10).
18. **lowerMinimum**: calls `LimitStore.Observe(actual size, appId)`; if the
    store's minimum for that app came down and the window has no minimum of
    its own, the display re-lands.
19. **Placement generation**: a full pass (`placeWindows`) bumps and records
    a generation, starts a counter of outstanding writes and marks
    "placement done" when all completions have arrived. Suspend/releaseAll
    bump the generation, so late completions of an abandoned pass are not
    acted on.
20. **Drift check** (`checkPlacement`, after a landing settles): each target
    is read back; only if the generation is still current, the stage is at
    rest, no interaction is running and nothing is in flight:
    - park targets: if the window shows > 5 % on screen and is not "at rest
      parked" → it is re-parked (write that one target again);
    - tile targets: drift > 4 on any component is only logged; no rewrite.
21. **Settle timing**: after a landing the engine focuses (rule 33), renders,
    and `coverFade + 0.02` s later (default 0.16 s) records laid-out sizes,
    runs the drift check, settles floats, renders and **saves**.
22. **Direct presentation (no animation)**: when the stage is at rest
    (phase desk, not in overview, no interaction, no native drag, no layout
    tween, camera at its target) an edit is shown by computing all targets
    and writing them immediately (rule 13 skips unchanged windows), then
    focusing the requested window (if it differs from the focused key) on the
    next UI turn, saving, rendering and firing `onChange`. Otherwise the edit
    goes through a camera motion that lands later (#15/#16). CanvasWM #13
    should implement only the direct path.

### D. start(with windows)
23. Order (Observed calls): schedule native-tab watcher (0.25 s); register a
    managed record per window; load the saved record; **sanitise** the tree
    against the known window keys (layout-tree rule 18+); if the tree has no
    desktop node, insert one beside the root with fraction 0.5; set the
    document; restore the framed node and saved views (keys "1"…"9");
    drop from the layout docked windows that appear to have been moved by the
    user while unmanaged (reported frame differs from remembered by > 2 on any
    component and the window was not remembered as parked, park-geometry
    rule 21); ensure a desktop node exists; then for every known window that
    is not in the layout, in a sorted order (sort key Unknown): **float it
    behind** at its current position, or **place** it with `placeNew(framed =
    framed ?? root, focused = none, stageAspect)`; join native tab siblings;
    start navigation history; snap the camera; compute targets; record
    laid-out sizes; render; save; then land (write targets) once first
    pictures exist or after a 1 s deadline.
24. CanvasWM: without capture, land immediately (rule 22). Float-behind vs
    place decision at start: Inferred = non-dockable or remembered-floating
    windows float behind, dockable ones are placed.

### E. windowAdded (adopted, not frozen)
25. End any interaction; schedule a native-tab recheck in 0.3 s
    (`nativeTabRecheck`; corrected per tuning-constants findings, was 1.2 s);
    try to join a native tab group (Windows: never).
26. Window already in the document: hidden panel → restored; otherwise the
    existing slot is kept. A remembered dock spot from the "away" table
    (`windowRemoved(temporary)`) puts it back at that spot; a pending "split
    for new window" request (`takeSplit`) docks it there.
27. Otherwise the new-window rule:
    - If the **desktop slot is usable** — its screen rect (gap-inset) is
      ≥ 99.5 % on the stage and at least **480×320** — the window is
      **floated onto the desktop** at its own current position (kept as a
      fraction of the desktop rect, clamped to sizes ≤ 0.96 and offsets
      ≥ 0.02 of it).
    - Else if it is **dockable** → `placeNew(doc, key, framed = navigator's
      framed id, else the root id, focused = engine focused key,
      stageAspect)` (new-window-placement). A docked-tile cap
      (`policy.maxDockedTiles`) is checked first — this is the licensing
      gate; **out of scope**, treat as unlimited.
    - Else → floated (default float rect).
28. The edit is committed as a layout change with land = not in overview,
    focus = the new window, include = the new window (the framing is widened
    if needed so the new window is visible — `Navigator.ensureVisible`,
    Inferred). At rest this is rule 22: all changed targets written, then the
    new window is focused. Save happens with the edit.

### F. windowRemoved
29. `closeQuietUntil = now + 1 s` (suppresses reveal reactions to the focus
    change a close causes, rule 32).
30. **temporary = true** (minimise, app hide, desktop switch): if the window
    was docked, its restore target and neighbouring leaf ids are stored in
    the "away" table; the panel is **hidden** (a tabbed panel with ≥ 2 views
    only hides that tab). The space it occupied is given up in the tree the
    same way as a removal (Inferred).
31. **temporary = false** (closed): the panel is **removed**
    (`Arrangement.remove`, which redistributes the freed weight per
    `shareRemovedSpace`, default true → siblings share it). Then the parked
    restore / capture stop / focused-key clear of engine-inbound-api rule 17.
    The remaining windows are re-targeted and only changed ones written.

### G. windowFocused
32. Records `lastFocused = now` on the window and sets the focused key;
    raises a floating window; restores a minimised tab group / splits a torn
    tab (tabs). **Reveal**: if the focus was not caused by the engine, the
    time is past both `suppressRevealUntil` and `closeQuietUntil`, the stage
    is at rest and the window's placement is not "on stage and fits", the
    engine sets `suppressRevealUntil = now + 1` and **zooms the camera to
    that window** (`zoomInto`) so it becomes visible (camera feature; for
    #13 equivalent to framing that window's tile and re-landing).
33. **Focus after landing** (Inferred): the requested landing focus if it is
    resting on stage, else the current focus if on stage, else the on-stage
    window with the latest `lastFocused`; issued via `IWindowSystem.Focus`.
    It then sets `suppressRevealUntil = now + 0.6 s` (`placementWait`), so the
    focus change it caused does not trigger a reveal. The 1 s value in rule 32
    applies only to a plain focus change. (Corrected per tuning-constants
    findings.)

### H. windowMoved (OS reports a new frame)
34. The frame is stored as the window's known frame; resized = width or
    height changed by > 0.5.
35. Ignored for layout purposes when: a write is in flight for that window,
    the mouse button is down (a native drag is handled by the drag-to-dock
    feature), or the frame is expected (rule 15).
36. Parked window that moved on its own → parked flag cleared and all
    targets rewritten (it is re-parked or shown).
37. Floating window → its float rect is updated to where it now is
    (`settleFloat`).
38. Tiled window moved/resized by the **app** (mouse up, not expected) →
    `appResized`:
    - grew to ≥ 0.92 × work-area width or ≥ 0.90 × work-area height and
      beyond its tile by more than about 100 (w) / 60 (h) → treated as a
      **zoom request**: frame that window alone (toggleMaximize);
    - shrank back while framed alone → un-zoom request;
    - otherwise → **snap back**: its tile target is written again. If the
      app keeps resisting (3 times within 3 s, insistCount/insistWindow), the
      engine stops fighting and learns the app's size ("insists; learned it").
39. Leaving the display (dragged to another monitor) → hand-off
    (multi-display feature).

### I. arrangeAll and move(toward:)
40. `arrangeAll`: order = window-system front-to-back list filtered as in
    new-window-placement rule 21, then the rest; rebuild with
    `ArrangeDwindle(landscape = stage.w ≥ stage.h)`; commit as a layout
    change; then **frame nothing** (whole layout), record history. Saved.
41. `move(toward: edge)`: current node = framed id, else root id;
    `Neighbour(entries, current, edge)`; if found → `frame(neighbour)`:
    the navigator frames that node (history recorded), the camera retargets
    to it and the display lands, so the neighbour **fills the stage** and the
    tiles outside it are parked; focus follows by rule 33. Not found → no-op.
    With nothing framed the root contains everything, so the move is a no-op
    (Inferred, directional-focus rule 19). The tree is never edited.

### J. Saving
42. Save after: start, every committed edit (window added/removed, arrange
    all, float all, toggle docked, desktop hide/show, saved views), every
    landing settle (rule 21), presentDirectly (rule 22), suspend, app stop
    (engine-interfaces rule 37).

## Data model
- Projection inputs: work area, gap, framed node rect (unit), stretch
  breakpoints per axis (list of (from, to) pairs; empty = identity).
- `Placement` per window: key, frame (screen), floating, onStage, fits,
  strip rect?, strip full-height?.
- Per managed window (in addition to engine-inbound-api): known frame,
  limits {minimum?, maximum?, natural?}, lastFocused time, parked flag,
  requested target?, writesInFlight, pending refusal rechecks, expected
  frames list [(rect, time)], float size?, shown size?, picture-held flag,
  dock spot?, laid-out size.
- Engine: placement generation, placed generation, placement started/done
  times, suppressRevealUntil, closeQuietUntil, focusedKey, landing focus.
- Constants: stage inset g/2 (g default 10); onStage 0.995; fits slack 0.5;
  floors 200×120 / 480×320; desktop usable 480×320 and 0.995; float clamp
  0.96 / 0.02; expected window 1.5 s, tolerance 2.0; refusal slack 24,
  recheck 0.25 s; accept-for-observe slack 2; drift log 4; park re-check
  visible fraction 0.05; settle delay coverFade + 0.02; reveal suppress 1 s
  after a plain focus change, 0.6 s after a landing focus (placementWait);
  close quiet 1 s; native-tab recheck 0.3 s (nativeTabRecheck; corrected per
  tuning-constants findings, was 1.2 s); zoom request 0.92 / 0.90 and
  100 / 60; insist 3 within 3 s.

## Worked examples
Common: work area A = (0, 25, 1440, 875), display (0, 0, 1440, 900), no
other displays, gap 10 → stage (5, 30, 1430, 865), stageAspect = 1430/865 ≈
1.6532. Nothing framed unless stated. Ids n1, n2, … from the generator.
Unit rect (ux, uy, uw, uh) → before rounding:
`(10 + 1430·ux, 35 + 865·uy, 1430·uw − 10, 865·uh − 10)`.

Projection
1. Unit (0,0,1,1) → (10, 35, 1420, 855).
2. Unit (0,0,.5,1) → (10, 35, 705, 855); unit (.5,0,.5,1) → (725, 35, 705, 855).
3. Rounding: unit (.5,0,.5,.5) → raw (725, 35, 705, 422.5) → bottom 457.5 →
   458 → **(725, 35, 705, 423)**; unit (.5,.5,.25,.5) → raw (725, 467.5,
   347.5, 422.5) → **(725, 468, 348, 422)**; unit (.75,.5,.25,.5) → raw
   (1082.5, 467.5, 347.5, 422.5) → **(1083, 468, 347, 422)**. Gaps between
   them stay exactly 10.
4. Odd width: A = (0, 25, 1441, 875): unit (0,0,.5,1) → raw (10, 35, 705.5,
   855) → **(10, 35, 706, 855)**; unit (.5,0,.5,1) → raw (725.5, 35, 705.5,
   855) → **(726, 35, 705, 855)**.
5. Gap 0: unit (0,0,.5,1) → (0, 25, 720, 875).
6. Framed sub-node with stretch: tree `X(s)[a .5, b .5]`, framed = a
   (0,0,.5,1) → stretch x ×2 on [0,.5], camera (0,0,1,1): a → (10, 35, 1420,
   855). b maps to x ≥ 1440 → off stage → parked: b's last size 705×855 →
   park frame (1439, 899, 705, 855) (park-geometry candidate 0).

Classification
7. Tile frame 190×400 on stage, no learned minimum, fixedMinimum off →
   fits = 190 ≥ 199.5? no → parked (cover). 199.6×400 → fits.
8. Tile (10, 35, 705, 855) with window maximum 600×700 → target centred:
   (62.5, 112.5, 600, 700) → rounded (63, 113, 600, 700) (edges 62.5→63,
   662.5→663; 112.5→113, 812.5→813).

Events (shareRemovedSpace on)
9. start([a]) on a fresh record (root = empty desktop), a dockable → placed
   with anchor "desktop", ratios 1/1 ≤ 1.6532 → Right → `X(n1)[D .5, a .5]`;
   writes: a → (725, 35, 705, 855). Saved.
10. Then windowAdded(b, dockable) with frame (100, 100, 600, 400): desktop
    slot screen rect (10, 35, 705, 855) is on stage and ≥ 480×320 → b is
    **floated** onto the desktop near its current position; a is not
    rewritten. (Exact float rect: floating feature.)
11. arrangeAll with front-to-back [a, b, c] (all dockable): tree
    `X(n1)[a .5, Y(n2)[b .5, X(n3)[c .5, D .5] .5] .5]`; writes a → (10, 35,
    705, 855), b → (725, 35, 705, 423), c → (725, 468, 348, 422); framed =
    none. Desktop slot (1083, 468, 347, 422) is < 480 wide → not usable.
12. Then windowAdded(d, dockable), focused = c: anchor c (.5,.5,.25,.5):
    .5 ≤ 1.6532×.25 = .4133? no → Bottom → `X(n3)[Y(n4)[c .5, d .5] .5, D
    .5]`. Writes: c → raw (725, 467.5, 347.5, 206.25) → (725, 468, 348, 206);
    d → raw (725, 683.75, 347.5, 206.25) → (725, 684, 348, 206). a and b
    unchanged → not written. d is focused.
13. Then windowRemoved(c, temporary: false) (state of example 11, before
    12): c's panel removed → `X(n3)` collapses to D →
    `X(n1)[a .5, Y(n2)[b .5, D .5] .5]`; a, b unchanged → no writes; desktop
    slot (725, 468, 705, 422) now usable for the next new window.
14. windowRemoved(b, temporary: true) in example 11: away[b] = b's restore
    target + neighbour leaves; b's panel hidden; later windowAdded(b) puts it
    back at that spot.
15. Self-echo: after writing b → (725, 35, 705, 423), the OS reports b at
    (725, 35, 706, 423) within 1.5 s → expected (|Δ| ≤ 2) → ignored.
16. App move: 2 s later b reports (300, 200, 705, 423) with mouse up →
    not expected; 705 < 0.92×1440 → snap back: write (725, 35, 705, 423)
    again.
17. Refusal: target for d (725, 684, 348, 206); read-back (725, 684, 348,
    300): 300 > 206 + 24 → recheck after 0.25 s; still 300 → minimum height
    300 learned for d's app (`Learn(…×300)`), relayout: d's tile 206 <
    300 − 0.5 → d parked (cover). Read-back (725, 684, 348, 228) → within
    24 → accepted, and since 228 ≥ 206 − 2 the store is told via `Observe`.
18. Grow refusal: target 348 wide, actual 300 wide → 300 < 348 − 24 →
    recheck → still 300 → window maximum width 300.
19. move(toward: right) with nothing framed → no-op. With framed = b
    (unit (.5,0,.5,.5) in example 11): move(bottom) → neighbour n3 (score
    0) → frame n3 (.5,.5,.5,.5): c → (10, 35, 705, 855) (stretch identity,
    n3 fills the stage), a and b parked at (1439, 899, 705, 855) and
    (1439, 899, 705, 423); focus → c.
20. windowFocused(a) while a is parked (example 19), user-initiated, past
    `suppressRevealUntil` (> 1 s after a reveal, > 0.6 s after a landing
    focus) and > 1 s after any close → reveal: camera frames a; a written to the
    stage, others outside a parked.

## Evidence
Evidence directory `~/Documents/trelliswm-re/evidence/`; summary of
addresses and field offsets in `engine-tiling-loop-notes.txt` (new).
- Rule 1: `Engine.destinationScene` 0x10002d270 (`dec-tiling-destinationScene.json`, stage from visibleArea + policy.gap·0.5).
- Rules 2–3: `Engine.cameraRect(for:overview:)` 0x10002c9ec (`asm-tiling-cameraRect.txt`), `Stretch.init(framing:)` 0x100093d54 and `Stretch.map` 0x100093e80 (`asm-tiling-stretch.txt`), `Projection.init` 0x100092568 (`dec-tiling-projInit.json`).
- Rules 4–5: `Scene.frame(of:rect:floating:)` 0x1000e1738 (`asm-tiling-sceneFrame.txt`).
- Rule 6: `Rect.pixelRounded` 0x1001136b8 (`asm-tiling-pixelRounded.txt`); used by `realTargets` 0x1000375d0, 0x100037770.
- Rule 7: `Scene.stripHeight`, `PanelNode.hasStrip` in `Scene.placements` (`dec-tiling-placements.json`).
- Rules 8–9: `Scene.placements` 0x1000df120 (`dec-tiling-placements.json`; 0.995, −0.5, `Rect.intersection` with stage), `Scene.minimum` 0x1000e00b0 (`dec-tiling-sceneMinimum.json`), Placement getters (`asm-tiling-placementGetters.txt`: onStage +0x31, fits +0x32).
- Rule 10: `Engine.realTargets` 0x100036fd0 (`asm-realTargets.json`): onStage/fits branches 0x10003726c–0x10003727c, maximum clamp 0x10003767c, park 0x1000374f8, preview park 0x100037808–0x10003783c.
- Rules 12–15: `Engine.writeTargets` 0x100055500 (`dec-tiling-writeTargets.json`), `ManagedWindow.isExpected/expect` 0x100029540/0x10002931c (1.5, 2.0).
- Rules 16–18: write completion 0x100057cc4 (`dec-tiling-writeDone.json`, constants −2, 24), `confirmRefusal` closures 0x1000582c8/0x10005843c (`asm-tiling-confirmRefusal.txt`, ±24, logs MINIMUM/MAXIMUM/REFUSED), `Engine.lowerMinimum` 0x1000571d4 (`dec-tiling-lowerMinimum.json`).
- Rule 19: `Engine.placeWindows` 0x10003e734 and closures 0x100057ba4/0x100057c60 (`dec-tiling-placeWindows.json`, `dec-tiling-pwC1.json`, `dec-tiling-pwC2.json`).
- Rule 20: `Engine.checkPlacement` 0x10003fc50 and closure 0x1000574d4 (`dec-tiling-checkPlacement.json`, `dec-tiling-cpC1.json`, `asm-tiling-checkPlacementClosure.txt`; 0.05, 4.0, log "DRIFT").
- Rule 21: `Engine.fade` 0x10003ec7c + closure 0x10003feb0 (`asm-tiling-fade.txt`; delay policy+0x28 + 0.02).
- Rule 22: `Engine.canPresentDirectly` 0x1000469c0, `Engine.presentDirectly(focus:)` 0x100054f14 (`asm-tiling-canPresentDirectly.txt`, `asm-tiling-presentDirectly.txt`), `changeLayout` specialisation 0x1000864e8.
- Rules 23–24: `Engine.start(with:)` 0x1000336ec call list (objdump; `dec-engine-inbound-start-coordinator.json`), `startWhenCaptured` 0x100038b58 (`dec-tiling-startWhenCaptured.json`), park-geometry rule 21.
- Rules 25–28: `Engine.windowAdded` 0x1000eb700 (`dec-engine-inbound-lifecycle.json`, `asm-tiling-windowAdded.txt`: `desktopUsable` 0x1000e19f4, 0.96/0.02 clamps, `placeNew` 0x1000ece40, `changeLayout` 0x1000ed0f4/0x1000ed1f4 with land = !inOverview).
- Rules 29–31: `Engine.windowRemoved` 0x1000ea030 (same file; `hide`, `hideTab`, `remove`, `restoreTargetFor`, closeQuietUntil write).
- Rules 32–33: `Engine.windowFocused` 0x1000f0504 (same file; "REVEAL" log, `zoomInto`), `Engine.focusAfterLanding` 0x10003f23c (objdump; landingFocus 0xa88, lastFocused compare).
- Rules 34–39: `Engine.windowMoved` 0x1000f1950 (same file), `Engine.userMoved` 0x1000e58dc (`dec-tiling-userMoved.json`), `Engine.appResized` 0x1000f3b98 (`asm-tiling-appResized.txt`; 0.92, 0.9, 100, 60, logs ZOOM/UNZOOM), `Engine.snapBack` 0x1000f5290 (`dec-tiling-snapBack.json`; 3.0, "INSISTS … learned it").
- Rule 40: `Engine.arrangeAll` 0x1000addf0 (`dec-arrangeAll.json`; `frame(nil, …)`).
- Rule 41: `Engine.move(toward:)` (directional-focus), `Engine.frame(_:land:record:)` 0x1000a7970 (objdump: `Navigator.focus`, `cameraRect`, `beginMotion`).
- Rule 42: `engine-save-callers.txt`; fade closure `save`.

## Confidence
| Rule | Label |
|---|---|
| 1 stage | Observed |
| 2 camera = framed rect / unit | Observed |
| 3 stretch | Observed (breakpoints, 1e-9); use of stretched camera in Scene Inferred from Projection.init |
| 4 tile formula + inset | Observed |
| 5 floats | Observed (no inset, aspect fit); details for floating feature |
| 6 edge rounding | Observed |
| 7 strip | Inferred |
| 8 onStage 0.995 | Observed |
| 9 fits | Observed (floors, −0.5); "learned minimum" source = limits dictionary Inferred |
| 10 targets | Observed branches; "not fits → cover" purpose Inferred |
| 11 | Inferred (consequence) |
| 12 | Observed |
| 13 skip rules | Observed checks; exact "already parked" test Inferred |
| 14–15 | Observed (1.5 s, 2.0) |
| 16 | Observed constants (24, −2, 0.25 s); which argument means "larger" Inferred |
| 17 | Observed log strings and learn call; maximum-lowering Inferred from field writes |
| 18 | Observed |
| 19 | Observed |
| 20 | Observed |
| 21 | Observed call order; delay Observed |
| 22 | Observed conditions and calls; focus via next-turn closure Inferred |
| 23 | Observed call list; ordering and conditions partly Inferred |
| 24 | Inferred |
| 25–26 | Observed callee names; branch meanings Inferred |
| 27 desktop-usable float rule | Observed thresholds; branch structure Inferred (decompile is heavily inlined) |
| 27 tile cap | Observed existence; licensing — not analysed |
| 28 | Observed (changeLayout args); ensureVisible effect Inferred |
| 29 | Observed |
| 30 | Observed (hide/hideTab, away record); space handling Inferred |
| 31 | Observed (remove); share flag effect via docking findings |
| 32 | Observed conditions and zoomInto; "not caused by engine" Inferred |
| 33 | Inferred |
| 34–37 | Observed |
| 38 | Observed constants and log strings; exact conditions Inferred |
| 39 | Observed (handOff call) |
| 40 | Observed |
| 41 | Observed calls; "neighbour fills stage" Inferred from cameraRect |
| 42 | Observed |

## Unknowns
- **Exact branch logic of windowAdded and start**: the decompiled bodies are
  very large and inlined; the "float onto a usable desktop" rule and the
  start-time float-behind vs place choice are reconstructed from branch
  structure and constants, not read line by line. A runtime check (open a
  new window with an empty desktop slot visible) would confirm rule 27.
- **What a user can do with nothing framed**: `move(toward:)` from the
  root is a no-op; whether something sets a default framing (e.g. on focus)
  was not found. Matters for how arrow keys feel on one monitor.
- **Refusal arguments**: which completion argument selects "learns" and the
  "larger" path, and the 0.9 factor seen in the refusal recheck, were not
  mapped precisely; the 24/0.25 s behaviour is solid.
- **Insist/snap-back accounting**: snapBacks counter and "learned it"
  outcome (what exactly is learned) Inferred.
- Start-time sort key for unplaced windows; `held` flag; `takeSplit` details.
- Float rect for floated new windows (floating feature).
- Overview/camera interplay (#15/#16): `zoomInto`, landing, tweens,
  `startWhenCaptured` (waits for first pictures, deadline 1 s).
- `Feel` table discrepancy: the decoded constants file lists placementWait
  0.6 and dropSettle 0.15 while the study table lists 1.2; trust
  `symbols/feel-constants.tsv`. `parkSettle` (60) is not referenced in any
  path read here. Corrected per tuning-constants findings
  (`findings/tuning-constants.md`): native-tab recheck is 0.3 s, and
  reveal suppression after a landing focus is 0.6 s (rules 25, 33, constants
  list, example 20).
- Feature gate: docked-tile cap (`maxDockedTiles`) in windowAdded/arrangeAll —
  licensing, deliberately not analysed.

## Windows notes
- Feed `rcWork` (DIP) as the work area; write targets through the Win32
  adapter, which converts to physical pixels and compensates the invisible
  DWM border (targets are visible-frame rects). Read-back must report the
  same visible frame, or rule 15/16 comparisons will see a constant ~7 px
  error and trigger rechecks.
- Rounding to whole DIPs then scaling to pixels can reintroduce half pixels at
  125/150 % scaling; round in the adapter's pixel space after conversion, and
  report read-back in DIPs, so the 2-pt "expected" tolerance still absorbs
  the difference.
- `SetWindowPos` is synchronous but `WM_WINDOWPOSCHANGED`/`LOCATIONCHANGE`
  events for the engine's own writes will arrive; the expected-frames list
  (1.5 s, ±2) is what keeps them from being treated as user moves. Also
  ignore events during `EVENT_SYSTEM_MOVESIZESTART…END` (mouse drag).
- Minimums: query `WM_GETMINMAXINFO` (via `SendMessageTimeout`) when a window
  is admitted and seed the per-window minimum/maximum instead of waiting for
  a refusal; keep the refusal path for apps that clamp later.
- Maximised windows must be restored before tiling; a user maximise is the
  natural Windows equivalent of the "zoom request" (rule 38) — map
  `SW_MAXIMIZE` of a tile to "frame this window alone".
- "Not fits → parked with a picture" needs DWM thumbnails (cover feature).
  Until then CanvasWM #13 should write the tile frame anyway and accept the
  app's clamped size (or park it), and say which in the spec.
- Parking on Windows can be fully off-screen; see park-geometry notes.
- Desktop-slot floating (rule 27) makes the second new window float, not
  tile, on a fresh desktop. If that is undesirable for Windows users, expose
  it as a policy (it is effectively governed by whether the desktop node is
  shown — the "hide desktop" toggle).

## Recommended scope split
This is three features:
1. **#13a engine-projection-targets** (pure, can live in Core): stage,
   stretch, unit→screen mapping, edge rounding, onStage/fits classification,
   real-target selection (tile / clamp-to-maximum / park). Rules 1–11,
   examples 1–8.
2. **#13b engine-frame-writer** (Engine): write pass with skip rules,
   in-flight and expected-frame tracking, completion handling, refusal
   recheck and learn/observe, lowerMinimum, placement generation, drift
   check, direct presentation. Rules 12–22, examples 15–18.
3. **#13c engine-window-events** (Engine): start, windowAdded (desktop float
   vs placeNew), windowRemoved (hide vs remove), windowFocused (focus key,
   reveal hook), windowMoved (echo filter, snap back, zoom request),
   arrangeAll, move(toward:), focus after landing, save triggers.
   Rules 23–42, examples 9–14, 19–20. Depends on 13a and 13b.
