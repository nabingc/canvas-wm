# Feature: win32-window-enum

Feature #10. Lands in `CanvasWm.Win32` (OS adapter only), with a pure
classification/reconciliation part proposed for Core/Engine (see Part 2 §K).
Builds on `findings/engine-interfaces.md` (keys, `WindowInfo`, `FrontToBack`,
`IsShowing`), `findings/engine-inbound-api.md` (rules 10–21) and
`findings/engine-window-events.md` (Windows notes).

## Summary
The studied app has a "window tracker" that watches every regular
application, asks each for its windows, keeps the ones that look like real,
resizable document windows of at least 120×80, and tells the engine when such
a window appears, disappears (temporarily or for good), moves, changes title
or gets focus. A window is "dockable" (may be tiled) only if the OS says it
can go full screen; that flag can later flip from false to true but never
back. Events are not debounced, but new-window notifications trigger two
delayed re-scans (0.05 s and 0.4 s) and the whole system is re-scanned every
3 s as a safety net. Part 2 maps this onto `EnumWindows` + `SetWinEventHook`
and proposes moving the decision logic into Core/Engine so it can be tested
on the Mac.

---

# Part 1 — Studied behaviour (macOS)

## Behaviour

### A. Which applications are watched
1. An application is watched only if all hold: it is not this window
   manager's own process; it has not terminated; it is a **regular** (normal
   UI, dock-visible) application — background/accessory/agent apps are never
   watched; and its bundle identifier is **not** in the user's exclusion list
   (a string list stored in preferences). An app with no bundle identifier
   cannot be excluded and is watched. The "is regular" answer is cached per
   process id.
2. For each watched app the tracker installs one OS observer on the app and,
   per admitted window, one observer on that window. All OS queries for one
   app run on a private serial queue for that app; results are handed to the
   UI thread. Every OS query to an app has a **1.0 s timeout**, so a hung
   app cannot stall the tracker.
3. App-level notifications observed: window created, focused window changed,
   focused UI element changed, application hidden, application shown.
   Window-level notifications observed: element destroyed, moved, resized,
   minimised, de-minimised, title changed.
4. Workspace-level notifications: app **launched** → start watching it after
   **0.3 s**; app **terminated** → forget it (all its windows removed
   permanently, observer dropped); app **activated** → focus report for that
   app (rule 17, no de-duplication).
5. When the exclusion list changes: every engine releases the windows of
   newly excluded apps (engine `release(key)`, inbound-api rule 14); the
   tracker removes those windows permanently and drops their observers; then
   a full re-scan picks up apps that are no longer excluded.

### B. Which windows are managed ("qualifying")
6. For each window the app reports, the tracker builds a probe. A window
   without a valid OS window number is ignored entirely.
7. The window **qualifies** only if all hold:
   - its role is "window" and its subrole is "standard window" (sheets,
     dialogs, floating panels, system dialogs, popovers, menus, tooltips
     have other roles/subroles and are excluded);
   - it is **not minimised**;
   - it is **not in native full-screen mode**;
   - its frame can be read;
   - width ≥ **120** and height ≥ **80** (points);
   - its size attribute is settable (the window manager could resize it).
8. A qualifying window is admitted only if it is not already tracked and an
   app-layer **acceptance test** passes (rule 9). Scans also skip all windows
   of an app that is currently **hidden**.
9. Acceptance test (coordinator level, display-aware): accept if the window's
   **centre point** lies inside any display's frame; otherwise accept if any
   engine already holds this window key (it is, e.g., parked off-screen by a
   previous session); otherwise accept only if **less than 5 %** of the
   window is visible on any display. So a window whose centre is off all
   displays but which still shows ≥ 5 % on screen is **not** admitted.
10. On admission the tracker records: key, OS window number, process id, app
    name (the app's display name, or the literal "App" if missing), bundle id
    (optional), icon, title (empty if unreadable), frame, dockable flag.
    It then asks the coordinator which display owns the window and, if the
    window is on that display's **currently shown virtual desktop** (or its
    desktop is unknown), delivers it to that display's engine as
    `windowAdded(info, held: false)`. Windows on other desktops stay tracked
    but are delivered later through the engine's `start/resume(with:)` list
    when that desktop becomes current (inbound-api rules 10, 12).

### C. Dockable vs merely managed
11. **Dockable** = the window has the OS full-screen button and that button
    is enabled (if its enabled state cannot be read, it counts as enabled).
    No full-screen button → not dockable. So a qualifying but fixed-size
    window (e.g. a preferences window) is managed (focus, release, removal)
    but floated, not tiled.
12. Dockable is re-checked on every scan of the app and on every focus report
    for that window. It can only change **false → true**; when it does, the
    tracker sets the flag and calls engine `windowBecameDockable(key)`. It is
    never revoked while the window stays tracked.

### D. Removal: temporary vs permanent
13. Window **destroyed** notification → `windowRemoved(key, temporary: false)`.
14. Window **minimised** → `windowRemoved(key, temporary: true)`.
15. App **hidden** → every tracked window of that app →
    `windowRemoved(key, temporary: true)`.
16. During a scan of an app, a tracked window that the app no longer lists
    as qualifying is removed when the app is hidden **or** the window number
    no longer exists anywhere in the system window list; `temporary` is true
    when the window still exists, false when it is gone. A window that still
    exists while its app is visible is kept (it may just be on another
    desktop). App terminated / excluded → permanent removal (rules 4, 5).
17. Every removal also drops the tracker record (so a de-minimised or unhidden
    window comes back as a fresh admission → `windowAdded`), stops its live
    capture, and is logged. Desktop (Space) switching does **not** produce
    removals; it is handled by engine `suspend/resume` (inbound-api 11–12).
18. Re-appearance: window created, de-minimised, or app shown → the tracker
    schedules **two** re-scans of that app on the UI thread, after **0.05 s**
    and after **0.4 s** (apps often finish setting up a new window after the
    notification). Re-scans of the same app while one is already running are
    no-ops (one scan in flight per app).

### E. Move, resize, title
19. Moved or resized → the frame (position + size) is read on the app's
    queue; on the UI thread, if the window is still tracked and the frame
    **differs** from the stored one, the stored frame is updated, a size
    change also tells the capture service the window resized, and the engine
    gets `windowMoved(key, frame)`. Identical frames are dropped. There is no
    time-based debounce and no filtering of the manager's own writes here
    (the engine recognises its own writes, inbound-api rule 19).
20. Title changed → title read on the app queue; on the UI thread the stored
    title is replaced, a fresh capture snapshot is requested, and the engine
    gets `windowTitleChanged(key, title)` (no de-duplication observed).

### F. Focus
21. Focus is reported per app ("focus report"). The tracker asks the app for
    its focused window (on the app queue), reads its window number and
    full-screen-button state, then on the UI thread:
    - if that window is tracked, re-check dockable (rule 12);
    - **de-duplication**: for reports caused by "focused UI element changed"
      only, if the focused window is the same as the last one reported for
      that app, stop here; "focused window changed" and "app activated"
      always report;
    - remember it as the app's last reported focused window;
    - tracked → the coordinator snapshots the old/new focused windows and
      calls engine `windowFocused(key)` on the engine holding the window;
    - not tracked, or the app has no focused window → every engine gets
      `focusLeftWindows(of: pid)` (focus moved to something unmanaged of
      that app).
22. App activation also reaches the engine as `appActivated(pid,
    ownsDesktop)` (inbound-api rule 20) via the coordinator.

### G. Start-up, periodic rescan, ordering
23. Start: subscribe to workspace notifications (rule 4), then begin
    watching every running app that passes rule 1 (each does a full scan).
    The "ready" callback fires once — when all those initial scans have
    finished or after a **0.6 s** deadline, whichever comes first (pairing
    of the deadline with the run-once guard Inferred) — and marks the initial
    scan complete; the coordinator then starts the engines with the windows
    of each current desktop.
24. A repeating **3.0 s** timer re-scans: watched apps are scanned again;
    running apps not yet watched are re-tested (rule 1) and watched if they
    now pass. This catches missed notifications.
25. Front-to-back order (`frontToBack`) comes from the OS z-ordered window
    list of the current desktop (including off-screen windows), filtered to
    tracked keys; `isShowing` uses the OS on-screen list cached for ~0.1 s
    (engine-interfaces rules 13–14, already specified).
26. Identity: key = `w` + OS window number in decimal; the number is stable
    for the life of the window and is never reused by the tracker while
    tracked. A window first reported again after a removal is a new
    admission with the same key.

## Data model (neutral)
- `TrackedWindow`: key, osWindowId, pid, appName, appId?, title, frame,
  dockable, (OS handle), writesInFlight (used by the frame writer).
- `Probe` (per scan, per window): osWindowId, qualifies (bool), frame?,
  title, dockableOffer (bool).
- Tracker state: tracked windows by key; watched apps by pid;
  "is regular app" cache by pid; scans-in-flight set by pid; last reported
  focused key by pid; initial-scan-complete flag.
- Constants: min size 120×80; AX timeout 1.0 s; re-scan delays 0.05 s and
  0.4 s after a create/show event; launch delay 0.3 s; start deadline 0.6 s;
  periodic re-scan 3.0 s; acceptance off-screen threshold 5 %;
  on-screen cache 0.1 s.

## Worked examples (studied rules)
1. App "Editor" (regular, not excluded) reports window 4211: standard, not
   minimised, 900×700 at (100,100) on display 0, resizable, full-screen button
   enabled → admitted as `w4211`, dockable = true, `windowAdded`.
2. Same app reports a 400×300 fixed-size preferences window (full-screen
   button disabled) → admitted, dockable = false (floated by the engine).
3. A 100×60 standard window → not admitted (below 120×80).
4. A sheet / dialog / floating panel (non-standard subrole) → not admitted.
5. Window centre at (−500, 300), 600 px wide, so 10 % visible, no engine holds
   it → rejected (≥ 5 % visible). The same window at 3 % visible → accepted.
6. Minimise `w4211` → `windowRemoved(w4211, temporary: true)`; de-minimise →
   re-scans at +0.05 s and +0.4 s → `windowAdded(w4211, …)` (engine restores
   its place from its away table).
7. Hide the app (2 tracked windows) → two temporary removals.
8. Close `w4211` → `windowRemoved(w4211, temporary: false)`.
9. A scan finds tracked `w5` missing from the app's list and absent from the
   system window list → permanent removal; present in the system list and the
   app visible → kept.
10. Move: stored (0,0,800,600), OS reports (0,0,800,600) → nothing; reports
    (10,0,800,600) → `windowMoved(w, (10,0,800,600))`, no capture-resize;
    reports (10,0,820,600) → `windowMoved` + capture told it resized.
11. Focus: "focused UI element changed" twice inside `w1` → one
    `windowFocused(w1)`; "focused window changed" to `w1` again → reported
    again. Focus to an untracked palette of the same app →
    `focusLeftWindows(pid)` on every engine.
12. Window `w7` admitted with dockable = false; later focus report sees the
    full-screen button enabled → `windowBecameDockable(w7)`; a later report
    seeing it disabled → no change.

## Evidence
All addresses in the shipped binary; notes in
`evidence/win32-window-enum-notes.txt`.
- Rule 1: `WindowTracker.manageable` 0x100203c0c; exclusion key string
  `excludedBundleIDs` 0x10021cb80 (`dec-win-enum-tracker.json`).
- Rules 2–3: `AppObserver.init` 0x1001270a0 (1.0 s messaging timeout; app
  notification array 0x100262360), `AppObserver.watch` 0x100127448 (window
  notification array 0x1002622c0, bytes read via REA read_bytes);
  `WindowTracker.queue(for:)` 0x1002026b4.
- Rule 4: `WindowTracker.start(ready:)` 0x100200530 and closures 0x100202a20
  (0.3 s at 0x100202c3c), 0x100203158 (`forget`), 0x100203698
  (`reportFocus(changes:false)`); `forget(pid:)` 0x1002032f4.
- Rule 5: `Coordinator.applyExclusions` 0x10015d750,
  `WindowTracker.forgetExcludedApps` 0x100202248.
- Rules 6–7: `WindowTracker.probe` 0x1002090f4 (asm read directly: role,
  subrole, minimised, full-screen, frame, 120.0 / 80.0 compares, size
  settable).
- Rule 8: scan main closure 0x100205efc, qualifies test at 0x100206430,
  hidden test 0x100206438; `admit` 0x1002068fc (frame-missing check
  0x100206b44, acceptance closure call 0x100206b90).
- Rule 9: `Coordinator.accepts(id:frame:)` 0x10015e718 (display loop,
  `Engine.holds` 0x10015eac4, 0.05 threshold 0x10015eb3c).
- Rule 10: `admit` (TrackedWindow fields, "App" fallback 0x100206ce4);
  `Coordinator.owner(of:)` 0x10016cd4c, `onShownSpace` 0x100173c78,
  `arrive` 0x100170048 (`windowAdded(…, held: false)`)
  (`dec-win-enum-coordinator.json`).
- Rules 11–12: probe 0x10020943c–0x100209498 (full-screen button, enabled
  tri-state); `recheckDockable` 0x1002055fc; callers in scan 0x1002064f4 and
  reportFocus 0x1002080b4.
- Rules 13–15, 18: `WindowTracker.handle` 0x100204168 (destroyed → 0x1002049bc
  temporary 0; minimised → temporary 1; hidden loop 0x100204ac8; delayed
  scans 0.05 / 0.4 at 0x1002043b4 / 0x1002045b8, closure 0x100208844).
- Rule 16: scan main closure 0x100205efc + `existingWindowIDs` 0x100209604.
- Rule 17: `remove(_:temporary:)` 0x100207544.
- Rule 19: handle closure #5 0x1002088a8 and 0x100208bc8.
- Rule 20: handle closure #7 0x1002084a4 and 0x100208714.
- Rule 21: `reportFocus` 0x100203838, closures 0x100207a74, 0x100207f28
  (`reportedFocus` dedupe, `focusLeftWindows`),
  `Coordinator.tracker(_:focused:)` 0x100173e1c.
- Rules 23–24: start 0.6 s 0x100201364, run-once closure 0x100203b0c,
  3.0 s timer 0x1002014fc, `rescan` 0x100201814, `observe` 0x100202e5c,
  `scan` 0x100205290 (in-flight set).
- Rules 25–26: `findings/engine-interfaces.md` rules 1, 13, 14.
- Raw files saved this run: `evidence/dec-win-enum-tracker.json`,
  `evidence/dec-win-enum-tracker-closures.json`,
  `evidence/dec-win-enum-coordinator.json`,
  `evidence/win32-window-enum-notes.txt`.

## Confidence
| Rule | Label |
|---|---|
| 1 app filter | Observed |
| 2 per-app queue, 1.0 s timeout | Observed |
| 3 notification sets | Observed |
| 4 launch 0.3 s / terminate / activate | Observed |
| 5 exclusions | Observed (calls); exact ordering Inferred |
| 6–7 qualifying test, 120×80 | Observed |
| 8 skip hidden app, not-tracked, acceptance | Observed |
| 9 acceptance test | Observed constants; "centre point" reading of the containment call Inferred |
| 10 admission fields, desktop routing | Observed (calls); space semantics Inferred |
| 11 dockable = full-screen button enabled | Observed |
| 12 one-way upgrade | Observed |
| 13–15 | Observed |
| 16 scan reconciliation | Inferred (two key sets in the scan; the exact role of the second set not fully resolved) |
| 17 | Observed |
| 18 0.05 s / 0.4 s re-scans, one scan per app | Observed |
| 19 move/resize, change-only | Observed |
| 20 title, no dedupe | Observed (no comparison seen) |
| 21 focus reporting and dedupe | Observed |
| 22 | Observed (inbound-api) |
| 23 start, 0.6 s deadline | Observed constants; deadline/group pairing Inferred |
| 24 3 s rescan | Observed |
| 25–26 | Observed (engine-interfaces) |

## Unknowns
- **Second key set in scan reconciliation (rule 16)**: whether a tracked
  window that the app still lists but that no longer qualifies (e.g. resized
  below 120×80) is removed, and with which `temporary` value. Minimise and
  full-screen already have their own paths; for CanvasWM treat "no longer
  qualifies, still exists" as a temporary removal.
- **Native full-screen entry**: no explicit notification is observed; a
  window entering full screen stops qualifying and is presumably handled by
  rule 16 on the next scan (≤ 3 s) — exact path unknown.
- **`held` argument**: always false on every observed path (also Unknown in
  inbound-api).
- Focused-window fallback when the app reports no focused window (the
  reportFocus queue closure also consults the focused UI element); fine
  detail not needed for Windows.
- Multi-display/desktop ownership (`Coordinator.owner(of:)`, saved owners,
  visible-fraction tie-breaks) — belongs to the multi-display feature, not
  analysed here.

---

# Part 2 — CanvasWM design input (NOT studied behaviour)

Everything below is a proposal for the spec, based on Microsoft
documentation knowledge, not on TrellisWM.

## H. Enumeration and the "manageable" test on Windows
H1. Initial enumeration: `EnumWindows` (top-level windows only, delivered in
    z-order, topmost first). For each HWND gather a neutral **facts**
    record (§K) and classify. Unlike macOS, enumeration is synchronous, so
    the 0.6 s ready deadline is unnecessary: classify all, then start engines.
H2. Mapping of studied rules to Win32 facts:

| Studied rule | Win32 fact(s) |
|---|---|
| own process excluded | `GetWindowThreadProcessId` pid == `Environment.ProcessId`; also use `WINEVENT_SKIPOWNPROCESS` |
| regular app only | no direct analogue; use the window-level "would appear in Alt-Tab/taskbar" test: visible, unowned (`GetWindow(GW_OWNER)` null) or `WS_EX_APPWINDOW`, not `WS_EX_TOOLWINDOW`, not `WS_EX_NOACTIVATE` |
| excluded apps | AppId (H4) in a user exclusion list |
| standard window, not sheet/dialog/panel/popover | top-level (`GetAncestor(h, GA_ROOT) == h`), not `WS_CHILD`, has `WS_CAPTION` (or `WS_THICKFRAME` for custom-chrome apps), unowned; owned windows (dialogs, property sheets, find boxes) excluded; class `#32770` owned dialogs excluded; shell windows (`Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`) excluded |
| not minimised | `IsIconic` false |
| not full screen | not `IsZoomed`-with-monitor-rect exclusive full screen: window rect equals `MONITORINFO.rcMonitor` and no `WS_CAPTION`; (maximised windows are **not** full screen and stay manageable) |
| visible / on this desktop | `IsWindowVisible` and `DwmGetWindowAttribute(DWMWA_CLOAKED) == 0` |
| frame readable, ≥ 120×80 | `DWMWA_EXTENDED_FRAME_BOUNDS` (fallback `GetWindowRect`), converted to DIPs; width ≥ 120, height ≥ 80 DIP |
| size settable | process accessible for `SetWindowPos` (UIPI: a higher-integrity window cannot be moved by a medium-integrity process → exclude, or manage only when running elevated) |
| dockable = full-screen button enabled | `WS_THICKFRAME` **and** `WS_MAXIMIZEBOX`, and the maximize item not greyed (`GetSystemMenu` → `SC_MAXIMIZE` enabled) — closest analogue of "can be made full-size" |
| hidden app (temporary) | window hidden (`EVENT_OBJECT_HIDE`) but `IsWindow` still true |
| gone (permanent) | `IsWindow` false / `EVENT_OBJECT_DESTROY` |
| acceptance (centre on a display, held, or < 5 % visible) | `MonitorFromPoint(centre, MONITOR_DEFAULTTONULL)`; engine `Holds(key)`; `VisibleFraction(rect) < 0.05` |

H3. Cloaking kinds (`DWMWA_CLOAKED` value): `DWM_CLOAKED_SHELL` (another
    virtual desktop, or shell-hidden) → treat like "not on the shown
    desktop" (rule 10: keep tracked, do not deliver / temporary removal);
    `DWM_CLOAKED_APP` (e.g. suspended packaged apps) → temporary;
    `DWM_CLOAKED_INHERITED` → same as owner.
H4. AppId and AppName: AppId = full image path from
    `QueryFullProcessImageName` (lower-cased) for classic apps; for packaged
    apps hosted by `ApplicationFrameHost.exe` use the window's AUMID
    (`SHGetPropertyStoreForWindow` → `PKEY_AppUserModel_ID`) and the real
    process of the hosted `Windows.UI.Core.CoreWindow` child. AppName =
    `FileDescription` from version info, else the exe name without
    extension, else "App" (studied fallback). Cache per pid; drop on process
    exit.
H5. Title: `GetWindowText` sends `WM_GETTEXT` to the owning thread and can
    hang on a hung app. Use `InternalGetWindowText` or
    `SendMessageTimeout(WM_GETTEXT, SMTO_ABORTIFHUNG, 1000 ms)` — mirrors the
    studied 1.0 s timeout. Skip windows where `IsHungAppWindow` is true until
    they recover (re-tested by the periodic rescan).

## I. WinEvent hook design
I1. One or a few `SetWinEventHook` registrations on the UI thread with
    `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`. Out-of-context
    callbacks are delivered on the installing thread through its message
    loop, so that thread must pump messages; this is the engine's UI thread
    (inbound-api rule 7). Keep the delegate rooted (GC) for the hook lifetime;
    `UnhookWinEvent` on shutdown.
I2. Filter every callback to `idObject == OBJID_WINDOW && idChild ==
    CHILDID_SELF` and to top-level HWNDs (`GetAncestor(GA_ROOT) == hwnd`);
    `EVENT_SYSTEM_*` events are already window-level. Ignore events for
    untracked windows except the "may have appeared" set.
I3. Event mapping to studied rules:

| Event | Action |
|---|---|
| `EVENT_OBJECT_CREATE`, `EVENT_OBJECT_SHOW`, `EVENT_OBJECT_UNCLOAKED`, `EVENT_SYSTEM_MINIMIZEEND` | "may have appeared": re-evaluate that HWND after 0.05 s and again after 0.4 s (rule 18); admit if it qualifies and is accepted |
| `EVENT_OBJECT_DESTROY` | tracked → `WindowRemoved(key, temporary: false)` |
| `EVENT_SYSTEM_MINIMIZESTART` | tracked → `WindowRemoved(key, temporary: true)` |
| `EVENT_OBJECT_HIDE` | tracked → after a short re-check: `IsWindow` false → permanent, else temporary (rule 15/16) |
| `EVENT_OBJECT_CLOAKED` | tracked → temporary removal (unless the cloak is a virtual-desktop switch handled by Suspend, see I5) |
| `EVENT_SYSTEM_FOREGROUND` | focus report (rule 21) + `AppActivated(pid, ownsDesktop)`; tracked → `WindowFocused`; untracked top-level of a process → `FocusLeftWindows(pid)` |
| `EVENT_OBJECT_LOCATIONCHANGE` | tracked → read frame; report `WindowMoved` only if changed (rule 19) |
| `EVENT_SYSTEM_MOVESIZESTART` / `END` | pointer-drag bracket for the engine (see engine-window-events Windows notes) |
| `EVENT_OBJECT_NAMECHANGE` | tracked → `WindowTitleChanged` (studied: no dedupe; optional dedupe is harmless) |

I4. Coalescing: `EVENT_OBJECT_LOCATIONCHANGE` fires many times per drag.
    The studied app only drops identical frames; CanvasWM may additionally
    coalesce per window to at most one read per message-loop turn (or per
    ~16 ms) — design choice, flag in the spec as not studied.
I5. Periodic safety rescan every 3.0 s (rule 24): `EnumWindows` again,
    admit newly qualifying windows, upgrade dockable (rule 12), and
    reconcile tracked keys that no longer qualify (rule 16).
I6. Virtual desktops: `IVirtualDesktopManager.GetWindowDesktopId` per window
    gives the desktop GUID for the engine key; `IsWindowOnCurrentVirtualDesktop`
    for "on the shown desktop". There is no public desktop-switched event:
    detect it from `EVENT_SYSTEM_FOREGROUND` (new foreground on another
    desktop id) and a burst of `CLOAKED/UNCLOAKED`, then let the app layer
    `Suspend` / `Resume` engines rather than emitting removals (rule 17).
I7. Z-order (`FrontToBack`): walk `EnumWindows` (or
    `GetTopWindow` + `GetWindow(GW_HWNDNEXT)`) and keep tracked keys.
    `IsShowing` = visible, not iconic, not cloaked, cached ≤ 0.1 s.

## J. Identity
J1. Key = `"w"` + HWND as a decimal integer (engine-interfaces rule 1 /
    orchestrator request). HWNDs are recycled after destruction. Mitigation:
    always remove on `EVENT_OBJECT_DESTROY`; store (pid, thread id) with the
    tracked record and, if an admission finds the same HWND with a different
    pid/thread, first emit `WindowRemoved(old, temporary: false)`.
J2. HWND values are 64-bit on x64 but only the low 32 bits are significant;
    format the `nint` value as decimal without sign issues (use `ulong`).

## K. Recommended split (Core/Engine vs Win32)
Per CLAUDE.md, Win32 must not decide policy. Proposed:
- **Core** (`CanvasWm.Core`, pure, Mac-tested): a `WindowFacts` record
  (top-level, owned, child, visible, cloak kind, iconic, full screen,
  has caption, resizable, maximize box enabled, tool window, app window,
  no-activate, accessible-for-move, frame in DIPs, pid, appId, is own
  process, is shell window) and a static `WindowClassifier.Classify(facts,
  policy)` → `Ignore | Managed(dockable: bool)` implementing rules 1, 7, 11
  with the 120×80 minimum and the exclusion list as policy values; plus
  `RemovalKind(stillExists, appHidden)` → temporary/permanent (rules 13–16).
- **Engine** (Mac-tested with fakes): a `WindowTracker` state machine that
  owns the tracked set, the "last reported focus per pid" dedupe (rule 21),
  the one-way dockable upgrade (rule 12), the 0.05 s / 0.4 s re-check
  scheduling via `IClock.After`, the 3 s rescan, and the acceptance test
  (rule 9, using `IWindowSystem.VisibleFraction` and engine `Holds`). It
  consumes a new OS seam, e.g. `IWindowSource` { `Enumerate()` →
  facts in z-order; `Describe(handle)` → facts?; event stream of raw
  (kind, handle) notifications } and produces the inbound engine calls.
  Routing to the right per-display engine stays in the app-composition
  feature (#29).
- **Win32** (`CanvasWm.Win32`): only `IWindowSource`: `EnumWindows`,
  facts gathering (styles, DWM attributes, DPI conversion, process path /
  AUMID, timeouts), and the `SetWinEventHook` registration with the
  OBJID/CHILDID/top-level filter translating events into raw notifications.
  No thresholds, no exclusion logic, no temporary/permanent decision.

## Windows notes
- Elevated (admin) windows cannot be moved by a non-elevated CanvasWM (UIPI);
  the facts record should carry "accessible" so Core can exclude them.
- Maximised windows are not "full screen"; they must be restored before the
  frame writer can size them (frame-writer feature #11).
- Packaged/UWP windows: the visible top-level is `ApplicationFrameWindow`
  owned by `ApplicationFrameHost.exe`; AppId must come from the AUMID.
- `EVENT_OBJECT_SHOW` often precedes the final style/title/size; the two
  delayed re-checks (0.05 s, 0.4 s) cover this, and the 3 s rescan covers
  missed events.
- Hook callbacks are re-entrant with respect to the message loop (a
  `SendMessage` inside a callback can deliver another callback); queue raw
  events and process them after the callback returns.
- Licensing / feature gating: nothing in the tracker touched it.
