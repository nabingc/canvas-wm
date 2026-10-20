# Feature: engine-window-events

Sub-feature #27, carved out of `findings/engine-tiling-loop.md` (#13), parts
D–J (rules 23–42 there). Lives in `CanvasWm.Engine` (`DisplayEngine`). It
uses the shell of #23 (`findings/engine-inbound-api.md`), the targets of #25
(`findings/engine-projection-targets.md`) and the writer of #26
(`findings/engine-frame-writer.md`), plus the Core operations of #1, #3, #4,
#5. **This file supersedes #13 rules 23–42 where they differ** (notably: the
start-time order and the float-behind test, the zoom-request conditions, what
snap-back learns, the reveal target, the arrange-all filter, and the
"eligibility flag", which is the window's dockable flag).

## Summary
These are the engine's reactions to window events and two navigation
commands. On start it reloads the saved layout, drops windows that no longer
exist, tiles every dockable window that has no place yet next to the framed
area, and writes all frames. A new window is tiled next to the focused window
(or put back where it was, or left floating if it cannot be tiled); a closed
window's space goes to its siblings; a focus change on a window that is not
visible makes the engine frame that window. When an app moves or resizes a
tiled window on its own, the engine puts it back; if the app does that three
times within three seconds the engine learns a size limit for it instead, and
a large self-grow is read as "show this window alone". "Arrange all" rebuilds
the tree as a spiral in front-to-back order; "move toward" frames the
neighbouring tile. Every structural change is saved immediately.

Human decisions recorded for this feature (not re-analysed):
- **H1.** The studied app floats a new window onto the visible empty-desktop
  slot (rule 22). CanvasWM keeps that rule only behind an `EnginePolicy`
  flag, **default off**; with the flag off new windows always tile.
- **H2.** When nothing is framed, `Move(toward)` starts from the focused
  window's panel (CanvasWM deviation; the studied app does nothing).
- **H3.** No docked-tile cap of any kind. The cap present in the studied app
  is its licensing gate and was not analysed.

## Behaviour

Conventions: "commit an edit" = replace the document, sync the navigator
(rule 43), save, render, fire `OnChange`, then **present** (rule 44). "At
rest" = phase desk, not in overview, no interaction, no native drag, no
layout tween (#26 rule 1). Times are seconds on `IClock.Now`. All events are
ignored while suspended (inbound-api rule 16–19 guards; already in #23).

### A. start(windows)
1. **Register** every given window (already in #23). Native-tab watcher:
   macOS only, not needed.
2. **Load** the saved record for the engine key. The saved layout is
   **ignored entirely** (start from an empty document: root = empty desktop)
   when there is no record **or** when none of the saved document's window
   keys is among the current windows (the two sets are disjoint).
3. Otherwise **sanitise** the saved document against the current window keys
   (layout-tree rules 17–21: unknown views dropped, duplicates dropped, at
   most one desktop node). If the sanitised tree has no desktop node, a
   desktop node is inserted beside the root (fraction 0.5, one new split id).
   Restore the saved framed node id (navigator) and the saved views "1"…"9"
   (camera feature; store them, otherwise unused), and the saved dock spots
   per window.
4. **Drop docked windows the user moved while unmanaged**: a docked window
   whose current frame differs from its remembered resting frame by more than
   2 on any component, and which was not remembered as parked, is removed
   from the tree (park-geometry rule 21). It becomes an unplaced window for
   rule 5.
5. **Adopt unplaced windows.** Take all managed windows **sorted by window
   key (ordinal string order)**; for each one that has no panel anywhere in
   the document:
   - **dockable** → `PlaceNew(doc, key, framed = navigator framed id, else
     the root id, focused = none, stageAspect)` (new-window-placement rules
     1–11; stageAspect = stage width ÷ stage height, stage = work area inset
     by gap/2 per side);
   - **not dockable** → **float behind** at its current position (rule 23).
   The desktop-slot rule of H1 is **not** applied at start, in either app.
   Windows that already have a panel (tree, floating or hidden) keep it.
6. Ensure a desktop node exists when the document needs one (the document
   setter does this on every assignment: if the layout "needs a desktop" one
   is shown again). Sync the navigator; start navigation history.
7. **Initial present**: mark started, compute targets, render, **save**, then
   land: write all targets as a landing pass (#26 rule 2) with a new
   generation. The studied app waits for first live pictures, at most 1 s;
   CanvasWM lands immediately. When the landing completes, the settle step
   of #26 rule 27 runs, preceded by **focus after landing** (rule 40), and
   saves again.
8. Start is a no-op if already started (#23).

### B. windowAdded(info)
9. Registration, duplicate refresh, away-table removal and the "not started
   → record only" rule are #23's. While an interaction is running it is ended
   first (move, gap drag or gesture; none exist yet in CanvasWM).
10. If the window **already has a panel**:
    - in the tree or floating → nothing changes (its slot is kept; a refresh
      of info only);
    - in the **hidden list** (it was hidden by a temporary removal) → it is
      **restored** to its remembered place and the edit is committed (needs
      slot-restore #19; see rule 26 for the CanvasWM minimum).
11. Otherwise the first rule that applies decides (order Inferred, see
    Unknowns):
    1. the record has a **remembered dock spot** (from the away table or the
       saved record) → redock at that spot (#19; minimum: rule 12);
    2. a pending **split-for-new-window** request exists → dock there
       (command not built; skip);
    3. **H1 flag on** and the desktop slot is usable (projection-targets:
       desktop node's gap-inset screen rect ≥ 99.5 % on stage and at least
       480 × 320) → float the window onto the desktop at its current position
       (needs floating-panels #22);
    4. window is **dockable** → `PlaceNew(doc, key, framed = navigator framed
       id, else root id, focused = engine's focused key, stageAspect)`; if
       PlaceNew reports "needs float" go to 5;
    5. otherwise → **float** (rule 23).
    No docked-tile cap is checked (H3).
12. **CanvasWM minimum while #19 is not built**: a remembered spot is ignored
    and the window is placed by rule 11.4.
13. The edit is **committed** with focus = the new window and "include" = the
    new window (the framing must keep the new window visible; at rest with
    PlaceNew this already holds, because PlaceNew puts the window inside the
    framed area). Present directly at rest: all changed targets are written
    (#26 skip rules drop unchanged windows), then the new window is focused
    on the next UI turn (#26 rule 1). Saved.
14. A dockable window whose `windowBecameDockable` arrives later is not
    re-placed by that event (#23 rule 21); it is placed only on its next add.

### C. windowRemoved(key, temporary)
15. Unknown or unmanaged key → only #23's set clean-up.
16. `closeQuietUntil = now + 1` (suppresses the reveal reaction to the focus
    change a close causes, rule 30). Any running interaction is ended.
17. **temporary = true** (minimise, app hide, desktop switch away):
    - if the window is **docked** in the tree, its dock spot is remembered in
      the away table: the restore target of its panel plus the leaf ids of the
      node that target points at;
    - its panel is **hidden**: removed from the tree and kept in the
      document's hidden list with that restore target. A tabbed panel with
      two or more views hides only that view.
    - the space is given up exactly like a removal (rule 18).
18. **temporary = false** (closed): the window's view is removed. If it was
    the panel's only view the panel is removed: when `ShareRemovedSpace` is
    on (default) and the panel is in the tree, its weight is shared by all
    its siblings in proportion (sharing variant, docking rule 7); otherwise
    the adjacent variant (whole weight to the following sibling, or the
    preceding one if it was last) is used. Floating or hidden panels are
    simply dropped from their list. A panel with several views just loses
    that view.
19. The managed record is dropped; a parked window is written back centred
    on screen; capture stopped; if it was the focused key, the focused key
    becomes none (all #23). The **framed node** is fixed by the navigator
    sync (rule 43). The edit is committed with no focus request; remaining
    windows are re-targeted and only changed ones written. Saved.
20. **CanvasWM minimum while #19 is not built**: for temporary removals,
    remove the panel exactly as rule 18 (no hidden list) and keep the key in
    the away table with no spot (the #23 shell already stores `null`). On
    re-add rule 11.4 places it again.

### D. windowFocused(key)
21. Unknown key → ignored. Otherwise: native tab-group restoration and
    torn-tab splitting (macOS only, skip); a pending split-for-new-window
    request for a different window is dropped if older than 1 s; the window's
    `lastFocused = now`; focused key = key; a floating window is raised to the
    top of the floating order; render. The framed node does **not** change
    here, except through a reveal (rule 22).
22. **Reveal** — the engine frames the window if the user focused a window
    that is not properly visible. All of the following must hold:
    - the focus was not caused by the engine itself (the key differs from
      the key the engine last activated, rule 41; Inferred);
    - `now ≥ suppressRevealUntil` and `now ≥ closeQuietUntil`;
    - the stage is at rest (otherwise the key is remembered as a pending
      reveal; camera feature);
    - the window has a placement and that placement is **not** (on stage and
      fits) — i.e. it is parked, partly off stage, or too small.
    Then `suppressRevealUntil = now + 1` and the engine **zooms into** the
    window: target node = the window's panel id (a floating window → the
    desktop node "desktop"); the **landing focus** is set to the window; the
    node is framed (history recorded) and the display re-lands (rule 42).
23. *(Float helper used by rules 5 and 11.5.)* "Float behind" at start and
    "float" for a non-dockable new window both create a floating panel whose
    rect is the window's current frame expressed in desktop-unit space (size
    fractions capped at 1; position clamped so the rect stays inside the
    desktop). **CanvasWM minimum while #22 is not built**: give the window no
    panel at all. It stays managed (focus, known frame, release, removal) but
    has no target, so the engine never moves or resizes it (projection rule
    10). When #22 lands, replace this with a floating panel.

### E. windowMoved(key, frame)
24. Unknown key → ignored. The reported frame becomes the known frame first
    (#23). `resized` = width or height differs from the previous known frame
    by more than 0.5.
25. **Ignored for layout** when any holds: a write for the window is in flight
    (`writesInFlight > 0`); the mouse button is down (native drag →
    drag-to-dock feature, out of scope); the frame is an **expected echo**
    (#26 rule 25: matches a write target or read-back within 2.0 on every
    component, entries ≤ 1.5 s old).
26. **Parked** window that moved by itself → parked flag cleared and **all**
    targets rewritten (it is re-parked, or shown if it now belongs on stage).
27. **Floating** window → its floating rect is updated to where it now is and
    the change settles (floating feature #22; minimum: windows without a
    panel are ignored here).
28. **Tiled** window moved or resized by its app (`appResized`): let *tile*
    = the window's current tile target (if it has none, tile = the frame
    itself), *area* = work area. Three flags:
    - **grew** = (frame width > tile width + 100 and frame height ≥ tile
      height − 2) or (frame height > tile height + 60 and frame width ≥ tile
      width − 2);
    - **shrank** = frame width < tile width − 100 or frame height < tile
      height − 100;
    - **fills** = frame width ≥ 0.92 × area width and frame height ≥ 0.90 ×
      area height;
    - **framed alone** = the window's panel is the navigator's framed node.
29. **Zoom decision** — only when `resized` and more than **1 s** has passed
    since the last zoom toggle by this rule:
    - framed alone and **shrank** → *un-zoom request*;
    - framed alone, **fills**, and the frame differs from the tile by more
      than **4** on any of x, y, w, h → *un-zoom request*;
    - not framed alone and **grew** → *zoom request*.
    A (un)zoom request records the toggle time and calls "toggle maximize"
    for the window: zoom = frame that window's panel alone (remembering the
    previous framing); un-zoom = return to the remembered framing. At rest
    this is a re-land (rule 42). Anything else → snap back (rule 30).
30. **Snap back with insist detection.** Each snap back appends `now` to the
    window's snap-back time list after dropping entries older than
    `SnapBackWindowSeconds` (3; kept if `now − t < 3`). Then:
    - fewer than `SnapBackLimit` (3) entries → rewrite **all** targets
      (`WriteTargets`, current generation, no landing count) and render. Only
      the moved window actually changes, so only it is written.
    - 3 or more → the list is cleared and the engine **accepts** the app's
      size instead of fighting (rule 31), then rewrites all targets and
      renders.
31. **What is learned on the 3rd snap back** (compare the frame size with the
    tile size + 2 per axis):
    - frame not larger than tile + 2 on **either** axis → the window's own
      **maximum** becomes the frame size (both axes). Not stored in the
      LimitStore.
    - larger on some axis → that axis **qualifies** if the frame is also
      smaller than **0.8 ×** the work area on that axis. If any axis
      qualifies: own **minimum** per axis = max(old own minimum or 0, frame)
      on qualifying axes, old own minimum or 0 on the others; and
      `LimitStore.Learn(minimum, appId)`. If no axis qualifies ("nearly the
      display's size") nothing is learned.
    The following retarget then usually parks the window (raised minimum no
    longer fits → park with picture) or clamps and centres it (maximum).
    Snap-back does not save.
32. Leaving the display (drag to another monitor) → hand-off (multi-display
    #24, out of scope).

### F. Other events
33. `windowTitleChanged`, `windowBecameDockable`, `captureArrived`,
    `appActivated`: no layout, frame or save effect beyond #23. (`appActivated`
    also expires a split-for-new-window request older than about 1 s; the
    command is not built.)

### G. arrangeAll()
34. Order list: the window system's **front-to-back** list, keeping a key
    only if it has a panel in the document, is not hidden, is managed and is
    **dockable**; then every other window key of the document (document
    order) not already listed, keeping managed ones only if dockable
    (unmanaged keys in the document are kept). Floating non-dockable windows
    therefore stay floating.
35. `ArrangeDwindle(doc, order, landscape = stage width ≥ stage height)`
    (new-window-placement rules 12–21); docked panels not in the list become
    cascaded floats. No cap (H3).
36. The framing is cleared (**frame nothing**, history recorded unless in
    overview), the edit is committed and presented, and saved. At rest the
    result is: nothing framed, every tile on stage and written.

### H. Move(toward: edge)
37. Current node = the navigator's framed node id. **H2:** if nothing is
    framed, current = the panel id of the focused window when that window is
    docked in the tree; if there is no focused window or it is floating /
    hidden / has no panel → nothing happens.
38. `Neighbour(entries, current, edge)` (directional-focus rules 4–14). None
    → nothing happens (no history, no write).
39. Found → **frame** the neighbour: the navigator frames that node and
    records history, the display re-lands (rule 42). Consequence at rest: the
    neighbour node fills the stage (its unit rect becomes the camera; a
    non-square node is stretched), every tile outside it is parked, and focus
    follows by rule 40. The tree is never edited.

### I. Focus, landing and navigator helpers
40. **Focus after landing** (runs when a landing completes, before the
    settle delay): candidates = windows whose placement is on stage and fits.
    Choose the landing focus if it is a candidate, else the current focused
    key if it is a candidate, else the candidate with the latest
    `lastFocused`. If one is chosen, activate it (rule 41). In every case
    `suppressRevealUntil = now + LandingRevealQuietSeconds` (0.6). Then clear
    the landing focus.
41. **Activate(key)**: `lastFocused = now`, focused key = key, "engine
    activated key" = key, then `IWindowSystem.Focus(key)`. The OS echo
    (`windowFocused`) for that key is not a reveal trigger (rule 22).
42. **Re-land at rest** (camera feature equivalent): bump the generation and
    run a landing pass with the new generation (#26 rule 3, `ReLand`),
    followed by settle → focus after landing (rule 40) → drift check → save.
    The camera animation of the studied app is replaced by this jump.
43. **Navigator sync** after every document change: if the framed node still
    exists, remember its leaf ids; if it no longer exists, frame the
    smallest node that contains the surviving previously framed leaves, or
    nothing if none survive (Inferred).
44. **Present** after a commit: at rest → direct presentation (#26 rule 1)
    with the edit's focus request; not at rest → camera motion (later
    feature).

### J. Saving
45. Save happens synchronously (no debounce, no timer) at: start (before
    landing) and after its landing settles; every committed edit (window
    added, window removed, arrange all, and later features' edits); every
    landing settle (#26 rule 27); suspend (#23). Not saved by: focus, title,
    dockable, capture, app activation, snap-back, echo/parked rewrite.
46. Saved content: document, framed node id, saved views, window owners,
    dock spots, float sizes (engine-interfaces rule 36). Some windows are
    removed from the saved copy (Inferred: guests from another display).

## Data model
- Per managed window (adds to #23/#26): `lastFocused: time`, `snapBackTimes:
  list of time`, own `minimum?`/`maximum?` (shared with #26), remembered
  `DockSpot?` (exists).
- Engine: `suppressRevealUntil: time` (initial −∞), `closeQuietUntil: time`
  (−∞), `lastZoomToggle: time` (−∞), `engineActivatedKey?`, `landingFocus?`,
  `pendingReveal?` (unused at rest), navigator `{ framed?: node id,
  framedLeaves: list, previousFramed? (for un-zoom) }`, history (can be a
  simple list until #15).
- Policy (new): `FloatNewWindowsOnDesktop: bool = false` (H1; name is a
  suggestion).
- Tuning (existing #8): `SnapBackLimit` 3, `SnapBackWindowSeconds` 3,
  `LandingRevealQuietSeconds` 0.6. Literals that are not tuning: reveal
  suppression after a plain focus 1 s, close quiet 1 s, zoom toggle spacing
  1 s, grow thresholds 100 / 60, shrink 100, keep −2 slack, fills 0.92 /
  0.90, un-zoom drift 4, insist size slack 2, insist screen factor 0.8,
  resized threshold 0.5.
- Inputs used: `WindowInfo.Dockable`, `IWindowSystem.FrontToBack()`,
  `IWindowSystem.Focus(key)`, work area and `Policy.Gap`, `ShareRemovedSpace`.

## Worked examples
Common: work area A = (0, 25, 1440, 875), display (0, 0, 1440, 900), gap 10
→ stage (5, 30, 1430, 865), stageAspect = 1430/865 ≈ 1.6532, landscape.
`ShareRemovedSpace` on, H1 flag off, nothing framed unless stated. New split
ids are shown as n1, n2, … in minting order. Unit rect (ux, uy, uw, uh) with
nothing framed → `(10 + 1430·ux, 35 + 865·uy, 1430·uw − 10, 865·uh − 10)`,
then edges rounded half away from zero (#25).

Start
1. **Fresh start.** No saved record. `Start([a, b, n])`, a and b dockable,
   n not dockable at (200, 200, 400, 300). Document = empty desktop D (root
   id "desktop"). Sorted keys a, b, n:
   - a: PlaceNew(framed "desktop", focused none) → anchor D, 1/1 ≤ 1.6532 →
     Right → `X(n1)[D .5, a .5]`;
   - b: framed = root n1 → anchor a (last panel), a (.5,0,.5,1): 1 ≤
     0.8266? no → Bottom → `X(n1)[D .5, Y(n2)[a .5, b .5] .5]`;
   - n: not dockable → floated behind (CanvasWM minimum: no panel, never
     written).
   Targets: a → (725, 35, 705, 423); b → raw (725, 467.5, 705, 422.5) →
   (725, 468, 705, 422). Saved; landing pass writes a and b (n untouched);
   after landing focus: landing focus none, focused none → candidates a, b
   with equal `lastFocused` (−∞) → first in candidate order (Unknown tie
   rule); `suppressRevealUntil = now + 0.6`; save again.
2. **Saved record unrelated.** Saved doc holds windows x, y only; current
   windows a, b → sets disjoint → saved layout ignored → same result as 1.
3. **Saved record partly valid.** Saved `X(s)[x .5, a .5]` (no desktop),
   current [a, c] (both dockable), saved framed none. Sanitise → x dropped →
   root `a`; no desktop → desktop inserted beside the root →
   `X(n1)[a .5, D .5]` (edge Inferred). Unplaced: c → framed = root n1,
   leaves [a, desktop] → anchor a (0,0,.5,1) → Bottom →
   `X(n1)[Y(n2)[a .5, c .5] .5, D .5]`.

windowAdded
4. State of example 1, focused b. `WindowAdded(c, dockable)` → no panel, no
   spot, flag off → PlaceNew(framed n1, focused b): b (.5,.5,.5,.5), 0.5 ≤
   0.8266 → Right → `X(n1)[D .5, Y(n2)[a .5, X(n3)[b .5, c .5] .5] .5]`.
   Writes: b → (725, 468, 348, 422), c → raw (1082.5, 467.5, 347.5, 422.5)
   → (1083, 468, 347, 422); a unchanged, not written. Next UI turn:
   Focus(c). Saved.
5. Same with H1 flag **on**: desktop slot D (0,0,.5,1) → screen (10, 35, 705,
   855), fully on stage, ≥ 480 × 320 → c is floated onto the desktop
   (floating feature); no tile changes.
6. Non-dockable `WindowAdded(d)` → no panel (minimum), nothing written,
   saved, not focused by the engine.
7. **Framed sub-node.** Example 1 with framed = n2 (unit (.5,0,.5,1)),
   focused a. `WindowAdded(c)`: anchor a (inside n2), a.h/f.h = 0.5 ≤
   1.6532 × (0.5/0.5) → Right → `Y(n2)[X(n3)[a .5, c .5] .5, b .5]`.
   Projection with framing n2 (x stretched ×2 on [.5,1]): a → (10, 35, 705,
   423), c → (725, 35, 705, 423), b → raw (10, 467.5, 1420, 422.5) → (10,
   468, 1420, 422); D off stage (no window). c is visible, so "include" needs
   no reframe.
8. Duplicate add of a managed, tiled window → no layout change, no write
   (only #23 refresh and save).

windowRemoved
9. Example 4 state, `WindowRemoved(b, false)`: `X(n3)[b .5, c .5]` loses b →
   collapses → `X(n1)[D .5, Y(n2)[a .5, c .5] .5]`; c → (725, 468, 705,
   422) written; a unchanged. If b was focused → focused none. Saved.
   `closeQuietUntil = now + 1`.
10. Sharing vs adjacent: `X(s)[a 1/3, b 1/3, c 1/3]` remove b → share on:
    a .5, c .5; share off: a 1/3, c 2/3.
11. `WindowRemoved(a, true)` in example 1 (studied app): away[a] = spot
    {restore target of a, neighbour leaves}; a hidden; tree `X(n1)[D .5,
    b .5]`; b → (725, 35, 705, 855). CanvasWM minimum (rule 20): same tree,
    away[a] = none; later `WindowAdded(a)` → PlaceNew(framed n1, focused b):
    b (.5,0,.5,1) → Bottom → `X(n1)[D .5, Y(n4)[b .5, a .5] .5]` (a now
    below b, not back above it — the difference #19 would fix).

windowFocused / reveal
12. Nothing framed, `WindowFocused(c)` → focused c, lastFocused updated,
    render; c is on stage → no reveal, no write.
13. Framed = b's panel (b fills the stage, a parked). At t = 50.0 a landing
    focused b → `suppressRevealUntil = 50.6`. User focuses a at 50.3 → no
    reveal. User focuses a at 51.0 (no close in the last second) → a is
    parked → reveal: `suppressRevealUntil = 52.0`, landing focus a, frame a's
    panel (unit (.5,0,.5,.5), square) → re-land: a → (10, 35, 1420, 855), b
    parked; after landing Focus(a), `suppressRevealUntil = now + 0.6`.
14. A window closes at 60.0 and the OS focuses parked window a at 60.4 →
    60.4 < closeQuietUntil 61.0 → no reveal.

windowMoved
15. Echo: after writing c → (1083, 468, 347, 422) at 70.00, OS reports c at
    (1083, 468, 348, 422) at 70.10 → expected → ignored.
16. Snap back: nothing framed, tile b = (725, 468, 705, 422). App moves b to
    (300, 200, 705, 422) at 80.0 (mouse up, nothing in flight) → resized
    false → snap back: times [80.0] → rewrite → b written to its tile. Again
    at 81.0 → [80.0, 81.0] → rewrite. At 82.0 → [80, 81, 82] → 3 → list
    cleared; frame not larger than tile + 2 → own maximum = (705, 422) → no
    visible change; rewrite → b written back. At 82.9 a 4th move → list
    [82.9] → plain snap back.
17. Insist larger: same tile; app resizes b to height 470 (w 705) at 90.0,
    90.5, 91.0, each time snapped back first. Flags each time: grew? 470 >
    482? no; shrank no → snap back. Third at 91.0: height 470 > 424 and 470
    < 0.8 × 875 = 700 → qualifies; width 705 > 707? no → own minimum = (0,
    470); Learn((0, 470), app). Retarget: tile height 422 < 470 − 0.5 → does
    not fit → b parked with picture.
18. Insist limits: tile (10, 35, 1420, 600); the app keeps height 655 (55
    more, below the 60 "grew" margin) → snapped back twice, third time:
    655 > 602 and 655 < 700 → own minimum (0, 655) learned. With tile height
    650 and app height 705 instead: 705 ≥ 0.8 × 875 = 700 → nothing learned
    ("nearly the display's size"); the window is still written back.
19. Zoom request: tile b (725, 468, 705, 422), nothing framed. App resizes b
    to (100, 100, 1340, 800) at 100.0 (more than 1 s since the last toggle):
    grew (1340 > 805, 800 ≥ 420) → zoom request → b's panel framed alone →
    re-land: b → (10, 35, 1420, 855), others parked; toggle time 100.0.
20. Un-zoom: then at 100.5 the app shrinks b to (300, 300, 600, 400):
    resized, but 0.5 s < 1 s since the toggle → snap back to (10, 35, 1420,
    855). At 101.5 the same shrink → shrank (600 < 1320) and framed alone →
    un-zoom → previous framing (none) restored → all tiles back on stage.
21. Parked window p reported at (400, 300, 705, 855) (not expected, mouse up)
    → parked flag cleared, all targets rewritten → p re-parked.

arrangeAll
22. State of example 4 plus a non-dockable window d that has no panel
    (rule 23 minimum).
    FrontToBack = [c, d, a, b]: d has no panel → skipped; order [c, a, b] →
    `X(n5)[c .5, Y(n6)[a .5, X(n7)[b .5, D .5] .5] .5]`; framed none; writes
    c → (10, 35, 705, 855), a → (725, 35, 705, 423), b → (725, 468, 348,
    422). Saved.
23. FrontToBack = [b] only (other windows missing from the OS list) → order
    [b, then document order of the rest: a, c] → `X[b, Y[a, X[c, D]]]`.

Move(toward)
24. Example 1 state, nothing framed, focused a (unit (.5,0,.5,.5)).
    `Move(Bottom)` (H2): current = panel a → neighbour b (touching, overlap,
    score 0) → frame b → re-land: b → (10, 35, 1420, 855); a parked at its
    park frame (park-geometry; in this display candidate 0 = (1439, 899,
    705, 423)); after landing: landing focus none, focused a not on stage →
    latest-focused candidate b → Focus(b); `suppressRevealUntil = now + 0.6`.
25. Same state, `Move(Right)` → no tile touches a's right edge (x = 1) →
    nothing. `Move(Left)` → neighbour = empty desktop D (0,0,.5,1) → framed
    D → stretched to fill the stage; a, b parked; no candidate window →
    focus unchanged.
26. Nothing framed and no focused window → `Move(any)` → nothing.
27. Framed b (after 24), `Move(Top)` → current b → neighbour a → frame a.

## Evidence
Directory `~/Documents/trelliswm-re/evidence/`; new summary
`engine-window-events-notes.txt` (addresses, offsets, constants).
- Rules 1–8: `Engine.start(with:)` 0x1000336ec (objdump call order: saved
  record test 0x100033e70, `viewIds` + `Set.isDisjoint` 0x100033f40,
  `sanitize` 0x100033f94, `findDesktop`/`insertBeside`, `Navigator.focus`,
  framings parse, `isDocked`/`remove` loop, sorted keys 0x1000354fc,
  dockable byte +0x78 test 0x1000359bc, `floatBehind` 0x100035cac,
  `placeNew` 0x100035d5c, `beginHistory`); `dec-engine-inbound-start-
  coordinator.json`; `dec-tiling-startWhenCaptured.json`; doc setter
  0x10002a5dc (`needsDesktop`/`showDesktop`); park-geometry rule 21.
- Rules 9–14: `Engine.windowAdded` 0x1000eb700 (`asm-tiling-windowAdded.txt`,
  `dec-engine-inbound-lifecycle.json` item 0: `joinTabGroup`, `isHidden`,
  `panelOfView`, `takeSplit`, `desktopUsable`, `liveFloatRect`/`float`,
  `placeNew` 0x1000ece40 with framed engine+0x210 else +0x258 and focused
  +0x290, stageAspect arithmetic 0x1000ecdc0, `changeLayout`
  specialisation 0x1000864e8); dockable at info+0x68 (= window +0x78).
- Rules 15–20: `Engine.windowRemoved` 0x1000ea030 (call list: `isDocked`,
  `restoreTargetFor`, `findNode`, `leafIds`, `visualWorldRects`, `remove`,
  `hideTab`, `hide`, `changeLayout`); `Arrangement.remove` 0x10000f5d4
  (global `shareRemovedSpace` 0x100260388 → `removeSharing` vs
  `removePanel`), `Arrangement.hide` 0x10000ea14, `Arrangement.dockSpot`
  0x100011218; #23 rule 17.
- Rules 21–22: `Engine.windowFocused` 0x1000f0504 (lifecycle item 2: "REVEAL"
  log, time checks engine+0xdd0/+0xf70, placements onStage/fits, `+1.0`,
  `zoomInto`); `focusMovedFromSplit` (`dec-tiling-focusMovedFromSplit.json`,
  `+1.0`); `Engine.zoomInto` 0x1000a7bec (floating → "desktop", panel id,
  landing focus +0xa88, `frame(land: true, record: true)`).
- Rule 23: `Arrangement.floatBehind` call and clamps 0x100035c18–
  0x100035c60; projection-targets rule 10 (no panel → no target).
- Rules 24–27: `Engine.windowMoved` 0x1000f1950 (`engine-tiling-loop-notes.txt`),
  #26 rule 25.
- Rules 28–29: `Engine.appResized` 0x1000f3b98 (`asm-tiling-appResized.txt`:
  0.92, 0.9, 100, 60, −2, −100, 4.0, 1.0, engine+0xde0, strings "ZOOM
  request"/"UNZOOM request" decoded from immediates, `toggleMaximize`).
- Rules 30–31: `Engine.snapBack(_:tile:)` 0x1000f5290 (`dec-tiling-snapBack.json`
  and objdump: 3.0, count ≥ 3, +2.0, 0.8, maximum store +0x98, minimum
  store +0x80, limit-store call, "learned it"/"nothing learned" logs,
  `realTargets` + `writeTargets` + `render`).
- Rule 33: #23 rules 20–21.
- Rules 34–36: `Engine.arrangeAll` 0x1000addf0 (`dec-arrangeAll.json`:
  dockable +0x78 tests in both loops, `frame(nil, land: false, record:
  !engine+0x279)`), new-window-placement rule 21.
- Rules 37–39: `dec-engineMoveToward.json`, directional-focus rules 2, 15–19,
  `Engine.frame(_:land:record:)` 0x1000a7970.
- Rules 40–41: `Engine.focusAfterLanding` 0x10003f23c (landing focus +0xa88,
  focused +0x290, lastFocused +0xc8, `+0.6` → +0xdd0), `Engine.activate`
  0x1000404e8 (stores +0xc8, +0x290, +0xf60).
- Rule 42: #26 rule 3, `Engine.frame` → `beginMotion(land:)`.
- Rule 43: `Navigator.sync` 0x1000be90c (`Entries.has`, `leafIds`,
  `frameLeaves`).
- Rules 45–46: `engine-save-callers.txt`; `Engine.save` 0x100039124 (no
  clock scheduling; `Arrangement.remove` on the saved copy).

## Confidence
| Rule | Label |
|---|---|
| 1 | Observed (calls); macOS watcher skip is a Windows decision |
| 2 disjoint → ignore saved | Observed (test and branch); "empty desktop root" for the fresh path Inferred |
| 3 | Observed calls; edge of the inserted desktop Unknown (fraction 0.5 per #13) |
| 4 | Observed (#13/park-geometry) |
| 5 sort order, dockable test, PlaceNew args | Observed |
| 5 "already has a panel → keep" | Inferred |
| 6 | Observed |
| 7 | Observed order; CanvasWM immediate landing is a decision |
| 9 | Observed |
| 10 | Inferred (branch reconstruction) |
| 11 order of 1–3 | Inferred; 11.4 args Observed; 11.5 Observed callee |
| 12, 20, 23 minimums | CanvasWM decisions (no evidence needed) |
| 13 | Observed (`changeLayout` args via #13); next-turn focus Observed (#26) |
| 14 | Observed (#23) |
| 16 | Observed |
| 17 | Observed callees; hideTab condition Inferred |
| 18 | Observed |
| 19 | Observed (#23); framed fix-up Inferred (rule 43) |
| 21 | Observed callees; tab parts macOS-only |
| 22 conditions, +1 s, zoomInto target | Observed |
| 22 "not caused by engine" via activated key | Inferred |
| 24–27 | Observed |
| 28 flag formulas | Observed (constants and compare order) |
| 29 decision table | Observed |
| 29 toggleMaximize meaning (frame alone / restore) | Inferred (camera feature) |
| 30 | Observed |
| 31 | Observed (stores, factor 0.8, learn call); limit-store slot = Learn Inferred |
| 34 | Observed (dockable filter); front-to-back source Inferred (#4) |
| 35–36 | Observed |
| 37 H2 | Human decision |
| 38–39 | Observed calls; at-rest consequence Inferred |
| 40 | Observed choice order and 0.6; desktop-focus special case Unknown |
| 41 | Observed |
| 42 | Inferred (at-rest equivalent of the camera motion) |
| 45 | Observed (callers); "no debounce" Observed (no timer in save) |
| 46 | Inferred |
| 43 | Inferred (call order) |

## Unknowns
- **windowAdded branch order** (rule 11): the function is very large and
  inlined; a flag on its stack frame selects between a desktop-float path and
  the split/desktop/place path and could not be named (candidate meanings:
  "has remembered spot" or the `held` argument). The relative order of
  remembered spot, pending split and desktop float is therefore Inferred.
  Irrelevant for CanvasWM defaults (spot ignored until #19, split not built,
  H1 off).
- **Start: windows in the hidden list** whose windows are present and not
  minimised at start — kept hidden or restored? Not traced.
- **Edge of the desktop node inserted at start** when the saved tree has none.
- **Focus-after-landing tie** when no candidate has ever been focused, and
  the desktop-focus special case (two engine flags read next to
  `desktopFocused`): not decoded. Suggest: keep the current OS focus.
- **toggleMaximize internals** (maximize session, history): camera feature.
  The at-rest minimum in rule 29 is an interpretation.
- **Pending reveal while not at rest** (remembered key): camera feature.
- **Saved-copy filtering** in `save` (which keys are removed).
- Licensing: the docked-tile cap in windowAdded and arrangeAll (policy field
  at offset 0x18) was identified only by its policy read and deliberately not
  analysed (H3).

## Windows notes
- **Event mapping**: create/show of a manageable top-level → `WindowAdded`;
  minimise (`EVENT_SYSTEM_MINIMIZESTART`), cloak/hide and virtual-desktop
  move → `WindowRemoved(temporary: true)`; destroy → `WindowRemoved(false)`;
  `EVENT_SYSTEM_FOREGROUND` → `WindowFocused`; `EVENT_OBJECT_LOCATIONCHANGE`
  (top-level, after coalescing) → `WindowMoved`. Restore from minimise →
  `WindowAdded` (the away table handles re-placement).
- **Mouse-down filter** (rule 25): treat the interval between
  `EVENT_SYSTEM_MOVESIZESTART` and `MOVESIZEEND` as "mouse down"; on END the
  final frame arrives as an ordinary `WindowMoved`, which will snap the
  window back (drag-to-dock is not built). That makes user drags of tiled
  windows snap back — expected until drag-to-dock exists.
- **Zoom request**: the user maximising a tiled window (`SW_MAXIMIZE`) is the
  natural trigger for rule 29, but a maximised window reports the monitor
  rect and rejects `SetWindowPos` sizes; the adapter should restore it
  (`SW_RESTORE`) before the engine's re-land writes, and report the
  maximised rect to `WindowMoved` first so the grew flag fires.
- **Foreground lock**: `SetForegroundWindow` may be refused; `Focus` failing
  silently means no `WindowFocused` echo — harmless. The "engine activated
  key" (rule 41) prevents the echo from triggering a reveal when it does
  arrive.
- **Dockable** must be decided by the Win32 adapter (`WindowInfo.Dockable`):
  e.g. `WS_EX_TOOLWINDOW`, owned windows and dialogs → not dockable; a
  non-resizable window (no `WS_THICKFRAME`) → not dockable. With the #22
  minimum such windows are simply left alone.
- **Start order**: Windows handle strings sort ordinally but arbitrarily; the
  rule only needs determinism. Feeding `Start` in z-order would not change
  the result because the engine sorts.
- **Work-area aspect**: compute stageAspect/landscape from `rcWork` in DIPs
  minus the gap, the same rect the projection uses.

## Recommended scope split
Fits one change, but it is large. If the builder wants two steps:
(a) start, windowAdded, windowRemoved, arrangeAll, save triggers, focus
after landing (rules 1–20, 23, 34–36, 40–41, 43–46); (b) windowFocused
reveal, windowMoved (snap-back, insist learning, zoom/un-zoom), Move(toward)
(rules 21–22, 24–33, 37–39, 42). Both depend on #26's `ReLand` and
`WriteTargets`. Remembered-spot restore and hidden-list semantics stay with
#19; floating with #22.
