# CanvasWM — project rules for every agent

CanvasWM (working name) is an independent Windows window manager whose
*behaviour* is modelled on a study of TrellisWM for macOS. It is written in
C# on .NET 10. Development and core tests run on a Mac; Windows-specific
code is compiled on the Mac and tested by the human on a Windows PC.

## Clean-room boundary (non-negotiable)

- `research/trelliswm-study.md` and `findings/` describe behaviour in our own
  words. Decompiled output lives outside this repo in
  `~/Documents/trelliswm-re/evidence/` and is read **only by `re-analyst`**.
- `spec-writer`, `developer` and `tester` never open `~/Documents/trelliswm-re/evidence/`
  or any decompiled/pseudocode text. They work from findings and specs only.
- Never copy TrellisWM's name, icons, artwork, lesson content or strings
  (beyond short action descriptions rewritten in our own words).
- The TrellisWM `Licensing` module, license checks, DRM and anti-tamper are
  out of scope. If a task touches them, stop and report.

## Architecture (dependencies point inward)

```
src/CanvasWm.Core      net10.0          pure model + algorithms: layout tree,
                                         solver, docking, placement, navigation,
                                         park geometry, shortcut model. No I/O.
src/CanvasWm.Engine    net10.0          state machine over Core; talks to the OS
                                         only through interfaces (IWindowSystem,
                                         IClock, ICapture, IRenderer, ILimitStore,
                                         IDisplayStore, IWindowSource). Window
                                         facts/events come in via IWindowSource and
                                         an Engine WindowTracker that decides policy
                                         and calls the engine; hotkeys arrive as
                                         plain engine method calls.
src/CanvasWm.Win32     net10.0-windows  implementations of those interfaces via
                                         P/Invoke (user32, dwmapi), incl. the
                                         IWindowSource fact/event gatherer and the
                                         hotkey source. No layout or policy logic.
src/CanvasWm.App       net10.0-windows  executable: composition, tray, settings.
tests/CanvasWm.Core.Tests     net10.0         xUnit, runs on the Mac
tests/CanvasWm.Engine.Tests   net10.0         xUnit with fakes, runs on the Mac
tests/CanvasWm.Win32.Tests    net10.0-windows xUnit, runs only on Windows
```

- Core and Engine must not reference Win32 or any `-windows` TFM.
- Win32 must not contain layout or policy decisions; it adapts the OS.
- Geometry in Core uses device-independent units (`double`), y-down, origin
  top-left of the virtual desktop.
- Mixed-DPI mapping (project-wide): each monitor keeps its physical top-left
  and is scaled by its own DPI only within its bounds; a window uses its
  nearest monitor's DPI. All Win32/App code converts via
  `CanvasWm.Win32.Coordinates.DipSpace` (specs/win32-window-enum.md A8).

## Commands (Mac)

```
dotnet build CanvasWm.slnx
dotnet test tests/CanvasWm.Core.Tests
dotnet test tests/CanvasWm.Engine.Tests
```
Windows-targeted projects build on the Mac via `EnableWindowsTargeting`
(set in `Directory.Build.props`) but cannot run here.

## Pipeline files

- `orchestration/feature-queue.md` — the work queue and each feature's status.
- `findings/<slug>.md` → `specs/<slug>.md` → `src/...` → `tests/...`.
- `windows-verification/<slug>.md` — checklist the human runs on Windows.

## Conventions

- Nullable enabled, warnings as errors, file-scoped namespaces.
- Prefer immutable records and pure static functions in Core.
- One feature per change. Do not refactor unrelated code.
- Do not commit; the human reviews and commits.
