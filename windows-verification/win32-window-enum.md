# Windows verification: win32-window-enum (feature #10)

## Prerequisites

- Windows 10 version 1703 or later (Windows 11 recommended; AC29 needs it for the new Notepad).
- .NET 10 SDK (with .slnx support), **64-bit** (the interop assumes 64-bit `GetWindowLongPtrW`).
- Run from the repo root in PowerShell, in an **interactive desktop session** (not a service or SSH session).
- Windows PowerShell 5.1 at `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe` (the automated tests launch it as a hidden WinForms probe window).
- Do not touch the mouse or keyboard focus while the automated tests run (focus tests AC22 need the probe to become foreground).
- Two monitors with mixed scaling are only needed for AC28; the rest needs one.

## Part A: automated tests (AC7-AC27)

Run everything for this feature:

```
dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~WindowEnumSpec" --logger "console;verbosity=normal"
```

Expected: `Failed: 0`. The tests that start the probe each take about 2-4 s; the whole run takes roughly 1-2 minutes. Run a single criterion by its id, for example:

```
dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~AC25_"
```

Tip: keep the trailing underscore, because `AC1` alone would also match AC10 to AC19.

Per-class runs (pure helpers need no desktop):

```
dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~WindowEnumSpec.PureHelperTests"
dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~WindowEnumSpec.EventPumpTests"
dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~WindowEnumSpec.RealWindowTests"
dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~WindowEnumSpec.ProbeTests"
```

Assumption-labelled tests: `--filter "Assumption=A10"` (also A3, A7, A11, A18).

Checkboxes (tick when the named tests pass on your machine):

- [x] **AC7** `AC7_HandleConversion_RoundTrips`
- [x] **AC8** `AC8_DipSpace_ScaleFor`, `AC8_DipSpace_ToDip_WorkedExamples`, `AC8_DipSpace_NonOverlap_And_NegativeWidth_And_ZeroDpi`
- [x] **AC9** `AC9_DecodeStyles_TruthTable`, `AC9_IsShellClass`
- [x] **AC10** `AC10_ToCloakKind`, `AC10_ToCloakKind_UnknownBitsMapToApp`, `AC10_MaximizeButtonEnabled`
- [x] **AC11** `AC11_IsFullScreen`
- [x] **AC12** `AC12_NormalizeAppId`, `AC12_AppNameFrom`, `AC12_IsAccessible`
- [x] **AC13** `AC13_HookRanges_AreTheSixRangesInOrder`, `AC13_KindFor_MapsExactlyTwelveIds`, `AC13_Translate_WithFakes`
- [x] **AC14** the six `AC14_*` tests in `EventPumpTests`
- [x] **AC15** `AC15_PublicSurface`
- [x] **AC16** `AC16_Enumerate_MatchesIndependentEnumWindows_AndShellFacts`
- [x] **AC17** `AC17_InProcessWindow_Facts`
- [x] **AC18** `AC18_Describe_EdgeCases`
- [x] **AC19** `AC19_ProbeFacts`
- [x] **AC20** `AC20_ProbeEvents`, `AC20_NoHandlerCallInsideEnumerateOrDescribe`
- [x] **AC21** `AC21_OwnProcessEventsAreSkipped_ProbeStillDelivers`
- [x] **AC22** `AC22_ChildWindowsFiltered_FocusReportsTopLevel`
- [x] **AC23** `AC23_SubscriptionLifecycle`, `AC23_Subscribe_OnLoopThread_NullHandler_Throws`
- [x] **AC24** `AC24_GcSafety_HooksSurviveCollections`
- [x] **AC25** `AC25_HungApp_EnumerateAndDescribeDoNotBlock` (takes about 25 s)
- [x] **AC26** `AC26_FixedSizeWindowFacts` (with `--logger "console;verbosity=detailed"` it prints whether `MaximizeEnabled` was `false` or `null`; both pass; please note which) — observed: `false (disabled)`

Notes on how the automated tests differ from the spec text (report if you disagree):

- The probe window receives its commands (`title X`, `fixed`, `child`, `focus`, `hang`) through small files in a temp directory polled by a 50 ms WinForms timer, not through stdin. A WinForms UI thread cannot block on stdin, and PowerShell has no cheap background reader.
- The in-process windows use the `Static` class on a dedicated message-loop thread created by the test.

## Part B: manual items (AC28-AC33)

The two manual probe tools:

```
$env:CANVASWM_MANUAL = "1"
dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~ManualProbe.DumpFacts" --logger "console;verbosity=detailed"
$env:CANVASWM_PROBE_SECONDS = "30"      # optional, default 30
dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~ManualProbe.RecordEvents" --logger "console;verbosity=detailed"
```

- `DumpFacts` writes `%TEMP%\canvaswm-facts.tsv`: a header row plus one row per window. Columns: Handle, ProcessId, ThreadId, AppId, AppName, Title, FrameX, FrameY, FrameWidth, FrameHeight, IsOwnProcess, IsShellWindow, OwnsDesktop, IsTopLevel, IsChild, IsOwned, IsAppWindow, IsToolWindow, IsNoActivate, HasCaption, IsResizable, HasMaximizeButton, MaximizeEnabled, IsVisible, Cloak, IsMinimized, IsFullScreen, IsHung, IsAccessible. The Frame is flattened into four columns, so the header names the 25 `OsWindowFacts` fields plus the split frame.
- `RecordEvents` starts recording, then you have `CANVASWM_PROBE_SECONDS` seconds to act. It writes `%TEMP%\canvaswm-events.tsv` with Time, Kind, Handle, Title.
- View a file: `Import-Csv $env:TEMP\canvaswm-facts.tsv -Delimiter "`t" | Where-Object Title -like "*Notepad*" | Format-List`
- Reset when finished: `Remove-Item Env:CANVASWM_MANUAL`.

- [x] **AC28** Classic app DIP frame.
  1. Set one monitor to 150 % scaling (Settings, Display).
  2. Open Notepad (or another classic app) on that monitor.
  3. Measure its outer visible border with Snipping Tool, in physical pixels.
  4. Run DumpFacts.

  Expected, Notepad row: `FrameWidth` is about measured width / 1.5 and `FrameHeight` about measured height / 1.5 (within 1 DIP); `FrameX - monitorLeft` is about (physical X - monitorLeft) / 1.5; `AppId` is the lower-cased exe path, `AppName` its file description (for example "Notepad"); `IsFullScreen` is False. With a second monitor at 100 % to its right, a window moved there has `FrameX` at or above that monitor's physical left.
- [x] **AC29** Packaged apps.
  1. Open Calculator.
  2. Run DumpFacts.

  Expected: the Calculator row has `AppId` starting with `microsoft.windowscalculator_`, containing `!`, all lower case; `ProcessId` equals the ApplicationFrameHost.exe pid (Task Manager, Details). On Windows 11 the new Notepad row has `AppId` = `microsoft.windowsnotepad_8wekyb3d8bbwe!app`.
- [x] **AC30** Virtual desktops and cloaking.
  1. Create Desktop 2 (Win+Ctrl+D) and open a Notepad there.
  2. Switch back to Desktop 1 and run DumpFacts.
  3. Start RecordEvents (30 s), switch to Desktop 2 and back (Win+Ctrl+Left/Right).

  Expected: after step 2 that Notepad shows `Cloak` = Shell and `IsVisible` = True; the event log has Uncloaked and Cloaked rows for its handle. A minimised window shows `IsMinimized` = True.
- [x] **AC31** Elevated window.
  1. Run the tests from a non-elevated terminal.
  2. Start `regedit` (accept UAC).
  3. Run DumpFacts.

  Expected: the Registry Editor row has `IsAccessible` = False. Repeat DumpFacts from an elevated terminal: `IsAccessible` = True.
- [x] **AC32** Focus, foreground and full screen. Start RecordEvents, then:
  - Click between two Notepad windows: Foreground rows for each handle in click order, and FocusChanged rows with the same top-level handles.
  - Click the desktop: a Foreground row whose handle is the Progman or WorkerW window (DumpFacts: `IsShellWindow` and `OwnsDesktop` True for it).
  - Press F11 in Edge or Chrome, then run DumpFacts: that window has `IsFullScreen` = True. Leave F11 and maximise it: `IsFullScreen` = False.
  - Minimise, restore and close a Notepad: Minimized, Restored, then Destroyed rows for its handle.

  **Result (2026-10-25): passed after an A18 fix.** Focus, foreground, desktop click and
  minimise/restore/close all behaved as expected. Full screen first failed in one case: Chrome at
  150% scaling, F11 pressed while maximised gives no caption, a rect equal to the monitor
  (0,0-2560x1600), but `WS_MAXIMIZE`/`IsZoomed` still set, so `IsFullScreen` = False because of
  SPEC-ASSUMPTION A18 ("never for a zoomed window"). F11 from a restored window gives `IsZoomed`
  = False and `IsFullScreen` = True. Maximised (not F11) has `WS_CAPTION` + `WS_THICKFRAME`,
  rect -11,-11-2571x1539 (work area plus borders), `IsFullScreen` = False. So "no caption and
  covers the monitor" already rules out a normal maximised window here, without the zoomed check.
  A18/B9 were revised to drop the zoomed check (spec, `FactMapping.IsFullScreen`, AC11 test).
  Re-checked: F11 from maximised now gives `IsFullScreen` = True (`IsZoomed` still true).
- [x] **AC33** Probe tool.
  - With `CANVASWM_MANUAL` unset, `dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~ManualProbe"` passes both tests in under 1 s and writes nothing (delete both TSV files first to check).
  - With it set to 1, both produce the two TSV files above, each with a header row.

## If something fails: least-certain interop points

Report the test name, the message, and which of these applies.

- `GetWindowLongPtrW` (64-bit only): declared for 64-bit; there is no 32-bit `GetWindowLong` fallback. Symptom: style facts (`HasCaption`, `IsResizable`, ...) wrong or an `EntryPointNotFoundException`. Check the SDK and test host are 64-bit (`dotnet --info`).
- `GetApplicationUserModelId` length and error 15703: Symptom: Calculator or packaged Notepad has an exe path as `AppId` instead of an AUMID, or `AppId` is null (AC29). 15703 is `APPMODEL_ERROR_NO_APPLICATION` (a classic app, expected to fall back to the exe path). Note the buffer length handling (130 chars, retry on 122).
- `GetTitleBarInfo` `rgstate[3]` and `cbSize` 44: Symptom: `MaximizeEnabled` always null, or always true for a fixed dialog (AC17, AC26). A wrong struct size makes the call fail, which also gives null.
- `GetSidSubAuthority` pointer reads: Symptom: `IsAccessible` wrong for elevated windows or false for every other-process window (AC19, AC31).
- `DwmGetWindowAttribute` declared twice (RECT and uint overloads): Symptom: `Frame` null or off by a constant, or `Cloak` always None (AC17, AC30).
- `SetWinEventHook` function-pointer callback convention: Symptom: process crash on the first event, or no events at all (AC20-AC24). A crash with no managed stack points here.
- `DPI_AWARENESS_CONTEXT` `(nint)-4`: Symptom: `Frame` wrong on a scaled monitor, or AC27 failing (frame differs under UNAWARE, or context not restored).
- `ApplicationFrameWindow` child walk (`FindWindowExW` for `Windows.UI.Core.CoreWindow`): Symptom: Calculator `AppId` is `applicationframehost.exe`'s path (AC29, spec E4).
- The whole event path (hook install, `EventPump` post hop, owner-thread check): Symptom: AC20-AC24 time out. Say which kinds arrive in `RecordEvents`, whether `ActiveHookCount` is 6 after Subscribe, and whether the probe window appeared at all (a hidden PowerShell that failed to start fails every probe test at start-up with a timeout or "probe exited early").
