# Windows verification: win32-frame-writer (feature #11)

Spec: `specs/win32-frame-writer.md`. Tests: `tests/CanvasWm.Win32.Tests/FrameWriter/`
(namespace `CanvasWm.Win32.Tests.FrameWriterSpec`). Compiled on the Mac, never run there.

## Prerequisites

- Windows 10/11, interactive desktop session (not a service, not locked, not RDP-minimised).
- .NET 10 SDK. Repo checked out; run from the repo root.
- Windows PowerShell 5.1 at the default path (the `ProbeProcess` helper hosts a WinForms window in it).
- Do not touch mouse or keyboard during the automated run (AC27, AC31 move focus and the cursor).
- For manual AC32/AC36: two monitors (100 % and 150 %) are best; AC39 needs a 125 % monitor.
- Notepad (several instances), Snipping Tool, a terminal, Edge or Chrome. `regedit` for AC34.
- Close anything that already uses Ctrl+Shift+F12.

## Automated commands

Whole feature:

    dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~FrameWriterSpec"

Everything, including #10 (AC13 needs the existing #10 tests to still pass):

    dotnet test tests/CanvasWm.Win32.Tests

Single AC (replace the number):

    dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~FrameWriterSpec&DisplayName~AC20"

Note: use the trailing underscore (`DisplayName~AC2_`) so AC2 does not also match AC20-AC29. The pure fake-based
ones (AC14-AC25) do not need a desktop; AC26-AC31 do.

## Automated acceptance criteria (tick when the test passes)

- [ ] AC8  `DipSpace.ToPixels` (PureHelperTests.AC8_*)
- [ ] AC9  round trips and shared edges (PureHelperTests.AC9_*)
- [ ] AC10 point `ToDip` (PureHelperTests.AC10_*)
- [ ] AC11 `FrameMath` (PureHelperTests.AC11_*)
- [ ] AC12 public surface via reflection (PureHelperTests.AC12_*)
- [ ] AC13 `Describe(h).Frame == WindowGeometry.ReadDipFrame(h)` (PureHelperTests.AC13_*) and the whole #10 suite passes
- [ ] AC14 single write W1 (AlgorithmTests.AC14_*)
- [ ] AC15 settle pass (AlgorithmTests.AC15_*, 4 tests)
- [ ] AC16 off-screen re-push (AlgorithmTests.AC16_*, 4 tests)
- [ ] AC17 batch and W4 fallback (AlgorithmTests.AC17_*)
- [ ] AC18 pre-filter W5, null identity, no monitors (AlgorithmTests.AC18_*)
- [ ] AC19 maximised restore order (AlgorithmTests.AC19_*)
- [ ] AC20 crossing DPI W3 (AlgorithmTests.AC20_*)
- [ ] AC21 exception containment (AlgorithmTests.AC21_*)
- [ ] AC22 threading contract and argument checks (SystemContractTests.AC22_*)
- [ ] AC23 disposal and completion errors (SystemContractTests.AC23_*)
- [ ] AC24 queries (SystemContractTests.AC24_*)
- [ ] AC25 focus with fakes (SystemContractTests.AC25_*)
- [ ] AC26 in-process window write/read/insets (RealWindowTests.AC26_*)
- [ ] AC27 maximised window restored, foreground unchanged (RealWindowTests.AC27_*)
- [ ] AC28 probe + self batch, z-order, IsShowing (RealWindowTests.AC28_*)
- [ ] AC29 hung probe does not block (RealWindowTests.AC29_*; takes about 25 s)
- [ ] AC30 dead handle / wrong thread id (RealWindowTests.AC30_*)
- [ ] AC31 pointer, buttons, displays (RealWindowTests.AC31_*; moves the cursor)

## Manual probes

Each probe is a test in `FrameWriterManual`. Run it with:

    set CANVASWM_MANUAL=1
    dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~FrameWriterManual.<Name>" --logger "console;verbosity=detailed"

(PowerShell: `$env:CANVASWM_MANUAL="1"`.) Choose the target window either by title substring
(`CANVASWM_TITLE`, plus `CANVASWM_TITLE2`/`CANVASWM_TITLE3` where a probe needs more) or leave the
variable unset and click the window during the 5 s countdown printed to the console. Output goes to
`%TEMP%\canvaswm-writes.tsv` (header row, one row per write: target, result, milliseconds, note). Each
probe overwrites the file; copy it out before the next run.

- [ ] AC32 `MoveAcross`. Monitor 1 at 100 %, monitor 2 at 150 %. Pick Notepad on monitor 1. The probe writes
  it to the centre half of the other monitor and back. Expect: each result within 1.0 DIP of its target
  (the test asserts this); the visible border sits where requested (Snipping Tool, within 2 px); TSV
  recorded (whether the settle pass ran is not printed; compare the first row result with the target: a
  difference over 1.0 before the second reading is not visible, so just note the result). Repeat with Edge or Chrome.
- [ ] AC33 `TileLeftHalf`. Maximise Notepad, click another window, run the probe with Notepad's title.
  Expect: Notepad restored (not maximised), fills the left half with its visible border at the work-area
  edge and no gap at the screen edge; the other window stays foreground; double-clicking Notepad's title bar
  maximises it again.
- [ ] AC34 elevated window. From a non-elevated terminal start `regedit` (accept UAC) and run `TileLeftHalf`
  with title "Registry Editor". Expect: TSV result is `null`, regedit does not move, no exception. Repeat from
  an elevated terminal: result non-null.
- [ ] AC35 `WriteLoop` (two Notepads alternate every second for 30 s; set `CANVASWM_TITLE` and `CANVASWM_TITLE2`
  and `CANVASWM_HANG=1` to also run a hung probe). Expect: Notepad rows continue on schedule (Ms small,
  rows about one second apart) for the whole run; probe rows become `null` after about 5 s.
- [ ] AC36 `ParkAndWait` (writes the window to the bottom-right park spot of the primary monitor: default
  fallback origin with the window size; the `ParkFrame` helper is not used by the probe). Expect: result
  within 1.0 of the target; after 60 s the read gives the same frame (the test asserts equality); only a
  sliver of at most 2 px visible at the corner (look at it); target pixels within +-30000. Repeat at 150 %.
  Record whether the re-push ran (the window not being fully hidden on the first try suggests it did).
- [ ] AC37 `FocusHotkey`. The probe registers Ctrl+Shift+F12 and waits 20 s. Click a terminal so another process
  is foreground, then press the hotkey. Expect: Notepad becomes the foreground window, TSV note shows
  `LastFocusSucceeded=True;IsForeground=True`, the taskbar button does not flash. Repeat with Notepad minimised
  (it restores and gets focus). Record the result. If it fails, see "if something fails".
- [ ] AC38 probe tool sanity. With `CANVASWM_MANUAL` unset run
  `dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~FrameWriterManual"`: all six pass in
  under 1 s each and no `canvaswm-writes.tsv` is touched. With it set each probe writes the header row first.
- [ ] AC39 `RepeatTile` (125 % scaling; run only if R2 is approved and implemented, otherwise record "R2 pending").
  Three windows (`CANVASWM_TITLE`, `_TITLE2`, `_TITLE3`) get the same three tile targets three times, with a
  read between. Expect: every result and read within 1.0 DIP of the target (asserted); TSV differences at
  most 0.4 DIP on X/Y and 0.8 on W/H for apps that do not clamp. (R2 is implemented in the Engine now, so run it.)

## If something fails

- **Hung window blocks the writer (A2).** Symptom: AC29 times out, or every read/write/focus stops after one app
  stops answering (WriteLoop rows stop). SetWindowPos and EndDeferWindowPos wait for the target thread with no
  timeout, and `IsHungAppWindow` only flags after about 5 s. This is a known limit; the watchdog is queued as #31.
  Record which app and how long; do not work around it in this feature.
- **SetForegroundWindow after the hotkey (A10).** If AC37 gives `LastFocusSucceeded=False` or the taskbar flashes
  instead, record the foreground process and the exact steps. This needs a human decision. Do not add the
  Alt-key trick (synthetic Alt via SendInput) as a quick fix: the default shortcut profile is Win+Alt, so a
  synthetic Alt while the user still holds the modifiers corrupts modifier state (Start menu, menu-bar
  mnemonics, broken next shortcut). Any workaround must wait for all shortcut modifiers to be released and must
  skip elevated targets. AttachThreadInput stays excluded.
- **DeferWindowPos with elevated or mixed-DPI windows (A3).** If a batch containing an elevated or
  mixed-DPI window fails, `TryDeferBatch` returns false and each window is written with SetWindowPos (fallback);
  the elevated one gets null. Watch for the other possibility: a silent no-op, where the write reports success but
  nothing moved (result equals the old frame). Record it: the Engine would read that as a refusal and could
  learn a bogus limit. Check AC34 for both outcomes and AC28 (cross-process batch).
- **NativePoint by value (MonitorFromPoint) and GetCursorPos.** If AC31 gets the wrong pointer or throws
  or reports (0,0), the by-value `NativePoint` marshalling for `MonitorFromPoint` is the first suspect (the
  struct is passed as one 8-byte value). Also check `GetCursorPos` returned true and that the call ran under
  the per-monitor-v2 `DpiScope`.
- **EnumDisplayMonitors callback.** If `Monitors.Enumerate()` returns an empty list (every write null, AC31
  `ParkingOrigin` equals 10000,10000), the `UnmanagedCallersOnly` callback or GCHandle passing is wrong
  (the callback must return 1 and not throw). Compare with the monitor list from `Native` test helpers.
- **DpiScope restore.** If later tests in the same process see odd frames after a failing write or read, check
  that the writer thread's awareness context is restored after each item (AC27 in #10 verifies the scope on the
  fact path; here the writer thread wraps each item).
- **Inset DPI rescale.** If AC20-style crossings (AC32 manual) land 1-4 px off, or the settle pass runs every
  time on a per-monitor-aware app, record the app, both DPIs and the TSV rows. The rescale (`toDpi / fromDpi`,
  half away from zero) is only a first guess; the settle pass re-measures on the new monitor.
- **Probe startup failures (AC28/AC29).** The probe needs Windows PowerShell and a visible desktop. If it exits
  early the test says "probe exited early".
