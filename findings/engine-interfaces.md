# Feature: engine-interfaces

## Summary
TrellisWM's engine reaches the operating system only through six small
injected services (window system, capture source, clock, renderer, limit
store, display store). This finding pins down every member of each service:
its inputs, outputs, threading and failure behaviour. It also describes the
opposite direction: how the app layer feeds the engine (window lifecycle,
focus, hotkeys, pointer/gestures) by calling ordinary engine methods, and how
the engine calls back out through a handful of optional callbacks. Windows are
identified everywhere by an opaque string key; displays by an opaque string
key. Recommendation: this is really two features — (a) the six outbound seams
plus test fakes, and (b) the inbound engine command/event API. See Unknowns
and the split note at the end.

## Behaviour

### A. Identity keys
1. **Window key** is an opaque string. On macOS it is the letter `w` followed
   by the OS window number in decimal (e.g. window 4211 → `"w4211"`). The
   engine never parses it; it is only compared, hashed and handed back to the
   services.
2. **Display key** is an opaque string. The macOS adapter uses the display's
   stable hardware UUID string; if no UUID is available it uses `display-`
   followed by the numeric display id.
3. **Engine key** (the `displayKey` passed to the engine) is the display key,
   a `|`, then an identifier of the OS virtual desktop ("Space"). So there is
   one engine per (display, virtual desktop) pair, not one per display.
4. **Application identity** for the limit store is the app's bundle
   identifier, which may be absent. Absent identity means "no learning, no
   lookup" (rules 30–32).

### B. Engine construction
5. The engine is constructed with: engine key (string), visible area (work
   area of the display, a rect in y-down top-left coordinates), policy (a
   settings record), and the six services. The coordinator creates it lazily
   when a (display, desktop) world is first needed.
6. Shared vs per-engine services: the window system, capture source, clock,
   limit store and display store are single app-wide instances shared by all
   engines; the **renderer is per display/world** (each world gets its own
   overlay renderer for its screen).
7. Before constructing, the coordinator loads the saved layout for the engine
   key from the display store (with a fallback lookup by the bare display
   key, apparently for migration from an older per-display scheme).
8. Policy is **mutable at runtime**: the app assigns a new policy value to a
   live engine when settings change and then asks it to redraw. The engine is
   not rebuilt.
9. The visible area can change at runtime via an engine method
   (`visibleAreaChanged(rect)`), called by the coordinator on screen/dock
   changes.

### C. Threading model
10. The engine is single-threaded and lives on the UI (main) thread. Every
    completion and scheduled callback a service delivers back to the engine is
    delivered on that thread.
11. Animation is not driven by the clock. The engine raises "I need frames"
    (callback `onNeedsFrames(bool)`); the app then runs a per-display vsync
    timer and calls the engine's `tick(dt)` each frame until the engine says
    it no longer needs frames.

### D. Window system service (8 methods/properties)
12. `focus(key)` → nothing. Fire-and-forget: activates the window's app and
    raises/focuses the window asynchronously on a per-application background
    queue. Before doing so the adapter broadcasts an internal "raising"
    notification (so overlays can react). Unknown key: no effect. No result
    or error is reported back; the engine learns about focus changes only via
    the inbound `windowFocused` path (rule 41).
13. `frontToBack()` → ordered list of window keys, frontmost first. Built from
    the OS z-ordered window list (all layers except desktop elements,
    including off-screen windows of the current desktop), mapped to keys and
    filtered to **windows the tracker currently manages**; unmanaged windows
    are dropped. Synchronous.
14. `isShowing(key)` → bool. True if the window is currently on screen (in the
    OS "on-screen windows" list). Unknown key → false. The on-screen set is
    cached and re-read at most about every 0.1 s. Synchronous.
15. `visibleFraction(rect)` → number in [0, 1]. Takes a **rect, not a key**:
    the area of the rect that lies on any display (sum over all display full
    frames, y-down) divided by the rect's area; 0 for an empty rect.
    Synchronous, pure geometry on current screens (detail in
    `findings/park-geometry.md` rule 19).
16. `readFrame(key, completion)` → nothing; asynchronous. Reads the window's
    current frame on that application's background queue and calls
    `completion(rect-or-null)` on the UI thread. Unknown key → completion
    with null (still asynchronous, posted to the UI thread). Null also means
    the read failed.
17. `setFrame(key, rect, completion)` → nothing; asynchronous. Requests a new
    frame and later calls `completion(actual-rect-or-null)` on the UI thread,
    where the rect is the frame **read back after the write** (the app may
    have clamped it). Unknown key → completion(null). Details:
    - Writes for one window are serialised on a dedicated per-window queue;
      different windows are written in parallel. There is **no batch API**;
      the engine issues one call per window.
    - The adapter remembers the last requested target and increments a
      per-window "writes in flight" counter (used to recognise self-caused
      move notifications; Inferred).
    - If the target is mostly off-screen (visible fraction below about 0.1),
      the write is flagged as an off-screen/park write; the macOS write path
      then re-pushes position if the window still shows more than 2 % after
      the write (corrects the study §4 description of the 0.02 constant;
      Inferred).
    - **Refusal is not signalled by the service.** The engine detects refusal
      itself by comparing the returned actual frame with its target (tolerance
      and retry budget from `Feel.refusalSlack/insistCount/insistWindow`).
18. `tabSibling(key)` → key-or-null. Returns another managed window that is
    the given window's native OS tab sibling (same app, same frame), or null.
    Used by the engine to discover native tab groups.
19. `pointer` (property) → point. Current mouse position in the same y-down
    global coordinates as all rects.
20. `mouseDown` (property) → bool. True while the primary (left) mouse button
    is held.
21. `parkingOrigin` (property) → point. A fallback location for parked
    windows, used only when the engine has no display frame (see
    `findings/park-geometry.md` rule 13: bottom-right corner of the right-most
    screen, inset 1; (10000, 10000) with no screens).

### E. Capture source service (5 methods)
22. `start(key)` → nothing. Registers the window for live-picture capture and
    takes a first snapshot immediately.
23. `stop(key)` → nothing. Unregisters the window and discards its stored
    picture, rate and pending flags.
24. `setRate(key, fps)` → nothing. Sets the capture rate in frames per second;
    stored as an interval 1/fps. Ignored if fps ≤ 0 or the window was not
    started. If the rate actually changes, a fresh snapshot may be taken at
    once. The engine supplies the `Feel.pictureRate*` values here (so those
    constants are fps: 10, 3, 0.2, 0.167, 0.033, 0.0167 — i.e. down to one
    picture per minute for "away" windows; Inferred from this use).
25. `hold(key, on)` → nothing. Toggles a per-window "held" mark; when the mark
    actually changes, a re-capture is scheduled a moment later. The engine
    uses it for windows whose picture is standing in for the real window
    (e.g. while it is parked or being resized under a cover). Exact effect of
    the mark on capture scheduling is Unknown (rule-level semantics Inferred).
26. `latest(key)` → frame-or-null. Synchronous. The most recent captured
    picture's descriptor: a **size** (pixels/points of the picture) and a
    **serial** (integer, increases with every captured picture across all
    windows). The engine sees only these two fields; the pixel data stays in
    the app layer and is drawn by the renderer.
27. Capture is pull-by-timer on macOS: a repeating timer checks each started
    window and snapshots it when its interval has elapsed, with at most about
    four snapshots in flight. When a picture arrives the app calls the
    engine's `captureArrived(key)` (inbound), so the engine can re-plan.
    Without screen-capture permission nothing is captured (latest stays null).

### F. Clock service (2 members)
28. `now` (property) → seconds as a double, from a **monotonic** clock
    (not wall time; arbitrary epoch).
29. `after(seconds, action)` → nothing. Runs `action` once on the UI thread
    after the delay. **No cancellation handle**; the engine guards stale
    callbacks itself (e.g. by checking a generation counter or state when the
    callback fires; Inferred).

### G. Limit store service (3 methods), keyed by application identity
30. `minimum(appId?)` → size-or-null. Absent appId → null. Otherwise the
    stored learned minimum for that app, or null if none.
31. `learn(minimum: size, appId?)` → nothing. Absent appId → no-op. Raises the
    stored value: new = per-axis maximum of (stored, or 0×0 if none) and the
    given size. Persisted immediately.
32. `observe(size, appId?)` → nothing. Absent appId or nothing stored → no-op.
    If the observed actual window size is smaller than the stored minimum by
    **more than 2** on either axis, the stored value is lowered to the
    per-axis minimum of (stored, observed). Otherwise no change. Persisted
    immediately. (Engine log text indicates this is "the app's minimum came
    down".)
33. Persistence: one dictionary appId → size in user preferences. Engine-side
    `WindowLimits` per window are seeded from this store and can also come
    from probing (see `findings/layout-solver.md` rule 33).

### H. Display store service (2 methods), keyed by engine key
34. `load(display: key)` → saved-display-or-null.
35. `save(savedDisplay, display: key)` → nothing. Updates an in-memory cache
    and writes the record as JSON to
    `<app data folder>/arrangement-<key>.json`. Write errors are swallowed.
36. Saved-display record fields: the layout document; the framed node id
    (string); saved views ("framings": number-as-string → list of node ids);
    window owners (window key → owner string, used to tell which engine a
    window belongs to across desktops); optional dock spots (window key →
    restore target + neighbouring leaf ids); optional float sizes (window key
    → size).
37. When saved: the engine calls save after every committed layout edit
    (end of move, end of gap drag, tab release, join/split tab, float/dock
    toggles, arrange all, float all, desktop hide/show, saved-view
    save/forget, window added/removed, float settled), on start, on suspend
    (desktop switched away), and the coordinator calls it on app stop.
    When loaded: by the coordinator when creating the engine (rule 7), and by
    the coordinator to decide which engine "owns" a returning window.

### I. Renderer service (1 method)
38. `render(plan)` → nothing. Synchronous; the engine hands over a complete
    immutable description of everything to draw on that display's overlay and
    the renderer diffs/draws it. The plan contains (neutral names):
    - per-window entries: key, title, frame, focused, highlighted, lifted,
      moving, movable, resizable, covered, alpha, a look/style enum, the size
      it was laid out at;
    - tab strips (panel id, frame, tabs, selected, focused, floating, tucked,
      lifted, live, close-button flags, dragged tab and its x);
    - collapsed groups (id, frame, lines), gap handles (split id, index, axis,
      rect, gap, adjacent leaf ids);
    - optional dims (region, hole, opacity, blur, fade, edge, focused) for
      stage and desktop;
    - optional drop slot / live drop preview / tab target / space highlight
      rects, hover (rect, title, key), marquee (rect, label, candidate), HUD
      (path, framing rect, aspect, tiles);
    - global flags: stage phase, cursor style, in-motion, interactive,
      passes-input, behind-draws, picture look (preview style, proxy scale,
      cover style, fade, icon, tint, blur), tab style/tint/lights, and a set
      of window keys whose strips leave with them.
    For the interface feature only the method shape matters; the plan type
    can start as a small record and grow with the overlay features.

### J. Inbound: how the app layer drives the engine
39. **Input is not a service.** Hotkeys, the global mouse/keyboard tap,
    trackpad gestures and scroll arrive in the app layer and are translated
    into direct engine method calls on the active (or pointer's) engine.
    Observed direct calls include: toggle overview, step in/out, move toward
    edge, back/forward, toggle maximize, show desktop / set desktop hidden,
    split for new window, go to / save saved view, arrange all, float all,
    zoom in/out, scroll(dx, dy, precise, zoomModifier, at, phase,
    momentumEnded), pinch(by, at, phase, option, overWallpaper), touch
    pinch-pan, begin rect zoom, overview key handling.
40. Policy for "what a key does" therefore lives in the app layer's binding
    table plus engine methods; the engine never sees key codes.
41. **Window lifecycle** comes from the app's window tracker (OS
    notifications per application) through the coordinator into engine
    methods: `windowAdded(info, held)`, `windowRemoved(key, temporary)`,
    `windowFocused(key)`, `windowBecameDockable(key)`,
    `windowMoved(key, frame)`, `windowTitleChanged(key, title)`,
    `appActivated(pid, ownsDesktop)`, plus `start(with: infos)`,
    `resume(with: infos)`, `suspend()` when a desktop becomes current or
    stops being current, and `releaseAll(into, completion)` on quit.
42. The window description passed in (`info`) has: key, process id, app name,
    app identity (optional), title, frame, and a "dockable" flag (whether the
    window is eligible for tiling; Inferred meaning).
43. **Outbound callbacks** (optional closures the app sets on the engine):
    something changed (refresh menus), needs frames (rule 11), hand-off of a
    window to another engine (window, dragged flag), carry-out request
    (key → accepted?), an "accomplishment" notification and a "gate"
    notification. The last two relate to onboarding lessons and feature
    gating (licensing) and are out of scope.
44. **Private window-server call**: the only use of the private
    connection-property call sets a "cursor may be changed while app is in
    background" property, so the overlay can show resize/grab cursors
    without being frontmost. It is an app-layer detail, not an engine seam.

## Data model
- `WindowKey`: opaque string. `DisplayKey`/`EngineKey`: opaque strings.
- `AppId`: optional string (bundle id on macOS; executable path or AUMID on
  Windows).
- `WindowInfo`: key, pid, appName, appId?, title, frame, dockable.
- `CaptureFrame`: size, serial (int).
- `SavedDisplay`: doc, framed, framings (map string → list of ids), owners
  (map key → string), dockSpots? (map key → {restore target, neighbour leaf
  ids}), floatSizes? (map key → size).
- `StagePlan` (render input): see rule 38; start minimal.
- Service shapes (neutral, CanvasWM-oriented):

| Service | Member | In | Out | Sync? |
|---|---|---|---|---|
| WindowSystem | Focus | key | — | fire-and-forget |
| | FrontToBack | — | list of keys | sync |
| | IsShowing | key | bool | sync (cached ≤0.1 s) |
| | VisibleFraction | rect | double 0..1 | sync |
| | ReadFrame | key, callback(rect?) | — | async, callback on UI thread |
| | SetFrame | key, rect, callback(rect?) | — | async, callback on UI thread |
| | TabSibling | key | key? | sync |
| | Pointer | — | point | sync |
| | MouseDown | — | bool | sync |
| | ParkingOrigin | — | point | sync |
| Capture | Start / Stop | key | — | sync call, async effect |
| | SetRate | key, fps | — | |
| | Hold | key, bool | — | |
| | Latest | key | CaptureFrame? | sync |
| Clock | Now | — | seconds (monotonic) | sync |
| | After | seconds, action | — (no handle) | callback on UI thread |
| Renderer | Render | StagePlan | — | sync |
| LimitStore | Minimum | appId? | size? | sync |
| | Learn | size, appId? | — | sync, persists |
| | Observe | size, appId? | — | sync, persists |
| DisplayStore | Load | engineKey | SavedDisplay? | sync |
| | Save | SavedDisplay, engineKey | — | sync, persists |

## Worked examples
- Window key: OS window number 4211 → key `"w4211"`; the engine treats it as
  an opaque string (CanvasWM can use e.g. the HWND in hex; only equality
  matters).
- Engine key: display UUID `"ABC"` on desktop `"7"` → `"ABC|7"`.
- `frontToBack` with OS z-order [A, X, B] where X is unmanaged → [A, B].
- `isShowing("w9")` when w9 is not managed → false.
- `readFrame("w9")` when w9 is not managed → callback(null), delivered later
  on the UI thread (not re-entrantly inside the call).
- `setFrame("w1", (0,0,800,600))`, app clamps to 900 wide → callback with
  (0,0,900,600); service raises no error.
- LimitStore, app `"com.x"`:
  - nothing stored; `learn(500×300)` → stored 500×300.
  - stored 500×300; `learn(400×350)` → stored 500×350.
  - stored 500×350; `observe(499×349)` → unchanged (within 2).
  - stored 500×350; `observe(450×360)` → stored 450×350.
  - nothing stored; `observe(100×100)` → still nothing stored.
  - appId absent: `learn` / `observe` no-op, `minimum` → null.
- Clock: `now` = 1000.0; `after(0.25, f)` → f runs once at now ≥ 1000.25 on
  the UI thread; there is no way to cancel it.
- Capture: `start("w1")`, `setRate("w1", 10)` → interval 0.1 s;
  `setRate("w1", 0)` → ignored; `stop("w1")` → `latest("w1")` = null.
  Serial of a later picture is greater than any earlier picture's serial.
- DisplayStore: `save(rec, "ABC|7")` then `load("ABC|7")` → equal record;
  `load("unknown")` → null.
- Fakes for tests (CanvasWM): a fake window system that records SetFrame
  calls, completes them when the test pumps a queue, and can be told to clamp
  sizes; a manual clock whose `Advance(dt)` fires due `After` actions in time
  order; in-memory limit/display stores implementing rules 30–35; a recording
  renderer that keeps the last plan.

## Evidence
- Rules 1, 42: `WindowTracker.admit` 0x1002068fc — key built from literal
  `w` (asm 0x100206a6c) plus window id description; `WindowInfo.init(key:pid:
  appName:bundleID:title:frame:dockable:)`. Files:
  `evidence/dec-engine-seams-windowsystem.json`, `evidence/engine-seam-signatures.json`.
- Rule 2: `Coordinator.buildDisplays` — `evidence/dec-buildDisplays.json`
  (UUID string, `display-` fallback).
- Rules 3, 5–7: `Coordinator.makeWorld(_:space:)` 0x100168fb8 — `|` join,
  `JSONDisplayStore.load`, `StageRenderer.init` per world, `Preferences.policy`,
  `Engine.init` — `evidence/dec-makeWorld.json`; init signature in
  `evidence/engine-seam-signatures.json`.
- Rule 8: `Engine.policy.setter` and `Engine.redraw()` called from
  `AppDelegate` — `evidence/engine-inbound-direct-calls.txt`.
- Rule 9: `Engine.visibleAreaChanged(_:)` called from Coordinator — same file.
- Rules 10–11: completions dispatched to main queue in readFrame/setFrame;
  `MacClock.after` uses main queue (asm 0x10018ae84);
  `Engine.onNeedsFrames`, `Engine.tick(_:)`, `Coordinator.World.link`
  (display link), `FrameTarget.tick` — `engine-seam-signatures.json`.
- Rule 12: `MacWindowSystem.focus` 0x100187024 (per-pid queue, "raising"
  notification) — `dec-engine-seams-windowsystem.json`.
- Rule 13: `MacWindowSystem.frontToBack` 0x100187454, window-list option 0x10,
  closure #1 0x100187604 (tracker lookup) — same file and
  `dec-engine-seams-capture-stores.json`.
- Rule 14: `MacWindowSystem.isShowing` 0x100188430, `readOnScreen` 0x10018a954
  (option 0x11), cache field `onScreenRead (time, ids)`, 0.1 constant.
- Rule 15: witness 0x1001885b8 → `Screens.visibleFraction(of:)`;
  `findings/park-geometry.md`.
- Rules 16–17: `readFrame` 0x1001868bc, `setFrame` 0x100185824, `writeQueue`
  0x100185514 (queue label prefix per window key), `TrackedWindow.writesInFlight`;
  `evidence/dec-noeui.json` (visible-fraction > 0.02 re-push); refusal
  handling via `Feel` constants (study §3.1).
- Rule 18: `MacWindowSystem.tabSibling` 0x1001877e0, `sameFrameSibling`.
- Rules 19–21: witnesses 0x1001885e0 (pointer → mouseLocation flip),
  0x1001885bc (`pressedMouseButtons & 1`), 0x1001885b4 / park-geometry
  evidence.
- Rules 22–27: `MacCapture.start` 0x100143f38, `stop` 0x100144dbc, `setRate`
  0x1001451b0 (1/fps, fps>0), `hold` 0x10014570c (held set, `resized` →
  delayed snapshot 0x1001453ac), `latest` 0x100145008, `refreshDue`
  0x100143cf4 (interval vs last shot, ≤3 in flight check, screen-capture
  preflight), snapshot completion 0x10014613c (serial increment, onFrame);
  field offsets in `engine-seam-signatures.json`; `CaptureFrame.init(size:serial:)`;
  `Engine.captureArrived` called from app closure.
- Rules 28–29: witness 0x10018861c → `CACurrentMediaTime`; 0x10018ae84
  (asyncAfter on main, void return).
- Rules 30–33: witnesses 0x100189520 / 0x100189590 / 0x100189658 (asm:
  `fcsel` max for learn; −2 threshold and min for observe), `AppLimitStore.set`
  0x100189250 (user defaults) — `dec-engine-seams-capture-stores.json`;
  `Engine.lowerMinimum` log text.
- Rules 34–36: `JSONDisplayStore.url` 0x100188624 (`arrangement-` + key +
  `.json`), `save` 0x1001852f8 (cache + JSON encode + write), `load`
  0x10018465c; `SavedDisplay` field getters.
- Rule 37: callers of `Engine.save()` — `evidence/engine-save-callers.txt`.
- Rule 38: `Renderer.render(StagePlan)` witness; `StagePlan`, `WindowPlan`,
  `TabStrip`, `CollapsedGroup`, `GapHandle`, `Dim` getters —
  `engine-seam-signatures.json`.
- Rules 39–41: direct calls from TrellisWM module into Engine —
  `evidence/engine-inbound-direct-calls.txt`; study §3.2 (event tap, hotkeys);
  `Engine.windowMoved`, `windowTitleChanged` symbols.
- Rule 43: `Engine.onChange/onNeedsFrames/onHandOff/onCarryOut/onDid/onLocked`
  getters.
- Rule 44: only call of `CGSSetConnectionProperty` at 0x10014238c in
  `BackgroundCursor.enable()`, property name string `SetsCursorInBackground`
  at 0x10021ce10.

## Confidence
| Rule | Label |
|---|---|
| 1 key format | Observed |
| 2 display key | Observed |
| 3 engine key = display + `|` + desktop | Observed (join); that the second part is the OS desktop id Inferred from `makeWorld(_:space:)` |
| 4 app identity optional | Observed |
| 5–6 construction, shared vs per-world | Observed |
| 7 saved-layout fallback lookup | Inferred (two lookups + equality seen; purpose Inferred) |
| 8 policy mutable | Observed |
| 9 visible area change | Observed |
| 10 UI-thread delivery | Observed |
| 11 frame ticks via app display link | Observed names; wiring Inferred |
| 12 focus | Observed (async, no result); "raising" purpose Inferred |
| 13 frontToBack | Observed |
| 14 isShowing + 0.1 s cache | Observed (cache); exact TTL comparison Inferred |
| 15 visibleFraction | Observed |
| 16 readFrame | Observed |
| 17 setFrame async/per-window queue/null on unknown | Observed; park-write flag and in-flight purpose Inferred |
| 17 refusal handled by engine | Inferred (service has no error channel — Observed) |
| 18 tabSibling | Inferred from names (function not read in detail) |
| 19–21 | Observed |
| 22–24, 26 | Observed |
| 24 pictureRate constants are fps | Inferred |
| 25 hold | Observed (toggle + delayed recapture); purpose Inferred; effect Unknown |
| 27 timer-driven capture, ≤4 in flight | Observed |
| 28–29 | Observed |
| 30–33 | Observed |
| 34–36 | Observed |
| 37 save triggers | Observed (caller list) |
| 38 plan contents | Observed (field names/types); meanings Inferred |
| 39–41 inbound calls | Observed for directly-called methods; `windowMoved`/`windowTitleChanged`/move-session methods reached via dynamic dispatch — Inferred |
| 42 info fields | Observed; "dockable" meaning Inferred |
| 43 callbacks | Observed names; meanings Inferred |
| 44 private call purpose | Observed |

## Unknowns
- **Capture `hold` effect**: what the held mark changes in scheduling (always
  capture? capture even when hidden? skip rate limiting?). The toggle and the
  delayed re-capture are clear; the use of the mark inside snapshot was not
  traced. Safe default for CanvasWM: treat hold as "keep the last picture
  available and refresh it once now".
- **Who calls `learn` / `observe` in the engine and with which sizes**
  (presumably learn after a refused shrink, observe after any read-back). The
  store semantics are exact; the engine-side trigger belongs to the
  limits/solver feature.
- **`tabSibling` exact rule** (same frame vs OS tab group API) — the macOS
  function is large and was not read; Windows has no native tabs, so CanvasWM
  can return null.
- **Engine reaction to `setFrame` returning null** (retry vs give up).
- Full list of inbound methods reached by dynamic dispatch from the app
  (move sessions, tab presses, preview drags): names are known from the
  engine API, call sites not enumerated.
- Meaning of `held` in `windowAdded(info, held)` and of the hand-off /
  carry-out callbacks (multi-display feature).
- Outbound "accomplishment" (lessons) and "gate" (licensing) callbacks:
  deliberately not analysed (out of scope).

## Windows notes
- **Keys**: use an opaque string derived from the HWND (e.g. `"w" + hex`).
  HWNDs can be reused after a window is destroyed; the app layer must remove
  the old key (`windowRemoved`) before admitting a new window with the same
  handle.
- **Engine key**: monitor device path or `DISPLAYCONFIG` target id (stable
  across reboots) plus `|` plus the virtual-desktop GUID from
  `IVirtualDesktopManager.GetWindowDesktopId` (if CanvasWM supports per-desktop
  layouts; otherwise a constant).
- **SetFrame**: keep the async contract (callback with actual rect, null on
  failure) even though Win32 `SetWindowPos` is synchronous; complete via the
  UI dispatcher, never re-entrantly. A Win32 adapter may coalesce several
  pending writes into one `BeginDeferWindowPos` batch internally; the engine
  contract stays per-window. Report the frame from
  `DWMWA_EXTENDED_FRAME_BOUNDS` converted to DIPs, and translate targets the
  same way (invisible borders). Elevated windows (UIPI) fail silently —
  return null.
- **Focus**: `SetForegroundWindow` restrictions apply; the adapter may need
  the usual Alt-key/AttachThreadInput workaround. Still fire-and-forget.
- **FrontToBack / IsShowing**: `EnumWindows` order is front-to-back z-order;
  IsShowing = visible, not minimised, not cloaked (`DWMWA_CLOAKED`).
- **Capture**: DWM thumbnails are registered per (overlay window, source
  window) and drawn by DWM; there are no pixel frames. Keep `Latest` returning
  a size + serial (serial bumped when the source size changes or a thumbnail
  is (re)registered); rate becomes advisory; the renderer owns the actual
  `DwmRegisterThumbnail` handles keyed by window key.
- **Clock**: `Stopwatch`/`QueryPerformanceCounter` seconds for `Now`;
  `After` via a `DispatcherTimer` or UI-thread timer queue. A frame callback
  (`onNeedsFrames` → tick) can use `CompositionTarget.Rendering` or a
  `DwmFlush`-paced loop.
- **Limit store**: appId = process image path (or AUMID for packaged apps);
  feed `WM_GETMINMAXINFO`-derived minimums through `Learn`.
- **Display store**: JSON under `%LOCALAPPDATA%\CanvasWM\` with a sanitised
  file name (engine keys contain `|` and `\`, which are illegal in file
  names — hash or escape the key).
- **Hotkeys / input**: TrellisWM has no input seam. Recommendation for
  CanvasWM: no `IHotkeys` in Engine; App registers `RegisterHotKey` /
  low-level hooks and calls an engine command API. If an `IHotkeys`
  abstraction is still wanted for testability, put it in App, not Engine.
- **Cursor in background**: not needed on Windows; layered overlay windows
  set cursors via `WM_SETCURSOR` while hooks are active.

## Recommended scope split
1. `engine-interfaces` (this feature): the six outbound service interfaces
   (WindowSystem, Capture, Clock, Renderer with a placeholder plan record,
   LimitStore, DisplayStore as two interfaces), value types `WindowInfo`,
   `CaptureFrame`, `SavedDisplay` (fields per rule 36, document type from
   Core), and Mac-runnable fakes with the behaviours in Worked examples.
2. `engine-inbound-api` (new): engine construction, the lifecycle/focus/
   pointer command methods (rules 39–42), outbound callbacks (rule 43),
   `tick`/needs-frames loop. This decides the IHotkeys question (recommend
   none in Engine).
3. Renderer plan contents grow with the overlay/tab-strip/overview features.
