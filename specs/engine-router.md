# Spec: engine-router

Feature #32 (split from #29 app-composition). Input:
`findings/app-composition.md` (Part 1 rules 6–22, 27; Part 2 M1–M3, M8, N7,
N8, O3) and the human decisions recorded on queue rows #29 and #32.

## Goal

Build the pure, Mac-testable **coordinator** that sits between the window
tracker (#30), the per-display `DisplayEngine`s (#23/#26/#27) and the App
(#33). It:

- implements `IWindowTrackerTarget` and routes every tracker decision to the
  right engine (or to all engines);
- creates one engine per (display, desktop) **lazily**, through an injected
  factory, the first time that desktop is shown;
- keeps the display list from injected `DisplayInfo` snapshots and
  orchestrates display changes (signature with DPI, debounce, release,
  rebuild, re-show);
- chooses the active engine and the pointer engine and executes Core
  `ShortcutCommand`s on the right engine (#7 B3/B4);
- aggregates the engines' frame requests into one signal plus a `Tick(now)`
  fan-out;
- runs the quit sequence (save all, release all, report completion when all
  releases finished or after 2.0 s);
- raises hotkey "apply now" requests at the agreed trigger points (#12 A5)
  without any Win32 type.

It also adds the small seams the App needs from existing types:
`DisplayEngine.FocusLeftWindows` (placeholder), `WindowTracker.IdentityOf` /
`KeyOf`, a shared `DisplayInfo` record and Core `DisplaySignature`.

Out of scope: Win32 display enumeration, file stores, host window, real
clock, tick driver, tray (#33); settings UI and the "open a file-manager
window" part of Split (#34); virtual-desktop detection, per-desktop window
filtering and rescan-on-switch (#35, a seam is left: `DesktopShown`);
hand-off / carry between engines and saved-owner routing (#24); renderers and
capture (#16). Anything related to licences, upgrades, locked states,
first-run onboarding or lessons is **excluded entirely** (not modelled, not
even as stubs).

## Placement

- **Core** (`src/CanvasWm.Core`, `net10.0`): new namespace
  `CanvasWm.Core.Displays`, file `Displays/DisplaySignature.cs`
  (`DisplaySignatureEntry`, `DisplaySignature`). Pure. Depends only on
  `CanvasWm.Core.Geometry`.
- **Engine** (`src/CanvasWm.Engine`, `net10.0`):
  - new file `Services/DisplayInfo.cs` (`DisplayInfo`, namespace
    `CanvasWm.Engine.Services`);
  - new folder `Routing/`, namespace `CanvasWm.Engine.Routing`:
    `EngineRouter.cs` (may be split into partial files), `RouterOptions.cs`,
    `CommandRoute.cs`, `HotkeyTriggers.cs` (with `HotkeyApplyReason`,
    `HotkeyApplyRequest`);
  - existing-type changes (see "Changes to existing types" below):
    `DisplayEngine` (one method), `WindowTracker` (two methods), new
    `Tracking/TrackedIdentity.cs`.
  - The project still references only Core; no package reference.
- **Tests**: `tests/CanvasWm.Core.Tests/Displays/*Tests.cs`,
  `tests/CanvasWm.Engine.Tests/Routing/*Tests.cs`, plus additions under
  `tests/CanvasWm.Engine.Tests/Tracking/` and `EngineShell/` for the
  existing-type changes. Existing fakes are reused unchanged
  (`ManualClock`, `FakeWindowSystem`, `FakeCapture`, `InMemoryLimitStore`,
  `InMemoryDisplayStore`, `RecordingRenderer`, `FakeWindowSource`).
- Depends on: `specs/engine-interfaces.md` (keys, `IClock`, `IWindowSystem`,
  `WindowInfo`, `IDisplayStore`), `specs/engine-inbound-api.md` (+ #26/#27
  behaviour of `DisplayEngine`), `specs/window-tracker.md`
  (`IWindowTrackerTarget`, `WindowTracker`), `specs/shortcut-model.md`
  (`ShortcutCommand`, `CommandKind`, `TargetEngine`, `ShortcutCatalog`,
  `ShortcutDispatch`, `ShortcutBindings`, B3/B4), `specs/win32-hotkeys.md`
  (`HotkeyPlan.Desired`, A5, B17), `specs/win32-frame-writer.md` R4,
  Core `DisplayChoice.ForRect` (#11), Core `Point`/`Rect`.
- Coordinates: DIPs, y-down, virtual-desktop origin. All rects in
  `DisplayInfo` are DIPs (the Win32 side converts with `DipSpace`, #33).
- No text in `src/` or `tests/` may contain the words `TrellisWM` or
  `Licensing`.

### Changes to existing types (approved by #32 scope)

| Type | Change |
|---|---|
| `CanvasWm.Engine.DisplayEngine` | + `public void FocusLeftWindows(int processId)` (placeholder, B40) |
| `CanvasWm.Engine.Tracking.WindowTracker` | + `public TrackedIdentity? IdentityOf(WindowKey key)`; + `public WindowKey? KeyOf(ulong handle)` (B41, B42) |
| new `CanvasWm.Engine.Tracking.TrackedIdentity` | record struct (handle, pid, tid) |

No other existing member changes. No earlier spec file is edited; the
additions are reported for the spec owners (see "Reported for review").

## Public API

A non-nullable reference parameter that is null throws
`ArgumentNullException` before any state change. All public members carry
XML docs.

### Core — display signature

```csharp
namespace CanvasWm.Core.Displays;

/// <summary>The parts of one display that define the display set (B1).</summary>
/// <param name="Key">Stable display identity text (non-empty).</param>
/// <param name="Frame">Full frame, DIPs.</param>
/// <param name="Dpi">Effective DPI (96 = 100 %).</param>
public readonly record struct DisplaySignatureEntry(string Key, Rect Frame, int Dpi);

/// <summary>One text value that changes when a display is added, removed, moved, resized or
/// rescaled, and not when only its work area changes. Pure.</summary>
public static class DisplaySignature
{
    /// <summary>Entries sorted by Key (ordinal); each "Key@X,Y,W,H@Dpi"; joined by ";" (B1).</summary>
    /// <exception cref="ArgumentNullException">displays is null.</exception>
    /// <exception cref="ArgumentException">An entry's Key is null or empty.</exception>
    public static string Of(IEnumerable<DisplaySignatureEntry> displays);
}
```

### Engine — display snapshot record

```csharp
namespace CanvasWm.Engine.Services;

/// <summary>One display as the platform reports it. Data only; all rects in DIPs.</summary>
/// <param name="Key">Stable display key.</param>
/// <param name="Frame">Full frame.</param>
/// <param name="WorkArea">Usable area (frame minus taskbars).</param>
/// <param name="Dpi">Effective DPI, &gt; 0 (96 = 100 %). Used only in the signature.</param>
/// <param name="IsMain">True for the primary display.</param>
/// <exception cref="ArgumentNullException">Key is null.</exception>
/// <exception cref="ArgumentOutOfRangeException">Frame or WorkArea has a non-finite component
/// or a negative size; Dpi &lt;= 0.</exception>
public sealed record DisplayInfo(DisplayKey Key, Rect Frame, Rect WorkArea, int Dpi, bool IsMain)
{
    // init accessors validate as above (same pattern as WindowInfo)

    /// <summary>DisplaySignature.Of over (Key.Value, Frame, Dpi) of each display (B2).</summary>
    /// <exception cref="ArgumentNullException">displays or an element is null.</exception>
    public static string SignatureOf(IEnumerable<DisplayInfo> displays);
}
```

### Engine — tracker identity

```csharp
namespace CanvasWm.Engine.Tracking;

/// <summary>OS identity of a tracked window, as recorded at admission.</summary>
public readonly record struct TrackedIdentity(ulong Handle, int ProcessId, int ThreadId);

public sealed class WindowTracker   // existing; additions only
{
    /// <summary>Identity of the tracked window with this key, or null if not tracked (B41).</summary>
    public TrackedIdentity? IdentityOf(WindowKey key);

    /// <summary>Key of the tracked window with this handle, or null if not tracked or 0 (B42).</summary>
    public WindowKey? KeyOf(ulong handle);
}
```

### Engine — DisplayEngine addition

```csharp
public sealed partial class DisplayEngine   // existing; addition only
{
    /// <summary>Focus moved to an unmanaged window (or none) of processId. PLACEHOLDER until the
    /// studied effect is known: clears FocusedKey only (B40).</summary>
    public void FocusLeftWindows(int processId);
}
```

### Engine — router

```csharp
namespace CanvasWm.Engine.Routing;

/// <summary>Router timings. Immutable; with-expressions. Every value finite and &gt; 0,
/// else ArgumentOutOfRangeException from the init accessor.</summary>
public sealed record RouterOptions
{
    /// <summary>The default options; the same instance every time.</summary>
    public static RouterOptions Default { get; }
    /// <summary>Delay before a changed display set is rebuilt, s. Default 1.5.</summary>
    public double DisplaySetChangeDelaySeconds { get; init; }
    /// <summary>Delay before work areas are refreshed when the set is unchanged, s. Default 0.4.</summary>
    public double WorkAreaRefreshDelaySeconds { get; init; }
    /// <summary>Delay between a rebuild and re-showing desktops, s. Default 0.8.</summary>
    public double ReshowDelaySeconds { get; init; }
    /// <summary>Longest wait for release completions when quitting, s. Default 2.0.</summary>
    public double ShutdownTimeoutSeconds { get; init; }
}

/// <summary>What Execute did (B27).</summary>
/// <param name="Engine">Key of the engine that received the call.</param>
/// <param name="Command">The executed command.</param>
/// <param name="ZoomToward">ZoomIn argument (null = centre); null for other kinds.</param>
/// <param name="SplitKey">SplitForNewWindow argument; null when no call was made or other kinds.</param>
public sealed record CommandRoute(EngineKey Engine, ShortcutCommand Command, Point? ZoomToward,
                                  WindowKey? SplitKey);

/// <summary>Coordinator over the per-(display, desktop) engines. Single-threaded: every member
/// is called on the engine (UI) thread, and every clock action and engine callback arrives there.
/// Implements the tracker target. Not reusable after BeginShutdown.</summary>
public sealed class EngineRouter : IWindowTrackerTarget
{
    /// <summary>Creates an idle router (B3). Creates no engine, calls neither the factory nor the
    /// clock nor the window system.</summary>
    /// <param name="displays">Initial display snapshot (validated as in DisplaysChanged).</param>
    /// <param name="engineFactory">Creates the engine for (key, display). Must return an engine
    /// whose Key equals key, built with display.WorkArea as its visible area.</param>
    /// <param name="clock">Delays (debounce, re-show, quit deadline).</param>
    /// <param name="windows">Read for Pointer only.</param>
    /// <param name="options">null = RouterOptions.Default.</param>
    public EngineRouter(IReadOnlyList<DisplayInfo> displays,
                        Func<EngineKey, DisplayInfo, DisplayEngine> engineFactory,
                        IClock clock, IWindowSystem windows, RouterOptions? options = null);

    // ---- state (inspectors; no side effects) ----
    public RouterOptions Options { get; }
    /// <summary>The applied display list, in snapshot order (copy).</summary>
    public IReadOnlyList<DisplayInfo> Displays { get; }
    /// <summary>Existing engines in creation order (copy).</summary>
    public IReadOnlyList<DisplayEngine> Engines { get; }
    /// <summary>The engine of the desktop currently shown on that display, or null.</summary>
    public DisplayEngine? EngineFor(DisplayKey display);
    /// <summary>Desktop id currently shown on the display (EngineKey.SingleDesktopId until #35);
    /// null for an unknown display.</summary>
    public string? DesktopOf(DisplayKey display);
    /// <summary>True after Ready was received.</summary>
    public bool IsReady { get; }
    /// <summary>True from a changed-display notification until the following re-show (B15).</summary>
    public bool IsTornDown { get; }
    /// <summary>True after BeginShutdown.</summary>
    public bool IsShuttingDown { get; }
    /// <summary>True once shutdown completed: all releases reported or the deadline passed (B33).</summary>
    public bool IsShutdownComplete { get; }
    /// <summary>Key of the last window reported focused (B12).</summary>
    public WindowKey? LastFocusedKey { get; }
    /// <summary>Process id of the last activated app, or null before any.</summary>
    public int? LastActiveProcessId { get; }
    /// <summary>User preference "step in toward the pointer" (#7 B4). Default true.</summary>
    public bool StepTowardPointer { get; set; }
    /// <summary>Aggregated frame request (B29).</summary>
    public bool FramesNeeded { get; }
    /// <summary>Active engine per B23, or null.</summary>
    public DisplayEngine? ActiveEngine { get; }
    /// <summary>Engine under the window system's pointer (B24), or null.</summary>
    public DisplayEngine? PointerEngine { get; }
    /// <summary>Engine shown on the display whose frame contains point (B24), or null.</summary>
    public DisplayEngine? EngineAt(Point point);

    // ---- outbound events (raised synchronously on the engine thread) ----
    /// <summary>Raised once, after the first successful show of desktops (B8).</summary>
    public event Action? Started;
    /// <summary>An engine reported a menu-relevant change, or the engine set changed (B30).</summary>
    public event Action? Changed;
    /// <summary>FramesNeeded changed (edge-triggered, B29).</summary>
    public event Action<bool>? FramesNeededChanged;
    /// <summary>An engine was dropped by a rebuild, after its ReleaseAll (B18); the App disposes
    /// its per-display resources.</summary>
    public event Action<DisplayEngine>? EngineRetired;
    /// <summary>Raised exactly once when shutdown completes (B33).</summary>
    public event Action? ShutdownCompleted;
    /// <summary>Diagnostics (engine Log callbacks forwarded, router notes). No content contract.</summary>
    public event Action<string>? Log;

    // ---- IWindowTrackerTarget (B9–B13) ----
    public IReadOnlyList<Rect> DisplayFrames { get; }
    public bool Holds(WindowKey key);
    public void Ready(IReadOnlyList<WindowInfo> windows);
    public void WindowAdded(WindowInfo info);
    public void WindowRemoved(WindowKey key, bool temporary);
    public void WindowFocused(WindowKey key);
    public void FocusLeftWindows(int processId);
    public void WindowMoved(WindowKey key, Rect frame);
    public void WindowTitleChanged(WindowKey key, string title);
    public void WindowBecameDockable(WindowKey key);
    public void AppActivated(int processId, bool ownsDesktop);
    public void Release(WindowKey key);

    // ---- commands, frames, displays, desktops, quit ----
    /// <summary>Executes a shortcut command on its target engine (B25–B28); null = nothing ran.</summary>
    public CommandRoute? Execute(ShortcutCommand command);
    /// <summary>Calls Tick(now) on every shown engine whose frames are running (B31).</summary>
    /// <exception cref="ArgumentOutOfRangeException">now is NaN or infinite.</exception>
    public void Tick(double now);
    /// <summary>A new display snapshot from the platform (B14–B21).</summary>
    /// <exception cref="ArgumentNullException">displays or an element is null.</exception>
    /// <exception cref="ArgumentException">Two displays share a key.</exception>
    public void DisplaysChanged(IReadOnlyList<DisplayInfo> displays);
    /// <summary>SEAM for #35: the OS now shows desktopId on display (B22).</summary>
    /// <exception cref="ArgumentNullException">display or desktopId is null.</exception>
    /// <exception cref="ArgumentException">desktopId empty or contains EngineKey.Separator.</exception>
    public void DesktopShown(DisplayKey display, string desktopId);
    /// <summary>Quit sequence (B32–B34). Second call: no-op.</summary>
    public void BeginShutdown();

    /// <summary>Test seam: when set, Execute calls this instead of the DisplayEngine method.</summary>
    internal Action<DisplayEngine, CommandRoute>? CommandInvoker { get; set; }
}
```

### Engine — hotkey apply triggers

```csharp
namespace CanvasWm.Engine.Routing;

/// <summary>Why the App should call the hotkey source's Apply now (#12 A5).</summary>
public enum HotkeyApplyReason
{
    Start = 0, BindingsChanged = 1, RecordingStarted = 2, RecordingStopped = 3,
    SettingsOpened = 4, Retry = 5, Stop = 6,
}

/// <summary>One apply request: register exactly Desired (B36).</summary>
public sealed record HotkeyApplyRequest(HotkeyApplyReason Reason, IReadOnlyList<KeyCombo> Desired);

/// <summary>Owns the bindings, dispatch table and recording flag, and says when hotkeys must be
/// re-applied. No OS types. Single-threaded (engine thread).</summary>
public sealed class HotkeyTriggers
{
    /// <exception cref="ArgumentNullException">bindings is null.</exception>
    public HotkeyTriggers(ShortcutBindings bindings);
    public ShortcutBindings Bindings { get; }
    /// <summary>ShortcutDispatch.Build(Bindings).</summary>
    public IReadOnlyList<DispatchEntry> Table { get; }
    public bool Recording { get; }
    public bool IsStarted { get; }
    public bool IsStopped { get; }
    /// <summary>HotkeyPlan.Desired(Table, Recording); empty after Stop.</summary>
    public IReadOnlyList<KeyCombo> Desired { get; }
    /// <summary>Raised synchronously by the trigger methods (B36, B37).</summary>
    public event Action<HotkeyApplyRequest>? ApplyRequested;

    public void Start();                                // reason Start (wire to EngineRouter.Started)
    public void SetBindings(ShortcutBindings bindings); // reason BindingsChanged
    public void BeginRecording();                       // reason RecordingStarted
    public void EndRecording();                         // reason RecordingStopped
    public void SettingsOpened();                       // reason SettingsOpened
    public void Retry();                                // reason Retry
    public void Stop();                                 // reason Stop, Desired empty
    /// <summary>The command for a pressed combo, or null while recording, before Start, after
    /// Stop, or when nothing matches (B38).</summary>
    public ShortcutCommand? Pressed(KeyCombo combo);
}
```

## Behaviour

### A. Core signature

- **B1 (DisplaySignature.Of).** Sort the entries by `Key` with
  `StringComparer.Ordinal` (stable for equal keys). Format each as
  `Key + "@" + X + "," + Y + "," + Width + "," + Height + "@" + Dpi`, every
  number with `ToString(CultureInfo.InvariantCulture)` (shortest round-trip
  for doubles, decimal for the int). Join with `";"`. No entries → `""`. No
  escaping (A4). Work area and main flag are not inputs.
- **B2 (DisplayInfo.SignatureOf).** `DisplaySignature.Of(displays.Select(d
  => new DisplaySignatureEntry(d.Key.Value, d.Frame, d.Dpi)))`. Hence equal
  for snapshots that differ only in `WorkArea`, `IsMain` or list order.
  *CanvasWM addition:* `Dpi` is included because on Windows a scale change
  alters DIP geometry without moving pixels (M8).

### B. Construction and engines

- **B3 (construction).** Validate as `DisplaysChanged` (B14 step 1). Store a
  copy of the display list as the **applied list**, its signature as the
  **applied signature**, the current desktop of every display =
  `EngineKey.SingleDesktopId`, `StepTowardPointer = true`, all flags false.
  No engine exists until the first show (studied rule 6e).
- **B4 (engine key).** The engine of display `d` with desktop `s` has key
  `new EngineKey(d.Key, s)`. In this build `s` is always
  `EngineKey.SingleDesktopId` unless `DesktopShown` changed it (B22).
- **B5 (get or create).** The engine for (display `d`, desktop `s`) is
  looked up by key; if absent it is created now, and only here:
  1. `e = engineFactory(key, d)`. If `e` is null or `e.Key != key` →
     `InvalidOperationException` (nothing registered).
  2. `e.DisplayFrame = d.Frame`; `e.OtherDisplays` = frames of every other
     display of the applied list, in list order.
  3. Wire callbacks: `OnNeedsFrames` → B29; `OnChange` → raise `Changed`;
     `Log` → raise `Log`. `OnHandOff` and `OnCarryOut` stay null (#24).
  4. If `LastActiveProcessId` is not null:
     `e.AppActivated(LastActiveProcessId, lastOwnsDesktop)` (rule 8).
  5. Append to the creation-order list; raise `Changed` once.
- **B6 (owner display of a window).** For a window key `k` with frame `f`:
  1. if some engine holds `k` (`Holds`, engines in creation order, first
     wins) → that engine's display;
  2. else `i = DisplayChoice.ForRect(f, appliedFrames, mainIndex)` where
     `mainIndex` = index of the first display with `IsMain`, else 0; the
     owner is display `i` (largest overlap, ties lowest index; no overlap →
     the main display). Empty list → no owner.
  Saved-layout ownership (studied rule 17) is #24 (A6).
- **B7 (show one display).** For display `d` with current desktop `s`:
  `e` = get or create (B5); `windows` = the **window mirror** (B9) entries,
  in mirror order, whose owner display (B6) is `d`. If this router has
  started `e` before → `e.Resume(windows)`; else `e.Start(windows)` and mark
  it started-before. (Resume on a running engine is a no-op, engine B12.)
- **B8 (show current desktops).** Runs only when `IsReady`, not
  `IsTornDown`, not `IsShuttingDown`. For each display of the applied list in
  order: B7. Afterwards, the first time this completes, raise `Started`
  once. Show runs: on `Ready` (B10), after a rebuild's re-show delay (B18),
  and for one display from `DesktopShown` (B22).

### C. Tracker target and routing (studied rule 17)

- **B9 (window mirror).** The router keeps an ordered map key → `WindowInfo`
  mirroring the tracker's set: `Ready` replaces it with the given list;
  `WindowAdded` appends (or replaces in place when present);
  `WindowRemoved` and `Release` remove; `WindowMoved` sets `Frame`;
  `WindowTitleChanged` sets `Title`; `WindowBecameDockable` sets `Dockable =
  true` (unknown keys: no mirror change). The mirror is updated for every
  call, also while torn down; during shutdown no updates are needed (B34).
- **B10 (Ready).** First call: replace the mirror, `IsReady = true`, then
  B8 (which does nothing while torn down; the post-rebuild re-show will
  start the engines). Later calls: ignored entirely.
- **B11 (holder).** `holder(k)` = first engine in creation order with
  `Holds(k)`, or null. `Holds(k)` (the target member) = `holder(k) != null`.
- **B12 (per-window routing).** After the mirror update (B9):
  - `WindowAdded(info)`: if `holder(info.Key)` exists → `holder.WindowAdded(
    info)`; else the owner display (B6) → `EngineFor(owner)?.WindowAdded(
    info)`; no engine → mirror only (picked up by the next show).
  - `WindowRemoved(k, t)` → `holder(k)?.WindowRemoved(k, t)`.
  - `WindowFocused(k)`: `LastFocusedKey = k`; `holder(k)?.WindowFocused(k)`.
  - `WindowMoved(k, f)` → `holder(k)?.WindowMoved(k, f)`.
  - `WindowTitleChanged(k, t)` → `holder(k)?.WindowTitleChanged(k, t)`.
  - `WindowBecameDockable(k)` → `holder(k)?.WindowBecameDockable(k)`.
  - `Release(k)` → `holder(k)?.Release(k)`.
  The holder is resolved before the call. `WindowRemoved` and `Release` of
  the key equal to `LastFocusedKey` clear `LastFocusedKey`.
- **B13 (broadcasts).**
  - `FocusLeftWindows(pid)` → `FocusLeftWindows(pid)` on every engine, in
    creation order. `LastFocusedKey` is unchanged (A8).
  - `AppActivated(pid, owns)`: `LastActiveProcessId = pid`,
    `lastOwnsDesktop = owns`; then `AppActivated(pid, owns)` on every
    engine, in creation order (suspended engines record it, engine B25).
  - Before `Ready`, the routed calls of B12/B13 still update the mirror and
    the last-focused/last-active state; there are no engines to call.

### D. Display changes (studied rules 10–13)

- **B14 (DisplaysChanged).**
  1. Validate: null list or element → `ArgumentNullException`; duplicate
     `Key` → `ArgumentException`; nothing changes on failure. An empty list
     is accepted.
  2. If `IsShuttingDown` → ignore (after validation).
  3. Increment the **display generation** `g` (this cancels every pending
     display action, A2) and keep a copy of the snapshot as `pending`.
  4. If `IsTornDown` or `SignatureOf(pending) != applied signature` →
     **changed path** (B15); else **unchanged path** (B16).
- **B15 (changed path).** `IsTornDown = true`; recompute `FramesNeeded`
  (B29, becomes false). `clock.After(DisplaySetChangeDelaySeconds, act)`
  where `act` runs the rebuild (B18) with `pending` only if the generation
  is still `g`.
- **B16 (unchanged path).** `clock.After(WorkAreaRefreshDelaySeconds,
  act)` where `act` runs the refresh (B17) with `pending` only if the
  generation is still `g`.
- **B17 (refresh work areas).** Replace the applied list with `pending`
  (same signature: only work areas / main flag may differ). For each display
  in order, call `VisibleAreaChanged(d.WorkArea)` on every engine of that
  display (all desktops, creation order). (Engines ignore an equal area,
  engine B26.) One refresh per notification burst (A3).
- **B18 (rebuild).**
  1. `fallback` = work area of the first `IsMain` display of `pending`,
     else of its first display; null when `pending` is empty.
  2. For each engine in creation order: if its display key is in `pending`
     → `e.ReleaseAll(null)`; else `e.ReleaseAll(fallback)`. Completions are
     not awaited (no completion passed).
  3. Then for each of those engines, in creation order: detach the router's
     callbacks (`OnNeedsFrames`, `OnChange`, `Log` = null), remove it, raise
     `EngineRetired(e)`. The started-before marks are dropped with them.
     Recompute `FramesNeeded`.
  4. Applied list = `pending`; applied signature = its signature; current
     desktop of every display = the previous value for displays still
     present, else `EngineKey.SingleDesktopId`.
  5. If at least one engine was retired, raise `Changed` once.
  6. `clock.After(ReshowDelaySeconds, act)` where `act`, only if the
     generation is still `g`, sets `IsTornDown = false`, recomputes
     `FramesNeeded`, then runs B8 (fresh engines load their saved layouts
     through `Start`).
- **B19.** Engines are not saved during a rebuild (they save on every
  change). The renderer teardown of the studied rebuild is the App's job on
  `EngineRetired` (#33).
- **B20 (routing while torn down).** Until the rebuild runs, existing
  engines keep receiving routed calls (B12/B13) and commands (B25); between
  rebuild and re-show there are no engines, so calls only update the mirror
  and `Execute` returns null.
- **B21 (DisplayFrames).** `DisplayFrames` = frames of the applied list, in
  order (a new list each call).

### E. Desktop seam (#35)

- **B22 (DesktopShown).** Validate (null → `ArgumentNullException`; empty or
  containing `EngineKey.Separator` → `ArgumentException`). Ignore if
  shutting down or `display` is not in the applied list, or if `desktopId`
  equals the current desktop of that display. Otherwise: `EngineFor(display)
  ?.Suspend()`; set the display's current desktop to `desktopId`; then, if
  `IsReady` and not torn down, show that display only (B7). In this build no
  caller exists outside tests; #35 adds the per-desktop window filter, the
  tracker rescan and the 0.35 s wait (studied rule 15).

### F. Active engine, pointer engine, commands (rules 18–20)

- **B23 (active engine).** First match wins, over the **shown engines**
  (`EngineFor(d)` for each display `d` of the applied list, in list order,
  skipping nulls):
  1. the first shown engine with `Phase == StagePhase.Overview`;
  2. `h = holder(LastFocusedKey)` when `LastFocusedKey` is not null and `h`
     is not null and (`LastActiveProcessId` is null **or**
     `h.Window(LastFocusedKey).Info.ProcessId == LastActiveProcessId`) → `h`;
  3. when `LastActiveProcessId` is not null: the first shown engine holding
     a window (`Windows`) whose `Info.ProcessId == LastActiveProcessId`;
  4. `PointerEngine`;
  5. null.
- **B24 (pointer engine).** `EngineAt(p)`: the first display of the applied
  list whose frame contains `p` with the half-open test `X <= p.X < X+W` and
  `Y <= p.Y < Y+H` (same as tracker A9) → `EngineFor(that display)`; none →
  null. `PointerEngine = EngineAt(windows.Pointer)`.
- **B25 (Execute).** If shutting down → return null (no call). Read
  `p = windows.Pointer` once. Target kind = `TargetEngine.Active` for view
  commands (`Action` null), else `ShortcutCatalog.Info(command.Action).
  Target`. Resolve:
  - `Active` → `ActiveEngine`;
  - `PointerThenActive` (StepIn, #7 B4) → if `StepTowardPointer` and
    `EngineAt(p)` is not null → that engine with `ZoomToward = p`, or
    `ZoomToward = null` when that engine's `Phase == Overview`; otherwise
    `ActiveEngine` with `ZoomToward = null`;
  - `Pointer` (Split) → `EngineAt(p)` only.
  No engine → return null, nothing runs (the App still treats the key as
  consumed, #7 B3).
- **B26 (engine call per CommandKind, #7 B3).**

  | CommandKind | DisplayEngine call |
  |---|---|
  | ToggleOverview | `ToggleOverview()` |
  | ZoomOut | `ZoomOut()` |
  | ZoomIn | `ZoomIn(ZoomToward)` |
  | Move | `Move(command.Toward!.Value)` |
  | Back / Forward | `Back()` / `Forward()` |
  | ToggleMaximize | `ToggleMaximize(null)` |
  | ToggleDesktopHidden | `ToggleDesktopHidden()` |
  | SplitForNewWindow | `SplitForNewWindow(k)` with `k = target.FocusedKey`; when null, no call (A5) |
  | GoToView / SaveView | `GoToFraming(Slot)` / `SaveFraming(Slot)` |

- **B27 (result).** When a target was resolved, return
  `new CommandRoute(target.Key, command, ZoomToward (ZoomIn only, else
  null), SplitKey (Split with a call, else null))`. When `CommandInvoker` is
  set, it is called with `(target, route)` instead of the B26 call (test
  seam); exactly one of them runs (for Split without a focused key, neither
  runs).
- **B28.** Exceptions from the engine call propagate (for example the
  engine's re-entry guard when `Execute` is called from inside that engine's
  callback).

### G. Frames (rules 21–22)

- **B29 (aggregation).** `FramesNeeded = !IsTornDown && !IsShuttingDown &&
  any existing engine has FramesRunning`. It is recomputed after every
  engine `OnNeedsFrames` callback and where B15, B18 and B32 say so; when
  the value differs from the last raised value (initially false),
  `FramesNeededChanged(newValue)` is raised. Handlers may run inside an
  engine callback and must not call engines synchronously (engine B7); the
  App only starts or parks its tick driver.
- **B30 (Changed).** Raised for each engine `OnChange`, and as stated in
  B5/B18. Same handler constraint as B29.
- **B31 (Tick).** `now` not finite → `ArgumentOutOfRangeException`. If torn
  down or shutting down → nothing. Otherwise take a snapshot of the shown
  engines (B23 order) and call `Tick(now)` on each whose `FramesRunning` is
  true at the moment of the call. Engines of non-shown desktops are not
  ticked (they are suspended and frames-off anyway).

### H. Quit (rule 27)

- **B32 (BeginShutdown).** Second call → no-op. Otherwise:
  1. `IsShuttingDown = true`; increment the display generation (pending
     display actions are cancelled); recompute `FramesNeeded` (→ false).
  2. `Save()` on **every** engine, creation order.
  3. Then for every engine, creation order: `ReleaseAll(into, done)` where
     `into` = null if the engine's display is in the latest snapshot (the
     pending one when a display action is outstanding, else the applied
     list), else that snapshot's fallback area (B18 step 1 rule) (A7).
     `done` counts down from the number of engines.
  4. `deadline = clock.Now + ShutdownTimeoutSeconds`;
     `clock.After(ShutdownTimeoutSeconds, complete)`.
  5. Zero engines → complete at once (inside this call).
- **B33 (completion).** "Complete" sets `IsShutdownComplete = true` and
  raises `ShutdownCompleted`, both exactly once, at the first of: the last
  `done` call, or the deadline action. `IsShutdownComplete` also reads true
  whenever `clock.Now >= deadline`, so an App loop polling it cannot wait
  past the deadline even if the clock action is late. The App pumps its
  message loop in ≤ 0.02 s slices until it reads true (#33).
- **B34 (inert after shutdown).** After `BeginShutdown`: `Execute` → null;
  `Tick`, `DisplaysChanged` (after validation), `DesktopShown`, `Ready` and
  the routed calls of B12/B13 do nothing (no engine call); pending clock
  actions do nothing. Inspectors keep working; `Engines` still lists the
  released engines.

### I. Hotkey triggers (#12 A5, B17; studied rule 5)

- **B35 (state).** Constructor: `Bindings = bindings`, `Table =
  ShortcutDispatch.Build(bindings)`, `Recording = false`, not started, not
  stopped. `Desired = IsStopped ? [] : HotkeyPlan.Desired(Table,
  Recording)`.
- **B36 (raising rule).** A trigger method first updates state, then raises
  `ApplyRequested(new HotkeyApplyRequest(reason, Desired))` **only if
  `IsStarted` and not `IsStopped`** (before `Start` the request is implicit
  in the later `Start` request; after `Stop` nothing is registered). `Stop`
  itself is the one exception (B37).
- **B37 (triggers).**
  - `Start()`: if already started or stopped → nothing. Else `IsStarted =
    true`, raise `Start`. The App wires `EngineRouter.Started` to it, so
    hotkeys are applied after the engines started (studied rule 5).
  - `SetBindings(b)`: `Bindings = b`, `Table` rebuilt; raise
    `BindingsChanged` (always, even when equal: Apply is idempotent and
    doubles as a retry).
  - `BeginRecording()`: already recording → nothing. Else `Recording =
    true`; raise `RecordingStarted` (Desired is empty).
  - `EndRecording()`: not recording → nothing. Else `Recording = false`;
    raise `RecordingStopped`.
  - `SettingsOpened()` → raise `SettingsOpened`. `Retry()` → raise `Retry`.
  - `Stop()`: if stopped → nothing. Else `IsStopped = true`; if it was
    started, raise `Stop` with an empty `Desired` (the App calls
    `Apply([])`, N8 step 1). Not started → no event.
  There is no periodic retry and no timer.
- **B38 (Pressed).** Returns null when not started, stopped or recording;
  else `ShortcutDispatch.Match(Table, combo)`. The App passes a non-null
  result to `EngineRouter.Execute`.

### J. Existing-type additions

- **B40 (DisplayEngine.FocusLeftWindows — placeholder).** Runs inside the
  engine's normal call wrapper (re-entry guard, engine B7; OnChange when
  `FocusedKey` changed, engine A13). If `IsSuspended` → nothing. Otherwise
  `FocusedKey = null`. Nothing else: no render, no save, no frame write, the
  pid → last-focused map and `EngineActivatedKey` unchanged; `processId` is
  not used. Marked in code with a placeholder comment naming this rule; to
  be replaced once the studied effect is traced (findings Unknowns).
- **B41 (WindowTracker.IdentityOf).** Null key → `ArgumentNullException`.
  Tracked → `new TrackedIdentity(handle, Info.ProcessId, threadId)` from the
  record; else null. Makes no call on any dependency; works before Start
  (null) and after Dispose (records are not cleared by Dispose).
- **B42 (WindowTracker.KeyOf).** `0` → null (no throw). A tracked record
  with that handle → `KeyFor(handle)`; else null. No dependency call.
- **B43.** The App (#33) implements `IManagedWindowMap` over B41/B42
  (`Resolve(k)` = `IdentityOf(k)` mapped to `ManagedWindowIdentity`,
  `KeyOf(h)` = `KeyOf(h)`), closing #11 R4. Windows an engine still holds
  but the tracker does not (parked from an earlier session, not yet
  reported) resolve to null and their writes fail; accepted for this build
  (#14).

### Worked examples

Common rig: displays **A** = key `"A"`, frame (0,0,1920,1080), work
(0,0,1920,1040), Dpi 96, main; **B** = key `"B"`, frame (1920,0,1280,720),
work (1920,0,1280,680), Dpi 144. Windows: w1 pid 100 frame (100,100,800,600)
(on A), w2 pid 200 frame (2000,100,800,500) (on B), w3 pid 100 frame
(5000,100,800,600) (on no display). Clock starts at 0.

1. **Lazy start.** Construction: `Engines` empty, factory not called.
   `Ready([w1,w2,w3])` → factory called for `A|default` then `B|default`;
   A starts with [w1, w3] (w3 has no overlap → main display A), B with [w2];
   `Started` raised once.
2. **Active engine.** Pointer at (2500,300) (on B).
   - No focus/activation yet → steps 1–3 miss → pointer engine **B**.
   - `WindowFocused(w1)`, `AppActivated(100,false)` → step 2: holder A, pid
     100 = 100 → **A**.
   - `FocusLeftWindows(200)`, `AppActivated(200,false)` → step 2 fails (w1 is
     pid 100); step 3: B holds w2 (pid 200) → **B**.
   - `AppActivated(300,false)` → steps 2–3 miss → pointer → **B**; pointer
     moved to (10,10) → **A**; pointer (-50,-50) → **null**.
   - `B` phase set to Overview (internal `SetPhase`) → **B** whatever else.
3. **Command routing.** Pointer on B at (2500,300), active engine A (after
   `WindowFocused(w1)`, `AppActivated(100,false)`):
   - Right (`ForAction(Right)`) → route (A|default, Move Right); A frames
     its right neighbour as a direct `A.Move(Right)` would; B unchanged.
   - StepIn, `StepTowardPointer` true → route (B|default, ZoomIn, ZoomToward
     (2500,300)); with B in Overview → ZoomToward null; preference false →
     route (A|default, ZoomIn, null).
   - Split → pointer engine B; B focused w2 → `SplitKey = w2`; nothing
     focused on B → route with `SplitKey = null`, no engine call; pointer
     off all displays → null.
   - `GoToView(3)` → route (A|default, GoToView 3).
4. **Work-area refresh (unchanged signature).** t=20:
   `DisplaysChanged([A with work (0,0,1920,1000), B])` → nothing yet; t=20.3:
   a second identical snapshot (generation bumped) → at t=20.4 nothing;
   at t=20.7 `A.VisibleAreaChanged((0,0,1920,1000))`; B's area is equal →
   the engine ignores it.
5. **Display unplugged (changed signature).** Let A' = A with work area
   (0,0,1920,1000). t=10: `DisplaysChanged([A'])` → `IsTornDown`,
   `FramesNeeded` false. t=10.5: `DisplaysChanged([A'])` again → restarts.
   t=11.5: nothing. t=12.0: rebuild: A `ReleaseAll(null)` (its own visible
   area, still (0,0,1920,1040)), B `ReleaseAll((0,0,1920,1000))` (A''s work
   area, the fallback); both retired; `Engines` empty; `DisplayFrames` =
   [A frame]. t=12.0–12.8: `WindowAdded(w4)` only updates the mirror;
   `Execute` → null. t=12.8: new engine `A|default` created and started with
   every mirrored window (w2's frame is on no display → main A); it loads
   its saved record from the store.
6. **Scale change only.** B Dpi 144 → 168, same frames → signature differs
   → rebuild at +1.5 s with both engines `ReleaseAll(null)`; re-show at
   +2.3 s creates fresh A and B engines.
7. **Mixed debounce.** t=40 unchanged-looking snapshot (refresh due 40.4);
   t=40.2 a changed snapshot → the refresh never runs; rebuild at 41.7.
   Changed at t=30, then the *original* set again at t=30.5 (still torn
   down) → treated as changed → rebuild at 32.0 releasing both into
   `null` (both displays present), re-show at 32.8.
8. **Quit.** Engines A (w1 parked, w3 off-screen) and B (nothing selected
   for release). t=50 `BeginShutdown()`: A.Save, B.Save (both still started
   at each save); then A.ReleaseAll(null) issues 2 writes, B.ReleaseAll(null)
   schedules its completion with `After(0)`. `IsShutdownComplete` false.
   Advance(0) → B done (1 left). Pump the window system → A's 2 completions
   → complete at t=50: `ShutdownCompleted` once. Variant: never pump → at
   t=52.0 the deadline completes; a later pump raises nothing.

## Edge cases & errors

- Factory returns null or an engine with another key →
  `InvalidOperationException` from the call that needed the engine (Ready,
  re-show action, DesktopShown); engines created earlier in the same show
  remain.
- Factory or engine exceptions propagate out of the router call (no
  catching). A throwing handler of a router event propagates likewise.
- Empty display list: no engines are created on show; `DisplayFrames`
  empty; `ActiveEngine`/`PointerEngine` null; owner (B6) none, so
  `WindowAdded` only updates the mirror.
- A display snapshot whose work area is outside its frame is accepted (no
  cross-validation).
- Duplicate keys inside `Ready`'s list: the mirror keeps the first
  position, later info wins (same as engine B10).
- `WindowAdded` for a key already in the mirror replaces it in place (no
  duplicate).
- `LastFocusedKey` naming a window no engine holds → B23 step 2 skipped.
- Tick while an engine stops its own frames mid-loop: the snapshot is
  iterated; engines whose `FramesRunning` became false are skipped.
- `Execute` with `default(ShortcutCommand)` (Kind ToggleOverview, Action
  null) is treated as a view-style command (Active target) and calls
  `ToggleOverview()`; Core factories never produce it.
- Router events raised while an engine is delivering a callback: handlers
  must not call that engine (it throws, engine B7).
- `DisplaysChanged` from inside an engine callback is allowed (it calls no
  engine synchronously).
- `HotkeyTriggers.SetBindings(null!)` → `ArgumentNullException`, state
  unchanged.

## Acceptance criteria

Test rig (all [mac]): one `ManualClock`, one `FakeWindowSystem` (with
`Screens` = the display frames), one `FakeCapture`, `InMemoryLimitStore`,
`InMemoryDisplayStore`, and a factory that builds **real** `DisplayEngine`s:
`(key, d) => new DisplayEngine(key, d.WorkArea, EnginePolicy.Default, new
EngineServices(win, capture, clock, new RecordingRenderer(), limits,
store))`, recording each `(key, d)` call. Rationale: `DisplayEngine` is
sealed with no interface; the router's contract is stated in terms of real
engine state (`IsStarted`, `IsSuspended`, `Holds`, `FocusedKey`,
`VisibleArea`, `Phase`, `FramesRunning`, `ActiveProcessId`) and the real
re-entry guard; a recording fake would need an engine interface that no
other feature needs. Stub commands are observed through `CommandRoute` and
the internal `CommandInvoker`; internal engine members (`SetPhase`,
`SetParked`, `SetFramesRunning`, `FramedNodeId`, `FramingHistory`,
`LastTick`) are used via `InternalsVisibleTo`. The worked-example rig (A, B,
w1–w3) is used unless stated. Times compare within 1e-9.

### Core and records

- **AC1** [mac] `DisplaySignature.Of`:
  - `[("A",(0,0,1920,1080),96)]` → `"A@0,0,1920,1080@96"`;
  - `[("B",(1920,0,1706.6666666666667,960),144), ("A",(0,0,1920,1080),96)]`
    → `"A@0,0,1920,1080@96;B@1920,0,1706.6666666666667,960@144"`, also when
    run under `CultureInfo.CurrentCulture = de-DE`;
  - empty → `""`; null → `ArgumentNullException`; an entry with key `""` →
    `ArgumentException`. (B1)
- **AC2** [mac] `DisplayInfo.SignatureOf`: equal for two snapshots that
  differ only in `WorkArea`, only in `IsMain`, or only in order; different
  when a frame moves by 1, a size changes, a DPI changes, or a display is
  added/removed. (B2)
- **AC3** [mac] `DisplayInfo` validation: null key → ANE; frame or work
  area with NaN, ±∞ or negative width/height → `ArgumentOutOfRangeException`
  (also via `with`); `Dpi` 0 or −1 → AOORE; zero-size rects accepted. (API)
- **AC4** [mac] `RouterOptions.Default`: 1.5, 0.4, 0.8, 2.0;
  `ReferenceEquals(Default, Default)`; each value set to 0, −1, NaN or ∞
  throws AOORE; exactly four public instance properties. (API)

### Existing-type additions

- **AC5** [mac] Tracker identity, with `FakeWindowSource` facts handle 42
  (pid 7, tid 9) tracked and handle 43 not: `IdentityOf(w42) ==
  new TrackedIdentity(42,7,9)`; `IdentityOf(w43)` null; `KeyOf(42) == w42`;
  `KeyOf(43)` null; `KeyOf(0)` null; `IdentityOf(null!)` → ANE; before
  `Start` both return null; after the window is destroyed (removed) both
  return null; neither call adds an entry to the source's or target's
  call logs. (B41, B42)
- **AC6** [mac] `DisplayEngine.FocusLeftWindows`: started engine with w1
  focused → after `FocusLeftWindows(100)` `FocusedKey` null, exactly one
  `OnChange`, no new renderer plan, no store save, no window-system write
  or focus request; calling it with an unrelated pid has the same effect;
  suspended engine → `FocusedKey` unchanged; called from inside the
  engine's `OnChange` → `InvalidOperationException`. (B40)

### Construction, show, routing

- **AC7** [mac] Construction: no factory call, `ManualClock.PendingCount`
  0, `Engines` empty, `DisplayFrames` = [A frame, B frame],
  `DesktopOf(A) == "default"`, `DesktopOf(unknown)` null, flags false,
  `StepTowardPointer` true. Null displays/factory/clock/windows → ANE;
  duplicate display keys → `ArgumentException`. (B3, B21)
- **AC8** [mac] Ready (worked example 1): factory calls exactly
  `[(A|default, A), (B|default, B)]`; A `IsStarted` with `Windows` keys
  `[w1, w3]`, B with `[w2]`; `A.DisplayFrame == A.Frame`, `A.OtherDisplays
  == [B.Frame]`, `B.OtherDisplays == [A.Frame]`; `Started` raised once;
  `Changed` raised at least twice; a second `Ready` → no factory call, no
  event. With `AppActivated(100,true)` before `Ready`, each new engine has
  `ActiveProcessId == 100` and `DesktopOwnerActive == true`. A factory
  returning an engine keyed `"A|other"` → `InvalidOperationException`.
  (B4, B5, B7, B8, B10)
- **AC9** [mac] Owner display: a window (1800,100,400,300) (overlap 120 on
  A, 280 on B) → B; a window split exactly 50/50 → A (lower index); no
  overlap → main display (also when the main display is listed second);
  a key held by A whose frame is on B → A (holder first). (B6)
- **AC10** [mac] Per-window routing after Ready: `WindowAdded(w4 on B)` →
  B holds w4, A does not; `WindowMoved(w2, f)` → B's record frame `f`;
  `WindowTitleChanged(w2,"t")` → B's title; `WindowBecameDockable` reaches
  B; `WindowFocused(w2)` → `B.FocusedKey == w2`, `LastFocusedKey == w2`;
  `WindowRemoved(w2,false)` → B no longer holds it, `LastFocusedKey` null;
  `Release(w1)` → A no longer holds w1; calls for unknown keys change no
  engine. `Holds(w1)` true before release, false after. (B9, B11, B12)
- **AC11** [mac] Broadcasts: `AppActivated(200,false)` → both engines'
  `ActiveProcessId == 200`, `LastActiveProcessId == 200`;
  `FocusLeftWindows(100)` with A focused on w1 and B on w2 → both
  `FocusedKey` null, `LastFocusedKey` unchanged. (B13)
- **AC12** [mac] Mirror: after Ready, `WindowMoved(w2,(100,100,800,500))`,
  then a snapshot with A's frame moved by 1 (signature change) → at the
  re-show w2 is started on the new A engine (its mirrored frame is on A).
  A `WindowAdded` during the rebuild gap appears in the re-shown engine.
  (B9, B20)

### Active / pointer engine and commands

- **AC13** [mac] Active engine sequence of worked example 2, each bullet an
  assertion on `ActiveEngine` (reference equality). (B23)
- **AC14** [mac] Pointer engine: `EngineAt((1919.999,0))` → A,
  `EngineAt((1920,0))` → B, `EngineAt((3200,0))` → null,
  `EngineAt((0,1080))` → null; before Ready → null for every point.
  (B24)
- **AC15** [mac] Command routes of worked example 3 (exact `CommandRoute`
  values). For Move without an invoker: A's `FramingHistory.Count`
  increases by 1 and `FramedNodeId` changes, B's are unchanged (the rig
  makes sure A has a tile to the right of w1's panel, e.g. the desktop
  node inserted on the right at start). With a recording
  `CommandInvoker`, every `CommandKind` (the 12 actions + `GoToView(1)` +
  `SaveView(9)`) produces exactly one invoker call with the target given
  by `ShortcutCatalog.Info(action).Target` (Active for views) and no
  direct engine call (A's `FramingHistory` unchanged for Move). (B25–B27)
- **AC16** [mac] No target: before Ready, after shutdown, during the
  rebuild gap, and Split with the pointer off every display → `Execute`
  returns null and the invoker is not called. Calling `Execute(Right)` from
  inside A's `OnChange` → `InvalidOperationException` from A. (B25, B28,
  B34)

### Frames

- **AC17** [mac] Aggregation: `A.SetFramesRunning(true)` →
  `FramesNeededChanged(true)` once, `FramesNeeded` true; B on too → no
  event; A off → no event; B off → `FramesNeededChanged(false)`. A changed
  display snapshot while A is on → `FramesNeededChanged(false)` at once;
  after the re-show (fresh engines, frames off) no `true` event. (B29)
- **AC18** [mac] Tick: with A on and B off, `Tick(5.0)` → A
  `LastTick == 5.0`, B `LastTick` unchanged; `Tick(NaN)` → AOORE; while
  torn down and after shutdown `Tick` changes no `LastTick`. (B31)

### Display changes

- **AC19** [mac] Unchanged path (worked example 4): exact times — no
  `VisibleArea` change before t=20.7; at 20.7 `A.VisibleArea ==
  (0,0,1920,1000)`; `Displays` updated; no factory call and no new
  window-system write. (B14, B16, B17)
- **AC20** [mac] Changed path (worked example 5; the test sets
  `FakeWindowSystem.Screens = [A frame]` at t=10 so w2 is 0 % visible and
  is selected for release; w3 is off-screen throughout): `IsTornDown` true
  from t=10; no new write at 11.5; at 12.0 the new release writes are
  exactly w3 → `ReleaseGeometry.Cascade((0,0,1920,1040), (800,600), 0)` (A,
  own area) and w2 → `Cascade((0,0,1920,1000), (800,500), 0)` (B,
  fallback); `EngineRetired` raised for A then B; `Engines` empty;
  `Changed` raised; `DisplayFrames == [A frame]`. At 12.8: one factory call
  `(A|default, A')`, the new engine started with all mirrored windows,
  `IsTornDown` false, and the store was asked to `Load(A|default)` again.
  Callbacks of retired engines no longer reach router events. (B15, B18,
  B20)
- **AC21** [mac] DPI-only change (worked example 6): both engines
  `ReleaseAll(null)` (targets in their own work areas), re-show creates two
  engines. (B2, B18)
- **AC22** [mac] Debounce (worked example 7): both variants with exact
  times; three changed snapshots at t=0, 0.5, 1.0 → exactly one rebuild, at
  t=2.5, using the t=1.0 snapshot. (B14–B16, A2)
- **AC23** [mac] Ready while torn down: snapshot change at t=1, `Ready`
  at t=1.2 → no factory call until the re-show at t=3.3; `Started` raised
  then. (B10, B8, A11)

### Desktop seam

- **AC24** [mac] `DesktopShown(A, "d2")` after Ready → old `A|default`
  suspended; new engine `A|d2` created and started with A's windows;
  `EngineFor(A)` is the new engine; `DesktopOf(A) == "d2"`; back to
  `"default"` → `A|d2` suspended, `A|default` resumed (not re-created,
  factory call count unchanged, `IsSuspended` false); same id again →
  nothing; unknown display → nothing; `"a|b"` or `""` → `ArgumentException`.
  (B22, B7)

### Quit

- **AC25** [mac] Worked example 8 (parking via internal `SetParked` after
  the start landing has been pumped): a wrapping `IDisplayStore` records,
  at each `Save` during shutdown, that every engine still has `IsStarted ==
  true`; both engines are saved before any release write; completion
  timings and the single `ShutdownCompleted` exactly as described. Deadline
  variant: completes at t=52.0; additionally, with a clock wrapper that
  drops `After` actions scheduled by the router, `IsShutdownComplete` reads
  false at t=51.999 and true at t=52.0 and `ShutdownCompleted` is not
  raised. (B32, B33)
- **AC26** [mac] Shutdown details: second `BeginShutdown` → no new save or
  release; zero engines (before Ready) → `ShutdownCompleted` raised inside
  the call; a pending rebuild at shutdown time never runs; after shutdown
  `Ready`, routed calls, `DisplaysChanged`, `DesktopShown`, `Tick` cause no
  engine call or factory call; with display B removed in a pending
  snapshot, B's engine releases into the pending snapshot's main work area.
  (B32–B34, A7)

### Hotkey triggers

- **AC27** [mac] `HotkeyTriggers(ShortcutProfiles.WinAlt)`: no event
  before `Start`; `SetBindings`/`SettingsOpened`/`Retry` before Start → no
  event but state updated; `Start()` → one request `(Start,
  HotkeyPlan.Desired(Table,false))`, a second `Start` → none;
  `BeginRecording` → `(RecordingStarted, [])`, again → none;
  `Pressed(any bound combo)` while recording → null; `EndRecording` →
  `(RecordingStopped, full list)`; `SetBindings(CtrlAlt)` →
  `(BindingsChanged, CtrlAlt desired)` and `Table` rebuilt;
  `SettingsOpened` and `Retry` → one request each with the current list;
  after rebinding to CtrlAlt, `Pressed(WinAlt Right combo)` → null and
  `Pressed(CtrlAlt Right combo)` → `ForAction(Right)`; `Stop` → `(Stop,
  [])`, then every trigger → no event and `Pressed` → null; `Stop` before
  `Start` → no event. (B35–B38)

### Architecture

- **AC28** [mac] Reflection/text checks:
  - `EngineRouter` implements `IWindowTrackerTarget`; `CanvasWm.Engine`
    still references only `CanvasWm.Core`; `CanvasWm.Core` references no
    Engine/Win32 assembly;
  - no public type or member name in `CanvasWm.Engine.Routing` contains
    `Gate`, `Locked`, `Lesson`, `Upgrade`, `Licen`, `Trial`, `Welcome` or
    `Onboard` (case-sensitive) — the exclusion rule;
  - the four `RouterOptions` default values appear as numeric literals
    only in `RouterOptions.cs` (no `1.5`, `0.4`, `0.8`, `2.0` literal in
    other `Routing/*.cs` files);
  - the existing clean-room word scans (engine-interfaces AC28,
    engine-inbound-api AC34) still pass. (Placement; scope)

## Assumptions

- **A1 (factory returns real engines; no engine interface).** The factory
  is `Func<EngineKey, DisplayInfo, DisplayEngine>`; tests use real engines
  with fakes (see AC rig). Introducing `IDisplayEngine` would change an
  existing type for test convenience only.
- **A2 (cancellation by generation counter).** `IClock.After` has no
  cancel, so each `DisplaysChanged` and `BeginShutdown` increments one
  generation; stale actions check it and do nothing. Changed and unchanged
  paths share the counter, so a newer notification of either kind cancels
  any older pending action (studied: "cancel any pending display work").
- **A3 (one work-area refresh, not two).** The study observed 0.4 s and
  1.2 s constants on the unchanged path (exact schedule Inferred). The
  router does not read the OS; a second refresh would reapply the same
  snapshot, so it would be useless. The App sends a fresh snapshot for each
  `WM_SETTINGCHANGE`/`WM_DISPLAYCHANGE` (#33). *Needs review if Windows
  delivers a work-area change late without a further message.*
- **A4 (signature format, no escaping, sorted).** Sorting by key (the study
  joins in screen order) avoids a needless rebuild when enumeration order
  changes. Keys containing `@` or `;` could in theory collide; device paths
  do not contain them. DPI is a CanvasWM addition (M8).
- **A5 (Split argument).** *Needs human review.* The engine method takes a
  window key (stub until #13); the router passes the pointer engine's
  `FocusedKey` and makes no call when there is none. Opening a new
  file-manager window after a Split route is App work (#34, findings N12);
  the study passed the file manager's app id instead.
- **A6 (owner display without saved-owner lookup).** Holder first, then
  largest overlap, then the main display. The studied desktop→display map
  and "saved layout that docks it" steps need #35 and #24. Off-screen
  windows go to the main display.
- **A7 (quit `into`).** *Needs human review.* Studied value Unknown. Null
  (each engine's own work area) for engines whose display is still present
  in the latest snapshot, else the main display's work area — the same rule
  as the rebuild, so a quit during a pending unplug does not strand
  windows off-screen.
- **A8 (router keeps LastFocusedKey on FocusLeftWindows).** Active-engine
  step 2 already checks that the last focused window belongs to the last
  active app, which is what the study tests; only removal/release clears
  it.
- **A9 (active engine step 2 with no activation yet).** When
  `LastActiveProcessId` is null, the holder of the last focused window
  wins (no app to compare with).
- **A10 (frames off while torn down).** Mirrors the studied immediate stop
  of every frame timer on a changed display set; ticking resumes only after
  fresh engines request frames.
- **A11 (Ready during torn-down state is deferred).** Starting engines that
  will be released ≤ 1.5 s later would only cause window churn; the
  post-rebuild re-show starts them. The studied code's behaviour for this
  overlap was not traced.
- **A12 (FocusLeftWindows placeholder = clear FocusedKey only).** Human
  decision on #29. No render, so the overlay's focus flag may stay stale
  until the next render; acceptable while the overlay is a null renderer
  (#16).
- **A13 (HotkeyTriggers in Engine.Routing).** The trigger logic is policy
  (when to re-apply), so it stays out of Win32 and out of App code-behind,
  and is Mac-testable. It uses Core shortcut types only. `SetBindings`
  always raises (Apply is idempotent).
- **A14 (events, not Action properties).** Router outbound notifications
  are C# events (several App parts listen: tray, tick driver, hotkeys);
  the engine keeps its single-handler properties.
- **A15 (no router-level save on display rebuild).** Engines persist on
  every change (#27); the study releases without saving.

### Reported for review (no change made to earlier specs)

- `specs/engine-inbound-api.md`: add `FocusLeftWindows(int)` to the engine
  API (B40 here); `StubsArchitectureTests` (AC32 there) is unaffected
  (it checks containment, not an exact set).
- `specs/window-tracker.md`: add `IdentityOf`, `KeyOf`, `TrackedIdentity`
  (B41/B42); resolves its "Reported for review" item on
  `FocusLeftWindows` (mapped to the engine placeholder).
- `specs/win32-frame-writer.md` R4: closed by B41–B43 (App adapter in #33).
- `specs/shortcut-model.md` A9 (Split argument): decided here as A5,
  pending human review.
- `specs/win32-hotkeys.md` A5 (retry policy): implemented as
  `HotkeyTriggers` (no periodic retry).

## Traceability

| Spec item | Findings rule / decision |
|---|---|
| B1, B2, AC1–AC2, A4 | rule 10; M8 (DPI addition) |
| B3, AC7 | rule 6e (display list built, no engine) |
| B4, B5, AC8 | rules 8, 9, 16; #32 scope (single desktop, `SingleDesktopId`) |
| B6, AC9, A6 | rule 17 (owner decision), M3 |
| B7, B8, B10, AC8, AC23, A11 | rules 6f, 7 |
| B9, AC12 | rules 7, 12 (re-show with tracked windows); M3 |
| B11–B13, AC10, AC11, A8 | rule 17 |
| B14–B17, AC19, AC22, A2, A3 | rules 11, 13; worked ex. 4, 6 |
| B18–B21, AC20, AC21, A10, A15 | rules 11, 12; worked ex. 5; P ex. 3, 4 |
| B22, AC24 | rules 14, 15; O3; #32 scope (seam for #35) |
| B23, B24, AC13, AC14, A9 | rules 18, 19; worked ex. 8; P ex. 5 |
| B25–B28, AC15, AC16, A5 | rule 20; #7 B1, B3, B4, A9; N12 |
| B29–B31, AC17, AC18 | rules 21, 22; N6 (single driver) |
| B32–B34, AC25, AC26, A7 | rule 27; worked ex. 9; N8 |
| B35–B38, AC27, A13 | rule 5; N7; #12 A5, B17 |
| B40, AC6, A12 | M1; #29 human decision (placeholder) |
| B41–B43, AC5 | M2; #11 R4 |
| AC28 | CLAUDE.md layering and clean-room rules; #32 scope (licensing/onboarding excluded) |

## Corrections from the test stage (human-approved)

- **AC19:** read "no new window-system write" as "the router itself makes no
  write". The engine's own rule (engine-inbound-api B26) reframes on
  `VisibleAreaChanged`, so the 20.7 refresh writes A's two windows (w1, w3)
  once; nothing is written for B, and the duplicate notification adds no
  second reframe.
- **AC15 rig:** the started layout places the empty desktop to the *left* of
  w1's panel, so w1 has no right neighbour. The test first frames the desktop
  with `A.Move(Left)`; `Execute(Right)` then adds exactly one framing to A and
  leaves B unchanged.
- **AC20, AC21, AC25 rigs:** after the start landing is pumped, w3 lands
  on-screen and is not selected for release. AC20/AC21 do not pump the
  landing (matching the described writes); AC21 additionally parks w2 on B to
  give B an observable release; AC25 parks w1 and w3 explicitly. All timings
  are unchanged.
- **AC3:** `DisplayInfo` validation must also apply via `with` (init
  accessors validate) — fixed in the developer stage after the test run.

