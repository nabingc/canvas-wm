# Spec: win32-frame-writer

## Goal

Feature #11. Implement the Engine seam `IWindowSystem` (specs/engine-interfaces.md)
for Windows as `Win32WindowSystem` in `CanvasWm.Win32`, together with the
small pure decisions it needs, which go in Core so they can be tested on the Mac:

- **Frame writes** (`SetFrame`, `SetFrames`). They run on one dedicated
  writer thread and are batched with `DeferWindowPos`, falling back to
  `SetWindowPos` per window. Before a write the adapter restores a maximised
  window and compensates for the DWM invisible border. After the write it
  reads the frame back as DIPs. Each write may include **one** fixed settle
  pass and **one** off-screen position re-push (amended B7, see "Required
  edits to earlier specs").
- **Frame reads** (`ReadFrame`), in the same FIFO as the writes.
- **Focus**: `SetForegroundWindow`, with no synthetic input.
- **Queries** (`FrontToBack`, `IsShowing`, `VisibleFraction`, `Pointer`,
  `MouseDown`, `ParkingOrigin`, `TabSibling`). They are answered
  synchronously on the engine thread.
- **`DipSpace.ToPixels`**: the inverse of #10's `ToDip`.

The adapter is mechanism only. Refusal detection, rechecks, limit learning,
park decisions and "which windows to write" belong to the Engine (#26). The
only thresholds the adapter applies are the Core predicates of the amended B7.

Out of scope:
- Composition, the message loop and the `post` delegate (#29).
- How the tracker exposes window identity to the App (reported for #29).
- Crash-safe restore of parked windows and the Windows park overlap choice
  (#14).
- Live capture (#16).
- Hotkey registration (#29). The "foreground after hotkey" check uses a
  test-only hotkey probe.
- The studied per-process accessibility-mode hold (findings rule 6). It has
  no Windows equivalent (A19).

## Placement

### Core (pure, Mac-tested)
- Project `src/CanvasWm.Core`, new namespace **`CanvasWm.Core.Frames`**, with
  new files:
  - `src/CanvasWm.Core/Frames/FrameSettle.cs`
  - `src/CanvasWm.Core/Frames/DisplayChoice.cs`
  - `src/CanvasWm.Core/Frames/FrameInsets.cs`
  - `src/CanvasWm.Core/Frames/PhysicalCoordinates.cs`
- Tests: `tests/CanvasWm.Core.Tests/Frames/*Tests.cs`, namespace
  `CanvasWm.Core.Tests.Frames`.
- Reuses, unchanged: `CanvasWm.Core.Geometry.Rect`, `Point`;
  `CanvasWm.Core.Parking.ParkGeometry.IntersectionArea`, `VisibleFraction`,
  `DefaultFallbackOrigin`.
- No existing Core file changes.

### Win32 (adapter)
- Project `src/CanvasWm.Win32` (`net10.0-windows`). It already references
  Core and Engine, allows unsafe code and has `InternalsVisibleTo`. The
  csproj does not change.
- New public files:
  - `src/CanvasWm.Win32/Windows/Win32WindowSystem.cs`
  - `src/CanvasWm.Win32/Windows/IManagedWindowMap.cs` (with
    `ManagedWindowIdentity`)
- New internal files:
  - `src/CanvasWm.Win32/Windows/INativeWindowOps.cs` (with `MonitorInfo`)
  - `src/CanvasWm.Win32/Windows/NativeWindowOps.cs` (the production
    implementation)
  - `src/CanvasWm.Win32/Windows/FrameWriteRunner.cs`
  - `src/CanvasWm.Win32/Windows/WriterQueue.cs`
  - `src/CanvasWm.Win32/Windows/FrameMath.cs`
  - `src/CanvasWm.Win32/Windows/WindowGeometry.cs` (extracted from
    `FactGatherer`, see C4)
  - `src/CanvasWm.Win32/Windows/Monitors.cs`
- Changed files (complete list):

  | # | File | Change |
  |---|---|---|
  | C1 | `Coordinates/DipSpace.cs` | **Additive**: `ToPixels(Rect, PixelRect, uint)` and `ToDip(int, int, PixelRect, uint)` (point). `ScaleFor` and `ToDip(PixelRect, …)` are unchanged. |
  | C2 | `Interop/NativeMethods.User32.cs` | New entry points (B21). The class doc comment becomes: "user32 entry points. Fact gathering and events use none that send a window message; frame writing and focus (#11) do." |
  | C3 | `Interop/NativeConstants.cs`, `Interop/NativeStructs.cs` | New constants (B21) and `NativePoint { int X; int Y; }`. |
  | C4 | `Windows/FactGatherer.cs` | Internal refactor with no behaviour change. The frame part of `ReadGeometry` (EFB, else `GetWindowRect`, then `MonitorFromWindow`, `GetMonitorInfoW`, `GetDpiForMonitor`, then `ToDip`) moves to `WindowGeometry.ReadDipFrame(nint)`. `FactGatherer` calls it, so facts and read-backs use one code path (#10 B10). The full-screen computation stays in `FactGatherer`. |

- Tests: `tests/CanvasWm.Win32.Tests/FrameWriter/*Tests.cs`, namespace
  `CanvasWm.Win32.Tests.FrameWriter`. They reuse the existing helpers in
  `tests/CanvasWm.Win32.Tests/WindowEnum/` (`MessageLoopThread`,
  `ProbeProcess`, `Native`, `Rig`) unchanged. New test-only fakes go in
  `tests/CanvasWm.Win32.Tests/FrameWriter/Fakes.cs`.
- Manual checklist: `windows-verification/win32-frame-writer.md`. It
  contains every [windows-manual] AC with the steps written here.
- No file in `src/CanvasWm.Engine`, `tests/CanvasWm.Engine.Tests`,
  `src/CanvasWm.App` or `Directory.Build.props` changes in this feature. The
  proposed Engine tolerance change (R2) is a separate, human-approved change.
- Depends on, unchanged except as listed under "Required edits to earlier
  specs":
  - `specs/engine-interfaces.md`: `IWindowSystem`, `FrameWrite`,
    `FrameWriteResult`, `WindowKey`, B5–B10.
  - `specs/win32-window-enum.md`: `DipSpace`, `PixelRect`,
    `HandleConversion`, `DpiScope`, interop style B1, DIP space B10/A8, the
    `post` contract A13.
  - `specs/park-geometry.md`: `VisibleFraction`, `DefaultFallbackOrigin`,
    `IntersectionArea`.
  - `specs/engine-frame-writer.md`: what the Engine does with results.
- No text in `src/` or `tests/` may contain the studied app's name or the
  name of its licence module (CLAUDE.md).

## Public API

All public members carry XML docs. A null non-nullable reference argument
throws `ArgumentNullException`.

### Core: `CanvasWm.Core.Frames`

```csharp
namespace CanvasWm.Core.Frames;

/// <summary>The fixed in-write correction rules a window-system adapter may apply (amended
/// engine-interfaces B7). Pure.</summary>
public static class FrameSettle
{
    /// <summary>A read-back that differs from the target by strictly more than this on any of
    /// X, Y, Width, Height triggers the one settle pass: 1.0 DIP.</summary>
    public const double Tolerance = 1.0;

    /// <summary>A write is an off-screen write when the target's visible fraction is strictly
    /// below this: 0.05.</summary>
    public const double OffScreenWriteBelow = 0.05;

    /// <summary>An off-screen write is re-pushed once when the result's visible fraction is
    /// strictly above this: 0.02.</summary>
    public const double RepushAbove = 0.02;

    /// <summary>True when |actual.c - target.c| &gt; Tolerance for some c in {X, Y, Width,
    /// Height}. A NaN difference counts as exceeding (true).</summary>
    public static bool NeedsSecondPass(Rect target, Rect actual);

    /// <summary>targetVisibleFraction &lt; OffScreenWriteBelow. NaN gives false.</summary>
    public static bool IsOffScreenWrite(double targetVisibleFraction);

    /// <summary>offScreenWrite &amp;&amp; resultVisibleFraction &gt; RepushAbove. NaN gives
    /// false.</summary>
    public static bool NeedsRepush(bool offScreenWrite, double resultVisibleFraction);
}

/// <summary>Which display's coordinate mapping applies to a rect (B9). Pure.</summary>
public static class DisplayChoice
{
    /// <summary>Index into displayFrames of the frame with the largest positive
    /// ParkGeometry.IntersectionArea with rect (ties: lowest index). If none intersects:
    /// currentIndex when 0 &lt;= currentIndex &lt; Count, else 0 when the list is non-empty,
    /// else -1.</summary>
    /// <param name="rect">The rect, in DIPs.</param>
    /// <param name="displayFrames">The displays' full frames, in DIPs.</param>
    /// <param name="currentIndex">The display the window is on now, or -1 if unknown.</param>
    /// <exception cref="ArgumentNullException">displayFrames is null.</exception>
    public static int ForRect(Rect rect, IReadOnlyList<Rect> displayFrames, int currentIndex);
}

/// <summary>Widths of a window's invisible border on each side, in device pixels (opaque
/// integers to Core). Never negative when produced by Measure or Rescale.</summary>
public readonly record struct FrameInsets(int Left, int Top, int Right, int Bottom)
{
    /// <summary>(0, 0, 0, 0).</summary>
    public static FrameInsets Zero { get; }

    /// <summary>Insets between an outer rect and the visible rect inside it (edges as
    /// left/top inclusive, right/bottom exclusive): Left = visibleLeft - outerLeft,
    /// Top = visibleTop - outerTop, Right = outerRight - visibleRight,
    /// Bottom = outerBottom - visibleBottom. Each side is computed in 64-bit and clamped to
    /// [0, int.MaxValue].</summary>
    public static FrameInsets Measure(int outerLeft, int outerTop, int outerRight, int outerBottom,
                                      int visibleLeft, int visibleTop, int visibleRight, int visibleBottom);

    /// <summary>Each side times toDpi / fromDpi, rounded half away from zero. Returns this
    /// unchanged when fromDpi == toDpi.</summary>
    /// <exception cref="ArgumentOutOfRangeException">fromDpi or toDpi is 0.</exception>
    public FrameInsets Rescale(uint fromDpi, uint toDpi);
}

/// <summary>Safe range for window positions in device pixels (B12). Pure.</summary>
public static class PhysicalCoordinates
{
    /// <summary>30000: positions are kept in [-30000, 30000]. That is well inside the 16-bit
    /// range that window messages pack positions into, and away from the minimised sentinel
    /// -32000.</summary>
    public const int Limit = 30000;

    /// <summary>Math.Clamp(value, -Limit, Limit).</summary>
    public static int Clamp(int value);
}
```

### Win32: coordinates (additive to #10)

```csharp
namespace CanvasWm.Win32.Coordinates;

public static class DipSpace
{
    // existing: BaseDpi, ScaleFor, ToDip(PixelRect, PixelRect, uint) — unchanged

    /// <summary>Inverse of ToDip for one monitor. With M = monitorBounds and s = ScaleFor(dpi):
    /// Left = R(M.Left + (d.X - M.Left)*s), Top = R(M.Top + (d.Y - M.Top)*s),
    /// Right = R(M.Left + (d.X + d.Width - M.Left)*s), Bottom = R(M.Top + (d.Y + d.Height - M.Top)*s),
    /// where R rounds half away from zero and then saturates to [int.MinValue, int.MaxValue].
    /// Edges are rounded, not sizes, so rects that share an edge in DIPs share it in pixels.</summary>
    /// <exception cref="ArgumentOutOfRangeException">monitorDpi is 0, or a component of dips
    /// is NaN or infinite.</exception>
    public static PixelRect ToPixels(Rect dips, PixelRect monitorBounds, uint monitorDpi);

    /// <summary>Point form of ToDip: (M.Left + (x - M.Left)/s, M.Top + (y - M.Top)/s).</summary>
    /// <exception cref="ArgumentOutOfRangeException">monitorDpi is 0.</exception>
    public static Point ToDip(int x, int y, PixelRect monitorBounds, uint monitorDpi);
}
```

### Win32: the window system

```csharp
namespace CanvasWm.Win32.Windows;

/// <summary>Identity of a managed window as recorded when the tracker admitted it.</summary>
/// <param name="Handle">The window handle (HandleConversion form).</param>
/// <param name="ProcessId">Owning process id at admission.</param>
/// <param name="ThreadId">Owning thread id at admission.</param>
public readonly record struct ManagedWindowIdentity(ulong Handle, int ProcessId, int ThreadId);

/// <summary>Key/handle lookup over the managed set. The Win32 adapter never parses window
/// keys; the App composition (#29) implements this over the Engine tracker. Called only on
/// the engine thread; must be cheap and must not call back into the window system.</summary>
public interface IManagedWindowMap
{
    /// <summary>Identity of the managed window with this key, or null if not managed.</summary>
    ManagedWindowIdentity? Resolve(WindowKey key);

    /// <summary>Key of the managed window with this handle, or null if not managed.</summary>
    WindowKey? KeyOf(ulong handle);
}

/// <summary>IWindowSystem over user32/dwmapi (B1-B20). Construct on the engine thread.
/// Frame reads, writes and focus run on one private writer thread, in issue order;
/// completions come back through post. Queries run synchronously on the calling (engine)
/// thread and send no window message.</summary>
public sealed class Win32WindowSystem : IWindowSystem, IDisposable
{
    /// <param name="post">Schedules an action to run LATER on the engine thread, FIFO, never
    /// inline. Must be callable from any thread (the writer thread calls it).</param>
    /// <param name="windows">The managed-window map.</param>
    /// <param name="log">Optional diagnostics sink (focus failures, completion exceptions).
    /// Called on the engine thread.</param>
    public Win32WindowSystem(Action<Action> post, IManagedWindowMap windows,
                             Action<string>? log = null);

    /// <summary>Raised (posted, on the engine thread) once per Focus call on a managed key,
    /// before or while the window is raised. For overlays; the Engine does not use it.</summary>
    public event Action<WindowKey>? Raising;

    // IWindowSystem — semantics in Behaviour:
    public void Focus(WindowKey key);                                   // B17
    public IReadOnlyList<WindowKey> FrontToBack();                      // B18
    public bool IsShowing(WindowKey key);                               // B18
    public double VisibleFraction(Rect rect);                           // B19
    public void ReadFrame(WindowKey key, Action<Rect?> completion);     // B5, B16
    public void SetFrame(WindowKey key, Rect target, Action<Rect?> completion);          // B5-B15
    public void SetFrames(IReadOnlyList<FrameWrite> writes,
                          Action<IReadOnlyList<FrameWriteResult>> completion);           // B5-B15
    public WindowKey? TabSibling(WindowKey key);                        // B20
    public Point Pointer { get; }                                       // B20
    public bool MouseDown { get; }                                      // B20
    public Point ParkingOrigin { get; }                                 // B19

    /// <summary>Stops the writer (B4). Idempotent.</summary>
    public void Dispose();
}
```

### Win32: internal surface (names are binding; tests use them)

```csharp
namespace CanvasWm.Win32.Windows;

/// <summary>One monitor as seen by one operation.</summary>
internal readonly record struct MonitorInfo(nint Handle, PixelRect Bounds, uint Dpi, Rect DipFrame);

/// <summary>Every OS call the window system makes, so the algorithms are testable with a
/// fake. NativeWindowOps is the production implementation (B21).</summary>
internal interface INativeWindowOps
{
    // monitors and z-order (any thread)
    IReadOnlyList<MonitorInfo> Monitors();                 // B19
    IReadOnlyList<nint> TopLevelWindows();                 // EnumWindows order
    // per window
    bool IsLive(nint h, int processId, int threadId);      // IsWindow && GetWindowThreadProcessId matches
    bool IsHung(nint h);
    bool IsMinimized(nint h);
    bool IsMaximized(nint h);
    bool IsVisible(nint h);
    bool IsCloaked(nint h);                                // DWMWA_CLOAKED != 0; read failure → false
    int MonitorIndexOf(nint h, IReadOnlyList<MonitorInfo> monitors); // MonitorFromWindow(NEAREST) → index, or -1
    PixelRect? WindowRect(nint h);                         // GetWindowRect
    PixelRect? VisibleRect(nint h);                        // DWMWA_EXTENDED_FRAME_BOUNDS
    Rect? ReadDipFrame(nint h);                            // WindowGeometry.ReadDipFrame (C4)
    void RestoreNoActivate(nint h);                        // ShowWindow(SW_SHOWNOACTIVATE)
    void Restore(nint h);                                  // ShowWindow(SW_RESTORE)
    bool TryDeferBatch(IReadOnlyList<(nint Hwnd, PixelRect Outer)> entries); // B10
    bool SetWindowPos(nint h, PixelRect outer, bool positionOnly);           // B10
    bool SetForeground(nint h);                            // SetForegroundWindow
    // input
    (int X, int Y)? CursorPos();                           // GetCursorPos
    nint MonitorFromPoint(int x, int y);                   // MONITOR_DEFAULTTONEAREST
    bool ButtonsSwapped();                                 // GetSystemMetrics(SM_SWAPBUTTON) != 0
    bool AsyncKeyDown(int virtualKey);                     // (GetAsyncKeyState(vk) & 0x8000) != 0
}

/// <summary>Pure pixel helpers (B8, B12).</summary>
internal static class FrameMath
{
    /// <summary>(v.Left - i.Left, v.Top - i.Top, v.Right + i.Right, v.Bottom + i.Bottom).</summary>
    internal static PixelRect Expand(PixelRect visible, FrameInsets insets);
    /// <summary>Moves r (size kept) so that Left and Top are PhysicalCoordinates.Clamp'ed.</summary>
    internal static PixelRect ClampOrigin(PixelRect r);
    /// <summary>VK_RBUTTON (0x02) when swapped, else VK_LBUTTON (0x01).</summary>
    internal static int PrimaryButtonKey(bool swapped);
}

/// <summary>A prepared write entry. Hwnd 0 / Identity null = not resolvable.</summary>
internal readonly record struct WriteRequest(WindowKey Key, ManagedWindowIdentity? Identity, Rect Target);

/// <summary>Runs one batch on the calling (writer) thread: B6-B15. Never throws.</summary>
internal sealed class FrameWriteRunner
{
    internal FrameWriteRunner(INativeWindowOps ops);
    internal IReadOnlyList<Rect?> Run(IReadOnlyList<WriteRequest> batch);   // one result per entry, input order
    internal Rect? Read(ManagedWindowIdentity? identity);                     // B16
    internal bool? Focus(ManagedWindowIdentity identity);                     // B17; null = not attempted
}

/// <summary>Single FIFO worker thread (B2-B4).</summary>
internal sealed class WriterQueue : IDisposable
{
    internal WriterQueue(string threadName);
    /// <summary>Enqueue work; false (nothing enqueued) after Dispose.</summary>
    internal bool TryEnqueue(Action work, Action onDropped);
    internal int PendingCount { get; }
    public void Dispose();
}

// Win32WindowSystem test constructor:
// internal Win32WindowSystem(Action<Action> post, IManagedWindowMap windows, Action<string>? log,
//                            INativeWindowOps ops, Action<Action>? runOnWriter = null);
// runOnWriter null = real WriterQueue; tests pass a recording runner to step the writer by hand.
// internal bool? LastFocusSucceeded { get; }   // B17
```

## Behaviour

Notation:
- "Engine thread" is the thread that constructed the instance.
- "Writer" is the writer queue (B2).
- `ops` is `INativeWindowOps`.
- "Monitors" is `ops.Monitors()` taken at the stated moment, and "DIP
  frames" are its `DipFrame`s in order.
- "Post" means the injected `post`.

### A. Threading, ordering, completion

- **B1 (call-time work on the engine thread).** `ReadFrame`, `SetFrame` and
  `SetFrames` do the following synchronously, in this order:
  1. Argument checks (E1). For `SetFrames`, any entry with a null `Key`, or
     two entries with equal keys → `ArgumentException`. In every failure case
     nothing is resolved, queued or posted.
  2. Resolve each key with `windows.Resolve(key)` (on the engine thread).
  3. Enqueue **one** work item on the writer that carries the keys,
     identities (null if unresolved), targets and the completion.

  They never call an OS function that sends a window message, and never call
  the completion.
- **B2 (writer).** One dedicated background thread (`IsBackground = true`,
  name `"CanvasWm frame writer"`) runs work items strictly FIFO, one at a
  time. `SetFrame` and `SetFrames` items, `ReadFrame` items and `Focus` items
  share this one queue. Each item runs inside a `DpiScope` (#10 B10.1), so
  every coordinate API returns physical pixels. The writer uses no timer, no
  sleep and no stopwatch.
- **B3 (completion).** Each `ReadFrame`, `SetFrame` and `SetFrames` item
  posts **exactly one** action that invokes its completion. This holds when
  the item throws (affected results are null) and when the item is dropped
  (B4). Because both the writer and `post` are FIFO, completions run in issue
  order. This satisfies engine-interfaces B5 (never inline, exactly once, on
  the engine thread) and B6 (per-window order, read-after-write). Behaviour
  of the posted action:
  - It invokes the completion inside `try`. An exception thrown by the
    completion is caught and passed to `log` (when given) as one line
    containing the exception type name, and is otherwise swallowed (E3 of
    engine-interfaces: "real adapters log and continue").
  - `SetFrame` gets `results[0]`.
  - `SetFrames` gets a new list of `FrameWriteResult(key_i, result_i)` in
    input order, with `writes.Count` entries (an empty batch gives an empty
    list).
- **B4 (dispose).** `Dispose()` marks the instance disposed and disposes the
  writer queue. Items not yet started are dropped: their completions are
  posted with all-null results, and dropped Focus items do nothing. The item
  that is running finishes normally. `Dispose` does not wait for it (no
  join, A14). After `Dispose`:
  - `ReadFrame`, `SetFrame` and `SetFrames` still validate their arguments,
    then post their completion directly with null (or all-null) results and
    make no OS call;
  - `Focus` does nothing;
  - the queries keep working.

  A second `Dispose` does nothing.

### B. The write (one batch; `SetFrame` is a batch of one)

`FrameWriteRunner.Run(batch)` on the writer:

- **B5 (snapshot).** Take Monitors once for the whole batch, at the start.
  If the list is empty, every result is null and nothing is written.
- **B6 (eligibility, per entry, in input order).** An entry's result is
  null, and the entry takes no further part, if **any** of these holds:
  1. its identity is null (the key was not managed at issue time);
  2. any target component is NaN or ±∞ (engine-interfaces B8);
  3. `!ops.IsLive(h, pid, tid)` (window gone, or handle reused by another
     thread or process);
  4. `ops.IsHung(h)`;
  5. `ops.IsMinimized(h)` (A8).

  Here `h = HandleConversion.ToHwnd(identity.Handle)`. Inaccessible
  (elevated) windows are not checked again here (A3). If they reach the
  write, they fail in B10.
- **B7 (maximised).** For each eligible entry with `ops.IsMaximized(h)`,
  call `ops.RestoreNoActivate(h)` (`ShowWindow(h, SW_SHOWNOACTIVATE)`)
  before anything in B8 is measured. A maximised window is never put into a
  `DeferWindowPos` batch before it has been restored.
- **B8 (outer rect, per eligible entry).**
  1. `cur = ops.MonitorIndexOf(h, monitors)`.
  2. `m = DisplayChoice.ForRect(target, dipFrames, cur)` (B9). `M =
     monitors[m]`.
  3. `vis = DipSpace.ToPixels(target, M.Bounds, M.Dpi)`.
  4. `insets = FrameInsets.Measure(outer = ops.WindowRect(h), visible =
     ops.VisibleRect(h))`. If either read fails, `insets = FrameInsets.Zero`.
  5. If `cur ≥ 0` and `monitors[cur].Dpi != M.Dpi`, then `insets =
     insets.Rescale(monitors[cur].Dpi, M.Dpi)`.
  6. `outer = FrameMath.ClampOrigin(FrameMath.Expand(vis, insets))` (B12).
  7. `offScreen = FrameSettle.IsOffScreenWrite(ParkGeometry.VisibleFraction(
     target, dipFrames))`.
- **B9 (monitor choice).** `DisplayChoice.ForRect` returns:
  - the frame with the largest positive intersection area (the lowest index
    on a tie);
  - otherwise `currentIndex` if it is in range;
  - otherwise 0 for a non-empty list;
  - otherwise −1.

  Park targets overlap their display by `ParkOptions.EdgeOverlap` (1 DIP) of
  the visible frame. So they normally pick the display they are parked
  against, and the window keeps that display's DPI while parked (A5).
- **B10 (first write).** Let `n` be the number of eligible entries.
  - `n == 0`: no write.
  - `n == 1`: `ops.SetWindowPos(h, outer, positionOnly: false)`.
  - `n ≥ 2`: `ops.TryDeferBatch(entries in input order)`. The production
    version does `BeginDeferWindowPos(n)`, then one `DeferWindowPos(hdwp, h,
    0, outer.Left, outer.Top, outer.Width, outer.Height, flags)` per entry,
    then `EndDeferWindowPos`. It returns false if `Begin` returns NULL, any
    `DeferWindowPos` returns NULL (the system has then destroyed the batch;
    stop deferring) or `End` returns FALSE. On **false**, every eligible entry
    is written individually with `ops.SetWindowPos`, in input order. Windows
    that were already moved are simply written again.

  `flags = SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER` (0x0004 |
  0x0010 | 0x0200). `positionOnly` adds `SWP_NOSIZE` (0x0001). These flags
  are never used: `SWP_NOSENDCHANGING`, `SWP_ASYNCWINDOWPOS`,
  `SWP_NOCOPYBITS`, `SWP_SHOWWINDOW` and `SWP_FRAMECHANGED`. An entry whose
  individual `SetWindowPos` returns false gets a null result and takes no
  further part. A successful batch counts as a successful write for every
  entry in it.
- **B11 (read-back, settle, re-push; per written entry, in input order).**
  1. `r = ops.ReadDipFrame(h)`. If it is null, the result is null; next
     entry.
  2. **Settle.** If `FrameSettle.NeedsSecondPass(target, r)`:
     - recompute `outer2` with B8.1 and B8.3–B8.6, measuring again in the
       window's current state (`m` from B8.2 is kept);
     - call `ops.SetWindowPos(h, outer2, false)`;
     - if that returns true, `r2 = ops.ReadDipFrame(h)` and `r = r2 ?? r`.
       If it returns false, `r` is kept.

     Set `outer = outer2`.
  3. **Re-push.** If `FrameSettle.NeedsRepush(offScreen,
     ParkGeometry.VisibleFraction(r, dipFrames))`:
     - call `ops.SetWindowPos(h, outer, positionOnly: true)`;
     - if that returns true, `r3 = ops.ReadDipFrame(h)` and `r = r3 ?? r`.
  4. The result is `r`.

  For one entry, B10 and B11 together give at most 3 `SetWindowPos` calls
  (or 1 batch slot + 2) and at most 3 reads. No other retry exists. The
  adapter never compares the result with any learned limit, and never
  treats it as a refusal.
- **B12 (physical clamp).** `ClampOrigin` moves the rect so that `Left` and
  `Top` lie in [−30000, 30000]. Width and height are kept. Within range it is
  the identity. With anchor-preserving DIPs and park spots next to their
  display, real targets stay in range. The clamp is a safety net (A12).
- **B13 (exceptions).** An exception inside the B6–B11 handling of one entry
  makes that entry's result null and does not affect other entries. An
  exception outside per-entry handling (for example in B5 or B10's batch
  call) makes every not-yet-finished entry null. `Run` never throws.
- **B14 (frame convention).** Targets and results are the **visible** frame,
  in DIPs: `DWMWA_EXTENDED_FRAME_BOUNDS` mapped through `MonitorFromWindow`
  with that monitor's effective DPI, exactly as #10's facts `Frame`
  (`WindowGeometry.ReadDipFrame`, C4). When the extended frame bounds cannot
  be read, both the facts and the read-back fall back to `GetWindowRect`.
- **B15 (no state).** The runner keeps no cached frames, no per-window
  state and no counters between batches. Echo handling stays in the Engine
  (#26 B26–B27). The tracker's change-only filter is unaffected.

### C. Read and focus

- **B16 (ReadFrame).** On the writer: identity null, or `!IsLive` → null.
  Otherwise the result is `ops.ReadDipFrame(h)`. Reads do not check
  `IsHung`: they send no window message, so a hung window can still be read.
- **B17 (Focus).** On the engine thread:
  - null key → `ArgumentNullException`;
  - disposed → nothing;
  - `Resolve(key)` is null → nothing (no Raising);
  - otherwise post `Raising?.Invoke(key)` (exceptions from handlers are
    caught and logged), then enqueue a focus item.

  On the writer the focus item does:
  1. `!IsLive` → not attempted (`LastFocusSucceeded = null`);
  2. `IsHung` → not attempted, and log `"focus skipped (hung): <key>"`;
  3. `IsMinimized` → `ops.Restore(h)` (`SW_RESTORE`);
  4. `ok = ops.SetForeground(h)`, and `LastFocusSucceeded = ok`. If
     `!ok`, log `"focus refused: <key>"` (posted to the engine thread).

  No result reaches the Engine; focus changes come back through the
  tracker's foreground path. There is no `SendInput`, no synthetic Alt key,
  no `AttachThreadInput`, no `AllowSetForegroundWindow`, no
  `BringWindowToTop` and no `SetWindowPos(HWND_TOP)` (A10).

### D. Queries (engine thread, synchronous, message-free)

- **B18 (z-order and visibility).**
  - `FrontToBack()`: for each `h` in `ops.TopLevelWindows()` (EnumWindows
    order, topmost first), `k = windows.KeyOf(ToHandle(h))`; non-null keys
    are appended in that order. The result is a new list each call.
  - `IsShowing(key)`:
    - null key → `ArgumentNullException`;
    - unresolved → false;
    - otherwise `IsLive(h, pid, tid) && IsVisible(h) && !IsMinimized(h) &&
      !IsCloaked(h)`.

    It is computed per call with **no cache** (A9).
- **B19 (displays).** `ops.Monitors()` (production):
  1. `EnumDisplayMonitors(0, null, cb, 0)`, in enumeration order.
  2. For each monitor: `GetMonitorInfoW` (`rcMonitor`; a failed read skips
     the monitor) and `GetDpiForMonitor(MDT_EFFECTIVE_DPI)`. A failure or
     0 gives 96 (A16).
  3. `DipFrame = DipSpace.ToDip(rcMonitor, rcMonitor, dpi)`.

  The list is taken fresh on every call (no cache). Then:
  - `VisibleFraction(rect) = ParkGeometry.VisibleFraction(rect, dipFrames)`;
  - `ParkingOrigin = ParkGeometry.DefaultFallbackOrigin(dipFrames)`, which
    is (10000, 10000) with no monitors.
- **B20 (input and tabs).**
  - `Pointer`:
    1. `ops.CursorPos()` (physical, under `DpiScope`).
    2. `hm = ops.MonitorFromPoint(x, y)`; find `hm` in `Monitors()` by
       handle.
    3. The result is `DipSpace.ToDip(x, y, M.Bounds, M.Dpi)`.

    If the monitor is not found, the result is `(x, y)` unscaled. If the
    cursor read fails, the result is the last successful value, or (0, 0)
    before any (A16).
  - `MouseDown = ops.AsyncKeyDown(FrameMath.PrimaryButtonKey(
    ops.ButtonsSwapped()))`. `GetAsyncKeyState` reports *physical* buttons,
    so the primary button is the right button when buttons are swapped.
  - `TabSibling(key)`: `ArgumentNullException` for null; otherwise always
    null (engine-interfaces B10).

### E. Interop (production `NativeWindowOps`)

- **B21.** Same style as #10 B1 (`LibraryImport`, UTF-16, `SetLastError`
  where failure is checked, `UnmanagedCallersOnly` callbacks via function
  pointers, no COM, no `DllImport`). New user32 entry points:
  - `SetWindowPos`, `BeginDeferWindowPos`, `DeferWindowPos`,
    `EndDeferWindowPos`;
  - `ShowWindow`, `SetForegroundWindow`;
  - `EnumDisplayMonitors`, `MonitorFromPoint`;
  - `GetCursorPos`, `GetAsyncKeyState`, `GetSystemMetrics`.

  New constants:

  | Name | Value | Name | Value |
  |---|---|---|---|
  | SWP_NOSIZE | 0x0001 | SWP_NOZORDER | 0x0004 |
  | SWP_NOACTIVATE | 0x0010 | SWP_NOOWNERZORDER | 0x0200 |
  | SW_SHOWNOACTIVATE | 4 | SW_RESTORE | 9 |
  | SM_SWAPBUTTON | 23 | VK_LBUTTON / VK_RBUTTON | 0x01 / 0x02 |

  `SWP_NOSENDCHANGING`, `SWP_ASYNCWINDOWPOS`, `SendInput`, `keybd_event`,
  `AttachThreadInput`, `AllowSetForegroundWindow` and `SendMessage*` are not
  declared. `IsLive` is `IsWindow(h)` and `GetWindowThreadProcessId(h, out
  pid)` equal to the recorded `(threadId, processId)`. `IsCloaked` uses
  `DwmGetWindowAttribute(DWMWA_CLOAKED)`. `TopLevelWindows` uses the same
  `EnumWindows` collection pattern as #10 B2.

## Edge cases & errors

- **E1.** These throw `ArgumentNullException` synchronously, with nothing
  queued or posted:
  - a null `post` or `windows` in the constructor;
  - a null key in `ReadFrame`, `SetFrame`, `Focus`, `IsShowing` or
    `TabSibling`;
  - a null `completion`;
  - a null `writes`.

  `SetFrames` with a null entry key or duplicate keys throws
  `ArgumentException` (engine-interfaces B9).
- **E2.** A window destroyed, or with its handle reused, between issue and
  run → null (B6.3). A window that is still alive but stopped being managed
  between issue and run is still written (A4).
- **E3.** An elevated window (UIPI): `DeferWindowPos` or `SetWindowPos`
  fails, so the batch falls back and that entry gets null (B10). A silent
  no-op is possible in theory. Then the unchanged frame is returned and the
  Engine reads it as a refusal (A3).
- **E4.** A hung window (`IsHungAppWindow`, about 5 s unresponsive) → null,
  with no write and no blocking. A window that is not yet flagged as hung
  but does not answer blocks the writer until it answers (A2).
- **E5.** No monitors (for example during a display reconfiguration):
  - writes → null;
  - `VisibleFraction` → 0;
  - `ParkingOrigin` → (10000, 10000);
  - `Pointer` → the unscaled cursor position.
- **E6.** An empty `SetFrames` list posts an empty list. It still goes
  through the writer queue, which keeps the ordering.
- **E7.** A target with zero or negative size is passed through (B8 of
  interfaces). `ToPixels` then gives Right ≤ Left. The OS decides.
- **E8.** A target spanning two monitors maps through the monitor it
  overlaps most. A per-monitor-aware app may then receive `WM_DPICHANGED`
  and resize itself. The settle pass (B11.2) rewrites it once.
- **E9.** A minimised managed window (rare: the classifier removes these
  temporarily) → null. It is not un-minimised.

## Acceptance criteria

Legend:
- [mac]: an xUnit test in `tests/CanvasWm.Core.Tests/Frames/` or a
  command/inspection on the Mac.
- [windows]: an automated xUnit test in
  `tests/CanvasWm.Win32.Tests/FrameWriter/`, run with `dotnet test
  tests/CanvasWm.Win32.Tests` in an interactive Windows session.
- [windows-manual]: an item in `windows-verification/win32-frame-writer.md`.

Tolerances:
- Rect and point comparisons use 1e-9 unless stated.
- Fractions use 1e-12.
- Pixel rects compare exactly.

Fake rig for [windows] AC14–AC25:
- `FakeOps : INativeWindowOps` holds scripted per-handle state and a FIFO of
  scripted `ReadDipFrame` results per handle. It records every call in order.
- `FakeMap : IManagedWindowMap` is backed by a dictionary.
- `ManualPost` appends actions to a list and runs them only on request.
- The writer is stepped with the internal `runOnWriter` constructor
  parameter: a recording runner that is run on request.
- Monitors:
  - **A** = handle 1, bounds (0,0,1920,1080), 96 dpi, DIP frame
    (0,0,1920,1080);
  - **B** = handle 2, bounds (1920,0,3840,1440), 144 dpi, DIP frame
    (1920,0,1280,960).
- Default window: insets (7,0,7,7), measured as WindowRect
  (93,100,907,707) and VisibleRect (100,100,900,700). It is live, not hung,
  not minimised and not maximised, and is on A.

### Core (Mac)

- **AC1** [mac] `FrameSettle` constants are `1.0`, `0.05` and `0.02`.
  `NeedsSecondPass`:
  - target (0,0,400,600), actual (0,0,500,600) → true;
  - (300,25,600,400) vs (300.5,25,600,400) → false;
  - a difference of exactly 1.0 on X → false;
  - 1.0 + 1e-9 on Height → true;
  - equal rects → false;
  - actual with NaN Width → true;
  - −1.5 on Y → true.

  (B11, amended B7)
- **AC2** [mac] `IsOffScreenWrite`: 0.000003 → true, 0.0499 → true, 0.05 →
  false, 1 → false, NaN → false. `NeedsRepush`: (true, 0.059) → true, (true,
  0.02) → false, (true, 0.0201) → true, (false, 0.9) → false, (true, NaN) →
  false. Worked check:
  - screen (0,0,1440,900), target (1439,899,705,423): `VisibleFraction` is
    1/298215, so it is an off-screen write;
  - result (1000,860,705,423): fraction 17600/298215 ≈ 0.059, so a re-push
    happens;
  - result (1439,860,705,423): fraction 40/298215, so no re-push.

  (B8.7, B11.3)
- **AC3** [mac] `DisplayChoice.ForRect` with frames `[A=(0,0,1920,1080),
  B=(1920,0,1280,960)]`:
  - (1930,10,600,400), cur 0 → 1;
  - (1800,0,400,400) → 1 (overlap 112000 vs 48000);
  - (1820,0,200,100) with frames `[(0,0,1920,1080),(1920,0,1920,1080)]` → 0
    (a tie, lowest index);
  - (1919,1079,705,423), cur 1 → 0 (a 1×1 overlap wins over current);
  - (5000,5000,10,10): cur 1 → 1; cur −1 → 0; cur 7 → 0;
  - a rect touching A's right edge exactly, (1920,1000,10,10) with only A
    listed, cur −1 → 0 (no overlap, fallback);
  - any rect with an empty list → −1;
  - a NaN rect, cur 1 → 1;
  - a null list → `ArgumentNullException`.

  (B9)
- **AC4** [mac] `FrameInsets`:
  - `Measure(93,100,907,707, 100,100,900,700) == (7,0,7,7)`;
  - `Measure(110,0,900,700, 100,0,910,690)` → `(0,0,0,10)` (negatives
    clamp to 0);
  - `Zero == (0,0,0,0)`;
  - `(7,0,7,7).Rescale(96,144) == (11,0,11,11)` (10.5 → 11);
  - `(11,0,11,11).Rescale(144,96) == (7,0,7,7)`;
  - `(7,0,7,7).Rescale(96,120) == (9,0,9,9)`;
  - `Rescale(96,96)` returns an equal value;
  - `Rescale(0,96)` and `Rescale(96,0)` → `ArgumentOutOfRangeException`.

  (B8.4–5)
- **AC5** [mac] `PhysicalCoordinates`: `Limit == 30000`; `Clamp(−32000) ==
  −30000`; `Clamp(30001) == 30000`; `Clamp(1919) == 1919`;
  `Clamp(int.MinValue) == −30000`; `Clamp(int.MaxValue) == 30000`. (B12)
- **AC6** [mac] Build and inspection:
  - `dotnet build CanvasWm.slnx` gives 0 warnings and 0 errors;
  - `dotnet test tests/CanvasWm.Core.Tests` and `dotnet test
    tests/CanvasWm.Engine.Tests` pass;
  - no file under `src/CanvasWm.App` changed in this feature; under
    `src/CanvasWm.Engine` only the approved R2 change (`FrameWriting.cs`,
    `DisplayEngine.Writer.cs`), and under `tests/CanvasWm.Engine.Tests` only
    the new R2 test file (amended after the human approved R2);
  - Core files changed only under `src/CanvasWm.Core/Frames/`.

  (Placement)
- **AC7** [mac] Each of these prints nothing, run from the repo root:
  - `rg -n "TrellisWM|Licensing" src tests`
  - `rg -n "SWP_NOSENDCHANGING|SWP_ASYNCWINDOWPOS|SendInput|keybd_event|AttachThreadInput|AllowSetForegroundWindow|SendMessage|DllImport" src/CanvasWm.Win32`
  - `rg -nP "(?<![\w.])(0\.05|0\.02|0\.1|120)(?![\w.])" src/CanvasWm.Win32 -g "*.cs"`
    (thresholds come only from Core)
  - `rg -n "System\.Threading\.Timer|PeriodicTimer|Task\.Delay|Thread\.Sleep|Stopwatch|DispatcherTimer|SetTimer" src/CanvasWm.Win32`
  - `rg -n "\.Value\b.*(Substring|TrimStart|Parse)|ulong\.Parse" src/CanvasWm.Win32/Windows/Win32WindowSystem.cs`
    (window keys are not parsed)

  (B2, B21, H1 of findings, CLAUDE.md)

### Windows: pure helpers (automated)

- **AC8** [windows] `DipSpace.ToPixels`:
  - W2: `((1930,10,600,400), B, 144) == (1935,15,2835,615)`;
  - `((0,0,960,1040), A, 96) == (0,0,960,1040)`;
  - L = (−3840,0,0,2160) at 192: `((−3740,100,500,500), L, 192) ==
    (−3640,200,−2640,1200)`;
  - half rounding at 120 dpi (s = 1.25), monitor (0,0,…): X 0.4 → Left 1
    (0.5 rounds away from zero), X −0.4 → Left −1;
  - `dpi 0`, NaN X and +∞ Width → `ArgumentOutOfRangeException`;
  - X = 1e12 at 96 → Left `int.MaxValue` (saturated).

  (C1)
- **AC9** [windows] Round trips:
  - for every pixel rect p in {(0,0,960,1040), (1935,15,2835,615),
    (−3640,200,−2640,1200), (1,1,2,2)} and its monitor (A, B, L, A at 120
    dpi), `ToPixels(ToDip(p, M, dpi), M, dpi) == p` exactly;
  - for fractional DIP rects r ∈ {(10.3,20.7,705.5,422.25), (1930.1,
    0.9,600.6,400.4)} on A at 120 and B at 144: `ToDip(ToPixels(r))` has
    |ΔX|, |ΔY| ≤ 0.5/s and |ΔW|, |ΔH| ≤ 1/s;
  - two adjacent DIP rects (0,0,705.3,100) and (705.3,0,700,100) at 120
    give `Right` of the first `== Left` of the second.

  (C1, A6)
- **AC10** [windows] `DipSpace.ToDip(point)`: W9, `(2235, 315)` on B →
  (2130, 210); `(100, 100)` on A → (100, 100); dpi 0 →
  `ArgumentOutOfRangeException`. (C1, B20)
- **AC11** [windows] `FrameMath`:
  - `Expand((0,0,960,1040), (7,0,7,7)) == (−7,0,967,1047)` (outer width
    974, height 1047, as in W1);
  - `ClampOrigin((−40000,−50,−39000,950)) == (−30000,−50,−29000,950)`;
  - `ClampOrigin((31000,40000,31100,40100)) == (30000,30000,30100,30100)`;
  - in-range rects are unchanged;
  - `PrimaryButtonKey(false) == 0x01`, `PrimaryButtonKey(true) == 0x02`.

  (B12, B20)
- **AC12** [windows] Reflection over `CanvasWm.Win32`:
  - the public types are exactly `Win32WindowSource`, `HandleConversion`,
    `DipSpace`, `PixelRect`, `Win32WindowSystem`, `IManagedWindowMap` and
    `ManagedWindowIdentity`;
  - `Win32WindowSystem` implements `IWindowSystem` and `IDisposable`;
  - no type has a member typed from `CanvasWm.Engine.Tracking`.

  (Public API; see R3)
- **AC13** [windows] Refactor safety (C4): for the #10 in-process test
  window, `Win32WindowSource.Describe(h).Frame` equals
  `WindowGeometry.ReadDipFrame(h)`, and every existing #10 test still
  passes.

### Windows: algorithm with fakes (automated)

- **AC14** [windows] W1, a single write. A window on A with the default
  insets, target (0,0,960,1040), and a scripted read (0,0,960,1040):
  - exactly one `SetWindowPos(h, (−7,0,967,1047), positionOnly false)`;
  - no `TryDeferBatch`;
  - the result is (0,0,960,1040);
  - call order: `IsLive`, `IsHung`, `IsMinimized`, `IsMaximized`, then the
    measurements, then the write, then one read.

  (B6, B8, B10, B11)
- **AC15** [windows] Settle:
  - target (0,0,400,600), reads `[(0,0,500,600), (0,0,500,600)]` → two
    `SetWindowPos` calls and result (0,0,500,600). There is no third call
    even though it still differs by more than 1.
  - reads `[(0,0,500,600), null]` → result (0,0,500,600).
  - reads `[(0.6,0,400,600)]` → one write and result (0.6,0,400,600).
  - the second write fails (`SetWindowPos` false) → result is the first
    read, and no third read.

  (B11.2)
- **AC16** [windows] Off-screen re-push, on monitor A only:
  - target (1919,1079,705,423), reads `[(1000,860,705,423),
    (1000,860,705,423), (1919,1079,705,423)]` → calls: write, read, settle
    write, read, position-only write, read. The result is
    (1919,1079,705,423).
  - target (100,100,705,423) (on screen) with the same first two reads → no
    position-only write.
  - an off-screen target whose reads give fraction ≤ 0.02 → no re-push.
  - a failed re-read → the previous reading.

  (B8.7, B11.3)
- **AC17** [windows] Batch: three live windows, results scripted:
  - exactly one `TryDeferBatch` with 3 entries in input order and no
    `SetWindowPos`;
  - the results come in input order.

  W4: `TryDeferBatch` returns false and entry 2's `SetWindowPos` returns
  false → `SetWindowPos` for entries 1, 2, 3 in order; results `[r1, null,
  r3]`; entry 2 is never read. (B10)
- **AC18** [windows] Pre-filter (W5), with five entries:
  1. hung;
  2. dead (`IsLive` false);
  3. minimised;
  4. NaN target;
  5. live.

  The results are `[null, null, null, null, r5]`. The write is a single
  `SetWindowPos` for entry 5 with no batch. No measurement or write call is
  made for handles 1–4. An entry with a null identity → null with no ops
  call. An empty monitor list → all null and no write call. (B5, B6)
- **AC19** [windows] W7, maximised: `RestoreNoActivate(h)` is recorded
  before the first `WindowRect`/`VisibleRect` of that window. In a batch of
  two with one maximised window, the restore comes before `TryDeferBatch`.
  `Restore` (`SW_RESTORE`) is never called by a write. (B7)
- **AC20** [windows] W3, crossing DPI. A window on A with insets (7,0,7,7)
  and target (1930,10,600,400):
  - `vis = (1935,15,2835,615)`; the insets are rescaled to (11,0,11,11);
  - the first write is `SetWindowPos((1924,15,2846,626))` (outer width 922,
    height 611);
  - with reads `[(1930,10,400,266.667), (1930,10,600,400)]` and the window
    now reported on B with VisibleRect/WindowRect giving insets (11,0,11,11),
    the settle write is again `(1924,15,2846,626)`, and the result is
    (1930,10,600,400).

  (B8, B11.2, E8)
- **AC21** [windows] Exceptions: `FakeOps.ReadDipFrame` throws for entry 1
  of 3 → results `[null, r2, r3]`. `TryDeferBatch` throws → all three are
  null. `Run` does not throw. (B13)
- **AC22** [windows] Threading contract (`ManualPost`, stepped writer):
  - after `SetFrame`, `ReadFrame` and `SetFrames([])`, no completion has
    run and nothing is posted until the writer is stepped;
  - stepping runs the items in issue order; after all posted actions run,
    the completions ran exactly once each, in order (write, read, empty
    list);
  - the read saw the write's outcome (scripted fake state);
  - an unknown key gives null after stepping (never inline);
  - `SetFrames` with duplicate keys or a default `FrameWrite` (null key) →
    `ArgumentException`, with the writer and post lists unchanged;
  - the E1 null cases → `ArgumentNullException`.

  (B1, B3; engine-interfaces B5, B6, B9)
- **AC23** [windows] Disposal and completion errors:
  - queue two writes, `Dispose()` before stepping → both completions are
    posted with null (all-null for the batch);
  - after `Dispose`, a `SetFrame` posts null directly with no ops call;
    `Focus` makes no ops call; `Dispose` twice is fine;
  - a completion that throws → `log` gets one line containing the exception
    type name, and the next posted completion still runs.

  (B3, B4)
- **AC24** [windows] Queries with fakes:
  - `FrontToBack()`: with top-level order `[h9, h2, h5]` where only h2 and
    h5 map to keys → `[k2, k5]`.
  - `IsShowing`: true only when live, visible, not minimised and not
    cloaked; false for each single negation, for an unknown key and for an
    identity whose thread id mismatches.
  - `VisibleFraction((1340,0,200,100))` with monitors A and B → 1.
  - `ParkingOrigin` with A and B → (3199, 959).
  - `ParkingOrigin` with no monitors → (10000, 10000).
  - `Pointer` with cursor (2235,315) on B → (2130,210).
  - `Pointer` when the cursor read fails after that → (2130,210).
  - `MouseDown` with swapped buttons and only VK_RBUTTON down → true; not
    swapped with only VK_RBUTTON down → false.
  - `TabSibling(k)` is null.

  (B18–B20)
- **AC25** [windows] Focus with fakes:
  - unknown key → no Raising and no ops call;
  - known key → exactly one Raising posted with that key, then on the
    writer `SetForeground(h)`, and `LastFocusSucceeded == true`;
  - minimised → `Restore` before `SetForeground`;
  - hung → no `SetForeground` and one log line;
  - `SetForeground` false → `LastFocusSucceeded == false` and one "focus
    refused" log line;
  - a focus item runs after a previously queued write (FIFO).

  (B17)

### Windows: real windows (automated)

These use `MessageLoopThread` (engine thread and `post`), the in-process
`CanvasWmSelf` window of #10 AC17 and `ProbeProcess`. `FakeMap` maps test
keys to the real identities (pid and tid from `GetWindowThreadProcessId`).

- **AC26** [windows] The in-process window on the primary monitor:
  - `SetFrame(k, (100,100,800,600))` → the completion runs on the loop
    thread, and the result is within 1.0 DIP of the target on every
    component;
  - a following `ReadFrame(k)` equals `Describe(h).Frame` (1e-9);
  - `GetWindowRect` minus the extended frame bounds equals the insets
    applied (the visible frame, not the outer rect, matches the target).

  (B8, B11, B14)
- **AC27** [windows] Maximised: `ShowWindow(SW_MAXIMIZE)` on the test
  window, then `SetFrame(k, (100,100,800,600))` → afterwards `IsZoomed` is
  false and the result is within 1.0 of the target. `GetForegroundWindow()`
  is the same before and after the write (no activation). (B7, B10 flags)
- **AC28** [windows] Probe (cross-process):
  - `SetFrames` with the probe and the in-process window → both results are
    within 1.0 of their targets;
  - `FrontToBack()` contains both keys in the same relative order as
    `EnumWindows`;
  - `IsShowing` is true, and false after `SW_HIDE` on the probe.

  (B10, B18)
- **AC29** [windows] Hung probe: "hang" for 15 s, then wait 6 s. Then:
  - `SetFrame(probe, …)` completes with null within 1000 ms of the call;
  - a `SetFrame` to the in-process window issued right after completes
    normally within 1000 ms;
  - `ReadFrame(probe)` returns a non-null frame (reads do not check for
    hangs).

  (B6.4, B16, E4)
- **AC30** [windows] Dead and reused handles: destroy the test window, then
  `SetFrame` and `ReadFrame` → null. An identity with the right handle but a
  wrong thread id → null. (B6.3, E2)
- **AC31** [windows] Pointer and buttons: `SetCursorPos` to the physical
  centre of the primary monitor → `Pointer` equals
  `DipSpace.ToDip(center, rcMonitor, dpi)`. With no button held,
  `MouseDown` is false. `VisibleFraction` of the primary monitor's DIP frame
  is 1. `ParkingOrigin` equals `DefaultFallbackOrigin` of the test's own
  monitor list. (B19, B20)

### Windows: manual checklist

The manual steps use `CANVASWM_MANUAL=1 dotnet test tests/CanvasWm.Win32.Tests
--filter "FullyQualifiedName~FrameWriterManual.<Name>"`. These probe tests
(AC38) take a window by title or by click-to-pick (`GetForegroundWindow`
after a 5 s countdown), call the `Win32WindowSystem`, and print the targets
and results to `%TEMP%\canvaswm-writes.tsv`.

- **AC32** [windows-manual] Mixed-DPI move (`MoveAcross`). Monitor 1 at
  100 %, monitor 2 at 150 %. Pick a Notepad window on monitor 1. The probe
  writes it to the centre half of monitor 2's DIP frame and back. Expected:
  - each result is within 1.0 DIP of its target;
  - the visible border sits where requested (Snipping Tool, ±2 px);
  - the TSV shows whether the settle pass ran (record it; either outcome
    passes).

  Repeat with Edge/Chrome (per-monitor-aware). (B8, B11.2, E8)
- **AC33** [windows-manual] Maximised restore. Maximise Notepad, unfocus it
  (click another window), and run `TileLeftHalf`. Expected:
  - Notepad is restored (not maximised), fills the left half with its
    visible border at the work-area edge, and has no visible gap at the
    screen edge;
  - the other window stays in the foreground;
  - double-clicking Notepad's title bar afterwards maximises it again.

  (B7, B8)
- **AC34** [windows-manual] Elevated window. From a non-elevated terminal,
  start `regedit` (accept UAC) and run `TileLeftHalf` on it. Expected:
  - the result is `null` in the TSV;
  - regedit does not move;
  - no exception occurs.

  Repeat elevated: the result is non-null. (B10, E3)
- **AC35** [windows-manual] Hung app does not block. Start
  `WriteLoop` (it writes two Notepads alternately every second for 30 s)
  plus a probe told to "hang". Expected: the Notepad writes continue on
  schedule for the whole run; probe writes report `null` once Windows
  flags it as hung (after about 5 s). (B6.4, E4)
- **AC36** [windows-manual] Park stays off-screen (`ParkAndWait`). Park a
  Notepad at the bottom-right park spot of the primary monitor
  (`ParkGeometry.ParkFrame` for its size). Expected:
  - the result is within 1.0 of the target;
  - after 60 s, `ReadFrame` still gives the same frame;
  - only a ≤ 2 px sliver is visible at the corner;
  - `ToPixels` of the target is within ±30000.

  Repeat at 150 % scaling. Record whether the re-push ran. (B9, B11.3,
  B12)
- **AC37** [windows-manual] Foreground after hotkey (`FocusHotkey`). The
  probe registers a test-only hotkey (Ctrl+Shift+F12, via `RegisterHotKey`
  on the loop thread) and waits 20 s. Click a terminal so that another
  process is in the foreground, then press the hotkey. On `WM_HOTKEY` the
  probe calls `Focus(notepadKey)`. Expected: Notepad becomes the foreground
  window, the TSV records `LastFocusSucceeded = true`, and the taskbar does
  not flash. Repeat with Notepad minimised: it restores and gets focus.
  Record the result. On failure, record it for the human decision on a
  workaround (A10). (B17)
- **AC38** [windows-manual] Probe tool: with `CANVASWM_MANUAL` unset, every
  `FrameWriterManual` test passes in < 1 s and does nothing. With it set,
  each writes the TSV with a header row.
- **AC39** [windows-manual] Fractional scaling churn. Run only after R2 is
  approved and implemented, otherwise record "R2 pending". At 125 %, run
  `RepeatTile` (it issues the same three tile targets three times, with
  read-backs between). Expected: every result is within 1.0 DIP of its
  target, and the TSV shows that results differ from the targets by at most
  0.5/1.25 = 0.4 DIP on X/Y and 0.8 on W/H for apps that do not clamp.
  (A6, R2)

Count:
- **7 [mac]**: AC1–AC7 (5 xUnit, 2 command/inspection);
- **24 [windows]**: AC8–AC31, all automated;
- **8 [windows-manual]**: AC32–AC39.

## Assumptions

- **A1. One writer thread for all windows** (not per-window queues).
  *Needs human review.* It satisfies B6 trivially and keeps completions in
  issue order. Cost: windows are written one batch at a time, so a slow app
  delays writes to others. The studied adapter runs windows in parallel.
- **A2. A window that stops responding during a write blocks the writer.**
  *Needs human review (highest risk).* `SetWindowPos` and
  `EndDeferWindowPos` wait for the target thread with no timeout. Hung
  windows that are already flagged are skipped (B6.4), but one that hangs
  just before or during the write blocks every later read, write and focus
  until it answers. The Engine's landing then waits too (#26 B23). A later
  mitigation could add a watchdog with a per-window fallback thread, or
  `SWP_ASYNCWINDOWPOS` for suspect windows (with a null result).
- **A3. No integrity re-check in the writer.** Inaccessible windows are
  never admitted (classifier `IgnoreReason.Inaccessible`), integrity does not
  change during a process's life, and B6.3 pins the process via (pid, tid).
  A silent UIPI no-op would return the unchanged frame, which the Engine
  could learn as a refusal (findings M2). This is believed not to happen
  for windows that pass admission.
- **A4. Keys are resolved at issue time on the engine thread.** The map is
  engine-thread state, and the writer never calls it. A window that is
  untracked but still alive between issue and run is still written. That
  window is just leaving management, so this is harmless. Its completion
  is ignored by the Engine (#26 B13 same-record test).
- **A5. Monitor choice: the largest overlap, else the current monitor**
  (human decision). *Needs human review with #14.* For a target far from
  every monitor (for example the no-display fallback origin, or a Windows
  park overlap of 0), the anchor-preserving mapping of the current monitor
  can place the window far from where the DIP rect suggests. The default
  overlap of 1 DIP makes park targets intersect their display, so this path
  is rare. The ±30000 clamp bounds the damage. #14 must keep the overlap
  > 0 or choose "nearest monitor" instead. The Windows park overlap (findings
  L3) is deferred to #14 for this reason.
- **A6. `ToPixels` rounds edges half away from zero.** This matches Core's
  `FitWithin` rounding. Shared DIP edges then map to shared pixel edges, so
  there are no 1 px gaps or overlaps between tiles. The read-back of a
  fractional target differs by ≤ 0.5/s per edge (decision 2, R2).
- **A7. Insets.** They are measured per write in the window's current state
  as `GetWindowRect` minus the extended frame bounds, with negative sides
  set to 0 and zeros when either read fails. They are rescaled by `toDpi /
  fromDpi` when the target monitor's DPI differs from the current one. On
  the settle pass they are measured again (the window is now on the target
  monitor).
- **A8. Maximised → `SW_SHOWNOACTIVATE` restore, minimised → null.**
  `SW_SHOWNOACTIVATE` is documented as "most recent size and position, not
  activated", which restores a zoomed window without focus. Verified by
  AC27/AC33. Un-minimising is a visibility change that the writer does not
  make (findings K3).
- **A9. `IsShowing` has no cache.** The contract allows up to 0.1 s of
  staleness, and 0 satisfies it. The four calls (`IsWindow`,
  `IsWindowVisible`, `IsIconic`, `DWMWA_CLOAKED`) send no messages and cost
  microseconds. A cache would need a time source in Win32, which #10 AC4
  forbids. If profiling shows a need, a cache keyed on an injected `IClock`
  belongs in the App.
- **A10. Focus = `SetForegroundWindow` only** (human decision), run on the
  writer thread. Running there keeps focus ordered after earlier writes and
  keeps a blocking `ShowWindow(SW_RESTORE)` off the engine thread.
  Foreground permission is per process (the last-input/hotkey rule), so the
  thread does not matter. AC37 verifies this. Hung windows are not focused.
  **Risk if a workaround is ever added:** a synthetic Alt press (`SendInput`)
  while the user still holds Win+Alt from the default shortcut profile (#7)
  would corrupt the modifier state. It can open the Start menu, toggle the
  target's menu-bar mnemonics, or break the next shortcut. Any workaround
  must wait for all shortcut modifiers to be released and must not run
  against elevated targets. `AttachThreadInput` stays excluded (stall and
  deadlock risk).
- **A11. `Raising` is included** as a posted .NET event on a managed key.
  It is cheap and mirrors the studied broadcast. Only overlays (#16) use it.
- **A12. The physical clamp applies to Left/Top only.** Right/Bottom may
  exceed 30000 for very large windows. Window messages pack the window
  *position*, so that is what must stay in range.
- **A13. Deferred studied/optional items.** The 120 ms slow-write log (it
  needs a stopwatch, and #10 AC4 forbids one in Win32), `SWP_NOCOPYBITS`,
  skipping unchanged size/position components (`SWP_NOMOVE`/`SWP_NOSIZE`),
  and `SWP_ASYNCWINDOWPOS`. The Engine's skip rules already avoid
  redundant writes.
- **A14. Disposal does not join the writer.** Joining could hang forever on
  a blocked write (A2). The thread is a background thread, so process exit
  is not held up.
- **A15. Completion exceptions are logged and swallowed** (engine-interfaces
  E3), so one faulty completion cannot stop later ones. Exceptions from
  `Raising` handlers are handled the same way.
- **A16. Fallbacks.** A failed DPI read for a display uses 96. A failed
  `GetCursorPos` (for example on the secure desktop) returns the last good
  pointer, or (0, 0). A cursor on no listed monitor is returned unscaled.
- **A17. Device-pixel integers in Core.** `FrameInsets` and
  `PhysicalCoordinates` hold device pixels, while CLAUDE.md says Core
  *geometry* is in DIPs. They are unit-agnostic integer arithmetic with no
  OS access and are not model geometry. Putting them in Core makes the
  rescale, rounding and clamp decisions Mac-testable, which was the point of
  the split. `ToPixels` stays in Win32 next to `ToDip`, so its tests are
  [windows].
- **A18. Off-screen constant separate from `ParkOptions`.**
  `FrameSettle.OffScreenWriteBelow` (0.05) equals
  `ParkOptions.Default.VisibleFractionThreshold` but is a different studied
  literal (the adapter's own classification), so it is not tied to park
  options.
- **A19. The studied accessibility-mode hold is omitted.** Win32 has no
  per-process "enhanced UI" flag that makes apps animate or resist
  programmatic moves.
- **A20. Monitors are taken per call/batch, with no cache** (studied:
  per-write snapshot). `EnumDisplayMonitors` and `GetMonitorInfoW` are
  cheap and message-free. Display changes are therefore always current.

## Required edits to earlier specs

These are for the orchestrator to apply. This spec does not change them.

- **R1 (human decision 1) — `specs/engine-interfaces.md` B7, replace the whole
  bullet with:**

  > - **B7 (actual frame).** A write's result is the frame **read back after**
  >   the write, in DIPs, using the same frame convention as `ReadFrame` (on
  >   Windows: extended frame bounds, #11). Inside one write the adapter may
  >   perform exactly these two fixed mechanical steps, each decided only by
  >   the pure predicates of `CanvasWm.Core.Frames.FrameSettle`, and no others:
  >   (a) **settle pass** — if the read-back differs from the target by more
  >   than `FrameSettle.Tolerance` (1.0) on any of X, Y, Width, Height, write
  >   size and position once more and read back again; the second reading is
  >   the result (the first if the second write or read fails);
  >   (b) **off-screen re-push** — if the target's visible fraction is below
  >   `FrameSettle.OffScreenWriteBelow` (0.05) and the result's visible
  >   fraction is above `FrameSettle.RepushAbove` (0.02), write the position
  >   once more and read back; that reading is the result (the previous one if
  >   the write or read fails).
  >   There is no further write. The adapter never judges the result: no
  >   refusal detection, no retries beyond (a) and (b), no learning; a clamped
  >   or ignored write simply returns the actual frame. Null means: key unknown
  >   at the time the operation runs, the window vanished, access was denied
  >   (e.g. elevated window), or the read-back failed.

  Consequences: the interfaces spec's Placement line "No Core type is
  changed or added" stays true for #9 itself. Its "Reported for review"
  note ("#11's spec should keep retries out of Win32") is superseded by
  (a)/(b). `FakeWindowSystem` is unchanged (the permission is optional).
- **R1b — `findings/engine-interfaces.md` rule 17:** the off-screen
  classification literal is **0.05**, not "about 0.1"
  (findings/win32-frame-writer.md rule 2).
- **R2 (human decision 2) — proposal for human review,
  `specs/engine-frame-writer.md`.** **Finding:** skip rule B8.3
  (`DisplayEngine.ShouldSkip`: `if (known == t.Rect) return true;`) compares
  the target with the **known frame**. The known frame is the last
  read-back (`Complete` → `SetKnown(k, a)`) or the last frame the tracker
  reported. It is **not** the last written target. The comparison is exact.
  B8.4 compares with `Requested` (the last target) only while a write is in
  flight. At fractional scaling (125 %, 150 %), and for fractional DIP
  targets at any scale, the read-back differs from the target by up to
  0.5/s per edge (A6). So the window never skips and is rewritten on every
  direct presentation and landing pass. That means no visible movement, but
  every pass issues redundant `SetFrames` work, and landings always wait for
  completions. **Minimal fix:**
  - add `FrameWriting.SkipTolerance = 1.0` (equal to
    `FrameSettle.Tolerance`, the precision the adapter guarantees);
  - change B8.3 to "**Known frame matches target**: `|known.c − T.Rect.c| ≤
    SkipTolerance` for every c ∈ {X, Y, Width, Height} → skip", using the
    existing `Near` helper;
  - add an AC: known (725,35,705,422.6) with target `Tile(725,35,705,423)`
    → no write; known (725,35,705,421.9) → write.

  B8.2, B8.4 and the drift check are unchanged. Re-run all Engine tests
  (none is known to depend on sub-1-DIP exact skips). Side effect: a user
  nudge of ≤ 1 DIP is not corrected, which is harmless.
- **R3 — `specs/win32-window-enum.md` (#10):**
  - (a) C1: `DipSpace` gains `ToPixels` and point `ToDip` (additive).
  - (b) AC4's first `rg` pattern must drop `WindowKey`, because
    `Win32WindowSystem` implements `IWindowSystem`, whose members use
    `WindowKey`. Proposed replacement:
    `rg -n "ClassifierPolicy|WindowClassifier|TrackerOptions|WindowTracker|IgnoreReason|TrackedFate|ExcludedApps|MinimumSize" src/CanvasWm.Win32`.
    AC4's numeric pattern stays valid (this feature adds none of those
    literals).
  - (c) AC15's public type list becomes the seven types of AC12 here.
  - (d) The B1 doc wording on `NativeMethods` user32 is reworded per C2.
    B1's "only these for fact gathering and events" still holds.
  - (e) C4 refactor of `FactGatherer` (no behaviour change; AC13).
  - Its "Reported for review" item "#11 must add the inverse and define the
    monitor choice" is resolved by C1 and B9.
- **R4 — for #29 (no spec yet).** `IManagedWindowMap` needs `(handle, pid,
  tid)` per managed key. `WindowTracker` keeps these privately (`Record`) and
  exposes only `WindowInfo` (no handle or thread id). #29 needs either a
  small Engine addition, for example `WindowTracker.IdentityOf(WindowKey) →
  (ulong Handle, int ProcessId, int ThreadId)?` and a handle → key lookup
  (`KeyFor` + `Find`), or an App-side map that it fills from the
  `IWindowTrackerTarget` callbacks. Win32 must not parse keys in either
  case. #29 should also build its display frames with this feature's
  `Monitors` logic (B19). A public helper may be wanted then.

## Traceability

| Spec item | Findings rule / earlier spec |
|---|---|
| B1–B4, AC22, AC23, A1, A14, A15 | G1, G3 (writer thread, post); rules 5, 12; engine-interfaces B5, B6, B9, E3 |
| B5, B8.7, AC2, AC16, A18, A20 | rules 2, 3 (snapshot, < 0.05); O3 |
| B6, AC18, AC29, AC30, A3, A4 | G4 (hung → null), H1 (IsWindow, identity), J3 (pre-filter), M1–M2, K3; engine-interfaces B7, B8 |
| B7, AC19, AC27, AC33, A8 | K1 (SW_SHOWNOACTIVATE), I6 |
| B8, B9, AC3, AC4, AC20, AC32, A5, A7 | I1–I5 (EFB convention, ToPixels, monitor choice, insets, DPI rescale); W1–W3 |
| B10, AC17, AC34 | J1, J2, J3, J8 (DeferWindowPos, flags, fallback, no NOSENDCHANGING); W4 |
| B11, AC1, AC15, AC16, R1 | rules 7–10 (settle > 1.0, re-push > 0.02); J4, J6, J7; human decision 1 |
| B12, AC5, AC11, AC36, A12 | L1, L2 (±30000), W6 |
| B13 | engine-interfaces B7 ("never throw for OS failures") |
| B14, AC13, AC26 | I1; #10 B10 (C4 shared read) |
| B15 | rule 13, J9 (echoes stay in Engine) |
| B16 | rules 14–16; engine-interfaces B6 (reads ordered, A3) |
| B17, AC25, AC37, A10, A11 | rules 17–19; N1–N5; human decision 3 |
| B18, AC24, AC28, A9 | rules 20, 21; O1, O2 |
| B19, AC24, AC31 | rules 22, 24; O3, O6; park-geometry B23/B25; engine-interfaces A7 |
| B20, AC10, AC24, AC31, A16 | rules 23, 25; O4, O5, O7; W8, W9 |
| B21, AC7 | #10 B1 style; G4 (no ASYNCWINDOWPOS), J1, N3 |
| C1, AC8–AC10, A6, AC39 | I2; Unknowns "rounding convention"; human decision 2 |
| R2, AC39 | Unknowns "rounding vs exact-equality skip"; engine-frame-writer B8.3; human decision 2 |
| A13 | rule 11 (120 ms log), J1 (NOCOPYBITS), J5, G4 |
| A17, Placement (Core) | §P recommended split |
| A19 | rule 6 |

## Amendments from #12 (win32-hotkeys, R2)

- AC12's exact public type list also gains `CanvasWm.Win32.Hotkeys.Win32HotkeySource` and `CanvasWm.Win32.Hotkeys.Win32KeyInput`.

## Amendments from #32 (engine-router)

- R4 resolved: `WindowTracker.IdentityOf` / `KeyOf` supply handle, pid and thread id per managed key; #33 builds `IManagedWindowMap` from them.

## Amendments from #33 (app-host)

- The exact public Win32 type list also accepts the six #33 types (see specs/app-host.md); the PureHelperTests type lists are updated in #33.
