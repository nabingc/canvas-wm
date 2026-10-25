# Spec: engine-frame-writer

## Goal

Feature #26 (carved out of #13). The engine's **at-rest write path**: it
turns the per-window targets of #25 into real frame writes and handles what
comes back.

1. **When a write pass runs.** A *direct presentation* writes all current
   targets immediately when the stage is at rest. A *landing pass* is used when
   a re-layout must "land" (refusal re-layout, store-minimum re-layout, later
   camera motion and gap drags). Landing passes are numbered by the engine
   generation (`PlacementGeneration`), so late results of an abandoned pass
   are ignored.
2. **Skip rules.** Windows that are already where they should be, or already
   being moved to the same place, are skipped.
3. **Echo list.** Every write remembers its target and its read-back for
   1.5 s. `IsExpected(key, rect)` (used by #27's `WindowMoved`) recognises the
   OS echoes of the engine's own moves.
4. **Completions.** Each write's completion is compared with its own target.
   This learns limits: own-minimum lowering, `ILimitStore.Observe`, and a
   0.25 s refusal recheck that learns a minimum (stored per app) or a maximum
   (per window) and re-lands.
5. **Settle and drift check.** A short time after a landing completes, a drift
   check re-parks any parked window that has crept back onto the screen and
   logs tiles that have moved.

This spec replaces #13 findings rules 13, 16, 17, 19 and 20 where they differ
(the findings file says so).

**Human decisions applied:**
- (D1) A refusal recheck that fires while the engine is suspended does
  nothing. This is a deliberate deviation from the studied app.
- (D2) The studied app's "probed minimum apps" set is omitted entirely
  (findings rule 14 is not implemented).
- (D3) The per-window pending-recheck counter is kept as diagnostic state only.
  It gates nothing.
- (D4) A full write pass goes through `IWindowSystem.SetFrames` as one batch
  with per-window results. Single-window rewrites use `SetFrame`. Skip rules,
  the echo list, refusal handling and completions stay per window. #11
  implements `SetFrames` with `DeferWindowPos`.

**Out of scope** (each with its owner; the hooks below are what they call):
- The landing state machine with camera and covers, `interruptLanding`, the
  `cameraRemaining`/`windowsNotYetShown` checks, and capture waits (#15/#16).
  Here, "landing done" means all completions of the pass are in and the
  generation is unchanged; then the settle delay follows.
- Overview shaping writes (#16). They call `WriteTargets(..., learns: false)`.
- Picture holds during park writes (findings rule 13, the `parkedSmall` set)
  belong to the cover feature #16. `ShownSize` is kept here.
- What `WindowMoved` does with an echo (#27), snap-back (#27), focus after a
  landing (tiling-loop rule 33, #27), recording laid-out sizes and settling
  floats at settle time (#27 / floating feature), and seeding own limits at
  admission (#27).
- The skip-store projection side path. #25 excluded it, so D2 removes its
  only reader.

## Placement

- Project: `src/CanvasWm.Engine` (`net10.0`). No new project or package
  references. Engine still references only Core.
- Namespace `CanvasWm.Engine`.
- New files:
  - `src/CanvasWm.Engine/DisplayEngine.Writer.cs` (partial class `DisplayEngine`:
    all writer members);
  - `src/CanvasWm.Engine/FrameWriting.cs` (internal constants class);
  - `src/CanvasWm.Engine/FrameWriterState.cs` (internal snapshot types).
- Changed file: `src/CanvasWm.Engine/DisplayEngine.cs`. Only the hooks listed
  under "Changes to existing types" are changed.
- Tests go in `tests/CanvasWm.Engine.Tests/Writer/*Tests.cs` and use the
  existing fakes unchanged (`FakeWindowSystem`, `ManualClock`,
  `InMemoryLimitStore`, `InMemoryDisplayStore`, `RecordingRenderer`,
  `FakeCapture`). `InternalsVisibleTo` already exists from #23.
- Depends on these specs, all **unchanged**:
  - `specs/engine-inbound-api.md`: `DisplayEngine` shell, `PlacementGeneration`,
    `ReframeCount`, `MotionActive`, `SetPhase`, `SetParked`, the call wrapper
    (B6–B8, B35), `ManagedWindow` (`Info.Frame` = known frame, `Parked`),
    `ParkContext()`.
  - `specs/engine-interfaces.md`: `IWindowSystem.SetFrame` / `SetFrames` /
    `ReadFrame` / `VisibleFraction` / `Focus`, `FrameWrite`, `FrameWriteResult`,
    `IClock.After`, `ILimitStore.Minimum` / `Learn(app, size)` /
    `Observe(app, size)`, `AppId`, `WindowKey`.
  - `specs/engine-projection-targets.md`: `ProjectionTargets.Compute`,
    `ProjectionRequest`, `ProjectionView.AtRest`, `ProjectionPolicy`,
    `WindowFacts`, `WindowTarget`, `TargetKind`.
  - `specs/tuning-constants.md`: `Tuning.FrameRefusalSlack` (24) and
    `Tuning.FrameRefusalRecheckSeconds` (0.25).
  - `specs/park-geometry.md`: `ParkGeometry.RestsParked`,
    `ParkOptions.Default.VisibleFractionThreshold` (0.05).
  - `specs/layout-solver.md`: `LayoutSolver.FocusLayout` (framed-rect lookup).
- Units are DIPs, y-down. Times are absolute seconds on `IClock.Now`.
- No text in `src/` or `tests/` may contain the words of the studied app's
  name or of its licence module (see CLAUDE.md).

### Changes to existing types (complete list)

| # | Where | Change | Hook it uses |
|---|---|---|---|
| C1 | `DisplayEngine` (public) | New property `Tuning Tuning { get; set; }`. Default `Tuning.Default`. Null throws `ArgumentNullException`. The setter is guarded by the B7 re-entry rule and has no side effect. | tuning-constants A10 ("#13 gives DisplayEngine a Tuning") |
| C2 | `DisplayEngine` re-frame (inbound B34) | The body becomes `ReframeCount++`, then `PresentLayout()` (B5) when started and not suspended, else `Render()`. | inbound B34 ("#13/#15 add target computation and frame writes") |
| C3 | `DisplayEngine.Start` (inbound B10) | After loading the saved record, the engine's working document and framed id are set from it (B3). There is no other change, and `Start` still makes no write. | inbound B10 "[#13: …]" |
| C4 | Registration, removal, `ReleaseAll` (inbound B17b, B19d, B13) | A new record gets a fresh writer state. A refresh keeps it. Removal and `ReleaseAll` drop it (B30). | inbound B17/B19/B13 "[#13]" slots |
| C5 | Parked flag (inbound B27) | The writer also sets `Parked` before each write (B12). **This extends inbound B27**, which lists only `SetParked`, `Release` and removal (reported). | `SetParked` (#14 hook) |
| C6 | Call wrapper (private `Run`) | (a) Writer completions and clock actions run their bodies through the same wrapper as public members (B33). (b) An internal "OnChange requested" flag makes the enclosing call fire `OnChange` once even when the B35 trigger set is unchanged (B4). | inbound B6, B35 |
| C7 | `PlacementGeneration` XML doc | The doc now says it is also incremented by re-land (B22). | inbound internal extension point |

No Core type, no `Services/*`, no `Rendering/*` and no fake is changed.
`ManagedWindow` is unchanged.

## Public API

### Public (one addition)

```csharp
namespace CanvasWm.Engine;

public sealed partial class DisplayEngine
{
    /// <summary>Internal tuning constants this engine reads (refusal slack, recheck delay, ...).
    /// Default Tuning.Default. Setting it has no side effect; the new value is read at next use.</summary>
    /// <exception cref="ArgumentNullException">On set with null.</exception>
    /// <exception cref="InvalidOperationException">Set from inside one of this engine's callbacks.</exception>
    public Tuning Tuning { get; set; }
}
```

### Internal (visible to `CanvasWm.Engine.Tests`; later features call these)

```csharp
namespace CanvasWm.Engine;

/// <summary>Writer constants that are not tuning values (algorithm literals, findings constants list).</summary>
internal static class FrameWriting
{
    /// <summary>Age after which an expected-frame entry is pruned (strict &gt;), seconds.</summary>
    public const double EchoWindowSeconds = 1.5;
    /// <summary>Per-component match tolerance of an echo (≤), DIPs.</summary>
    public const double EchoTolerance = 2.0;
    /// <summary>A park write counts as accepted when actual ≥ target − this on both axes.</summary>
    public const double AcceptedParkSlack = 2.0;
    /// <summary>Own minimum is lowered when a tile's actual is below it by more than this.</summary>
    public const double OwnMinimumSlack = 2.0;
    /// <summary>A larger-refusal axis qualifies only below this fraction of the visible area.</summary>
    public const double RefusalScreenFactor = 0.8;
    /// <summary>Retarget is "unchanged" when every component differs by at most this.</summary>
    public const double RetargetTolerance = 1.0;
    /// <summary>Drift check logs a tile whose frame moved by more than this on any component.</summary>
    public const double DriftTileLogThreshold = 4.0;
    /// <summary>Settle delay = Policy.CoverFade + this, seconds.</summary>
    public const double SettleExtraSeconds = 0.02;
    /// <summary>Delay between a re-land's generation bump and its landing pass, seconds.</summary>
    public const double ReLandDelaySeconds = 0.034;
}

/// <summary>One expected-frame entry.</summary>
internal readonly record struct ExpectedFrame(Rect Frame, double Time);

/// <summary>Snapshot of one managed window's writer state (diagnostics and tests).</summary>
/// <param name="Requested">Target of the most recent write, or null.</param>
/// <param name="WritesInFlight">Writes issued and not yet completed.</param>
/// <param name="PendingRechecks">Refusal rechecks scheduled and not yet finished (diagnostic, D3).</param>
/// <param name="Expected">Expected-frame list as stored (not pruned by reading the snapshot).</param>
/// <param name="ShownSize">Size of the last tile target written, or null.</param>
/// <param name="Minimum">Own learned minimum, or null. An axis may be 0.</param>
/// <param name="Maximum">Own maximum, or null. An unlimited axis is +∞.</param>
/// <param name="Natural">Natural size, or null.</param>
internal sealed record FrameWriterState(Rect? Requested, int WritesInFlight, int PendingRechecks,
    ImmutableArray<ExpectedFrame> Expected, Size? ShownSize, Size? Minimum, Size? Maximum, Size? Natural);

public sealed partial class DisplayEngine
{
    // ----- layout source (#27 replaces with live edits) -----
    /// <summary>Working layout document; LayoutDocument.Empty before Start.</summary>
    internal LayoutDocument Document { get; }
    /// <summary>Id of the framed node, or null.</summary>
    internal string? FramedNodeId { get; }
    /// <summary>Replace the working document and framed id. No write, no render, no save.</summary>
    /// <exception cref="ArgumentNullException">document is null.</exception>
    internal void SetLayout(LayoutDocument document, string? framedNodeId);

    // ----- per-window limits and state -----
    /// <summary>Set a managed window's own minimum, maximum and natural size; unknown key: nothing.</summary>
    internal void SetLimits(WindowKey key, Size? minimum, Size? maximum, Size? natural);
    /// <summary>Writer state of a managed window, or null if not managed.</summary>
    internal FrameWriterState? WriterState(WindowKey key);
    /// <summary>True when frame matches a non-stale expected entry of key (B27). Unknown key: false.</summary>
    internal bool IsExpected(WindowKey key, Rect frame);

    // ----- stage state -----
    /// <summary>A pointer interaction or native window drag is in progress (driven by #27/#18).
    /// Default false. No side effect.</summary>
    internal bool InteractionActive { get; set; }
    /// <summary>B1.</summary>
    internal bool CanPresentDirectly { get; }
    /// <summary>Generation of the most recent landing pass; 0 before any.</summary>
    internal int PlacedGeneration { get; }
    /// <summary>Clock time the most recent landing pass started; null before any.</summary>
    internal double? PlacementStarted { get; }
    /// <summary>Clock time the most recent landing pass completed; null while running or abandoned.</summary>
    internal double? PlacementDone { get; }

    // ----- targets and passes -----
    /// <summary>Targets of all managed windows from the working document (B3). No service call
    /// other than reading ParkingOrigin.</summary>
    internal IReadOnlyDictionary<WindowKey, WindowTarget> ComputeTargets();
    /// <summary>A layout edit was committed: direct presentation when at rest, else re-land (B5).</summary>
    internal void PresentLayout(WindowKey? focus = null);
    /// <summary>Direct presentation (B4).</summary>
    internal void PresentDirectly(WindowKey? focus = null);
    /// <summary>Re-land (B22).</summary>
    internal void ReLand();
    /// <summary>Landing pass for generation g (B23).</summary>
    internal void PlaceWindows(int generation);
    /// <summary>Batched write of the given targets (B6–B12); returns the number of writes issued.</summary>
    internal int WriteTargets(IReadOnlyDictionary<WindowKey, WindowTarget> targets,
                              IReadOnlySet<WindowKey>? except = null, bool learns = true);
    /// <summary>Single-window write through SetFrame (B6–B12); true if a write was issued.</summary>
    internal bool WriteOne(WindowKey key, WindowTarget target, bool learns = true);
}
```

All internal entry points throw `ArgumentNullException` for null non-nullable
arguments. Like public mutating members, they throw `InvalidOperationException`
when called from inside one of this engine's callbacks (B7 of inbound).

## Behaviour

Notation:
- `T` is a write's own target and `A` is the actual frame read back.
- `slack` = `Tuning.FrameRefusalSlack`.
- `now` = `Services.Clock.Now`.
- "Known frame" is `Window(key).Info.Frame`. Setting it replaces `Info` with
  `Info with { Frame = … }`.
- "Same record" means the writer state object captured at write time is still
  the one stored for that key (B30).
- "Re-land" means B22.

### A. Targets

- **B1 (at rest).** `CanPresentDirectly` is true iff all of these hold:
  `IsStarted`, not `IsSuspended`, `Phase == Desk`, `!MotionActive` (the
  stand-in for camera, layout tween and gestures until #15), and
  `!InteractionActive`.
- **B2 (writer state).** Each managed record has a writer state:
  - `Requested = null`, `WritesInFlight = 0`, `PendingRechecks = 0`;
  - `Expected` empty;
  - `ShownSize`, `Minimum`, `Maximum` and `Natural` all null.

  `SetLimits` replaces `Minimum`, `Maximum` and `Natural` as given.
- **B3 (target computation).** `ComputeTargets()` calls
  `ProjectionTargets.Compute` with:
  - `document = Document`;
  - `entries = LayoutSolver.FocusLayout(Document.Root)`, passed as
    `Entries`;
  - framed rect = `entries[FramedNodeId].Rect` when `FramedNodeId` is non-null
    and present, else null;
  - `view = ProjectionView.AtRest(VisibleArea, Policy.Gap, framedRect)`;
  - `parkDisplay = ParkContext()`;
  - `windows`: for each managed record, `Info.Key.Value →`
    `new WindowFacts { LastFrameSize = ShownSize ?? size of Info.Frame,
    Minimum, Maximum, Natural }`;
  - `Policy = new ProjectionPolicy { FixedMinimum = Policy.FixedMinimum,
    TabPadding = Policy.TabPadding, TabInset = Policy.TabInset,
    ParkOutOfViewSmall = Policy.ParkOutOfViewSmall }`;
  - `Tuning = this.Tuning`;
  - `DesktopSlotUsable = false`, empty `CollapsedPanels`, empty
    `FloatingUnitRects`, `ParkOptions = null`.

  The result maps each managed `WindowKey` whose `Value` is in
  `result.Targets` to that target. `Start` sets `Document` and `FramedNodeId`
  from the loaded record (`SavedDisplay.Document`, `.FramedNodeId`). Before
  `Start` they are `LayoutDocument.Empty` and null. `SetLayout` replaces them.
  Neither one touches the saved record (#27 links them).

### B. When a write pass runs (findings 1–5)

- **B4 (direct presentation).** `PresentDirectly(focus)`:
  - If not started or suspended, it does nothing.
  - Otherwise, in this order:
    1. `WriteTargets(ComputeTargets())` with no counting and no generation
       change.
    2. If `focus` is non-null and differs from `FocusedKey`, it schedules
       `Clock.After(0, …)`. That action calls `Services.Windows.Focus(focus)`
       if `focus` is still managed.
    3. It saves (inbound B16), renders once, and requests `OnChange` (C6b).

  No settle and no drift check follow.
- **B5 (layout committed).** `PresentLayout(focus)`:
  - not started or suspended → nothing;
  - `Phase == Overview` → render only (#16 decides, A8);
  - `CanPresentDirectly` → `PresentDirectly(focus)`;
  - otherwise → `ReLand()`.
- **B6 (write pass, batched).** `WriteTargets(targets, except, learns)` does
  nothing and returns 0 when not started or suspended. Otherwise it goes
  through the managed records in **registration order**. For each record whose
  key has an entry in `targets`, it applies the skip rules B8. Each survivor
  gets the bookkeeping of B12 at once and becomes one `FrameWrite(key, T)`.
  - If there are no survivors, no service call is made and 0 is returned.
  - Otherwise it makes exactly **one** `Services.Windows.SetFrames(writes, cb)`
    call. `cb` handles each result **in input order** through B13–B17, using
    that entry's own `T` and learns flag. Then it returns the count.

  Keys in `targets` that are not managed are ignored (findings rule 7).
- **B7 (single write).** `WriteOne(key, T, learns)`:
  - not started, suspended or unknown key → false;
  - a skip rule applies → false;
  - otherwise B12, then one `Services.Windows.SetFrame(key, T, cb)`, then true.
    `cb` runs B13–B17.

### C. Skip rules (findings 6–11), evaluated in order

- **B8.** For key `k` with target `T`:
  1. `k` is in `except` → skip.
  2. **Already resting parked.** Skip when all of these hold: `T.Kind` is
     `Park` or `ParkWithPicture`, `Window(k).Parked` is true, and
     `ParkGeometry.RestsParked(known, T.Rect,
     Services.Windows.VisibleFraction(known))` is true.
  3. **Known frame matches target.** `|known.c − T.Rect.c| ≤
     FrameWriting.SkipTolerance` (1.0) for every c ∈ {X, Y, Width, Height},
     using the existing `Near` helper → skip. (Amended by #11 R2, human
     decision: read-backs at fractional scaling differ by up to 0.5/s per
     edge, so an exact compare never skipped. New AC: known
     (725,35,705,422.6) with target `Tile(725,35,705,423)` → no write; known
     (725,35,705,421.9) → write.)
  4. **Same target in flight.** `Requested == T.Rect` exactly and
     `WritesInFlight > 0` → skip.
  5. Otherwise **write**. This applies even when another write for the window
     is in flight with a different target. Nothing is held back or queued for
     re-issue.
- **B9.** The writer never invents targets. A managed window with no target
  is not written.
- **B10.** "Park target" in this spec always means `Kind` is `Park` or
  `ParkWithPicture`. "Tile target" means `Kind == Tile`.
- **B11.** Skip rule 2 uses the parked flag and known frame as they are
  **before** B12 runs for this write.

### D. Before each write (findings 12; D2)

- **B12.** In this order:
  1. `SetParked(k, T is a park target)`.
  2. For a tile target, `ShownSize = (T.Rect.Width, T.Rect.Height)`.
     A park target leaves `ShownSize` unchanged.
  3. `Requested = T.Rect`.
  4. The expected list is pruned (B26) and `(T.Rect, now)` is appended.
  5. `WritesInFlight += 1`.

  No probed-minimum bookkeeping is done (D2). No capture hold is taken (#16).

### E. Write completion (findings 15–18)

- **B13 (head).** With captured state `s`, captured key `k`, own target `T`,
  flag `isPark` and flag `learns`:
  1. `s.WritesInFlight −= 1`. This always happens, even when `s` is orphaned.
  2. If `A` is null → stop.
  3. If `s` is not the same record → stop.
  4. Prune and append `(A, now)` to `s.Expected`, and set the known frame to
     `A`.
  5. If `!learns` → stop.

  The comparison always uses this write's `T`, never the window's latest
  `Requested`. Completions are **not** generation-guarded.
- **B14 (park write).** If `isPark`:
  1. If `A.Width ≥ T.Width − 2` and `A.Height ≥ T.Height − 2`
     (`AcceptedParkSlack`) → `LowerMinimum(k, (A.Width, A.Height))` (B21).
  2. Then, if `A.Width > T.Width + slack` or `A.Height > T.Height + slack` →
     schedule a **larger** recheck (B16).
  3. A park write whose actual is smaller has no further effect.
- **B15 (tile write).** If not `isPark`:
  1. If `Minimum` is present and (`A.Width < Minimum.Width − 2` or
     `A.Height < Minimum.Height − 2`) (`OwnMinimumSlack`), then `Minimum`
     becomes `(min(Minimum.Width, A.Width), min(Minimum.Height, A.Height))`.
  2. `Services.Limits.Observe(Info.App, (A.Width, A.Height))` is called
     always. With a null app it does nothing, by the store contract.
  3. If `A.Width > T.Width + slack` or `A.Height > T.Height + slack` → schedule
     a **larger** recheck.
  4. Otherwise, if `A.Width < T.Width − slack` or `A.Height < T.Height − slack`
     → schedule a **smaller** recheck.
  5. Otherwise the write is accepted and nothing more happens.

  Thresholds are per axis, either axis is enough, and the comparison is strict
  (`> slack`). Position differences never trigger a recheck.
- **B16 (schedule a recheck).** `s.PendingRechecks += 1`. The recheck captures
  `(s, k, larger/smaller, T, VisibleArea as of now)` and calls
  `Clock.After(Tuning.FrameRefusalRecheckSeconds, recheck)`. It is not tied
  to the generation.

### F. Refusal recheck (findings 19–23; D1, D3)

- **B17 (fire).** When the delay elapses:
  - If `IsSuspended`, or `s` is not the same record: `s.PendingRechecks −= 1`
    and stop. No read is made (D1, A6).
  - Otherwise, call `Services.Windows.ReadFrame(k, r => …)`. On completion:
    1. `s.PendingRechecks −= 1`.
    2. If `s` is not the same record, or `IsSuspended` → stop.
    3. `A' = r ?? known frame`. Set the known frame to `A'`. `A'` is **not**
       added to the expected list.
- **B18 (larger).** An axis *qualifies* if `A'` on that axis is
  `> T + slack` **and** `< RefusalScreenFactor × captured visible area` on
  that axis (width with width, height with height). If no axis qualifies,
  nothing more happens. Otherwise:
  1. `Minimum` becomes, per axis: for a qualifying axis,
     `max(old axis or 0, A' axis)`; for a non-qualifying axis, `old axis or 0`.
  2. If `Natural` is present, each qualifying axis of `Natural` becomes
     `max(Natural axis, A' axis)`.
  3. `Services.Limits.Learn(Info.App, Minimum)`.
  4. Log once (B32).
  5. Go to B20.
- **B19 (smaller).** An axis qualifies if `A'` on that axis is `< T − slack`.
  If no axis qualifies, nothing more happens. Otherwise:
  1. `Maximum` becomes, per axis: for a qualifying axis, the `A'` axis
     (replacing any old value); for a non-qualifying axis, the old axis or
     `+∞`.
  2. Nothing is written to the limit store.
  3. Log once.
  4. Go to B20.
- **B20 (after learning).** Compute `ComputeTargets()`.
  - If `k` has a target `N` with `|N.Rect.c − T.c| ≤ RetargetTolerance` for
    every component `c ∈ {X, Y, Width, Height}`, there is no rewrite. Log
    once ("refusal kept").
  - Otherwise (the target changed or is gone) → `ReLand()`.
- **B21 (lower minimum).** `LowerMinimum(k, size)`:
  1. `before = Limits.Minimum(app)`.
  2. `Limits.Observe(app, size)`.
  3. `after = Limits.Minimum(app)`.
  4. If the window has **no** own `Minimum`, and `before` and `after` are both
     non-null and differ → `ReLand()`. Otherwise nothing.

### G. Re-land and landing pass (findings 2, 3, 26)

- **B22 (re-land).** `ReLand()`:
  - not started or suspended → nothing (A5);
  - `Phase` is `Desk` or `Fading`:
    1. `PlacementGeneration += 1`, and let `g` be the new value.
    2. Schedule `Clock.After(ReLandDelaySeconds, …)`. That action runs
       `PlaceWindows(g)` only if `PlacementGeneration == g` and the engine is
       not suspended.
    3. The phase is unchanged, so a second re-land inside the delay bumps
       again and the first is dropped.
  - `Phase` is `Moving` or `Landing` → `PlaceWindows(PlacementGeneration)`
    at once, with no bump;
  - `Phase == Overview` → no pass (#16, A8).

  In every non-trivial case, render once.
- **B23 (landing pass).** `PlaceWindows(g)`:
  1. If not started or suspended → nothing.
  2. Set `PlacedGeneration = g`, `PlacementStarted = now`,
     `PlacementDone = null`, and `Phase = Landing`. Increment a private pass
     serial `p`.
  3. Write `ComputeTargets()` as in B6 (learns on). Each result decrements a
     per-pass counter that starts at the number of writes issued, after its
     B13–B15 handling.
  4. When the counter reaches 0 **and** `PlacementGeneration == g` **and** `p`
     is still the latest pass → `LandingDone(g)`.
  5. A pass that issues no writes calls `LandingDone(g)` at once, inside
     `PlaceWindows`.
- **B24 (landing done).** `LandingDone(g)`:
  1. `PlacementDone = now`.
  2. `Phase = Fading` (the studied "settling" phase, A7).
  3. Render.
  4. `Clock.After(Policy.CoverFade + SettleExtraSeconds, () => Settle(g))`.
     A non-finite or negative sum is treated as 0 by the clock contract.
- **B25 (settle).** `Settle(g)`: if `PlacementGeneration != g`, or
  `Phase != Fading`, or the engine is suspended → nothing. Otherwise:
  1. `Phase = Desk`.
  2. `DriftCheck(g)` (B28).
  3. Render.
  4. Save.

  Focus, laid-out sizes and float settling are filled by #27 and the floating
  feature.

### H. Expected-frame echoes (findings 24–25)

- **B26 (prune).** Pruning removes every entry with `now − Time > 1.5`
  (strict). It happens before every append (B12, B13) and in `IsExpected`.
  `WriterState` does not prune.
- **B27 (match).** `IsExpected(k, f)`:
  1. Unknown key → false.
  2. Otherwise prune, then return true iff some entry has
     `|f.c − e.Frame.c| ≤ 2.0` for all four components.

  A match does **not** consume the entry. `IsExpected` changes nothing else,
  makes no service call and fires no callback. #27's `WindowMoved` stores the
  reported frame as the known frame first. It then ignores the frame for layout
  when `IsExpected` is true, when `WritesInFlight > 0`, or while the mouse is
  down. That part is #27.

### I. Drift check (findings 27)

- **B28.** `DriftCheck(g)`:
  1. Let `targets = ComputeTargets()`.
  2. For each managed record with a target, in registration order, call
     `Services.Windows.ReadFrame(k, r => …)`. Windows without a target are not
     read.
  3. On completion, act only if all of these hold: `r` is non-null,
     `PlacementGeneration == g`, `Phase == Desk`, not suspended,
     `!InteractionActive`, `s` is the same record, and
     `s.WritesInFlight == 0`. Then:
     - **park target `P`**: if `Services.Windows.VisibleFraction(r) >
       ParkOptions.Default.VisibleFractionThreshold` (0.05, strict) and
       `!ParkGeometry.RestsParked(r, P.Rect, thatFraction)` → set the known
       frame to `r`, then `WriteOne(k, P)`. B8 and B12 apply.
     - **tile target**: if `|r.c − known.c| > 4` for any component → Log once.
       The known frame is **not** updated and nothing is written. (The
       comparison is with the known frame, not with the target.)

### J. Generation, suspend, releaseAll (findings 26, 28, 29)

- **B29.** `PlacementGeneration` is incremented only by:
  - `ReLand()` (B22);
  - `Suspend()` and `ReleaseAll()` (unchanged, inbound B11/B13);
  - later, #15/#16's interrupt-landing.

  `PresentDirectly`, `PlaceWindows`, `WriteTargets`, `WriteOne`, completions
  and rechecks never change it. A stale generation:
  - stops a landing pass from setting `PlacementDone` or calling
    `LandingDone`;
  - cancels a scheduled re-land pass;
  - makes `Settle` and drift-check reads do nothing.

  Write completions still update in-flight counts, known frames and echo
  lists, and can still learn (B13–B15). Rechecks stop while suspended (D1,
  B17).
- **B30 (record identity).**
  - Registration of a **new** record creates a fresh writer state.
  - A refresh of a managed key (inbound B17b) keeps the state.
  - Removal (`RemoveCore`, `Release`) and `ReleaseAll` discard the state, so
    late completions only decrement the orphan's counter, and rechecks stop at
    the same-record test.
  - A key that is removed and re-added gets a new state. Completions of the
    old state's writes do not touch the new record.
  - `ReleaseAll`'s own release writes stay outside the writer (inbound B13
    counts them).
- **B31 (suspend).** `Suspend` makes no write (inbound B11). Writes issued
  before it still complete through B13–B15. Those can lower own minimums,
  call `Observe`, and schedule rechecks, which then stop at B17. A re-land
  requested by `LowerMinimum` while suspended does nothing (B22, A5).

### K. Callbacks and wrapper

- **B32 (log events).** The writer calls `Log` exactly once for each of these
  events, with text that contains `k.Value` (no other content contract):
  - own minimum learned (B18);
  - own maximum learned (B19);
  - refusal kept (B20);
  - tile drift (B28).

  The writer makes no other `Log` calls.
- **B33 (wrapper).** The body of every `SetFrames`/`SetFrame`/`ReadFrame`
  completion and every `Clock.After` action registered by the writer runs
  inside the same call wrapper as public members. So callbacks it causes
  (`Log`, `OnChange` when the B35 trigger set changed, for example `Phase`
  Landing → Fading → Desk) are delivered at the end of that body, in order,
  under the re-entry guard. Internal entry points called from outside any call
  (tests) behave the same way. Nested internal calls do not open a second
  wrapper.
- **B34 (re-frame, C2).** A re-frame (inbound B34) is now:
  1. `ReframeCount += 1`.
  2. If started and not suspended → `PresentLayout()`. That is either a
     direct presentation (save, one render, `OnChange` requested) or a re-land
     (one render). Otherwise → render once, as before.

## Edge cases & errors

- **E1.** A target with non-finite components is passed to the window system
  as is. The adapter completes it with null (interfaces B8), and B13 drops it.
  The projection never produces such targets.
- **E2.** `SetFrames` is never called with an empty list, and never with
  duplicate keys (one target per managed key).
- **E3.** A completion for a window that was removed and re-added: the old
  state's counter is decremented. The new record's known frame, echo list,
  limits and the store are untouched.
- **E4.** A recheck whose read returns null uses the known frame, which is
  normally the completion's actual.
- **E5.** A larger recheck where an axis is exactly `0.8 ×` the visible area
  on that axis does not qualify (strict `<`).
- **E6.** `Policy.CoverFade` NaN or negative → settle runs on the next clock
  turn (delay treated as 0).
- **E7.** `WriteOne`, `WriteTargets`, `PresentDirectly`, `PlaceWindows` and
  `ReLand` on an engine that is not started or is suspended make no service
  call and change nothing.
- **E8.** Null `key`, `targets`, `document` → `ArgumentNullException` before
  any change. Called from inside a callback → `InvalidOperationException`.
- **E9.** `Tuning = null` → `ArgumentNullException`, value unchanged.

## Acceptance criteria

All criteria are [mac]: xUnit tests in `tests/CanvasWm.Engine.Tests/Writer/`
using the existing fakes, with no OS access.

### Rig

- `ManualClock(100)`.
- `FakeWindowSystem` with `Screens = [(0,0,1440,900)]`.
- `InMemoryLimitStore`, `InMemoryDisplayStore`, `RecordingRenderer`,
  `FakeCapture`.
- Key `new EngineKey(new DisplayKey("D1"), "default")`.
- Visible area `VA = (0,25,1440,875)`; `DisplayFrame = (0,0,1440,900)` set
  after construction.
- `EnginePolicy.Default` (gap 10, `CoverFade` 0.14) and `Tuning.Default`.
- Windows `a`, `b`, `c`, `p` are `WindowInfo(key, pid, "X", new AppId("com.x"),
  "t", frame, true)`. Each is also added to the fake with the same frame.

The engine is started with `Start([...])`, then `SetLayout(doc, framed)` is
called. Then baselines are taken (`Writes`, `Reads`, `Plans`, `SaveLog`,
`ManualClock.PendingCount`, `PlacementGeneration` = `g0`).

"**Complete**" means `fake.Step()` at the stated clock time, reached by
`ManualClock.Advance`. Times compare within `1e-9`. Rects compare exactly.

Layouts:
- **L2** = `X{s}[a(.5), b(.5)]`. Targets: a `Tile(10,35,705,855)`, b
  `Tile(725,35,705,855)`.
- **L2F** = L2 with `a` framed. Targets: a `Tile(10,35,1420,855)`; b
  `Park(1439,899,705,855)` when b's last size is 705×855.
- **L3** = `X{s1}[a(.5), Y{s2}[b(.5), c(.5)](.5)]`. Targets: a
  `Tile(10,35,705,855)`, b `Tile(725,35,705,423)`, c `Tile(725,468,705,422)`.
- `PwP(z, m)` means `ParkGeometry.ParkFrame(engine.ParkContext(), z, m)` with
  kind `ParkWithPicture`. `PwP((1001,600),(200,600))` is `(1439,899,1001,600)`.
  If the two disagree, the expression wins.

### Wiring and targets

- **AC1** [mac] `Tuning`:
  - a new engine has `ReferenceEquals(Tuning, Tuning.Default)`;
  - setting `Tuning.Default with { FrameRefusalSlack = 10 }` reads back, with
    no service call, render or callback;
  - `Tuning = null` → `ArgumentNullException`;
  - setting it from inside an `OnChange` handler →
    `InvalidOperationException`;
  - with slack 10, a tile write whose actual height is target + 11 schedules a
    recheck, and target + 10 does not.

  (C1, B15)
- **AC2** [mac] `ComputeTargets`:
  - L3 gives exactly the three L3 tiles;
  - L2F gives a `Tile(10,35,1420,855)` and b
    `Park(1439,899,705,855)`;
  - with `Policy.Gap = 20`, L2 gives a `Tile(20,45,690,835)`;
  - an empty document gives an empty map;
  - a layout key that is not managed is absent;
  - after `Start` with a pre-saved record `(L2, framed "a")`, `Document` and
    `FramedNodeId` equal that record's and the targets are L2F's;
  - with `SetLimits(b, null, (300, +∞), null)`, L3 gives b
    `Tile(928,35,300,423)`;
  - L2F with `ParkOutOfViewSmall = true`: b's `ShownSize` is (705,855) (from
    an earlier `WriteOne(b, Tile(725,35,705,855))`, completed), and its known
    frame is later set to `(1439,899,240,292)` (a park-small write,
    completed). b's target is still `ParkSmallFrame(ctx, (705,855))`, not the
    park-small frame of 240×292.

  (B3, A3)

### Direct pass, skip rules, bookkeeping

- **AC3** [mac] Worked ex. 1. Setup: L3, a and c at their targets, b known
  `(725,35,705,855)`. At 100, `PresentDirectly()`:
  - `Writes` gains exactly `[(b,(725,35,705,423), batchId non-null)]` (one
    `SetFrames` call);
  - `WriterState(b)`: `Requested = (725,35,705,423)`, `WritesInFlight = 1`,
    `Expected = [((725,35,705,423),100)]`, `ShownSize = (705,423)`;
  - `Window(b).Parked` is false;
  - exactly one save and one render; `OnChange` once;
  - generation `g0`; clock pending unchanged.

  Complete at 100.05:
  - `WritesInFlight 0`; `Expected` has a second entry
    `((725,35,705,423),100.05)`; known frame `(725,35,705,423)`;
  - `PendingRechecks 0`; clock pending unchanged; store unchanged; `Phase`
    Desk.

  (B4, B6, B12, B13, B15)
- **AC4** [mac] Worked ex. 2 and 3, continuing AC3 before the completion:
  - At 100.01, `PresentDirectly()` issues no write (no new `SetFrames`).
  - `WriteOne(b, Tile(725,35,705,600))` returns true and adds one
    `SetFrame` record (batch null). `WritesInFlight` is 2 and `Requested` is
    the new rect.
  - `WriteOne(b, Tile(725,35,705,600))` again → false.
  - Complete at 100.05 (actual `(…,423)`) → no recheck. Complete at 100.06
    (actual `(…,600)`) → no recheck. `WritesInFlight` is 0 and the known frame
    is `(725,35,705,600)`.

  (B8.4–5, B13)
- **AC5** [mac] Skip rules:
  - (a) When every window is at its target, `PresentDirectly()` makes no
    `SetFrames` call but still saves, renders once and fires `OnChange` once.
  - (b) Worked ex. 12, in variants:
    - p parked (`SetParked`), known `(100,80,200,200)`,
      `WriteOne(p, Park(100,100,200,200))` → false;
    - known `(100,30,200,200)` → true;
    - p not parked with known `(100,80,200,200)` → true;
    - p parked, known `(1439,860,705,855)`, target `Park(1439,899,705,855)` →
      false (less than 5 % visible);
    - the same with kind `ParkWithPicture` → false.
  - (c) `WriteTargets({a: T}, except: {a})` → 0, no service call.
  - (d) Targets for unmanaged keys are ignored.

  (B8, B9, B10, B11)
- **AC6** [mac] Pre-write bookkeeping:
  - `WriteOne(b, Park(1439,899,705,423))` → `Window(b).Parked` true and
    `ShownSize` unchanged (null);
  - then `WriteOne(b, Tile(725,35,705,423))` → `Parked` false and `ShownSize`
    (705,423);
  - a write at 102 to a window whose list has entries from 100 and 100.6
    leaves `[(…,100.6), (T,102)]` (100 is older than 1.5 s);
  - in a batch, entries are in registration order and results are processed
    in that order.

  (B6, B12, B26)

### Echoes

- **AC7** [mac] Worked ex. 4, after AC3's completion:
  - At 100.9, `IsExpected(b,(726,35,706,423))` → true, and true again on a
    second call (not consumed);
  - `(727,35,705,423)` → true (Δ = 2);
  - `(727.5,35,705,423)` → false;
  - at 101.56, `IsExpected(b,(725,35,705,423))` → false, and
    `WriterState(b).Expected` is now empty.

  Boundary: a fresh write at 100 with no completion. At 101.5,
  `IsExpected(b,T)` → true. At 101.75 → false. An unknown key → false.
  `IsExpected` makes no service call and fires no callback. `WriterState`
  returns entries without pruning.

  (B26, B27)

### Completions

- **AC8** [mac] Worked ex. 11 and identity:
  - `SetFails(b, true)`, `WriteOne(b, Tile(725,35,705,423))`, complete → null
    result:
    - `WritesInFlight 0`; the known frame is unchanged; `Expected` has only
      the write entry;
    - no recheck; the store is untouched.
  - After `SetFails(b, false)`, `PresentDirectly()` (L3) writes b again.
  - Remove and re-add before completion: write b, then
    `WindowRemoved(b,false)`, then `WindowAdded(b info with frame F)`, then
    complete:
    - the new record's frame is still `F`;
    - `WriterState(b)` has `WritesInFlight 0` and empty `Expected`;
    - no exception.
  - A duplicate `WindowAdded(b)` (refresh) keeps `Requested`, `Expected` and
    the limits.

  (B13, B30, E3)
- **AC9** [mac] Tile learning:
  - Worked ex. 15: `SetLimits(a, (500,400), null, null)`, store seeded
    `com.x = (900,900)` via `Learn`, `WriteOne(a, Tile(10,35,450,855))`, then
    complete. Result: `Minimum == (450,400)`, the store is
    `com.x = (450,855)`, no recheck.
  - With own minimum `(460,400)` and actual width 459 (within 2), the minimum
    is unchanged.
  - A window with `App = null`: completion with no store change and no
    exception.

  (B15)
- **AC10** [mac] Thresholds (L3, c target `Tile(725,468,705,422)` via
  `WriteOne`):
  - fake min height 446 → actual 446 → no recheck (`PendingRechecks 0`, clock
    pending unchanged);
  - min height 447 → one recheck due at completion time + 0.25
    (`PendingRechecks 1`);
  - fake max width 681 → none; max width 680 → smaller recheck;
  - refusing writes with the current frame `(0,0,705,422)` → position-only
    difference → no recheck;
  - refusing with the current frame `(725,468,735,392)` (one axis +30, one
    −30) → a **larger** recheck, which later learns `Minimum == (735, 0)`.

  (B15, B16, B18)

### Refusal rechecks and learning

- **AC11** [mac] Worked ex. 5 (tile, larger, full timeline). Setup: L3, a and
  b at their targets, c known `(725,35,705,855)`, fake constraint
  `min (0,600)` on c.
  1. At 100, `PresentDirectly()` → one batch `[(c,(725,468,705,422))]`.
  2. Complete at 100.05 → known `(725,468,705,600)`; recheck due 100.30.
  3. `Advance(0.25)` → `Reads` gains `[c]` at 100.30.
  4. Complete:
     - `PendingRechecks 0`; known `(725,468,705,600)`; `Expected` count
       unchanged (2);
     - `Minimum == (0,600)`; store `com.x == (0,600)`;
     - exactly one Log;
     - generation `g0+1`; `Phase` Desk; one render.
  5. `Advance(0.034)` → one batch exactly
     `[(c, PwP((1001,600),(200,600)))]` at 100.334:
     - `Phase` Landing, `PlacedGeneration g0+1`, `PlacementStarted 100.334`,
       `PlacementDone` null, `Window(c).Parked` true.
  6. Complete at 100.384:
     - `PlacementDone 100.384`, `Phase` Fading, the store is still `(0,600)`,
       and no further re-land (generation unchanged).
  7. `Advance(0.16)` → at 100.544:
     - `Phase` Desk; `Reads` gains `[a, b, c]`; exactly one save.
  8. `Pump()` → no new writes.

  (B15–B18, B20, B22–B25, B28)
- **AC12** [mac] Worked ex. 6 and the screen factor. As AC11 but with
  `min (0,760)`:
  - after the recheck: `Minimum` null, store empty, generation `g0`, no Log,
    clock pending empty;
  - at 100.9, `IsExpected(c,(725,468,705,760))` → true.
  - With `min (0,699)` → `Minimum (0,699)`.
  - With `min (0,701)` → nothing learned.
  - With an existing own minimum `(100,550)` (the tile completion leaves it
    unchanged: 600 is not below 548) and actual 600 (qualifying height) →
    `Minimum (100,600)`; width keeps the old 100; `Learn` is called with
    `(100,600)` (store `(100,600)`).
  - With an existing own minimum `(100,650)`, issue the write explicitly via
    `WriteOne(c, Tile(725,468,705,422))` (a direct pass would already target
    height 650): the tile completion first lowers it to `(100,600)` (B15.1,
    since 600 < 648). The recheck then gives `(100,600)`.
  - With `Natural = (400,500)` → `Natural` becomes `(400,600)`.

  (B15, B18, E5)
- **AC13** [mac] Worked ex. 7 (smaller). L3, b target
  `Tile(725,35,705,423)`, fake `max (300,10000)`. Completion and recheck give:
  - `Maximum == (300, +∞)`;
  - store `Entries` empty and `PersistCount` 0;
  - exactly one Log;
  - `ComputeTargets()[b] == Tile(928,35,300,423)`;
  - generation `g0+1` and a pass 0.034 s later writes b there.

  (B19, B20)
- **AC14** [mac] Refusal kept. L2F with `ParkOutOfViewSmall = true`, b known
  `(725,35,705,855)` and fake `min (705,855)` on b.
  `WriteOne(b, ComputeTargets()[b])` writes `ParkSmallFrame(ctx,(705,855))`.
  The completion's actual is `(1439,899,705,855)`, so `LowerMinimum` runs
  (store empty, no effect) and a larger recheck is scheduled. The recheck:
  - learns `Minimum == (705, 0)` (855 ≥ 700 does not qualify) and store
    `(705,0)`;
  - the retarget is identical → two Logs (minimum learned, refusal kept);
  - generation `g0`; no scheduled pass.

  (B14, B18, B20)
- **AC15** [mac] Worked ex. 9. Store `com.x = (800,600)`, b has no own
  minimum. `WriteOne(b, Park(1439,899,705,423))`, then complete (accepted):
  - store `(705,423)`; generation `g0+1`; one pass scheduled.
  - The same with `SetLimits(b,(10,10),null,null)` → store `(705,423)`,
    generation `g0`.
  - With an empty store → generation `g0`.

  (B14, B21)
- **AC16** [mac] Worked ex. 10. L3, b fake `min (0,600)`.
  `WriteOne(b, Park(1439,899,705,423))` → actual `(1439,899,705,600)`. Then:
  - larger recheck → `Minimum (0,600)`, store `(0,600)`;
  - generation `g0+1`;
  - the pass writes b to `PwP((1001,600),(200,600))`.

  A park write whose actual is 30 smaller on both axes → no recheck and no
  store change.

  (B14, B18, B20)
- **AC17** [mac] Recheck reads:
  - After AC11's completion, `SetFails(c, true)` → the read returns null → the
    known frame `(725,468,705,600)` is used and `Minimum (0,600)` is learned.
  - If the fake frame is reset to `(725,468,705,422)` before the read
    (`SetFrameDirect`) → no axis qualifies, nothing is learned, the known
    frame becomes `(725,468,705,422)`, and the generation is `g0`.
  - `VisibleAreaChanged((0,25,1440,700))` between scheduling and firing: the
    qualification still uses the captured 875 (600 qualifies).

  (B16–B18, E4)

### Suspend, releaseAll, identity

- **AC18** [mac] D1, using AC11's setup:
  - `Suspend()` at 100.1, after the completion that scheduled the recheck.
    At 100.30: `Reads` unchanged, `PendingRechecks 0`, `Minimum` null, store
    empty, generation `g0+1` (Suspend only).
  - Variant with the read issued before `Suspend` and completed after it: no
    learning, and the known frame is unchanged by the read.
  - Variant with `Suspend` between write and completion: the completion still
    sets the known frame, appends to `Expected`, and schedules the recheck,
    which then does nothing.

  (B17, B31, D1, D3)
- **AC19** [mac] Release paths:
  - Write b, then `ReleaseAll(null, done)`, then `Pump()`:
    - no exception; `Windows` empty; `done` runs exactly once;
    - no write other than the release writes.
  - A recheck pending at `ReleaseAll` → when it fires, `Reads` is unchanged
    and the store is unchanged.
  - `WindowRemoved(c,false)` with a pending recheck → no read.
  - `Release(b)` → `WriterState(b)` null.

  (B30, B17)
- **AC20** [mac] Learning off. Store `com.x = (900,900)`, fake `min (0,600)` on
  c. `WriteTargets({c: Tile(725,468,705,422)}, learns: false)`, then complete:
  - the known frame is `(725,468,705,600)` and `Expected` has 2 entries;
  - store still `(900,900)`; no recheck; `Minimum` null.

  (B13)

### Re-land, landing, settle, drift

- **AC21** [mac] Re-land at rest (L3, all known frames differ from targets):
  - `ReLand()` at 100 → generation `g0+1`, `Phase` Desk, one render, one clock
    action due 100.034, no write.
  - `Advance(0.034)` → one batch of 3 entries in order a, b, c.
  - Two `ReLand()` calls at 100 and 100.02 → generation `g0+2` and exactly
    one batch (at 100.054).
  - `ReLand()` then `Suspend()` at 100.01 → no batch ever.
  - `ReLand()` on a suspended or not-started engine → generation unchanged, no
    clock action, no render.

  (B22, B29)
- **AC22** [mac] Re-land from other phases:
  - `SetPhase(Landing)` (or `Moving`), then `ReLand()` → an immediate batch,
    generation unchanged, `PlacedGeneration == PlacementGeneration`.
  - `SetPhase(Fading)`, then `ReLand()` → bump and a delayed pass.
  - `SetPhase(Overview)`, then `ReLand()` → no write, no bump, no clock
    action.

  (B22, A8)
- **AC23** [mac] Worked ex. 13, landing counting (L3, all three differing):
  - `PlaceWindows(PlacementGeneration)` at 100 → one batch of 3, `Phase`
    Landing, `PlacementStarted 100`.
  - Complete at 100.08 → `PlacementDone 100.08`, `Phase` Fading, one clock
    action due 100.24, `OnChange` fired once (phase change).
  - Variant: `Suspend()` at 100.07, then complete at 100.08 →
    `PlacementDone` null; `Phase` Desk; known frames of a, b, c updated;
    `Expected` lists have 2 entries each; no settle action.
  - With all windows at target → `LandingDone` inside the call: `Phase`
    Fading and `PlacementDone 100`.
  - Two `PlaceWindows(g)` calls with the same g before completion:
    - The second call issues no writes (all three have the same target in
      flight, B8.4), so it calls `LandingDone` inside the call
      (`PlacementDone 100`, `Phase` Fading, one settle action).
    - Completing the first batch afterwards does **not** call `LandingDone`
      again (it is no longer the latest pass): `PlacementDone` stays 100 and
      no second settle action is scheduled.

  (B23, B24, B29, A9)
- **AC24** [mac] Settle:
  - After AC23 (done 100.08), `Advance(0.16)` → at 100.24: `Phase` Desk,
    `Reads` gains `[a, b, c]` (registration order), exactly one save, one
    render.
  - With `Policy = Policy with { CoverFade = 0.3 }` (set before landing) →
    the settle is at 100.40.
  - `ReLand()` at 100.1 (during Fading) → bump. The old settle at 100.24 does
    nothing (no reads, `Phase` not changed by it).
  - Windows without a target are not read.

  (B24, B25, B28)
- **AC25** [mac] Worked ex. 14, drift of a parked window. L2F; a known
  `(10,35,705,855)`; b known `(725,35,705,855)`.
  1. `ReLand()` at 100; `Advance(0.034)`; complete at 100.084; `Advance(0.16)`
     → reads `[a, b]` at 100.244.
  2. `SetFrameDirect(b,(1200,400,705,855))`. `Step()` (read a, unchanged),
     then `Step()` (read b):
     - known b `(1200,400,705,855)`;
     - exactly one new write `(b,(1439,899,705,855), batch null)`.

  Guards, each in a fresh run with the same moved frame give no write and an
  unchanged known frame:
  - generation bumped (`ReLand`) before the read completes;
  - `SetPhase(Landing)`;
  - `InteractionActive = true`;
  - a write to b in flight (`WriteOne(b, Park(1439,899,705,850))` before the
    read);
  - frame `(1439,860,705,855)` (rests parked);
  - frame `(1430,890,705,855)` (≤ 5 % visible).

  (B28)
- **AC26** [mac] Drift of a tile. AC25 setup:
  - `SetFrameDirect(a,(16,35,1420,855))` before a's drift read → after the
    read: exactly one Log, known a still `(10,35,1420,855)`, no write.
  - `(14,35,1420,855)` (Δ 4) → no Log.

  (B28, B32)

### Presentation entry points and re-frame

- **AC27** [mac] `PresentLayout`:
  - at Desk with no motion → direct batch, generation `g0`;
  - `MotionActive = true` → re-land (generation `g0+1`, no immediate write);
  - `InteractionActive = true` → re-land;
  - `SetPhase(Overview)` → one render, no write;
  - suspended → nothing.
  - `PresentDirectly(a)` with `FocusedKey != a` → no focus request during the
    call; after `Advance(0)` → `FocusRequests` ends with `a`.
  - With `FocusedKey == a` → no clock action.
  - With a removed before `Advance(0)` → no focus request.
  - `CanPresentDirectly` matches B1 for each listed state.

  (B1, B4, B5)
- **AC28** [mac] Re-frame hook (C2):
  - Started with L2 at targets, `VisibleAreaChanged((0,25,1440,800))` →
    `ReframeCount +1`, exactly one `SetFrames` batch
    `[(a,(10,35,705,780)), (b,(725,35,705,780))]`, exactly one render,
    generation unchanged.
  - `Policy = Policy with { Gap = 20 }` → one batch with
    `[(a,(20,45,690,835)), (b,(730,45,690,835))]`.
  - `Policy = Policy with { TabTint = 0.5 }` → no write.
  - `Resume([...])` on a suspended started engine with L2 and b's known frame
    changed → a direct batch for b.
  - `Suspend()` then `Resume([])` on a never-started engine → no write, one
    render (inbound behaviour; a plain `Resume` without a prior `Suspend` does
    nothing, inbound B12).

  (B34, C2)

### Callbacks, wrapper, hygiene

- **AC29** [mac] Wrapper:
  - In AC23 the completion that ends the landing delivers `OnChange` exactly
    once, after the completion's state changes (`Phase == Fading` inside the
    handler).
  - An `OnChange` handler that calls `Suspend()` during that delivery →
    `InvalidOperationException`.
  - A `Log` handler that calls `WriteOne` → `InvalidOperationException`.
  - `PresentDirectly()` with no state change still fires `OnChange` once.

  (B33, C6)
- **AC30** [mac] Existing behaviour preserved: every test of
  `specs/engine-inbound-api.md` (AC1–AC34), `specs/engine-interfaces.md` and
  `specs/engine-projection-targets.md` still passes unchanged. (C2–C7)
- **AC31** [mac] Build and inspection:
  - `dotnet build CanvasWm.slnx` gives 0 warnings;
  - Engine still has exactly one `ProjectReference` (Core) and no
    `PackageReference`;
  - no file under `src/CanvasWm.Core`, `src/CanvasWm.Engine/Services`,
    `src/CanvasWm.Engine/Rendering` or `tests/CanvasWm.Engine.Tests/Fakes` is
    changed;
  - by reflection, `DisplayEngine` gains exactly one new public member
    (`Tuning`), and `FrameWriting`, `FrameWriterState` and `ExpectedFrame` are
    not public;
  - `FrameWriting` constants equal 1.5, 2.0, 2.0, 2.0, 0.8, 1.0, 4.0, 0.02 and
    0.034;
  - the writer source files contain no numeric literal `24` or `0.25` (these
    are read from `Tuning`) and no `0.05` (it is read from `ParkOptions`);
  - no file in `src/` or `tests/` contains the studied app's name or the
    licence-module word;
  - no member or type name contains `Probe` (D2).

  (C1–C7, D2)

**Count: 31 [mac] (AC1–AC31), 0 [windows].** Windows-side checks belong to
#11 (`SetFrames` via `DeferWindowPos`, per-entry read-back of the extended
frame bounds in DIPs, null for elevated/destroyed windows) and #14 (parking).
#11's verification list should include: a refusing app (a fixed-size dialog)
triggers one recheck and is parked with a picture afterwards; a window that
the OS pulls back from a park position is re-parked after a landing.

## Assumptions

- **A1. Tuning is a public settable property** (C1), not a constructor
  overload. It mirrors `Policy` but has no side effects. The App sets it once.
- **A2. Constants.** `FrameRefusalSlack` (24) and `FrameRefusalRecheckSeconds`
  (0.25) are read from `Tuning`. The 5 % drift threshold is read from
  `ParkOptions.Default.VisibleFractionThreshold`. The settle delay is
  `Policy.CoverFade + 0.02`. The other findings constants are algorithm
  literals and are named in the internal `FrameWriting` class. They are not
  added to `Tuning`, because tuning-constants keeps "pure algorithm literals"
  in their own feature specs. *Review:* the tuning spec lists the "refusal
  factor 0.9". The findings correct this to **0.8**, and 0.8 is used here.
- **A3. Target source.** Until #27 owns the live layout, targets come from an
  engine-held working document. It is loaded at `Start` and replaced by the
  internal `SetLayout`. The framed rect comes from `FocusLayout`.
  `DesktopSlotUsable` is false (the #27 human decision: flag default off).
  There are no collapsed panels (#28) and no float rects (floating feature).
  `LastFrameSize = ShownSize ?? known size`, so a window parked at a small
  size does not shrink further on every pass. *Review:* this uses the studied
  app's `shownSize` for a purpose the findings do not state outright.
- **A4. Batching (D4).** A full pass (`WriteTargets`, used by direct
  presentation and landing passes) always uses `SetFrames`, even with one
  entry. It never calls it with zero entries. `WriteOne` (drift re-park, and
  #27 snap-back) uses `SetFrame`. Completion handling is identical per entry.
- **A5. Re-land is a no-op while suspended or not started.** *Needs human
  review.* This extends D1: a park completion that lowers the store minimum
  during suspend does not schedule a landing either. `Resume` re-frames
  anyway (C2).
- **A6. Suspended or orphaned rechecks make no read.** The studied app reads
  first and then stops at the identity test. Skipping the read is
  unobservable, apart from one fewer OS call. The diagnostic counter is still
  decremented (D3).
- **A7. Phase mapping.** The studied "settling" phase (raw 4) is
  `StagePhase.Fading` (raw 4). A landing pass sets `Landing`. The re-land bump
  does **not** change the phase, so quick successive re-lands coalesce (only
  the newest generation's pass runs). *Review.*
- **A8. Overview.** `PresentLayout` and `ReLand` in `Overview` only render. The
  findings say other phases call the landing pass directly. Overview writes
  are #16's (with learning off), so they are deferred rather than writing real
  frames under the overview. *Review.*
- **A9. Per-pass guard.** Besides the generation, only the newest landing pass
  may finish the landing. Two passes with the same generation (B22 from
  `Landing`) would otherwise call `LandingDone` twice. CanvasWM choice.
- **A10. Re-frame now presents** (C2). *Needs human review.* `Resume`,
  `VisibleAreaChanged` and relayout policy changes now write frames: directly
  at rest, else by re-land. The findings name only the landing triggers. The
  inbound spec left B34 for this feature.
- **A11. Picture hold deferred.** Findings rule 13 (`parkedSmall`, capture
  holds) needs the cover feature. `ShownSize` is kept because A3 uses it.
- **A12. Callbacks from completions and timers** go through the call wrapper
  (C6a). `PresentDirectly` forces one `OnChange` (C6b, findings rule 1), even
  though inbound B35 normally fires only on trigger-set changes.
- **A13. The drift check reads only windows that have a target**, in
  registration order. A window without a target has nothing to compare.
- **A14. Unlimited maximum axis = `+∞`.** The projection's maximum clamp
  ignores non-finite components (projection B19), so `+∞` means "no clamp".
- **A15. Fixed log events** (B32). There is one call per event and no text
  contract beyond containing the key.
- **A16. The store-minimum re-land (B21) is kept for parity.** It currently
  changes no target, because the projection does not read the store (#25
  exclusion, D2). The resulting pass writes nothing and only re-runs settle
  and the drift check. *Review:* whether to drop it.
- **A17. The parked flag is set by the writer** (C5). This extends inbound
  B27. As a result, windows the writer parked are selected by `ReleaseAll`
  and written back on removal.
- **A18. `InteractionActive`** is a plain internal flag (stand-in for
  interaction and native drag) until #27/#18 own it.
- **A19. Single and batch writes skip when suspended or not started** (E7).
  The studied app relies on callers instead.

### Reported for review (earlier specs not changed)
- `specs/engine-inbound-api.md` B27: the parked flag is also set by the writer
  (C5).
- `specs/engine-inbound-api.md` B34: the re-frame body is now defined here
  (C2).
- `specs/tuning-constants.md` "pure algorithm literals" lists the refusal
  factor 0.9. The findings correct it to 0.8 (A2).
- `findings/engine-frame-writer.md` writes `Observe(size, appId)`. The
  interface is `Observe(app, size)`, and this spec uses the interface.

## Traceability

| Spec item | Findings rule / example / decision |
|---|---|
| B1, B4, AC3, AC5a, AC27, AC29, A12 | 1; ex. 1 |
| B23, B24, AC23, A9 | 2; ex. 13 |
| B22, AC21, AC22, A5, A7, A8 | 3, 26 |
| B7, WriteOne, AC4, AC25 | 4, 11; ex. 3 |
| `learns`, AC20 | 5 |
| B8–B11, AC4, AC5 | 6–11; ex. 2, 3, 12 |
| B12, AC6, A11 | 12, 13 (hold deferred) |
| D2, AC31 | 14 (omitted) |
| B13, AC8 | 15; ex. 11 |
| B14, B21, AC14–AC16, A16 | 16, 22; ex. 9, 10 |
| B15, AC9, AC10 | 17; ex. 8, 15 |
| B16, B17, AC17, AC18, A6, D1, D3 | 18, 19, 28; Unknowns (counter) |
| B18, AC11, AC12 | 20; ex. 5, 6 |
| B19, AC13, A14 | 21; ex. 7 |
| B20, AC11, AC13, AC14 | 23 |
| B26, B27, AC7 | 24, 25; ex. 4 |
| B29, B31, AC18, AC21, AC23 | 26, 28; ex. 13 |
| B25, B28, AC24–AC26, A13 | 27; ex. 14 |
| B30, AC8, AC19 | 29 |
| B3, AC2, A3 | #25 placement note (Engine maps policy → projection) |
| B6, A4, D4 | human decision 4; Windows notes (SetFrame contract) |
| B34, C2, AC28, A10 | inbound B34 hook |
| C1, AC1, A1, A2 | tuning-constants A10; constants list |
| B32, A15 | Unknowns (log texts are diagnostics) |
| AC30, AC31 | CLAUDE.md architecture; earlier specs |
