# Feature: app-composition

Feature #29. Lands in `CanvasWm.App` plus small `CanvasWm.Win32` pieces, and
(proposed) a routing/coordination layer in `CanvasWm.Engine`. This is the
first runnable build: it wires the engine (#23, #25–#27), tracker (#30),
window source (#10), frame writer (#11) and hotkeys (#12).

Part 1 is studied behaviour of the macOS app layer ("coordinator" and app
delegate). Part 2 is CanvasWM design input and is **not** studied behaviour.
This feature is too big for one change; see "Recommended split" at the end.

## Summary
The studied app starts in stages: it prepares settings, puts its icon in the
menu bar, installs termination-signal handlers, waits up to 4 s for an older
copy of itself to quit, and then polls once a second until the OS grants the
accessibility permission. Only then does it start the engine layer: a
coordinator that watches displays, virtual desktops and applications, keeps
one engine per (display, desktop) that it creates lazily the first time that
desktop is shown, and routes window events and shortcut commands to the
right engine. Display changes are debounced (0.4 s if only the usable area
changed, 1.5 s if the display set changed) and a changed display set is
handled by releasing every window and rebuilding. Quitting saves every
engine, releases every window and waits at most 2 s for the moves to finish.

---

# Part 1 — Studied behaviour (macOS)

## Behaviour

### A. Start sequence (app delegate)
1. **Launch order.** On launch the app, in this order: writes a log line;
   migrates old settings and registers setting defaults; prepares its menus;
   (sets up licensing — out of scope, not traced); starts its updater;
   asks any running copy of an *earlier build* (matched by application
   identifier, never its own process) to quit; creates the menu-bar status
   item; installs termination-signal handlers (rule 4); then waits for the
   quitting copies (rule 2); shows the first-run welcome window if a
   "seen" flag is not set (onboarding — out of scope); and arms a 1.0 s
   repeating timer (rule 3).
2. **Wait for an older copy.** The list of copies asked to quit is polled
   every **0.1 s**. When all have terminated, or **4.0 s** after launch,
   whichever is first, the app continues after a further **0.3 s**.
3. **Permission gate.** The 1.0 s timer asks the OS whether the app is
   trusted for accessibility (window control). The first time the answer is
   yes, the timer is stopped and the engine layer is started (rule 5). Until
   then nothing is managed; the menu-bar item exists but the engine does
   not. (Licensing code sits next to this start path; per project rules it
   was not traced, and CanvasWM has no equivalent.)
4. **Signals.** For SIGTERM, SIGINT and SIGHUP the default action is
   disabled and a main-thread handler requests a normal application
   termination, so a signal takes the same path as "Quit" (rule 27).
5. **Engine start (once).** Guarded so it runs only once. It creates the
   coordinator, wires the coordinator's callbacks to the app (menu refresh,
   status strip), starts the coordinator (rule 6), builds the shortcut table
   (findings/win32-hotkeys.md rule 5), then subscribes to two app-internal
   notifications: "shortcuts changed" → rebuild the shortcut table and the
   status strip; and a second settings notification → `redraw()` on every
   engine. Settings changes that affect engine policy are pushed by the
   settings window: new policy assigned to every engine, then `redraw()`.

### B. Coordinator start
6. **Order inside coordinator start.**
   a. Sets a system-wide **0.6 s** timeout for accessibility queries (the
      per-application timeout is 1.0 s, findings/win32-window-enum.md rule 2).
   b. Wires capture and input-interceptor callbacks; starts the global input
      interceptor; starts a Mission-Control watcher (macOS only).
   c. Subscribes to OS notifications on the main thread:
      - **application activated** → `appActivated(pid, ownsDesktop)` on
        **every** engine (inbound-api rule 20);
      - **system woke** → after **1.5 s**, restart live capture and touch
        input;
      - **active virtual desktop changed** → rule 15;
      - **screen configuration changed** → rule 11.
   d. Installs global mouse monitors and touch input.
   e. Builds the **display list** (one record per screen with its key and
      frame) and stores the display signature (rule 10). **No engine is
      created here.**
   f. Starts the window tracker. When the tracker reports ready (initial
      scans done or 0.6 s, win32-window-enum rule 23), the coordinator runs
      "show current desktops" (rule 7).
7. **Show current desktops.** For each display: read the OS desktop now shown
   on it. If it differs from the one recorded, suspend the old engine (and its
   overlay renderer). Get the engine for (display, desktop), **creating it
   now if it does not exist yet** (this is the only place engines are
   created). Collect the tracked windows shown on that display and desktop,
   leaving out windows whose saved owner is another desktop. Resume the
   renderer; then call `resume(with: windows)` if that engine was started
   before, otherwise `start(with: windows)`. A window being dragged across is
   carried in (multi-display feature #24).
8. **Engine creation** (`makeWorld`; already in findings/engine-inbound-api.md
   rules 1–2 and engine-interfaces rules 5–7): engine key = display key + `|`
   + desktop id; work area; policy from settings; shared window system,
   capture, clock, limit store and display store; a **per-display overlay
   renderer**; display frame, other displays' frames; callbacks; then an
   initial `appActivated(front app)`.
9. **Display key.** The display's hardware UUID string, else `display-` +
   numeric id (engine-interfaces rule 2).

### C. Displays
10. **Signature.** One string: for every screen, its key, `@`, its full
    frame as text, joined by `;`. It changes when a display is added,
    removed, moved, or changes resolution; it does **not** change when only
    the usable (work) area changes.
11. **Screen-configuration change.** On each notification: cancel any
    pending display work; compute the new signature.
    - Changed: at once tear down every overlay renderer and stop every frame
      timer, mark "torn down", and schedule the rebuild after **1.5 s**.
    - Unchanged: schedule a usable-area refresh after **0.4 s**.
    A new notification inside the delay cancels and restarts it (debounce).
12. **Rebuild (when torn down or the signature differs at run time).** Take
    the main display's usable area as the fallback. For every existing
    engine: tear down its renderer, call `releaseAll(into: nil)` if its
    display still exists, else `releaseAll(into: main display's usable
    area)`; stop its frame timer. The completions are **not** awaited.
    Then rebuild the display list (engines are dropped) and after **0.8 s**
    run "show current desktops" (rule 7), which creates fresh engines that
    reload their saved layouts.
13. **Usable-area refresh.** For every display, read its usable area and call
    `visibleAreaChanged(area)` on its engine(s) and re-lay-out the renderer
    (the engine ignores an equal area, inbound-api rule 4). In the
    unchanged-signature path refreshes are scheduled at more than one delay
    (0.4 s and 1.2 s constants observed), so a slow dock/menu-bar change is
    still picked up.

### D. Virtual desktops (macOS "Spaces")
14. One engine per (display, desktop); engines of desktops not currently
    shown are **suspended** (inbound-api rule 11), not destroyed. Windows are
    not moved on a switch: the OS hides the inactive desktop's windows.
15. **Desktop switch.** On the "active desktop changed" notification: cancel
    pending desktop work; read the desktop now shown per display; for each
    display whose desktop changed: if a native window drag is in progress,
    hand the dragged window over (multi-display); suspend the old engine and
    its renderer; ask the tracker for a full rescan; then run "show current
    desktops" (rule 7) once the rescan finishes or after **0.35 s**
    (Inferred: both paths observed, the exact either/or join was not traced).
16. Engine keys of unknown desktops use the desktop id `default`
    (inbound-api rule 1).

### E. Routing
17. **Window events (from the tracker).**
    - New window → decide the owning display (desktop→display map; any
      engine already holding it; saved layouts that dock it; largest visible
      overlap) → if that display's shown desktop is the window's desktop (or
      unknown), `windowAdded(info, held: false)` on that display's current
      engine; otherwise it waits for that desktop's `start/resume` list.
    - Removed, moved, title changed, became dockable, focused → the engine
      that **holds** the window key.
    - Focus moved to an unmanaged window (or none) of an app →
      `focusLeftWindows(pid)` on **every** engine.
    - App activated → `appActivated(pid, ownsDesktop)` on **every** engine
      (also when the engine is suspended; inbound-api rule 20 records it).
18. **Active engine** (used by most shortcut commands and menus), first match
    wins:
    1. any engine currently in the overview;
    2. the engine holding the last focused window, if that window still
       belongs to the last active app;
    3. the shown engine that manages a window of the last active app (or the
       frontmost app);
    4. the pointer engine (rule 19).
    (Order Observed; exact membership tests of steps 2–3 Inferred.)
19. **Pointer engine.** The engine of the shown desktop on the display whose
    full frame contains the mouse pointer.
20. **Shortcut commands** go to the active engine, except "step in" (pointer
    engine when "step toward pointer" is on, else active) and "split" (pointer
    engine only) — findings/shortcut-model.md rules 2–4. A command with no
    target engine does nothing.
21. Engine callbacks (inbound-api rule 24): `onChange` → refresh menu and
    status strip; `onNeedsFrames` → frame driver (rule 22); `onHandOff`,
    `onCarryOut` → multi-display coordinator (#24); the gate and lesson
    callbacks are out of scope.

### F. Frame driver
22. Per display, on `onNeedsFrames(true)` the app creates **one** display-
    synchronised timer for that display (only if none exists) and on each
    refresh calls the engine's `tick(now)` with the host's media clock time;
    on `onNeedsFrames(false)` it stops and discards it. Rate = that
    display's refresh rate. Late frames (> 1.5 × the frame interval) are only
    logged.

### G. Persistence and settings
23. **Layouts:** one JSON file per engine key, `arrangement-<engine key>.json`,
    in the app's application-support folder (created if missing). The store
    can list all such files (used for "which desktop owns this window").
    Written on every engine save; write errors swallowed.
24. **Everything else** (policy values, exclusion list, shortcut strings,
    learned app minimums, first-run flag, menu-bar strip option) lives in the
    OS per-user preferences store (key/value).
25. **Start at login** is a settings toggle using the OS login-item service.
    Default state: Unknown.

### H. Menu-bar item (contents in our own words)
26. The status menu (about 22 items, 2 submenus) offers: the shortcut actions
    (overview, zoom out/in, move left/right/up/down, back/forward with
    enabled state, maximize, hide/show desktop area with a check mark, split)
    each showing its current shortcut; a saved-views submenu (go to / save
    view n, with a short summary of what each slot holds); a per-app toggle
    "stop managing the frontmost app" that edits the exclusion list; open
    settings; help/website links; compose a problem report; about; quit.
    Also present but out of scope: lessons/getting started, licence/upgrade.
    A separate optional menu-bar strip shows the current workspace.

### I. Quit
27. **Quit path** (menu Quit, signal, or OS shutdown → app will-terminate):
    a. `save()` on every engine;
    b. `releaseAll(into: ?)` on every engine, each completion counted;
    c. wait while some completion is outstanding **and** less than **2.0 s**
       has passed, running the UI loop in **0.02 s** slices;
    d. tear down renderers and frame timers, remove global input monitors,
       stop the tracker.
    The `into` argument on quit could not be decoded (inbound-api Unknowns).
28. **Crash safety at app level:** only the signal handlers of rule 4 (they
    route to the orderly quit). No app-level journal was seen; restoring
    windows after a hard crash belongs to #14 (note only).

### J. Other observed app-layer items (not needed for CanvasWM v1)
29. A development-build-only main-thread stall reporter (20 ms timer).
    A Mission-Control watcher and a desktop-switch overlay fade
    (decorations hidden, faded back after 0.3 s). An external URL command
    handler (open research item).

## Data model (studied, neutral)
- **Display**: key, OS screen, shown desktop id, active engine, engines by
  desktop id.
- **World**: engine key, engine, overlay renderer, frame timer.
- **Coordinator state**: displays, display signature, "torn down" flag,
  pending display work, pending desktop work, last focused window key, last
  active app pid, tracker, shared services (window system, capture, clock,
  limit store, display store), started flag.
- **App state**: coordinator, shortcut table, status item/menu, settings
  window, permission timer, signal sources.

## Worked examples (studied)
1. Launch with permission already granted, no older copy running → after
   ~0.3 s the 1.0 s timer's first tick finds trust → engine layer starts →
   tracker ready → engines are created for each display's shown desktop and
   started with their windows.
2. Launch without permission → menu-bar item only; every 1.0 s trust is
   checked; when the user grants it the engine starts within ≤ 1 s.
3. Older copy running and slow to quit → start continues at the latest
   4.0 s + 0.3 s after launch.
4. Dock auto-hide toggled → screen notification, signature unchanged →
   after 0.4 s (and again later) every engine gets `visibleAreaChanged`.
5. Second monitor unplugged → signature changed → overlays torn down at once;
   1.5 s later the unplugged display's engine releases its windows into the
   main display's usable area, the surviving display's engine releases in
   place, all engines are dropped and display list rebuilt; 0.8 s later
   fresh engines start from their saved layouts.
6. Three notifications 0.5 s apart while plugging a dock → each cancels the
   previous; rebuild runs 1.5 s after the last one.
7. Switch desktop on display A → A's old engine suspended; tracker rescan;
   A's engine for the new desktop created (first visit) and started, or
   resumed (later visits) with that desktop's windows.
8. Ctrl+Opt+Right while the overview is open on display B → goes to B's
   engine (active-engine step 1) even if the pointer is on display A.
9. Quit with 3 parked windows whose moves take 50 ms each → save all, release
   all, the wait ends after ~0.05–0.1 s when the last completion arrives.
   A hung app's window that never reports → wait ends at 2.0 s.
10. `kill -TERM <pid>` → same as Quit (example 9).

## Evidence
New notes: `~/Documents/trelliswm-re/evidence/app-composition-notes.txt`
(objdump-based; addresses below are in the shipped binary).
- Rule 1: `AppDelegate.applicationDidFinishLaunching` 0x10012bd28 call order
  (Migration.run, SettingsVisibility.registerDefaults, setUpEditMenu,
  setUpLicense [not traced], updater init, reportStillness, quitFirstBuild
  0x10013b91c, setUpStatusItem 0x10012d1f0, installSignalHandlers 0x10012d560,
  startWhenGone 0x10012d7e0, showWelcome, timer).
- Rule 2: deadline `+4.0` at 0x10012c650; `startWhenGone` 0.1 s retry
  (0x3fb999999999999a) and 0.3 s continue (0x3fd3333333333333).
- Rule 3: timer 1.0 s at 0x10012c8e8; closure 0x10012df74 calls
  `AXIsProcessTrusted`, invalidates, calls `startEngine` (0x10012e000).
- Rule 4: signal list {15, 2, 1} at 0x100262758; handler 0x1001421a0 →
  0x100139314 (`terminate:`); `applicationWillTerminate` → `Coordinator.stop`
  (0x10012e90c).
- Rule 5: `AppDelegate.startEngine` 0x10012e01c (guard 0x10012e114,
  Coordinator.start 0x10012e284, registerHotkeys 0x10012e28c, observers
  0x10012e368 / 0x10012e450; closure #4 → registerHotkeys + refreshStrip,
  closure #5 → Engine.redraw); policy setter callers in showSettings
  closures #1/#3.
- Rule 6: `Coordinator.start` 0x10015bd48 (AX timeout 0.6f, EventTap.start
  0x10015c538, MissionControl.start, notification constants 0x1002443f8 /
  0x100244410 / 0x1002443e8 / 0x100244398, buildDisplays 0x10015d118,
  WindowTracker.start 0x10015d15c, ready closure #19 → showCurrentSpaces);
  closure #11 → `Engine.appActivated`; closure #12 → 1.5 s → capture /
  Multitouch restart.
- Rules 7–8: `Coordinator.showCurrentSpaces` 0x100166cd4 (suspend,
  makeWorld [sole caller], windowsShown, isAwayFromHome, resume/start,
  carryIn, windowAdded); `makeWorld` per engine-inbound-api evidence.
- Rule 10: `Coordinator.signature` 0x100172f54.
- Rules 11–12: `Coordinator.screensChanged` 0x100164d74 (0.4 s / 1.5 s,
  tearDown, link invalidate) and closure #1 0x10016ea88 (releaseAll at
  0x10016fbf0 with default no-op completion, buildDisplays 0x10016fc64,
  0.8 s → showCurrentSpaces).
- Rule 13: `Coordinator.refreshUsableAreas` 0x10016de7c; scheduled from
  screensChanged closure (0.4 s and 1.2 s constants).
- Rules 14–15: `Coordinator.spacesChanged` 0x100163fbc (handOverDrag,
  Engine.suspend 0x1001648c0, WindowTracker.rescan, 0.35 s).
- Rule 17: callers of `Engine.windowAdded` (Coordinator.arrive 0x100170158),
  `Coordinator.world(holding:)` callers (tracker remove/dockable/move/title),
  `Coordinator.owner(of:)` 0x10016cd4c, `Engine.focusLeftWindows` caller in
  `WindowTracker.reportFocus`; `Engine.appActivated` caller closure #11.
- Rules 18–19: `Coordinator.activeEngine.getter` (inOverview field 0x279,
  lastFocusedKey 0xef8, lastAppPID 0xf00, frontmost app, pointerEngine
  fallback 0x10015adcc); `Coordinator.pointerEngine.getter` 0x10015d374.
- Rule 20: findings/shortcut-model.md rules 2–4.
- Rule 22: makeWorld closure #12 (engine-inbound-notes), `FrameTarget.tick`
  0x10015dd54 (1.5 × interval late threshold).
- Rule 23: `JSONDisplayStore.url` 0x100188624, `JSONDisplayStore.all()`.
- Rules 24–25: imports `NSUserDefaults`, `SMAppService`;
  `SettingsWindow.toggleLogin`; findings/engine-interfaces.md rule 33.
- Rule 26: `AppDelegate.refreshMenu` callee profile (addItem ×22,
  setSubmenu ×2, Shortcuts.menuKey, Navigator.canGoForward,
  Engine.desktopHidden, applyExclusions, framingSummary, openURL,
  ProblemReport.compose, about, terminate).
- Rule 27: `Coordinator.stop` 0x10015adf8 (Engine.save loop 0x10015b074,
  releaseAll + group 0x10015b270, 2.0 s 0x10015b2f8, 0.02 s slices,
  tearDown, removeMonitor, WindowTracker.stop).
- Rule 29: `AppDelegate.reportStillness` 0x10012d000; Coordinator.start
  closure #10 (0.3 s decoration fade).

## Confidence
| Rule | Label |
|---|---|
| 1 launch order | Observed (call order); what quitFirstBuild matches (which identifiers) Inferred |
| 2 0.1 / 4.0 / 0.3 s | Observed constants; the continuation target after 0.3 s Inferred |
| 3 permission gate 1.0 s | Observed |
| 4 signals → terminate | Observed |
| 5 engine start, notifications | Observed calls; meaning of the second notification Inferred |
| 6 coordinator start order, notifications | Observed |
| 7 show current desktops | Observed calls; filter semantics Inferred |
| 8–9 | Observed (earlier findings) |
| 10 signature | Observed |
| 11 debounce 0.4 / 1.5 s, teardown | Observed |
| 12 rebuild, releaseAll targets, not awaited | Observed |
| 13 usable-area refresh | Observed function; the exact refresh schedule (how many, which delays) Inferred |
| 14 | Observed |
| 15 desktop switch, 0.35 s | Observed calls/constant; join Inferred |
| 16 | Observed |
| 17 routing | Observed call sites; owner-decision order Inferred (multi-display #24) |
| 18 active engine order | Observed; step 2–3 tests Inferred |
| 19 pointer engine | Observed |
| 20–21 | Observed (earlier findings) |
| 22 frame driver | Observed |
| 23 layout files | Observed |
| 24 preferences store | Observed (imports, earlier findings) |
| 25 login item | Observed toggle; default Unknown |
| 26 menu contents | Inferred from callees (no strings copied) |
| 27 quit sequence | Observed; `into` argument Unknown |
| 28 | Observed (only signal handlers); absence of journal Inferred |
| 29 | Observed names; not analysed further |

## Unknowns (Part 1)
- The `into` area used by `releaseAll` on quit (nil vs main display).
- Which identifiers "older build" matching covers, and whether a second
  launch of the *same* build is prevented (macOS normally prevents it).
- The exact schedule of usable-area refreshes after an unchanged signature
  (first delay constant not decoded).
- Default of the start-at-login option.
- What the engine's `focusLeftWindows` does internally (only the routing was
  traced; it is an engine method CanvasWM does not have yet).
- Anything gated by licensing in the start path (deliberately not traced).

---

# Part 2 — CanvasWM design input (NOT studied behaviour)

Everything below is a proposal for the spec, based on Windows platform
knowledge and on the existing CanvasWM code. Items marked **[verify]** need
the human on a Windows PC. Items marked **[decide]** need a human decision.

## What exists vs what is missing (code audit, 2026-10-25)
| Area | Exists | Missing for #29 |
|---|---|---|
| Engine | `DisplayEngine(EngineKey, Rect, EnginePolicy, EngineServices)`; Start/Suspend/Resume/ReleaseAll/Release/Save; window events; AppActivated; VisibleAreaChanged; Tick; Move/ArrangeAll; most other commands are stubs; callbacks OnChange/OnNeedsFrames/OnHandOff/OnCarryOut/Log; re-entry from a callback throws | **`FocusLeftWindows(pid)`** (M1); no coordinator/router (M3) |
| Tracking | `WindowTracker` (IWindowSource, IWindowSystem, IClock, IWindowTrackerTarget, ClassifierPolicy); `Find(key)`, `KeyFor(handle)`; records keep handle + thread id privately | **`IdentityOf(key)`** (M2); `IWindowTrackerTarget` implementation (the router, M3) |
| Services | interfaces + in-memory fakes in tests | real `IClock` (M5), `IDisplayStore`/`ILimitStore` file stores + JSON codec (M6), `IRenderer`/`ICapture` null implementations until #16 (M7) |
| Win32 | `Win32WindowSource(post)`, `Win32WindowSystem(post, IManagedWindowMap, log)`, `Win32HotkeySource(post)` with `Apply`/`Pressed`, `DipSpace`, internal `Monitors` (no work area, no stable key) | public display enumeration with stable key + work area + DPI (M4); virtual-desktop helper (M9); message loop/host (App) |
| Core | Shortcuts (`ShortcutDispatch.Match`, `HotkeyPlan`, `ShortcutStore` strings, `TargetEngine`), `ParkGeometry`, `ClassifierPolicy` | `DisplaySignature` (pure) (M8) |
| App | `Program.Main` returns 0 | everything else |

## K. Threading model
K1. One **STA UI thread** owns everything: the message loop, the hidden host
    window, `Win32WindowSource`, `Win32WindowSystem` completions,
    `Win32HotkeySource`, the tracker, the router and every `DisplayEngine`
    call (engine-inbound-api rule 7). The frame writer keeps its own writer
    thread (#11) and posts completions back.
K2. **`post`** (the delegate every Win32 component takes): enqueue the
    action in a `ConcurrentQueue<Action>` and `PostMessage(hostHwnd,
    WM_APP+1)` only when the queue goes from empty to non-empty; the window
    procedure drains the queue FIFO. Never runs inline (#10 B17). If WinForms
    is chosen (L-decision), `WindowsFormsSynchronizationContext.Post` is an
    equivalent.
K3. **Cross-engine calls from callbacks** (hand-off, frames on/off) must be
    posted, never made synchronously: `DisplayEngine` throws on re-entry from
    its own callbacks, and inbound-api rule 8 forbids it.
K4. The host window must be a **hidden top-level window** (not
    `HWND_MESSAGE`): message-only windows do not receive broadcast messages
    such as `WM_DISPLAYCHANGE`, `WM_SETTINGCHANGE`, `WM_POWERBROADCAST`,
    `WM_ENDSESSION` or the `TaskbarCreated` registered message. Give it
    `WS_EX_TOOLWINDOW` so it never shows in Alt-Tab/taskbar, and exclude own
    process from tracking (already done by #10/#30).

## L. UI framework [decide] (resolves #0 A5)
| | Raw Win32 | WinForms | WPF |
|---|---|---|---|
| Tray icon + menu | `Shell_NotifyIcon` + `TrackPopupMenu`, ~200 lines | `NotifyIcon` + `ContextMenuStrip` | no built-in tray (needs WinForms interop or a package) |
| Settings window (tabs, checkboxes, list, shortcut recorder) | very costly | easy; recorder via `WndProc` override feeding `Win32KeyInput` | easy, nicer |
| Message loop | own `GetMessage` loop | `Application.Run(ApplicationContext)`; modeless forms need it for Tab/accelerators | `Dispatcher.Run`; `CompositionTarget.Rendering` available |
| Per-monitor DPI | manual | PerMonitorV2 via `ApplicationHighDpiMode` (adequate) | good |
| Size / startup | smallest | moderate | largest |
| Coexists with existing message-only windows and WinEvent hooks | yes | yes | yes |

Recommendation: **WinForms**, chosen now, with `Application.Run` as the one
message loop from 29a onward (so 29c settings forms work without changing the
loop). Raw Win32 is fine for 29a alone but makes 29c expensive; WPF adds no
tray and more weight for little gain. Keep every WinForms type inside
`CanvasWm.App` (Win32 stays framework-free).

## M. Missing seams and where they belong
M1. **`DisplayEngine.FocusLeftWindows(int processId)`** — Engine. Studied
    engine has it; its effect is Unknown. Proposal for 29a: add the method
    with a frozen-guard and "clear the engine's per-app last-focused record
    for that pid and re-render" (Inferred from `appActivated` rule 20); flag
    in the spec as a placeholder until traced. Router broadcasts it (rule 17).
M2. **`WindowTracker.IdentityOf(WindowKey) → TrackedIdentity?`** with
    `TrackedIdentity(ulong Handle, int ProcessId, int ThreadId)` in Engine,
    plus `KeyOf(ulong handle) → WindowKey?` (= `KeyFor` + tracked check) —
    Engine (#11 R4). The App implements `IManagedWindowMap` as a 10-line
    adapter (Engine type → Win32 `ManagedWindowIdentity`). Edge: engines keep
    parked windows the OS did not report on resume (inbound-api rule 12); the
    tracker does not hold them, so `Resolve` returns null and writes fail
    (null result). Accept for v1 and note for #14.
M3. **Router / coordinator** — Engine, new `CanvasWm.Engine.Composition`
    (pure, Mac-testable with fakes). Implements `IWindowTrackerTarget` and
    owns all policy of Part 1 sections B–F:
    - engines by `EngineKey`; created lazily via an injected factory
      `Func<EngineKey, DisplayInfo, DisplayEngine>` on first show (rule 7);
    - `DisplayFrames` (tracker admission) = DIP frames of all displays;
    - `Ready(windows)` → per display: windows whose owning display is that
      display (centre point, else largest overlap — Core helper) →
      `Start(windows)` (and `Resume` on later shows);
    - routing of rule 17 (holder first; owner display for new windows);
      `FocusLeftWindows` and `AppActivated` broadcast;
    - last focused key / last active pid for the active-engine order of
      rule 18; pointer engine from `IWindowSystem.Pointer` + display frames;
    - `Execute(ShortcutCommand, pointer)` mapping `CommandKind` → engine call
      with `TargetEngine` (shortcut-model rules 1–4; "split" needs the
      file-manager hook, see N12);
    - display-set orchestration: `DisplaysChanged(IReadOnlyList<DisplayInfo>)`
      with the 0.4 s / 1.5 s debounce (via `IClock.After` + generation
      counter, since `After` has no cancel), signature compare, release
      targets, rebuild, 0.8 s re-show (rules 10–13);
    - desktop orchestration: `DesktopShown(displayKey, desktopId)` → suspend /
      resume / start (rules 14–15);
    - quit: `SaveAll()` then `ReleaseAll(onAllDone)` with completion counting
      (rule 27); the 2 s wait itself is App (needs the message loop);
    - frames: tracks which engines have frames on (from `OnNeedsFrames`) and
      exposes `TickAll(now)` + an `AnyFramesRunning` change event for the
      driver;
    - `OnHandOff`/`OnCarryOut`: not wired until #24 (`OnCarryOut` → false).
    Keeping this in Engine keeps App to wiring only (CLAUDE.md layering).
M4. **Display enumeration** — Win32, public `Win32Displays.Snapshot()` →
    `DisplayInfo(DisplayKey Key, PixelRect Bounds, PixelRect Work, uint Dpi,
    Rect DipFrame, Rect DipWork, bool Primary)` where `DipFrame =
    DipSpace.ToDip(M, M, dpi)` and `DipWork = DipSpace.ToDip(Work, M, dpi)`
    (project rule; #11 B19). `DisplayInfo` itself should be an Engine or Core
    record so the router can use it (Win32 fills it). Reuse the internal
    `Monitors` logic; add `rcWork`.
    **Display key** [decide]: preferred = the monitor's `DISPLAYCONFIG`
    target device path (`QueryDisplayConfig` +
    `DisplayConfigGetDeviceInfo(GET_TARGET_NAME).monitorDevicePath`), stable
    per physical monitor + port across reboots; fallback = `MONITORINFOEX`
    `szDevice` (`\\.\DISPLAYn`, not stable). Cloned/mirrored outputs appear as
    one `HMONITOR` → one key (use the first target).
M5. **`IClock`** — Win32 (needs timers) or App: `Now` = `Stopwatch`
    seconds (QPC); `After(s, a)` = a min-heap of due actions serviced by one
    `SetTimer` on the host window re-armed to the earliest due time (or a
    high-resolution waitable timer thread that posts). Delivery through
    `post`, never inline. Granularity of `SetTimer` is ~10–16 ms — enough for
    0.05 s tracker rechecks [verify jitter acceptable].
M6. **Stores** — codec pure (Engine `Persistence` namespace, System.Text.Json,
    Mac-tested round-trips); file I/O thin (App, or Engine if the human
    accepts plain `System.IO` there) [decide]:
    - `FileDisplayStore(directory)`: one file per engine key. The key
      contains `|` and possibly `\`, `#`, `{`, which are illegal or awkward
      in Windows file names → file name = `arrangement-` + lowercase hex
      SHA-256 of the UTF-8 key + `.json`, and the key stored inside the file
      for listing. Write = temp file + `File.Replace`/`Move` (atomic), errors
      swallowed and logged (engine-interfaces B17). JSON per
      engine-interfaces B15 + layout-tree A1/A2 (keys `doc`, `framed`,
      `framings`, `owners`, `dockSpots`, `floatSizes`; node `kind`
      discriminator). Nested shapes (restore target, dock spot, size) must be
      fixed in the spec (A10 there).
    - `FileLimitStore`: one `limits.json` map appId → `{w,h}`; persisted on
      each change (rule 31–32 semantics already in `LimitRules`).
    - `SettingsStore`: `settings.json` flat string map: shortcut keys from
      `ShortcutStore` (`hotkeys.*`), policy fields, tuning, exclusion list,
      start-at-login. Mirrors the studied key/value preferences (rule 24).
    - Location: `%LOCALAPPDATA%\CanvasWM\` (`layouts\`, `limits.json`,
      `settings.json`, `logs\`). Local, not roaming: layouts are per machine
      (display keys) [decide].
M7. **Null renderer / null capture** — App, until #16: renderer ignores plans;
    capture `Latest` returns null, `Start/Stop/SetRate/Hold` no-ops. Check in
    the spec that `Start` does not wait for pictures forever (current engine
    code starts capture but has no "start when captured" wait — Observed in
    `DisplayEngine.cs`; keep it that way or bound it).
M8. **`DisplaySignature.Of(IReadOnlyList<DisplayInfo>)`** — Core pure:
    ordered by key, `key@x,y,w,h@dpi` joined by `;`. Including **DPI** is a
    CanvasWM addition: on Windows a scale change alters DIP frames without
    moving pixels and must rebuild (the studied signature uses frames only).
M9. **Virtual desktops** — Win32 helper + router (see O).
M10. **Window-to-desktop fact** — optional `OsWindowFacts.DesktopId` (GUID
    string) filled by `IVirtualDesktopManager.GetWindowDesktopId` (public,
    documented COM; must be called on the STA thread) — needed only if
    per-desktop engines are in scope (O2).

## N. Windows app design (29a/29b)
N1. **Process start order** (mirrors rules 1–6, adapted):
    1. single-instance check (N2); 2. DPI awareness is already set by the
    manifest (N3); 3. load settings; 4. create host window + `post` + clock;
    5. tray icon (Quit always available); 6. create window source, tracker
    (not started), window system with the tracker-backed map, hotkey source;
    7. enumerate displays → router; 8. subscribe host messages (N5);
    9. `tracker.Start()` → `Ready` → router starts engines; 10. apply hotkeys
    (`Apply(HotkeyPlan.Desired(table, recording: false))`); 11. install
    crash/exit hooks (N9). There is no permission gate on Windows (no
    accessibility consent); elevated windows are simply unmanageable (#10).
N2. **Single instance**: named mutex `Local\CanvasWM-<user SID>` (per
    session). If held: find the running instance's host window by its
    registered class name and post it a "show settings" message, then exit 0.
    Studied app instead quits an older build and waits ≤ 4 s; replacing an
    old instance during upgrades can be done the same way later [decide].
N3. **DPI**: `ApplicationHighDpiMode = PerMonitorV2` (WinForms) or an
    `app.manifest` with `<dpiAwareness>PerMonitorV2</dpiAwareness>`; keep
    `asInvoker` (no elevation). Existing Win32 code already scopes thread DPI
    with `DpiScope`; the manifest makes the host and tray consistent.
N4. **Run as user, not elevated** [decide]: elevated windows are then
    excluded (#10 `IsAccessible`). Running elevated would manage them but
    makes every hook/hotkey run at high integrity; not recommended for v1.
N5. **Host messages → router**:
    | Message | Action |
    |---|---|
    | `WM_DISPLAYCHANGE` | re-enumerate displays → `router.DisplaysChanged` (debounce in router) |
    | `WM_SETTINGCHANGE` with `wParam == SPI_SETWORKAREA` (0x2F) | re-enumerate → same call; signature unchanged → usable-area refresh path |
    | `WM_DPICHANGED` (rarely delivered to a hidden window) and `WM_SETTINGCHANGE` generally | re-enumerate; the signature includes DPI (M8) [verify a scale change triggers at least one of these] |
    | `WM_POWERBROADCAST` / `PBT_APMRESUMEAUTOMATIC` | after 1.5 s: re-enumerate displays and `tracker.Rescan()` (studied wake rule 6c) |
    | `WM_QUERYENDSESSION` / `WM_ENDSESSION` | quit path (N8) synchronously; Windows allows ~5 s |
    | registered `TaskbarCreated` | re-add the tray icon (Explorer restarted) |
    | `WM_APP+1` | drain `post` queue |
    | private "show settings" | from a second instance (N2) |
N6. **Frame/tick driver** [decide]:
    | Option | Pros | Cons |
    |---|---|---|
    | **`DwmFlush` loop on a dedicated thread**, posting one coalesced "tick" message | vsync-paced to DWM composition, no extra deps, ~10 lines | paced to the primary/composition clock, not per monitor; may stall if DWM has nothing to present [verify]; extra thread |
    | `IDXGIOutput.WaitForVBlank` per monitor thread | true per-monitor vblank like the studied per-display link | COM/DXGI interop, one thread per output |
    | Waitable high-resolution timer (`CREATE_WAITABLE_TIMER_HIGH_RESOLUTION`, Win10 1803+) at 1/refresh | simple, precise period | not vsync-aligned (tearing-free not needed: we move windows, DWM composes) |
    | `SetTimer` 16 ms | trivial | 15.6 ms granularity, jitter, beats against 60 Hz |
    | `CompositionTarget.Rendering` | vsync | WPF only |
    Recommendation: **one** driver for all engines: a `DwmFlush` thread that
    is started on the first `OnNeedsFrames(true)` and parked (event wait) when
    no engine needs frames; each wake posts a single tick message (never more
    than one outstanding); on the UI thread call `Tick(clock.Now)` for every
    engine with frames on. Fall back to the high-resolution waitable timer at
    the primary refresh rate when `DwmFlush` fails or returns too fast
    (< 2 ms) repeatedly. Same time base as `IClock.Now` (inbound-api rule 26).
    Deviation from studied: single driver, not per display.
N7. **Hotkeys** (#12 A5 decision input): App keeps `ShortcutBindings`,
    the dispatch table and a `Recording` flag; calls
    `hotkeys.Apply(HotkeyPlan.Desired(table, recording))` (a) at start after
    engines start, (b) whenever bindings change, (c) when recording starts
    (desired = empty) and stops, (d) when settings opens, (e) on a tray/menu
    "Retry shortcuts" action. `Pressed(combo)` → `ShortcutDispatch.Match` →
    `router.Execute(command, pointer)`. Reports go to the settings UI (and a
    one-line log for `Taken`). No periodic retry.
N8. **Quit** (tray Quit, end-session, console-less `Ctrl+Break` N/A):
    1. `hotkeys.Apply([])`, unhook WinEvents (no new events);
    2. router `SaveAll()`; 3. router `ReleaseAll(into: null)` with
    completion counting; 4. pump messages (`MsgWaitForMultipleObjectsEx` +
    `PeekMessage`/`DispatchMessage`, or `Application.DoEvents` in WinForms)
    in ≤ 20 ms slices until all completions or **2.0 s**; 5. dispose window
    system (writer thread), sources, tray icon; 6. release mutex; exit 0.
    `into: null` = each window returns to its own engine's work area, which
    is the right choice when the display still exists (studied quit value
    Unknown).
N9. **Crash/exit safety** (app level only; #14 owns the journal):
    `AppDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`
    → log, then best-effort quit path with a shorter wait (e.g. 1 s) if on
    the UI thread; `Console.CancelKeyPress` is not relevant (WinExe). A hard
    kill leaves parked windows off-screen until #14 lands — **do not ship to
    users before #14** [decide]. Partial mitigation already exists: on the
    next start the tracker admits windows that are < 5 % visible and the
    engine re-adopts them from the saved layout.
N10. **Tray menu (29b, our own wording)**: Pause/Resume management (optional,
    not studied), Arrange all, the shortcut actions with their current combo
    as a hint, "Stop managing <front app>" (edits exclusions →
    `tracker.SetPolicy`), Retry shortcuts, Settings…, Start at login (check),
    Quit. No help/website/problem-report/licence items in v1.
N11. **Start at login** [decide]: `HKCU\Software\Microsoft\Windows\
    CurrentVersion\Run` value `CanvasWM` = quoted exe path; default **off**.
    Respect `...\Explorer\StartupApproved\Run` (user disabled it in Task
    Manager) by reading it for the check state [verify].
N12. **"split" action**: needs "open a new file-manager window" — on Windows
    `explorer.exe` with no args (opens a new window) after
    `SplitForNewWindow(<explorer app id>)`. `SplitForNewWindow` is currently
    a stub in `DisplayEngine`; 29 can wire it but behaviour arrives with the
    engine feature [decide whether to include].
N13. **Logging**: `Log` callbacks of engines, tracker diagnostics, hotkey
    reports → rolling text file under `%LOCALAPPDATA%\CanvasWM\logs`.

## O. Virtual desktops [decide — biggest open design issue]
O1. Current behaviour of the built pieces: the classifier ignores cloaked
    windows at admission, and keeps (does not remove) tracked windows that
    get a shell cloak (#30 A5). With **one engine per display** (desktop id
    `default`), switching desktops therefore lets windows of every visited
    desktop accumulate in one layout: hidden-desktop windows keep tiles and
    get frame writes. That is wrong behaviour, not just a missing feature.
O2. Per-desktop engines (studied model, rules 14–15) need:
    - window → desktop: `IVirtualDesktopManager.GetWindowDesktopId`
      (documented);
    - current desktop id: no documented API. Options: read
      `HKCU\...\Explorer\VirtualDesktops\CurrentVirtualDesktop` (Win11;
      Win10 keeps it under `...\Explorer\SessionInfo\<session>\
      VirtualDesktops`) and watch it with `RegNotifyChangeKeyValue`
      [verify on 10 and 11]; or infer from the foreground window's desktop id
      on `EVENT_SYSTEM_FOREGROUND` plus the cloak/uncloak burst;
    - switch handling in the router: suspend old engine, `tracker.Rescan()`,
      after the rescan (or 0.35 s) start/resume the new one.
    Windows desktops span all monitors (one current desktop for all
    displays), simpler than macOS per-display Spaces.
O3. Options: (a) do O2 as feature **29d** right after 29a/b; (b) v1
    single-desktop mode where the router treats windows whose desktop id ≠
    current as temporarily removed on switch and re-added on return (needs
    M10 anyway); (c) document "single virtual desktop only" for the first
    test build. Recommendation: (c) for the very first Windows smoke test,
    then (a).

## P. Worked examples (design)
1. Two monitors (A 1920×1080 @96 at (0,0), B 2560×1440 @144 at (1920,0)),
   work areas exclude a 48 px taskbar → `DisplayInfo` B: `DipFrame =
   (1920, 0, 1706.67, 960)`, `DipWork = (1920, 0, 1706.67, 928)`; engine keys
   `<pathA>|default`, `<pathB>|default`; layouts in
   `%LOCALAPPDATA%\CanvasWM\layouts\arrangement-<sha256>.json`.
2. Taskbar moved to the left on A → `WM_SETTINGCHANGE(SPI_SETWORKAREA)` →
   signature unchanged → after 0.4 s engine A `VisibleAreaChanged`.
3. B unplugged → `WM_DISPLAYCHANGE` → signature changed → overlays (none
   yet) torn down; 1.5 s later engine B `ReleaseAll(into: A.DipWork)`,
   engine A `ReleaseAll(null)`; engines dropped; 0.8 s later new engine A
   starts with all windows.
4. Scale of B changed 150 % → 175 % → pixels same, DPI differs → signature
   differs (M8) → rebuild as in example 3 but both survive (`into: null`).
5. Win+Alt+Right pressed with the pointer on B, last focused window on A →
   active engine = A (rule 18 step 2) → `A.Move(Right)`.
6. Tray Quit with 2 parked windows → save both engines, release both, pump
   ≤ 2 s, exit.
7. Second launch → mutex held → running instance opens settings → second
   process exits 0.
8. Tracker `FocusLeftWindows(4120)` → router calls
   `FocusLeftWindows(4120)` on every engine (M1).

## Windows notes
- Broadcast messages need a top-level hidden window (K4).
- `IVirtualDesktopManager` must be created and called on the STA UI thread.
- `RegisterHotKey`/`UnregisterHotKey` and WinEvent hooks are thread-affine
  to the UI thread (#10, #12).
- Explorer restarts drop tray icons (`TaskbarCreated`).
- End-session gives a few seconds; the 2 s wait fits.
- Hard kill leaves parked windows off-screen (#14).

## Recommended split
- **29a engine-router** (Engine, Mac-tested): M1, M2, M3, M8 and `DisplayInfo`
  record; all of rules 7–22 and 27 as pure orchestration over fakes
  (factory, clock, window system pointer). Biggest logic, fully testable.
- **29b app-host** (App + Win32 small parts): K, L-decision, M4, M5, M6
  (codec in Engine, files in App or Engine), M7, N1–N9, N13; tray icon with
  Quit + Retry shortcuts only. First runnable build on Windows.
- **29c tray-and-settings** (App): N10–N12, settings window (policy fields,
  exclusions list, shortcut recorder using `Win32KeyInput` and
  `ShortcutEditor`, hotkey status display), start at login.
- **29d virtual-desktops** (Win32 + router): O2.
- #24 multi-display hand-off and #14 crash journal remain separate.
- Licensing / feature gate: none in CanvasWM. The studied start path has
  licensing neighbours (`setUpLicense`, `onLocked`, upgrade prompt); they
  were identified by name only and not analysed.

## Decisions needed from the human
1. UI framework (L): WinForms recommended.
2. Scope split (above) and order (29a → 29b → 29d/29c), and whether to block
   user release on #14.
3. Virtual desktops for the first build (O3).
4. Display key source (M4): DISPLAYCONFIG device path vs `\\.\DISPLAYn`.
5. Where file I/O for stores lives (M6) and data folder (`%LOCALAPPDATA%`).
6. Single-instance behaviour (N2) and start-at-login default (N11, off).
7. Tick driver (N6): DwmFlush thread recommended.
8. `FocusLeftWindows` placeholder semantics (M1) until traced.
