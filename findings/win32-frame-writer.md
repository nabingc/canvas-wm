# Feature: win32-frame-writer

Feature #11. Lands in `CanvasWm.Win32` (adapter only). Builds on
`findings/engine-interfaces.md` (IWindowSystem contract, rules 12–21),
`findings/engine-frame-writer.md` (what the Engine does with results),
`findings/park-geometry.md` (park frames, visible fraction, fallback origin)
and `findings/win32-window-enum.md` / `specs/win32-window-enum.md` (DIP space
A8/B10, handles, threading A13). Refusal detection, rechecks and limit
learning are Engine policy (#26) and are **not** part of this feature.

## Summary
The studied macOS adapter writes a window frame on a private per-window
background queue: it sets the size, then the position (skipping parts that
already match its cached frame), reads the frame back, and if any edge is off
by more than 1 point it writes size and position once more and reads again.
For park writes it pushes the position a further time if the window still
shows more than 2 % on screen. The result is handed back to the UI thread,
where the cached frame is updated and the Engine's completion runs. Focus
activates the app, then raises the window and marks it main. Part 2 maps this
onto Win32 (batched `DeferWindowPos`, DWM invisible-border compensation,
inverse DIP mapping, restore-before-write, worker thread, focus rules) and
proposes which small decisions move to Core/Engine.

---

# Part 1 — Studied behaviour (macOS)

## Behaviour

### A. setFrame(key, target, completion) — UI-thread part
1. Unknown key (no tracked window) → completion(null), posted to the UI
   thread (already in engine-interfaces rule 17).
2. The adapter computes the **visible fraction of the target** against the
   current screens. The write is an *off-screen write* when that fraction is
   **< 0.05**. (Corrects "about 0.1" in engine-interfaces rule 17; the
   literal is 0.05.)
3. It takes a snapshot of all screen frames (in its y-down space) on the UI
   thread and hands it to the background write, so the background code never
   queries screens itself.
4. Before dispatching, on the UI thread: the tracker's cached frame for the
   window is replaced with the new value (Inferred: the target), the
   previous cached frame is passed along as "current", and the window's
   writes-in-flight counter is incremented.
5. The write runs on that window's own **serial queue**, created lazily on
   first use, named by a fixed prefix plus the window key, at the highest
   (user-interactive) priority, and kept for the life of the process. Writes
   to one window therefore run strictly in issue order; writes to different
   windows run in parallel. There is no batching.

### B. The write itself (background)
6. **Accessibility mode off.** For the duration of the write the app's
   "enhanced user interface" accessibility flag (which makes some apps animate
   or resist programmatic resizes) is switched off if it was on, and restored
   afterwards. Overlapping writes for the same process share one hold
   (count per process under a lock); only the first turns it off and the last
   restores it. Logged once per process.
7. **Pass 1:** write the **size** (only if no current frame is known or the
   size differs exactly from it), then the **position** (only if no current
   frame is known or the origin differs exactly). Size first, position second
   — for both growing and shrinking; no special ordering by direction.
8. **Read back** position and size. If either cannot be read → the result is
   null.
9. **Pass 2 (settle):** if the read-back differs from the target by
   **more than 1.0** point on **any** of x, y, width, height, write size and
   then position again (unconditionally), read back again, and use the
   second reading (or the first reading if the second read fails). There is
   no third attempt.
10. **Off-screen re-push:** if the write was an off-screen write (rule 2), the
    result is non-null and the result's visible fraction against the
    snapshot (rule 3) is **> 0.02**, the position is written once more and
    the frame read again; that reading becomes the result (Inferred: falls
    back to the previous reading if the re-read fails). This counters the OS
    pulling a window back on-screen.
11. **Timing log:** the elapsed time of the whole write is measured; writes
    taking more than **120 ms** are logged. Diagnostics only; no behaviour
    change.

### C. Completion (UI thread)
12. Writes-in-flight −1. If the result is non-null, it becomes the tracker's
    cached frame. Then the Engine's completion is called with the result
    (null passes through). The adapter never judges the result: no refusal
    detection, no retries beyond rules 9–10 (refusal handling is in the
    Engine, `findings/engine-frame-writer.md` D/E).
13. Consequence (Inferred): because the cached frame is set to the target
    before the write and to the actual after it, the OS move/resize
    notifications caused by the engine's own write usually match the cached
    frame and are dropped by the tracker's change-only filter
    (`findings/win32-window-enum.md` rule 19) before reaching the Engine; the
    Engine's echo list (engine-frame-writer rules 24–25) covers the rest.

### D. readFrame(key, completion)
14. Unknown key → completion(null) on the UI thread.
15. The read runs on the **per-application** queue (the tracker's queue for
    the window's process), **not** on the per-window write queue. So a read
    issued after a write to the same window is not ordered after it in the
    studied app. (CanvasWM's contract B6 deliberately requires ordering.)
16. On the UI thread a non-null result also becomes the tracker's cached
    frame, then the completion runs.

### E. focus(key)
17. Unknown key → nothing.
18. On the UI thread: if the window manager itself is the active app, it
    first **yields activation** to the target app; then it **activates the
    target app**; then it broadcasts an internal "raising window" notification
    (overlays react to it).
19. Then, asynchronously on the target app's queue: perform the window's
    **raise** action, then set the window's **main** attribute (Inferred: to
    true). Order: app activation → raise window → make main. No result is
    reported; focus changes come back through the tracker's focus path.

### F. Other members (already established; summarised)
20. `frontToBack`: OS z-ordered list of windows (all layers except desktop
    elements, off-screen windows of the current desktop included), mapped to
    keys and filtered to tracked windows. (engine-interfaces rule 13.)
21. `isShowing`: membership in the OS on-screen window list, cached and
    re-read at most every **0.1 s**. (engine-interfaces rule 14.)
22. `visibleFraction(rect)`: Σ area(rect ∩ screen full frame) ÷ area(rect),
    0 for an empty rect. (park-geometry rule 19.)
23. `pointer`: global mouse location converted to y-down. `mouseDown`:
    primary button bit of the pressed-buttons mask. (engine-interfaces 19–20.)
24. `parkingOrigin`: bottom-right corner of the right-most screen minus 1 on
    each axis; (10000, 10000) with no screens. (park-geometry rule 13.)
25. `tabSibling`: macOS native tabs only; not relevant on Windows.

## Data model (studied, neutral)
- Tracked window (adapter-relevant fields): OS handle, process id, cached
  frame, writes-in-flight counter.
- Per-window serial write queue, keyed by window key, never removed.
- Per-process accessibility-mode hold: count + "restore on release" flag.
- Write work item: target, "current" (cached frame or none), off-screen flag,
  screen-frame snapshot, completion.
- Constants: off-screen classification 0.05 (visible fraction of the target);
  settle tolerance 1.0 per component (strictly greater triggers pass 2);
  off-screen re-push threshold 0.02 (strictly greater); slow-write log
  120 ms; on-screen cache 0.1 s; fallback origin (10000, 10000).

## Worked examples (studied rules)
1. Cached frame (0,0,800,600), target (0,0,800,600)… (the Engine normally
   skips this; if issued) pass 1 writes nothing, read-back (0,0,800,600),
   within 1 → result (0,0,800,600).
2. Cached (0,0,800,600), target (100,0,800,600): pass 1 writes position only;
   read-back (100,0,800,600) → result.
3. Cached (0,0,800,600), target (0,0,400,600), app clamps width to 500:
   pass 1 writes size; read-back (0,0,500,600) → width off by 100 > 1 →
   pass 2 writes size and position again; read-back (0,0,500,600) → result
   (0,0,500,600). The Engine then sees a refusal (500 − 400 > 24).
4. Target (300,25,600,400) on a 1440-wide screen, window cached at
   (0,25,600,400); after pass 1 the app reports (300.5,25,600,400) → all
   within 1 → result (300.5,25,600,400) (no pass 2).
5. Park write: target (1439,899,705,423), fraction of target on screen ≈
   0.000003 < 0.05 → off-screen write. After passes 1–2 the OS left the
   window at (1439,860,705,423) (pulled up 39): visible fraction ≈ 0.055 >
   0.02 → position written again → re-read (1439,899,705,423) → result.
6. Read failure: window closed mid-write → read-back null → completion(null).
7. Focus while the manager is frontmost: yield activation → activate app →
   "raising" broadcast → (background) raise → main.

## Evidence
All addresses in the shipped binary; raw notes in
`~/Documents/trelliswm-re/evidence/win32-frame-writer-notes.txt` (new).
- Rule 1, 14, 17: `MacWindowSystem.setFrame` 0x100185824, `readFrame`
  0x1001868bc, `focus` 0x100187024 (`dec-engine-seams-windowsystem.json`).
- Rule 2: setFrame calls `Screens.visibleFraction` at 0x1001859d8; literal
  0.05 built at 0x100185e48–0x100185e58, `fcmp`/`cset mi` at 0x100185e64.
- Rule 3: setFrame NSScreen loop 0x1001859e4–0x100185c1c building a rect list
  passed into the closure context.
- Rule 4: stores to TrackedWindow `frame` (+0x68..+0x80) and
  `writesInFlight` (+0x88) in setFrame (`dec-engine-seams-windowsystem.json`
  item 1); field names from symbol table (`symbols/all-defined.demangled.txt`
  lines 7990–8001).
- Rule 5: `MacWindowSystem.writeQueue` 0x100185514 (label prefix
  `spaces.ax.write.`, QoS userInteractive, cached dictionary).
- Rule 6: `AX.withoutEnhancedUserInterface` specialisation 0x1001298c0
  (`dec-noeui.json`); study §4 step 1.
- Rules 7–9: `AX.setFrame` 0x100129348 (`dec-setframe.json`); AXValue type 2
  (size) before type 1 (point); exact-equality skips at 0x1001293ac/0x100129410;
  1.0 tolerances at 0x1001294d0, 0x10012950c, 0x100129548, 0x100129588;
  second write + `AX.frame` 0x100129220 re-read.
- Rule 10: `dec-noeui.json` (visibleFraction > 0x3f947ae147ae147b = 0.02 →
  position write + `AX.frame`).
- Rule 11: closure #3 0x1001860b0: `CACurrentMediaTime` before/after,
  ×1000 at 0x10018629c, compare 120.0 at 0x1001862a8.
- Rules 12–13: closure #2 in closure #3 0x1001867c0 (in-flight −1, frame
  store at +0x68/+0x78 when non-nil, completion call).
- Rules 15–16: readFrame uses `WindowTracker.queue(for:)` at 0x1001869d8;
  read closure 0x100186ce0 → `AX.frame`; main closure 0x100186f3c stores
  +0x68/+0x78.
- Rules 18–19: focus pseudocode (`yieldActivationToApplication:`,
  `activateWithOptions:0`, `raising` notification, per-app queue); closure
  0x100187398: `AXUIElementPerformAction` with literal "AXRaise", then
  `AXUIElementSetAttributeValue` with literal "AXMain".
- Rules 20–25: `findings/engine-interfaces.md` rules 13–15, 18–21;
  `findings/park-geometry.md` rules 13, 19.

## Confidence
| Rule | Label |
|---|---|
| 1, 14, 17 | Observed |
| 2 off-screen < 0.05 | Observed |
| 3 screen snapshot | Observed (list built and passed); y-flip detail Inferred |
| 4 cached frame/counter update before write | Observed (stores); value = target Inferred |
| 5 per-window serial queue | Observed |
| 6 accessibility-mode hold | Observed |
| 7 size then position, exact skips | Observed |
| 8 read-back, null on failure | Observed |
| 9 settle pass, > 1.0 on any component | Observed |
| 10 off-screen re-push > 0.02 | Observed; fallback on failed re-read Inferred |
| 11 120 ms log | Observed (diagnostic) |
| 12 completion | Observed |
| 13 own-echo suppression via cached frame | Inferred |
| 15–16 read on per-app queue, caches frame | Observed |
| 18 activation order | Observed |
| 19 raise then main | Observed; value "true" Inferred |
| 20–25 | Observed (earlier findings) |

## Unknowns (Part 1)
- Exact value stored into the cached frame before the write (target vs a
  converted variant) — only the store and its later overwrite were seen.
- Whether the off-screen re-push (rule 10) returns the re-read or keeps the
  pass-2 reading when the re-read fails.
- Whether `AXMain` is set to true (GOT symbol not resolved; true is the only
  sensible value).

---

# Part 2 — CanvasWM design input (NOT studied behaviour)

Proposals from Win32 documentation knowledge. Everything here needs the
human's Windows verification.

## G. Threading and ordering
G1. **Writer thread.** `SetWindowPos`/`EndDeferWindowPos` are synchronous:
    they `SendMessage` `WM_WINDOWPOSCHANGING`/`CHANGED`, `WM_GETMINMAXINFO`,
    `WM_SIZE` etc. to the owning thread and wait. A slow or hung app would
    freeze the Engine's UI thread. Mirror the studied design (rule 5) with
    **one dedicated writer thread** (a single serial work queue) that runs
    every `SetFrame`, `SetFrames` and `ReadFrame` in issue order. One queue
    satisfies contract B6 (per-window ordering, read-after-write) trivially;
    the studied per-window parallelism is traded for simplicity.
G2. Each work item runs inside the existing per-monitor-v2 `DpiScope`
    (spec win32-window-enum B10.1/A9) so all coordinates are physical pixels.
G3. Completions are posted back with the injected `post` delegate (A13), so
    they are never synchronous (B5). One `SetFrames` call posts one
    completion with all results in input order (B9).
G4. **Hung windows.** Before writing, `IsHungAppWindow(h)` true → skip the
    window and report **null** (never a guessed frame, which the Engine
    would treat as a refusal and learn a bogus minimum). Optionally use
    `SWP_ASYNCWINDOWPOS` only for such windows if the spec prefers "try
    anyway"; the read-back would then be stale, so the result must still be
    null. A window that hangs *during* a write still blocks the writer thread
    until Windows' ~5 s hang detection; acceptable for v1, flag for review.
G5. Do not touch windows of the CanvasWM process from the writer thread
    (overlays) — they are not managed anyway.

## H. Key → HWND
H1. Keys are made by the Engine tracker (`"w"` + decimal handle). The adapter
    should **not** parse keys; inject a resolver `Func<WindowKey, ulong?>`
    (backed by the tracker's tracked set) and use `HandleConversion.ToHwnd`.
    Unknown key → null (B7). Re-check `IsWindow(h)` on the writer thread
    before writing (handles can die or be recycled meanwhile; also compare
    the thread/pid recorded at admission if the tracker exposes it).

## I. Coordinates: DIP target → physical outer rect
I1. **Frame convention.** Targets and results are the *visible* frame
    (`DWMWA_EXTENDED_FRAME_BOUNDS`, EFB) in DIPs, exactly as `ReadFrame` and
    the facts gatherer report it (B7, B10).
I2. **Inverse DIP mapping (new, `DipSpace.ToPixels`).** Needs a monitor M
    with physical bounds and DPI: `px = M.Left + (x − M.Left)·s`,
    `py = M.Top + (y − M.Top)·s`, `pw = w·s`, `ph = h·s`, s = dpi/96,
    rounded to integers (round-half-away or floor — must be the same
    convention the spec picks; see Unknowns). `ToDip(ToPixels(r)) == r`
    within 1/s.
I3. **Monitor choice for a DIP rect** (needed because the anchor-preserving
    space is per monitor, A8): (a) the monitor whose DIP frame
    (`ToDip(M,M,dpi)`) has the **largest intersection** with the target;
    (b) if none intersects (park targets), the monitor the window is
    **currently on** (`MonitorFromWindow(h, NEAREST)`), which keeps its DPI
    stable while parked (park-geometry Windows notes); (c) tie → first in
    `EnumDisplayMonitors` order. This choice is pure and belongs in Core or
    in `DipSpace` (pure, Win32) — see split §P.
I4. **Invisible border compensation.** Per window, per write, measure
    `GetWindowRect` (outer) and EFB (visible) in the current state:
    `dl = efb.Left − wr.Left`, `dt = efb.Top − wr.Top`,
    `dr = wr.Right − efb.Right`, `db = wr.Bottom − efb.Bottom` (typically
    ≈ 7,0,7,7 px at 100 % on Windows 10/11, 0 for borderless/`WS_POPUP`).
    Outer target = visible target expanded by those deltas. If EFB fails
    (DWM off, some old apps) deltas are 0.
I5. **Cross-DPI moves.** The deltas scale with the DPI of the monitor the
    window is on. When the chosen monitor's DPI differs from the window's
    current monitor DPI, scale the deltas by `dpiNew / dpiOld` (Inferred;
    verify). Per-monitor-aware apps also receive `WM_DPICHANGED` and often
    resize themselves to the suggested rect, so the first write across a
    DPI boundary frequently lands with the wrong size. This is exactly what
    the studied settle pass (rule 9) fixes — see J4.
I6. Minimised windows: EFB/`GetWindowRect` report the parked-iconic rect
    (≈ −32000); never derive deltas from it (restore first, K).

## J. Writing: batching and fallback
J1. **Batch path (`SetFrames`).** On the writer thread:
    `BeginDeferWindowPos(n)`; for each eligible entry
    `DeferWindowPos(hdwp, h, NULL, x, y, cx, cy, flags)`;
    `EndDeferWindowPos(hdwp)`. Flags: `SWP_NOZORDER | SWP_NOACTIVATE |
    SWP_NOOWNERZORDER` (z-order and activation are never changed by a frame
    write — studied writes only set position/size). Optional
    `SWP_NOSENDCHANGING` is **not** recommended (it bypasses the app's
    `WM_GETMINMAXINFO` limits and leads to corrupted layouts; refusals must
    stay visible). `SWP_NOCOPYBITS` optional to avoid smeared content on
    resize (cosmetic; verify).
J2. **All-or-nothing failure.** If `DeferWindowPos` returns NULL for one
    entry (invalid handle, UIPI access denied on an elevated window, window
    on another thread that refuses), the whole HDWP is destroyed and nothing
    is applied. Fallback: discard the batch and write **every** entry
    individually with `SetWindowPos` (same flags); entries whose call fails
    report null. If `EndDeferWindowPos` returns FALSE, do the same per-window
    fallback (the windows already moved will just be written again).
J3. **Pre-filter** to keep batches succeeding: skip (→ null) non-finite
    targets (B8), dead handles, hung windows (G4), and windows already known
    inaccessible (the facts' UIPI flag, spec win32-window-enum A7). Do not
    put minimised/maximised windows in the batch before restoring (K).
J4. **Settle pass (mirrors studied rules 7–9).** After `EndDeferWindowPos`,
    read every written window back (EFB → DIP). For any window whose result
    differs from its target by more than **1.0 DIP** on any component, write
    it once more individually with `SetWindowPos` and read again; use the
    second reading (first if the second read fails). This absorbs
    `WM_DPICHANGED` self-resizes and apps that apply size and position in
    separate steps. *Conflict to resolve in the spec:* engine-interfaces
    spec B7 says the adapter "never adjusts, retries or judges"; the studied
    adapter does exactly one mechanical settle pass inside the write. Either
    amend B7 to allow this fixed one-shot pass (recommended — it is part of
    the studied adapter, not refusal policy, and the Engine's 24-point
    refusal logic still sees genuine clamps) or drop J4.
J5. **Size-vs-position ordering.** `SetWindowPos` applies position and size
    in one call, so the studied size-then-position order (rule 7) has no
    direct equivalent; the settle pass covers apps that react badly. The
    studied "skip unchanged components" optimisation maps to adding
    `SWP_NOMOVE`/`SWP_NOSIZE` when the outer rect already matches the
    current `GetWindowRect` (optional).
J6. **Off-screen re-push (mirrors rule 10).** Windows does not pull windows
    back on-screen, so this is mostly moot; keep it cheap: for targets whose
    visible fraction is < 0.05, if the result's visible fraction is > 0.02,
    write the position once more (`SWP_NOSIZE`). Same B7 conflict as J4.
J7. **Read-back timing.** Read immediately after `EndDeferWindowPos` /
    `SetWindowPos` returns — the call is synchronous for windows on other
    threads that respond, so the new rect is in place. Apps that resize
    themselves later (posted messages, async UI frameworks such as some
    Electron/UWP windows) are covered by the Engine's 0.25 s recheck
    (engine-frame-writer rule 18) and by `EVENT_OBJECT_LOCATIONCHANGE`.
    No extra delay in the adapter.
J8. **Single `SetFrame`.** Same pipeline with a batch of one, or plain
    `SetWindowPos` (simpler; no HDWP needed).
J9. **Own-echo suppression (mirrors rule 13).** Windows raises
    `EVENT_OBJECT_LOCATIONCHANGE` for our writes, often before the
    completion is posted. The Engine's echo list (target added at write time)
    already absorbs them. If the Win32 tracker path keeps a "last reported
    frame" for its change-only filter, the Engine tracker should update it
    with targets/results like the studied cache (decision for the spec; not
    Win32 logic).

## K. Maximised and minimised windows
K1. A maximised window ignores/undoes size writes (and Windows restores the
    maximised geometry on the next maximise toggle). Before writing,
    `IsZoomed(h)` → restore without activating: `ShowWindow(h,
    SW_SHOWNOACTIVATE)` restores to the normal rect without focus
    (`SW_RESTORE` would activate). Then measure deltas (I4) and write.
K2. Alternative: `SetWindowPlacement` with `showCmd = SW_SHOWNOACTIVATE` and
    `rcNormalPosition` = outer target. Caution: `rcNormalPosition` is in
    **workspace** coordinates (offset by the taskbar when it is on the
    top/left of the primary monitor) unless the window has
    `WS_EX_TOOLWINDOW`. Prefer K1.
K3. Minimised windows are not managed (classifier), so a write to one is
    unusual; if it happens, report null rather than un-minimising (the
    tracker will deliver it again when restored). Policy choice — see §P.
K4. Aero-snapped ("arranged") windows are normal windows to `SetWindowPos`;
    writing breaks the snap (expected). `IsWindowArranged` exists on newer
    Windows 10/11 if diagnostics are wanted.

## L. Off-screen parking on Windows
L1. Windows lets top-level windows sit fully outside every monitor and does
    not pull them back (no "reach" effect). Park frames from Core work as-is.
L2. Keep physical coordinates well inside ±30000: (−32000, −32000) is the
    minimised sentinel, and `WM_MOVE` packs coordinates into 16-bit signed
    values, so apps mis-handle positions beyond ±32767. The Core fallback
    origin (10000,10000) is safe at 100 %; at 300 % the anchor-preserving
    mapping keeps the parked window near its monitor so it stays in range —
    the spec should clamp physical park positions to [−30000, 30000]
    (Inferred safety margin).
L3. **The 1-unit overlap.** Park targets overlap the screen by 1 DIP of the
    *visible* frame. On Windows that sliver is visible (≈1–2 px at the
    screen corner) and, because the invisible resize border extends ~7 px
    beyond the visible frame, a few pixels of resize hit-test area of the
    parked window lie on-screen near the corner (only when nothing is above
    it in z-order). Harmless but visible; the spec may choose overlap 0 on
    Windows via `ParkOptions` (Core) — design choice.
L4. Parked windows stay on their monitor's DPI (I3b), so no
    `WM_DPICHANGED` resize when parking/unparking on the same monitor.
L5. Crash-safe restore of parked windows is feature #14; the writer must
    not add any persistence.

## M. UIPI / elevated windows
M1. A non-elevated CanvasWM cannot move higher-integrity windows:
    `SetWindowPos`/`DeferWindowPos` fail with `ERROR_ACCESS_DENIED` (not
    always reported; some paths fail silently). Report null. Since a null
    completion makes the Engine retry on the next pass, the tracker should
    not manage such windows at all (facts flag A7) — classifier policy, not
    writer policy.
M2. A silent no-op write returns the unchanged frame, which the Engine
    would read as a refusal. Detect access up front (A7 flag, or
    `GetLastError` after a failed call) and report null instead.

## N. Focus
N1. Studied order: activate app → raise → make main. Win32 equivalent:
    if `IsIconic(h)` → `ShowWindow(h, SW_RESTORE)`; then
    `SetForegroundWindow(h)` (activates and raises in one step).
N2. **Foreground restrictions.** `SetForegroundWindow` only succeeds if the
    caller is allowed (it is the foreground process, received the last input
    event, etc.). CanvasWM focus normally follows a hotkey (`WM_HOTKEY`) or a
    click on its overlay, which usually grants the right (Inferred; verify on
    Windows). When it fails Windows flashes the taskbar button instead.
N3. Workarounds, with risks (pick at most one, behind a setting):
    - `AllowSetForegroundWindow` — only useful when CanvasWM *is* the
      foreground process (it lets the target take focus); harmless.
    - **Alt-key trick** (`SendInput` a lone Alt press/release before
      `SetForegroundWindow`) — usually works; risks: activates the target's
      menu bar mnemonic mode, interferes with a user-held Alt (Win+Alt
      shortcut profile from #7!), synthetic input is blocked against
      elevated targets.
    - `AttachThreadInput` to the foreground thread — risks: shares input
      state and key state, can deadlock or stall if the foreground thread
      hangs; must detach in `finally`.
    Recommendation: plain `SetForegroundWindow` first; if it returns FALSE,
    fall back to the Alt trick only when no shortcut modifier is still held;
    avoid `AttachThreadInput`. Measure on Windows before deciding.
N4. Focus is fire-and-forget (contract): run it on the writer thread (it may
    block on the target) or post it; never report a result. Raise a .NET
    event (e.g. `Raising(key)`) for App overlays, mirroring the studied
    broadcast; the Engine does not need it.
N5. `BringWindowToTop`/`SetWindowPos(HWND_TOP)` are not needed after a
    successful `SetForegroundWindow`.

## O. Queries
O1. **FrontToBack:** `EnumWindows` (top-level, z-order, topmost first) or
    `GetTopWindow`/`GetWindow(GW_HWNDNEXT)`, map handle → key and keep only
    managed keys. "Managed" must come from the Engine tracker: inject a
    predicate/resolver (same as H1) rather than re-classifying in Win32.
O2. **IsShowing:** `IsWindow && IsWindowVisible && !IsIconic && cloak == 0`
    for the resolved handle; cache the answer set for **0.1 s** (studied) —
    or compute per call (cheap on Windows; the cache is optional). Unknown
    key → false.
O3. **VisibleFraction:** `ParkGeometry.VisibleFraction(rect, monitorFullFramesInDip)`
    where each frame is `DipSpace.ToDip(rcMonitor, rcMonitor, dpi)` from
    `EnumDisplayMonitors` + `GetMonitorInfoW` + `GetDpiForMonitor`. Refresh
    the monitor list on `WM_DISPLAYCHANGE`/`WM_DPICHANGED` (or per call).
O4. **Pointer:** `GetCursorPos` (physical under the PMv2 scope) → DIP via the
    monitor under the point (`MonitorFromPoint(NEAREST)`) and `DipSpace`
    point mapping (new `ToDip(point, M, dpi)` helper).
O5. **MouseDown:** `(GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0`. Note
    swapped mouse buttons: `VK_LBUTTON` is the *physical* left button; the
    primary button is `VK_RBUTTON` when `GetSystemMetrics(SM_SWAPBUTTON)` is
    non-zero. Use the primary button to match "primary button held".
O6. **ParkingOrigin:** `ParkGeometry.DefaultFallbackOrigin(monitorFullFramesInDip)`
    (spec engine-interfaces B10 already says so). With the anchor-preserving
    space, "bottom-right of the right-most monitor − 1" is computed in DIPs.
O7. **TabSibling:** always null.

## P. Recommended split (Core / Engine / Win32)
Pure decisions that should **not** live in Win32:
- **Core** (pure, Mac-tested):
  - `ParkGeometry.VisibleFraction` and `DefaultFallbackOrigin` (exist).
  - `FrameSettle.NeedsSecondPass(target, actual, tolerance = 1.0)` and
    `NeedsOffscreenRepush(targetFraction, actualFraction)` (0.05 / 0.02) —
    the studied constants as pure predicates, used by Win32 J4/J6.
  - Monitor choice for a DIP rect (I3): `DisplayChoice.ForRect(rect,
    monitors, currentMonitorIndex)` over neutral (dipFrame, index) records.
  - Border-delta arithmetic (I4/I5): `BorderInsets` record + expand/shrink
    and DPI rescale — pure arithmetic over ints/doubles.
  - Optional `ParkOptions.Overlap` (L3) and a physical clamp range (L2).
- **Engine**: which windows to write, refusal detection/rechecks/learning
  (#26, done), whether to un-maximise (K1 is mechanism; "a maximised window
  gets tiled" is already decided by the classifier/tracker), managed-key
  predicate and key→handle resolver supplied by the tracker (H1, O1),
  updating the tracker's last-known frame after writes (J9).
- **Win32** (`Win32WindowSystem : IWindowSystem`): writer thread + post,
  DPI scope, `IsWindow`/`IsHungAppWindow`/access pre-checks, `ShowWindow`
  restore, `DeferWindowPos` batch with per-window fallback, `SetWindowPos`,
  EFB/`GetWindowRect` reads, `DipSpace.ToPixels` (inverse mapping, pure,
  next to `ToDip`), monitor enumeration, `SetForegroundWindow` (+ chosen
  workaround), `EnumWindows` z-order, `GetCursorPos`, `GetAsyncKeyState`.
  No thresholds of its own beyond calling the Core predicates.
  Note spec win32-window-enum A14: Win32 pure helpers are not Mac-testable;
  putting the predicates in Core makes them Mac-testable.

## Worked examples (Part 2, for spec/tests; all at 100 % unless stated)
W1. Monitor A (0,0,1920,1080) 96 dpi; window EFB (100,100,800,600),
    `GetWindowRect` (93,100,907,707) → deltas (7,0,7,7). Target DIP
    (0,0,960,1040) → physical visible (0,0,960,1040) → outer
    (−7,0,974,1047) → `DeferWindowPos(x=−7,y=0,cx=974,cy=1047)`. Read-back
    EFB (0,0,960,1040) → result (0,0,960,1040).
W2. Monitor B at physical (1920,0,3840,1440) with 144 dpi (s = 1.5): its DIP
    frame is (1920,0,1280,960). Target DIP (1930,10,600,400) → physical
    (1920+(10)·1.5, 0+10·1.5, 900, 600) = (1935,15,900,600); deltas at 144
    dpi ≈ (11,0,11,11) (7 × 1.5, rounded) → outer (1924,15,922,611).
W3. Window on A moved to target on B (W2): deltas measured on A (7,0,7,7)
    are rescaled ×1.5 → (10.5→11, 0, 11, 11); first write may come back
    (1935,15,600,400) physical after the app's `WM_DPICHANGED` handling →
    DIP width 400 vs 600 > 1 → settle pass writes again → (1935,15,900,600)
    → result (1930,10,600,400).
W4. Batch of three where the second window is elevated: `DeferWindowPos`
    for it returns NULL → batch discarded → per-window `SetWindowPos`:
    first and third succeed (results read back), second fails → null.
W5. Hung window in a batch → excluded up front → its result null; the other
    entries complete normally; one completion with three results in order.
W6. Park target (1919,1079,705,423) on a lone 1920×1080 monitor: outer
    rect with deltas (1912,1079,719,430); visible sliver 1×1 px at the
    corner; physical coordinates within ±30000.
W7. Maximised window, target (0,0,960,1040): `IsZoomed` → `ShowWindow(
    SW_SHOWNOACTIVATE)` → measure deltas → write → result.
W8. `MouseDown` with swapped buttons (`SM_SWAPBUTTON` = 1): physical right
    button held → `VK_RBUTTON` down → true.
W9. `Pointer` at physical (2235,315) on B (W2): DIP (1920 + (2235 − 1920)/1.5,
    0 + 315/1.5) = (2130, 210).

## Confidence (Part 2)
All Part 2 rules are **Inferred** (design input from Win32 documentation
knowledge, not studied behaviour) and need Windows verification. The
studied anchors they mirror (rules 2, 5, 7–10, 18–24) are Observed.

## Unknowns
- **B7 vs settle pass (J4/J6):** the studied adapter performs one mechanical
  re-write inside the write; CanvasWM's spec B7 forbids adapter retries.
  Needs a spec decision (recommend amending B7 to allow exactly this fixed
  pass with the Core predicates).
- **Rounding convention** for `DipSpace.ToPixels` (round vs floor) and how
  it interacts with the Engine's exact-equality skip (engine-frame-writer
  rule 9): a target with fractional DIPs never reads back exactly, so the
  Engine may rewrite every pass at 125 %/150 %. Either Engine targets are
  snapped to the pixel grid (Core `PixelRounded`-style, per monitor scale)
  or the skip uses a tolerance — needs a spec decision (Windows-specific;
  studied macOS uses whole points).
- **DPI-crossing behaviour** (I5/W3): delta rescaling and how often apps
  resize themselves on `WM_DPICHANGED` must be measured on a mixed-DPI
  machine.
- **Foreground permission after `WM_HOTKEY`** (N2): believed granted; must be
  verified, together with whether the Alt-key trick is needed.
- **`SWP_ASYNCWINDOWPOS` inside `DeferWindowPos`**: whether the flag is
  honoured per entry in a deferred batch is not documented clearly;
  recommend not relying on it (G4).
- Studied unknowns (Part 1): exact pre-write cached value, re-push
  fallback, `AXMain` value.

## Windows notes
- Never call `SetWindowPos`/`EndDeferWindowPos` on the Engine UI thread
  (blocking `SendMessage` to other processes).
- `WM_GETMINMAXINFO` clamps are applied inside `SetWindowPos`; refusals are
  therefore visible in the immediate read-back (good for the Engine's
  refusal logic). Do not use `SWP_NOSENDCHANGING`.
- Packaged/UWP (`ApplicationFrameWindow`) windows move like normal
  top-level windows; their content may re-layout asynchronously (covered by
  the Engine's 0.25 s recheck).
- Licensing / feature gating: nothing in this feature touches it.
