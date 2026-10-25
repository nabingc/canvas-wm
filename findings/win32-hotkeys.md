# Feature: win32-hotkeys

## Summary
The studied app never registers hotkeys with the operating system. It runs one
session-wide keyboard/mouse event interceptor for its whole lifetime and
matches every key-down against its in-memory binding list (feature #7). The
binding list is rebuilt wholesale when the app's engine starts and again on
every "shortcuts changed" notification; the only time matching is switched off
is while the settings recorder is capturing a new shortcut. CanvasWM will
instead register each bound combo with `RegisterHotKey` on a message-only
window. That changes four things the studied app never had to handle: a combo
can be **unavailable** (owned by someone else), there is **no key-up or
repeat** stream, registrations must be **dropped while recording**, and keys
are identified by **Windows virtual-key codes**, which depend on the keyboard
layout.

Part 1 is studied behaviour (macOS). Part 2 is CanvasWM design input and is
not studied behaviour.

---

# Part 1 — Studied behaviour (macOS)

## Behaviour

### A. Interceptor lifecycle
1. **One interceptor for the whole process.** A single session-level event
   interceptor (placed at the head, allowed to modify or drop events) is
   created when the coordinator starts. It is attached to the main run loop,
   and it carries key-down, key-up, mouse, gesture and scroll events. Every
   callback runs on the main thread.
2. **Never switched off.** The interceptor is enabled once and never disabled,
   removed or invalidated during the process lifetime. Only two places enable
   it: creation, and an automatic re-enable whenever the OS reports that it
   switched the interceptor off (for a slow callback or for user input). There
   is no code path that disables it.
3. **Creation failure.** If the interceptor cannot be created (normally because
   the input-monitoring/accessibility permission is missing), the app writes
   one log line and carries on. Shortcuts, Escape handling and drag gestures
   then do nothing. No per-shortcut failure state exists, because no shortcut
   is ever "registered".
4. **Order inside one key event.** Escape (macOS key code 0x35) goes first to
   the Escape handler, which consumes the key only if it reports that it acted.
   If it did not, the event continues to the shortcut matcher. If the matcher
   consumes it, processing stops. Otherwise the overview key-capture path runs
   (findings/shortcut-model.md rule 25).

### B. Binding table lifecycle
5. **Initial build.** At engine start-up the coordinator (and with it the
   interceptor) is started first. The binding table is built right after that.
6. **Rebuild on change.** Right after the initial build, the app subscribes on
   the main queue to the shortcuts-changed notification, which is posted by
   every shortcut or view-set write (shortcut-model rule 15). Each
   notification rebuilds the **whole** table from settings: 12 actions, unbound
   ones skipped, then the 18 saved-view entries. Nothing is diffed and nothing
   is registered with the OS. The table is simply replaced.
7. **No suspension other than recording.** The shortcut matcher checks exactly
   one global flag, "recording". It does not check whether the app or engine
   is paused, suspended, in overview, or has no active engine. While the
   overview is open, shortcuts still match, because the matcher runs before the
   overview capture path. A matched shortcut with no target engine still
   consumes the key and then does nothing (shortcut-model rule 2).

### C. Repeat and key-up (in the matcher, not at an OS level)
8. **Repeat.** The OS marks auto-repeat key-downs. A matching repeat is
   consumed and runs nothing. Nothing is suppressed at the OS level; the
   interceptor sees every repeat.
9. **Key-up.** A key-up removes the key from the held set and is consumed only
   if the key was in the set. **This branch runs before the recording check and
   does not look at it.** So a key-up while recording is still consumed when
   its key-down was consumed before recording started. Spec
   `shortcut-model` A7 says such a key-up passes through. That is a small
   deviation, and it is moot for a `RegisterHotKey` design (Part 2, D7).
10. **Action runs synchronously** inside the interceptor callback, on the main
    thread, before the callback returns its verdict.
11. **Modifier reading.** Only the Control, Option, Shift and Command flags are
    read. Caps Lock, Fn and left/right distinctions are ignored (confirms
    shortcut-model rule 9).

### D. Settings recorder
12. **Start.** When a recorder field is activated, the field marks itself
    "listening", sets the global recording flag, clears its live preview, and
    installs an **in-process** event monitor for key-down and modifier-change
    events. That monitor sees only events delivered to the app's own key
    window. It hands each event to the recorder logic (shortcut-model rules
    16–19) and swallows it. The recorder also watches its window for losing
    key (focused) status.
13. **Stop.** Recording stops when the recorder's window stops being key, when
    the recorder view is removed from its window, or when the recorder logic
    finishes (a combo is stored, refused, cleared or cancelled). Stopping clears
    "listening" and the global flag, removes the monitor and the observer, and
    refreshes the field.
14. **Why both paths are needed.** While recording, the global interceptor still
    sees the keys, but the shortcut matcher lets them pass (rule 7). So a combo
    that is currently bound still reaches the settings window and can be
    re-recorded or moved.

## Data model (studied, neutral terms)
- Interceptor: enabled flag (always true after start), Escape callback,
  shortcut callback, overview key callback, mouse/gesture callbacks,
  "captures keys" and "swallow up" flags.
- Binding table: an ordered list of (key code, modifier bits, action); it is
  replaced wholesale on each rebuild.
- Held set: key codes whose key-down was consumed.
- Global flag `recording`, owned by the recorder.

## Worked examples (studied)
1. App starts with permission granted → interceptor created → table built with
   defaults → Ctrl+Opt+Right moves right.
2. Permission missing at start → one log line; all shortcuts do nothing; the
   app keeps running.
3. User changes `back` in settings → notification → table rebuilt → the old
   `back` combo passes through to apps and the new one fires.
4. Overview open, user presses Ctrl+Opt+Left → the shortcut fires (the matcher
   runs before overview capture).
5. Recorder active, user presses the currently bound Ctrl+Opt+Space → the
   matcher passes it → the recorder receives it (and moves it if it belongs to
   another action).
6. User holds Ctrl+Opt+Right, starts recording with the mouse while still
   holding it, then releases → the key-up is still consumed (rule 9).

## Evidence
(`~/Documents/trelliswm-re/evidence/win32-hotkeys-notes.txt`, new; raw REA
output `win32-hotkeys-recorder-eventtap.json`, new; older files cited by name)
- Rule 1: `EventTap.start` 0x100174940 (`dec-eventTapStart.json`): session
  tap, head insert, default (modify) option, main run loop common modes; caller
  `Coordinator.start` (call site 0x10015c538).
- Rule 2: the `CGEventTapEnable` stub has exactly two call sites (0x100174b60 in
  start, 0x100174f38 in handle, on negative event types). The run-loop-source
  removal callers are unrelated (AppObserver, MissionControl, WindowTracker).
  `EventTap.deinit` 0x1001751dc only releases fields.
- Rule 3: `dec-eventTapStart.json`, failure branch (log "could not be
  created").
- Rule 4: `dec-eventTapHandle.json` (`EventTap.handle` 0x100174e08).
- Rules 5–6: `AppDelegate.startEngine` 0x10012e01c calls Coordinator.start
  (0x10012e284), then registerHotkeys (0x10012e28c), then subscribes an
  observer on the main queue (0x10012e368), whose closure calls registerHotkeys
  (0x1001339f0). `registerHotkeys` 0x100132ba0 replaces `Hotkeys.bindings`
  (bundle record 62).
- Rules 7–11: `Hotkeys.handle` 0x10017dd28 (`dec-hotkeysHandle.json`); its
  caller is the shortcut callback closure in startEngine (0x100132b10).
- Rules 12–14: `ShortcutRecorder.start` 0x1001c7934 (local monitor, mask =
  key-down + flags-changed; resign-key observer), `stop` 0x1001c77cc,
  `viewDidMoveToWindow` 0x1001c839c, the monitor closure 0x1001c7bf8 (returns
  nil, so the event is swallowed), the resign closure 0x1001c8340 (stop).

## Confidence
| Rule | Confidence |
|---|---|
| 1 | Observed |
| 2 | Observed (all call sites enumerated with objdump) |
| 3 | Observed (log only); "no user-facing notice" Inferred (only this function traced) |
| 4 | Observed |
| 5 | Observed |
| 6 | Observed (call order and observer); the notification's identity is Inferred from the Shortcuts static region it references (upgrades shortcut-model rule 15 from Inferred) |
| 7 | Observed (no other gate in `Hotkeys.handle`) |
| 8, 10, 11 | Observed |
| 9 | Observed (key-up branch precedes and ignores the recording flag) |
| 12, 13 | Observed |
| 14 | Inferred from rules 7 and 12 together |

## Unknowns (Part 1)
- Whether engine start-up (and therefore the interceptor) is gated by anything
  outside shortcuts. `startEngine` has license-related neighbours; per project
  rules they were not traced.
- Whether a missing permission is retried later (a `permissionTimer` field
  exists, but it was not traced for this feature).

---

# Part 2 — CanvasWM design input (NOT studied behaviour)

Everything below is a proposal for the spec. It comes from Windows platform
knowledge, not from the studied app. Items marked **[verify]** need the human
to check them on a Windows PC.

## Behaviour (design)

### D1. Host window and thread
- Register hotkeys against a **message-only window** (parent `HWND_MESSAGE`,
  i.e. −3), with a private window class registered by `RegisterClassExW` and
  a window procedure that is a `[UnmanagedCallersOnly]` function pointer. This
  is the same style as #10 and #11 (`LibraryImport`, UTF-16, `SetLastError`
  where failure is checked).
- Create the window on the **owner thread**. That is the same message-pumping
  engine thread that owns `Win32WindowSource` (#10 A13). `RegisterHotKey` and
  `UnregisterHotKey` for a window only work on the thread that created it, so
  all registration calls must run on that thread. A call from any other thread
  is a programming error and should throw.
- Use a window, not `hWnd = NULL`. With NULL, `WM_HOTKEY` becomes a
  thread message. Modal loops (menus, message boxes, window move/size loops,
  `DialogBox`) do not dispatch thread messages, so presses would be lost. A
  message-only window gets its messages dispatched in modal loops too.
- In the window procedure, do not run the command inline. Turn `WM_HOTKEY`
  into a `KeyCombo` and hand it on through the injected `post` delegate (FIFO,
  never inline). This matches the #10 `EventPump` rule and avoids re-entrancy
  if a command pumps messages.

### D2. Registration call shape
- `RegisterHotKey(hwnd, id, mods | MOD_NOREPEAT, vk)`.
- Modifier map: Alt → `MOD_ALT` (0x1), Ctrl → `MOD_CONTROL` (0x2), Shift →
  `MOD_SHIFT` (0x4), Win → `MOD_WIN` (0x8), and always `MOD_NOREPEAT` (0x4000).
  **Pitfall:** Core `Modifiers` has Ctrl = 1 and Alt = 2, the reverse of
  `MOD_*`. The map must be explicit; a numeric cast is wrong.
- Left and right modifier keys are not told apart (this matches studied rule
  11). Modifier matching is exact: a Win+Alt hotkey does not fire if Shift is
  also down (Inferred from documented behaviour; matches Core exact match).
- **Id allocation:** application ids must lie in 0x0000–0xBFFF (0xC000–0xFFFF
  is for DLLs/global atoms). Ids are per window. Proposal: derive a stable id
  from the combo, `id = (int)key * 16 + (int)modifiers`. The maximum is
  110 × 16 + 15 = 1775, so no counter or free list is needed. The id is then a
  pure function, unit-testable on the Mac if it lives in a pure helper.
- `WM_HOTKEY` (0x0312): `wParam` = id. It is −1 or −2
  (`IDHOT_SNAPWINDOW`/`IDHOT_SNAPDESKTOP`) for system hotkeys, which are
  ignored. `lParam` low word = `MOD_*` bits, high word = VK. Use the id to
  find the combo, and treat a mismatch between the lParam and the registered
  combo as "ignore + log".

### D3. Failure → "unavailable" per combo
- `RegisterHotKey` returns FALSE. `GetLastError()` is
  `ERROR_HOTKEY_ALREADY_REGISTERED` (1409) when another thread or process
  already holds the exact (mods, vk) pair. That includes Explorer for many Win+
  shortcuts. Report per combo: `Registered | Taken (1409) | Failed(code)`.
- Registration is all-or-nothing **per combo**, never per batch. One taken
  combo must not stop the others.
- Proposed extra status `NotOnLayout`: registration succeeded, but the current
  keyboard layout has no physical key for that VK (D6). This is a warning, not
  an error.
- "Taken" can change at runtime (the other app quits or starts). The studied
  app has no such state. Proposal (needs a human decision): retry taken combos
  whenever the bindings change, when the settings window opens, and on an
  explicit "retry" button. A periodic retry is possible but not recommended.
- Success does **not** prove the combo works. Some shell shortcuts are handled
  before hotkey dispatch (for example Win+L by the secure-attention/winlogon
  path), so registration can succeed and the hotkey never fire, or both
  CanvasWM and Windows may act. Only a press test can tell. **[verify]** (D11)

### D4. Desired vs registered (what to (re)register)
- **Desired set** = distinct combos of `ShortcutDispatch.Build(bindings)`, in
  order, with the first entry winning (as `Match` does). Duplicate combos are
  registered once, since a second `RegisterHotKey` of the same pair by the same
  window fails with 1409.
- On every bindings change, and on recording start and stop: unregister
  `registered − desired` **first**, then register `desired − registered`. A
  combo kept but moved to another command needs no OS call, because the
  command lookup is by combo at press time.
- Start-up: register after the engines are started and the bindings are loaded
  (the studied order: interceptor, then table). Shutdown: unregister every
  registered combo on the owner thread, then `DestroyWindow`. Process exit
  frees them anyway.
- Engine suspend (virtual-desktop switch), overview open, "no active engine":
  **keep everything registered** (studied rule 7). A press with no target
  engine is still consumed (RegisterHotKey always eats it) and does nothing.

### D5. Repeat and key-up with RegisterHotKey
- `WM_HOTKEY` is posted on key-down only. With `MOD_NOREPEAT` the system does
  not post again for auto-repeat while the key is held. So "repeats on hold =
  never" (shortcut-model rule 12 / Core B15) is met by the OS.
- No key-up is reported. Most likely the key-up reaches the focused app as an
  unmatched `WM_KEYUP`, which apps generally ignore. So the Core dispatcher's
  held set and key-up swallow (B14–B19) have nothing to do in this design.
  **Recommendation:** on `WM_HOTKEY` call `ShortcutDispatch.Match(table,
  combo)` (spec shortcut-model note: "With RegisterHotKey, call Match"). Do not
  drive `KeyDown`/`KeyUp`. Gate it on the recording flag, which should be moot
  anyway because recording unregisters everything (D8).
- **Modifier side effects [verify]:** when a hotkey with Alt or Win is eaten,
  the focused app may see Alt-down/Alt-up with nothing in between and activate
  its menu bar, and the shell may open Start when Win is released alone. Check
  both on Windows 10 and 11 with Win+Alt+O in Notepad. If either happens, the
  fix needs a human decision. Do **not** add `SendInput` masking by default;
  see the #11 A10 risk note about synthetic keys while Win+Alt is held.

### D6. Key → virtual-key mapping (all 86 Core `Key` members)

| Core `Key` | VK (hex) | Name |
|---|---|---|
| A … Z (26) | 0x41 … 0x5A | 'A' … 'Z' |
| D0 … D9 (10) | 0x30 … 0x39 | '0' … '9' (top row; never `VK_NUMPAD*`) |
| F1 … F24 (24) | 0x70 … 0x87 | `VK_F1` … `VK_F24` |
| Left, Up, Right, Down | 0x25, 0x26, 0x27, 0x28 | `VK_LEFT`, `VK_UP`, `VK_RIGHT`, `VK_DOWN` |
| Space | 0x20 | `VK_SPACE` |
| Enter | 0x0D | `VK_RETURN` |
| Tab | 0x09 | `VK_TAB` |
| Backspace | 0x08 | `VK_BACK` |
| Delete | 0x2E | `VK_DELETE` |
| Escape | 0x1B | `VK_ESCAPE` (never registered: not bindable) |
| Insert | 0x2D | `VK_INSERT` |
| Home | 0x24 | `VK_HOME` |
| End | 0x23 | `VK_END` |
| PageUp | 0x21 | `VK_PRIOR` |
| PageDown | 0x22 | `VK_NEXT` |
| Minus | 0xBD | `VK_OEM_MINUS` |
| Equals | 0xBB | `VK_OEM_PLUS` |
| LeftBracket | 0xDB | `VK_OEM_4` |
| RightBracket | 0xDD | `VK_OEM_6` |
| Backslash | 0xDC | `VK_OEM_5` |
| Semicolon | 0xBA | `VK_OEM_1` |
| Quote | 0xDE | `VK_OEM_7` |
| Comma | 0xBC | `VK_OEM_COMMA` |
| Period | 0xBE | `VK_OEM_PERIOD` |
| Slash | 0xBF | `VK_OEM_2` |
| Backquote | 0xC0 | `VK_OEM_3` |

Count: 26 + 10 + 24 + 4 + 11 + 11 = 86. The table must be bijective, so the
reverse map (VK → Key) is used by the recorder. VKs not in the table (numpad,
`VK_OEM_102`, PrintScreen, media keys, modifiers) map to "no Key".

Layout notes:
- Letters, digits, F-keys and navigation keys are **identity** VKs. On AZERTY,
  'A' is the key labelled A (US Q position). Mnemonic letters (O for overview,
  N for new window) therefore follow the key cap, which is what users expect.
  On AZERTY the top-row digits still produce `'1'..'9'` VKs. On Cyrillic and
  Greek layouts, letter VKs stay A–Z by physical position.
- The 11 punctuation VKs (`VK_OEM_*`) are **assigned per layout** and sit on
  different physical keys. On German QWERTZ, for example, `VK_OEM_4` is the ß
  key and `VK_OEM_6` is the ´ key, so Win+Alt+LeftBracket is Win+Alt+ß there.
  Two options:
  - **(a) VK identity (recommended for v1).** Register the fixed VK, and show
    the key's local name from the current layout (VK → scan code with
    `MapVirtualKeyExW(vk, MAPVK_VK_TO_VSC_EX, hkl)`, then
    `GetKeyNameTextW`/`ToUnicodeEx`). There is no re-registration when the
    layout changes. If the layout has no key for the VK (scan code 0), report
    `NotOnLayout`. The Core `Key` doc text "US-layout punctuation position" then
    describes the US default only. **Flag for Core:** reword it to "the key that
    produces this OEM virtual-key code".
  - **(b) Physical position.** Store the US scan code (Minus 0x0C, Equals 0x0D,
    [ 0x1A, ] 0x1B, \ 0x2B, ; 0x27, ' 0x28, , 0x33, . 0x34, / 0x35, ` 0x29),
    convert with `MapVirtualKeyExW(sc, MAPVK_VSC_TO_VK_EX, hkl)`, and
    re-register when the layout changes. The difficulty: `WM_INPUTLANGCHANGE`
    goes only to the focused thread's windows, never to our message-only
    window. Detection would need `GetKeyboardLayout(foreground thread)` checks
    on `EVENT_SYSTEM_FOREGROUND` (already hooked in #10) plus a check when the
    layout list changes. With "per-app input method" enabled, the layout can
    differ per window, so one VK cannot follow the position everywhere. Defer.
- Numpad duplicates: `VK_RETURN` also comes from numpad Enter. With NumLock
  off, the numpad Home/End/arrow/Ins/Del keys produce the same VKs as the
  dedicated keys. A hotkey on those VKs therefore also fires from the numpad.
  Accept and document this; RegisterHotKey cannot tell them apart.
- **F12:** Windows documents F12 as reserved for the debugger. Avoid it in
  defaults; allow it in user bindings, with a warning.

### D7. Low-level keyboard hook (`WH_KEYBOARD_LL`)? Trade-offs
| | RegisterHotKey | WH_KEYBOARD_LL |
|---|---|---|
| Key-up / repeat visible | no (repeat suppressed by MOD_NOREPEAT) | yes |
| Elevated (admin) window in foreground | still fires (Inferred, widely relied on) | does not see the input when CanvasWM is not elevated (UIPI) (Inferred) |
| Cost | none until pressed | every keystroke system-wide passes through our thread |
| Robustness | system-managed | the system may remove the hook silently if the callback exceeds `LowLevelHooksTimeout` (about 300 ms to 1 s); there is no notification, and it needs a dedicated, always-responsive thread |
| Conflicts | reports 1409 per combo | no conflict report; whichever hook runs first wins |
| Modifier side effects | system-handled (D5 [verify]) | must suppress them itself (synthetic key injection) |
| Security-tool suspicion | low | higher (global key hooks are keylogger-like) |

**Recommendation:** use `RegisterHotKey` for all bindings, always on. Use **no**
LL hook in this feature. The overview key-capture and Escape interception
(shortcut-model rules 24–25) belong to #16, which may use an LL hook installed
**only while the overview or a drag is active**. Note for #16: LL hooks run
before hotkey processing. If that hook swallows a key, a registered hotkey with
that key cannot fire. It must pass through every key with Ctrl, Alt or Win held
(the studied capture path likewise ignores Command and Ctrl+Opt chords), or
call `Match` itself.

### D8. Recording new bindings
- Problem: a registered combo is eaten by the system before any window sees it.
  If registrations stayed active, the user could not re-record a bound combo,
  so studied rule 14 would break.
- **Recommendation:** when recording starts, unregister **everything** (desired
  set = empty). When it stops, register the desired set again (D4 diff). Then
  capture in the **focused settings window** from `WM_KEYDOWN` **and**
  `WM_SYSKEYDOWN` (Alt combos and F10 arrive as SYSKEYDOWN). Read the VK from
  `wParam` and the extended bit (lParam bit 24) to reject numpad Enter if
  wanted. Read modifiers with `GetKeyState` for `VK_CONTROL`, `VK_MENU`,
  `VK_SHIFT` and `VK_LWIN`/`VK_RWIN`. Map VK → Key with the reverse table and
  ignore keys that are not in it. Stop recording when the settings window
  deactivates (`WM_ACTIVATE` inactive / `WM_KILLFOCUS`), as the studied app
  does on loss of key status.
- Mark the key messages as handled. For Alt combos, also stop `DefWindowProc`
  from entering menu mode (handle `WM_SYSKEYDOWN` and `WM_SYSCHAR`).
- Known gaps **[verify]**: combos the shell owns (Win+D, Win+E, …) never reach
  the window. That is acceptable, since they are reserved anyway. Releasing Win
  after a Win combo may open Start, which deactivates the settings window and
  ends recording. AltGr arrives as Ctrl+Alt (right Alt plus a synthetic left
  Ctrl), so the recorder stores Ctrl+Alt; `MayClashWithAltGr` already lets the
  UI warn about this.
- Alternative: a temporary LL hook while recording. It can capture and swallow
  Win combos and avoid Start popping up, but the elevation, timeout and
  suspicion costs above apply. Not recommended for v1.

### D9. Reserved combos — what to confirm [verify]
Probe design (a manual test like #11 AC37): for each combo, record the
`RegisterHotKey` result (ok / 1409 / other), press it with Notepad focused, and
record (a) whether `WM_HOTKEY` arrived, (b) whether Windows also acted, (c)
whether the Alt-menu cue or the Start menu appeared. Run it on Windows 10 and
11, with Game Bar on and off.
- All NC rows of `ReservedCombos` (B13 #53–84): Win+Alt+0–9, Win+Alt+Up/Down,
  Win+Alt+B/D/K/R/G/T/M/H/Enter, and the Ctrl+Alt Magnifier and driver keys
  (with Magnifier closed and open).
- The WinAlt view sets: Win+Alt+Shift+1–9 and Win+Ctrl+Alt+1–9. I know of no
  shell binding for either (Inferred).
- All 12 WinAlt action defaults (B10).
- Expected-blocked rows, to learn how "blocked" looks: Win+L, Win+D, Win+Left,
  Ctrl+Shift+Esc. Each will be either 1409, or "ok but never delivered", or "ok
  and both act". Which one is Unknown.
- **Candidate additions** for `ReservedCombos` (Core data; human decision):
  Ctrl+Alt+Shift+Win+{W,T,Y,O,P,D,L,X,N,Space} (the "Office key" launcher
  combos; Inferred), Win+Shift+F23 (sent by the Copilot key on new keyboards;
  Inferred), Win+Shift+S (screen snip), Win+Ctrl+Shift+B (graphics reset),
  Win+Ctrl+Enter (Narrator), Win+Ctrl+O (on-screen keyboard), Win+Ctrl+C (colour
  filters). None conflicts with a B10 or B11 default.

### D10. Foreground rights
The thread that receives a hotkey counts as having received the last input,
so `SetForegroundWindow` from the CanvasWM process succeeds right after a
press. #11 AC37 passed on Windows with the plain path (2026-10-25). No
workaround is needed. Focus calls made long after the press (deferred work)
may lose that right; that is #11's concern.

## Data model (design, neutral)
- `HotkeyStatus`: `Registered | Taken | Failed(errorCode) | NotOnLayout`.
- Registered map: `KeyCombo → id` (id derived per D2).
- Desired list: distinct combos from the dispatch table (D4), or empty while
  recording.
- Availability report: `KeyCombo → HotkeyStatus`, for the settings UI.

## Worked examples (design)
1. WinAlt defaults on a clean Windows 11 → desired = 12 action combos + 18 view
   combos = 30 registrations, all `Registered` (to be confirmed by D9).
2. Another tool owns Win+Alt+O → that one combo is `Taken` (1409); the other 29
   work; settings show "Overview: unavailable".
3. User rebinds Back from Win+Alt+[ to Win+Alt+B → diff: unregister Win+Alt+[,
   register Win+Alt+B. If B is a Game Bar key (NC) → `Taken`, or registered but
   doubled; that is the D9 question.
4. `WM_HOTKEY` with wParam = id(Overview combo) → combo → `Match` →
   `ShortcutCommand` Overview → posted to the engine thread → engine call
   (#29).
5. Recording starts → all 30 unregistered → user presses Win+Alt+Shift+Up in
   the settings window → `WM_SYSKEYDOWN` VK_UP with Win/Alt/Shift down →
   `ShortcutEditor.RecordForAction` → stored → recording stops → diff
   registers the new set.
6. Hold Win+Alt+Right for 2 s → one `WM_HOTKEY` (MOD_NOREPEAT) → one move.
7. Core combo Ctrl+Alt+D → `MOD_CONTROL|MOD_ALT|MOD_NOREPEAT` = 0x4003, VK 0x44,
   id = 4 × 16 + 3 = 67.
8. German layout, Back = Win+Alt+LeftBracket → registers VK_OEM_4; the UI label
   reads "ß"; pressing Win+Alt+ß fires Back.

## Evidence (Part 2)
Design input from Windows platform documentation knowledge (RegisterHotKey,
UnregisterHotKey, WM_HOTKEY, MOD_NOREPEAT, HWND_MESSAGE, LowLevelKeyboardProc,
MapVirtualKeyEx, keyboard layouts). No TrellisWM evidence applies. The project
constraints come from `specs/shortcut-model.md` ("Notes for #12", B10, B13,
A6, A7), `findings/win32-window-enum.md` I1/K, `specs/win32-frame-writer.md`
B21 and AC37, and `windows-verification/win32-frame-writer.md` (AC37 result).

## Confidence (Part 2)
| Item | Confidence |
|---|---|
| D1 message-only window, owner-thread rule, modal-loop reason | Inferred (documented API behaviour) |
| D2 MOD_* values, id range, WM_HOTKEY layout, exact-modifier match | Inferred (documented); exact-match Inferred |
| D3 error 1409; "success ≠ works" | Inferred; which combos behave how: Unknown [verify] |
| D4 diff policy | Design proposal |
| D5 no key-up/repeat via WM_HOTKEY | Inferred (documented); modifier side effects Unknown [verify] |
| D6 VK table | Inferred (standard VK values); German example Inferred |
| D7 LL hook trade-offs (elevation, timeout) | Inferred |
| D8 recorder via focused window | Design proposal; Start-menu effect Unknown [verify] |
| D9 reserved facts | Unknown until probed |
| D10 | Observed on Windows (#11 AC37) |

## Unknowns
1. For each reserved/NC combo: does `RegisterHotKey` fail (1409), succeed but
   never deliver, or succeed while Windows also acts? This needs the D9 probe
   on Windows 10 and 11, with Game Bar on and off.
2. Whether an eaten Alt or Win hotkey leaves side effects (menu-bar cue in the
   focused app, Start menu on Win release). If so, a mitigation needs a human
   decision; `SendInput` masking carries the #11 A10 risk.
3. Punctuation semantics on non-US layouts (VK identity vs physical position),
   and how per-app input languages affect a global VK. The recommendation is
   VK identity for v1, but this is a product decision.
4. Retry policy for `Taken` combos (when to retry; whether to notify).
5. Whether hotkeys fire with an elevated window in the foreground while
   CanvasWM runs non-elevated (expected yes) [verify].
6. Studied: whether engine start (and so the interceptor) is gated elsewhere
   (license-adjacent, not traced by rule).

## Windows notes / recommended split
Per CLAUDE.md, Win32 adapts the OS and must not decide policy. Feature #9
recorded "no IHotkeys" in Engine, and CLAUDE.md says hotkeys reach the engine
as plain method calls. Proposed split:
- **Core (pure, Mac-tested), small addition** in `CanvasWm.Core.Shortcuts`:
  - `HotkeyPlan.Desired(IReadOnlyList<DispatchEntry> table, bool recording)` →
    the distinct combos in order (empty while recording);
  - `HotkeyPlan.Diff(IReadOnlySet<KeyCombo> registered, IReadOnlyList<KeyCombo>
    desired)` → (toUnregister, toRegister), unregister first;
  - `HotkeyStatus` enum;
  - doc fix on `Key` punctuation members (D6 a);
  - optional `ReservedCombos` additions after D9 (human decision).
- **Win32** (`CanvasWm.Win32.Hotkeys`): `Win32HotkeySource(Action<Action>
  post)`, constructed on the owner thread. It contains: the message-only window
  and an `UnmanagedCallersOnly` wndproc; `Apply(IReadOnlyList<KeyCombo>
  desired) → IReadOnlyDictionary<KeyCombo, HotkeyStatus>` (it runs the Core
  diff and calls Register/UnregisterHotKey; no other decisions); a `Pressed`
  callback raised through `post` with the `KeyCombo`; `Dispose` (unregister
  all, destroy window); pure internal helpers `KeyMap` (Key ↔ VK, the 86-row
  table) and `ModMap` (Modifiers ↔ MOD_*), plus `HotkeyId`. Also a recorder
  helper for the App's settings window: `TryReadKeyEvent(msg, wParam, lParam,
  out Key, out Modifiers)` using `GetKeyState`.
- **App (#29)**: holds the bindings and the `Recording` flag. On any change it
  calls `Apply(HotkeyPlan.Desired(...))` and shows statuses in settings. On
  `Pressed` it calls `ShortcutDispatch.Match` and then the engine method for the
  command. The settings recorder uses focused-window capture plus
  `ShortcutEditor`.
- If the team prefers the press → command → engine routing and the
  retry/availability state to be Mac-testable, move them into an Engine
  `HotkeyBinder`. That would reverse #9's "no IHotkeys" decision, so it needs a
  human decision.
- Licensing / feature gate: none of the shortcut, recorder or interceptor code
  traced here touched it. The license-adjacent start-up code was deliberately
  not traced.
