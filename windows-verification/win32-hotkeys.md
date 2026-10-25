# Windows verification: win32-hotkeys (feature #12)

Spec: `specs/win32-hotkeys.md`. Tests: `tests/CanvasWm.Win32.Tests/Hotkeys/` (namespace
`CanvasWm.Win32.Tests.HotkeysSpec`). Compiled on the Mac, never run there. Nothing below has been run.

## Prerequisites

- Windows 10 and/or 11, interactive desktop session (not a service, not locked, not RDP-minimised).
- .NET 10 SDK. Repo checked out; run from the repo root.
- Do not touch mouse or keyboard during the automated run: AC24 injects Ctrl+Alt+Shift+F22 with `SendInput`.
- Nothing else may own Ctrl+Alt+Shift+F21, F22 or Ctrl+Alt+Shift+Win+F24 (the test combos T3, T2, T1). If one is
  owned the rig fails with "test combo owned by another program": close the other program and rerun.
- For the manual part: Notepad, a classic Win32 app with a menu bar (for example WordPad or Registry Editor),
  an elevated Notepad (AC33), Magnifier (Win+Plus), a German (QWERTZ) and a French (AZERTY) keyboard layout
  installed (Settings > Time & language > Language), and a way to toggle Game Bar (Settings > Gaming > Xbox
  Game Bar). Windows 10 and 11 machines for the full matrix.
- Probe output needs console output: add `--logger "console;verbosity=detailed"` to the manual commands.

## Automated commands

Whole feature (AC10-AC26):

    dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~HotkeysSpec"

Everything, including #10 and #11 (their AC15 / AC12 public-surface lists were amended, spec R2):

    dotnet test tests/CanvasWm.Win32.Tests

Single AC (replace the number; the leading dot stops AC1 matching AC10-AC19):

    dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~HotkeysSpec&FullyQualifiedName~.AC24_"

AC15 and AC16 have lettered tests: use `~.AC15a_`, `~.AC15b_`, `~.AC16a_` and so on, or `~.AC16` for all of AC16.
AC10-AC17 need no desktop; AC15b, AC18-AC26 do. Windows must not be locked.

## Automated acceptance criteria (tick when the test passes)

- [ ] AC10 `KeyMap` (PureHelperTests.AC10_KeyMap)
- [ ] AC11 `ModMap` (PureHelperTests.AC11_ModMap)
- [ ] AC12 `HotkeyIds` (PureHelperTests.AC12_HotkeyIds)
- [ ] AC13 `ReportFor`, every B12 row (PureHelperTests.AC13_ReportFor, 10 cases)
- [ ] AC14 `DecodeHotkey` (PureHelperTests.AC14_DecodeHotkey)
- [ ] AC15 key messages: (a) `TranslateKeyMessage` (AC15a_*), (b) `ReadKeyMessage` with a faked keyboard state on a
      message loop thread (AC15b_*; restores the keyboard state afterwards)
- [ ] AC16 `PressQueue` (AC16a_ .. AC16f_, six tests)
- [ ] AC17 public surface via reflection (PureHelperTests.AC17_PublicSurface)
- [ ] AC18 construction, message-only window, second source, null post (RealRegistrationTests.AC18_*)
- [ ] AC19 register and unregister (RealRegistrationTests.AC19_*)
- [ ] AC20 Taken and retry (RealRegistrationTests.AC20_*)
- [ ] AC21 Apply errors leave the set unchanged (RealRegistrationTests.AC21_*)
- [ ] AC22 duplicates and order (RealRegistrationTests.AC22_*)
- [ ] AC23 posted WM_HOTKEY, strict check, stale message (RealRegistrationTests.AC23_*)
- [ ] AC24 real `SendInput` with 10 repeats gives exactly one press (RealRegistrationTests.AC24_*)
- [ ] AC25 Dispose on the owner thread (AC25a_*) and off it (AC25b_*)
- [ ] AC26 labels on the US layout; skipped with "US layout not installed" if absent (RealRegistrationTests.AC26_*)

Also tick: the amended #10 AC15 and #11 AC12 tests pass in the full run.

## Probe tools (AC27-AC34)

All are `[Fact]`s in `HotkeyProbeManual`. Without `CANVASWM_MANUAL=1` they return at once and write nothing.

Environment (PowerShell):

    $env:CANVASWM_MANUAL = "1"
    $env:CANVASWM_LABEL  = "win11-gamebar-on"      # one label per run; names the output file
    $env:CANVASWM_HOTKEY_GROUPS = "nc,defaults,controls,candidates"   # optional, comma list

Output file: `%TEMP%\canvaswm-hotkeys-<label>.tsv`. Delete it before a rerun with the same label (rows are appended).
Columns: `Test, Group, Combo, RegisterResult (ok | taken | error:<code>), Status, Fired (yes | no | skipped), Ms,
ForegroundBefore, ForegroundAfter, Note`.

- [ ] **AC27** Probe off by default. With `CANVASWM_MANUAL` unset (`Remove-Item Env:CANVASWM_MANUAL`) run

      dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~HotkeyProbeManual"

  Expected: 4 tests pass at once, no TSV appears in `%TEMP%`, nothing is registered.

- [ ] **AC28** Reserved-combo probe. Set `CANVASWM_MANUAL=1` and a label, then:

      dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~HotkeyProbeManual.RegisterAll" --logger "console;verbosity=detailed"
      dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~HotkeyProbeManual.PressEach" --logger "console;verbosity=detailed"

  `RegisterAll` registers all four groups at once and writes one row per combo (Fired = skipped).
  `PressEach` goes group by group. With Notepad focused, when the console prints `Press <combo> now (6 s; do nothing
  to skip)`, press that combo once, or do nothing to skip it. It waits the 6 s plus 1 s for the foreground check.
  Win+L is registered but never prompted. Groups: `nc` (the `ReservedCombos` rows that need confirmation),
  `defaults` (WinAlt: 30), `controls` (Win+L, Win+D, Win+Left, Ctrl+Shift+Escape), `candidates` (16).
  Use `CANVASWM_HOTKEY_GROUPS=nc` and so on to run one group.
  Runs required, each with its own `CANVASWM_LABEL`:
  - [ ] Windows 11, Game Bar on
  - [ ] Windows 11, Game Bar off
  - [ ] Windows 10, Game Bar on
  - [ ] Windows 10, Game Bar off
  - [ ] `nc` Ctrl+Alt rows with Magnifier closed, then again with Magnifier open (labels `...-mag-off`, `...-mag-on`)

  For each combo classify the outcome in the results table below. Pass condition: a TSV and a filled table exist for
  every run.

- [ ] **AC29** WinAlt defaults on a clean Windows 11 (and on Windows 10). From the `defaults` rows: all 30 show
  `RegisterResult = ok` and `Fired = yes`, and Windows takes no visible action (check `ForegroundBefore` equals
  `ForegroundAfter`, and watch the screen). Write up any failure for a human decision; do not edit the spec.

- [ ] **AC30** Alt/Win side effects, observation only (Windows 10 and 11). Run `PressEach` with
  `CANVASWM_HOTKEY_GROUPS=defaults`, first with Notepad focused, then a classic app with a menu bar. Press
  Win+Alt+O when prompted and record:
  - does the menu bar get focus, or do access-key underlines appear after release? yes / no
  - does the Start menu open when Win is released? yes / no
  Repeat for Win+Alt+Left. Expected: observation only; any "yes" is raised for a human decision (#11 A10). Nothing is
  built to fix it.

- [ ] **AC31** Hold and repeat. In `PressEach` (`defaults`), when `Win+Alt+Right` is prompted, hold it for 2 s.
  Expected: its row has `Fired = yes` and Note `presses=1`.

- [ ] **AC32** Non-US layouts. On each layout (switch with Win+Space):

      dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~HotkeyProbeManual.LayoutLabels" --logger "console;verbosity=detailed"

  It writes `label=` and `OnLayout` / `NotOnLayout` for D1 and the 11 punctuation keys. Then run `PressEach` with
  `CANVASWM_HOTKEY_GROUPS=defaults` and a fresh label.
  - [ ] German (QWERTZ): the LeftBracket label is `ß`; Win+Alt+ß fires `LeftBracket+Win+Alt` (Back); Win+Alt+O fires
    from the key labelled O.
  - [ ] French (AZERTY): Win+Alt+Shift plus the top-row key labelled 1/& fires `D1+Win+Alt+Shift`; Win+Alt+O fires from
    the key labelled O.
  - [ ] Record every punctuation key whose row says `NotOnLayout`: ______________

- [ ] **AC33** Elevated foreground. Start an elevated Notepad and focus it. From a non-elevated terminal run
  `PressEach` (`defaults`). Record whether Win+Alt+O fires: yes / no (expected yes).

- [ ] **AC34** Recorder probe:

      dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~HotkeyProbeManual.RecorderProbe" --logger "console;verbosity=detailed"

  A window opens (120 s, or `CANVASWM_PROBE_SECONDS`; close it to end). With it focused, press each and check the
  console line / TSV row (`Test = RecorderProbe`):
  - [ ] Win+Alt+Shift+Up -> `Win+Alt+Shift+Up`
  - [ ] Ctrl+Alt+[ -> `Ctrl+Alt+LeftBracket`
  - [ ] Escape -> `Escape`
  - [ ] F10 -> `F10` (no menu mode)
  - [ ] numpad 1 with NumLock on -> `(null)`
  - [ ] on a layout with AltGr, AltGr+Q -> `Ctrl+Alt+Q`
  - [ ] Win+D: never reaches the window (note what happens): ______________
  - [ ] after Win+Alt+Shift+Up, releasing Win opens Start? yes / no: ______
  - [ ] no menu mode is entered after Alt combos

## Results table template (AC28), one per run

Run label: ____________   OS: ______   Game Bar: on / off   Magnifier: on / off

| Group | Combo | fails (taken / error code) | succeeds, never fires | succeeds and Windows also acts | works cleanly | Notes |
|---|---|---|---|---|---|---|
| nc | | | | | | |
| defaults | | | | | | |
| controls | | | | | | |
| candidates | | | | | | |

Guide: "fails" = `RegisterResult` is `taken` or `error:<n>`. "succeeds, never fires" = `ok` and `Fired = no` although
you pressed it. "succeeds and Windows also acts" = `ok`, `Fired = yes`, and Windows did something visible (Start,
Game Bar, a jump list, a window snap; `ForegroundBefore != ForegroundAfter` is a hint). "works cleanly" = `ok`,
`Fired = yes`, no Windows action.

The results are the input to a **separate, later** Core change that edits `ReservedCombos`. This feature changes no
`ReservedCombos` data (spec A12).

## If something fails

- **Constructor throws `Win32Exception` (RegisterClassExW / CreateWindowExW).** The window procedure is a single
  `[UnmanagedCallersOnly]` static method whose pointer is stored in `NativeWndClassEx.lpfnWndProc`. Check the error
  code in the exception. Likely suspects: `cbSize` of `NativeWndClassEx` (struct layout / padding), the
  `lpszClassName` pointer, `GetModuleHandleW(null)` returning 0, or the string marshalling of the class name /
  `HWND_MESSAGE` (-3 as a `nint`) in `CreateWindowExW`. AC18 isolates this. A **second source on one thread** hitting
  `ERROR_CLASS_ALREADY_EXISTS` (1410) is treated as success; if AC18's second source throws, look at that path and at
  the per-process `s_classRegistered` flag.
- **AC24 gets no press, but AC23 passes.** The strict `WM_HOTKEY` lParam check (spec A4) is probably dropping real
  presses (lParam low word = modifier flags, high word = VK). Per A4, relax `DecodeHotkey` to an id-only check and
  report it. AC24 is the test that exists to decide this.
- **AC24 gets more than one press.** `MOD_NOREPEAT` (0x4000) is not reaching `RegisterHotKey`; check
  `ModMap.RegisterFlags`. Also confirm the injected input did not arrive twice.
- **AC25 / Dispose problems.** On the owner thread `UnregisterHotKey` and `DestroyWindow` run inline. Off it they are
  posted: if T1 stays held after the posted action ran, the action ran on the wrong thread (the post delegate in the
  rig must run on loop L) or `DestroyWindow` failed from a foreign thread. A hang at test end usually means a loop
  was disposed before the source.
- **AC26 / AC32 labels wrong or empty.** `MapVirtualKeyExW(VK, MAPVK_VK_TO_CHAR, hkl)`: the high bit marks a dead key
  (ignored); a value below 0x20 falls back to the US character. On German `[` (VK_OEM_4) is `ß`; on a layout where the
  VK has no key the label is the US character and `IsOnLayout` (`MAPVK_VK_TO_VSC_EX` result 0) should be false. Check
  that `hkl` is the foreground thread's layout and that the test layout was actually active (Win+Space).
- **AC21 / AC23 intermittent.** A test combo owned by another program, or the machine session locked. Rerun after
  closing hotkey utilities.
- **AC15b fails.** `SetKeyboardState` acts on the calling thread's key state; it must run on the message loop thread
  (it does). Some environments (remote sessions, a different foreground thread) make `GetKeyState` ignore it: note the
  environment and run locally.
- **Probe prompts invisible.** Add `--logger "console;verbosity=detailed"`.
