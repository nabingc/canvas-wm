# Feature: solution-skeleton

## Summary
TrellisWM is built as three layers of its own code (plus an excluded licensing
module): a platform-free layout model, a platform-free interaction engine that
sits on the model, and a macOS adapter/application layer. The engine never calls
the OS directly; every OS effect goes through a small set of injected interfaces
(window system, capture, clock, renderer, and two stores). CanvasWM's
Core / Engine / Win32 / App split mirrors this one-to-one, with one deliberate
difference: TrellisWM keeps hotkey and global-input handling in the app layer
rather than behind an engine interface. This feature carries no runtime
behaviour; it only fixes where code is allowed to live.

## Behaviour
1. **Layer 1 – model (maps to CanvasWM.Core).** Holds the layout document
   (per-display tree of desktop/split/panel nodes, floating and hidden windows),
   geometry value types (point, size, rect), layout metrics, per-window size
   limits, the tree-to-rectangles solvers, directional neighbour search and
   projection helpers. It has no OS or UI dependency.
2. **Layer 2 – engine (maps to CanvasWM.Engine).** Holds the state machine:
   the per-display engine object, static layout edits ("arrangement"), the scene
   and presenter (what to show), camera and layout tweens, drop/dock resolution,
   move/resize/maximize sessions, the tuning-constant table and the user policy
   (preview style, tab style, etc.). It depends on layer 1 only, plus the
   interfaces in rule 4.
3. **Layer 3 – platform glue and app (maps to CanvasWM.Win32 + CanvasWM.App).**
   Holds the coordinator that owns all displays and one engine per display,
   the concrete window-system adapter, accessibility helpers, the window tracker
   (discovers/admits/forgets windows, reports focus), per-app observers, a
   global event tap, hotkey handling, live window capture, wallpaper capture,
   the stage renderer (overlay drawing: strips, handles, groups, overview),
   settings window, menu-bar UI, and the concrete stores. CanvasWM splits this
   into Win32 (pure OS adapters) and App (composition, tray, settings).
4. **Engine seams.** One engine instance is constructed per display with:
   a display key, the display's visible (work) area, the policy, and six
   injected services:
   - *Window system* – focus a window; enumerate windows front-to-back; ask
     whether a window is showing and what fraction is visible; read a window's
     frame and write a new frame (both asynchronous with a completion that
     returns the actual resulting frame); find the native tab sibling of a
     window; expose current pointer position, whether the mouse button is down,
     and a "parking origin" (the place off-view windows are moved to).
   - *Capture source* – start/stop live capture of a window, set its capture
     rate (frames per second), hold/pin a frame, and fetch the latest frame
     (a frame carries a size and a serial number).
   - *Clock* – current time and "run this after a delay" scheduling.
   - *Renderer* – a single "render this scene" call; the renderer owns all
     overlay drawing.
   - *Limit store* – per-application learned minimum window size: look up,
     learn a minimum, and observe an actual size (keyed by application id).
   - *Display store* – save and load a per-display saved layout (keyed by
     display); the concrete version persists JSON.
5. **Input is not an engine seam.** Hotkeys and the global mouse/keyboard tap
   live in the app layer and call engine methods (activate, move toward, begin
   gesture, scroll, etc.) through the coordinator. CanvasWM's planned `IHotkeys`
   interface is therefore a CanvasWM design choice, not a TrellisWM mirror; it
   is compatible as long as policy (what a key does) stays in Engine/Core and
   the adapter only reports key events.
6. **Window lifecycle discovery is not an engine seam either.** The tracker in
   the app layer turns OS notifications into engine calls (window appeared,
   app activated, app resized, window removed). CanvasWM's `IWindowSystem`
   may carry this as events, but the engine must stay unaware of how the OS
   delivers them.
7. **Licensing module** exists as a separate module with its own store and API
   interfaces; it is out of scope and must not appear in CanvasWM.
8. **Skeleton acceptance.** For this feature: the solution builds, Core and
   Engine target `net10.0` and reference nothing Windows-specific, Win32 and
   App target `net10.0-windows`, Engine references Core only, Win32 references
   Engine+Core, App references all three, and each test project contains at
   least one trivially passing test.

## Data model
- No runtime data. Structural mapping only:

| TrellisWM layer | CanvasWM project | Allowed dependencies |
|---|---|---|
| model | CanvasWm.Core | none (BCL only) |
| engine | CanvasWm.Engine | Core |
| platform adapters | CanvasWm.Win32 | Core, Engine |
| coordinator, UI, composition | CanvasWm.App | Core, Engine, Win32 |

| TrellisWM seam | CanvasWM interface | Notes |
|---|---|---|
| window system | IWindowSystem | async-style frame write returning actual frame |
| capture source | ICapture | per-window start/stop/rate/latest/hold |
| clock | IClock | now + delayed callback |
| renderer | IRenderer | single render(scene) entry |
| limit store + display store | IStore (or two interfaces) | TrellisWM keeps them separate |
| (none – app layer) | IHotkeys | CanvasWM addition, see rule 5 |

## Worked examples
- Build check: `dotnet build CanvasWm.slnx` succeeds on the Mac with no
  warnings (warnings are errors).
- Dependency check: CanvasWm.Core.csproj and CanvasWm.Engine.csproj contain
  no `-windows` TFM and no reference to CanvasWm.Win32 or CanvasWm.App.
- Test check: `dotnet test tests/CanvasWm.Core.Tests` and
  `dotnet test tests/CanvasWm.Engine.Tests` each report >= 1 passed, 0 failed.
- Engine construction shape (for a later feature): constructing an engine
  requires a display key, a work-area rect, a policy and the six services;
  a test can supply fakes for all six with no OS present.

## Evidence
- Rules 1–3, 7: `research/trelliswm-study.md` §1 (module table: SpacesModel,
  SpacesEngine, TrellisWM, Licensing).
- Rule 4 constructor shape: study §1; symbol
  `Engine.init(displayKey:visibleArea:policy:system:capture:clock:renderer:limits:store:)`
  (`symbols/spaces-api.txt` line 74).
- Rule 4 seam protocols and their requirements: new evidence
  `~/Documents/trelliswm-re/evidence/engine-seam-protocols.json` — protocol
  descriptors in module SpacesEngine for `WindowSystem` (0x100226d90),
  `CaptureSource` (0x100226e20), `Clock` (0x100226e6c), `LimitStore`
  (0x100226ea0), `DisplayStore` (0x100226edc), `Renderer` (0x100226f2c); protocol
  witnesses in conformances `MacWindowSystem`, `MacCapture`, `MacClock`,
  `AppLimitStore`, `JSONDisplayStore`, `StageRenderer`.
- Rule 5: `EventTap`, `Hotkeys` are classes in module TrellisWM with no
  SpacesEngine protocol (only six non-licensing protocols exist in
  SpacesEngine; app-layer protocols are `DeskDecoration`,
  `WindowTrackerDelegate`).
- Rule 6: `WindowTracker.*` methods (admit, forget, rescan, reportFocus) and
  `Engine.appActivated`, `Engine.appResized` in `symbols/all-defined.demangled.txt`.
- Rule 8: `CLAUDE.md` architecture section.

## Confidence
| Rule | Label |
|---|---|
| 1 Model layer contents | Observed |
| 2 Engine layer contents | Observed |
| 3 Glue/app layer contents | Observed |
| 4 Seam list and method names | Observed (semantics of each method Inferred from names) |
| 5 Input outside engine seams | Inferred (absence of a protocol observed) |
| 6 Lifecycle via tracker → engine calls | Inferred |
| 7 Licensing excluded | Observed |
| 8 Skeleton acceptance | Inferred (project rule, not TrellisWM behaviour) |

## Unknowns
- Exact parameter/return types of each seam method (e.g. what "visible
  fraction" is measured against, what "hold" pins, how "parking origin" is
  chosen). Not needed for the skeleton; resolve per feature.
- How the coordinator routes calls when one window spans or moves between
  displays (one engine per display). Belongs to the multi-monitor feature.
- Purpose of the private `CGSSetConnectionProperty` call (study §7); may imply
  an extra OS seam, unknown which layer uses it.
- Whether the `Policy` value is mutable at runtime or the engine is rebuilt on
  settings change.

## Windows notes
- HWND is the window identity; no separate element-to-id lookup is needed.
- Frame writes on Windows are naturally batched (DeferWindowPos); keep the
  seam's "write then report actual frame" contract so the engine can learn
  minimum sizes (limit store) the same way.
- Capture maps best to DWM thumbnails, which are drawn by DWM rather than
  delivered as pixel frames; ICapture may need to expose a handle/registration
  rather than a frame buffer. Keep that difference inside Win32/App.
- Clock must marshal delayed callbacks onto the engine's thread (UI thread or
  a dedicated dispatcher), since Win32 hooks and WinEvents arrive on the
  hook-owning thread.
- Win32 projects build on the Mac only via EnableWindowsTargeting and cannot
  be run there; the Win32 test project only runs on Windows.
