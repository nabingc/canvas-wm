# Spec: engine-inbound-api

## Goal

Build the **Engine shell**: the one object per (display, virtual desktop)
that the app layer constructs, gives its display context, wires callbacks to,
and then drives with plain method calls on the UI thread. This feature fixes
the shape of that object and the parts of its behaviour that hold no matter
what tiling, camera or multi-display logic is added later:

- construction (engine key, work area, policy, services bundle) and the
  display context set after construction;
- the user-facing `EnginePolicy` record with its observed defaults, and which
  policy changes cause a relayout versus a redraw;
- the outbound callbacks and their synchronous, non-re-entrant contract,
  including edge-triggered `OnNeedsFrames`;
- the lifecycle state machine (`Start` / `Suspend` / `Resume` / `ReleaseAll` /
  `Release` / `Save`) with its guards and idempotence;
- window events at the contract level (suspended guard, duplicate add =
  refresh, unknown remove = no-op, parked-window restore on removal, the
  "away" table);
- the `ReleaseAll` cascade and its single completion;
- the `Tick(now)` time contract, `NeedsFrames`, and the `VisibleAreaChanged`
  equality guard;
- named, empty stubs for navigation, pointer and gesture commands.

Out of scope (approved split; each later feature fills the named hook):

| Later feature | Fills |
|---|---|
| #13 engine-tiling-loop | what `Start`, `WindowAdded`, `WindowRemoved`, `WindowFocused`, `WindowMoved` do to the layout and to real window frames; layout sanitising on start; "start when captured"; live dock-spot computation; `ArrangeAll`, `FloatAll`, `Move`, `ToggleDocked`, `SplitForNewWindow` (and its 1 s expiry in `AppActivated`), `ToggleMaximize`, native drag tracking; the live `SavedDisplay` contents; `Holds` for layout views |
| #14 win32-parking | when a window becomes parked (sets the parked flag) |
| #15 camera-projection | the `Tick` body (camera curve, layout tween, move session), the real `NeedsFrames` conditions, step/zoom/history/saved views, desktop node commands, scroll/pinch/rect zoom |
| #16 overview-thumbnails | overview, landing and fading, `CaptureArrived` landing logic, capture-rate lowering/restoring on suspend/resume, cover re-check after frames stop |
| #20 tab-grouping | tab strip height/lead, tab press/drag, group closing/minimising |
| #24 multi-display | hand-off, carry-in/carry-out, guests, the "leaving" set, display-set rebuild in App |

Excluded entirely (not modelled, not even as stubs): the studied app's
feature gate and its onboarding/lessons hook.

## Placement

- Project: `src/CanvasWm.Engine` (`net10.0`). Still exactly one
  `ProjectReference` (Core) and no `PackageReference`.
- One build change: `CanvasWm.Engine.csproj` gains
  `<InternalsVisibleTo Include="CanvasWm.Engine.Tests" />` (an `ItemGroup`
  item, not a reference) so the internal extension points below can be
  driven by tests (A3).
- Namespace `CanvasWm.Engine` (new types):
  `DisplayEngine`, `EngineServices`, `EnginePolicy`, `PreviewStyle`,
  `TabStyle`, `ProxyScale`, `ResizeCover`, `StagePhase`, `GesturePhase`,
  `ManagedWindow`, `ReleaseGeometry`.
- Suggested files: `src/CanvasWm.Engine/{DisplayEngine.cs,
  DisplayEngine.Commands.cs (partial, stubs only), EngineServices.cs,
  EnginePolicy.cs, StagePhase.cs, GesturePhase.cs, ManagedWindow.cs,
  ReleaseGeometry.cs}`.
- Tests: `tests/CanvasWm.Engine.Tests/Engine/*Tests.cs`, using the existing
  fakes in `tests/CanvasWm.Engine.Tests/Fakes/` unchanged
  (`FakeWindowSystem`, `FakeCapture`, `ManualClock`, `InMemoryLimitStore`,
  `InMemoryDisplayStore`, `RecordingRenderer`).
- Reuses, unchanged: from `specs/engine-interfaces.md` — `EngineKey`,
  `DisplayKey`, `WindowKey`, `WindowInfo`, `DockSpot`, `SavedDisplay`, the six
  service interfaces, `RenderPlan`, `WindowPlan`; from Core —
  `CanvasWm.Core.Geometry.Rect`, `Size`, `Point`; `CanvasWm.Core.Layout.Edge`,
  `LayoutDocument`; `CanvasWm.Core.Parking.ParkDisplay`.
  **No existing type in Core or Engine is changed.**
- Coordinates: DIPs, y-down, origin top-left of the virtual desktop.
- No text in `src/` or `tests/` may contain the words `TrellisWM` or
  `Licensing`.

## Public API

All public members carry XML docs. A reference parameter not marked nullable
throws `ArgumentNullException` when null (before any state change).

### Services bundle

```csharp
namespace CanvasWm.Engine;

/// <summary>The six outbound services one engine uses. Several engines may share the same
/// instances (window system, capture, clock, stores are per-app; the renderer is per display).</summary>
public sealed record EngineServices
{
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public EngineServices(IWindowSystem windows, ICapture capture, IClock clock,
                          IRenderer renderer, ILimitStore limits, IDisplayStore displays);
    public IWindowSystem Windows { get; }
    public ICapture Capture { get; }
    public IClock Clock { get; }
    public IRenderer Renderer { get; }
    public ILimitStore Limits { get; }
    public IDisplayStore Displays { get; }
}
```

### Policy (user-facing settings)

```csharp
/// <summary>User-facing behaviour settings of one engine. Immutable; change with `with` and assign
/// to DisplayEngine.Policy. Separate from the internal tuning constants of feature #8. Values are
/// not validated here; consumers clamp what they read.</summary>
public sealed record EnginePolicy
{
    /// <summary>A policy with every default below.</summary>
    public static EnginePolicy Default { get; }

    // --- layout (relayout triggers: Gap, FixedMinimum, TabPadding) ---
    /// <summary>Space between tiles, DIPs. Default 10. Relayout trigger.</summary>
    public double Gap { get; init; } = 10;
    /// <summary>Use a fixed minimum tile size instead of learned minimums. Default false. Relayout trigger.</summary>
    public bool FixedMinimum { get; init; } = false;
    /// <summary>Allow tiles below their minimum when space runs out. Default false.</summary>
    public bool SqueezeBelowMinimum { get; init; } = false;
    /// <summary>When a tile is removed, share its space with all siblings (true) or give it to the
    /// neighbour (false). Default true. Passed to Core edit calls (A9).</summary>
    public bool ShareRemovedSpace { get; init; } = true;
    /// <summary>A new window that cannot go anywhere else floats onto the empty desktop area
    /// when that area is visible and large enough, instead of being tiled. Default false.
    /// Added by #27 (H1).</summary>
    public bool FloatNewWindowsOnDesktop { get; init; } = false;
    // MaxDockedTiles: removed per human decision (licensing-related tile cap of the studied
    // app; CanvasWM has no tile cap). See specs/engine-window-events.md C12.
    /// <summary>Collapse very small groups. Default true.</summary>
    public bool CollapseSmallGroups { get; init; } = true;
    /// <summary>A single floating window takes the desktop. Default true.</summary>
    public bool SoleFloatTakesDesktop { get; init; } = true;
    /// <summary>Park out-of-view windows at the small park size. Default false.</summary>
    public bool ParkOutOfViewSmall { get; init; } = false;

    // --- tabs ---
    /// <summary>Tab groups enabled. Default true.</summary>
    public bool TabsAllowed { get; init; } = true;
    /// <summary>Tab strip style. Default TabStyle.Standard (raw 0).</summary>
    public TabStyle TabStyle { get; init; } = TabStyle.Standard;
    /// <summary>Tab tint strength. Default 0.</summary>
    public double TabTint { get; init; } = 0;
    /// <summary>Tab padding, part of the tab-strip height, DIPs. Default 4. Relayout trigger.</summary>
    public double TabPadding { get; init; } = 4;
    /// <summary>Show close buttons on tabs. Default true.</summary>
    public bool TabCloseButtons { get; init; } = true;
    /// <summary>Close button on the right of a tab. Default false (A6).</summary>
    public bool TabCloseOnRight { get; init; } = true;
    /// <summary>Tab inset, DIPs. Default 0.</summary>
    public double TabInset { get; init; } = 0;
    /// <summary>Tuck tab strips. Default false.</summary>
    public bool TuckStrips { get; init; } = false;

    // --- motion and gestures ---
    /// <summary>Gestures commit real window frames. Default false.</summary>
    public bool GesturesLand { get; init; } = false;
    /// <summary>Touchpad two-finger pinch/pan drives the camera. Default true.</summary>
    public bool TouchPinchPan { get; init; } = true;
    /// <summary>A new motion may interrupt a landing. Default false.</summary>
    public bool InterruptLandings { get; init; } = false;
    /// <summary>Keep pushed windows where they were pushed. Default false.</summary>
    public bool KeepPushed { get; init; } = false;

    // --- dragging and docking ---
    /// <summary>Drags show previews. Default false.</summary>
    public bool PreviewDrags { get; init; } = false;
    /// <summary>Gap handles are shown at rest. Default true.</summary>
    public bool DeskGapHandles { get; init; } = true;
    /// <summary>Hover time before a drop target engages, seconds. Default 0.15.</summary>
    public double DropHold { get; init; } = 0.15;
    /// <summary>Reflow other tiles live while dragging. Default false.</summary>
    public bool ReflowWhileDragging { get; init; } = false;

    // --- views and overview ---
    /// <summary>Saved views (framings 1-9) enabled. Default true.</summary>
    public bool SavedViewsAllowed { get; init; } = true;
    /// <summary>Overview presentation variant. Default false.</summary>
    public bool OverviewSurfaced { get; init; } = false;
    /// <summary>Desktop edge fraction. Default 0.2.</summary>
    public double DesktopEdge { get; init; } = 0.2;

    // --- overlay look (redraw only) ---
    /// <summary>Picture look. Default PreviewStyle.Standard (raw 1).</summary>
    public PreviewStyle PreviewStyle { get; init; } = PreviewStyle.Standard;
    /// <summary>Default 0.72.</summary>
    public double TintOpacity { get; init; } = 0.72;
    /// <summary>Default 0.14.</summary>
    public double CoverFade { get; init; } = 0.14;
    /// <summary>Show the app icon on window covers. Default true.</summary>
    public bool CoverIcon { get; init; } = true;
    /// <summary>Corner radius of drawn windows, DIPs. Default 10 (A6).</summary>
    public double WindowCornerRadius { get; init; } = 8;
    /// <summary>Stand-in picture scale. Default ProxyScale.Medium (raw 1).</summary>
    public ProxyScale ProxyScale { get; init; } = ProxyScale.Medium;
    /// <summary>What covers a window while it is resized. Default ResizeCover.Picture (raw 1).</summary>
    public ResizeCover ResizeCover { get; init; } = ResizeCover.Picture;
    /// <summary>Background blur radius, DIPs. Default 12. Advisory on Windows (A6).</summary>
    public double BlurRadius { get; init; } = 12;
    /// <summary>Dim tiled windows that are not focused. Default true.</summary>
    public bool DimDocked { get; init; } = true;
    /// <summary>Default 0.25.</summary>
    public double DimOpacity { get; init; } = 0.25;
    /// <summary>Default 16. Advisory on Windows (A6).</summary>
    public double DimBlur { get; init; } = 16;

    /// <summary>True when the two policies differ in Gap, FixedMinimum or TabPadding (B29).</summary>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static bool RequiresRelayout(EnginePolicy before, EnginePolicy after);
}

// Enum case names are CanvasWM's own (A5). Raw values are fixed; the default's raw value matches
// the observed default.
public enum PreviewStyle { Plain = 0, Standard = 1, Detailed = 2 }
public enum TabStyle     { Standard = 0, Compact = 1 }
public enum ProxyScale   { Small = 0, Medium = 1, Large = 2 }
public enum ResizeCover  { None = 0, Picture = 1, Blur = 2 }
```

Equality is the compiler-generated record equality (all instance members are
value types or nullable value types). **38 instance properties**: the
observed 39 fields minus the dropped `tabLights` (A6) and minus
`MaxDockedTiles` (removed per human decision, licensing-related; #27 C12),
plus `FloatNewWindowsOnDesktop` (#27 C1). Derived values
(tab-strip height, tab lead) are not part of this record (#20).

### Small types

```csharp
/// <summary>What the stage is doing. Raw values fixed.</summary>
public enum StagePhase { Desk = 0, Moving = 1, Overview = 2, Landing = 3, Fading = 4 }

/// <summary>Phase of a continuous pointer gesture as translated by the app.</summary>
public enum GesturePhase { Began = 0, Changed = 1, Ended = 2, Cancelled = 3 }

/// <summary>Read-only snapshot of one managed window.</summary>
/// <param name="Info">Latest reported info. Info.Frame is the last known frame.</param>
/// <param name="Parked">True while the engine keeps the window parked off the visible stage.</param>
/// <param name="RememberedSpot">Dock spot recovered from the away table on re-add, or null.</param>
public sealed record ManagedWindow(WindowInfo Info, bool Parked, DockSpot? RememberedSpot);
// Info null → ArgumentNullException (init accessor validates too).

/// <summary>Where released windows go (B13, B19, B43). Pure.</summary>
public static class ReleaseGeometry
{
    public const double MaxAreaFraction = 0.7;
    public const double MinWidth = 640;
    public const double MinHeight = 420;
    public const double OffsetX = 60;
    public const double OffsetY = 40;
    public const double Step = 32;
    public const int Cycle = 8;

    /// <summary>Cascade slot `index` (0-based) for a window of `size` released into `area`.</summary>
    /// <exception cref="ArgumentOutOfRangeException">index &lt; 0.</exception>
    public static Rect Cascade(Rect area, Size size, int index);

    /// <summary>`size` centred in `area` (no clamping).</summary>
    public static Rect Centre(Rect area, Size size);
}
```

### The engine

```csharp
namespace CanvasWm.Engine;

/// <summary>The window manager for one (display, virtual desktop). Single-threaded: every member
/// is called on the UI thread, and every callback and service completion arrives on it.</summary>
public sealed partial class DisplayEngine
{
    /// <summary>Creates an idle engine (not started, not suspended, phase Desk). Makes no service
    /// call other than reading Clock.Now. Does not load the saved layout.</summary>
    /// <exception cref="ArgumentNullException">key, policy or services is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">visibleArea invalid (B4).</exception>
    public DisplayEngine(EngineKey key, Rect visibleArea, EnginePolicy policy, EngineServices services);

    // ---------- identity and display context ----------
    public EngineKey Key { get; }
    /// <summary>Key.Display.</summary>
    public DisplayKey DisplayKey { get; }
    public EngineServices Services { get; }
    /// <summary>The display's work area.</summary>
    public Rect VisibleArea { get; }
    /// <summary>Full frame of this engine's display; null until the app sets it.</summary>
    public Rect? DisplayFrame { get; set; }
    /// <summary>Full frames of every other display (own display excluded). Copied on set.
    /// Default empty.</summary>
    /// <exception cref="ArgumentNullException">On set with null.</exception>
    public IReadOnlyList<Rect> OtherDisplays { get; set; }
    /// <summary>y of the bottom edge of a system bar along the top of this display, or null when
    /// the platform has none (always null on Windows).</summary>
    public double? TopBarBottom { get; set; }
    /// <summary>Park context from VisibleArea, DisplayFrame, OtherDisplays and
    /// Services.Windows.ParkingOrigin, evaluated now.</summary>
    public ParkDisplay ParkContext();

    // ---------- policy ----------
    /// <summary>Current policy. Setting it applies B31-B33.</summary>
    /// <exception cref="ArgumentNullException">On set with null.</exception>
    public EnginePolicy Policy { get; set; }

    // ---------- outbound callbacks (all optional) ----------
    /// <summary>State shown in menus changed (B35).</summary>
    public Action? OnChange { get; set; }
    /// <summary>The frames-running flag changed; true = start calling Tick each display refresh,
    /// false = stop. Fired only on transitions (B37).</summary>
    public Action<bool>? OnNeedsFrames { get; set; }
    /// <summary>A window was moved to another display (dragged = moved by a pointer drag). Raised by
    /// #24 only.</summary>
    public Action<ManagedWindow, bool>? OnHandOff { get; set; }
    /// <summary>Asks whether another display accepts a window being dragged out; null handler =
    /// false. Queried by #24 only.</summary>
    public Func<WindowKey, bool>? OnCarryOut { get; set; }
    /// <summary>Diagnostic text. No content contract.</summary>
    public Action<string>? Log { get; set; }

    // ---------- inspectors (never throw, never call services or callbacks) ----------
    /// <summary>True after Start until ReleaseAll ("adopted").</summary>
    public bool IsStarted { get; }
    /// <summary>True after Suspend until Resume ("frozen").</summary>
    public bool IsSuspended { get; }
    public StagePhase Phase { get; }
    public WindowKey? FocusedKey { get; }
    /// <summary>Managed windows in registration order (first add).</summary>
    public IReadOnlyList<ManagedWindow> Windows { get; }
    /// <summary>Snapshot for key, or null if not managed.</summary>
    public ManagedWindow? Window(WindowKey key);
    /// <summary>True if key is managed. (#13 extends to views held by the layout document.)</summary>
    public bool Holds(WindowKey key);
    /// <summary>Temporarily removed windows and their remembered spots (value may be null).</summary>
    public IReadOnlyDictionary<WindowKey, DockSpot?> Away { get; }
    /// <summary>Process id of the frontmost app as last reported, or null before any report.</summary>
    public int? ActiveProcessId { get; }
    /// <summary>The frontmost app owns the desktop (Explorer desktop on Windows).</summary>
    public bool DesktopOwnerActive { get; }
    /// <summary>The frames-running flag (B37).</summary>
    public bool FramesRunning { get; }
    /// <summary>Whether anything still needs per-frame ticks (B40).</summary>
    public bool NeedsFrames { get; }
    /// <summary>Human-readable dump; contains Key.Value. No format contract.</summary>
    public string DebugState { get; }

    // ---------- lifecycle ----------
    public void Start(IReadOnlyList<WindowInfo> windows);
    public void Suspend();
    public void Resume(IReadOnlyList<WindowInfo> windows);
    /// <param name="into">Area to release windows into; null = VisibleArea.</param>
    /// <param name="completion">Runs once after every release write has reported back.</param>
    public void ReleaseAll(Rect? into, Action? completion = null);
    public void Release(WindowKey key);
    public void Save();

    // ---------- window events ----------
    public void WindowAdded(WindowInfo info);
    public void WindowRemoved(WindowKey key, bool temporary);
    public void WindowFocused(WindowKey key);
    public void WindowMoved(WindowKey key, Rect frame);
    public void WindowTitleChanged(WindowKey key, string title);
    public void WindowBecameDockable(WindowKey key);
    public void CaptureArrived(WindowKey key);
    public void AppActivated(int processId, bool ownsDesktop);
    public void VisibleAreaChanged(Rect visibleArea);

    // ---------- frames ----------
    /// <summary>now: absolute seconds on the same time base as IClock.Now (not a delta).</summary>
    public void Tick(double now);
    /// <summary>Re-render (e.g. after a settings change).</summary>
    public void Redraw();
}
```

### Command stubs (`DisplayEngine.Commands.cs`)

Each stub is public, has a one-line XML doc naming its meaning and owning
feature, and in #23 **does nothing** (B44). 33 methods.

```csharp
public sealed partial class DisplayEngine
{
    // #13 tiling loop (8)
    /// <summary>Focus and frame the neighbour on that side. (#13)</summary>
    public void Move(Edge toward);
    /// <summary>Rebuild the tiling from all windows. (#13)</summary>
    public void ArrangeAll();
    /// <summary>Float every window. (#13)</summary>
    public void FloatAll();
    /// <summary>Toggle one window between floating and tiled. (#13)</summary>
    public void ToggleDocked(WindowKey key);
    /// <summary>Reserve a split next to key for the next new window. (#13)</summary>
    public void SplitForNewWindow(WindowKey key);
    /// <summary>Frame one window alone, or undo; null = focused window. (#13)</summary>
    public void ToggleMaximize(WindowKey? key);
    /// <summary>The pointer is dragging a native window at the given point. (#13)</summary>
    public void NativePointerDragged(Point at);
    /// <summary>The mouse button was released after a native window drag. (#13)</summary>
    public void NativeMouseUp(Point at);

    // #15 camera (23)
    /// <summary>Frame the whole layout. (#15)</summary>
    public void ShowEverything();
    /// <summary>Move the camera one tree level up. (#15)</summary>
    public void StepOut();
    /// <summary>One level down toward a point. (#15)</summary>
    public void StepIn(Point toward);
    /// <summary>One level down into a node. (#15)</summary>
    public void StepInto(string nodeId);
    /// <summary>Camera history back. (#15)</summary>
    public void Back();
    /// <summary>Camera history forward. (#15)</summary>
    public void Forward();
    /// <summary>Zoom in toward a point (null = centre). (#15)</summary>
    public void ZoomIn(Point? toward);
    /// <summary>Zoom out one step. (#15)</summary>
    public void ZoomOut();
    /// <summary>Zoom into a node. (#15)</summary>
    public void ZoomInto(string nodeId);
    /// <summary>Go to saved view 1-9. (#15)</summary>
    public void GoToFraming(int slot);
    /// <summary>Save the current view in slot 1-9. (#15)</summary>
    public void SaveFraming(int slot);
    /// <summary>Forget saved view 1-9. (#15)</summary>
    public void ForgetFraming(int slot);
    /// <summary>Frame the desktop node. (#15)</summary>
    public void ShowDesktop();
    /// <summary>Show or hide the desktop node. (#15)</summary>
    public void SetDesktopHidden(bool hidden);
    /// <summary>Toggle framing the desktop node. (#15)</summary>
    public void ToggleDesktop();
    /// <summary>Toggle desktop node visibility. (#15)</summary>
    public void ToggleDesktopHidden();
    /// <summary>Scroll input (wheel or touchpad). (#15)</summary>
    public void Scroll(double dx, double dy, bool precise, bool zoomModifier, Point at,
                       GesturePhase phase, bool momentumEnded);
    /// <summary>Pinch input; alternate = the alternate modifier is held. (#15)</summary>
    public void Pinch(double by, Point at, GesturePhase phase, bool alternate, bool overWallpaper);
    /// <summary>Touch pinch-and-pan combined step. (#15)</summary>
    public void TouchPinchPan(double zoom, Point around, double dx, double dy);
    /// <summary>A touch gesture ended. (#15)</summary>
    public void TouchGestureEnded();
    /// <summary>Begin a rectangle zoom. (#15)</summary>
    public void BeginRectZoom(Point at);
    /// <summary>Move the rectangle-zoom corner. (#15)</summary>
    public void MoveRectZoom(Point to);
    /// <summary>Finish the rectangle zoom. (#15)</summary>
    public void EndRectZoom(Point at);

    // #16 overview (2)
    /// <summary>Open or close the overview of everything. (#16)</summary>
    public void ToggleOverview();
    /// <summary>Open the overview from a touch gesture. (#16)</summary>
    public void OpenOverviewByTouch();
}
```

Not stubbed here (their owning features add them with real signatures):
overview pointer and keyboard input, preview press/drag/resize, tab
press/drag and tab-docking (#16, #20); gap drag (#18); group closing /
minimising, tab leaving (#20); carry-in, hand-over drag, left-display (#24).

### Internal extension points (visible to `CanvasWm.Engine.Tests`)

```csharp
public sealed partial class DisplayEngine
{
    /// <summary>Incremented by ReleaseAll and Suspend; later features' pending frame-write
    /// sequences compare it and abandon themselves when it changed.</summary>
    internal int PlacementGeneration { get; }
    /// <summary>Number of re-frames requested (B34). #13/#15 replace the body, not the count.</summary>
    internal int ReframeCount { get; }
    /// <summary>Time base of the last tick (B37-B38).</summary>
    internal double LastTick { get; }
    /// <summary>dt computed by the most recent Tick; 0 before any tick.</summary>
    internal double LastTickDelta { get; }
    /// <summary>Stand-in for the motion conditions of NeedsFrames (#15 replaces).</summary>
    internal bool MotionActive { get; set; }
    /// <summary>Phase setter for later features and tests. No render, no callback.</summary>
    internal void SetPhase(StagePhase phase);
    /// <summary>Sets/clears the parked flag of a managed window; unknown key: nothing. (#14)</summary>
    internal void SetParked(WindowKey key, bool parked);
    /// <summary>Sets a managed record's RememberedSpot; unknown key: nothing. (#13 replaces with the
    /// live dock-spot computation.)</summary>
    internal void SetRememberedSpot(WindowKey key, DockSpot? spot);
    /// <summary>Turn the frames-running flag on/off (edge-triggered, B37).</summary>
    internal void SetFramesRunning(bool on);
}
```

## Behaviour

### Construction and display context (findings rules 1–4)

- **B1.** The constructor stores `key`, `visibleArea`, `policy`, `services`;
  sets `IsStarted = false`, `IsSuspended = false`, `Phase = Desk`,
  `FocusedKey = null`, no windows, empty `Away`, `ActiveProcessId = null`,
  `DesktopOwnerActive = false`, `FramesRunning = false`, `MotionActive =
  false`, `DisplayFrame = null`, `OtherDisplays` empty, `TopBarBottom = null`,
  `LastTick = Services.Clock.Now`, `LastTickDelta = 0`, counters 0. It makes
  no other service call (no load, no render, no capture) and invokes no
  callback.
- **B2.** `DisplayFrame`, `OtherDisplays`, `TopBarBottom` are plain settable
  properties. The app sets them once, right after construction, before
  wiring callbacks and before the first `AppActivated`. Setting them has no
  side effect (no render, no re-frame, no callback). `OtherDisplays` copies
  the given list into an immutable snapshot. Display-set changes are not
  pushed into a live engine: the app calls `ReleaseAll` and builds new
  engines (findings rule 3; #24).
- **B3.** `ParkContext()` returns `new ParkDisplay(VisibleArea, DisplayFrame,
  OtherDisplays, Services.Windows.ParkingOrigin)` evaluated at call time.
- **B4 (rect validation).** A visible area (constructor or
  `VisibleAreaChanged`) or a `ReleaseAll` `into` rect with any non-finite
  component, or negative width or height, throws
  `ArgumentOutOfRangeException` and changes nothing. Zero width/height is
  accepted.

### Threading and callbacks (rules 7–9, 24)

- **B5.** All members are called on one UI thread; the engine has no locks
  and does not check the thread (A10).
- **B6 (delivery).** Notification callbacks (`OnChange`, `OnNeedsFrames`,
  `OnHandOff`, `Log`) are invoked **synchronously before the inbound method
  returns**, after that method's state changes are complete, in the order
  the events occurred, with `OnChange` (if any) last. `OnCarryOut` is a query
  and is invoked at the point of need (#24). A null handler is skipped
  (`OnCarryOut` null = `false`).
- **B7 (no re-entry).** While any callback of an engine is running, every
  mutating public member of **that** engine (all lifecycle, window-event,
  frame, command-stub and `Redraw` methods, and the setters of `Policy`,
  `DisplayFrame`, `OtherDisplays`, `TopBarBottom` and the callback
  properties) throws `InvalidOperationException` and changes nothing.
  Inspectors remain usable. Calls into **other** engines are allowed.
- **B8.** An exception thrown by a callback propagates to the caller of the
  inbound method; callbacks queued after it in the same call are not
  delivered. The state changes of that call are already complete (B6). The
  re-entry guard is released even when a callback throws.
- **B9.** Service completions (`ReadFrame`, `SetFrame`, `SetFrames`, `After`)
  arrive later per engine-interfaces B5/B19; the engine never relies on them
  running inside the requesting call.

### Lifecycle (rules 10–15)

- **B10 (Start).** `Start(windows)`: validate (null list or element →
  `ArgumentNullException`, nothing changed). If `IsStarted` → nothing at
  all. Otherwise, in order: register every window as in B17b (duplicate keys
  in the list: the later info wins); load `Services.Displays.Load(Key)` and
  keep it as the engine's saved record (`new
  SavedDisplay(LayoutDocument.Empty)` when null); [#13: sanitise, place
  unsaved windows, ensure desktop node, start-when-captured]; set `IsStarted
  = true`; render (B42); save (B16). `Start` does not check `IsSuspended`.
  No periodic tab watcher is scheduled (A8).
- **B11 (Suspend).** If `IsSuspended` → nothing. Otherwise: [#13/#15/#16:
  cancel interactions, leave overview, clear hover]; `PlacementGeneration++`;
  `MotionActive = false`; `Phase = Desk`; `IsSuspended = true`; save (B16);
  render; `SetFramesRunning(false)`. It makes no `SetFrame`/`SetFrames`
  call. [#16: lower capture rates.]
- **B12 (Resume).** Validate as B10. If not `IsSuspended` → nothing.
  Otherwise: `IsSuspended = false`; then, with `L` = keys in `windows`:
  1. each managed key not in `L` whose record is **not parked** is removed
     as by `WindowRemoved(key, temporary: false)`; parked records are kept;
  2. each info in `windows` (list order) is processed as `WindowAdded(info)`
     (new → appended; managed → info refreshed);
  3. one re-frame (B34). [#16: restore capture rates; landing.]
- **B13 (ReleaseAll).** Validate `into` (B4). In order:
  1. `PlacementGeneration++`.
  2. `area = into ?? VisibleArea`.
  3. Selected windows, in registration order: every managed record with
     `Parked == true` **or** `Services.Windows.VisibleFraction(Info.Frame) <
     0.5` (A2). For the i-th selected window (i from 0), issue
     `Services.Windows.SetFrame(key, ReleaseGeometry.Cascade(area,
     new Size(Info.Frame.Width, Info.Frame.Height), i), cb)`. Non-selected
     windows get no write.
  4. For every managed key (registration order): `Services.Capture.Stop(key)`.
  5. Clear the managed table, `Away`, `FocusedKey` and the pid →
     last-focused map; `IsStarted = false`; `Phase = Desk`; `MotionActive =
     false`; render `RenderPlan.Empty`; `SetFramesRunning(false)`.
     `IsSuspended` is unchanged. No save.
  6. Completion: with `n` selected windows, each write's completion
     (whatever its result, including null) decrements a counter; when it
     reaches 0, `completion` runs exactly once. If `n == 0`, `completion` is
     scheduled with `Services.Clock.After(0, …)` (never run inside
     `ReleaseAll`, A4). A null `completion` is allowed.
  `ReleaseAll` applies whether or not the engine is started or suspended,
  and may be called again; each call has its own completion.
- **B14 (Release).** `Release(key)`: unknown key → nothing. If the record is
  parked: write it back on-screen as in B19d and clear the flag. Then remove
  it as `WindowRemoved(key, temporary: false)` **ignoring the suspended
  guard** (A7). Being unparked by then, B19d writes nothing further.
- **B15.** After `ReleaseAll`, `Start` works again (`IsStarted` is false).
- **B16 (Save).** If `Start` has never run on this engine → nothing (A11).
  Otherwise `Services.Displays.Save(Key, savedRecord)`. In #23 the saved
  record is the one loaded by `Start` (or the empty record); #13 makes it
  reflect the live layout. `Save` works while suspended and after
  `ReleaseAll`.

### Window events (rules 16–21)

- **B17 (WindowAdded).** `WindowAdded(info)`:
  a. `IsSuspended` → ignored.
  b. Register: if `info.Key` is managed, replace the record's `Info` with
     `info` (keeping `Parked`, `RememberedSpot` and the position in
     `Windows`); otherwise append `(info, Parked: false, RememberedSpot:
     null)` and call `Services.Capture.Start(key)`. Then, if `Away` contains
     the key, remove it and set the record's `RememberedSpot` to the removed
     value.
  c. Not started → nothing more (no save, no render).
  d. Started → [#13: end interactions, place, write frames]; save; render.
- **B18.** A duplicate add never creates a second record and never calls
  `Capture.Start` again (worked example 1).
- **B19 (WindowRemoved).** `WindowRemoved(key, temporary)`:
  a. `IsSuspended` → ignored.
  b. Unknown key → nothing (no service call, no save, no render, no
     callback). [#24: guest/leaving set clean-up.]
  c. Started: [#13: end interactions]; if `temporary`, `Away[key] =` the
     record's `RememberedSpot` (possibly null) [#13 computes the live spot];
     [#13: remove from layout].
  d. The record is removed. If it was parked:
     `Services.Windows.SetFrame(key, ReleaseGeometry.Centre(VisibleArea,
     frame size of Info.Frame), _ => { })` (fire-and-forget).
  e. `Services.Capture.Stop(key)`; if `FocusedKey == key` it becomes null;
     pid → last-focused entries whose value is `key` are removed.
  f. Started → save; render. Not started → nothing more.
- **B20 (WindowFocused).** Suspended or unknown key → ignored. Otherwise
  `FocusedKey = key`; last-focused[record's `Info.ProcessId`] = key;
  [#13/#15: tab, raise, camera]; render.
- **B21 (WindowMoved).** Suspended or unknown key → ignored. Otherwise the
  record's `Info` becomes `Info with { Frame = frame }`; [#13: absorb own
  writes or treat as a user move]. No render, no save in #23.
- **B22 (WindowTitleChanged).** Works while suspended. Null title →
  `ArgumentNullException`. Unknown key → nothing. Otherwise `Info with {
  Title = title }`; render only if `Phase != Desk` and not suspended.
- **B23 (WindowBecameDockable).** Works while suspended. Unknown → nothing.
  Otherwise `Info with { Dockable = true }`. No render, no relayout.
- **B24 (CaptureArrived).** Unknown key → nothing. Otherwise render iff
  `Phase != Desk` and not suspended. [#16: advance landing.]
- **B25 (AppActivated).** Always (even suspended) records
  `ActiveProcessId = processId` and `DesktopOwnerActive = ownsDesktop`. Let
  `E(pid)` = last-focused[pid], or null when `pid` is null or has no entry.
  If not suspended and (`E(new pid) != E(previous ActiveProcessId)` or
  `ownsDesktop != previous DesktopOwnerActive`) → render. Otherwise no
  render. [#13: expire a split-for-new-window request older than 1 s.]
- **B26 (VisibleAreaChanged).** Validate (B4). Equal to `VisibleArea`
  (exact `Rect` equality) → nothing. Otherwise store it; if started and not
  suspended → one re-frame (B34).

### Parked flag

- **B27.** `Parked` is false on creation and changes only through
  `SetParked` (#14), `Release` (cleared) and removal. A refresh (B17b) keeps
  it.

### Policy (rules 22–23)

- **B28.** `EnginePolicy.Default` and `new EnginePolicy()` are equal and
  have exactly the defaults in the Public API.
- **B29.** `RequiresRelayout(a, b)` is true iff `a.Gap != b.Gap` or
  `a.FixedMinimum != b.FixedMinimum` or `a.TabPadding != b.TabPadding`
  (plain `!=`).
- **B30.** The policy record is never mutated; consumers read
  `DisplayEngine.Policy` at the time of use.
- **B31 (set).** Setting `Policy` stores the new value. Then:
  - new equals old (record equality), or not started, or suspended →
    nothing else (A12);
  - else if `RequiresRelayout(old, new)` → one re-frame (B34);
  - else → render only (`ReframeCount` unchanged).
- **B32.** In #23 a relayout re-frame is counted also in overview phase;
  whether real windows are rewritten there is decided by #13/#16.
- **B33.** `ShareRemovedSpace` is not mirrored into any global; the engine
  passes `Policy.ShareRemovedSpace` to the Core edit functions that take it
  (A9).
- **B34 (re-frame).** In #23 a re-frame is: `ReframeCount++`, then render.
  #13/#15 add target computation and frame writes.

### OnChange (rule 24)

- **B35.** At the end of each mutating public call (after other callbacks,
  B6), if any of `IsStarted`, `IsSuspended`, `Phase`, `FocusedKey`,
  `Policy`, or the set of managed keys differs from its value at the start
  of the call, `OnChange` fires exactly once; otherwise it does not fire
  (A13). Internal extension points never fire it.

### Frames and tick (rules 25–28)

- **B36.** `FramesRunning` changes only through `SetFramesRunning`.
- **B37 (edge trigger).** `SetFramesRunning(on)`: if `on == FramesRunning`
  → nothing. Turning on: `LastTick = Services.Clock.Now`, `FramesRunning =
  true`, then `OnNeedsFrames(true)`. Turning off: `FramesRunning = false`,
  then `OnNeedsFrames(false)`. Inside a public call the callback is queued
  per B6; when called directly (tests, later features' completions) it is
  delivered before `SetFramesRunning` returns, under the B7 guard. [#16:
  schedule the cover re-check after off.]
- **B38 (Tick).** `Tick(now)`: `now` NaN or ±∞ →
  `ArgumentOutOfRangeException`. Suspended → nothing (A14). Otherwise:
  `LastTickDelta = max(0, now − LastTick)`; `LastTick = now`; [#15/#16:
  step camera by dt, end the layout tween after 0.46 s, move session,
  landings, overview shapes]; render; if `FramesRunning` and not
  `NeedsFrames` → `SetFramesRunning(false)`.
- **B39.** `now` is absolute seconds on the `IClock.Now` time base, never a
  delta. The app's frame driver passes `Services.Clock.Now` (or an equal
  time base) once per display refresh while frames run.
- **B40.** `NeedsFrames` in #23 = `MotionActive`. #15/#16 extend it with:
  phase Moving or Landing, layout tween running, camera not at target, move
  session or continuous gesture active.
- **B41.** `Suspend` and `ReleaseAll` turn frames off (B11, B13), so
  `OnNeedsFrames(false)` fires there only if frames were running.

### Rendering in the shell

- **B42.** "Render" means one `Services.Renderer.Render(plan)` call. In #23
  the plan is `new RenderPlan(Windows.Select(w => new WindowPlan(w.Info.Key,
  w.Info.Frame, w.Info.Key == FocusedKey)))` (registration order), except
  `ReleaseAll`, which renders `RenderPlan.Empty`. Overlay features replace
  the plan contents (A15). `Redraw()` renders once in every state.

### Release geometry

- **B43.** `ReleaseGeometry.Cascade(area, size, i)`, with `k = Step × (i mod
  Cycle)`: `width = min(MaxAreaFraction × area.Width, max(size.Width,
  MinWidth))`, `height = min(MaxAreaFraction × area.Height, max(size.Height,
  MinHeight))`, result `(area.X + OffsetX + k, area.Y + OffsetY + k, width,
  height)`. `Centre(area, size) = (area.X + (area.Width − size.Width) / 2,
  area.Y + (area.Height − size.Height) / 2, size.Width, size.Height)`.

### Command stubs

- **B44.** Every method in `DisplayEngine.Commands.cs` returns immediately:
  no state change, no service call, no callback, no argument validation, no
  exception — except the re-entry guard of B7, which applies to them too.

## Edge cases & errors

- **E1.** Null arguments (`Start`/`Resume` list or element, `WindowAdded`
  info, any non-nullable `WindowKey`, `WindowTitleChanged` title, `Policy` /
  `OtherDisplays` setter value, constructor args) → `ArgumentNullException`,
  no state change. Stub parameters are not validated (B44); `into` of
  `ReleaseAll` may be null.
- **E2.** A `ReleaseAll` write completing with `null` (window vanished)
  still counts toward the completion.
- **E3.** A window larger than the area: `Centre` yields a negative offset;
  `Cascade` limits the size to 0.7 × area.
- **E4.** `WindowRemoved(key, true)` before `Start`: the record is removed,
  nothing goes into `Away` (B19c applies only when started).
- **E5.** A key in `Away` re-added while suspended: ignored (B17a); `Away`
  keeps it.
- **E6.** `Tick` with `now < LastTick`: `LastTickDelta = 0`, `LastTick =
  now`.
- **E7.** A `ReleaseAll` completion arriving after the app discarded the
  engine still runs (the engine holds no state that blocks it); the app
  ignores late completions.

## Acceptance criteria

All [mac]: xUnit tests in `tests/CanvasWm.Engine.Tests` using the existing
fakes, no OS access. Default rig: `ManualClock(100)`, `FakeWindowSystem`
with `Screens = [(0,0,1440,900)]`, `FakeCapture`, `RecordingRenderer`,
`InMemoryLimitStore`, `InMemoryDisplayStore`, key `new EngineKey(new
DisplayKey("D1"), "default")`, visible area `VA = (0,25,1440,875)`,
`EnginePolicy.Default`. Windows `wN` are `WindowInfo` values with key "wN";
on-screen frames unless stated. Rect equality exact, except rects computed
by scaling (e.g. the B43 cascade's `0.7 × area`), which compare per
component within `1e-9` (IEEE rounding: `0.7 × 1440` is not exactly 1008);
times within `1e-9`.
"No service calls" means: fake `Writes`, `Reads`, `FocusRequests`,
`PendingCount` unchanged, renderer `Plans` unchanged, `SaveLog` unchanged,
`ManualClock.PendingCount` unchanged, no capture start/stop.

### Construction, context, policy record
- **AC1** [mac] After construction: `IsStarted` false, `IsSuspended` false,
  `Phase == Desk`, `Windows` empty, `FramesRunning` false, `NeedsFrames`
  false, `LastTick == 100`, `DisplayFrame` null, `OtherDisplays` empty,
  `TopBarBottom` null, `DisplayKey == Key.Display`; no service calls; no
  callback. Null `key`/`policy`/`services` → `ArgumentNullException`; a
  visible area with NaN X or width −1 → `ArgumentOutOfRangeException`;
  `EngineServices` with any null argument → `ArgumentNullException`. (B1,
  B4)
- **AC2** [mac] Setting `DisplayFrame`, `OtherDisplays` (from a `List<Rect>`
  cleared afterwards) and `TopBarBottom` → values read back, `OtherDisplays`
  keeps the original elements, no render, no callback. `OtherDisplays =
  null` → `ArgumentNullException`. `ParkContext()` has `VisibleArea`,
  `DisplayFrame`, `OtherDisplays` as set and `FallbackOrigin ==
  FakeWindowSystem.ParkingOrigin`. (B2, B3)
- **AC3** [mac] `EnginePolicy.Default` equals `new EnginePolicy()`; every one
  of the 38 properties has the listed default (one assertion each; raw
  values `(int)PreviewStyle == 1`, `(int)TabStyle == 0`, `(int)ProxyScale ==
  1`, `(int)ResizeCover == 1`). Reflection: exactly 38 public instance
  properties (excluding the compiler's `EqualityContract`), none named
  `TabLights` or `MaxDockedTiles`; `FloatNewWindowsOnDesktop == false`
  (updated by #27 C12). (B28, A6)
- **AC4** [mac] `RequiresRelayout`: true for a change of `Gap` 10→12, of
  `FixedMinimum`, of `TabPadding`; false for changes of `TabTint`,
  `DimOpacity`, `ShareRemovedSpace` alone and for equal policies. (B29)

### Callbacks
- **AC5** [mac] Re-entry: started engine with frames running; an
  `OnNeedsFrames` handler that calls `Suspend()`, `WindowAdded(w9)`,
  `Tick(101)`, `ToggleOverview()` and sets `Policy` on the same engine sees
  `InvalidOperationException` each time and, inside the handler,
  `IsSuspended` is true and `FramesRunning` false; the handler is triggered
  by `Suspend()`. A call from the handler into a second engine succeeds.
  After `Suspend` returns, the first engine accepts calls again (e.g.
  `Resume([])` works). (B6, B7)
- **AC6** [mac] Ordering: started engine with frames running; `Suspend()`
  delivers `OnNeedsFrames(false)` then `OnChange()`, exactly one each, in
  that order. If the `OnNeedsFrames` handler throws, the exception reaches
  the caller of `Suspend`, `IsSuspended` is true, `OnChange` was not called,
  and a following `Resume([])` does not throw `InvalidOperationException`.
  (B6, B8, B35)
- **AC7** [mac] An engine with no callbacks set runs the scenarios of
  AC8–AC17 without exceptions. (B6)

### Lifecycle
- **AC8** [mac] `Start([w1, w2])`: `IsStarted` true; `Windows` keys `[w1,
  w2]`; `FakeCapture.IsStarted` true for both; exactly one render whose plan
  lists w1, w2 at their frames, none focused; `OnChange` once. With a
  pre-saved record `R` under `Key` in the `InMemoryDisplayStore`, the last
  `SaveLog` entry is `(Key, R)`; with none, it equals `(Key, new
  SavedDisplay(LayoutDocument.Empty))`. (B10, B16, B42)
- **AC9** [mac] A second `Start([w3])` makes no service call, fires no
  callback, and w3 is not managed (worked example 7). `Start([w1 "A", w1
  "B"])` keeps one record with title "B". `Start([])` sets `IsStarted`.
  `Start(null)` and a list with a null element → `ArgumentNullException`,
  `IsStarted` still false, no service calls. (B10)
- **AC10** [mac] Started engine, `MotionActive = true`,
  `SetFramesRunning(true)`: `Suspend()` → `IsSuspended` true, `Phase ==
  Desk`, `MotionActive` false, `PlacementGeneration` +1, exactly one save,
  one render, `OnNeedsFrames(false)` once, `Writes` and `PendingCount`
  unchanged. A second `Suspend()` → no service calls, no callbacks,
  generation unchanged (worked example 4). Suspending with frames not
  running → `OnNeedsFrames` never fires. (B11, B41)
- **AC11** [mac] `Resume([w1])` on a not-suspended engine → no service
  calls, no callbacks, w1 not added (worked example 5). On a suspended
  engine, `Resume` with a null element → `ArgumentNullException` and still
  suspended. (B12)
- **AC12** [mac] Worked example 6: started with `[w1 "A", w2, w4]`,
  `SetParked(w4, true)`; `Suspend()`; `Resume([w1 "B", w3])` → managed keys
  `[w1, w4, w3]` in that order; w2 capture stopped; w4 still parked and no
  write issued for it; w3 capture started; `Window(w1).Info.Title == "B"`;
  `ReframeCount` +1; `IsSuspended` false. (B12)
- **AC13** [mac] Worked example 9: started with w1 `(-3000,0,500,300)`, w2
  `(5000,0,1400,900)`, w3 `(0,2000,900,700)` and on-screen w4
  `(100,100,800,600)`; `SetParked(w1, true)`; `ReleaseAll(null, done)` →
  new `Writes` are exactly `[(w1,(60,65,640,420)), (w2,(92,97,1008,612.5)),
  (w3,(124,129,900,612.5))]`, none for w4; capture stopped for all four;
  `Windows` and `Away` empty; `FocusedKey` null; `IsStarted` false; `Phase
  == Desk`; renderer `Last` is `RenderPlan.Empty`; `PlacementGeneration` +1;
  no new save. `done` has not run; after two `Step()` calls still not; after
  the third it has run exactly once; a further `Pump()` and
  `Advance(1)` do not run it again. (B13, B43)
- **AC14** [mac] Selection threshold and target area: a non-parked window at
  `(1040,0,800,600)` (visible fraction exactly 0.5) gets no release write;
  one at `(1041,0,800,600)` does. A parked 300×200 window with
  `ReleaseAll(into: (2000,0,1000,800))` is written to `(2060,40,640,420)`.
  With no managed windows, `done` does not run during the call and runs once
  on `ManualClock.Advance(0)`. Removing a selected window from the fake
  before `Pump` (result null) still lets `done` run once. `ReleaseAll(into:
  (0,0,NaN,10))` → `ArgumentOutOfRangeException`, engine unchanged. (B13,
  E2, A2, A4)
- **AC15** [mac] `ReleaseGeometry`: `Cascade(VA, s, 8) == Cascade(VA, s, 0)`
  and slot 9 equals slot 1; `Cascade(VA, s, −1)` →
  `ArgumentOutOfRangeException`. `Centre(VA, 800×600) ==
  (320,162.5,800,600)`; `Centre((0,0,100,100), 200×50) == (−50,25,200,50)`.
  (B43, E3)
- **AC16** [mac] `Release`: unknown key → no service calls. Parked w1
  (frame size 800×600) → exactly one `SetFrame(w1, (320,162.5,800,600))`, w1
  no longer managed, capture stopped; non-parked w2 → no write, removed.
  `Release(w3)` on a suspended engine still removes w3. After `ReleaseAll`,
  `Start([w5])` works (`IsStarted` true, w5 managed). (B14, B15, A7)
- **AC17** [mac] `Save()` before any `Start` → `SaveLog` unchanged. After
  `Start`, `Save()` appends `(Key, record)`; it also appends while suspended
  and after `ReleaseAll`. (B16, A11)

### Window events
- **AC18** [mac] Worked example 1: started; `WindowAdded(w1 "A")`;
  `SetParked(w1, true)`; `WindowAdded(w1 "B")` → one record, title "B",
  still parked, same index in `Windows`; `FakeCapture.RecaptureRequests(w1)`
  did not grow from the second add. (B17, B18, B27)
- **AC19** [mac] Before `Start`, `WindowAdded(w1)` records w1 and starts its
  capture, with no save and no render. After `Start`, `WindowAdded(w2)`
  causes exactly one save and one render. (B17c, B17d)
- **AC20** [mac] Worked example 2: `WindowRemoved(w9, false)` for an unknown
  key on a started engine → no service calls, no callbacks. (B19b)
- **AC21** [mac] Worked example 3: started with w1; `Suspend()`; then
  `WindowAdded(w2)`, `WindowRemoved(w1, false)`, `WindowFocused(w1)`,
  `WindowMoved(w1, (1,2,3,4))` → `Windows` is still just w1 with its
  original frame, `FocusedKey` unchanged, no service calls, no callbacks.
  `AppActivated(42, false)` → `ActiveProcessId == 42`, no render.
  `WindowTitleChanged(w1, "T")` and `WindowBecameDockable(w1)` while
  suspended update the record (title "T", `Dockable` true) with no render.
  (B17a, B19a, B20–B23, B25)
- **AC22** [mac] Worked example 8: started, w1 `(5000,0,800,600)`,
  `SetParked(w1, true)`; `WindowRemoved(w1, false)` → exactly one new write
  `(w1, (320,162.5,800,600))`, capture stopped, w1 not managed, one save,
  one render; `Pump()` completes the write with no further render, save or
  write. Removing a non-parked window makes no write. (B19d–f)
- **AC23** [mac] Away table, started engine: `WindowRemoved(w1, true)` →
  `Away` has w1 with a null value; `WindowAdded(w1)` → `Away` empty, record
  `RememberedSpot` null. With `SetRememberedSpot(w2, S)` (S a `DockSpot`),
  `WindowRemoved(w2, true)` → `Away[w2] == S`; `WindowAdded(w2)` → `Away`
  empty and `Window(w2).RememberedSpot == S`. `WindowRemoved(w3, false)`
  never adds to `Away`. Before `Start`, `WindowRemoved(w4, true)` leaves
  `Away` empty. While suspended, `WindowAdded(w1)` for a key in `Away`
  leaves `Away` unchanged. (B17b, B19c, E4, E5)
- **AC24** [mac] Focus: started with w1 (pid 7), w2 (pid 8);
  `WindowFocused(w2)` → `FocusedKey == w2`, one render whose plan marks only
  w2 focused, `OnChange` once; `WindowFocused(w9)` (unknown) → nothing;
  `WindowRemoved(w2, false)` → `FocusedKey` null. (B20, B19e, B42)
- **AC25** [mac] `WindowMoved(w1, (10,20,300,200))` on a started engine →
  `Window(w1).Info.Frame == (10,20,300,200)`, no render, no save, no write.
  (B21)
- **AC26** [mac] `WindowTitleChanged(w1, "X")` at `Phase == Desk` → stored,
  no render; after `SetPhase(Overview)` → stored and one render.
  `WindowTitleChanged(w1, null)` → `ArgumentNullException`.
  `CaptureArrived(w1)`: no render at Desk, one render at Overview, nothing
  for an unknown key. (B22, B24)
- **AC27** [mac] AppActivated: started with w1 (pid 7), w2 (pid 8);
  `WindowFocused(w1)`, `WindowFocused(w2)` so last-focused = {7→w1, 8→w2};
  then, counting renders per call: `AppActivated(7,false)` → 1 (null → w1);
  `AppActivated(7,false)` → 0; `AppActivated(8,false)` → 1 (w1 → w2);
  `AppActivated(9,false)` → 1 (w2 → null); `AppActivated(99,false)` → 0
  (null → null); `AppActivated(99,true)` → 1 (desktop flag flipped). (B25)
- **AC28** [mac] Worked example 12: started; `VisibleAreaChanged(VA)` →
  nothing; `VisibleAreaChanged((0,0,1440,860))` → `VisibleArea` updated,
  `ReframeCount` +1, one render. Before `Start`, and while suspended, a
  different rect is stored with `ReframeCount` unchanged and no render. An
  invalid rect → `ArgumentOutOfRangeException`, `VisibleArea` unchanged.
  (B26, B4)

### Policy
- **AC29** [mac] Worked example 10, started and not suspended: `Policy =
  Policy with { TabTint = 0.5 }` → one render, `ReframeCount` unchanged, no
  write; `Policy = Policy with { Gap = 12 }` → `ReframeCount` +1; setting a
  new instance with equal values → no render, no callback. On a not-started
  engine and on a suspended engine, any change is stored (reads back) with
  no render and `ReframeCount` unchanged. `OnChange` fires once for each
  actual value change (stored-only cases included), never for an equal one.
  `Policy = null` → `ArgumentNullException`. (B31, B35, A12)

### Frames and tick
- **AC30** [mac] Worked example 11: started, clock at 100;
  `SetFramesRunning(true)` → `OnNeedsFrames(true)` once, `LastTick == 100`;
  again → no callback; `MotionActive = true`; `Tick(100.016)` →
  `LastTickDelta == 0.016`, `LastTick == 100.016`, one render, frames still
  running; `MotionActive = false`; `Tick(100.032)` → one render, then
  `OnNeedsFrames(false)` once, `FramesRunning` false. `Tick(100.048)` →
  render, no callback. (B37, B38, B40)
- **AC31** [mac] After `ManualClock.Advance(5)`, `SetFramesRunning(true)`
  stores `LastTick == 105`. `Tick(NaN)`, `Tick(+∞)` →
  `ArgumentOutOfRangeException`. `Tick(50)` after `LastTick == 105` →
  `LastTickDelta == 0`, `LastTick == 50`. While suspended, `Tick(200)` makes
  no render and leaves `LastTick` unchanged. `ReleaseAll` with frames
  running fires `OnNeedsFrames(false)` once. (B37, B38, B41, E6, A14)

### Stubs and architecture
- **AC32** [mac] Each of the 33 command stubs called on a started engine
  (arbitrary arguments, null for `WindowKey?`/`Point?`/`string`) → no
  exception, no service calls, no callbacks, `ReframeCount`, `Windows`,
  `FocusedKey`, `Phase` unchanged. Reflection: the public methods declared
  in `DisplayEngine.Commands.cs` (identified by the name list in the Public
  API) all exist with those signatures. (B44)
- **AC33** [mac] `Redraw()` renders exactly once when idle, started and
  suspended. (B42)
- **AC34** [mac] Build/inspection: `dotnet build CanvasWm.slnx` exits 0 with
  0 warnings; `CanvasWm.Engine.csproj` targets `net10.0`, has exactly one
  `ProjectReference` (Core), no `PackageReference`, and an
  `InternalsVisibleTo` for `CanvasWm.Engine.Tests`; no file under
  `src/CanvasWm.Core`, `src/CanvasWm.Engine/Services`,
  `src/CanvasWm.Engine/Rendering` or `tests/CanvasWm.Engine.Tests/Fakes` is
  changed; reflection finds no public member of `DisplayEngine` whose name
  contains `Gate`, `Locked`, `Lesson` or `Accomplish`; no file in `src/` or
  `tests/` contains `TrellisWM` or `Licensing`; the existing
  `EngineReferencesOnlyCore` test passes.

Count: **34 [mac]** (AC1–AC34), **0 [windows]**. Windows-side wiring (frame
driver, WinEvent marshalling, virtual-desktop switching, quit pump) is
verified when the App and Win32 features land.

## Assumptions

- **A1. Names.** `DisplayEngine` in namespace `CanvasWm.Engine` avoids the
  `CanvasWm.Engine.Engine` collision; `EnginePolicy` keeps the user settings
  apart from #8's tuning constants; the menu-bar bottom becomes the
  platform-neutral `TopBarBottom`.
- **A2. ReleaseAll selection = parked OR visible fraction < 0.5.** *Needs
  human review.* The study's exact second condition is Inferred; this is the
  findings' proposed safe rule. On Windows, off-screen windows stay lost, so
  anything mostly off every display comes back. The fraction is from
  `IWindowSystem.VisibleFraction` over the **current** OS displays (a window
  on a removed monitor qualifies), using the last known frame (`Info.Frame`,
  which #13 keeps current). Strict `< 0.5`; exactly half visible stays.
  Cascade order = registration order.
- **A3. Internal extension points + `InternalsVisibleTo`.** Parking (#14),
  motion (#15), phase (#16) and dock-spot computation (#13) do not exist
  yet, so tests drive `SetParked`, `SetRememberedSpot`, `MotionActive`,
  `SetPhase`, `SetFramesRunning` and read `ReframeCount`,
  `PlacementGeneration`, `LastTick`, `LastTickDelta`. Adds one
  `InternalsVisibleTo` item to the Engine csproj.
- **A4. ReleaseAll with nothing to move completes via `Clock.After(0)`**,
  never inside the call, matching the never-re-entrant completion rule of
  engine-interfaces B5. The study's zero-write behaviour was not stated.
- **A5. Enum case names and counts** (`PreviewStyle`, `TabStyle`,
  `ProxyScale`, `ResizeCover`) are our own; only the defaults' raw values
  (1, 0, 1, 1) are observed. Overlay features may rename or add cases but
  must keep the defaults' raw values.
- **A6. Platform-specific policy fields.** *Needs human review.*
  - `tabLights` (window-control buttons drawn in tabs, macOS style) →
    **dropped**.
  - `TouchPinchPan` kept: Windows precision touchpads produce pinch/pan.
  - `BlurRadius`, `DimBlur` kept but advisory: DWM thumbnails are not easily
    blurred.
  - `TabCloseOnRight` default changed to **true** (human decision): close on
    the right is the Windows convention. Observed studied default was false.
  - `WindowCornerRadius` default changed to **8** (human decision) to match
    Windows 11. Observed studied default was 10.
  - The menu-bar bottom is display context, not policy (`TopBarBottom`,
    always null on Windows).
- **A7. `Release` ignores the suspended guard** so that user exclusions
  always take effect. In the study, release delegates to the removal method,
  which ignores calls while suspended; treated as an oversight.
- **A8. No native-tab watcher.** Windows has no native window tabs
  (`TabSibling` is always null), so `Start` schedules nothing periodic.
- **A9. `ShareRemovedSpace` is a parameter, not a global.** Core's edit
  functions already take it; avoids shared mutable state across engines.
- **A10. Thread affinity is documented, not checked.**
- **A11. `Save` is a no-op until `Start` has loaded the record**, so an idle
  engine never overwrites a saved layout with an empty one.
- **A12. Policy changes while suspended are stored only.** The study does
  not check suspension in the policy setter; we skip the re-frame because
  `Resume` re-frames anyway and writing a hidden desktop is pointless.
- **A13. `OnChange` trigger set** (started, suspended, phase, focused key,
  policy, managed-key set; at most once per call) is our choice; the study
  only shows the handler refreshing menus.
- **A14. `Tick` while suspended does nothing.** Frames are off after
  `Suspend`; a stray tick must not render a hidden desktop.
- **A15. Shell render plan** lists managed windows at their last known
  frames with the focus flag; a placeholder until #15/#16.
- **A16. `held` argument of window-added dropped** (approved). All observed
  callers pass false; if a caller needing true appears, #13 adds an
  overload.
- **A17. Capture starts when a window is first registered** (before or after
  `Start`), mirroring the stop on removal; the study's exact start point is
  not stated.
- **A18. `ReleaseAll` clears the away table**: all windows are handed back
  and the remembered spots refer to the old layout.
- **A19. `GesturePhase` adds `Cancelled`**: Windows pointer/touchpad input
  can be cancelled.
- **A20. Bad rects throw** (`ArgumentOutOfRangeException`) rather than being
  ignored, so app-side bugs surface in tests.
- **A21. Callbacks are delivered at the end of the call** (after state
  changes), not at the exact point inside the method as in the study. Still
  synchronous; makes handler-visible state consistent and the re-entry
  guard simple. `OnCarryOut` (a query) is the exception.

### Reported for review (no change to earlier specs)
- engine-interfaces' note "#23 must decide how the Engine receives display
  full frames" is resolved by `DisplayFrame` / `OtherDisplays` +
  `ParkContext()`; no seam change.
- `RenderPlan`/`WindowPlan` stay the engine-interfaces placeholder (its A16).

## Traceability

| Spec item | Findings rule |
|---|---|
| B1–B4, AC1, AC2, A1, A20 | 1, 2, 3, 4; Windows notes (display context) |
| B5–B9, AC5–AC7, A10, A21 | 7, 8, 9, 24; Windows notes (UI thread, re-entrancy) |
| B10, AC8, AC9, A8, A11, A17 | 10; worked ex. 7 |
| B11, AC10 | 11; worked ex. 4 |
| B12, AC11, AC12 | 12; worked ex. 5, 6 |
| B13, B43, AC13–AC15, A2, A4, A18 | 13; worked ex. 9; Unknowns (release selection) |
| B14, B15, AC16, A7 | 14, 13 |
| B16, AC17 | 15, 10 |
| B17, B18, AC18, AC19, A16 | 16; worked ex. 1; Unknowns (`held`) |
| B19, AC20, AC22, AC23 | 17; worked ex. 2, 8 |
| B20–B24, AC21, AC24–AC26 | 18, 19, 21; worked ex. 3 |
| B25, AC27 | 20; worked ex. 3 |
| B26, AC28 | 4; worked ex. 12 |
| B27, A3 | 13, 17 (parked flag) |
| B28–B34, AC3, AC4, AC29, A5, A6, A9, A12 | 5, 22, 23; worked ex. 10 |
| B35, A13 | 24 (onChange) |
| B36–B41, AC30, AC31, A14 | 25, 26, 27, 28; worked ex. 11 |
| B42, AC33, A15 | 6, 21, 26 (render) |
| B44, AC32, A19 | 29, 30 |
| AC34 | CLAUDE.md architecture; engine-interfaces AC28; 24 (gate/lessons excluded) |

## Amendments from #32 (engine-router)

- `DisplayEngine` gains `FocusLeftWindows(int processId)`: placeholder (human decision) — clears `FocusedKey` only; ignored while suspended; keeps the re-entry guard; fires `OnChange`; no render or save. See specs/engine-router.md.
