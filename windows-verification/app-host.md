# Windows verification: app-host (feature #33)

Spec: `specs/app-host.md` (read "Corrections after the build gate"). Automated tests:
`tests/CanvasWm.Win32.Tests/Host/` (namespace `CanvasWm.Win32.Tests.HostSpec`). They were compiled on the Mac
(0 warnings) and never run there. Nothing below has been run. This is the first runnable build: expect to find
bugs, and use the "If something fails" section at the end before reporting them.

## Safety (read first)

CanvasWM parks windows off-screen while it manages them. If the process dies without running its quit sequence
(`taskkill /F`, a debugger stop, power loss, a crash it cannot catch) the parked windows STAY OFF-SCREEN (A18; the
crash-safe journal is feature #14, not built yet).

**If windows end up off-screen: start CanvasWM again, wait about 3 seconds, then choose Quit CanvasWM from the tray
menu. Quit brings every window back on-screen.** Keep Task Manager open (Ctrl+Shift+Esc) so you can always see and
end the process, and use a throw-away set of windows (Notepad, Calculator, File Explorer) for the first run, not your
real work. Do not start with unsaved documents.

## Prerequisites

- Windows 10 or 11, 64-bit, an interactive desktop session (not locked, not a service).
- .NET 10 SDK (`dotnet --version` prints 10.x). Repo checked out; run all commands from the repo root.
- Close any running CanvasWM before the automated tests: AC20-AC24 look at every `CanvasWm.HostWindow` on the
  desktop, and `AC24_SignalExistingWithoutHost` returns early (counts as a pass, tests nothing) if one is found.
- Do not touch mouse or keyboard during the automated run (windows are created and destroyed).
- Where things live:
  - data folder: `%LOCALAPPDATA%\CanvasWM` (open with `explorer %LOCALAPPDATA%\CanvasWM`)
  - settings: `%LOCALAPPDATA%\CanvasWM\settings.json`, learned minimums: `limits.json`
  - layouts: `%LOCALAPPDATA%\CanvasWM\layouts\arrangement-<64 hex>.json`
  - log: `%LOCALAPPDATA%\CanvasWM\logs\canvaswm.log` (rotated to `canvaswm.1.log` at 1 MiB)
  - tail the log live: `powershell -c "Get-Content $env:LOCALAPPDATA\CanvasWM\logs\canvaswm.log -Wait -Tail 40"`

## Build and run

    dotnet build CanvasWm.slnx -c Release

Exe: `src\CanvasWm.App\bin\Release\net10.0-windows\CanvasWm.App.exe` (double-click from Explorer, or
`start /wait src\CanvasWm.App\bin\Release\net10.0-windows\CanvasWm.App.exe` to see the exit code with
`echo %ERRORLEVEL%`).

Alternatively, from the repo root: `dotnet run --project src/CanvasWm.App -c Release` (the console only shows the
build; the app itself has no console window). The process is then `CanvasWm.App.exe` as well. For tests that kill the
process or read its exit code use the exe directly.

To get a self-contained folder you can copy: `dotnet publish src/CanvasWm.App -c Release -r win-x64 --self-contained false -o publish`
then run `publish\CanvasWm.App.exe`.

## Automated commands

Whole feature (AC20-AC28):

    dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~HostSpec"

Everything, including #10-#12 (their public-surface lists were amended to 15 types):

    dotnet test tests/CanvasWm.Win32.Tests

Single AC (the leading dot stops AC2 matching AC20-AC28):

    dotnet test tests/CanvasWm.Win32.Tests --filter "FullyQualifiedName~HostSpec&FullyQualifiedName~.AC21_"

Add `--logger "console;verbosity=detailed"` to see test output (AC25 prints a line for any display key that is not a
`\\?\` device path).

## Automated acceptance criteria (tick when the test passes)

- [ ] AC20 host window shape: hidden, top-level, tool window, owner thread, two hosts, Dispose rules (HostWindowTests.AC20_*, 3 tests)
- [ ] AC21 Post: not inline, FIFO 1 000, 4 threads x 1 000, self-reposting action does not starve notifications, post inside drain, post after Dispose (HostWindowTests.AC21_*, 5 tests)
- [ ] AC22 errors: throwing action, throwing handler, throwing onError (HostWindowTests.AC22_*, 3 tests)
- [ ] AC23 notification mapping and return values (HostWindowTests.AC23_*, 2 tests)
- [ ] AC24 SignalExisting with and without a host, PumpPending argument check and idle wait (HostWindowTests.AC24_*, 3 tests)
- [ ] AC25 display snapshot sanity (DisplaysAndStorageTests.AC25_SnapshotSanity). Needs an interactive desktop. Note the
      output line if keys are not `\\?\` paths (A9)
- [ ] AC26 `DirectoryTextStore` and round trip through the three stores in a temp folder (DisplaysAndStorageTests.AC26_*, 6 tests)
- [ ] AC27 `DwmFlush`, refresh rate, `HighResolutionTimer` (TickAndSurfaceTests.AC27_*, 3 tests). The 10 ms timer test
      takes the best of five runs; a very busy machine can still fail the 50 ms bound
- [ ] AC28 exactly 15 exported types, no forbidden member types, `DirectoryTextStore : ITextFileStore`
      (TickAndSurfaceTests.AC28_*, 3 tests). Dry run of the first two checks was done on the Mac by loading the built
      assembly with reflection: 15 types, no offenders. Not a Windows pass.

Also tick: the amended #10 AC15, #11 AC12 and #12 public-surface tests pass in the full run.

## Manual smoke tests (AC29-AC42)

Setup for each test unless it says otherwise: no CanvasWM running (check Task Manager, Details tab,
`CanvasWm.App.exe`), three Notepad windows open, `%LOCALAPPDATA%\CanvasWM` may exist. Tick the box only when every
"expect" line holds. Write down anything odd with the log lines around it.

- [ ] **AC29 First run.** Delete `%LOCALAPPDATA%\CanvasWM` first. Start the exe from Explorer.
  - Expect: a tray icon (generic application icon) with tooltip "CanvasWM" within 3 s. No console, no window, no
    taskbar button, not in Alt+Tab.
  - Expect: `settings.json` exists and holds 16 values (15 `hotkeys.*` entries incl. `hotkeys.version`, plus
    `shortcuts.profile` = `WinAlt`).
  - Expect: the log has a `start:` line and a `hotkeys Start: N requested, M active` line.
- [ ] **AC30 Tiling.** With three Notepads open, start CanvasWM.
  - Expect: within 2 s the three are tiled on their display. Open a fourth: it is placed. Close one: the rest re-tile.
- [ ] **AC31 Hotkeys.** Win+Alt+Left / Win+Alt+Right move focus or framing between tiles; Win+Alt+Shift+Up / Down work.
  Put the pointer and focus on an empty desktop area and press Win+Alt+Right.
  - Expect: tiles react to the first group; on the empty desktop nothing visible changes, the app keeps running and
    the log has no `fault:` line.
- [ ] **AC32 Retry shortcuts.** Start another tool that holds one CanvasWM combo (for example an AutoHotkey script:
  `#!n::return` holds Win+Alt+N; use whichever combo the default table binds, see `settings.json`). Then start CanvasWM.
  - Expect: a balloon "Some shortcuts could not be registered (n)..."; the log has `hotkey <combo>: Taken`.
  - Close the other tool, tray menu > Retry shortcuts.
  - Expect: the log shows a new `hotkeys Retry:` line with the combo active, and the combo works.
- [ ] **AC33 Persistence.** Rearrange the tiles (move focus a few times, make a split), tray > Quit CanvasWM, start it
  again.
  - Expect: the arrangement is restored. `layouts\arrangement-<64 hex>.json` exists per display, is readable JSON,
    and its `engineKey` begins with `\\?\` (if it does not, see "DISPLAYCONFIG offsets" below).
- [ ] **AC34 Display changes.** (a) Unplug the second monitor (or disable it in Display settings).
  - Expect: within 3 s every window is on the remaining monitor and visible. Replug: both monitors are managed.
  (b) Change the second monitor's scale (e.g. 150 % to 125 %).
  - Expect: within 3 s tiles fit the new work area, nothing off-screen. Record which notifications appear in the log
    (A15, `WM_DPICHANGED` is unverified; note whether you see `DisplayChanged`, `SettingChanged` or `DpiChanged`
    effects, i.e. router `Log` lines about changed displays).
  (c) Move the taskbar to the left edge.
  - Expect: tiles respect the new work area within 1 s.
  Single-monitor machine: do (c) only and say so.
- [ ] **AC35 Mixed DPI (100 % + 150 %).**
  - Expect: tiles on both monitors align to their work areas, no gap or overlap larger than 2 px. Single-DPI
    machine: mark "not testable".
- [ ] **AC36 Quit restores.** Make at least one window parked (move focus so a tile leaves the visible frame). Tray >
  Quit CanvasWM. Run it with `start /wait` from a command prompt to read the exit code.
  - Expect: within 2.5 s every window is visible on a display; the process is gone from Task Manager; the tray icon
    is gone (no ghost icon after hovering over the tray); `echo %ERRORLEVEL%` prints 0; the log ends with
    `finish: reason User, exit code 0, faults 0` then `exit: code 0`.
- [ ] **AC37 taskkill.** With a parked window: `taskkill /IM CanvasWm.App.exe` (no /F).
  - Expect (A13, unverified): same result as AC36 (windows back, process ends, log `finish: reason User`). If taskkill
    prints "This process can only be terminated forcefully", the hidden tool window is not offered WM_CLOSE: record
    that, then use the PowerShell snippet under "WM_CLOSE" to check whether the host window itself handles it.
  - Then park a window again and run `taskkill /F /IM CanvasWm.App.exe`.
  - Expect: the process ends at once; the parked window STAYS off-screen (documented, #14). Recovery: start CanvasWM
    again (it re-adopts windows that are less than 5 % visible), wait 3 s, tray > Quit CanvasWM. Windows are back.
- [ ] **AC38 Second launch.** With CanvasWM running, start the exe again.
  - Expect: the second process exits in under 1 s with exit code 0 (`start /wait`, `echo %ERRORLEVEL%`); the first
    shows the balloon "CanvasWM is already running."; the log has `second instance signalled this one`; only one
    `CanvasWm.App.exe` is left in Task Manager.
- [ ] **AC39 Sign out / restart.** With a parked window, sign out (or restart).
  - Expect: no "this app is preventing shutdown" screen lasting longer than 3 s. After signing in, run CanvasWM and
    open the log: the previous run's last lines include `finish: reason EndSession`.
- [ ] **AC40 Crash path.** Park a window. (a) `CanvasWm.App.exe --diagnostic-fault=ui` (use `start /wait`).
  - Expect: about 5 s after start the windows are restored, the process exits with code 1, the log holds the
    exception `diagnostic fault (ui)` and `finish: reason Fault, exit code 1`.
  (b) Park a window again, `CanvasWm.App.exe --diagnostic-fault=background`.
  - Expect: windows are restored before the process ends (the exit code is the runtime's crash code, for example
    -532462766), the log holds `unhandled exception (terminating: True)` and `diagnostic fault (background)`.
- [ ] **AC41 Corrupt files.** Quit CanvasWM. Replace one `layouts\arrangement-*.json` and `settings.json` with the
  single line `{"version":1`. Start CanvasWM.
  - Expect: it runs normally; `arrangement-....json.corrupt` and `settings.json.corrupt` exist; `settings.json` is
    rewritten with defaults (16 values); the log names both files (`invalid (...); moved aside`).
- [ ] **AC42 Tick driver.** Trigger a motion that runs frames (a landing after Win+Alt+Right) and read the log.
  - Expect: a `frames off: N ticks in S s, max interval X ms, late L, source Composition` line; the mean interval
    (S / N) within 20 % of the refresh period. Write down the actual refresh rate, the mean interval and `late`.
  - If it says `source Timer`: note it (A10). That means `DwmFlush` returned too fast or failed five times in a row;
    the timer fallback is working as designed, but report it with the display setup.

## If something fails

Check these suspects first. Each is a known risk, not a confirmed bug.

- **`NativeDwmTimingInfo` layout (AC27, AC42).** `DwmComposition.RefreshRate()` reads `DWM_TIMING_INFO` (struct
  fixed with `Pack = 1` after the build gate). If the struct is wrong, `RefreshRate()` returns null (AC27 still
  passes) and the pacer falls back to 60 Hz: on a 144 Hz display AC42 then shows a mean interval near 6.9 ms against
  a nominal 16.7 ms and `late` stays 0. Test: `RefreshRate()` should print your real rate (add a temporary
  `Console.WriteLine` in a test, or compare with Settings > Display > Advanced).
- **DISPLAYCONFIG offsets (AC25, AC33).** Keys should begin with `\\?\DISPLAY#`. If they are `\\.\DISPLAY1` or
  `display-0`, the path/target-name struct offsets in `Win32Displays` (path info 72 bytes, target name path at offset
  164) or the query failed and the fallback ran. Consequence: layouts are not stable across reboots or replugs. AC25
  logs the offending key; also look at the `engineKey` in a layout file.
- **WM_CLOSE reaching the hidden tool window (AC37).** `taskkill` without /F only offers WM_CLOSE to windows it
  considers closable and may refuse a process with no visible top-level window. Check the host directly, without
  taskkill: in PowerShell
  `Add-Type -Name W -Namespace N -MemberDefinition '[DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern System.IntPtr FindWindow(string c,string t); [DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h,uint m,System.UIntPtr w,System.IntPtr l);'; [N.W]::PostMessage([N.W]::FindWindow("CanvasWm.HostWindow",$null),0x10,[UIntPtr]::Zero,[IntPtr]::Zero)`
  If that quits CanvasWM (AC36 result) but taskkill does not, the handler is right and only the delivery path
  differs: record it, and the follow-up is to give the process a visible-enough window or another quit signal.
- **`DwmFlush` with nothing to compose (AC42).** `DwmFlush` may block for a long time when the desktop is idle (no
  composition pass). Symptom: a frames-off line with few ticks and a huge max interval, or windows lagging behind a
  landing animation until you move the mouse. The pacer only switches to the timer on fast or failed flushes, not
  on slow ones.
- **Tray left-click uses reflection (AC29).** `TrayMenu` calls the private `NotifyIcon.ShowContextMenu` by name. If
  left-click does nothing but right-click shows the menu, the reflection lookup failed and the `_menu.Show` fallback
  did not appear either: report it. Right-click is the supported path for this build.
- **16 ms timer granularity versus router/tracker delays (AC30, AC34).** The clock uses a WinForms timer (about
  16 ms). Delays of 0.05 s (tracker rechecks) and the router debounce (1.5 s, 0.8 s) tolerate it. Symptom if not:
  re-tiling after a window event lags by one or two frames, or display changes settle a second late. Compare the log
  timestamps with the actions.
- **NotifyIcon and hotkey windows created before `Application.Run` (AC29, AC31, AC32).** Everything is composed
  before the message loop starts. Symptoms: no tray icon, or the balloon never shows (the first one is posted at
  start), or the first hotkey press after launch is lost. Log lines `hotkeys Start:` appear only once the loop runs.
- **Double Dispose of `Win32WindowSource` / `Win32WindowSystem` (AC36).** `Finish` disposes them and `HostContext.Dispose`
  disposes them again after `Run` returns. If a `finish: ... failed:` line shows up in the log after a quit, or the
  exit code is not 0 after a clean quit, this is the first place to look.
- **Windows stay off-screen after Quit (AC36).** Look for `fault:` and `quit step` lines before the `finish:` line; a
  failed release write leaves a window parked. Relaunch and quit again to recover.
- **Log rotation.** `FileLog` rotates by copy and delete, not rename (the App source scan forbids `.Move(`). If
  `canvaswm.1.log` is missing after a very long session, that is why it is worth a look; it is not critical.
