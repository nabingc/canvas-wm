# Feature: shortcut-model

## Summary
The studied app has twelve fixed, individually rebindable keyboard actions
plus nine "saved view" slots. Each action owns at most one binding (a key plus
an exact modifier set) or is explicitly unbound; the nine saved-view slots
share two modifier sets (one for "go to view n", one for "save view n") applied
to the digit keys 1–9. Bindings are matched by exact key + exact modifier set
on key-down, fire once per physical press (auto-repeat is swallowed, never
fires again), and the matching key-up is swallowed too. Assigning a combo that
another action already uses moves it (the other action becomes unbound);
combos that collide with the saved-view digit keys are refused.

## Behaviour

### A. Action catalogue
1. There are exactly 12 bindable actions, in this fixed order (the order is
   also the match/registration order, rule 15). Each maps to one engine
   command from `findings/engine-inbound-api.md` rule 29:

   | # | id | Our description | Engine command (inbound API name) | Target engine | Repeats on hold |
   |---|---|---|---|---|---|
   | 0 | `everything` | Open/close the zoomed-out view of the whole layout | `toggleOverview()` | active | No |
   | 1 | `stepOut` | Zoom the camera out one level of the layout tree | `zoomOut()` | active | No |
   | 2 | `stepIn` | Zoom the camera in one level (toward the pointer when enabled) | `zoomIn(toward: point?)` | see rule 3 | No |
   | 3 | `left` | Go to the neighbouring area on the left | `move(toward: Left=0)` | active | No |
   | 4 | `right` | Go to the neighbouring area on the right | `move(toward: Right=1)` | active | No |
   | 5 | `up` | Go to the neighbouring area above | `move(toward: Top=2)` | active | No |
   | 6 | `down` | Go to the neighbouring area below | `move(toward: Bottom=3)` | active | No |
   | 7 | `back` | Go back in camera history | `back()` | active | No |
   | 8 | `forward` | Go forward in camera history | `forward()` | active | No |
   | 9 | `maximize` | Frame the focused window alone / undo | `toggleMaximize(nil)` (nil = focused window) | active | No |
   | 10 | `desktop` | Toggle hiding the desktop area in the current workspace | `setDesktopHidden(!desktopHidden)` (≡ `toggleDesktopHidden()`) | active | No |
   | 11 | `split` | Reserve a split beside the active tile and open a new file-manager window into it | `splitForNewWindow(of: <file-manager app id>)` then ask the file manager to open a new window | pointer | No |

   Notes: the "everything" key calls the overview toggle, **not**
   `showEverything()`; "step out/in" keys call `zoomOut()`/`zoomIn(...)`,
   **not** `stepOut()`/`stepIn(...)`.
2. "Active engine" = the engine of the current (display, desktop) the app
   considers active; if there is none the key does nothing (but is still
   consumed, rule 11).
3. `stepIn` target: if the "step toward pointer" preference is on (default
   **on**) and an engine exists under the pointer, call `zoomIn` on that
   engine with the pointer position — except when that engine is in the
   overview, then with no point. If the preference is off or no pointer
   engine exists, call `zoomIn(toward: none)` on the active engine.
4. `split` acts on the engine under the pointer (no fallback to the active
   engine). The argument is the application identifier of the system file
   manager (not a window key); the new-window request is issued
   asynchronously afterwards.

### B. Saved views (framings)
5. Nine slots, numbered 1–9, bound to the digit keys 1–9 of the main row
   (not the numeric keypad).
6. Two actions per slot: **go to view n** → `goToFraming(n)`, **save current
   view as n** → `saveFraming(n)`, both on the active engine; no repeat.
   `forgetFraming(n)` exists in the engine but has no keyboard binding.
7. The 9 slots do not have individual bindings. There are two modifier sets:
   *recall modifiers* (default Ctrl+Option) and *save modifiers* (default
   Ctrl+Option+Shift). Slot n's binding is (digit n, that modifier set).
   Either set can be unbound, which removes all nine bindings of that kind.
8. What a saved view stores is engine state (persisted per display in the
   saved-display record's `framings` collection); the shortcut layer only
   passes the slot number. (Content of a framing: Unknown at this level;
   belongs to the camera/framing feature.)

### C. Binding model
9. A binding = (key code, modifier set). Modifier set ⊆ {Ctrl, Option(Alt),
   Shift, Command}. Caps Lock, Fn and left/right distinctions are ignored.
   Internally packed as one 64-bit number: key code in the low 32 bits,
   Carbon-style modifier bits in the high 32 (Command 0x100, Shift 0x200,
   Option 0x800, Control 0x1000).
10. Per action: **one** binding or **unbound**. No multiple bindings per
    action, no chords/sequences, no key-up bindings, no mouse/gesture
    bindings in this model.
11. Matching (key-down): exact key code **and** exact modifier set (Ctrl+Opt+↑
    does not fire on Ctrl+Opt+Shift+↑ and vice versa). First matching binding
    in list order wins (actions in rule-1 order, then for each digit 1..9:
    recall then save). A matched key-down is consumed (not delivered to the
    focused app) and its key code is remembered as "held".
12. Auto-repeat: a repeated key-down that matches is consumed but the action
    is **not** run again. So no action repeats on hold.
13. Key-up: if the key code is in the "held" set it is removed and the key-up
    is consumed; otherwise the key-up passes through. Key-up never triggers
    an action.
14. While the settings UI is recording a new shortcut, matching is suspended
    entirely (all keys pass to the recorder).
15. Bindings are (re)built from settings at engine start and again whenever
    the shortcut settings change (a change notification is posted on every
    write). Unbound entries are skipped.

### D. Editing rules (settings recorder)
16. A recorded combo must contain at least one of Ctrl, Option or Command;
    Shift alone or no modifier is rejected with a beep and a hint.
17. With no modifiers held: Delete or Forward-Delete **unbinds** the action
    (or the view modifier set being edited); Escape cancels recording without
    change.
18. Setting an action's combo:
    a. If the key is a digit 1–9 and the modifiers equal the current recall
       or save modifier set → **refused** (stays as before), with a hint to
       change the view modifiers first.
    b. Else if another action already has exactly this combo → the combo is
       **moved**: the other action becomes unbound, this action takes it, and
       the user is told which action lost its shortcut.
    c. Else stored.
19. Setting a view modifier set: the user presses modifiers + any digit 1–9
    (a non-digit key is rejected). Refused if equal to the other view set
    (recall and save must differ). Refused (no stealing) if some action is
    bound to a digit key with those exact modifiers. Else stored.
20. "Restore defaults" resets all 12 actions and both view modifier sets to
    defaults; an "are defaults" check drives whether that button is enabled.

### E. Persistence shape
21. One string setting per action, key `shortcut.<id>`, value
    `"<keyCode>,<modifiers>"` in decimal (e.g. `"49,6144"`), or the literal
    `none` for unbound. Missing setting → default from the table below.
    A present value that does not parse as exactly two unsigned integers →
    unbound.
22. View sets: `shortcut.views` and `shortcut.saveViews`, value = decimal
    modifier bits, or `none` for unbound; missing → default.

### F. Studied defaults (macOS, facts only)
23. All defaults use Ctrl+Option (0x1800); Shift added = 0x1A00.

    | id | Default modifiers | Key (macOS key code) |
    |---|---|---|
    | everything | Ctrl+Opt | Space (0x31) |
    | stepOut | Ctrl+Opt | Up (0x7E) |
    | stepIn | Ctrl+Opt | Down (0x7D) |
    | left | Ctrl+Opt | Left (0x7B) |
    | right | Ctrl+Opt | Right (0x7C) |
    | up | Ctrl+Opt+Shift | Up (0x7E) |
    | down | Ctrl+Opt+Shift | Down (0x7D) |
    | back | Ctrl+Opt | `[` (0x21) |
    | forward | Ctrl+Opt | `]` (0x1E) |
    | maximize | Ctrl+Opt | Return (0x24) |
    | desktop | Ctrl+Opt | D (0x02) |
    | split | Ctrl+Opt | N (0x2D) |
    | go to view n | Ctrl+Opt | digit n |
    | save view n | Ctrl+Opt+Shift | digit n |

    Digit key codes 1..9: 0x12 0x13 0x14 0x15 0x17 0x16 0x1A 0x1C 0x19.

### G. Other input facts found (not part of the binding table)
24. Escape is checked before shortcuts by the global key interceptor and goes
    to a separate "escape" handler (cancel overview/drag); it is not
    bindable.
25. While the engine is capturing keys (overview open), keys that are not a
    shortcut, have no Command and are not Ctrl+Option chords are consumed
    and sent to the overview's own key navigation (`overviewKey`); their
    key-ups are swallowed. Ctrl+Option chords that are not shortcuts pass
    through. (Correction to study §3.2: the Ctrl+Option-without-Command test
    gates this overview capture path, not shortcut matching; shortcuts with
    any modifiers match.)
26. **Scroll-zoom modifier (resolves study §7 open item):** for scroll-wheel
    events over the app's own stage/overlay view, `zoomModifier` is true when
    **Command or Option** is held; otherwise scroll pans. Scroll phase is
    passed through as began/changed/ended-style values and momentum end is
    flagged. Scroll is not part of the configurable binding model.
27. The same bindings are also shown as key equivalents on the app's menu
    items and in tooltips (display only).

## Data model
- `ShortcutAction` enum, 12 cases in rule-1 order, each with a stable id
  string (`everything` … `split`).
- `Modifiers` flags: Ctrl, Alt(Option), Shift, Win/Command.
- `KeyCombo` = (key, modifiers); equality is exact on both.
- `ShortcutBindings`: map action → `KeyCombo?` (null = unbound); plus
  `RecallViewModifiers: Modifiers?`, `SaveViewModifiers: Modifiers?`.
- Derived dispatch list: ordered list of (combo, command) — 12 action
  entries (skipping unbound) then 9×(recall, save) entries.
- `ShortcutCommand` (what the app layer executes): one of the 12 actions or
  `GoToView(n)` / `SaveView(n)`, n ∈ 1..9.
- Edit result: `Stored` | `MovedFrom(action)` | `ConflictsWithViews` |
  `ConflictsWithAction(action)` (view sets) | `RecallEqualsSave` |
  `Rejected(reason)` (no required modifier / non-digit for views).
- Held-key set (runtime state of the dispatcher): key codes whose key-down
  was consumed.

## Worked examples
1. Defaults, press Ctrl+Opt+Space → `everything` → toggle overview; consumed.
2. Defaults, press Ctrl+Opt+Shift+Up → `up` (move toward Top), not `stepOut`.
3. Defaults, hold Ctrl+Opt+Right: first key-down → move right once; 10
   auto-repeat key-downs → all consumed, nothing more happens; key-up →
   consumed. Total: one move.
4. Defaults, press Ctrl+Opt+Cmd+Right → no match (extra modifier) → passes
   through.
5. Defaults, press Ctrl+Opt+3 → `goToFraming(3)`; Ctrl+Opt+Shift+3 →
   `saveFraming(3)`.
6. Set `back` to Ctrl+Opt+Space → `everything` becomes unbound, `back` =
   Ctrl+Opt+Space, result MovedFrom(everything).
7. Set `desktop` to Ctrl+Opt+5 (recall mods Ctrl+Opt) → refused,
   ConflictsWithViews; `desktop` keeps Ctrl+Opt+D.
8. Set recall modifiers = Ctrl+Opt+Shift while save = Ctrl+Opt+Shift →
   refused, RecallEqualsSave.
9. `split` bound to Cmd+Opt+7, then set recall modifiers to Cmd+Opt → refused
   ConflictsWithAction(split).
10. Record Shift+K → rejected (needs Ctrl/Opt/Cmd). Record Delete with no
    modifiers while editing `maximize` → `maximize` unbound (stored `none`).
11. Persistence: `shortcut.everything` absent → Ctrl+Opt+Space;
    `"49,6144"` → Space + Ctrl+Opt; `none` → unbound; `"abc"` → unbound;
    `"49"` → unbound (needs exactly two numbers). `shortcut.views` = `none`
    → no recall bindings at all; save bindings unaffected.
12. Key-up of an unrelated key with no prior consumed down → passes through.

## Evidence
(addresses in `~/Documents/trelliswm-re/evidence/shortcut-model-notes.txt`)
- Rule 1 order/ids/defaults: study §3.2; `registerHotkeys` 0x100132ba0
  (inline default constants e.g. 0x1800_00000031). Commands: closure thunks
  0x100141744 (toggleOverview), 0x100141918 (zoomOut), 0x100133f4c (zoomIn),
  0x100141940/0x10014195c/0x100141978/0x100141994 → `move(toward:)` 0/1/2/3,
  0x100141704 (back), 0x100141724 (forward), 0x100134128 (toggleMaximize nil),
  0x100136abc (setDesktopHidden(!desktopHidden)), 0x1001341d0 (split).
  Edge raw values: `findings/directional-focus.md` rule 3.
- Rules 2–4: same closures (activeEngine / pointerEngine getters);
  `Preferences.stepTowardPointer` 0x1001c0840 (default true); engine field
  0x279 = inOverview (`engine-field-offsets.txt`).
- Rules 5–7: `framingBindings` 0x1001344bc (`dec-framingBindings.json`),
  thunks 0x1001416e4 (goToFraming), 0x1001419e4 (saveFraming);
  `dec-digitsInit.json`; study §3.2 digit codes.
- Rule 8: `SavedDisplay.framings` symbols; `Engine.forgetFraming` symbol.
- Rules 9, 11–13: `Hotkeys.handle` 0x10017dd28 (`dec-hotkeysHandle.json`);
  `ShortcutRecorder.carbon` 0x1001c875c; `dec-shortcutCombo.json` packing.
- Rule 10: `Shortcuts.combo`/`set` store a single value per action; framing
  bindings derived from two modifier settings.
- Rule 14: `Shortcuts.recording` checked in `Hotkeys.handle`.
- Rule 15: `registerHotkeys` callers (startEngine and a closure inside it);
  `Shortcuts.changed` posted by `set`/`setViewModifiers`.
- Rules 16–19: `ShortcutRecorder.handle` 0x1001c7c68; `Shortcuts.set`
  0x1001caa14; `Shortcuts.setViewModifiers` 0x1001cb018 (batch file
  `dec-shortcut-model-batch.json`).
- Rule 20: `Shortcuts.restoreDefaults` 0x1001ca728, `areDefaults`
  0x1001c9ff8, `SettingsWindow.restoreShortcuts` symbol.
- Rules 21–22: `Shortcuts.set` (comma join, `none`), `Shortcuts.decode`
  0x1001c93f4 (exactly two parts), `Shortcuts.modifiers(forKey:default:)`
  0x1001c9c58 (`none` → nil), `dec-shortcutCombo.json` (default fallback).
- Rule 23: study §3.2 table; inline constants in `registerHotkeys`.
- Rules 24–25: `dec-eventTapHandle.json` (Escape 0x35 first, then shortcut
  handler, then capture-mode path).
- Rule 26: `StageView.scrollWheel` 0x1001dfd80 (saved in
  `shortcut-model-bundle.json`).
- Rule 27: `Shortcuts.menuKey`, `Shortcuts.tip` symbols.

## Confidence
| Rule | Confidence |
|---|---|
| 1 ids/order/defaults/commands | Observed |
| 1 "repeats on hold = No" | Observed (rule 12) |
| 2 active-engine target; no-engine → nothing | Observed; "still consumed" Observed (consume happens before the closure runs) |
| 3 stepIn pointer logic, default on | Observed |
| 4 split on pointer engine, app id argument | Observed; async follow-up Observed |
| 5–7 | Observed |
| 8 | Observed that a per-display collection exists; content Unknown |
| 9 modifier packing | Observed |
| 10 one binding per action, no chords | Observed (storage shape); "no gesture bindings" Inferred (none found) |
| 11 exact match, first wins | Observed |
| 12, 13, 14 | Observed |
| 15 rebuild on change | Inferred (caller is a closure in startEngine, assumed to be the change observer) |
| 16, 17 | Observed |
| 18a, 18c | Observed |
| 18b move/steal | Observed user-facing outcome; exact clearing mechanism Inferred (inside a jump table) |
| 19 | Observed results; "action uses a digit key" criterion Inferred (jump table) |
| 20 | Observed existence; exact keys reset Inferred |
| 21, 22 | Observed |
| 23 | Observed |
| 24, 25 | Observed control flow; which closure is "shortcut" vs "key" handler Inferred from field roles |
| 26 | Observed |
| 27 | Inferred (symbol names only) |

## Unknowns
- Exact body of the per-action conflict jump tables in `set` /
  `setViewModifiers` (inferred: compare current combo of each other action).
- Whether "restore defaults" also resets the view modifier sets (likely).
- Contents of a saved framing (camera target node vs rect) — out of this
  feature's scope.
- Whether the shortcut layer is suspended while the app is disabled/paused
  (not traced).
- Trackpad pinch/gesture thresholds (not part of this feature).
- No licensing/feature gate was seen on any action in the traced paths.

## Windows notes (CanvasWM design input — not studied behaviour)
- **Key identity.** Store bindings by Windows virtual-key code (layout
  mapped) for letters/arrows/Return/Space, but treat the 1–9 slot keys and
  `[`/`]` carefully: on non-US layouts `VK_OEM_4`/`VK_OEM_6` are other
  characters or need AltGr. Recommend storing VK + a display name; digits use
  `'1'..'9'` VKs (top row) — `VK_NUMPAD1..9` could be an optional extra.
  Scan codes are layout-independent but `RegisterHotKey` takes VKs only; if a
  low-level hook dispatcher is used, it can match on scan code instead.
- **Modifier mapping.** Ctrl→MOD_CONTROL, Option→MOD_ALT, Shift→MOD_SHIFT,
  Command→MOD_WIN. The "needs Ctrl, Alt or Command" rule becomes "needs Ctrl,
  Alt or Win".
- **AltGr.** On many layouts AltGr is delivered as Ctrl+Alt (right Alt plus a
  synthetic left Ctrl), so Ctrl+Alt+digit/`[`/`]`/letters type characters
  there; a global Ctrl+Alt hotkey steals them. The spec needs an
  "AltGr-safe" default profile, e.g. Win+Alt (+Shift for "up/down" and
  "save view"), and may warn when a user picks Ctrl+Alt with a printable key.
- **Reserved combos.** Win+L cannot be registered (and Ctrl+Alt+Del is not
  interceptable). Avoid Win+arrows (Snap), Win+Ctrl+Left/Right/D (virtual
  desktops), Win+D (show desktop), Win+Tab, Win+digits (taskbar launch),
  Win+Space (input language), Win+Shift+arrows (move to monitor), Alt+Tab,
  Alt+F4, Alt+Space. `RegisterHotKey` fails if another process owns the
  combo — the spec should surface "unavailable" per binding (the studied app
  has no such state because it intercepts events instead of registering).
- **Repeat.** Use `MOD_NOREPEAT` for every binding (studied behaviour:
  nothing repeats on hold), or ignore repeats in a hook dispatcher. Note the
  study's earlier suggestion to allow repeat for navigation keys conflicts
  with Observed rule 12.
- **Key-up swallowing.** `RegisterHotKey` already consumes the key-down;
  stray key-ups reaching the foreground app are generally harmless. A
  `WH_KEYBOARD_LL` implementation should mirror rules 11–13 (consume matched
  down, swallow its up).
- **Escape / overview capture** (rules 24–25) needs a low-level hook active
  only while overview/drag is open; out of this Core model.
- **Scroll zoom** (rule 26): Win equivalent of Command/Option for
  wheel-over-overlay zoom is likely Ctrl (conventional Ctrl+wheel zoom) and/or
  Alt; decide in the scroll feature.
- **Split action**: the file-manager app id becomes Explorer's process/AUMID;
  "open a new Explorer window" replaces the Finder request.
- **Persistence**: keep the `"<key>,<mods>"` / `none` string shape in our own
  JSON settings (with Windows VK and MOD_* values, not macOS codes); a
  version field is advisable since codes differ from the studied app.
