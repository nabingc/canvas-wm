# Spec: engine-interfaces

## Goal

Define the six **outbound** seams through which the CanvasWM Engine reaches the
operating system, together with the value types they exchange and a set of
deterministic fakes that let Engine features be tested on the Mac:

| Seam | Purpose | Shared? |
|---|---|---|
| `IWindowSystem` | focus, z-order, visibility, frame read/write, pointer, parking origin | one per app |
| `ICapture` | live-picture capture registration and latest-picture descriptor | one per app |
| `IClock` | monotonic time and one-shot delayed actions | one per app |
| `IRenderer` | hand a complete, immutable render plan to a display's overlay | one per display/world |
| `ILimitStore` | learned per-application minimum window size | one per app |
| `IDisplayStore` | saved per-(display, virtual desktop) arrangement | one per app |

The interfaces are pure adaptation: they report what the OS did and never
decide policy (no refusal detection, no retries, no parking decisions). The
"Shared?" column is informative; wiring is decided by #23.

Out of scope (approved split):
- Engine construction, policy, the inbound command/lifecycle methods
  (`windowAdded`, `windowFocused`, `captureArrived`, …), outbound callbacks
  (changed / needs-frames / hand-off / carry-out) and the tick loop — feature
  #23 `engine-inbound-api`.
- Hotkeys and any input seam. Input reaches the Engine as ordinary method calls
  (#23); there is **no** `IHotkeys` in Engine.
- The studied app's onboarding ("accomplishment") and feature-gate callbacks;
  the gate relates to its Licensing module, which is excluded. Neither is
  modelled anywhere.
- Real implementations: Win32 window system (#10, #11), parking writes (#14),
  capture via DWM thumbnails, JSON file I/O for the stores (Win32/App).
- Render-plan contents beyond a placeholder (overlay/overview features).

## Placement

- Project: `src/CanvasWm.Engine` (`net10.0`). It keeps exactly one
  `ProjectReference` (Core) and no `PackageReference`. Only BCL types
  (including `System.Collections.Immutable`) are used.
- Namespaces:
  - `CanvasWm.Engine.Services` — the six interfaces; keys (`WindowKey`,
    `DisplayKey`, `EngineKey`, `AppId`); `WindowInfo`, `CaptureFrame`,
    `FrameWrite`, `FrameWriteResult`, `SavedDisplay`, `DockSpot`;
    `LimitRules`.
  - `CanvasWm.Engine.Rendering` — `RenderPlan`, `WindowPlan` (placeholder).
- Suggested files: `src/CanvasWm.Engine/Services/{Keys.cs, WindowInfo.cs,
  CaptureFrame.cs, FrameWrite.cs, SavedDisplay.cs, LimitRules.cs,
  IWindowSystem.cs, ICapture.cs, IClock.cs, IRenderer.cs, ILimitStore.cs,
  IDisplayStore.cs}`, `src/CanvasWm.Engine/Rendering/RenderPlan.cs`.
- **Fakes**: `tests/CanvasWm.Engine.Tests/Fakes/` with namespace
  `CanvasWm.Engine.Tests.Fakes`: `FakeWindowSystem`, `FakeCapture`,
  `ManualClock`, `InMemoryLimitStore`, `InMemoryDisplayStore`,
  `RecordingRenderer`. Public classes (test projects do not generate XML docs).
  Rationale: every later Engine feature (#13, #15, #17, #23) tests inside
  `CanvasWm.Engine.Tests`, so a folder there is enough; a separate
  test-support project would change the fixed project list of
  `specs/solution-skeleton.md` (B1, "no other projects"). If Win32.Tests ever
  needs them, a project can be split out later (A12).
- Tests: `tests/CanvasWm.Engine.Tests/Services/*Tests.cs` (value types,
  `LimitRules`, architecture) and `tests/CanvasWm.Engine.Tests/Fakes/*Tests.cs`
  (fake contract tests).
- Reuses from Core (unchanged): `CanvasWm.Core.Geometry.Rect`, `Size`, `Point`;
  `CanvasWm.Core.Layout.LayoutDocument`, `RestoreTarget`;
  `CanvasWm.Core.Parking.ParkGeometry.VisibleFraction` and
  `DefaultFallbackOrigin` (fakes only). **No Core type is changed or added.**
- Coordinates: DIPs, y-down, origin top-left of the virtual desktop
  (CLAUDE.md).

## Public API

All public members carry XML docs (build rule B2 of solution-skeleton). A
reference parameter not marked nullable throws `ArgumentNullException` when
null. Unless a member says otherwise, adapters never throw for OS failures;
they report "unknown / failed" as `null` (or `false`, or an empty list).

### Keys and identities

```csharp
namespace CanvasWm.Engine.Services;

/// <summary>Opaque identity of a managed top-level window. Compared ordinally and
/// case-sensitively; the Engine never parses it. The Win32 adapter chooses the format
/// (recommended "w" + lowercase hex HWND, A6).</summary>
public sealed record WindowKey
{
    /// <exception cref="ArgumentNullException">value is null.</exception>
    /// <exception cref="ArgumentException">value is empty.</exception>
    public WindowKey(string value);
    public string Value { get; }
    /// <summary>Returns Value.</summary>
    public override string ToString();
}

/// <summary>Opaque identity of a physical display (e.g. monitor device path). Same rules as
/// WindowKey.</summary>
public sealed record DisplayKey
{
    public DisplayKey(string value);   // same validation as WindowKey
    public string Value { get; }
    public override string ToString();
}

/// <summary>Identity of an application (process image path or AUMID on Windows). Same rules
/// as WindowKey. "No identity" is expressed as a null AppId reference.</summary>
public sealed record AppId
{
    public AppId(string value);        // same validation as WindowKey
    public string Value { get; }
    public override string ToString();
}

/// <summary>Identity of one engine: a display plus an OS virtual-desktop id. Wire form
/// "&lt;display&gt;|&lt;desktop&gt;".</summary>
public sealed record EngineKey
{
    /// <summary>Desktop id to use when per-virtual-desktop layouts are not supported: "default".</summary>
    public const string SingleDesktopId = "default";
    /// <summary>The separator between display and desktop: '|'.</summary>
    public const char Separator = '|';

    /// <exception cref="ArgumentNullException">display or desktopId is null.</exception>
    /// <exception cref="ArgumentException">desktopId is empty or contains '|'.</exception>
    public EngineKey(DisplayKey display, string desktopId);
    public DisplayKey Display { get; }
    public string DesktopId { get; }
    /// <summary>Display.Value + "|" + DesktopId.</summary>
    public string Value { get; }
    /// <summary>Returns Value.</summary>
    public override string ToString();
    /// <summary>Splits at the LAST '|' (B3). False for null, no '|', empty display part or
    /// empty desktop part.</summary>
    public static bool TryParse(string? value, [NotNullWhen(true)] out EngineKey? key);
}
```

### Value types

```csharp
/// <summary>Description of a window as the adapter reports it (used by #23's inbound
/// lifecycle methods).</summary>
/// <param name="Dockable">Whether the window is eligible for tiling.</param>
public sealed record WindowInfo(WindowKey Key, int ProcessId, string AppName, AppId? App,
                                string Title, Rect Frame, bool Dockable);
// Key, AppName, Title: null → ArgumentNullException (init accessors validate too).

/// <summary>Descriptor of the latest captured picture of a window. The pixels stay in the
/// adapter/renderer.</summary>
/// <param name="Size">Picture size in DIPs.</param>
/// <param name="Serial">Increases with every captured picture across all windows.</param>
public readonly record struct CaptureFrame(Size Size, long Serial);

/// <summary>One entry of a batched frame write.</summary>
public readonly record struct FrameWrite(WindowKey Key, Rect Target);

/// <summary>Outcome of one entry of a batched write: the frame read back after the write,
/// or null when the window is unknown or the write/read-back failed.</summary>
public readonly record struct FrameWriteResult(WindowKey Key, Rect? Actual);

/// <summary>Remembered dock place of a window: where to restore it and its neighbouring
/// leaf ids at the time it left.</summary>
public sealed record DockSpot
{
    /// <exception cref="ArgumentNullException">restore, neighbourLeaves or an element is null.</exception>
    public DockSpot(RestoreTarget restore, IEnumerable<string> neighbourLeaves);
    public RestoreTarget Restore { get; }
    public ImmutableArray<string> NeighbourLeaves { get; }
    // Structural equality: Restore by Equals, NeighbourLeaves ordinal sequence equality.
}

/// <summary>Everything persisted for one engine key (findings rule 36).</summary>
public sealed record SavedDisplay
{
    /// <param name="document">The layout document.</param>
    /// <param name="framedNodeId">Id of the framed node, or null for none.</param>
    /// <param name="framings">Saved views: slot name (a number as text) → node ids. null = empty.</param>
    /// <param name="owners">Window key → owner (an EngineKey.Value, A8). null = empty.</param>
    /// <param name="dockSpots">Optional; null means "absent" and is distinct from empty.</param>
    /// <param name="floatSizes">Optional; null means "absent" and is distinct from empty.</param>
    /// <exception cref="ArgumentNullException">document is null, or any key/value inside a
    /// supplied dictionary or list is null.</exception>
    public SavedDisplay(LayoutDocument document,
                        string? framedNodeId = null,
                        IReadOnlyDictionary<string, IReadOnlyList<string>>? framings = null,
                        IReadOnlyDictionary<WindowKey, string>? owners = null,
                        IReadOnlyDictionary<WindowKey, DockSpot>? dockSpots = null,
                        IReadOnlyDictionary<WindowKey, Size>? floatSizes = null);

    public LayoutDocument Document { get; }
    public string? FramedNodeId { get; }
    public ImmutableDictionary<string, ImmutableArray<string>> Framings { get; }  // ordinal keys
    public ImmutableDictionary<WindowKey, string> Owners { get; }
    public ImmutableDictionary<WindowKey, DockSpot>? DockSpots { get; }
    public ImmutableDictionary<WindowKey, Size>? FloatSizes { get; }
    // Structural equality per B14; GetHashCode consistent with it (order-insensitive
    // over dictionary entries).
}
```

### Limit rules (pure, reusable by every ILimitStore implementation)

```csharp
/// <summary>The learned-minimum update rules (findings rules 31-32). Pure.</summary>
public static class LimitRules
{
    /// <summary>Observe lowers the stored minimum only when the observed size is smaller by
    /// strictly more than this on an axis: 2.</summary>
    public const double ObserveSlack = 2.0;

    /// <summary>New stored value after learning <paramref name="minimum"/> (B11).</summary>
    public static Size Learn(Size? stored, Size minimum);

    /// <summary>New stored value after observing <paramref name="observed"/>; null when
    /// nothing is stored (B12).</summary>
    public static Size? Observe(Size? stored, Size observed);
}
```

### The six seams

```csharp
/// <summary>The OS window system. All members are called on the engine (UI) thread.
/// Completions are invoked on that thread, exactly once, never inside the call that
/// started the operation (B5-B9).</summary>
public interface IWindowSystem
{
    /// <summary>Fire-and-forget: bring the window's app forward and focus the window.
    /// Unknown key: no effect. No result is reported; focus changes come back through the
    /// Engine's inbound API.</summary>
    void Focus(WindowKey key);

    /// <summary>Managed windows, frontmost first. Unmanaged windows are omitted. Never null;
    /// a new snapshot each call.</summary>
    IReadOnlyList<WindowKey> FrontToBack();

    /// <summary>True if the window is currently on screen (visible, not minimised, not
    /// cloaked). Unknown key: false. May be up to 0.1 s stale.</summary>
    bool IsShowing(WindowKey key);

    /// <summary>Fraction of rect that lies on any display's full frame, with the formula of
    /// ParkGeometry.VisibleFraction over the current display frames.</summary>
    double VisibleFraction(Rect rect);

    /// <summary>Reads the window's current frame. completion(null) if unknown or the read
    /// failed.</summary>
    void ReadFrame(WindowKey key, Action<Rect?> completion);

    /// <summary>Requests a new frame. completion receives the frame read back after the
    /// write (the app may have clamped it), or null if unknown or failed. Refusal is not
    /// signalled; the Engine compares actual with target.</summary>
    void SetFrame(WindowKey key, Rect target, Action<Rect?> completion);

    /// <summary>Writes several windows as one batch (Win32: one DeferWindowPos pass).
    /// completion receives one result per entry, in input order.</summary>
    /// <exception cref="ArgumentException">An entry has a null Key, or two entries share a
    /// key.</exception>
    void SetFrames(IReadOnlyList<FrameWrite> writes,
                   Action<IReadOnlyList<FrameWriteResult>> completion);

    /// <summary>A managed window that is a native OS tab sibling of key, or null. Windows has
    /// no native window tabs: the Win32 adapter always returns null.</summary>
    WindowKey? TabSibling(WindowKey key);

    /// <summary>Current pointer position.</summary>
    Point Pointer { get; }

    /// <summary>True while the primary mouse button is held.</summary>
    bool MouseDown { get; }

    /// <summary>Fallback origin for parked windows when the Engine has no display frame.
    /// Implementations return ParkGeometry.DefaultFallbackOrigin(current display frames)
    /// (A7).</summary>
    Point ParkingOrigin { get; }
}

/// <summary>Live-picture capture. All members called on the engine thread; none
/// throws for unknown keys.</summary>
public interface ICapture
{
    /// <summary>Register the window for capture and request a first picture.</summary>
    void Start(WindowKey key);
    /// <summary>Unregister the window and drop its picture, rate and hold mark.</summary>
    void Stop(WindowKey key);
    /// <summary>Capture rate in pictures per second. Ignored if fps is not finite and
    /// positive, or the window is not started. Advisory on Windows.</summary>
    void SetRate(WindowKey key, double fps);
    /// <summary>Set/clear the "picture stands in for the window" mark; when the mark changes,
    /// a fresh picture is requested. Ignored if not started (A9).</summary>
    void Hold(WindowKey key, bool on);
    /// <summary>Descriptor of the latest picture, or null if none / not started.</summary>
    CaptureFrame? Latest(WindowKey key);
}

/// <summary>Monotonic time and delayed actions.</summary>
public interface IClock
{
    /// <summary>Seconds since an arbitrary epoch; never decreases.</summary>
    double Now { get; }
    /// <summary>Run action once on the engine thread after `seconds`. No cancellation; the
    /// Engine guards stale callbacks itself (B19).</summary>
    void After(double seconds, Action action);
}

/// <summary>One display's overlay. Synchronous: the renderer draws or diffs the plan
/// before returning. The plan is immutable, so the renderer may keep it.</summary>
public interface IRenderer
{
    void Render(RenderPlan plan);
}

/// <summary>Learned minimum window sizes per application (B11-B13).</summary>
public interface ILimitStore
{
    /// <summary>Stored minimum for app, or null when app is null or nothing is stored.</summary>
    Size? Minimum(AppId? app);
    /// <summary>Raise the stored minimum per LimitRules.Learn. No-op when app is null.</summary>
    void Learn(AppId? app, Size minimum);
    /// <summary>Lower the stored minimum per LimitRules.Observe. No-op when app is null or
    /// nothing is stored.</summary>
    void Observe(AppId? app, Size observed);
}

/// <summary>Saved arrangements keyed by engine key (B16-B18).</summary>
public interface IDisplayStore
{
    /// <summary>The saved record, or null when none exists or it cannot be read.</summary>
    SavedDisplay? Load(EngineKey key);
    /// <summary>Replace the record for key. Never throws for storage failures.</summary>
    void Save(EngineKey key, SavedDisplay display);
}
```

### Render plan placeholder

```csharp
namespace CanvasWm.Engine.Rendering;

/// <summary>One window's entry in a render plan. Later overlay features add members.</summary>
public sealed record WindowPlan(WindowKey Key, Rect Frame, bool Focused);   // Key null → ArgumentNullException

/// <summary>Complete, immutable description of everything to draw on one display.
/// Placeholder; grows with the overlay, tab-strip and overview features.</summary>
public sealed record RenderPlan
{
    public static RenderPlan Empty { get; }
    /// <exception cref="ArgumentNullException">windows or an element is null.</exception>
    public RenderPlan(IEnumerable<WindowPlan> windows);
    public ImmutableArray<WindowPlan> Windows { get; }
    // Structural equality: Windows sequence-equal.
}
```

### Fakes (tests/CanvasWm.Engine.Tests/Fakes)

```csharp
namespace CanvasWm.Engine.Tests.Fakes;

public sealed class FakeWindowSystem : IWindowSystem
{
    // --- scene set-up (immediate, no queue) ---
    public IReadOnlyList<Rect> Screens { get; set; }          // default [(0,0,1440,900)]
    public Point? ParkingOriginOverride { get; set; }          // default null
    public Point Pointer { get; set; }
    public bool MouseDown { get; set; }
    public void AddWindow(WindowKey key, Rect frame, bool showing = true); // becomes frontmost; ArgumentException if already managed
    public void RemoveWindow(WindowKey key);                   // unknown: no-op
    public void SetShowing(WindowKey key, bool showing);
    public void SetFrameDirect(WindowKey key, Rect frame);     // "the user/app moved it"
    public void SetConstraints(WindowKey key, Size? min = null, Size? max = null);
    public void SetRefusesWrites(WindowKey key, bool refuses);
    public void SetFails(WindowKey key, bool fails);           // reads and writes complete with null
    public void SetZOrder(IEnumerable<WindowKey> frontToBack);  // may include unmanaged keys
    public Rect? FrameOf(WindowKey key);                       // current frame, null if unknown

    // --- recording ---
    public IReadOnlyList<FrameWriteRecord> Writes { get; }     // call order
    public IReadOnlyList<WindowKey> Reads { get; }             // call order
    public IReadOnlyList<WindowKey> FocusRequests { get; }     // call order
    public int PendingCount { get; }

    // --- pumping ---
    public bool Step();                  // run oldest pending op; false if none
    public bool Step(WindowKey key);     // run oldest pending op involving key; false if none
    public int Pump();                   // run until empty (incl. ops queued by completions)
}
public readonly record struct FrameWriteRecord(WindowKey Key, Rect Target, int? BatchId);

public sealed class FakeCapture : ICapture
{
    public bool IsStarted(WindowKey key);
    public double? Interval(WindowKey key);       // 1/fps, null if no rate or not started
    public bool IsHeld(WindowKey key);
    public int RecaptureRequests(WindowKey key);  // see B27
    public CaptureFrame? Deliver(WindowKey key, Size size); // simulate an arriving picture
}

public sealed class ManualClock : IClock
{
    public ManualClock(double start = 0.0);
    public int PendingCount { get; }
    public void Advance(double seconds);
}

public sealed class InMemoryLimitStore : ILimitStore
{
    public IReadOnlyDictionary<AppId, Size> Entries { get; }
    public int PersistCount { get; }   // see B28
}

public sealed class InMemoryDisplayStore : IDisplayStore
{
    public IReadOnlyList<(EngineKey Key, SavedDisplay Display)> SaveLog { get; }
}

public sealed class RecordingRenderer : IRenderer
{
    public IReadOnlyList<RenderPlan> Plans { get; }
    public RenderPlan? Last { get; }
}
```

The fake member lists are the minimum; the developer may add test helpers but
must not change the behaviour below.

## Behaviour

### Keys (findings rules 1–4)
- **B1.** `WindowKey`, `DisplayKey`, `AppId`: equality and hashing are ordinal
  and case-sensitive on `Value`; `ToString()` returns `Value`. The constructor
  rejects null (`ArgumentNullException`) and `""` (`ArgumentException`).
  Whitespace-only values are accepted (opaque).
- **B2.** `EngineKey.Value = Display.Value + "|" + DesktopId`. Equality is
  `Display` and `DesktopId` (ordinal). `DesktopId` must be non-empty and must
  not contain `|`; `Display.Value` may contain `|`.
- **B3.** `EngineKey.TryParse(s)`: false if `s` is null or contains no `|`.
  Otherwise split at the **last** `|`; false if either part is empty; else
  true with `Display = new DisplayKey(left)`, `DesktopId = right`. For every
  valid key `k`, `TryParse(k.Value)` yields a key equal to `k`.
- **B4.** "No application identity" is a null `AppId?`; it means no learning
  and no lookup (rule 4).

### Window system contract (rules 10, 12–21)
- **B5 (threading).** Every member is called on the engine thread. Every
  completion is invoked on the engine thread, **exactly once**, and **never
  synchronously inside the call** that started it, including for unknown
  keys and empty batches. Adapters that have a result at once post it to the
  UI dispatcher.
- **B6 (per-window ordering).** For one window, reads and writes (single or
  as part of a batch) take effect in the order they were issued, and their
  completions are invoked in that order. A read issued after a write to the
  same window observes that write's outcome. Operations on different windows
  have no ordering guarantee.
- **B7 (actual frame).** A write's result is the frame **read back after**
  the write, in DIPs, using the same frame convention as `ReadFrame` (on
  Windows: extended frame bounds, #11). The adapter never adjusts, retries or
  judges it; a clamped or ignored write simply returns the actual frame.
  Null means: key unknown at the time the operation runs, the window vanished,
  access was denied (e.g. elevated window), or the read-back failed.
- **B8 (target validation).** A target with any non-finite component
  (NaN/±∞) is not sent to the OS; that write completes with null and the
  window is unchanged. No other validation (zero/negative sizes are passed
  through; the OS decides).
- **B9 (batch).** `SetFrames(writes, completion)` is equivalent, for each
  window, to one `SetFrame` issued at the time of the call (B6), except that
  all entries complete together: `completion` is invoked once, after every
  entry is done, with `writes.Count` results in input order, each carrying
  its entry's key. Unknown/failed entries yield `Actual = null` without
  affecting other entries. Duplicate keys or a null key throw
  `ArgumentException` synchronously and nothing is recorded or queued. An
  empty list is valid and completes (asynchronously) with an empty list.
- **B10 (queries).** `FrontToBack()` returns only managed windows in OS
  z-order, frontmost first. `IsShowing` is false for unknown keys.
  `VisibleFraction(rect)` equals `ParkGeometry.VisibleFraction(rect,
  displayFullFrames)`. `ParkingOrigin` equals
  `ParkGeometry.DefaultFallbackOrigin(displayFullFrames)` (A7).
  `TabSibling` returns null on Windows. `Focus` on an unknown key does
  nothing and never throws.

### Limit store (rules 30–33)
- **B11 (learn).** `LimitRules.Learn(stored, m)`: let `b = stored ?? (0, 0)`.
  If `m.Width` or `m.Height` is not finite, return `b`. Otherwise return
  `(Math.Max(b.Width, m.Width), Math.Max(b.Height, m.Height))`.
- **B12 (observe).** `LimitRules.Observe(stored, o)`: null if `stored` is
  null. If `o.Width` or `o.Height` is not finite, return `stored`. If
  `o.Width < stored.Width − 2` **or** `o.Height < stored.Height − 2`
  (strict), return `(Math.Min(stored.Width, o.Width), Math.Min(stored.Height,
  o.Height))` — both axes take the minimum, even an axis within the slack.
  Otherwise return `stored` unchanged.
- **B13 (store contract).** For every `ILimitStore`:
  - `Minimum(null)` is null; `Learn(null, …)` and `Observe(null, …)` do
    nothing.
  - `Learn(app, m)` with non-finite `m` does nothing (no entry is created).
    Otherwise the entry becomes `LimitRules.Learn(current, m)`.
  - `Observe(app, o)` with no entry does nothing (no entry is created).
    Otherwise the entry becomes `LimitRules.Observe(current, o)`.
  - Changes are visible to the next `Minimum` call immediately and are
    persisted before the call returns (durability is the implementation's
    concern; storage errors are swallowed). Keys compare by `AppId` equality.

### SavedDisplay (rule 36)
- **B14 (equality and copying).** Two `SavedDisplay` values are equal iff:
  `Document` equal (Core structural equality); `FramedNodeId` ordinal-equal;
  `Framings` have the same key set and, per key, ordinal sequence-equal id
  lists (order matters); `Owners` same key set and ordinal-equal values;
  `DockSpots` both null, or both non-null with the same key set and equal
  `DockSpot`s; `FloatSizes` likewise with `Size` equality. Dictionary
  insertion order never matters. The constructor copies every input
  collection (later changes to the sources are not visible).
- **B15 (wire names, informative).** The future store implementation
  serialises a `SavedDisplay` as a JSON object with keys `doc` (document, in
  the layout-tree wire shape of `specs/layout-tree.md` A1), `framed`,
  `framings`, `owners`, `dockSpots` (omitted when null), `floatSizes`
  (omitted when null). Nested shapes of dock spots, restore targets and sizes
  are decided by that feature (A10). Engine contains no JSON code.

### Display store (rules 34, 35, 37)
- **B16 (load).** `Load(key)` returns the record most recently saved under an
  equal `EngineKey` (an equal value; the in-memory fake returns the same
  instance), or null. Keys differing only in desktop id are different
  records.
- **B17 (save).** `Save(key, display)` replaces any previous record for
  `key`. Storage failures are swallowed; `Load` of an unreadable record
  returns null.
- **B18 (no fallback).** There is no lookup by bare display key (A11). File
  naming and location are the implementation's concern.

### Clock (rules 28–29)
- **B19.** `Now` is monotonic, in seconds. `After(seconds, action)` runs
  `action` exactly once on the engine thread no earlier than
  `Now + seconds`, never synchronously inside `After`. `seconds` ≤ 0 or NaN
  is treated as 0; `+∞` means never. There is no cancellation; the Engine
  guards stale callbacks itself.

### Capture (rules 22–27)
- **B20.** `Start` on an already-started window is a no-op. `Stop` clears the
  picture, rate and hold mark; `Latest` is then null. `SetRate` ignores
  non-finite or ≤ 0 fps and unstarted windows; the interval is `1/fps`.
  `Hold` changes the mark only for started windows; a change of the mark
  requests a fresh picture; setting the same value again does nothing.
- **B21.** `Latest(key)` is null until a picture has arrived for a started
  window. Serials are strictly increasing across all windows in arrival
  order. Picture arrival is reported to the Engine through #23's inbound
  `captureArrived`; this seam has no event.

### Renderer (rules 6, 38)
- **B22.** `Render(plan)` is synchronous; the plan is immutable and the
  renderer may keep it. One renderer per display/world.

### Fakes (findings worked examples)
- **B23 (FakeWindowSystem queue).** Every `ReadFrame`, `SetFrame` and
  `SetFrames` call is recorded immediately (`Reads` / `Writes`, with a fresh
  `BatchId` 1, 2, … per `SetFrames` call and null for `SetFrame`) and
  enqueued as one operation in a single FIFO. Nothing is applied and no
  completion runs until `Step`/`Pump`. `Step()` dequeues the oldest
  operation, applies it against the **current** fake state, then invokes its
  completion. `Step(key)` does the same for the oldest operation involving
  `key` (a batch counts as involving each of its keys). `Pump()` steps until
  the queue is empty, including operations enqueued by completions, and
  returns the number of operations run; if it would exceed 10 000 it throws
  `InvalidOperationException` (runaway-loop guard).
- **B24 (FakeWindowSystem write rule).** Applying a write to `key` with
  target `T`:
  1. key unmanaged, or `SetFails(key, true)` → result null, no change;
  2. non-finite component in `T` → result null, no change (B8);
  3. `SetRefusesWrites(key, true)` → result is the unchanged current frame;
  4. otherwise new frame = `(T.X, T.Y, clamp(T.Width), clamp(T.Height))`
     where each axis is first raised to `min` and then lowered to `max`, for
     whichever bounds are set; the result is the new frame.
  Reads return the current frame, or null if unmanaged or failing.
- **B25 (FakeWindowSystem scene).** `AddWindow` puts the key at the front of
  the OS z-order. `SetZOrder(list)` replaces the OS z-order with `list`
  followed by any managed windows not in it (in their previous relative
  order); unmanaged keys in `list` stay in the OS order but are filtered out
  by `FrontToBack`. `RemoveWindow` makes the key unmanaged (pending
  operations on it complete with null). `Focus` only appends to
  `FocusRequests` (no z-order change, nothing queued). `TabSibling` always
  returns null. `VisibleFraction` and `ParkingOrigin` use `Screens` via
  `ParkGeometry` (`ParkingOriginOverride` wins when set).
- **B26 (ManualClock).** `Now` starts at `start`. `After` stores
  `(due = Now + max(seconds, 0), seq)` (NaN → 0; `+∞` stored but never due).
  `Advance(dt)`: `dt` negative or NaN → `ArgumentOutOfRangeException`. Let
  `target = Now + dt`. Repeatedly take the pending action with the smallest
  `due ≤ target` (ties: lowest `seq`, i.e. registration order), set
  `Now = due`, remove it and invoke it; actions registered during this loop
  are eligible if due ≤ target. Finally `Now = target`. `Now` never
  decreases. `Advance(0)` runs everything due now (including `After(0, …)`).
- **B27 (FakeCapture).** Implements B20–B21. `Deliver(key, size)` on a
  started window sets `Latest = (size, ++serial)` (one global counter
  starting at 0, so the first serial is 1) and returns it; on an unstarted
  window it returns null and changes nothing. `RecaptureRequests(key)` counts
  `Start` of a not-yet-started window, each actual hold-mark change and each
  actual rate change since the window was last started.
- **B28 (in-memory stores).** `InMemoryLimitStore` implements B13 using
  `LimitRules`; `PersistCount` increments on each call that wrote an entry
  (every `Learn` that creates or updates an entry, every `Observe` that
  changes one). `InMemoryDisplayStore` implements B16–B18 and appends every
  `Save` to `SaveLog`.
- **B29 (RecordingRenderer).** Appends each plan to `Plans`; `Last` is the
  most recent or null.

## Edge cases & errors

- **E1.** Null `completion`, null `writes`, null `action`, null `plan`, null
  `display`/`key` in store calls, null `key` in window-system/capture calls:
  `ArgumentNullException`, nothing recorded or queued. (`AppId?` is the only
  nullable key parameter.)
- **E2.** A window removed after a write is issued but before it runs:
  result null (B7, B25).
- **E3.** A completion that throws: the exception propagates out of the
  fake's `Step`/`Pump` (and out of `Advance` for the clock); the operation is
  already dequeued. Real adapters log and continue (#11).
- **E4.** `VisibleFraction` of an empty rect is 0 (park-geometry B23).
- **E5.** `SetFrames` with one entry is legal and behaves like `SetFrame` but
  delivers a one-element list.
- **E6.** `Learn` with a negative component when nothing is stored yields 0
  on that axis (max with 0).
- **E7.** An `EngineKey` whose display key contains `|` round-trips through
  `TryParse` because of the last-`|` split.

## Acceptance criteria

All are xUnit tests in `tests/CanvasWm.Engine.Tests` (AC28 is a build /
inspection check), and run on the Mac with no OS access. Exact equality for
rects and sizes; fractions within `1e-12`.

### Value types
- **AC1** [mac] `new WindowKey("w1") == new WindowKey("w1")`, equal hashes;
  `new WindowKey("w1") != new WindowKey("W1")`; `ToString()` is `"w1"`; null →
  `ArgumentNullException`, `""` → `ArgumentException`. The same checks pass
  for `DisplayKey` and `AppId`. (B1)
- **AC2** [mac] `new EngineKey(new DisplayKey("ABC"), "7").Value == "ABC|7"`;
  equal to another built the same way; not equal to one with desktop `"8"`;
  desktop `"a|b"` and `""` → `ArgumentException`; `TryParse("ABC|7")` →
  true and an equal key; `TryParse("a|b|7")` → display `"a|b"`, desktop
  `"7"`; `TryParse` of `null`, `"ABC"`, `"ABC|"`, `"|7"` → false;
  `EngineKey.SingleDesktopId == "default"`. (B2, B3, E7)
- **AC3** [mac] `WindowInfo` with null key, app name or title throws
  `ArgumentNullException`; null `App` is accepted; two equal infos are equal.
  `new CaptureFrame(new Size(10, 20), 5) == new CaptureFrame(new Size(10, 20), 5)`.
- **AC4** [mac] `SavedDisplay`: two instances built from equal content but
  with dictionaries filled in different insertion orders are equal with equal
  hashes; a differing framing list order → not equal; `DockSpots` null vs
  empty → not equal; `FloatSizes` null vs empty → not equal; clearing the
  source dictionaries/lists after construction leaves the record unchanged;
  defaults give empty `Framings`/`Owners` and null `DockSpots`/`FloatSizes`;
  null document → `ArgumentNullException`. `DockSpot` built with a test-only
  subclass of `RestoreTarget` compares structurally. (B14)
- **AC5** [mac] `RenderPlan.Empty.Windows` is empty; two plans with equal
  window lists are equal; a null element throws `ArgumentNullException`.
  (B22)

### Limit rules and store
- **AC6** [mac] `LimitRules.Learn`: `(null, 500×300)` → `500×300`;
  `(500×300, 400×350)` → `500×350`; `(null, −5×10)` → `0×10`;
  `(500×300, NaN×10)` → `500×300`. (B11, E6)
- **AC7** [mac] `LimitRules.Observe`: `(500×350, 499×349)` → `500×350`;
  `(500×350, 498×350)` → `500×350` (exactly 2 is not more than 2);
  `(500×350, 497.9×350)` → `497.9×350`; `(500×350, 450×360)` → `450×350`;
  `(500×350, 499×100)` → `499×100` (both axes take the minimum);
  `(null, 100×100)` → null; `(500×350, ∞×1)` → `500×350`. (B12)
- **AC8** [mac] `InMemoryLimitStore`, app `"com.x"`, the sequence: learn
  `500×300` → `Minimum` = `500×300`; learn `400×350` → `500×350`; observe
  `499×349` → `500×350`; observe `450×360` → `450×350`. A fresh store:
  observe `100×100` → `Minimum` null and `Entries` empty; learn `NaN×1` →
  still null and `PersistCount == 0`. With app null: learn/observe change
  nothing, `Minimum(null)` is null. Two `AppId` instances with equal values
  share one entry. (B13, B28)

### Display store
- **AC9** [mac] `InMemoryDisplayStore`: `Save("ABC|7", rec)` then
  `Load(equal key)` → `rec`; `Load("ABC|8")` → null; `Load` of a never-saved
  key → null; a second `Save` to the same key replaces the first; `SaveLog`
  has both saves in order; null key or record → `ArgumentNullException`.
  (B16–B18)

### Clock
- **AC10** [mac] `ManualClock(1000)`: `After(0.25, f)`; `Advance(0.2)` → f not
  run, `Now == 1000.2`; `Advance(0.05)` → f run once, `Now == 1000.25`;
  a further `Advance(10)` → f not run again. (B19, B26)
- **AC11** [mac] From start 0: `After(0.3,a)`, `After(0.1,b)`, `After(0.1,c)`,
  `After(0.2,d)`; `Advance(1)` runs `b, c, d, a`; inside each action `Now`
  equals its due time (`0.1, 0.1, 0.2, 0.3`); afterwards `Now == 1`. (B26)
- **AC12** [mac] From start 0, an action due at 0.5 that calls
  `After(0.1, e)` and `After(0.9, g)` during `Advance(1)` → `e` runs in the
  same `Advance` with `Now == 0.6`; `g` (due 1.4) does not run until a later
  `Advance`. (B26)
- **AC13** [mac] `After(0, g)` does not run g during the call; `Advance(0)`
  runs it. `After(−1, h)` and `After(NaN, i)` behave as delay 0.
  `After(+∞, j)` never runs (`Advance(1e9)`) and `PendingCount` stays 1.
  `Advance(−0.1)` and `Advance(NaN)` throw `ArgumentOutOfRangeException`;
  `After(1, null)` throws `ArgumentNullException`. (B19, B26, E1)

### Window system fake
- **AC14** [mac] Asynchrony: with `w1` at `(0,0,800,600)`,
  `SetFrame(w1, (10,10,700,500), cb)` → `cb` not invoked, `FrameOf(w1)`
  unchanged, `Writes == [(w1, (10,10,700,500), null)]`, `PendingCount == 1`;
  `Pump()` returns 1 and `cb` receives `(10,10,700,500)` exactly once. The
  same "not during the call, exactly once on pump" holds for `ReadFrame`, an
  unknown key (result null) and an empty `SetFrames` batch (empty list).
  (B5, B23)
- **AC15** [mac] Clamp example: `SetConstraints(w1, min: (900, 0))`;
  `SetFrame(w1, (0,0,800,600))` → result `(0,0,900,600)`. With max
  `(500, 400)` instead → `(0,0,500,400)` for the same target.
  `SetRefusesWrites` → result is the pre-write frame and the frame is
  unchanged. `SetFails` → reads and writes give null. (B7, B24)
- **AC16** [mac] Removal race: `SetFrame(w1, …)`, then `RemoveWindow(w1)`,
  then `Pump()` → result null. A target with `NaN` X → null and the frame is
  unchanged. (B8, E2)
- **AC17** [mac] Per-window order: `SetFrame(w1, A, c1)`, `SetFrame(w1, B,
  c2)`, `ReadFrame(w1, c3)` → after `Pump`, completions ran `c1, c2, c3` and
  `c3` received `B`. Cross-window: queue `SetFrame(w1,…)` then
  `SetFrame(w2,…)`; `Step(w2)` completes only w2's write; `Step(w1)` then
  completes w1's; `Step(w1)` again returns false. (B6, B23)
- **AC18** [mac] Batch: windows w1 (min width 900) and w2, unknown w9;
  `SetFrames([(w1,(0,0,800,600)), (w9,R), (w2,(800,0,640,900))], cb)` →
  `Writes` gains three records sharing one non-null `BatchId`; after `Pump`,
  `cb` was invoked once with
  `[(w1,(0,0,900,600)), (w9,null), (w2,(800,0,640,900))]` in that order. A
  batch with a duplicate key, or with a null key, throws `ArgumentException`
  and records/queues nothing. A `SetFrame(w2, X)` issued after the batch is
  applied after it (a later `ReadFrame(w2)` sees `X`). (B9, B23)
- **AC19** [mac] Pump re-entrancy: a completion that issues another
  `SetFrame` → the same `Pump()` runs it too and returns 2. A completion that
  always re-issues itself makes `Pump()` throw `InvalidOperationException`.
  (B23)
- **AC20** [mac] Z-order: with managed B then A added (A frontmost),
  `SetZOrder([A, X, B])` where X is unmanaged → `FrontToBack() == [A, B]`.
  After `AddWindow(C)`, `FrontToBack()[0] == C`. `IsShowing` is false for an
  unknown key and after `SetShowing(…, false)`, true for a showing managed
  window. (B10, B25)
- **AC21** [mac] Geometry: with `Screens = [(0,0,1440,900),
  (1440,0,1920,1080)]`, `VisibleFraction((1340,0,200,100)) == 1` and
  `VisibleFraction((−100,0,200,100)) == 0.5` (within 1e-12);
  `ParkingOrigin == (3359, 1079)`; with `Screens = []` it is
  `(10000, 10000)`; with `ParkingOriginOverride = (5,5)` it is `(5,5)`.
  `Pointer` and `MouseDown` return the values set. `TabSibling(w1)` is null.
  (B10, B25)
- **AC22** [mac] `Focus(w1)` and `Focus(unknown)` append to `FocusRequests`,
  do not throw, leave `FrontToBack()` unchanged and queue nothing
  (`PendingCount` unchanged). (B10, B25)
- **AC23** [mac] Null checks: `SetFrame(w1, r, null)`, `ReadFrame(null, cb)`,
  `SetFrames(null, cb)`, `SetFrames([], null)`, `Focus(null)` throw
  `ArgumentNullException` and leave `Writes`, `Reads`, `FocusRequests` and
  `PendingCount` unchanged. (E1)

### Capture, renderer
- **AC24** [mac] `FakeCapture`: `Start(w1)`; `Latest(w1)` null;
  `SetRate(w1, 10)` → `Interval(w1) == 0.1` (1e-12); `SetRate(w1, 0)`,
  `SetRate(w1, −1)`, `SetRate(w1, NaN)` → still 0.1; `SetRate(w2, 5)` (not
  started) → `Interval(w2)` null. `Deliver(w1, 300×200)` → serial 1;
  `Start(w2)`, `Deliver(w2, …)` → serial 2; `Deliver(w1, …)` → serial 3;
  `Latest(w1)` has serial 3. `Hold(w1, true)` twice → `IsHeld(w1)` and
  `RecaptureRequests(w1)` grew by exactly 1. `Stop(w1)` → `Latest(w1)` and
  `Interval(w1)` null, `IsHeld(w1)` false. `Deliver` to an unstarted key →
  null. (B20, B21, B27)
- **AC25** [mac] `RecordingRenderer`: `Last` null initially; after two
  renders `Plans` has both in order and `Last` is the second;
  `Render(null)` throws `ArgumentNullException`. (B22, B29)

### Contract conformance and architecture
- **AC26** [mac] Each fake is assignable to its interface (`IWindowSystem`,
  `ICapture`, `IClock`, `IRenderer`, `ILimitStore`, `IDisplayStore`), checked
  in a test.
- **AC27** [mac] Reflection on `CanvasWm.Engine`: the six interfaces are
  public and in namespace `CanvasWm.Engine.Services`; no exported type is
  named `IHotkeys` or `IStore`; the existing `EngineReferencesOnlyCore` test
  passes, and the set of referenced `CanvasWm.*` assemblies is now exactly
  `{ "CanvasWm.Core" }`.
- **AC28** [mac] Build/inspection: `dotnet build CanvasWm.slnx` exits 0 with 0
  warnings; `CanvasWm.Engine.csproj` still targets `net10.0` with exactly one
  `ProjectReference` (Core) and no `PackageReference`; no file under
  `src/CanvasWm.Core` is changed by this feature; no file in `src/` or
  `tests/` contains `TrellisWM` or `Licensing`; no `-windows` TFM appears in
  Engine or Engine.Tests.

Count: 28 [mac] (AC1–AC28), 0 [windows]. Real adapters' conformance to
B5–B10, B13, B16–B22 is verified on Windows in #10, #11 and #14.

## Assumptions

- **A1. Callbacks, not `Task`, for frame reads/writes.** *Needs human review.*
  Chosen: `Action<Rect?>` / `Action<IReadOnlyList<FrameWriteResult>>`. Why:
  the Engine is a single-threaded state machine; a callback contract states
  "on the engine thread, exactly once, never re-entrant" directly, while
  `Task` continuations depend on the ambient `SynchronizationContext` and run
  inline when the task is already complete (a re-entrancy hazard), which
  would push `async` state through every Engine method and make the
  deterministic pump fake harder. #23 can add `Task` wrappers later without
  changing the seam.
- **A2. Batch write added to the seam** (the studied app writes one window
  per call). Approved scope. Semantics are "N per-window writes completing
  together", so no policy enters the adapter. Duplicate keys are rejected
  rather than merged.
- **A3. Per-window ordering covers reads too** (B6). The study shows writes
  serialised per window and reads on a per-app queue; CanvasWM orders both per
  window so a read after a write is meaningful.
- **A4. Non-finite targets complete with null** (B8). A CanvasWM safety
  choice; the study shows no validation.
- **A5. Keys are sealed record classes**, not record structs, so that no
  default/empty key can exist and `WindowKey?` reads naturally. `AppId` is a
  type rather than `string?` for the same reason.
- **A6. Window-key format belongs to #10.** Recommended `"w" + lowercase hex
  HWND`; the app layer must report removal before re-admitting a reused HWND.
- **A7. `ParkingOrigin` = `ParkGeometry.DefaultFallbackOrigin`** of the current
  display full frames, so the rule lives in Core and Win32 only supplies
  monitor rects (consistent with park-geometry A5). #14 may revisit.
- **A8. Owner strings are `EngineKey.Value`s.** The study only says "owner
  string used to tell which engine a window belongs to". Kept as `string` so
  this spec does not commit further; multi-display features decide.
- **A9. `Hold` semantics.** The effect of the hold mark is Unknown in the
  study. Default: the mark is stored per started window and a change requests
  one fresh picture ("keep the last picture available and refresh it once
  now"). On Windows (DWM thumbnails) it is likely advisory.
- **A10. Wire shape only at the top level** (B15). Nested shapes and the
  sanitising of engine keys into file names (they contain `|`, and display
  device paths contain `\`) are left to the store implementation in
  Win32/App.
- **A11. No fallback lookup by bare display key.** The studied app does one,
  apparently to migrate an older per-display scheme; CanvasWM has no older
  format. *Needs human review* if per-desktop layouts are dropped (then use
  `EngineKey.SingleDesktopId`).
- **A12. Fakes live in `tests/CanvasWm.Engine.Tests/Fakes/`.** A shared
  test-support project would be cleaner if Win32.Tests needs them; that is a
  change to solution-skeleton and must be raised first.
- **A13. `LimitRules` in Engine, not Core.** It is pure but specific to the
  store seam; keeping it in Engine avoids touching Core. Win32/App store
  implementations call it. Non-finite inputs are ignored (CanvasWM choice;
  the study's NaN behaviour is not known).
- **A14. `FakeWindowSystem.Focus` does not reorder windows.** Real focus is
  asynchronous and reported inbound; tests that need the effect call
  `SetZOrder` and the Engine's inbound focus method themselves.
- **A15. Clock edge values** (≤ 0 or NaN → 0, `+∞` → never) are CanvasWM
  choices.
- **A16. `WindowPlan` fields** (`Key`, `Frame`, `Focused`) are the smallest
  useful placeholder; overlay features extend `RenderPlan` and must report
  that as a change to this spec.

### Reported for review (no change made here)
- Feature-queue row #11 lists "refusal retries" under Win32. Per findings
  rule 17 and B7 here, refusal detection and retry are Engine policy (Win32
  only reports the actual frame). #11's spec should keep retries out of
  Win32.
- Display full frames for park scoring (`otherDisplays`) are not exposed by
  `IWindowSystem` (nor by the studied app's seam); #23 must decide how the
  Engine receives them (likely with the visible area at construction /
  `visibleAreaChanged`).

## Traceability

| Spec item | Findings rule |
|---|---|
| B1–B4, AC1, AC2, A5, A6 | 1–4; Windows notes (keys, engine key) |
| B5–B7, AC14, AC17, A1, A3 | 10, 16, 17 |
| B8, AC16, A4 | 17 (CanvasWM safety addition) |
| B9, AC18, A2 | 17 (no batch observed); Windows notes (DeferWindowPos); approved scope |
| B10, AC20–AC22, A7, A14 | 12–15, 18–21; park-geometry B23/B25 |
| B11–B13, AC6–AC8, A13 | 4, 30–33; worked examples (LimitStore) |
| B14, B15, AC3, AC4, A8, A10 | 36, 42; Data model; layout-tree A1 |
| B16–B18, AC9, A11 | 7, 34, 35, 37; worked examples (DisplayStore) |
| B19, B26, AC10–AC13, A15 | 28, 29; worked examples (Clock) |
| B20, B21, B27, AC24, A9 | 22–27; worked examples (Capture) |
| B22, B29, AC5, AC25, A16 | 6, 38 |
| B23–B25, B28, AC14–AC23 | worked examples (fakes) |
| AC26–AC28 | CLAUDE.md architecture; solution-skeleton B6, B11, AC7; 39–40 (no input seam), 43 (gate excluded) |
