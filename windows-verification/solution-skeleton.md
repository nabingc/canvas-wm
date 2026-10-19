# Windows verification: solution-skeleton

Prerequisite: .NET 10 SDK (with .slnx support) installed. Run from the repo root in PowerShell.

- [x] **AC11** Win32 test project runs and passes.
  ```
  dotnet test tests/CanvasWm.Win32.Tests
  ```
  Expected: exit code 0; `Passed! - Failed: 0, Passed: >= 1` (`ProductAssemblyLoads`).

- [x] **AC12** Whole solution tests run on Windows.
  ```
  dotnet test CanvasWm.slnx; echo "exit=$LASTEXITCODE"
  ```
  Expected: `exit=0`; three test assemblies run (CanvasWm.Core.Tests, CanvasWm.Engine.Tests, CanvasWm.Win32.Tests), each with 0 failed. Core and Engine show 2 passed each, Win32 at least 1.

- [x] **AC13** App exits immediately with no UI.
  ```
  dotnet build src/CanvasWm.App
  $sw = [Diagnostics.Stopwatch]::StartNew()
  $p = Start-Process src\CanvasWm.App\bin\Debug\net10.0-windows\CanvasWm.App.exe -PassThru -Wait
  $sw.Stop(); "exit=$($p.ExitCode) seconds=$($sw.Elapsed.TotalSeconds)"
  ```
  Expected: `exit=0`, seconds under 5. Visually confirm: no window, no tray icon, no console window appeared.
