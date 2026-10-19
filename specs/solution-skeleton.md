# Spec: solution-skeleton

## Goal

Create the empty but buildable CanvasWM solution: one solution file, four
source projects, three test projects, shared build settings, and one trivially
passing test per test project. The skeleton fixes **where code may live** and
**which way dependencies point** (findings rules 1–3, 8). It contains no
runtime behaviour, no engine interfaces, and no layout logic.

## Placement

All projects. No earlier specs exist; this is feature #0 and every later spec
depends on it.

| Project | Path | TFM | Output | Project references (exactly) |
|---|---|---|---|---|
| CanvasWm.Core | `src/CanvasWm.Core/CanvasWm.Core.csproj` | `net10.0` | Library | none |
| CanvasWm.Engine | `src/CanvasWm.Engine/CanvasWm.Engine.csproj` | `net10.0` | Library | Core |
| CanvasWm.Win32 | `src/CanvasWm.Win32/CanvasWm.Win32.csproj` | `net10.0-windows` | Library | Core, Engine |
| CanvasWm.App | `src/CanvasWm.App/CanvasWm.App.csproj` | `net10.0-windows` | WinExe | Core, Engine, Win32 |
| CanvasWm.Core.Tests | `tests/CanvasWm.Core.Tests/CanvasWm.Core.Tests.csproj` | `net10.0` | test | Core |
| CanvasWm.Engine.Tests | `tests/CanvasWm.Engine.Tests/CanvasWm.Engine.Tests.csproj` | `net10.0` | test | Engine, Core |
| CanvasWm.Win32.Tests | `tests/CanvasWm.Win32.Tests/CanvasWm.Win32.Tests.csproj` | `net10.0-windows` | test | Win32, Engine, Core |

Root-level files: `CanvasWm.slnx`, `Directory.Build.props`.

Root namespaces equal assembly names: `CanvasWm.Core`, `CanvasWm.Engine`,
`CanvasWm.Win32`, `CanvasWm.App`, `CanvasWm.Core.Tests`,
`CanvasWm.Engine.Tests`, `CanvasWm.Win32.Tests`.

## Public API

**None.** The skeleton deliberately introduces no public types in Core,
Engine or Win32 (no marker classes, no placeholder interfaces). The only
code files are:

- `src/CanvasWm.App/Program.cs` — an `internal static class Program` with
  `static int Main(string[] args)` that returns `0` immediately (no window,
  no tray, no message loop yet).
- One test file per test project (see B9–B11).

Engine seam interfaces (`IWindowSystem`, `IHotkeys`, `IClock`, `ICapture`,
`IRenderer`, `IStore`) are **out of scope** and belong to feature #9
`engine-interfaces` (see A6, A7).

## Behaviour

### Solution

- **B1.** `CanvasWm.slnx` (XML solution format) at the repo root lists all
  seven projects. Source projects are under a solution folder `/src/`, test
  projects under `/tests/`. No other projects are listed.

### Shared build settings — `Directory.Build.props` (repo root)

- **B2.** Applies to every project and sets exactly these properties:

  | Property | Value |
  |---|---|
  | `Nullable` | `enable` |
  | `TreatWarningsAsErrors` | `true` |
  | `ImplicitUsings` | `enable` |
  | `LangVersion` | `latest` |
  | `EnableWindowsTargeting` | `true` |
  | `Deterministic` | `true` |
  | `GenerateDocumentationFile` | `true` for projects under `src/`, `false` (or unset) for test projects |

  `GenerateDocumentationFile=true` combined with warnings-as-errors means
  public members in `src/` without XML docs fail the build (CS1591); this is
  intended so later specs' XML-doc descriptions are carried into code. The
  App project, which has no public API, is unaffected.
- **B3.** `Directory.Build.props` does **not** set `TargetFramework`; each
  project sets its own TFM per the Placement table.
- **B4.** All C# files use file-scoped namespaces.

### Dependency direction (findings rule 8, Data model table)

- **B5.** Core has no `ProjectReference` and no `PackageReference`
  (BCL only).
- **B6.** Engine has exactly one `ProjectReference`: Core. No package
  references.
- **B7.** Win32 references Core and Engine; App references Core, Engine and
  Win32. Neither Core nor Engine references Win32, App, any `-windows` TFM,
  `UseWindowsForms`, `UseWPF`, or any Windows-only package.
- **B8.** Win32 and App contain no layout or policy code (trivially true for
  the skeleton; stated so reviewers enforce it from the start).

### Test projects

- **B9.** Each test project uses xUnit (v2 line: packages `xunit`,
  `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`; see A3), sets
  `IsPackable=false`, and adds a global `<Using Include="Xunit" />`.
- **B10.** Each test project contains a file `SkeletonTests.cs` with a
  `public sealed class SkeletonTests` containing at least these facts:
  - `ProductAssemblyLoads` — `Assembly.Load("CanvasWm.<X>")` succeeds and the
    loaded assembly's `GetName().Name` equals `"CanvasWm.<X>"`, where `<X>` is
    `Core`, `Engine` or `Win32` for the respective test project.
- **B11.** Architecture guard facts (pure reflection, no file or OS access):
  - In `CanvasWm.Core.Tests`: `CoreReferencesNoCanvasWmAssembly` — the set of
    names from `Assembly.Load("CanvasWm.Core").GetReferencedAssemblies()` that
    start with `"CanvasWm."` is empty.
  - In `CanvasWm.Engine.Tests`: `EngineReferencesOnlyCore` — the set of names
    from `Assembly.Load("CanvasWm.Engine").GetReferencedAssemblies()` that
    start with `"CanvasWm."` is a subset of `{ "CanvasWm.Core" }`.
  - Note: the C# compiler only records references that are actually used, so
    these guards pass vacuously today and become meaningful once code exists.
    They complement, not replace, the csproj checks in AC3–AC5.
- **B12.** `CanvasWm.Win32.Tests` is only treated as a test project when
  building on Windows: its csproj sets
  `<IsTestProject Condition="!$([MSBuild]::IsOSPlatform('Windows'))">false</IsTestProject>`
  so that `dotnet test CanvasWm.slnx` on the Mac skips it instead of failing
  to launch a Windows test host (see A4). It still **builds** on the Mac.

### App

- **B13.** `CanvasWm.App` has `OutputType` `WinExe`, does not yet enable
  `UseWindowsForms` or `UseWPF` (see A5), and its `Main` returns `0`
  without side effects.

## Edge cases & errors

- **E1.** Building on the Mac must not require a Windows SDK or workload;
  `EnableWindowsTargeting=true` (B2) is what allows the two `-windows`
  projects and the Win32 test project to compile.
- **E2.** `dotnet test tests/CanvasWm.Win32.Tests` on the Mac is not expected
  to run tests (no Windows runtime). It is not part of the Mac command set in
  `CLAUDE.md`.
- **E3.** A future accidental reference from Core/Engine to Win32/App must be
  caught: at csproj level by AC3/AC4 review, and at build level because a
  `net10.0` project cannot reference a `net10.0-windows` project (NU1201 /
  incompatible framework error), which fails the build.
- **E4.** No Licensing-related project, folder, namespace or file may exist
  (findings rule 7).
- **E5.** Packages restore from nuget.org; no private feeds.

## Acceptance criteria

Note on [mac] criteria: AC1–AC7 are structural/build checks verified by
running the stated command or inspecting the stated file on the Mac (no OS
window access, no TrellisWM). AC8–AC11 are xUnit tests. This exception to the
"xUnit only" rule applies to this feature because its subject is the build
itself.

- **AC1** [mac] `dotnet build CanvasWm.slnx` on the Mac exits 0 with
  0 warnings and 0 errors, building all seven projects.
- **AC2** [mac] `CanvasWm.slnx` lists exactly the seven projects in the
  Placement table, sources under `/src/`, tests under `/tests/` (B1).
- **AC3** [mac] `CanvasWm.Core.csproj` has `TargetFramework` `net10.0`, no
  `ProjectReference`, no `PackageReference` (B5).
- **AC4** [mac] `CanvasWm.Engine.csproj` has `TargetFramework` `net10.0` and
  exactly one `ProjectReference`, to `CanvasWm.Core.csproj` (B6).
- **AC5** [mac] `CanvasWm.Win32.csproj` targets `net10.0-windows` and
  references exactly Core and Engine; `CanvasWm.App.csproj` targets
  `net10.0-windows`, has `OutputType` `WinExe`, and references exactly Core,
  Engine and Win32 (B7, B13).
- **AC6** [mac] `Directory.Build.props` exists at the repo root and sets the
  properties in B2 with the stated values, and no `TargetFramework` (B3).
- **AC7** [mac] No file in `src/` or `tests/` contains the strings
  `TrellisWM` or `Licensing` (E4), and no project file in the repo contains a
  `-windows` TFM other than Win32, App and Win32.Tests.
- **AC8** [mac] `dotnet test tests/CanvasWm.Core.Tests` reports >= 2 passed,
  0 failed, 0 skipped (`ProductAssemblyLoads`,
  `CoreReferencesNoCanvasWmAssembly`).
- **AC9** [mac] `dotnet test tests/CanvasWm.Engine.Tests` reports >= 2
  passed, 0 failed, 0 skipped (`ProductAssemblyLoads`,
  `EngineReferencesOnlyCore`).
- **AC10** [mac] `dotnet test CanvasWm.slnx` on the Mac exits 0; it runs the
  Core and Engine test projects and does not attempt to run
  `CanvasWm.Win32.Tests` (B12).
- **AC11** [windows] `dotnet test tests/CanvasWm.Win32.Tests` on Windows
  reports >= 1 passed, 0 failed (`ProductAssemblyLoads` for
  `CanvasWm.Win32`).
- **AC12** [windows] `dotnet test CanvasWm.slnx` on Windows exits 0 and runs
  all three test projects.
- **AC13** [windows] Running the built `CanvasWm.App.exe` on Windows exits
  with code 0 promptly (under 5 s) and shows no window, tray icon or console
  (B13).

Count: 10 [mac] (AC1–AC10), 3 [windows] (AC11–AC13).

## Assumptions

- **A1. No `global.json`.** Chosen default: do not pin an SDK; any installed
  .NET 10 SDK that supports `.slnx` is accepted. Why: avoids blocking the
  human's Windows machine on an exact patch version. Revisit if Mac and
  Windows builds diverge.
- **A2. No central package management.** Chosen default: package versions are
  written in each test csproj (latest stable at implementation time, same
  versions in all three). Why: only three projects use packages; a
  `Directory.Packages.props` can be added later in its own change.
- **A3. xUnit v2 rather than xUnit v3.** Chosen default: `xunit` 2.9.x with
  `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` (VSTest), because
  it works with plain `dotnet test` on both OSes with no extra configuration.
  *Needs human review*: xUnit v3 / Microsoft.Testing.Platform is the newer
  direction; switching later is cheap while the suite is tiny.
- **A4. Skipping Win32.Tests on non-Windows via conditional `IsTestProject`.**
  Chosen default per B12. Why: lets `dotnet test CanvasWm.slnx` succeed on the
  Mac. If the SDK in use still tries to run it, the fallback is to drop AC10
  and keep the per-project Mac commands from `CLAUDE.md`; the developer must
  report this rather than silently change it.
- **A5. App UI framework undecided.** Chosen default: `WinExe` with no
  `UseWindowsForms`/`UseWPF`; the tray/settings feature decides between
  WinForms, WPF or raw Win32. *Needs human review* before the first App UI
  feature.
- **A6. Hotkeys as an engine seam (deferred to feature #9).** Findings rule 5
  observes that the studied app keeps hotkeys and the global input tap in the
  app layer, calling the engine through a coordinator, with no engine
  interface for input. CanvasWM's planned `IHotkeys` is a CanvasWM design
  choice; feature #9 must decide whether to keep it in Engine (adapter only
  reports key events, policy stays in Engine/Core) or move input to App.
  Similarly, window lifecycle discovery (findings rule 6) is not an engine
  seam in the studied app; feature #9 decides whether `IWindowSystem` carries
  it as events.
- **A7. One `IStore` vs two stores (deferred to feature #9).** The studied app
  has two separate stores: a per-application minimum-size ("limit") store and
  a per-display saved-layout store. `CLAUDE.md` names a single `IStore`.
  Feature #9 must choose between one interface and two; this spec does not
  change `CLAUDE.md`. Also noted for #9: findings say the engine takes a
  display key, work area, policy and six services per display, and that
  capture on Windows likely maps to DWM thumbnails (handle registration, not
  pixel frames).
- **A8. `GenerateDocumentationFile` on for `src/`.** Chosen default per B2,
  making missing XML docs on public members a build error. Why: specs describe
  APIs at XML-doc level and this enforces it. *Needs human review* if it is
  felt too strict.

## Traceability

| Spec item | Findings rule / source |
|---|---|
| B1, AC1, AC2 | Rule 8 (solution builds); CLAUDE.md architecture & commands |
| B2, B3, B4, AC6 | CLAUDE.md conventions (nullable, warnings as errors, file-scoped namespaces); Windows notes (EnableWindowsTargeting) |
| B5, AC3 | Rule 1, Rule 8, Data model table (Core: BCL only) |
| B6, AC4, B11 (Engine), AC9 | Rule 2, Rule 8 (Engine references Core only) |
| B7, AC5, E3 | Rule 3, Rule 8 (Win32 → Engine+Core; App → all three) |
| B8 | Rule 3; CLAUDE.md "Win32 must not contain layout or policy decisions" |
| B9, B10, AC8, AC9, AC11 | Rule 8 (each test project has a trivially passing test); Worked example "Test check" |
| B11 (Core), AC8 | Rule 1 (model has no OS/other-layer dependency) |
| B12, AC10, E2 | Windows notes (Win32 test project only runs on Windows) |
| B13, AC13 | Rule 3 (App = composition, tray, settings; none implemented yet) |
| E4, AC7 | Rule 7 (Licensing excluded); CLAUDE.md clean-room boundary |
| A6 | Rules 5, 6 |
| A7 | Rule 4, Data model seam table, Windows notes (capture) |
| AC12 | Rule 8; Windows notes |
