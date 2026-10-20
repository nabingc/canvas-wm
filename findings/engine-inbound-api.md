# Feature: engine-inbound-api

## Summary
The engine is one object per (display, virtual desktop). The app layer builds
it with an engine key, the display's work area, a settings record and the six
services, then hands it the display's full frame, the other displays' frames
and (optionally) a menu-bar bottom, and wires a few optional callbacks. From
then on the app drives it only by calling plain methods on the UI thread:
window lifecycle events, focus/activation, start/resume/suspend/release-all,
work-area changes, navigation commands, pointer/gesture input and a per-frame
`tick(now)`. The engine answers through synchronous callbacks (changed, needs
frames on/off, hand a window to another display, ask to carry a window out)
and through the renderer/window-system services. Display-set changes are not
delivered live: the app releases every window and rebuilds all engines.

## Behaviour

### A. Construction and wiring
1. **Constructor inputs**: engine key (string = display key + `|` + desktop
   id; desktop id `default` when the OS desktop is unknown), visible area
   (work area rect, y-down), policy record, window system, capture, clock,
   renderer, limit store, display store. Nothing else is passed in the
   constructor. (Construction-time loading of the saved layout is done by the
   app layer, see `findings/engine-interfaces.md` rule 7, and again by the
   engine's own `start`, rule 13.)
2. **Post-construction properties set by the app immediately after
   construction** (all settable, none passed to the constructor):
   - `displayFrame` (optional rect): the full frame of this engine's display,
     y-down global coordinates. Set once.
   - `menuBarBottom` (optional number): y of the bottom of the system menu bar.
     macOS-specific; read by the camera/overview code. Set once.
   - `otherDisplays` (list of rects): full frames of **every other** display,
     built by iterating all screens and skipping this engine's own screen.
     Set once.
   - callbacks (rule 24) and finally an initial `appActivated(frontmost pid,
     ownsDesktop)` call so the engine knows the current front app.
3. **Display set changes are not pushed into a live engine.** When the screen
   configuration changes, the app compares a display signature; if it changed
   it tears down overlays at once and, after a short debounce, calls
   `releaseAll` on every engine (into nil if that display survives, else into
   the main display's work area), discards all engines and builds new ones
   (which reload their saved layouts). So `displayFrame`/`otherDisplays` are
   effectively per-engine-lifetime constants.
4. **Work-area changes are pushed live** with `visibleAreaChanged(rect)`.
   The app recomputes each display's work area when it may have changed
   (dock/menu-bar/screen notifications). The engine ignores an equal rect;
   otherwise it stores it and re-frames (camera retargeted, motion that lands,
   i.e. real windows get rewritten).
5. **Policy is a mutable property** (`policy` setter, rule 22). The app sets a
   new value and may also call `redraw()`.
6. **Read-only inspectors** the app uses: `displayKey`, `visibleArea`,
   `stage` (rect of the stage area), `focusedKey` (optional window key),
   `inOverview`, `desktopHidden`, `desktopFramed`, `phase` (stage phase:
   desk, moving, overview, landing, fading), `holds(key)` (true if the key is
   a managed window or a view in the layout document), `otherTabs(of: key)`,
   `lastPlan`, `needsFrames`, `debugState` (text dump).

### B. Threading and re-entrancy
7. Every inbound method, every callback and every service completion runs on
   the UI (main) thread; the engine has no locks. Callers must marshal OS
   events to that thread before calling in.
8. Callbacks are invoked **synchronously from inside** engine methods. The
   observed callback handlers do one of: start/stop a frame timer, refresh
   app menus, or move a window to another engine. A callback may call into
   **other** engines (hand-off), but no observed handler calls back into the
   engine that raised it. Contract for CanvasWM: callbacks must not re-enter
   the calling engine synchronously; defer if needed.
9. Service completions (`readFrame`, `setFrame`, `after`) are never delivered
   re-entrantly inside the call that requested them (see engine-interfaces
   rules 10, 16, 17, 29).

### C. Lifecycle methods (the "frozen" and "adopted" flags)
The engine has two lifecycle flags: **adopted** (it has taken ownership of
windows: set by `start`, cleared by `releaseAll`) and **frozen** (its desktop
is not current: set by `suspend`, cleared by `resume`).

10. **`start(with: [WindowInfo])`** — take over the windows of a desktop that
    has just become current for the first time.
    - Idempotent: if already adopted, it does nothing.
    - Otherwise: schedules a periodic native-tab watcher via the clock;
      registers every given window; loads the saved display record for its
      engine key and **sanitises** the saved layout against the known
      windows (unknown views dropped); windows not in the saved layout are
      placed with the new-window placement rule (focused = none) or floated
      behind, as their saved state says; ensures a desktop node exists when
      needed; syncs navigation and history; sets adopted; computes target
      frames; then waits for first live pictures up to a deadline before
      writing frames ("start when captured"); records laid-out sizes,
      renders, saves.
    - The app also calls `start(with: [])` for an empty desktop.
11. **`suspend()`** — the engine's desktop stopped being current.
    - Idempotent: if already frozen, nothing.
    - Otherwise: cancels any interaction (move, gap drag, gesture), native
      drag and rectangle zoom; leaves overview; clears hover; **bumps the
      placement generation** (pending frame-write sequences are abandoned);
      snaps the camera to its target (no animation left); clears any layout
      tween; phase = desk; saves; renders; turns frames off (fires
      `onNeedsFrames(false)` if they were on); sets frozen; lowers capture
      rates to their resting values.
    - **It does not move any window**: windows stay where they are (the OS
      hides the inactive desktop's windows).
12. **`resume(with: [WindowInfo])`** — the desktop became current again.
    - Only acts when frozen; otherwise nothing.
    - Clears frozen, then reconciles with the given window list:
      a managed window absent from the list is removed permanently
      **unless it is parked** (parked windows are kept even if the OS did not
      report them); a listed window not yet managed is added (as
      `windowAdded(info, held: false)`); a listed window already managed has
      its stored info refreshed.
    - Then restores capture rates and re-frames with landing (real windows
      rewritten).
13. **`releaseAll(into: rect?, completion)`** — give every window back to the
    user (app quit, display rebuild).
    - Bumps the placement generation (pending writes abandoned).
    - Target area = `into`, or the engine's visible area when absent.
    - Every window that is **parked** (or whose resting place is off the
      visible stage) is moved back into the target area with a cascade:
      width = min(0.7 × area width, max(window width, 640)),
      height = min(0.7 × area height, max(window height, 420)),
      origin = (area.x + 60 + k, area.y + 40 + k) where k cycles
      0, 32, 64, … 224 then back to 0 (k = 32 × index, wrapped every 8
      windows). Its parked flag is cleared.
    - Windows that are already on-screen are **not** moved.
    - Stops capture for every window, empties the managed-window table,
      clears adopted, phase = desk, clears the interaction, renders an empty
      overlay plan.
    - `completion` runs once, after the last of those `setFrame` writes has
      reported back (a counter of outstanding writes, plus a run-once guard).
    - The app's quit path calls `releaseAll` on every engine, then waits up
      to **2 s** for all completions while still pumping the UI loop in short
      slices (~0.02 s), then saves each engine.
14. **`release(key)`** — stop managing one window (used for user
    exclusions). Unknown key: nothing. If the window is parked it is first
    written back on-screen (same rule as 16b) and unparked; then it is
    removed permanently (`windowRemoved(key, temporary: false)`).
15. **`save()`** — persist now (layout record to the display store). Also
    called internally after edits; the app calls it on quit.

### D. Window events
All window-event methods first check **frozen**; while frozen they are
ignored (except `appActivated`, rule 20, and `windowTitleChanged` /
`windowBecameDockable`, which only touch stored fields). Window keys that the
engine does not manage are ignored unless stated.

16. **`windowAdded(info, held)`** — a window appeared (or re-appeared).
    a. Frozen → ignored.
    b. If the key is already managed, the existing record is reused and its
       info overwritten (a duplicate add is an info refresh, not a second
       window). Otherwise a new record is created. The key is removed from the
       "temporarily away" table, and the remembered dock spot from that table
       (if any) is attached to the record.
    c. If the engine is not adopted yet (before `start`), the window is only
       recorded; no layout happens.
    d. If adopted: any running interaction is ended first, then the window is
       placed (tab group join for native tabs, saved slot, new-window
       placement next to the focused window, or float), the layout is
       written and saved. (Placement policy itself is feature #13 / #4.)
    e. `held` meaning: Unknown (the app passes `false` from desktop switching
       and carry-in paths).
17. **`windowRemoved(key, temporary)`** — a window disappeared.
    a. Frozen → ignored.
    b. The key is dropped from the guest set and the "leaving" set.
    c. If adopted and the key is managed: interactions are ended; if
       `temporary`, the window's place (dock spot / restore target) is
       remembered in the "away" table so a later `windowAdded` can put it
       back; the window is removed from the layout (hidden/untabbed/removed as
       appropriate).
    d. The record is removed from the managed table. If it was **parked**,
       the engine writes it back on-screen: same size as its last known
       frame, **centred in the visible area**, fire-and-forget; parked flag
       cleared.
    e. Capture is stopped for the key; its look/style entries and, if it was
       the focused key, the focused key are cleared.
    f. Unknown key: effectively nothing beyond the set clean-up.
18. **`windowFocused(key)`** — the OS reports this window got focus.
    Frozen or unknown key → ignored. Otherwise the engine may restore a
    minimised tab group, split a torn tab, update the focused key, raise a
    floating window, zoom/frame to the window if it is off view, select its
    tab, and render. (The camera reaction is feature #13/#15.)
19. **`windowMoved(key, frame)`** — the OS reports a new frame for a window.
    Frozen or unknown key → ignored. Writes the engine caused itself are
    recognised (expected frame) and absorbed; otherwise it is treated as a
    user move/resize (may hand the window to another display, join a tab
    group, or update a float). Details belong to the tiling loop and
    multi-display features.
20. **`appActivated(pid, ownsDesktop)`** — the frontmost application changed.
    Always records the active pid and the `ownsDesktop` flag (true when the
    activated app is the desktop/file-manager owner; Inferred), even when
    frozen. Expires a pending "split for new window" request older than about
    1 s. Not frozen: re-renders if the app's last focused window (the engine
    keeps pid → last focused key) differs from before, or if the "desktop
    has focus" state flipped.
21. **`windowTitleChanged(key, title)`** stores the title for a managed key
    and re-renders only when the stage is not at rest (phase ≠ desk), because
    titles are drawn only in overview/motion. **`windowBecameDockable(key)`**
    sets the record's dockable flag; no relayout. **`captureArrived(key)`**
    (a new live picture exists) advances a landing in progress and re-renders
    when the stage is not at rest and the overlay shows pictures.

### E. Policy
22. Setting `policy`:
    - Always mirrors `shareRemovedSpace` into the layout-edit module's global
      setting (it changes how removal redistributes space).
    - If the new value equals the old one, or the engine is not adopted:
      nothing else.
    - Else if `gap`, `fixedMinimum` or `tabPadding` changed → re-frame
      (relayout; real windows rewritten unless in overview).
    - Else → render only (overlay-only settings).
23. Policy fields and struct defaults (the app may override from settings):

    | Field | Type | Default | Use (Inferred unless noted) |
    |---|---|---|---|
    | gap | number | 10 | space between tiles (relayout trigger) |
    | gesturesLand | bool | false | gestures commit real frames |
    | overviewSurfaced | bool | false | overview presentation |
    | maxDockedTiles | int? | none | cap on tiled windows |
    | tabsAllowed | bool | true | tab groups enabled |
    | savedViewsAllowed | bool | true | saved views enabled |
    | collapseSmallGroups | bool | true | collapse tiny groups |
    | previewDrags | bool | false | drag shows previews |
    | deskGapHandles | bool | true | gap handles at rest |
    | fixedMinimum | bool | false | use fixed minimum tile size (relayout trigger) |
    | previewStyle | enum | case 1 | picture look |
    | tintOpacity | number | 0.72 | |
    | coverFade | number | 0.14 | |
    | coverIcon | bool | true | |
    | tabStyle | enum | case 0 | |
    | tabTint | number | 0 | |
    | tabPadding | number | 4 | tab strip height part (relayout trigger) |
    | tabCloseButtons | bool | true | |
    | tabLights | bool | false | |
    | tabCloseOnRight | bool | false | |
    | tabInset | number | 0 | |
    | windowCornerRadius | number | 10 | |
    | desktopEdge | number | 0.2 | |
    | tuckStrips | bool | false | |
    | dropHold | number | 0.15 | seconds |
    | reflowWhileDragging | bool | false | |
    | parkOutOfViewSmall | bool | false | see park-geometry |
    | interruptLandings | bool | false | |
    | proxyScale | enum | case 1 | |
    | resizeCover | enum | case 1 | |
    | blurRadius | number | 12 | |
    | keepPushed | bool | false | |
    | dimDocked | bool | true | |
    | dimOpacity | number | 0.25 | |
    | dimBlur | number | 16 | |
    | shareRemovedSpace | bool | true | (mirrored globally, rule 22) |
    | touchPinchPan | bool | true | |
    | soleFloatTakesDesktop | bool | true | |
    | squeezeBelowMinimum | bool | false | see layout-solver |

    Derived (computed, not stored): `tabStripHeight` and `tabLead`.

### F. Outbound callbacks (all optional, all synchronous)
24. | Callback | Signature | Fires when | App reaction (TrellisWM) |
    |---|---|---|---|
    | onChange | () | engine state shown in menus changed | refresh menus/status |
    | onNeedsFrames | (bool) | **only on transitions** of the "frames running" flag | true: create a per-display vsync timer that calls `tick(now)`; false: stop it |
    | onHandOff | (window record, dragged: bool) | a window was moved to another display (after it was removed here with temporary = false) | coordinator adds it to the target display's engine (`carryIn`) |
    | onCarryOut | (key) → bool | engine asks whether another display will take a window being dragged out | coordinator decides; true = accepted |
    | log | (text) | diagnostics | write to log |
    | onLocked | (gate) | **feature gate — out of scope** | — |
    | onDid | (accomplishment) | **lessons/onboarding — out of scope** | — |

25. Frames on: raised by the motion starters (begin motion, enter overview,
    begin gesture, drag moves, drop target, group leave, return-to-strip).
    When turned on, the engine stores the clock's `now` as the last tick
    time, then fires `onNeedsFrames(true)`. Frames off: fired from `tick`
    when nothing needs frames any more, from `suspend`, `releaseAll`,
    interrupted landings and the end of a fade; after turning off the engine
    schedules a re-check of window covers.

### G. Tick loop
26. **`tick(now)` takes an absolute time in seconds on the same monotonic
    clock as `IClock.Now`**, not a delta. The engine computes
    dt = now − last tick, stores `now` as the new last tick, steps the camera
    animation by dt, ends the layout tween once 0.46 s (`Feel.layoutEase`)
    has passed since it began, animates an active move session at `now`,
    advances landings/fades, refreshes overview shapes when the camera is at
    rest in overview, renders, and if nothing needs frames any more turns
    frames off (rule 25).
27. `needsFrames` is true while the stage phase is moving or landing, or a
    layout tween is running, or the camera has not reached its target, or a
    move session or continuous gesture interaction is active.
28. The app's frame driver passes the host's current time to `tick` once per
    display refresh (TrellisWM: one display-link per display, created on
    `onNeedsFrames(true)`, destroyed on false). It also measures late frames
    for diagnostics only.

### H. Command and input methods (attach points for later features)
29. Navigation / view commands (called from hotkeys, menus, gestures):

    | Method | Meaning |
    |---|---|
    | toggleOverview() | open or close the overview of everything |
    | showEverything() | frame the whole layout |
    | stepOut() | move the camera one tree level up |
    | stepIn(toward: point) / stepIn(into: id) | one level down, toward a point or into a node |
    | move(toward: edge) | focus/frame the neighbour on that side |
    | back() / forward() | camera history |
    | zoomIn(toward: point?) / zoomOut() / zoomInto(id) | zoom steps |
    | toggleMaximize(key?) | frame one window alone / undo |
    | showDesktop() / setDesktopHidden(bool) / toggleDesktop() / toggleDesktopHidden() | desktop node visibility/framing |
    | splitForNewWindow(of: key) | reserve a split next to a window for the next new window |
    | goToFraming(n) / saveFraming(n) / forgetFraming(n) | saved views 1–9 |
    | arrangeAll() / floatAll() | rebuild tiling / float everything |
    | toggleDocked(key) / toggleTabDocked(key) | float ↔ tile one window/tab |
    | overviewKey(key, shift) | keyboard navigation inside overview |
    | redraw() | re-render (after settings change) |

30. Pointer / gesture input (app translates raw OS events):
    `scroll(dx, dy, precise, zoomModifier, at, phase, momentumEnded)`,
    `pinch(by, at, phase, option, overWallpaper)`,
    `touchPinchPan(zoom, around, dx, dy)`, `touchGestureEnded()`,
    `openOverviewByTouch()`, `beginRectZoom(at)` (then move/end),
    overview pointer down/moved/dragged/up, preview press/drag/release/
    resize/double-click, tab press/drag/release, gap drag begin/move/end,
    native window drag tracking (`nativePointerDragged`, `nativeMouseUp`),
    title-bar double-click, collapsed-group double-click,
    `groupClosing(key)`, `groupMinimising(key)`, `tabLeaving(key)`,
    `handOverDrag()`, `carryIn(info, lifted)`, `leftDisplay(...)`.
    The engine never sees key codes; `GesturePhase` is a began/changed/ended
    style enum.

## Data model
- `EngineKey` = DisplayKey + `|` + desktop id (`default` when unknown) —
  already in `CanvasWm.Engine.Services.Keys`.
- `WindowInfo` (key, pid, appName, appId?, title, frame, dockable) — exists.
- Engine construction input: key, visibleArea, policy, the six services.
- Engine display context (settable once): displayFrame?, otherDisplays[],
  menuBarBottom? (not needed on Windows; keep optional).
- `Policy`: record with the fields in rule 23.
- `StagePhase`: desk | moving | overview | landing | fading.
- Lifecycle flags: adopted, frozen; generation counter; "away" table
  key → remembered dock spot; guest set (keys of windows visiting from
  another display; set by the app; Inferred).
- Callbacks: `OnChange()`, `OnNeedsFrames(bool)`, `OnHandOff(window, dragged)`,
  `OnCarryOut(key) → bool`, `Log(text)`.

## Worked examples
1. Duplicate add: adopted engine manages `w1` (title "A"); `windowAdded(w1
   info with title "B", false)` → still one managed window, title "B"; no
   second tile.
2. Remove unknown: `windowRemoved("w9", false)` where w9 not managed → no
   service calls, no save.
3. Events while suspended: after `suspend()`, `windowAdded(w2)`,
   `windowRemoved(w1)`, `windowFocused(w1)`, `windowMoved(w1, r)` → all
   ignored; `appActivated(42, false)` → active pid recorded, no render.
4. `suspend()` twice → second call has no effect; `onNeedsFrames(false)`
   fires at most once (only if frames were on).
5. `resume([w1])` when not suspended → no effect.
6. `resume([w1, w3])` after suspend, managed {w1, w2 (not parked),
   w4 (parked)} → w2 removed permanently, w4 kept, w3 added, w1 info
   refreshed; then a landing re-frame.
7. `start([...])` twice → second call no effect.
8. Removed parked window: visible area (0, 25, 1440, 875), parked window
   last size 800×600 removed → `setFrame(key, (320, 162.5, 800, 600))`,
   then capture stopped.
9. `releaseAll(nil)` with visible area (0, 25, 1440, 875) and three parked
   windows of sizes 500×300, 1400×900, 900×700:
   - #0 → (60, 65, 640, 420)
   - #1 → (92, 97, 1008, 612.5)   [0.7 × 1440 = 1008, 0.7 × 875 = 612.5]
   - #2 → (124, 129, 900, 612.5)
   On-screen windows untouched; completion fires after the third
   `setFrame` completes. 9th parked window uses k = 0 again.
10. Policy: change `tabTint` only → render, no frame writes. Change `gap`
    10 → 12 on an adopted engine not in overview → re-frame with landing.
    Any change on a not-yet-started engine → stored, nothing else.
11. Frames: engine at rest; a motion starts at clock now = 100.0 →
    `onNeedsFrames(true)` once; `tick(100.016)` → camera stepped by 0.016;
    when motion ends inside a tick → `onNeedsFrames(false)` once.
12. `visibleAreaChanged(same rect)` → nothing; different rect → re-frame.

## Evidence
- Rule 1: `Coordinator.makeWorld` (`evidence/dec-makeWorld.json`; `Engine.init`
  call; literal `default` desktop id in `Coordinator.showCurrentSpaces`
  `evidence/dec-engine-inbound-start-coordinator.json`).
- Rule 2: `dec-makeWorld.json` — after init: screen frame → `displayFrame`
  (field 0x40), `Screens.menuBarBottom` → field 0x68, loop over screens
  collecting frames → `otherDisplays` (0x78), callback stores, `appActivated`.
  Field offsets: `evidence/engine-field-offsets.txt`.
- Rule 3: `Coordinator.screensChanged` 0x100164d74 and its closure #1
  0x10016ea88 (`evidence/dec-screensChanged-rebuild.json`); notes in
  `evidence/engine-inbound-notes.txt`.
- Rule 4: `Engine.visibleAreaChanged` 0x10003b4c8; `Coordinator.refreshUsableAreas`
  0x10016de7c (`dec-engine-inbound-lifecycle2.json`, `dec-engine-inbound-start-coordinator.json`).
- Rules 5–6: `evidence/engine-inbound-direct-calls.txt`; Engine getters
  (`symbols/all-defined.demangled.txt`), `StagePhase.rawValue` 0x1000ca8b0
  (objdump), `Engine.holds` 0x1000ed234.
- Rules 7–9: engine-interfaces rule 10; synchronous callback calls in
  `Engine.setFrames` 0x10003b41c, `Engine.handOff` 0x1000f2310; makeWorld
  closures #7, #10–#13 (`engine-inbound-notes.txt`).
- Rule 10: `Engine.start(with:)` 0x1000336ec
  (`dec-engine-inbound-start-coordinator.json`): adopted guard (0x2a0),
  `watchNativeTabs`, `sanitize`, `placeNew(focused: nil)`, `floatBehind`,
  `startWhenCaptured`, `recordLaidOut`, `render`, `save`.
- Rule 11: `Engine.suspend` 0x1000e9824 (`dec-engine-inbound-lifecycle2.json`).
- Rule 12: `Engine.resume(with:)` 0x1000e9b8c (same file).
- Rule 13: `Engine.releaseAll` 0x10003a768 and closures 0x10003b348,
  0x10003b3b8 (same file); constants from objdump at 0x10003acac–0x10003ad78;
  `Coordinator.stop` 0x10015adf8 (2.0 s wait, ~0.02 s run-loop slices).
- Rule 14: `Engine.release` 0x1000f01a4 (same file).
- Rule 16: `Engine.windowAdded` 0x1000eb700 (`evidence/dec-engine-inbound-lifecycle.json`):
  frozen guard (0xf80), dictionary reuse, `away` removal (0xf90), adopted check.
- Rule 17: `Engine.windowRemoved` 0x1000ea030 (same file); parked restore
  asm 0x1000eaa94–0x1000eab28.
- Rules 18–19: `Engine.windowFocused` 0x1000f0504, `Engine.windowMoved`
  0x1000f1950 (same file) — guards Observed, bodies only skimmed.
- Rule 20: `Engine.appActivated` 0x1000f14f0 (same file).
- Rule 21: `Engine.windowTitleChanged` 0x1000f03fc, `windowBecameDockable`
  0x1000f033c (same file), `captureArrived` 0x10003e6cc (lifecycle2).
- Rule 22: `Engine.policy.didset` 0x10002a1ac (`engine-inbound-notes.txt`).
- Rule 23: `Policy.init()` 0x100091720 and constants at 0x1002126a0
  (objdump); getter offsets (objdump of each `Policy.*.getter`); types from
  demangled getters.
- Rule 24: Engine callback property types (demangled setters 0x10002b4f8 …);
  makeWorld closures.
- Rule 25: `Engine.setFrames(_:)` 0x10003b41c and its callers (objdump scan
  of `bl setFrames`); inlined "frames off" in tick/suspend/releaseAll/
  interruptLanding/fade closure.
- Rules 26–27: `Engine.tick` 0x10003b85c (asm: lastTick 0xb60, `fsub` now −
  last before `CameraCurve.step`), tween 0.46 s compare;
  `Engine.needsFrames.getter` 0x10003b730.
- Rule 28: `FrameTarget.tick` 0x10015dd54 (`body(CACurrentMediaTime())`),
  makeWorld closure #12.
- Rules 29–30: `symbols/spaces-api.txt` (Engine method list) and
  `engine-inbound-direct-calls.txt`; study §3.2.

## Confidence
| Rule | Label |
|---|---|
| 1 constructor inputs, `default` desktop id | Observed |
| 2 post-construction properties | Observed (stores in makeWorld); `otherDisplays` excludes own screen — Observed loop, exact exclusion test Inferred |
| 3 rebuild on display change, release into main if display gone | Observed calls; debounce values 0.4/1.5 s seen, which applies when Inferred |
| 4 visibleAreaChanged equality guard + re-frame | Observed |
| 5–6 | Observed names; meanings of some inspectors Inferred |
| 7 main-thread only | Observed (completions/timers on main); "no locks" Inferred |
| 8 callbacks synchronous | Observed; "no re-entry into same engine" Inferred from the handlers read |
| 9 | Observed (engine-interfaces) |
| 10 start | Observed guard and call sequence; ordering details Inferred |
| 11 suspend | Observed |
| 12 resume incl. parked windows kept | Observed |
| 13 releaseAll cascade numbers, completion counting | Observed; exact selection of which windows are moved (parked vs off-stage) Inferred |
| 13 quit wait 2 s / 0.02 s | Observed constants; 0.02 meaning Inferred |
| 14 release | Observed |
| 16 a–c | Observed; d Inferred from callees; e Unknown |
| 17 a–e | Observed (c's "temporary → remember" Inferred from the away-table write) |
| 18–19 | Guards Observed; effects Inferred from callee names |
| 20 | Observed stores and render conditions; `ownsDesktop` meaning Inferred; 1 s expiry Observed constant |
| 21 | Observed |
| 22 | Observed |
| 23 | Observed types, offsets and defaults; uses Inferred; enum case names Unknown |
| 24 | Observed signatures; meanings Inferred from handlers |
| 25 | Observed |
| 26 tick takes absolute time | Observed |
| 27 | Observed (interaction tag meanings Inferred) |
| 28 | Observed |
| 29–30 | Observed names; one-line meanings Inferred |

## Unknowns
- **`held` in `windowAdded(info, held)`**: all observed callers pass false;
  the tracker path that may pass true was not traced.
- **Exactly which windows `releaseAll` moves**: the parked flag clearly
  selects; a second condition from the resting placement (two placement
  flags) and membership in the layout also participate. Safe CanvasWM rule:
  move every window that is parked or whose last written frame is mostly
  off all displays.
- **`otherDisplays` contents on mirrored displays / other Spaces**: built
  from the OS screen list minus the own screen; mirroring not examined.
- **Who sets `frozen` / `guests` from outside** (setters exist; no direct
  app call found in the direct-call scan).
- Policy enum case names (previewStyle, proxyScale, resizeCover, tabStyle)
  and the exact meaning of most overlay policy fields.
- `windowFocused` / `windowMoved` full behaviour (feature #13 and
  multi-display features).
- The quit path's `into` argument for `releaseAll` (nil vs main display).
- Feature gate (`onLocked`, `Gate`, `blockedGate`, `permitted(_:for:)`) and
  lessons (`onDid`, `Accomplishment`): identified by name only and
  deliberately not analysed. Nothing else in this API touched licensing.

## Windows notes
- **UI thread**: run the engine on the app's dispatcher thread; WinEvent hook
  callbacks (out-of-context) already arrive on the hooking thread's message
  loop — keep that the UI thread; marshal anything from worker threads.
- **Frame driver**: on `OnNeedsFrames(true)` start a per-monitor (or single)
  vsync-paced loop (`DwmFlush` loop on a dedicated thread posting to the UI
  thread, or `CompositionTarget.Rendering` in WPF); call `Tick(clock.Now)`
  with the **same** `IClock` time base (QueryPerformanceCounter seconds).
  Stop on false. Do not pass a delta.
- **Display context**: `displayFrame` = `MONITORINFO.rcMonitor`, `visibleArea`
  = `rcWork`, `otherDisplays` = rcMonitor of all other monitors, all in the
  Core DIP space; `menuBarBottom` not needed (null).
- **Work-area change**: `WM_SETTINGCHANGE(SPI_SETWORKAREA)` and taskbar
  auto-hide/position changes → `VisibleAreaChanged`.
- **Display change**: `WM_DISPLAYCHANGE` / `WM_DPICHANGED` on the app window
  → debounce, `ReleaseAll` (into the surviving monitor's work area or the
  primary's), rebuild engines. Because Windows lets windows stay off-screen
  forever, releasing parked windows on monitor removal is essential.
- **Virtual desktops**: `IVirtualDesktopManager` gives a desktop id per
  window but no public "current desktop changed" event; poll the foreground
  window's desktop id or use the Explorer-undocumented notification. Map
  desktop switch → `Suspend` old engine / `Resume` or `Start` new one.
  Windows does hide inactive-desktop windows (cloaking), matching rule 11's
  "do not move windows on suspend".
- **Quit**: `ReleaseAll` on every engine and pump the dispatcher until all
  completions arrive or ~2 s pass; also on session end (`WM_ENDSESSION`) and
  unhandled-exception handlers.
- **appActivated**: `EVENT_SYSTEM_FOREGROUND` → pid via
  `GetWindowThreadProcessId`; `ownsDesktop` ≈ foreground is Explorer's
  desktop (`Progman`/`WorkerW`).
- **Callback re-entrancy**: hand-off between engines must not synchronously
  re-enter the source engine; post if necessary.

## Recommended scope split
- **#23 engine-inbound-api** (this): `Engine` class shell with constructor,
  display context properties, `Policy` record with defaults, callbacks,
  lifecycle state machine at contract level (adopted/frozen flags, guards and
  idempotence of start/suspend/resume/releaseAll/release, the windowAdded
  duplicate refresh and the frozen guards, "away" table, parked-window
  restore on removal, releaseAll cascade + completion counting), policy
  relayout triggers, `SetFrames`/`OnNeedsFrames` edge triggering and the
  `Tick(now)` contract with `NeedsFrames`, `VisibleAreaChanged` guard, and
  no-op stubs for the navigation/pointer commands (names only).
- **#13 engine-tiling-loop**: what `start`/`windowAdded`/`windowRemoved`/
  `windowFocused`/`windowMoved` do to the layout and frames (placement,
  write targets, refusal handling, saving), `arrangeAll`, `move(toward:)`
  wiring to directional focus.
- **#15/#16 camera/overview**: tick body (camera curve, tweens, landing,
  overview), `needsFrames` conditions beyond the flag, scroll/pinch/zoom,
  history, saved views.
- **Multi-display feature (new, suggest)**: hand-off/carry-in/carry-out,
  `guests`, display rebuild orchestration in App.
- **Native tabs / tab grouping (#20 + new)**: tab-press/drag methods,
  native-tab watcher.
