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
| 0 | solution-skeleton | Solution, 4 projects, 3 test projects, Directory.Build.props, empty passing tests | §1, CLAUDE.md | all | — | done |
| 1 | layout-tree | Layout document, nodes (desktop/split/panel), IDs, tree normalisation (flatten same-axis, single-child collapse, weight renormalise) | §2, §2.2 normalize | Core | 0 | done |
| 2 | layout-solver | Tree → rects: weights layout, minimum-aware distribution, range entries, memoisation | §2.1 | Core | 1 | done |
| 3 | docking | Remove panel, insert beside (edge table), insert at seam, dock | §2.2 dock | Core | 1 | done |
| 4 | new-window-placement | placeNew anchor choice + right/bottom rule; dwindle rebuild | §2.2 | Core | 2, 3 | done |
| 5 | directional-focus | Neighbour search + scoring; move-toward effect | §2.3 | Core | 2 | done |
| 6 | park-geometry | Park size and position candidates scored against displays; parked detection | §3.0 | Core | 2 | done |
| 7 | shortcut-model | 12 actions + saved views, configurable bindings, Windows default mapping (AltGr-safe option) | §3.2 | Core | 0 | pending |
| 8 | tuning-constants | `Feel`-equivalent settings record with documented defaults | §3.1 | Core | 0 | done |
| 9 | engine-interfaces | IWindowSystem/IClock/ICapture/IRenderer/ILimitStore/IDisplayStore + value types + fakes for tests (no IHotkeys) | §1, §5 | Engine | 0 | done |
| 10 | win32-window-enum | Enumerate manageable windows, WinEvent hook lifecycle events | §5 | Win32 | 9 | pending |
| 11 | win32-frame-writer | Batched DeferWindowPos, DWM border compensation, read-back + drift correction (refusal detection/retry is Engine policy, see specs/engine-interfaces.md B7) | §4, §5 | Win32 | 9 | pending |
| 12 | win32-hotkeys | RegisterHotKey binding from shortcut-model | §3.2 | Win32 | 7, 9 | pending |
| 13 | engine-tiling-loop | Umbrella — split into #25 engine-projection-targets, #26 engine-frame-writer, #27 engine-window-events (findings/engine-tiling-loop.md) | §2, §6 | Engine | 25, 26, 27 | blocked: split into #25–#27 |
| 14 | win32-parking | Off-screen parking + crash-safe restore journal | §3.0 | Win32 | 6, 11 | pending |
| 15 | camera-projection | World↔screen projection, camera curve, step in/out, history, saved views | §3 | Core/Engine | 27 | pending |
| 16 | overview-thumbnails | Overview/camera motion with DWM thumbnails; land and write real frames | §3 | Win32/Engine | 15 | pending |
| 17 | layout-metrics | Per-node minimum provider for minimum-aware solving: gap, floor/fixed minimum, squeeze, learned per-app minimum, tab-strip allowance, cap at current size | §2.1, findings/layout-solver.md rules 31–34 | Core/Engine | 2, 8, 9 | pending |
| 18 | seam-drag-bounds | Seam drag / live-resize bounds computed with minimum-aware solving | §2.1 | Core | 2, 17 | pending |
| 19 | slot-restore | Insert a panel into a remembered slot; redock a window to its remembered place | §2.2, findings/docking.md | Core | 3 | pending |
| 20 | tab-grouping | Tab/untab windows into a panel, split off a tab, regroup; panel id rules after tab edits | §2.2, findings/layout-tree.md A13 | Core | 1, 3 | pending |
| 21 | drop-hit-testing | Pointer → dock target (panel edge / seam / desktop) during drag | §2.2, findings/docking.md | Core | 2, 3 | pending |
| 22 | floating-panels | Float a window/panel (default rect and size), unfloat, z-order; the float branch of placeNew | §2.2, findings/new-window-placement.md | Core | 1 | pending |
| 23 | engine-inbound-api | Engine construction (engine key = display + desktop id, work area, policy, services); inbound lifecycle/focus/pointer/navigation command methods; outbound callbacks; tick loop; how the Engine receives full display frames for parking. No feature gate, no lessons hook | §1, §5, findings/engine-interfaces.md | Engine | 9 | done |
| 24 | multi-display | Hand-off / carry-in / carry-out between display engines, guests, app-side engine rebuild on display-set change | §1, §5, findings/engine-inbound-api.md | Engine/App | 27, 23 | pending |
| 25 | engine-projection-targets | Tree → screen rects (work area, half-gap insets, square stretch, edge rounding), on-stage (≥99.5%) / fits (minimum floor) classification, per-window target vs park choice (human decision, revised: too-small-on-stage parks at tile scaled up to the window minimum; off-stage parks with no minimum) | §2, §6, findings/engine-projection-targets.md | Core | 2, 6, 8 | done |
| 26 | engine-frame-writer | Unbatched per-window writes, skip unchanged/in-flight, expected-frame echoes (1.5 s, ±2), refusal recheck (>24, 0.25 s) and min/max learning, limit-store observe, drift check, no-animation write path | §6, findings/engine-tiling-loop.md | Engine | 8, 23, 25 | pending |
| 27 | engine-window-events | start, windowAdded/Removed/Focused/Moved effects, snap-back (3 in 3 s), arrangeAll, move(toward:), save triggers. Human decisions: desktop-slot float rule behind EnginePolicy flag default OFF (always tile); nothing framed → move(toward:) starts from focused window's panel (CanvasWM deviation); no docked-tile cap | §2, §6, findings/engine-tiling-loop.md | Engine | 3, 4, 5, 26 | pending |
| 28 | collapsed-groups | Collapse tile groups narrower/shorter than 120 while framed; collapsed panels' rects, parking with pictures | §6, findings/engine-projection-targets.md | Core | 25 | pending |

Open research items (not yet features): `trelliswm://` commands,
scroll-zoom modifier. (Private connection property resolved in `findings/engine-interfaces.md`.) (Park-small factor resolved in `findings/park-geometry.md`.)
