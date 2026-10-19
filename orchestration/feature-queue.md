# Feature queue

## Target
- **Studied app:** `/Applications/TrellisWM.app` 0.9.5 (build 200), binary
  SHA-256 `d9148bb061ac096f438a882d5ca3b496ab3a76f867043cf7fa597213fdbb6304`
- **Purpose / authorization:** Study of the owned windows manager app to understand the interoperabiliy and reliability for contributing to open source code. Never used for commercial or unauthorized purpose
- **Build:** C# / .NET 10, xUnit. Core + Engine tested on the Mac; Win32 on
  the human's Windows PC.
- **Study:** `research/trelliswm-study.md` (section numbers below)

## Features
Statuses: `pending → analyzing → specced → building → testing → awaiting-windows → done`
| `blocked: <reason>` | `parked`

| # | Slug | Description | Study § | Project | Depends on | Status |
|---|------|-------------|---------|---------|------------|--------|
| 0 | solution-skeleton | Solution, 4 projects, 3 test projects, Directory.Build.props, empty passing tests | §1, CLAUDE.md | all | — | awaiting-windows |
| 1 | layout-tree | Layout document, nodes (desktop/split/panel), IDs, tree normalisation (flatten same-axis, single-child collapse, weight renormalise) | §2, §2.2 normalize | Core | 0 | pending |
| 2 | layout-solver | Tree → rects: weights layout, minimum-aware distribution, range entries, memoisation | §2.1 | Core | 1 | pending |
| 3 | docking | Remove panel, insert beside (edge table), insert at seam, dock | §2.2 dock | Core | 1 | pending |
| 4 | new-window-placement | placeNew anchor choice + right/bottom rule; dwindle rebuild | §2.2 | Core | 2, 3 | pending |
| 5 | directional-focus | Neighbour search + scoring; move-toward effect | §2.3 | Core | 2 | pending |
| 6 | park-geometry | Park size and position candidates scored against displays; parked detection | §3.0 | Core | 2 | pending |
| 7 | shortcut-model | 12 actions + saved views, configurable bindings, Windows default mapping (AltGr-safe option) | §3.2 | Core | 0 | pending |
| 8 | tuning-constants | `Feel`-equivalent settings record with documented defaults | §3.1 | Core | 0 | pending |
| 9 | engine-interfaces | IWindowSystem/IHotkeys/IClock/ICapture/IRenderer/IStore + fakes for tests | §1, §5 | Engine | 0 | pending |
| 10 | win32-window-enum | Enumerate manageable windows, WinEvent hook lifecycle events | §5 | Win32 | 9 | pending |
| 11 | win32-frame-writer | Batched DeferWindowPos, DWM border compensation, read-back + drift correction, refusal retries | §4, §5 | Win32 | 9 | pending |
| 12 | win32-hotkeys | RegisterHotKey binding from shortcut-model | §3.2 | Win32 | 7, 9 | pending |
| 13 | engine-tiling-loop | Single monitor: add/remove windows → place → layout → write frames; focus moves | §2, §6 | Engine | 2–6, 9 | pending |
| 14 | win32-parking | Off-screen parking + crash-safe restore journal | §3.0 | Win32 | 6, 11 | pending |
| 15 | camera-projection | World↔screen projection, camera curve, step in/out, history, saved views | §3 | Core/Engine | 13 | pending |
| 16 | overview-thumbnails | Overview/camera motion with DWM thumbnails; land and write real frames | §3 | Win32/Engine | 15 | pending |

Open research items (not yet features): `trelliswm://` commands, the private
connection property, scroll-zoom modifier, park-small factor.
