# Spec: window-tracker

## Goal

Decide **which OS windows CanvasWM manages** and **turn raw OS window
notifications into the Engine's inbound window calls** without any Win32 code,
so that all of the policy can be tested on the Mac.

- **Core** gets a raw-facts record (`OsWindowFacts`) that holds
  platform-neutral facts about one top-level window. It also gets a pure
  classifier. The classifier answers *Ignore* or *Managed(dockable)* for a
  candidate window and *Keep / remove temporarily / remove permanently* for a
  window that is already tracked. The policy inputs are a minimum size of
  120×80 and an app exclusion list.
- **Engine** gets an OS seam, `IWindowSource`, which enumerates facts in
  z-order, describes one window and delivers a stream of raw events. Engine
  also gets a `WindowTracker` state machine. The tracker owns the tracked set,
  the focus de-duplication, the one-way dockable upgrade, the per-app delayed
  re-checks (0.05 s and 0.4 s), the 3 s safety rescan, start readiness, the
  admission check and the change-only move filter. It emits its decisions as
  calls on an injected **target** interface, `IWindowTrackerTarget`. A later
  feature (#29) implements that interface and routes each call to the right
  per-display `DisplayEngine`.
- The fakes `FakeWindowSource` and `RecordingTrackerTarget` are part of this
  deliverable.

Out of scope:
- The Win32 implementation of `IWindowSource` (#10).
- Routing to per-display/per-desktop engines, and engine start/suspend/resume
  on virtual-desktop switches (#29, #24).
- A settings UI for the exclusion list.
- Live-capture side effects (the studied tracker tells capture about resizes
  and title changes; see "Reported for review").
- The studied 0.3 s app-launch delay, the 1.0 s per-app query timeout and the
  0.6 s ready fallback (A8). Windows has no app-launch notification. Hang
  safety while gathering facts is the adapter's job (#10 notes).
- Pointer-drag brackets (move/size start/end). They belong to the native-drag
  feature, which will add event kinds.

## Placement

- **Core**: `src/CanvasWm.Core/Windows/` with namespace
  **`CanvasWm.Core.Windows`**. Files: `OsWindowFacts.cs`, `CloakKind.cs`,
  `ClassifierPolicy.cs`, `WindowClassification.cs` (with `IgnoreReason`),
  `TrackedFate.cs`, `WindowClassifier.cs`. Pure code, BCL only
  (`System.Collections.Immutable` is in the shared framework). No reference
  changes.
  - Name clash avoided: `CanvasWm.Core.Projection.WindowFacts` (#25) is
    unrelated and stays unchanged.
- **Engine**:
  - `src/CanvasWm.Engine/Services/IWindowSource.cs` holds `IWindowSource`,
    `RawWindowEvent` and `RawWindowEventKind`, in namespace
    `CanvasWm.Engine.Services`. It sits there with the other OS seams, per the
    updated CLAUDE.md interface list.
  - `src/CanvasWm.Engine/Tracking/{WindowTracker.cs, IWindowTrackerTarget.cs,
    TrackerOptions.cs}` use namespace **`CanvasWm.Engine.Tracking`**.
- **Fakes**: `tests/CanvasWm.Engine.Tests/Fakes/FakeWindowSource.cs` and
  `RecordingTrackerTarget.cs`, in namespace `CanvasWm.Engine.Tests.Fakes`.
  These are public classes.
- **Tests**:
  - `tests/CanvasWm.Core.Tests/Windows/*Tests.cs`, in namespace
    `CanvasWm.Core.Tests.WindowsTests`.
  - `tests/CanvasWm.Engine.Tests/Tracking/*Tests.cs`.
  - `tests/CanvasWm.Engine.Tests/Fakes/FakeWindowSourceTests.cs`.
- **Reuses, unchanged**:
  - From Core: `CanvasWm.Core.Geometry.Rect` and `Size`.
  - From Engine (specs/engine-interfaces.md): `WindowKey`, `AppId`,
    `WindowInfo` (including `Dockable`), `IClock`, `IWindowSystem`
    (`VisibleFraction` only).
  - From Engine tests: `ManualClock` and `FakeWindowSystem` (for
    `Screens`-based `VisibleFraction`).
  - The target's method set mirrors the `DisplayEngine` inbound methods from
    specs/engine-inbound-api.md and specs/engine-window-events.md
    (`WindowAdded`, `WindowRemoved(key, temporary)`, `WindowFocused`,
    `WindowMoved`, `WindowTitleChanged`, `WindowBecameDockable`,
    `AppActivated`, `Release`, `Holds`). It adds `FocusLeftWindows` and
    `Ready`.
- **No existing type in Core or Engine is changed.** `DisplayEngine`,
  `EnginePolicy`, `Tuning` and `WindowInfo` are untouched. The tracker's
  constants live in `ClassifierPolicy` (Core) and `TrackerOptions` (Engine),
  not in `Tuning` (A11).
- Coordinates are in DIPs, y-down, with the origin at the top-left of the
  virtual desktop. Times are in seconds on the `IClock.Now` base.
- No text in `src/` or `tests/` may contain the words `TrellisWM` or
  `Licensing`.

## Public API

All public members carry XML docs. A non-nullable reference parameter that is
null throws `ArgumentNullException` before any state change.

### Core — `CanvasWm.Core.Windows`

```csharp
namespace CanvasWm.Core.Windows;

/// <summary>How a window is hidden by the compositor. Windows meanings: Shell = cloaked by the
/// shell (another virtual desktop, or shell-hidden); App = cloaked by its own app (e.g. a
/// suspended packaged app); Inherited = inherits its owner's cloak.</summary>
public enum CloakKind { None = 0, Shell = 1, App = 2, Inherited = 3 }

/// <summary>Raw, platform-neutral facts about one top-level OS window, as gathered by an
/// IWindowSource adapter. The adapter reports facts only and never judges them. Immutable;
/// compiler-generated value equality. Every bool defaults to false, so a default instance is
/// "nothing known" and is ignored by the classifier.</summary>
public sealed record OsWindowFacts
{
    /// <summary>OS window identity (Windows: the HWND as an unsigned integer). 0 = invalid.</summary>
    public ulong Handle { get; init; }
    /// <summary>Owning process id.</summary>
    public int ProcessId { get; init; }
    /// <summary>Owning thread id (used with ProcessId to detect a reused handle).</summary>
    public int ThreadId { get; init; }
    /// <summary>Application identity (Windows: lower-cased image path, or the AUMID for packaged
    /// apps), or null when unknown.</summary>
    public string? AppId { get; init; }
    /// <summary>Application display name, or null when unknown.</summary>
    public string? AppName { get; init; }
    /// <summary>Window title; empty when unreadable. Never null.</summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    public string Title { get; init; } = "";
    /// <summary>Frame in DIPs (Windows: DWM extended frame bounds), or null when unreadable.</summary>
    public Rect? Frame { get; init; }

    /// <summary>The window belongs to CanvasWM's own process.</summary>
    public bool IsOwnProcess { get; init; }
    /// <summary>The window is a shell surface: the desktop or a taskbar
    /// (Windows classes Progman, WorkerW, Shell_TrayWnd, Shell_SecondaryTrayWnd).</summary>
    public bool IsShellWindow { get; init; }
    /// <summary>The window's process owns the desktop (Windows: the process of the shell desktop
    /// window, normally explorer.exe). Passed to AppActivated.</summary>
    public bool OwnsDesktop { get; init; }
    /// <summary>The window is its own root (Windows: GetAncestor(GA_ROOT) == hwnd).</summary>
    public bool IsTopLevel { get; init; }
    /// <summary>Child-window style (Windows: WS_CHILD).</summary>
    public bool IsChild { get; init; }
    /// <summary>The window has an owner window (Windows: GW_OWNER non-null): dialogs, property
    /// sheets, find boxes, owned popups.</summary>
    public bool IsOwned { get; init; }
    /// <summary>Forced into the task switcher despite being owned (Windows: WS_EX_APPWINDOW).</summary>
    public bool IsAppWindow { get; init; }
    /// <summary>Tool/palette window (Windows: WS_EX_TOOLWINDOW).</summary>
    public bool IsToolWindow { get; init; }
    /// <summary>Never activated by clicks (Windows: WS_EX_NOACTIVATE).</summary>
    public bool IsNoActivate { get; init; }
    /// <summary>Has a title bar (Windows: WS_CAPTION).</summary>
    public bool HasCaption { get; init; }
    /// <summary>The user can resize it by its border (Windows: WS_THICKFRAME).</summary>
    public bool IsResizable { get; init; }
    /// <summary>Has a maximize button (Windows: WS_MAXIMIZEBOX).</summary>
    public bool HasMaximizeButton { get; init; }
    /// <summary>The maximize command is enabled (Windows: SC_MAXIMIZE not greyed in the system
    /// menu); null = could not be read (counts as enabled).</summary>
    public bool? MaximizeEnabled { get; init; }
    /// <summary>Shown (Windows: IsWindowVisible).</summary>
    public bool IsVisible { get; init; }
    /// <summary>Compositor cloak state (Windows: DWMWA_CLOAKED).</summary>
    public CloakKind Cloak { get; init; }
    /// <summary>Minimised (Windows: IsIconic).</summary>
    public bool IsMinimized { get; init; }
    /// <summary>Exclusive or borderless full screen (Windows: frame covers its monitor's full rect,
    /// no caption, not maximised — per specs/win32-window-enum.md). A maximised window is NOT
    /// full screen.</summary>
    public bool IsFullScreen { get; init; }
    /// <summary>The window's thread is not responding (Windows: IsHungAppWindow).</summary>
    public bool IsHung { get; init; }
    /// <summary>CanvasWM may move and resize it (Windows: false when UIPI blocks SetWindowPos,
    /// e.g. an elevated window while CanvasWM is not elevated).</summary>
    public bool IsAccessible { get; init; }
}

/// <summary>Policy inputs of the classifier. Immutable; change with a with-expression.</summary>
public sealed record ClassifierPolicy
{
    /// <summary>Minimum 120 x 80, no exclusions. The same instance every time.</summary>
    public static ClassifierPolicy Default { get; }
    /// <summary>Smallest managed frame, DIPs, inclusive on both axes. Default 120 x 80.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A component is negative or not finite.</exception>
    public Size MinimumSize { get; init; }
    /// <summary>App ids whose windows are never managed. Ordinal, case-sensitive. Default empty.
    /// The init accessor copies the input into an ordinal set.</summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    /// <exception cref="ArgumentException">An element is null or empty.</exception>
    public IImmutableSet<string> ExcludedApps { get; init; }
    /// <summary>Convenience: a copy of this policy with the given exclusions.</summary>
    public ClassifierPolicy WithExcludedApps(IEnumerable<string> appIds);
    // Equality: MinimumSize equal AND ExcludedApps.SetEquals; hash order-insensitive.
}

/// <summary>Why a window is ignored. The values are listed in check order (B2).</summary>
public enum IgnoreReason
{
    None = 0, NoHandle, OwnProcess, ShellWindow, ExcludedApp, NotTopLevel, NotStandard,
    Hidden, Cloaked, Minimized, FullScreen, Hung, Inaccessible, NoFrame, TooSmall
}

/// <summary>Result of classifying a candidate: Ignore(reason) or Managed(dockable).</summary>
public readonly record struct WindowClassification
{
    public static WindowClassification Managed(bool dockable);   // Reason = None
    /// <exception cref="ArgumentOutOfRangeException">reason is None or undefined.</exception>
    public static WindowClassification Ignore(IgnoreReason reason); // Dockable = false
    public bool IsManaged { get; }
    public bool Dockable { get; }
    public IgnoreReason Reason { get; }
}

/// <summary>What to do with an already tracked window given its current facts.</summary>
public enum TrackedFate { Keep = 0, RemoveTemporary = 1, RemovePermanent = 2 }

/// <summary>Pure window classification rules.</summary>
public static class WindowClassifier
{
    /// <summary>Ignore or Managed(dockable) for a window that is not tracked yet (B2-B5).</summary>
    public static WindowClassification Classify(OsWindowFacts facts, ClassifierPolicy policy);
    /// <summary>True when the window may be tiled: resizable, has a maximize button, and the
    /// maximize command is enabled or unknown (B4).</summary>
    public static bool IsDockable(OsWindowFacts facts);
    /// <summary>Fate of a tracked window; current = null means the window no longer exists (B6).</summary>
    public static TrackedFate Fate(OsWindowFacts? current, ClassifierPolicy policy);
}
```

### Engine — the OS seam (`CanvasWm.Engine.Services`)

```csharp
namespace CanvasWm.Engine.Services;

/// <summary>Kinds of raw window notifications (Windows mapping in the #10 notes).</summary>
public enum RawWindowEventKind
{
    /// <summary>A window was created.</summary>          Created,
    /// <summary>A window was shown.</summary>            Shown,
    /// <summary>A window was uncloaked.</summary>        Uncloaked,
    /// <summary>A window left the minimised state.</summary> Restored,
    /// <summary>A window was destroyed.</summary>        Destroyed,
    /// <summary>A window was minimised.</summary>        Minimized,
    /// <summary>A window was hidden.</summary>           Hidden,
    /// <summary>A window was cloaked.</summary>          Cloaked,
    /// <summary>A window's position or size changed.</summary> LocationChanged,
    /// <summary>A window's title changed.</summary>      TitleChanged,
    /// <summary>Keyboard focus moved inside or to the top-level window Handle.</summary> FocusChanged,
    /// <summary>Handle became the foreground window (its app is now active).</summary> Foreground,
}

/// <summary>One raw notification. Handle is a top-level window handle (0 = none, only
/// meaningful for Foreground/FocusChanged).</summary>
public readonly record struct RawWindowEvent(RawWindowEventKind Kind, ulong Handle);

/// <summary>The OS window list and its notifications. Reports facts only: no thresholds,
/// no exclusions, no removal decisions. All members are called on the engine (UI) thread.</summary>
public interface IWindowSource
{
    /// <summary>Facts for every top-level window currently in the system, frontmost first
    /// (including hidden, minimised and cloaked windows). Never null; a new snapshot each call.
    /// Never throws for OS failures (an unreadable window is omitted or reported with null
    /// fields).</summary>
    IReadOnlyList<OsWindowFacts> Enumerate();

    /// <summary>Facts for one window, or null if no window with that handle exists. Never
    /// throws for OS failures.</summary>
    OsWindowFacts? Describe(ulong handle);

    /// <summary>Starts delivering raw events to handler. Events arrive on the engine thread,
    /// in OS order, and never synchronously inside any IWindowSource call. Disposing the result
    /// stops delivery; disposing twice does nothing.</summary>
    IDisposable Subscribe(Action<RawWindowEvent> handler);
}
```

### Engine — tracker (`CanvasWm.Engine.Tracking`)

```csharp
namespace CanvasWm.Engine.Tracking;

/// <summary>Receives the tracker's decisions. #29 implements it by routing each call to the
/// DisplayEngine(s) concerned. All calls are made on the engine thread.</summary>
public interface IWindowTrackerTarget
{
    /// <summary>Full frames of every display, used for the admission centre test.</summary>
    IReadOnlyList<Rect> DisplayFrames { get; }
    /// <summary>True if any engine currently holds key (e.g. parked by an earlier session).</summary>
    bool Holds(WindowKey key);
    /// <summary>Exactly once: the initial scan finished; windows = tracked set, admission order.</summary>
    void Ready(IReadOnlyList<WindowInfo> windows);
    void WindowAdded(WindowInfo info);
    void WindowRemoved(WindowKey key, bool temporary);
    void WindowFocused(WindowKey key);
    /// <summary>Focus moved to an unmanaged window (or no window) of processId.</summary>
    void FocusLeftWindows(int processId);
    void WindowMoved(WindowKey key, Rect frame);
    void WindowTitleChanged(WindowKey key, string title);
    void WindowBecameDockable(WindowKey key);
    void AppActivated(int processId, bool ownsDesktop);
    /// <summary>Hand the window back to the OS and stop managing it (newly excluded app).</summary>
    void Release(WindowKey key);
}

/// <summary>Tracker timings and thresholds. Immutable; with-expressions.</summary>
public sealed record TrackerOptions
{
    public static TrackerOptions Default { get; }                 // same instance every time
    /// <summary>First delayed re-check after a may-have-appeared event, s. Default 0.05.</summary>
    public double FirstRecheckSeconds { get; init; }              // 0.05
    /// <summary>Second delayed re-check, s. Default 0.4.</summary>
    public double SecondRecheckSeconds { get; init; }             // 0.4
    /// <summary>Period of the full safety rescan, s. Default 3.0.</summary>
    public double RescanSeconds { get; init; }                    // 3.0
    /// <summary>A window whose centre is on no display and that no engine holds is admitted only
    /// when its visible fraction is strictly below this. Default 0.05.</summary>
    public double OffScreenAdmitBelow { get; init; }              // 0.05
    /// <summary>AppName used when the facts carry none. Default "App".</summary>
    public string FallbackAppName { get; init; }                  // "App"
    // Validation (ArgumentOutOfRangeException, ParamName = property name): the three *Seconds
    // must be finite and > 0; OffScreenAdmitBelow must be in [0, 1] (NaN rejected);
    // FallbackAppName null → ArgumentNullException, "" → ArgumentException.
}

/// <summary>Tracks the manageable OS windows and reports changes to the target. Single-threaded
/// (engine thread). Not reusable after Dispose.</summary>
public sealed class WindowTracker : IDisposable
{
    /// <summary>"w" + handle in decimal (invariant culture), e.g. 4211 → "w4211".</summary>
    /// <exception cref="ArgumentOutOfRangeException">handle is 0.</exception>
    public static WindowKey KeyFor(ulong handle);

    /// <summary>Creates an idle tracker. Makes no call on any dependency.</summary>
    /// <param name="windows">Used only for VisibleFraction in the admission test.</param>
    /// <param name="options">null = TrackerOptions.Default.</param>
    public WindowTracker(IWindowSource source, IWindowSystem windows, IClock clock,
                         IWindowTrackerTarget target, ClassifierPolicy policy,
                         TrackerOptions? options = null);

    public ClassifierPolicy Policy { get; }
    public TrackerOptions Options { get; }
    /// <summary>True after Start (until Dispose).</summary>
    public bool IsStarted { get; }
    /// <summary>True once Ready has been raised.</summary>
    public bool IsReady { get; }
    /// <summary>Snapshot of the tracked windows in admission order (current stored info).</summary>
    public IReadOnlyList<WindowInfo> Tracked { get; }
    /// <summary>Stored info for key, or null if not tracked.</summary>
    public WindowInfo? Find(WindowKey key);

    /// <summary>Subscribe, run the initial full scan, raise Ready, arm the rescan timer (B9).
    /// Second call: no-op.</summary>
    /// <exception cref="ObjectDisposedException">After Dispose.</exception>
    public void Start();
    /// <summary>Run a full scan now (e.g. after a virtual-desktop switch). No-op before Start or
    /// after Dispose.</summary>
    public void Rescan();
    /// <summary>Replace the classifier policy; after Start, release windows of newly excluded
    /// apps and run a full scan (B21).</summary>
    public void SetPolicy(ClassifierPolicy policy);
    /// <summary>Unsubscribe; later timer callbacks and events do nothing. Idempotent. Makes no
    /// target call.</summary>
    public void Dispose();
}
```

### Fakes (`tests/CanvasWm.Engine.Tests/Fakes`)

```csharp
namespace CanvasWm.Engine.Tests.Fakes;

public sealed class FakeWindowSource : IWindowSource
{
    // --- scene (immediate) ---
    public void Put(OsWindowFacts facts);      // add (as frontmost) or replace by Handle (keeps z position)
    public void Remove(ulong handle);          // window no longer exists; unknown: no-op
    public void SetZOrder(IEnumerable<ulong> frontToBack); // listed first, then the rest in previous order
    public OsWindowFacts? Get(ulong handle);
    public void Update(ulong handle, Func<OsWindowFacts, OsWindowFacts> change); // KeyNotFoundException if unknown
    // --- events ---
    public void Raise(RawWindowEvent e);       // delivers synchronously to every live subscriber, in subscription order
    public void Raise(RawWindowEventKind kind, ulong handle);
    public int SubscriberCount { get; }
    // --- recording ---
    public int EnumerateCount { get; }
    public int DescribeCount { get; }
    public int MaxEnumerateDepth { get; }      // deepest nesting of Enumerate calls observed (re-entrancy probe)
    public Action? OnEnumerate { get; set; }   // test hook run inside Enumerate, before it returns
}

public sealed class RecordingTrackerTarget : IWindowTrackerTarget
{
    public RecordingTrackerTarget(IClock clock);
    public List<Rect> Displays { get; }        // default [(0,0,1440,900)]; DisplayFrames returns a copy
    public HashSet<WindowKey> Held { get; }    // Holds(key) = Held.Contains(key)
    public IReadOnlyList<TrackerCall> Calls { get; } // every method call except DisplayFrames/Holds, in order
    public IReadOnlyList<WindowInfo>? ReadyWindows { get; }  // argument of the last Ready
    public Action<TrackerCall>? OnCall { get; set; }  // invoked after recording (re-entrancy tests)
    public void Clear();                        // clears Calls
}

/// <summary>Name = method name ("WindowAdded", "WindowRemoved", ...); Key/Info/Frame/Title/
/// ProcessId/Flag carry the arguments that method has (Flag = temporary or ownsDesktop);
/// At = clock time when recorded.</summary>
public sealed record TrackerCall(string Name, WindowKey? Key = null, WindowInfo? Info = null,
                                 Rect? Frame = null, string? Title = null, int? ProcessId = null,
                                 bool? Flag = null, double At = 0);
```

The developer may add helpers but must not change the behaviour above.

## Behaviour

### Core classifier

- **B1 (facts).** `OsWindowFacts` is a plain immutable record with
  compiler-generated equality. `Title` defaults to `""` and rejects null. No
  other member is validated: a zero handle, a null frame and non-finite
  frames are all legal facts, and the classifier is the one that judges them.
- **B2 (Classify).** `Classify(f, p)` runs the checks below **in this order**
  and returns `Ignore(reason)` for the **first** check that fails. If every
  check passes, it returns `Managed(IsDockable(f))`.
  1. `NoHandle`: `f.Handle == 0`.
  2. `OwnProcess`: `f.IsOwnProcess`.
  3. `ShellWindow`: `f.IsShellWindow`.
  4. `ExcludedApp`: `f.AppId` is non-null and `p.ExcludedApps` contains it
     (ordinal). A null `AppId` is never excluded.
  5. `NotTopLevel`: `!f.IsTopLevel || f.IsChild`.
  6. `NotStandard`: the window is *not* standard. Standard means all of:
     `!f.IsToolWindow`, `!f.IsNoActivate`, `(!f.IsOwned || f.IsAppWindow)` and
     `(f.HasCaption || f.IsResizable)`.
  7. `Hidden`: `!f.IsVisible`.
  8. `Cloaked`: `f.Cloak != CloakKind.None`.
  9. `Minimized`: `f.IsMinimized`.
  10. `FullScreen`: `f.IsFullScreen`.
  11. `Hung`: `f.IsHung`.
  12. `Inaccessible`: `!f.IsAccessible`.
  13. `NoFrame`: `f.Frame` is null, or any of its four components is not
      finite.
  14. `TooSmall`: `Frame.Width < p.MinimumSize.Width` or
      `Frame.Height < p.MinimumSize.Height`. Both comparisons are inclusive
      at the minimum: exactly 120×80 is managed.
- **B3 (reasons).** `WindowClassification.Ignore(r)` has `IsManaged = false`,
  `Dockable = false` and `Reason = r`. `Managed(d)` has `IsManaged = true`,
  `Dockable = d` and `Reason = None`. `Ignore(None)` throws, and so does an
  undefined enum value.
- **B4 (IsDockable).** `IsDockable(f) = f.IsResizable && f.HasMaximizeButton &&
  (f.MaximizeEnabled ?? true)`. It does not depend on any other fact. A
  managed but non-dockable window, such as a fixed-size preferences window,
  is managed but floated by the engine.
- **B5 (policy).** `ClassifierPolicy.Default` has `MinimumSize = (120, 80)`
  and `ExcludedApps` empty. `ExcludedApps` is stored as an ordinal immutable
  set copied from the assigned value. Null or empty elements throw
  `ArgumentException`. Equality and hashing are value-based and do not depend
  on order. `MinimumSize` components must be finite and `>= 0`, otherwise
  `ArgumentOutOfRangeException` is thrown with `ParamName = "MinimumSize"`.
- **B6 (Fate).** `Fate(c, p)` applies to a tracked window. The tracker has
  already checked that `c` belongs to the same window (B19). The first
  matching rule decides:
  1. `c` is null → `RemovePermanent`. The window is gone.
  2. `c.IsOwnProcess || c.IsShellWindow`, or `c.AppId` is excluded →
     `RemovePermanent`.
  3. `c.IsHung` → `Keep`. A hung window is not judged until it recovers
     (A6).
  4. `c.IsMinimized` → `RemoveTemporary`.
  5. `!c.IsVisible` → `RemoveTemporary`.
  6. `c.Cloak == App` → `RemoveTemporary`.
  7. `c.Cloak` is `Shell` or `Inherited` → `Keep`. The window is on another
     virtual desktop, so it stays tracked (A5).
  8. `Classify(c, p).IsManaged` → `Keep`.
  9. Otherwise → `RemoveTemporary`. The window still exists but no longer
     qualifies, for example it entered full screen, shrank below the
     minimum, became a tool window or became inaccessible (A3, A4).

### Tracker: identity and records

- **B7 (key and record).**
  - `KeyFor(h)` is `"w" + h.ToString(CultureInfo.InvariantCulture)`.
  - **Admitting** facts `f` stores a record made of the `WindowInfo` and
    `f.ThreadId`. The `WindowInfo` is
    `(KeyFor(f.Handle), f.ProcessId, name, app, f.Title, f.Frame!.Value,
    Classify(f).Dockable)`, where:
    - `name` is `f.AppName`, unless that is null or empty, in which case it
      is `Options.FallbackAppName`;
    - `app` is `new AppId(f.AppId)`, unless `f.AppId` is null or empty, in
      which case it is null.
  - `Tracked` lists records in admission order. A removed and re-admitted
    window goes to the end.
  - The tracker updates its record **before** it makes the corresponding
    target call.
- **B8 (target-call gating).** During the initial scan inside `Start` (before
  `Ready`), admissions and dockable upgrades update records only, and no
  target call is made except `DisplayFrames` and `Holds`. After `Ready`,
  every admission calls `WindowAdded(info)`, every removal calls
  `WindowRemoved(key, temporary)` and every upgrade calls
  `WindowBecameDockable(key)`.

### Tracker: start, rescans, readiness

- **B9 (Start).** `Start()` does the following, in order:
  1. subscribe to the source;
  2. run one **full scan** (B11);
  3. set `IsReady` and call `target.Ready(Tracked)`. This is the only place
     Ready is raised, so it is raised exactly once, synchronously at the end
     of the first scan;
  4. call `clock.After(RescanSeconds, rescan)`. This is the only clock
     action `Start` schedules. No timer is scheduled for readiness (A8).
  - A second `Start()` does nothing. `Start()` after `Dispose()` throws
    `ObjectDisposedException`.
  - Before `Start`, no member touches the source, clock or target, apart
    from `SetPolicy` storing the policy.
- **B10 (periodic rescan).** Each time the rescan callback runs, it does a
  full scan and then re-arms `After(RescanSeconds, rescan)`. The first runs
  happen at Start + 3, + 6, + 9 and so on. `Rescan()` runs one extra full scan
  immediately and does not change the timer.
- **B11 (scan algorithm).** A scan has a scope: **all** windows, or one
  process id `pid`.
  1. Call `listed = source.Enumerate()` once. If the scope is a pid, keep
     only the facts whose `ProcessId == pid`. Keep the source order (front
     first).
  2. For each `f` in `listed`, in order, with `k = KeyFor(f.Handle)`:
     - Skip `f` if `f.Handle == 0`.
     - If `k` is tracked:
       - If the identity check (B19) fails, remove `k` permanently, then
         treat `f` as untracked below.
       - Otherwise compute `Fate(f)`:
         - `Keep` → run the dockable re-check (B17);
         - `RemoveTemporary` / `RemovePermanent` → remove `k` with that
           kind.
     - If `k` is not tracked and `Classify(f).IsManaged`, run the admission
       test (B12). If it passes, admit `f`.
  3. Then take every tracked window that is in scope but was **not** in
     `listed`. For the pid scope, in scope means the stored `ProcessId`
     equals `pid`. Go through them in admission order. For each one, call
     `c = source.Describe(handle)`:
     - `c` is null → remove permanently;
     - identity check fails → remove permanently;
     - otherwise apply `Fate(c)`. A `Keep` result runs the dockable
       re-check.
- **B12 (admission test).** For a frame `r` and key `k`:
  - Let the centre be `(r.X + r.Width/2, r.Y + r.Height/2)`. Accept if any
    `d` in `target.DisplayFrames` has `d.X ≤ cx < d.X + d.Width` and
    `d.Y ≤ cy < d.Y + d.Height` (half-open, A9).
  - Otherwise accept if `target.Holds(k)`.
  - Otherwise accept if `windows.VisibleFraction(r) < OffScreenAdmitBelow`
    (strict).
  - Otherwise reject. A rejected window is not recorded, and later scans
    test it again.
  - The tests run in this order and stop at the first that accepts.

### Tracker: event handling (after Start, before Dispose)

- **B13 (dispatch).** Every raw event goes through the following steps:
  - **Ignored entirely** if `Handle == 0`, except for `Foreground` and
    `FocusChanged` (B16).
  - The tracker first looks up whether `KeyFor(Handle)` has a record. This
    makes no OS call. For Destroyed, Minimized, Hidden, Cloaked,
    LocationChanged and TitleChanged on a handle **without** a record, it
    stops here: no `Describe`, no call.
  - Otherwise, for the kinds whose table row needs facts, the tracker calls
    `Describe(Handle)` **at most once** per event and reuses that result.
    If the result has `IsOwnProcess`, the event is ignored (A10).
  - The table below then applies. "Tracked" means `KeyFor(Handle)` has a
    record **and**, when `Describe` returned non-null facts, the identity
    check (B19) passes against them. If the identity check fails, the old
    record is removed permanently first and the window is treated as
    untracked. A null `Describe` result is handled by the row itself; for
    Hidden and Cloaked it is `Fate(null)`, which is a permanent removal.

  | Kind | Effect |
  |---|---|
  | Created, Shown, Uncloaked, Restored | B14 (schedule re-checks of the window's process); no immediate admission |
  | Destroyed | tracked → remove permanently (no Describe) |
  | Minimized | tracked → remove temporarily (no Describe) |
  | Hidden, Cloaked | tracked → `Fate(Describe(Handle))`; Keep does nothing else |
  | LocationChanged | B15 |
  | TitleChanged | tracked and Describe non-null → store `Title`, call `WindowTitleChanged(key, title)` every time, even if the title is unchanged |
  | FocusChanged | focus report with de-duplication (B16) |
  | Foreground | focus report without de-duplication, then `AppActivated` (B16) |

  An event about an untracked window does nothing, except for the
  appear-type kinds and the focus kinds.
- **B14 (delayed re-checks).** Handling of Created, Shown, Uncloaked and
  Restored:
  - `c = Describe(Handle)`. If `c` is null, do nothing. Otherwise
    `pid = c.ProcessId`.
  - For each slot `s` in {first, second}, with delays `FirstRecheckSeconds`
    and `SecondRecheckSeconds`: if no re-check for `(pid, s)` is pending,
    mark it pending and call `clock.After(delay_s, …)`.
  - When the callback runs, it clears the pending mark for `(pid, s)` and
    runs a pid-scope scan (B11).
  - So at most one re-check per (process, slot) is pending at any time (A2).
    Different processes are independent.
  - Re-checks never run nested inside another scan (B20).
- **B15 (move/resize).** Handling of LocationChanged:
  - If the window is not tracked, do nothing.
  - `c = Describe(Handle)`. If `c` is null, or `c.Frame` is null or not
    finite, do nothing.
  - If `c.Frame` equals the stored frame exactly, do nothing.
  - Otherwise store the new frame and call `WindowMoved(key, frame)`.
  - There is no time-based debounce. The engine recognises its own writes
    (inbound-api / frame-writer echoes).
- **B16 (focus report).** Steps for a handle `h` with a dedupe flag:
  1. If `h == 0`, or `Describe(h)` is null, stop. No pid is known, so there
     is no call.
  2. Let `c = Describe(h)` (that same single result) and
     `pid = c.ProcessId`.
  3. If `KeyFor(h)` is tracked (after the identity check), run the dockable
     re-check (B17) with `c`.
  4. If the dedupe flag is set (FocusChanged only) and
     `lastReported[pid] == h`, skip to step 7.
  5. Set `lastReported[pid] = h`.
  6. If the window is tracked, call `WindowFocused(key)`. Otherwise call
     `FocusLeftWindows(pid)`.
  7. For Foreground only, call `AppActivated(pid, c.OwnsDesktop)`. This
     runs whether or not step 6 ran. It never runs for FocusChanged (A7).
  - `lastReported` is kept per pid and is never cleared, so removals do not
    reset it.
- **B17 (dockable upgrade, one way).** Given current facts `c` for a tracked
  window:
  - If the stored `Dockable` is false and `IsDockable(c)` is true, set it to
    true and (after Ready) call `WindowBecameDockable(key)`.
  - A stored `true` is never set back to false while the window stays
    tracked.
  - The re-check runs on every scan `Keep` (B11) and on every focus report
    (B16).
- **B18 (removal).** Removing `k` with a kind does the following:
  - drops the record;
  - after Ready, calls `WindowRemoved(k, temporary)`;
  - leaves `lastReported` and pending re-checks unchanged.
  - A later admission of the same handle is a fresh `WindowAdded` with the
    same key (studied rule 17).
  - Virtual-desktop switches cause **no removals**: shell cloaks are `Keep`
    (B6.7), and #29 suspends and resumes engines instead.
- **B19 (identity check / handle reuse).** A tracked record matches facts
  `c` iff `c.ProcessId == stored.ProcessId` and `c.ThreadId ==
  stored.ThreadId`. On a mismatch the old key is removed permanently
  (`WindowRemoved(k, false)` after Ready) before anything else happens with
  `c`.
- **B20 (re-entrancy).** Every public entry point (`Rescan`, `SetPolicy`)
  and every event or clock callback runs through one FIFO work queue that is
  drained by the outermost call. This is the same pattern as
  `DisplayEngine.Run`. A call that arrives while the tracker is working,
  such as a target method that calls `tracker.Rescan()` or an event raised
  from inside a target call, is queued. It runs after the current item has
  finished, inside the same outermost call. Scans therefore never nest
  (`MaxEnumerateDepth == 1`). Exceptions from the source or the target
  propagate to the outermost caller. Queued items stay queued and run on the
  next entry.
- **B21 (exclusion change).** `SetPolicy(p)`:
  - Before Start, it only stores `p`.
  - After Start, it first stores `p`. Then, for every tracked record (in
    admission order) whose app id was **not** excluded by the old policy and
    **is** excluded by `p`, it calls `target.Release(key)` and drops the
    record. No `WindowRemoved` call is made for these, because the engine's
    `Release` already stops managing the window. Finally it runs one full
    scan, which admits windows of apps that are no longer excluded and
    reconciles any minimum-size change through `Fate`.
  - `SetPolicy` with an equal policy still runs the scan.
- **B22 (Dispose).**
  - Disposes the subscription.
  - Sets the tracker disposed. Pending clock callbacks and any later events
    do nothing.
  - Makes no target call.
  - Is idempotent.
  - After Dispose, `Rescan()` and `SetPolicy()` do nothing, and `Start()`
    throws.

### Fakes

- **B23 (FakeWindowSource).** The fake behaves as follows:
  - `Enumerate()` returns the current facts in fake z-order as a new list,
    and increments `EnumerateCount`.
  - `Describe(h)` returns the current facts or null, and increments
    `DescribeCount`.
  - `Put` of a new handle makes it frontmost. `Put` of an existing handle
    replaces the facts and keeps the window's z position.
  - `Remove` makes `Describe` return null and drops the window from
    `Enumerate`.
  - `Subscribe` returns a disposable. Disposing it removes the handler.
  - `Raise` invokes each live handler synchronously, in subscription order.
    Tests call `Raise` only from outside source calls, which satisfies the
    "never inside a source call" contract.
  - `MaxEnumerateDepth` records the deepest nesting of `Enumerate`. The
    fake's `Enumerate` runs the `OnEnumerate` hook (when set) inside the
    call, so a test can try to re-enter.
- **B24 (RecordingTrackerTarget).** Records every interface method call
  except `DisplayFrames` and `Holds` as a `TrackerCall`, appended in call
  order with `At = clock.Now`, and then invokes `OnCall`. `ReadyWindows`
  holds the last `Ready` argument (copied).

## Edge cases & errors

- **E1.** Null arguments throw `ArgumentNullException` with no state change:
  the constructor (any argument except `options`), `Classify` and `Fate`'s
  policy, `Classify`'s facts, `IsDockable`'s facts, and `SetPolicy(null)`.
  `Fate(null, p)` is legal, because null means gone.
- **E2.** A frame with negative width or height is finite. It is classified
  `TooSmall`, since it is below any non-negative minimum. A minimum of
  `(0, 0)` admits any finite frame with width ≥ 0 and height ≥ 0.
- **E3.** If `DisplayFrames` is empty, the centre test always fails. The
  window is then admitted only if it is held or
  `VisibleFraction < OffScreenAdmitBelow`. With no displays,
  `FakeWindowSystem.VisibleFraction` is 0, so the window is admitted.
- **E4.** `Enumerate` may return two facts with the same handle (a
  misbehaving adapter). The first one wins and later duplicates within the
  same scan are skipped.
- **E5.** A tracked window can be listed under a different pid than the one
  stored (handle reuse). A pid-scope scan for the stored pid misses it,
  `Describe` then shows the new pid, and the identity check fails, which
  gives a permanent removal. A later scan of the new pid admits it as a new
  window with the same key.
- **E6.** A clock callback that fires after a record it concerned was
  removed simply scans. Re-checks hold no key state.
- **E7.** `TitleChanged` with `Describe` returning facts with an empty title
  stores and reports `""`.
- **E8.** `Foreground` with `Handle == 0` (no foreground window) makes no
  call (B16.1).

## Acceptance criteria

All criteria are [mac] xUnit tests, apart from AC30, which is a build and
inspection check that also runs on the Mac. Engine tests use
`FakeWindowSource`, `RecordingTrackerTarget`, `ManualClock(0)`, and
`FakeWindowSystem` with default `Screens = [(0,0,1440,900)]` as the
`IWindowSystem`. `RecordingTrackerTarget.Displays` defaults to
`[(0,0,1440,900)]`.

"Std(h, pid)" is the baseline facts:
- `Handle = h`, `ProcessId = pid`, `ThreadId = pid*10`;
- `AppId = "app" + pid`, `AppName = "App" + pid`, `Title = "t" + h`;
- `Frame = (100,100,900,700)`;
- `IsTopLevel`, `HasCaption`, `IsResizable`, `HasMaximizeButton`,
  `IsVisible` and `IsAccessible` are true;
- `MaximizeEnabled = true`, `Cloak = None`, and every other bool is false.

Times are compared within `1e-9`.

### Core

- **AC1** [mac] `Classify(Std(4211,7), Default)` returns `Managed(true)`, with
  `IsManaged`, `Dockable` and `Reason == None`. (B2, B4; worked example 1)
- **AC2** [mac] Dockable truth table, for windows that are otherwise
  Std(...):

  | IsResizable | HasMaximizeButton | MaximizeEnabled | Result |
  |---|---|---|---|
  | false | — | — | `Managed(false)` |
  | true | false | — | `Managed(false)` |
  | true | true | false | `Managed(false)` |
  | true | true | null | `Managed(true)` |

  `IsDockable` gives the same values as the table. (B4; rule 11; worked
  example 2)
- **AC3** [mac] Size boundaries with the Default policy:

  | Frame | Result |
  |---|---|
  | 120×80 | Managed |
  | 119.999×80 | TooSmall |
  | 120×79.999 | TooSmall |
  | 100×60 | TooSmall |
  | null | NoFrame |
  | `(NaN,0,500,500)` | NoFrame |
  | `(0,0,∞,500)` | NoFrame |

  With `MinimumSize = (0,0)`, a 1×1 frame is Managed. (B2.13–14; E2; worked
  example 3)
- **AC4** [mac] Single-fact truth table. Each row starts from Std(5,7) and
  changes one fact (or the pair shown) to get the stated reason:

  | Change | Reason |
  |---|---|
  | `Handle=0` | NoHandle |
  | `IsOwnProcess` | OwnProcess |
  | `IsShellWindow` | ShellWindow |
  | policy excludes `"app7"` | ExcludedApp |
  | `IsTopLevel=false` | NotTopLevel |
  | `IsChild` | NotTopLevel |
  | `IsOwned` | NotStandard |
  | `IsToolWindow` | NotStandard |
  | `IsNoActivate` | NotStandard |
  | `HasCaption=false, IsResizable=false` | NotStandard |
  | `IsVisible=false` | Hidden |
  | `Cloak` = Shell, App or Inherited | Cloaked (3 rows) |
  | `IsMinimized` | Minimized |
  | `IsFullScreen` | FullScreen |
  | `IsHung` | Hung |
  | `IsAccessible=false` | Inaccessible |

  These changes stay Managed:
  - `IsOwned` with `IsAppWindow`;
  - `HasCaption=false` with `IsResizable=true`;
  - `AppId=null` with the policy excluding `"app7"`;
  - the policy excluding `"APP7"` (ordinal).

  (B2; rules 1, 6–7; worked example 4)
- **AC5** [mac] Precedence when several checks fail:
  - `IsOwnProcess` and `IsMinimized` → OwnProcess;
  - `IsMinimized` with a 10×10 frame → Minimized;
  - `IsToolWindow` and `IsVisible=false` → NotStandard;
  - `IsFullScreen` with `IsAccessible=false` → FullScreen.

  (B2 order)
- **AC6** [mac] `Fate` table, each row from Std(5,7) with the Default policy
  unless stated:

  | Facts | Fate |
  |---|---|
  | null | RemovePermanent |
  | `IsOwnProcess` | RemovePermanent |
  | `IsShellWindow` | RemovePermanent |
  | app excluded | RemovePermanent |
  | `IsHung` together with `IsMinimized` | Keep |
  | `IsMinimized` | RemoveTemporary |
  | `IsVisible=false` | RemoveTemporary |
  | `Cloak=App` | RemoveTemporary |
  | `Cloak=Shell` | Keep |
  | `Cloak=Inherited` | Keep |
  | Std | Keep |
  | `IsResizable=false, HasMaximizeButton=false` (managed, not dockable) | Keep |
  | `IsFullScreen` | RemoveTemporary |
  | frame 100×60 | RemoveTemporary |
  | `IsToolWindow` | RemoveTemporary |
  | `IsAccessible=false` | RemoveTemporary |
  | `Frame=null` | RemoveTemporary |

  (B6; rules 13–16; Unknowns 1–2)
- **AC7** [mac] `ClassifierPolicy`:
  - Defaults: `Default.MinimumSize == (120,80)`, `ExcludedApps` is empty,
    and `ReferenceEquals(Default, Default)`.
  - Rejected sizes: `MinimumSize` set to `(NaN,1)`, `(1,∞)`, `(-1,1)` or
    `(1,-0.5)` throws `ArgumentOutOfRangeException` with `ParamName` equal
    to `"MinimumSize"`. `(0,0)` is accepted.
  - Rejected exclusions: `ExcludedApps = null` throws
    `ArgumentNullException`. A set containing null or `""` throws
    `ArgumentException`.
  - Equality: `WithExcludedApps(["a","b"])` equals `WithExcludedApps(["b","a"])`,
    and their hashes are equal. It does not equal `WithExcludedApps(["a"])`.
  - Copying: mutating the source list after construction does not change
    the policy.
  - `Default` is unchanged after `with` expressions.

  (B5)
- **AC8** [mac] `OsWindowFacts`:
  - A default instance has `Title == ""`, `Frame == null`,
    `Cloak == None` and `Handle == 0`.
  - `new OsWindowFacts { Title = null! }` throws `ArgumentNullException`.
  - Two instances with equal members are equal.
  - `Classify(new OsWindowFacts(), Default)` is `Ignore(NoHandle)`.
  - `WindowClassification.Ignore(IgnoreReason.None)` throws
    `ArgumentOutOfRangeException`.

  (B1, B3)

### Engine: options, keys, construction

- **AC9** [mac] `KeyFor`:
  - `KeyFor(4211).Value == "w4211"`;
  - `KeyFor(ulong.MaxValue).Value == "w18446744073709551615"`;
  - `KeyFor(0)` throws `ArgumentOutOfRangeException`.

  (B7; rule 26)
- **AC10** [mac] `TrackerOptions`:
  - Defaults: `FirstRecheckSeconds 0.05`, `SecondRecheckSeconds 0.4`,
    `RescanSeconds 3.0`, `OffScreenAdmitBelow 0.05`, `FallbackAppName "App"`,
    and `ReferenceEquals(Default, Default)`.
  - The record has exactly these five public instance properties
    (reflection). There is no ready-deadline property.
  - Each of the three `*Seconds` set to `0`, `-1`, `NaN` or `∞` throws
    `ArgumentOutOfRangeException` with `ParamName` equal to the property
    name.
  - `OffScreenAdmitBelow` set to `-0.01`, `1.01` or `NaN` throws. `0` and
    `1` are accepted.
  - `FallbackAppName = ""` throws `ArgumentException`, and `null` throws
    `ArgumentNullException`.

  (Public API; A8)
- **AC11** [mac] Construction makes no calls: `EnumerateCount == 0`,
  `DescribeCount == 0`, `SubscriberCount == 0`, `Calls` is empty and
  `clock.PendingCount == 0`. `IsStarted` and `IsReady` are false. Each null
  argument (other than `options`) throws `ArgumentNullException`. (B9, E1)

### Engine: start and readiness

- **AC12** [mac] Start. The scene, front to back, is:
  - `Std(1,7)`;
  - `Std(2,7)` with `AppName = null` and `AppId = ""`;
  - `Std(3,8)` sized 100×60;
  - `Std(4,9)` with `IsResizable = false`.

  When `Start()` returns (no clock advance):
  - `SubscriberCount == 1` and `EnumerateCount == 1`;
  - `Calls` is exactly `[Ready]`, recorded with `At == 0`, so Ready fired
    synchronously at the end of the first scan;
  - `ReadyWindows` keys are `[w1, w2, w4]`;
  - w2 has `AppName == "App"` and `App == null`;
  - w1 has `App == new AppId("app7")`, `Title "t1"`, frame
    `(100,100,900,700)` and `Dockable == true`;
  - w4 has `Dockable == false`;
  - `IsReady` is true;
  - `Tracked` equals `ReadyWindows`;
  - `clock.PendingCount == 1`. The only scheduled action is the rescan, so
    nothing is scheduled for readiness.

  `Advance(3)` (which runs the first rescan) adds no second `Ready`. A
  second `Start()` adds no call, no enumeration and no clock action.
  `Start()` after `Dispose()` throws `ObjectDisposedException`. (B7–B9;
  rules 10, 23; worked examples 1–3; A8)

### Engine: admission

- **AC13** [mac] Admission test, at Start with Displays `[(0,0,1440,900)]`:
  - A window with frame `(-800,300,600,400)` has its centre at (−500,500),
    off every display, and is 0 % visible. It is admitted.
  - A frame `(-540,300,600,400)` has its centre at (−240,500) and is 10 %
    visible (`60/600`). It is **not** admitted.
  - A frame `(-582,300,600,400)` is 3 % visible and is admitted.
  - The 10 % window is admitted when `Held` contains its key.
  - A window whose centre is exactly at x = 1440, with frame
    `(1140,100,600,400)`, is not centred on the display. At 50 % visible it
    is not admitted.
  - With `Displays = []` and `FakeWindowSystem.Screens = []`, the window is
    admitted (E3).
  - A rejected window is admitted by the next rescan (+3 s) after `Held`
    gains its key.

  (B12; rule 9; worked example 5)

### Engine: rescans and re-checks

- **AC14** [mac] Periodic rescan. After `Start()` at t=0:
  - `Put(Std(10,7))` at t=1. Nothing happens until `Advance(2)` reaches
    t=3, when `EnumerateCount == 2` and `WindowAdded(w10)` is recorded with
    `At == 3`.
  - At t=6 there is another enumeration and no new call.
  - `Remove(10)` and then `Advance(3)` give `WindowRemoved(w10, false)` at
    t=9.
  - For another window w11 that is tracked: `Update(11, IsMinimized=true)`
    and the next rescan give `WindowRemoved(w11, true)`.
  - A tracked w12 updated to `Cloak = Shell` gives no call at the rescan
    and stays in `Tracked`.

  (B10, B11, B18; rules 16, 24; worked example 9)
- **AC15** [mac] Re-check timing. Start with no windows at t=0, then
  `Advance(10)`, which runs rescans at 3, 6 and 9. Then:
  - At t=10, `Put(Std(20,7)` with frame 50×50, too small) and
    `Raise(Created, 20)`. No enumeration happens during the event.
  - At t=10.02, `Update(20, frame 900×700)` and `Put(Std(21,7))`.
  - At t=10.05, one enumeration gives `WindowAdded(w21)` and then
    `WindowAdded(w20)`, both with `At == 10.05`, in fake z-order (w21 was
    put later, so it is frontmost).
  - At t=10.4, one enumeration gives no call.
  - Between t=10 and t=10.45 there are exactly 2 enumerations.

  (B14; rule 18)
- **AC16** [mac] Re-check coalescing and scope:
  - **Same process.** `Raise(Created,20)` at t=10 and `Raise(Shown,20)` at
    t=10.01 (pid 7) give exactly two enumerations, at 10.05 and 10.4.
  - **New first slot, pending second slot.** A third event at t=10.1 gives
    a new first-slot re-check at 10.15. The second slot is still pending, so
    no new second-slot re-check is added. The result is enumerations at
    10.05, 10.15 and 10.4.
  - **Another process.** An event for pid 8 at t=10.01 adds its own two
    re-checks.
  - **Scope.** A pid-7 re-check never admits or removes a pid-8 window. In
    a scenario with no pid-8 events, a pid-8 window `Put` at t=10.02 is not
    admitted at 10.05 or 10.4 by pid 7's re-checks. It is admitted by the
    next periodic rescan (t=12).
  - **Unknown handle.** `Created` for a handle that `Describe` does not
    know schedules nothing (`PendingCount` is unchanged).

  (B11 scope, B14; A2)

### Engine: removals and re-admission

- **AC17** [mac] Removal events, all on tracked windows of pid 7:
  - `Destroyed(1)` → `WindowRemoved(w1, false)`;
  - `Minimized(2)` → `WindowRemoved(w2, true)`;
  - `Hidden(3)`, with the fake updated to `IsVisible=false` →
    `WindowRemoved(w3, true)`;
  - `Hidden(4)` after `Remove(4)` → `WindowRemoved(w4, false)`;
  - `Hidden(5)` while still visible → no call;
  - `Cloaked(6)` with `Cloak=App` → `WindowRemoved(w6, true)`;
  - `Cloaked(7)` with `Cloak=Shell` → no call, and w7 is still tracked;
  - `Destroyed(99)` (untracked) → no call.

  `Destroyed`, `Minimized` and events on untracked handles make no
  `Describe` call (`DescribeCount` is unchanged). (B13, B6, B18; rules
  13–15; worked examples 6–8)
- **AC18** [mac] Re-admission:
  - `Minimized(1)` on tracked w1 gives `WindowRemoved(w1, true)`, and
    `Find(w1) == null`.
  - The fake is then updated to `IsMinimized=false` and
    `Raise(Restored, 1)` at time T. At T+0.05 this gives `WindowAdded` with
    key w1, and w1 is now last in `Tracked`.
  - Two hidden windows of the same pid (`Hidden(1)` and `Hidden(2)`, both
    updated to invisible) give two temporary removals in event order.

  (B14, B18; rules 17–18; worked examples 6–7)

### Engine: move, title, focus

- **AC19** [mac] Move. w1 is stored at `(0,0,800,600)`:
  - `LocationChanged(1)` with the fake still at `(0,0,800,600)` → no call;
  - the fake at `(10,0,800,600)` → `WindowMoved(w1,(10,0,800,600))`, and
    `Find(w1).Frame` is updated;
  - the same event again → no call;
  - the fake at `(10,0,820,600)` → `WindowMoved`;
  - `LocationChanged` for an untracked handle → no call and no `Describe`;
  - after `Remove(1)`, `LocationChanged(1)` → no call;
  - with `Frame=null` → no call.

  (B15; rule 19; worked example 10)
- **AC20** [mac] Title. After `Update(1, Title="a")`,
  `TitleChanged(1)` raised twice gives two `WindowTitleChanged(w1,"a")`
  calls, and `Find(w1).Title == "a"`. An untracked handle gives no call.
  (B13; rule 20)
- **AC21** [mac] Focus de-duplication. w1 and w2 are tracked, pid 7:
  - `FocusChanged(1)` twice → exactly one `WindowFocused(w1)`;
  - `Foreground(1)` → `WindowFocused(w1)` and then `AppActivated(7,false)`;
  - `FocusChanged(1)` → no call (still deduped against handle 1);
  - `FocusChanged(2)` → `WindowFocused(w2)`;
  - `FocusChanged(1)` → `WindowFocused(w1)`;
  - `FocusChanged(30)`, where 30 is an untracked tool window of pid 7 →
    `FocusLeftWindows(7)`;
  - `FocusChanged(30)` again → no call.

  (B16; rule 21; worked example 11)
- **AC22** [mac] Foreground variants:
  - The foreground is untracked handle 40 of pid 9 with `OwnsDesktop=true`
    (a shell desktop window). The calls are `FocusLeftWindows(9)` and then
    `AppActivated(9,true)`.
  - `Foreground(0)` → no call (E8).
  - `Foreground` of an unknown handle (Describe null) → no call.
  - `Foreground` of an own-process window → no call (A10).
  - `Foreground(1)` repeated three times → three `WindowFocused(w1)` and
    three `AppActivated(7,false)` calls, interleaved.

  (B13, B16; rules 4, 21–22)
- **AC23** [mac] Dockable upgrade:
  - w7 is admitted with `HasMaximizeButton=false`, so `Dockable` is false.
  - `Update(7, HasMaximizeButton=true)` then `FocusChanged(7)` →
    `WindowBecameDockable(w7)` and then `WindowFocused(w7)`.
    `Find(w7).Dockable` is true.
  - `Update(7, HasMaximizeButton=false)`, then a focus report and a rescan
    → no `WindowBecameDockable`, and `Find(w7).Dockable` stays true.
  - On a separate tracker, w8 starts non-dockable. After an `Update`, the
    3 s rescan gives `WindowBecameDockable(w8)`.
  - No `WindowBecameDockable` call ever comes before `Ready` in `Calls`
    (B8). The `Ready` list carries the dockable value of the initial facts,
    which AC12 also checks.

  (B17, B8; rule 12; worked example 12)

### Engine: identity, policy, re-entrancy, dispose

- **AC24** [mac] Handle reuse:
  - w1 is tracked for pid 7. `Put(Std(1,8))` replaces its facts with pid 8
    and thread 80.
  - `LocationChanged(1)` → `WindowRemoved(w1,false)` and nothing else.
  - The next rescan → `WindowAdded` w1 with `ProcessId == 8`.
  - In a variant, the reuse is first seen by a rescan. The same scan gives
    `WindowRemoved(w1,false)` and then `WindowAdded(w1, pid 8)`.

  (B19, B11; E5)
- **AC25** [mac] Exclusions:
  - Tracked windows are w1 (app7) and w2 (app8).
  - `SetPolicy(Default.WithExcludedApps(["app7"]))` gives `Calls` of exactly
    `[Release(w1)]`. `Tracked` is `[w2]`, and one enumeration ran.
  - `SetPolicy(Default)` → `WindowAdded(w1)`.
  - `SetPolicy` before `Start` → no call and no enumeration, and the stored
    policy is used by `Start`.
  - `SetPolicy(Default with { MinimumSize = (1000, 1000) })` →
    `WindowRemoved(w*, true)` for every tracked 900×700 window.

  (B21; rule 5)
- **AC26** [mac] Re-entrancy:
  - The target's `OnCall` calls `tracker.Rescan()` whenever it records
    `WindowAdded`.
  - After a scan admits two windows, `MaxEnumerateDepth == 1`.
  - Each queued rescan runs after the current scan, inside the same
    outermost call. When the outermost call returns, there is no further
    `WindowAdded` for those windows.
  - An `OnCall` that raises `FocusChanged(1)` on the source during
    `WindowAdded(w1)` gives `WindowFocused(w1)` **after** the
    `WindowAdded(w1)` in `Calls`.

  (B20)
- **AC27** [mac] Dispose:
  - After `Dispose()`, `SubscriberCount == 0`.
  - `Advance(10)` makes no enumeration and no call.
  - `Rescan()` and `SetPolicy(...)` do nothing.
  - `Dispose()` twice is fine.
  - A re-check pending at dispose time does nothing when it fires.

  (B22)
- **AC28** [mac] Own-process and zero-handle events:
  - Each event kind raised for an own-process window (`IsOwnProcess`, even
    one that is otherwise standard) gives no call.
  - `Created` with `Handle=0` schedules nothing.
  - Neither kind of window is ever admitted by a scan.

  (B13, B2.1–2; A10)

### Fakes and architecture

- **AC29** [mac] `FakeWindowSource` contract:
  - `Put(a)`, `Put(b)` → `Enumerate` returns `[b, a]`.
  - `Put` of a changed `a` keeps the order `[b, a]` with the new facts.
  - `SetZOrder([a])` → `[a, b]`.
  - `Remove(a)` → `Describe(a)` is null and `Enumerate` returns `[b]`.
  - `Raise` reaches two subscribers in subscription order. After one
    subscription is disposed, it reaches only the other. Disposing twice is
    fine.
  - The counters count calls. An `OnEnumerate` hook that calls `Enumerate`
    again gives `MaxEnumerateDepth == 2`.
  - `RecordingTrackerTarget` records `At` from the clock and
    `DisplayFrames` returns a copy.
  - Each fake is assignable to its interface.

  (B23, B24)
- **AC30** [mac] Build and inspection:
  - `dotnet build CanvasWm.slnx` exits 0 with 0 warnings.
  - The Core and Engine project and package references are unchanged.
  - No existing file under `src/` is modified, so the only `src/` changes
    are the new files listed in Placement.
  - `CanvasWm.Core.Windows` types do not reference `CanvasWm.Engine` or any
    `-windows` TFM. This is checked by the existing architecture tests and
    by reflection: no type in `CanvasWm.Core.Windows` has a member of an
    Engine type.
  - No file in `src/` or `tests/` contains `TrellisWM` or `Licensing`.
  - `IWindowSource` is public in `CanvasWm.Engine.Services`.
    `WindowTracker`, `IWindowTrackerTarget` and `TrackerOptions` are public
    in `CanvasWm.Engine.Tracking`.

  (CLAUDE.md; Placement)

Count: **30 [mac]** (AC1–AC30), **0 [windows]**.

### Notes for #10 (Windows adapter; not criteria of this feature)
- **Enumeration.** `Enumerate` uses `EnumWindows`, which returns top-level
  windows in z-order with the topmost first. The facts map as follows:
  - `IsTopLevel`: `GetAncestor(GA_ROOT)==hwnd`;
  - `IsOwned`: `GetWindow(GW_OWNER)`;
  - `IsChild`, `IsToolWindow`, `IsAppWindow`, `IsNoActivate`, `HasCaption`,
    `IsResizable` and `HasMaximizeButton`: from the style and extended
    style bits;
  - `MaximizeEnabled`: from `GetSystemMenu` and the `SC_MAXIMIZE` state,
    null if it cannot be read;
  - `IsVisible`: `IsWindowVisible`;
  - `Cloak`: `DWMWA_CLOAKED`;
  - `IsMinimized`: `IsIconic`;
  - `IsFullScreen`: the frame equals `rcMonitor` and the window has no
    caption. A maximised window is not full screen;
  - `IsHung`: `IsHungAppWindow`;
  - `IsAccessible`: whether UIPI allows the move, based on integrity levels;
  - `Frame`: `DWMWA_EXTENDED_FRAME_BOUNDS` (falling back to
    `GetWindowRect`), converted to DIPs;
  - `IsShellWindow`: the classes Progman, WorkerW, Shell_TrayWnd and
    Shell_SecondaryTrayWnd;
  - `OwnsDesktop`: the pid equals the pid of `GetShellWindow()`.
- **Synchronous enumeration.** `Enumerate` must complete synchronously.
  The tracker's readiness depends on this (A8).
- **Identity.** `AppId` is the lower-cased image path from
  `QueryFullProcessImageName`, or the AUMID for windows hosted by
  ApplicationFrameHost. `AppName` is the `FileDescription`, falling back to
  the exe name, or null.
- **Title.** Read the title with `InternalGetWindowText`, which sends no
  message. Do not use `GetWindowText`.
- **Events.** Use `SetWinEventHook` with
  `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS`, filtered to
  `OBJID_WINDOW` / `CHILDID_SELF` and to top-level windows. The events map
  as follows:
  - `Created`: `EVENT_OBJECT_CREATE`;
  - `Shown`: `SHOW`;
  - `Uncloaked`: `UNCLOAKED`;
  - `Restored`: `EVENT_SYSTEM_MINIMIZEEND`;
  - `Destroyed`: `DESTROY`;
  - `Minimized`: `MINIMIZESTART`;
  - `Hidden`: `HIDE`;
  - `Cloaked`: `CLOAKED`;
  - `LocationChanged`: `LOCATIONCHANGE`;
  - `TitleChanged`: `NAMECHANGE`;
  - `FocusChanged`: `EVENT_OBJECT_FOCUS`, mapped to the root window of the
    focused element;
  - `Foreground`: `EVENT_SYSTEM_FOREGROUND`.
- **Event delivery.** Queue events and deliver them after the hook callback
  returns, so they never arrive inside a source call. Keep the hook delegate
  rooted.
- **Coalescing.** `LOCATIONCHANGE` may be coalesced to one per message-loop
  turn per window (this is not studied). The tracker's change-only filter
  makes this optional.
- **Not in the adapter.** The adapter has no thresholds, no exclusions and
  no removal decisions.

## Assumptions

- **A1. Output through an injected target interface rather than
  callbacks.** One `IWindowTrackerTarget` has 12 members. A single object is
  the natural unit for #29 to implement as a router over the per-display,
  per-desktop engines, routing by `Holds` and saved owners. The admission
  test also needs queries from it (`DisplayFrames`, `Holds`), which a set of
  fire-and-forget callbacks could not express cleanly. A recording fake also
  gives one ordered call log, which the ordering criteria need. The tracker
  never sees `DisplayEngine` directly, so routing stays entirely in #29.
- **A2. "One scan in flight per app" means at most one pending re-check per
  (process, slot).** *Needs human review.* `IWindowSource` is synchronous,
  so scans can never overlap: the re-entrancy queue (B20) already
  guarantees that. The studied guard collapses bursts in practice, and the
  closest synchronous equivalent is to skip scheduling a slot that is
  already pending. As a result, an event inside an already-pending
  second-slot window does not get its own full 0.4 s wait (AC16). This is
  not a time debounce: no pending re-check is ever postponed.
- **A3. A still-listed window that no longer qualifies is removed
  temporarily** (Unknown 1, human decision). See B6.9.
- **A4. Entry into native full screen is caught by the next scan** (Unknown
  2, human decision). There is no dedicated event. The next re-check or the
  3 s rescan applies `Fate`, which gives `RemoveTemporary`. Leaving full
  screen is not studied either: the window re-qualifies at a scan and is
  re-admitted. On Windows a `LocationChanged` is usually raised as well, but
  it only reports a move.
- **A5. Cloak semantics.** A Shell or Inherited cloak on a tracked window
  means it is on another virtual desktop, so it is kept and not removed
  (human note: desktop switches cause no removals). An App cloak means
  removal is temporary. An untracked cloaked window is not admitted until it
  is uncloaked, at which point the Uncloaked event triggers re-checks. Each
  virtual desktop therefore adopts its windows when it is first shown. #29
  must call `Rescan()` after a detected desktop switch.
- **A6. A hung window is kept while tracked and not admitted while
  untracked.** This avoids layout churn while an app is briefly
  unresponsive. The periodic rescan admits it once it recovers.
- **A7. Order within a Foreground report.** The focus call
  (`WindowFocused` or `FocusLeftWindows`) comes before
  `AppActivated(pid, ownsDesktop)`. The studied order between the two paths
  is not known. Putting focus first means the engine's AppActivated render
  sees the new last-focused window. FocusChanged never calls `AppActivated`.
- **A8. No ready-deadline timer (human decision).** Ready fires exactly
  once, synchronously, at the end of `Start`'s first full scan. No clock
  action is scheduled for readiness, and `TrackerOptions` has no deadline
  constant. The studied app had a 0.6 s fallback because its per-app scans
  were asynchronous. With the synchronous `IWindowSource` seam (findings H1)
  that fallback could never fire first, so it is dropped. If #10 ever needs
  asynchronous enumeration, the seam and this rule must be revisited
  together.
- **A9. The centre-on-display test is half-open** (`[X, X+W) × [Y, Y+H)`).
  The study says "centre point inside the display frame" and does not say
  how edges are handled. A half-open test means a point on a shared edge
  belongs to exactly one display.
- **A10. Events and windows of CanvasWM's own process are ignored.** The
  Windows adapter also filters them with `WINEVENT_SKIPOWNPROCESS`, so this
  is a defensive duplicate. It means CanvasWM's overlay never produces
  `AppActivated`.
- **A11. New constants live in `ClassifierPolicy` (Core) and
  `TrackerOptions` (Engine), not in `Tuning`.** `Tuning` holds the "feel" of
  the stage, and its spec forbids unrelated additions without a spec change.
  The minimum size and the exclusions are classifier policy, and in a
  future version they could become user settings. The timings and the 5 %
  threshold are tracker options. Both are immutable with-able records,
  following the `ParkOptions` pattern.
- **A12. The exclusion change uses `Release` and no `WindowRemoved`**
  (B21). The study calls engine release, then drops its record "as a
  permanent removal". The engine's `Release` already stops managing the
  window, so a second removal call would be a no-op. It is omitted to keep
  the call log exact.
- **A13. The window key is `"w"` + the decimal handle**, per findings rule
  26 and J1. engine-interfaces A6 had *recommended* lowercase hex and left
  the choice to #10. The format is now fixed here (see "Reported for
  review").
- **A14. Scans do not refresh stored frames or titles.** The study does not
  show this, and the events cover both. If a `LocationChanged` is missed,
  the stored frame stays stale until the next real move event. #29 or #10
  may revisit this.
- **A15. A standard window requires a caption or a thick frame**, so a
  borderless custom-chrome app with `WS_THICKFRAME` qualifies. An owned
  window qualifies only with `WS_EX_APPWINDOW`. `IsNoActivate` excludes a
  window. These are Windows interpretations of the studied "role window /
  subrole standard" test (findings H2).
- **A16. AppId exclusion matching is ordinal.** The adapter normalises
  AppIds (it lower-cases image paths). Matching is not case-insensitive in
  Core, to stay consistent with `AppId` equality.

## Reported for review (no change made to earlier specs)

- **`FocusLeftWindows` has no `DisplayEngine` method.** The studied engine
  receives "focus left the managed windows of pid". CanvasWM's
  `DisplayEngine` (specs/engine-inbound-api.md, engine-window-events.md)
  does not define it. The tracker emits it on the target. #29 must decide
  whether to map it to an engine method, which would be a change to the
  engine specs, or to drop it. Likely effect: clear the engine's per-pid
  last-focused window so that `AppActivated` renders the "nothing focused"
  state.
- **specs/engine-interfaces.md A6** recommends `"w" + lowercase hex HWND`.
  This spec fixes `"w" + decimal` (findings rule 26 and J1). A6 should be
  read as superseded. Only #10 and #29 are affected, and neither is
  implemented yet.
- **Capture side effects.** The study shows that a resize notifies capture
  and that a title change requests a fresh picture. `ICapture` has no resize
  member, and the tracker has no `ICapture`. If these side effects are
  wanted, the engine should do them on `WindowMoved` and
  `WindowTitleChanged` (#16).

## Traceability

| Spec item | Findings rule / section |
|---|---|
| B1, OsWindowFacts docs, #10 notes | Part 2 H2, H3, H4, H5, K; Windows notes |
| B2, AC1, AC3–AC5, A15, A16 | rules 1, 6, 7; H2; worked examples 1, 3, 4 |
| B4, AC2 | rule 11; H2 (thick frame + maximize box); worked example 2 |
| B5, AC7, A11 | rules 1, 5 (exclusion list), 7 (120×80); K |
| B6, AC6, A3–A6 | rules 13–16; Unknowns 1–2; H3; I3 |
| B7, AC9, AC12, A13 | rules 10, 26; J1–J2 |
| B8, B9, AC10, AC11, AC12, A8 | rule 23 (0.6 s fallback dropped by human decision); H1 |
| B10, AC14 | rule 24; I5 |
| B11, AC14, AC16 | rules 8, 16; I5 |
| B12, AC13, A9 | rule 9; H2 (acceptance row) |
| B13, AC17, AC28, A10 | rules 3, 13–15; I2, I3 |
| B14, AC15, AC16, AC18, A2 | rule 18; I3 (may-have-appeared row); Windows notes |
| B15, AC19 | rule 19; I3, I4 |
| B13 (title), AC20 | rule 20 |
| B16, AC21, AC22, A7 | rules 4, 21, 22; I3 (foreground row) |
| B17, AC23 | rule 12; worked example 12 |
| B18, AC17, AC18 | rule 17; worked examples 6–8 |
| B19, AC24 | J1 |
| B20, AC26 | rule 18 (one scan per app); Windows notes (re-entrant hooks) |
| B21, AC25, A12 | rule 5 |
| B22, AC27 | I1 (unhook on shutdown) |
| B23, B24, AC29 | K (fake seam) |
| AC30 | CLAUDE.md architecture; K split |
