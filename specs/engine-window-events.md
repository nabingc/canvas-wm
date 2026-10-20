# Spec: engine-window-events

## Goal

Feature #27 is the last part carved out of #13. It defines how the engine
reacts to window events and to two navigation commands. It builds on the
shell (#23), the targets (#25) and the frame writer (#26).

1. **Start.**
   - Reload the saved layout, or ignore it when it no longer fits the current
     windows.
   - Make sure the tree has a desktop node.
   - Tile every dockable window that has no place yet, next to the framed
     area, in a fixed key order.
   - Then land all windows once.
2. **WindowAdded.**
   - A new dockable window is tiled next to the focused window (Core
     `Placement.PlaceNew`).
   - Non-dockable windows are left alone.
   - The "float onto the visible empty desktop" rule exists only behind a new
     policy flag, which is off by default.
3. **WindowRemoved.**
   - The window's space goes to its siblings. `ShareRemovedSpace` chooses the
     sharing variant or the adjacent variant.
   - Then the framing is fixed.
4. **WindowFocused.**
   - If the user focuses a window that is not properly visible, the engine
     frames it. This is called a "reveal".
   - Reveals are suppressed for a short time after the engine's own focus
     changes and after a close.
5. **WindowMoved.**
   - Echoes of the engine's own writes are ignored.
   - A tiled window that an app moved or resized is put back ("snap-back").
   - After three snap-backs within three seconds, the engine accepts the
     app's size and learns a limit instead.
   - A large self-grow means "show this window alone". A later shrink means
     "undo that".
6. **ArrangeAll** rebuilds the tree as a spiral in front-to-back order.
   **Move(toward)** frames the neighbouring tile.
7. **Saving.** Every structural change and every landing settle is saved
   synchronously.

`findings/engine-window-events.md` replaces rules 23–42 of
`findings/engine-tiling-loop.md` where the two differ. This spec follows the
new findings file, except where a human decision below overrides it.

**Human decisions applied:**
- **H1.** The desktop-slot float rule is gated by a new policy flag,
  `EnginePolicy.FloatNewWindowsOnDesktop`. Its default is **false**.
  - With the flag off, new dockable windows always tile.
  - The rule never applies during `Start`.
- **H2.** When nothing is framed, `Move(toward)` starts from the focused
  window's tree panel and frames the neighbour.
  - The neighbour then fills the stage and the other tiles park.
  - This is a re-land at rest, with no camera animation.
- **H3.** There is no docked-tile cap of any kind.
  - `EnginePolicy.MaxDockedTiles` is **removed** from CanvasWM (C12). It was
    the studied app's gated tile cap.
- **H4 (reverses the findings' tie rule).** After a landing, if no candidate
  window has ever been focused, the engine focuses **nothing**. It makes no
  `Focus` call and leaves the user's current OS focus alone.
  - This changes the outcome of findings worked examples 1 and 24 (see B35,
    AC1, AC28).
- **Single change.** This feature is delivered as one change, not split.
- **Interim minimums.** These are temporary. The code comments name them as
  such.
  - **(M19)** Until #19 slot-restore exists:
    - a temporary removal removes the panel exactly like a close, so there is
      no hidden list;
    - the away table keeps the key with the record's `RememberedSpot`, which
      is normally null;
    - a returning window is placed with `PlaceNew`;
    - remembered spots are never used for placement.
  - **(M22)** Until #22 floating-panels exists, every "float" outcome gives
    the window **no panel**.
    - The float outcomes are: a non-dockable window at start, a non-dockable
      new window, a `NeedsFloat` result, and the H1 desktop float.
    - The window stays managed (focus, known frame, release, removal).
    - It has no target, so the engine never moves or resizes it.

**Out of scope.** Each item has an owner:
- #19: hidden-list hide and restore, and dock-spot redock.
- #22: floating panels, including raising and settling them.
- #15: camera motion, pending reveals while moving, history navigation,
  saved-view use, and the public `ToggleMaximize`. That method stays a stub;
  here zoom and un-zoom are internal at-rest helpers.
- #16: the overview.
- #21 or a later feature: drag-to-dock, `NativePointerDragged` and
  `NativeMouseUp`.
- #22: `FloatAll` and `ToggleDocked`.
- Not built: `SplitForNewWindow` and its expiry.
- #24: multi-display hand-off.
- #20: tabs.
- Deferred (A9): findings rule 4, dropping docked windows that the user
  moved while the engine was not running. It needs remembered resting and
  parked frames, and `SavedDisplay` does not persist them.

## Placement

- **Project:** `src/CanvasWm.Engine` (`net10.0`).
  - There is no new project or package reference.
  - The Engine still references only Core.
- **Namespace:** `CanvasWm.Engine`.
- **New files:**
  - `src/CanvasWm.Engine/DisplayEngine.Events.cs`: a partial class with all
    new members and private helpers of this feature;
  - `src/CanvasWm.Engine/WindowEvents.cs`: an internal constants class.
- **Changed files**, only through the changes listed under "Changes to
  existing types":
  - `DisplayEngine.cs`;
  - `DisplayEngine.Commands.cs` (`Move` and `ArrangeAll` only);
  - `DisplayEngine.Writer.cs` (`LandingDone` and `SetLayout`);
  - `FrameWriterState.cs` (`WriterRecord` fields);
  - `EnginePolicy.cs` (one property added, one removed);
  - `tests/CanvasWm.Engine.Tests/EngineShell/ConstructionPolicyTests.cs`
    (only the migration of AC32).
- **Tests:** `tests/CanvasWm.Engine.Tests/Events/*Tests.cs`.
  - They use the existing fakes **unchanged**: `FakeWindowSystem`,
    `ManualClock`, `InMemoryLimitStore`, `InMemoryDisplayStore`,
    `RecordingRenderer` and `FakeCapture`.
  - One new test-only helper goes in that folder:
    `SequentialIds : IIdGenerator`. Its `Make(prefix)` returns `"n1"`,
    `"n2"`, and so on, and ignores the prefix.
- **Depends on these specs.** All Core specs are **unchanged**.
  - `specs/engine-inbound-api.md`:
    - the shell and the call wrapper;
    - the hook points in `Register`, `RemoveCore` and `AddCore`;
    - the away table and the pid → last-focused map;
    - `SetParked` and `SetPhase`.

    Its policy listing changes as described in C12 and in "Required edits to
    earlier specs".
  - `specs/engine-frame-writer.md`:
    - `Document`, `FramedNodeId`, `SetLayout`, `ComputeTargets`;
    - `PresentLayout`, `PresentDirectly`, `ReLand`, `PlaceWindows`,
      `LandingDone`, `Settle`;
    - `WriteTargets`, `IsExpected`;
    - `WriterRecord` (`WritesInFlight`, `Minimum`, `Maximum`);
    - `CanPresentDirectly`, `InteractionActive`.
  - `specs/engine-interfaces.md`:
    - `WindowInfo.Dockable`;
    - `IWindowSystem.FrontToBack`, `Focus` and `MouseDown`;
    - `ILimitStore.Learn`;
    - `IDisplayStore.Load` and `Save`;
    - `SavedDisplay`.
  - `specs/new-window-placement.md`: `Placement.PlaceNew`,
    `PlacementOutcome`, `PlacementKind`, `Placement.ArrangeDwindle`,
    `Placement.CascadeRect`.
  - `specs/docking.md`:
    - `LayoutEdits.RemovePanel(doc, id, shareRemovedSpace)`;
    - `LayoutEdits.FindPanelOfWindow`, `LayoutEdits.PanelsOf`,
      `LayoutEdits.LeafIds`, `LayoutEdits.InsertBeside`.
  - `specs/directional-focus.md`:
    `DirectionalFocus.Neighbour(entries, id, edge)`.
  - `specs/layout-tree.md`:
    - `LayoutSanitizer.Sanitize`, `LayoutTree.FindDesktop`;
    - `DesktopNode`, `PanelNode`, `SplitNode`, `LayoutDocument`;
    - `IIdGenerator`, `UidGenerator.Shared`, `IdPrefixes.Split`.
  - `specs/layout-solver.md`: `LayoutSolver.FocusLayout` and
    `LayoutSolver.LayoutRects`.
  - `specs/engine-projection-targets.md`:
    - `ScreenProjection.Stage`, `DockedFrame`, `OnStageFraction`,
      `OnStageThreshold`;
    - `ProjectionView.AtRest`, `TargetKind`;
    - property P2: "on stage and fits" ⇔ target `Kind == Tile`.
  - `specs/tuning-constants.md`:
    - `Tuning.SnapBackLimit` (3);
    - `Tuning.SnapBackWindowSeconds` (3);
    - `Tuning.LandingRevealQuietSeconds` (0.6).
- **Units:** DIPs, y-down. Times are absolute seconds on `IClock.Now`.
- **Naming rule:** no text in `src/` or `tests/` may contain the studied
  app's name or the word for its licence module. This also applies to code
  comments about the C12 removal.

### Changes to existing types (complete list)

- **C1. `EnginePolicy` (public): new flag.**
  - Change: a new property `bool FloatNewWindowsOnDesktop { get; init; } =
    false;` in the layout group.
  - It is not a relayout trigger, so `RequiresRelayout` is unchanged.
  - Hook: H1 (findings data model "Policy (new)").
- **C2. `DisplayEngine.Start`.**
  - Change: the body is replaced by B1–B8. `Start` now places windows and
    starts a landing pass.
  - Hook: inbound B10 "[#13: sanitise, place unsaved windows, ensure desktop
    node, …]". This replaces writer C3.
- **C3. `AddCore` (inbound B17d).**
  - Change: after registration on a started engine, the tail "save; render"
    becomes B10–B15.
  - Hook: inbound B17d "[#13: end interactions, place, write frames]".
- **C4. `RemoveCore` (inbound B19c–f).**
  - Change, when the engine is started:
    - the close-quiet time is set;
    - the window's view is removed from the document;
    - the tail "save; render" becomes a commit (B16–B20).
  - Unchanged: the away-table store, record removal, park write-back, capture
    stop and focus clean-up.
  - Hook: inbound B19c "[#13: end interactions] … [#13: remove from layout]".
- **C5. `WindowFocused` (inbound B20).**
  - Change: it adds the per-window last-focus time, the engine-activated
    marker and the reveal (B21–B24).
  - Hook: inbound B20 "[#13/#15: tab, raise, camera]".
- **C6. `WindowMoved` (inbound B21).**
  - Change: it adds echo filtering, the parked re-park, snap-back, insist
    learning and zoom/un-zoom (B25–B33).
  - Hook: inbound B21 "[#13: absorb own writes or treat as a user move]".
- **C7. `Move` and `ArrangeAll` (`DisplayEngine.Commands.cs`).**
  - Change: the stub bodies are replaced by B37–B40. The other 31 stubs are
    unchanged.
  - Hook: inbound B44 and the stub list "(#13)".
- **C8. `Save0` (inbound B16).**
  - Change: it saves the **live** record (B42).
  - Hook: inbound B16 "#13 makes it reflect the live layout".
- **C9. `LandingDone` (writer B24).**
  - Change: it calls `FocusAfterLanding()` (B35) right after
    `PlacementDone = now` and before `Phase = Fading`.
  - Hook: writer out-of-scope note "focus after a landing (#27)".
- **C10. `SetLayout` (writer internal).**
  - Change: it also recomputes `FramedLeaves` and clears the zoom memory
    (B34). It still makes no write, render or save.
  - Hook: writer "layout source (#27 replaces with live edits)".
- **C11. `WriterRecord` (internal).**
  - Change: two new fields, `double LastFocused` (initially −∞) and
    `List<double> SnapBackTimes` (initially empty). They are created and
    dropped with the record (writer B30).
  - `FrameWriterState` is **unchanged**.
  - Hook: findings data model "per managed window (adds to #23/#26)".
- **C12. `EnginePolicy` (public): property removed.**
  - Change: the property `int? MaxDockedTiles` is deleted from
    `src/CanvasWm.Engine/EnginePolicy.cs`, together with its XML doc line.
  - Decision: H3. A docked-tile cap must not exist in CanvasWM.
  - Result: with C1, the public property count stays **38**.
  - The code comment for the removal, if any, says only "no tile cap (human
    decision)".
  - Consequences: the edits to `specs/engine-inbound-api.md` listed below,
    and the test migration in AC32.

Nothing else changes:
- No Core type, no `Services/*`, no `Rendering/*`, no `ManagedWindow` and no
  fake.
- No public member of `DisplayEngine` is added or removed.
- `EnginePolicy` gains exactly one property and loses exactly one.
- Only these method bodies change: `Start`, `WindowAdded`, `WindowRemoved`,
  `WindowFocused`, `WindowMoved`, `Release` (through `RemoveCore`), `Resume`
  (through `AddCore` and `RemoveCore`), `Move`, `ArrangeAll` and `Save`.

### Required edits to earlier specs (from C12; to be applied by the owner)

`specs/engine-inbound-api.md` needs three edits:

1. **Policy listing (around line 118–119).** Delete these lines:
   ```
       /// <summary>Maximum number of tiled windows; null = no cap. Default null.</summary>
       public int? MaxDockedTiles { get; init; } = null;
   ```
   In their place, add the C1 property and this note:
   ```
       /// <summary>A new window that cannot go anywhere else floats onto the empty desktop area
       /// when that area is visible and large enough, instead of being tiled. Default false.
       /// Added by #27 (H1).</summary>
       public bool FloatNewWindowsOnDesktop { get; init; } = false;
       // MaxDockedTiles: removed per human decision (licensing-related tile cap of the studied
       // app; CanvasWM has no tile cap). See specs/engine-window-events.md C12.
   ```
2. **Property count (around line 211).** Replace "**38 instance
   properties**: the observed 39 fields minus the dropped `tabLights` (A6)"
   with:

   "**38 instance properties**: the observed 39 fields minus the dropped
   `tabLights` (A6) and minus `MaxDockedTiles` (removed per human decision,
   licensing-related; #27 C12), plus `FloatNewWindowsOnDesktop` (#27 C1)."
3. **AC3 (around line 769–774).** The count stays "exactly 38". The
   per-property assertions change:
   - drop the assertion for `MaxDockedTiles`;
   - add `FloatNewWindowsOnDesktop == false`;
   - add "no property named `MaxDockedTiles`" next to "none named
     `TabLights`".

These edits are allowed in that document because it is a spec, not `src/` or
`tests/`. They are not applied by this spec (see Report).

## Public API

### Public (one addition, one removal)

```csharp
namespace CanvasWm.Engine;

public sealed record EnginePolicy
{
    // --- layout
    /// <summary>A new window that cannot go anywhere else floats onto the empty desktop area
    /// when that area is visible and large enough, instead of being tiled. Default false
    /// (new windows always tile). Never applies at start. Not a relayout trigger.</summary>
    public bool FloatNewWindowsOnDesktop { get; init; } = false;

    // MaxDockedTiles is removed (C12). There is no tile cap.
}
```

`DisplayEngine.Move(Edge)` and `DisplayEngine.ArrangeAll()` keep their
signatures. Their XML docs become:
- `Move`: `/// <summary>Frame the neighbouring tile on that side of the framed node (or of the focused window's tile when nothing is framed).</summary>`
- `ArrangeAll`: `/// <summary>Rebuild the tiling as a spiral of all dockable windows in front-to-back order and frame nothing.</summary>`

### Internal (visible to `CanvasWm.Engine.Tests`)

```csharp
namespace CanvasWm.Engine;

/// <summary>Window-event constants that are algorithm literals, not tuning values.</summary>
internal static class WindowEvents
{
    /// <summary>Reveal suppression after a reveal, seconds.</summary>
    public const double RevealQuietSeconds = 1.0;
    /// <summary>Reveal suppression after a window is removed, seconds.</summary>
    public const double CloseQuietSeconds = 1.0;
    /// <summary>Minimum spacing between two zoom/un-zoom toggles from app resizes (strict &gt;), seconds.</summary>
    public const double ZoomToggleSpacingSeconds = 1.0;
    /// <summary>A reported frame counts as resized when width or height changed by more than this.</summary>
    public const double ResizedThreshold = 0.5;
    /// <summary>"Grew": wider than the tile by more than this...</summary>
    public const double GrowWidthMargin = 100;
    /// <summary>...or taller than the tile by more than this.</summary>
    public const double GrowHeightMargin = 60;
    /// <summary>"Grew" also needs the other axis to be at least tile − this.</summary>
    public const double GrowKeepSlack = 2;
    /// <summary>"Shrank": narrower or shorter than the tile by more than this.</summary>
    public const double ShrinkMargin = 100;
    /// <summary>"Fills": width at least this fraction of the work area...</summary>
    public const double FillsWidthFraction = 0.92;
    /// <summary>...and height at least this fraction.</summary>
    public const double FillsHeightFraction = 0.90;
    /// <summary>A filling frame un-zooms only if it differs from the tile by more than this on some component.</summary>
    public const double UnzoomDrift = 4;
    /// <summary>Insist learning: an axis is "larger" when frame &gt; tile + this.</summary>
    public const double InsistSizeSlack = 2;
    /// <summary>Insist learning: a larger axis qualifies only below this fraction of the work area.</summary>
    public const double InsistScreenFactor = 0.8;
    /// <summary>Desktop slot (H1) usable only if its docked frame is at least this wide...</summary>
    public const double DesktopSlotMinWidth = 480;
    /// <summary>...and this tall.</summary>
    public const double DesktopSlotMinHeight = 320;
}

public sealed partial class DisplayEngine
{
    // ----- ids and stage -----
    /// <summary>Id generator for layout edits. Default UidGenerator.Shared. Tests inject a
    /// deterministic one. Setting it has no side effect.</summary>
    /// <exception cref="ArgumentNullException">On set with null.</exception>
    /// <exception cref="InvalidOperationException">Set from inside one of this engine's callbacks.</exception>
    internal IIdGenerator Ids { get; set; }
    /// <summary>ScreenProjection.Stage(VisibleArea, Policy.Gap).</summary>
    internal Rect Stage { get; }
    /// <summary>Stage.Width / Stage.Height (plain IEEE division; may be NaN or ∞).</summary>
    internal double StageAspect { get; }

    // ----- navigator -----
    /// <summary>Leaf ids of the framed node as of the last navigator sync; empty when nothing is framed.</summary>
    internal IReadOnlyList<string> FramedLeaves { get; }
    /// <summary>Previous framed ids, oldest first, appended by recorded framing changes (B34).
    /// Cleared by Start. A simple list until #15.</summary>
    internal IReadOnlyList<string?> FramingHistory { get; }
    /// <summary>True between an app-resize zoom (B31) and the next framing change.</summary>
    internal bool ZoomedAlone { get; }
    /// <summary>Framed id to return to on un-zoom; meaningful only while ZoomedAlone.</summary>
    internal string? ZoomReturnNodeId { get; }

    /// <summary>Change the framed node (null = frame nothing). Updates FramedLeaves. When
    /// record is true and the id changes, the previous FramedNodeId is appended to
    /// FramingHistory. Clears the zoom memory. No write, no render, no save.</summary>
    internal void FrameNode(string? nodeId, bool record);

    // ----- focus and reveal state -----
    /// <summary>Window the next landing should focus (B35); cleared by FocusAfterLanding.
    /// Settable for tests.</summary>
    internal WindowKey? LandingFocus { get; set; }
    /// <summary>Key the engine last activated and whose focus echo has not arrived yet (B21).</summary>
    internal WindowKey? EngineActivatedKey { get; }
    /// <summary>Key whose reveal was requested while not at rest (stored only; #15 uses it).</summary>
    internal WindowKey? PendingReveal { get; }
    /// <summary>Reveals are suppressed while Clock.Now &lt; this. Initial −∞.</summary>
    internal double SuppressRevealUntil { get; }
    /// <summary>Reveals are suppressed while Clock.Now &lt; this. Initial −∞.</summary>
    internal double CloseQuietUntil { get; }
    /// <summary>Time of the last zoom/un-zoom toggle from an app resize. Initial −∞.</summary>
    internal double LastZoomToggle { get; }
    /// <summary>Last focus time of a managed window (−∞ if never focused), or null if not managed.</summary>
    internal double? LastFocusedTime(WindowKey key);
    /// <summary>Snap-back times of a managed window, oldest first (copy); empty if not managed.</summary>
    internal IReadOnlyList<double> SnapBackTimes(WindowKey key);

    /// <summary>B36: lastFocused = now, FocusedKey = key, EngineActivatedKey = key, then
    /// Services.Windows.Focus(key). Unmanaged key: nothing.</summary>
    internal void Activate(WindowKey key);
    /// <summary>B35. Called by LandingDone; callable directly by tests.</summary>
    internal void FocusAfterLanding();
    /// <summary>B14: the H1 desktop-slot test for the current document and framing.</summary>
    internal bool DesktopSlotUsable();
}
```

Rules for all internal entry points:
- They run through the call wrapper (writer B33).
- A null non-nullable argument throws `ArgumentNullException`.
- A call from inside one of this engine's callbacks throws
  `InvalidOperationException`.

## Behaviour

### Notation and shared definitions

- `now` = `Services.Clock.Now`.
- `s(k)` = the writer record of managed key `k`.
- **Known frame** = `Window(k).Info.Frame`.
- **Window keys of a document**, in this order:
  1. the views of tree panels (pre-order);
  2. then the views of floating panels (list order);
  3. then the views of hidden panels (list order).
- **Tree panel of k** = the panel in `LayoutEdits.PanelsOf(Document.Root)`
  whose `Views` contain `k.Value`.
- **Has a panel** = `FindPanelOfWindow(Document, k.Value) != null`.
- **Targets** = `ComputeTargets()` (writer B3).
- **Placement:**
  - a window **has a placement** ⇔ it has a target;
  - its placement **is on stage and fits** ⇔ the target has `Kind == Tile`
    (projection P2).
- **Stage** = `ScreenProjection.Stage(VisibleArea, Policy.Gap)`.
  - **StageAspect** = `Stage.Width / Stage.Height`.
  - **Landscape** = `Stage.Width ≥ Stage.Height`.
- **Root id** = `Document.Root?.Id`.
- **Placement framed id** = `FramedNodeId` if non-null, else the root id.
- **At rest** = `CanPresentDirectly` (writer B1).
- **Float (M22)** = no document change. The window has no panel.
- **Commit(doc′, focus)** (findings "commit an edit", rule 44). The caller
  only commits when `doc′` differs from `Document` by record value equality.
  The steps are:
  1. Set `Document = doc′`, then run the navigator sync (B34).
  2. If `CanPresentDirectly` → `PresentDirectly(focus)` (writer B4). That
     makes one batch of changed targets, focuses on the next clock turn,
     saves exactly once, renders once and requests `OnChange`.
  3. Otherwise, in this order:
     1. save once;
     2. render once;
     3. request `OnChange`;
     4. set `LandingFocus = focus` when `focus` is non-null;
     5. call `PresentLayout(focus)`. This re-lands, or only renders in
        Overview.

### A. Start (findings 1–8)

- **B1.** `Start(windows)` validates as before.
  - Already started → nothing (inbound B10).
  - Otherwise every window is registered (inbound B17b).
- **B2 (load).** `loaded = Services.Displays.Load(Key)`. Let `cur` be the set
  of managed key values.
  - The saved layout is **ignored entirely** when either holds:
    - `loaded` is null;
    - no window key of `loaded.Document` is in `cur`. This includes a saved
      document with no window keys at all.
  - "Ignored entirely" means:
    - `doc = new LayoutDocument(new DesktopNode())`, with root id
      `"desktop"`;
    - `framed = null`;
    - the base record (B42) is `new SavedDisplay(LayoutDocument.Empty)`. So
      saved views, owners, dock spots and float sizes are dropped (A3).
- **B3 (sanitise).** Otherwise:
  - `doc = LayoutSanitizer.Sanitize(loaded.Document, v => cur.Contains(v))`;
  - the base record is `loaded`;
  - `framed = loaded.FramedNodeId` when that id has an entry in
    `LayoutSolver.FocusLayout(doc.Root)`, else null (A4).
- **B4 (ensure desktop at start).** This applies when
  `LayoutTree.FindDesktop(doc.Root)` is null and `doc.HiddenDesktop` is null.
  - Root null → `doc = doc with { Root = new DesktopNode() }`.
  - Otherwise:
    ```
    doc = doc with { Root = LayoutEdits.InsertBeside(doc.Root, doc.Root.Id,
        new DesktopNode(), Edge.Right, Ids.Make(IdPrefixes.Split),
        LayoutEdits.DefaultFraction) }
    ```
    The desktop goes on the trailing side of the old root, with fraction 0.5
    (A5).

  This is the only place where the engine inserts a desktop (A6).
- **B5 (adopt).** Go through the managed windows sorted by `Info.Key.Value`
  with `StringComparer.Ordinal`. For each window with **no panel** in `doc`:
  - **Dockable** →
    `o = Placement.PlaceNew(doc, key, framed ?? doc.Root.Id, focusedKey: null, StageAspect, Ids, Policy.ShareRemovedSpace)`.
    - Docked → `doc = o.Document`.
    - NeedsFloat → float (M22).
  - **Not dockable** → "float behind", which is a float (M22).

  More rules for this step:
  - The framed id is re-evaluated for each window, because the root id
    changes as windows are placed.
  - Windows that already have a panel anywhere (tree, floating or hidden)
    keep it unchanged. Hidden ones stay hidden (A7).
  - The H1 rule is not applied.
  - Findings rule 4 is deferred (A9).
- **B6.** Set `Document = doc` and `FramedNodeId = framed`, then run the
  navigator sync (B34). `FramingHistory` is cleared and `ZoomedAlone = false`.
- **B7 (initial present).** In this order:
  1. `IsStarted = true`;
  2. render once;
  3. save (B42);
  4. `g = ++PlacementGeneration`;
  5. `PlaceWindows(g)` (writer B23), synchronously inside `Start`. There is
     no re-land delay and no wait for pictures.

  When the pass completes: `LandingDone` → `FocusAfterLanding` (B35) → settle
  and save (writer B24–B25). If the pass issues no write, all of this except
  the delayed settle happens inside `Start`.

  Right after `Start` no window has ever been focused. So by H4 the first
  landing focuses nothing, unless a `WindowFocused` arrived in between.
- **B8.** `Start` while suspended: B1–B6 run. In B7, steps 1–4 run, but
  `PlaceWindows` makes no write (writer E7).

### B. WindowAdded (findings 9–14)

- **B9.** These parts are unchanged (inbound B17a–c): the suspended guard,
  registration, refresh, away-table removal, and "not started → record only".
  Ending interactions is a no-op, because none exist yet.
- **B10 (already has a panel).** `FindPanelOfWindow(Document, key)` returns:
  - a **tree or floating panel** → no document change. Save once and render
    once (the #23 path).
  - a panel in **`Document.Hidden`** (only possible from a loaded document)
    → (M19) remove the key from its hidden panel.
    - If it was the only view, the hidden panel is dropped with
      `LayoutEdits.RemovePanel`.
    - Otherwise the panel keeps its other views (B19 view rule).
    - Then continue with B11 on that document, as for a window with no
      panel.
- **B11 (no panel, decision order).** The first rule that applies decides:
  1. A remembered spot (record `RememberedSpot`) is **ignored** (M19).
  2. A pending split-for-new-window request is not built, so it is skipped.
  3. `Policy.FloatNewWindowsOnDesktop` and `DesktopSlotUsable()` → float
     (M22).
  4. `Info.Dockable` →
     `o = PlaceNew(doc, key, placement framed id, FocusedKey?.Value, StageAspect, Ids, Policy.ShareRemovedSpace)`.
     - Docked → `doc′ = o.Document`.
     - NeedsFloat → float (M22).
     - Root null (no framed id is possible) → treated as NeedsFloat (E4).
  5. Otherwise → float (M22).

  No cap exists (H3, C12).
- **B12 (commit).**
  - `doc′ != Document` → `Commit(doc′, focus: key)`.
  - Otherwise → save once and render once.

  The framing is not changed for the new window (A10).
- **B13.** `WindowBecameDockable` still does not place (inbound B23).
- **B14 (desktop slot, H1).** `DesktopSlotUsable()` is true iff all of these
  hold:
  - `d = LayoutTree.FindDesktop(Document.Root)` is non-null, with
    `d.Child == null`;
  - `d.Id` has an entry `e` in `LayoutSolver.FocusLayout(Document.Root)`;
  - with `view = ProjectionView.AtRest(VisibleArea, Policy.Gap, framed rect)`
    (framed rect as in writer B3) and
    `f = ScreenProjection.DockedFrame(view, e.Rect)`:
    - `ScreenProjection.OnStageFraction(f, view.Stage) ≥ ScreenProjection.OnStageThreshold`;
    - `f.Width ≥ DesktopSlotMinWidth`;
    - `f.Height ≥ DesktopSlotMinHeight`.

  It is evaluated only when the flag is on. `ComputeTargets` keeps
  `DesktopSlotUsable = false`, because there are no floating panels (M22).
- **B15.** `Resume` calls `AddCore` for every given window. A window that
  appeared while suspended is therefore placed here with its own commit,
  before `Resume`'s re-frame.

### C. WindowRemoved (findings 15–20)

- **B16.** The suspended guard and the unknown-key no-op are unchanged
  (inbound B19a–b). Not started → the #23 behaviour only.
- **B17 (started).** In this order:
  1. `CloseQuietUntil = now + CloseQuietSeconds`. This applies to closes and
     to temporary removals, including windows with no panel.
  2. `doc′ = RemoveView(Document, key)` (B19).
  3. The #23 steps, unchanged:
     - `Away[key] = RememberedSpot` when temporary (M19);
     - the record and its writer state are removed;
     - a parked window is written back centred;
     - capture is stopped;
     - `FocusedKey` is cleared if it was `key`;
     - the pid map is cleaned.
  4. If `doc′ != Document` → `Commit(doc′, focus: null)`. Otherwise save once
     and render once.
- **B18 (temporary, M19).** A temporary removal edits the document exactly
  like a close. This feature never writes `Document.Hidden`.
- **B19 (RemoveView).** Let `p = FindPanelOfWindow(doc, key.Value)`.
  - `p` is null → `doc` is unchanged.
  - `p.Views.Length == 1` →
    `LayoutEdits.RemovePanel(doc, p.Id, Policy.ShareRemovedSpace)`. This
    removes the panel from the tree (sharing or adjacent variant), or from the
    floating or hidden list.
  - Several views → `p` is replaced, wherever it is (tree, floating entry or
    hidden entry), by `new PanelNode(p.Id, views without key, selected′,
    p.Tabbed)`.
    - `selected′` is `p.Selected` if it is still a view.
    - Otherwise it is the view now at the removed view's index, clamped to
      the last index.
    - The tree shape and weights are unchanged.
- **B20.** `Release(key)` goes through `RemoveCore(key, false)`, so it also
  removes the window from the layout and commits. While suspended, the commit
  saves and renders but writes nothing (`PresentLayout` is inert, writer E7).

### D. WindowFocused and reveal (findings 21–22, 41)

- **B21.** The suspended guard and the unknown-key guard are unchanged. For a
  managed key, in this order:
  1. `fromEngine = (key == EngineActivatedKey)`, then
     `EngineActivatedKey = null` (A11).
  2. `s(key).LastFocused = now`; `FocusedKey = key`; the pid map is updated
     (unchanged).
  3. If started → run the reveal test (B22).
  4. Render once. This is the #23 render. A reveal's re-land adds its own
     render (writer B22).

  The framed node does not change, except through a reveal. There is no
  save.
- **B22 (reveal test).** A reveal happens iff **all** of these hold:
  - `!fromEngine`;
  - `now ≥ SuppressRevealUntil` and `now ≥ CloseQuietUntil`;
  - the window has a placement whose `Kind != Tile` (Park or
    ParkWithPicture).

  If these hold but the engine is not at rest → `PendingReveal = key` and
  nothing else happens.
- **B23 (reveal = zoom into the window).** At rest, in this order:
  1. `SuppressRevealUntil = now + RevealQuietSeconds`.
  2. The node is the window's tree panel id. If its panel is floating, the
     node is `DesktopNode.DefaultId` (unreachable under M22).
  3. `LandingFocus = key`.
  4. `FrameNode(node, record: true)`.
  5. `ReLand()`.
  6. Log once (the text contains the key).
- **B24.** Only B23 and B35 change `SuppressRevealUntil`.

### E. WindowMoved (findings 24–31)

- **B25.** The suspended guard and the unknown-key guard are unchanged.
  - Let `prev` be the known frame **before** this event.
  - The known frame becomes `frame` (inbound B21).
  - Not started → stop.
- **B26 (ignored for layout).** Stop when any of these holds:
  - `s(key).WritesInFlight > 0`;
  - `Services.Windows.MouseDown`;
  - `IsExpected(key, frame)` (writer B27).

  Nothing else changes: no snap-back time is added, no write is made and
  nothing is rendered.
- **B27 (parked).** If `Window(key).Parked`:
  1. `SetParked(key, false)`;
  2. `WriteTargets(ComputeTargets())`;
  3. render once.

  The window is re-parked, or shown if it now belongs on stage. There is no
  save and no snap-back time.
- **B28 (not tiled).** If the window has no **tree** panel → stop. This
  covers floating windows (#22) and windows without a panel (M22).
- **B29 (flags).** Let `tile` = the window's target rect when its target has
  `Kind == Tile`; otherwise `tile = frame`. Let `VA = VisibleArea`.
  - `resized` = `|frame.W − prev.W| > ResizedThreshold` or
    `|frame.H − prev.H| > ResizedThreshold`.
  - `grew` = either of:
    - `frame.W > tile.W + GrowWidthMargin` and
      `frame.H ≥ tile.H − GrowKeepSlack`;
    - `frame.H > tile.H + GrowHeightMargin` and
      `frame.W ≥ tile.W − GrowKeepSlack`.
  - `shrank` = `frame.W < tile.W − ShrinkMargin` or
    `frame.H < tile.H − ShrinkMargin`.
  - `fills` = `frame.W ≥ FillsWidthFraction × VA.W` and
    `frame.H ≥ FillsHeightFraction × VA.H`.
  - `drift` = `|frame.c − tile.c| > UnzoomDrift` for some
    `c ∈ {X, Y, W, H}`.
  - `alone` = `FramedNodeId ==` the id of the window's tree panel.
- **B30 (zoom decision).** This is evaluated only when `resized` and
  `now − LastZoomToggle > ZoomToggleSpacingSeconds` (strict):
  - `alone && shrank` → **un-zoom** (B32);
  - else `alone && fills && drift` → **un-zoom**;
  - else `!alone && grew` → **zoom** (B31).

  Every other case → **snap-back** (B33).
- **B31 (zoom).** This is the at-rest minimum of "toggle maximize". In this
  order:
  1. `LastZoomToggle = now`;
  2. `r = FramedNodeId`;
  3. `FrameNode(panel id, record: true)`;
  4. `ZoomedAlone = true` and `ZoomReturnNodeId = r`;
  5. `ReLand()`;
  6. Log once.

  There is no snap-back and no save. The landing's settle saves.
- **B32 (un-zoom).** In this order:
  1. `LastZoomToggle = now`;
  2. `back = ZoomedAlone ? ZoomReturnNodeId : null`;
  3. `FrameNode(back, record: true)`, which also clears the zoom memory;
  4. `ReLand()`;
  5. Log once.
- **B33 (snap-back with insist detection).** Let `L = s(key).SnapBackTimes`.
  1. Remove every `t` with `now − t ≥ Tuning.SnapBackWindowSeconds`. Then
     append `now`.
  2. If `L.Count < Tuning.SnapBackLimit` → `WriteTargets(ComputeTargets())`
     and render once.
  3. Otherwise:
     1. Clear `L`.
     2. **Accept** the app's size by comparing `frame` with `tile`:
        - If `frame.W ≤ tile.W + InsistSizeSlack` and
          `frame.H ≤ tile.H + InsistSizeSlack` →
          `s.Maximum = (frame.W, frame.H)`, replacing both axes. Nothing is
          written to the limit store.
        - Otherwise, an axis **qualifies** iff
          `frame.axis > tile.axis + InsistSizeSlack` and
          `frame.axis < InsistScreenFactor × VA.axis` (width with width,
          height with height). Let `old` be the old own minimum, or 0 per axis
          when there is none.
          - If any axis qualifies:
            `s.Minimum = (qw ? max(oldW, frame.W) : oldW, qh ? max(oldH, frame.H) : oldH)`,
            then `Services.Limits.Learn(Info.App, s.Minimum)`.
          - If no axis qualifies, nothing is learned.
        - Log once (the text contains the key), whatever the outcome.
     3. `WriteTargets(ComputeTargets())` and render once.

  Snap-back never saves and never changes the generation.

### F. Navigator, focus after landing, activate (findings 37–43)

- **B34 (navigator).**
  - **FrameNode(id, record)**, in this order:
    1. If `record` and `id != FramedNodeId` → append the old `FramedNodeId`
       (possibly null) to `FramingHistory`.
    2. Set `FramedNodeId = id`.
    3. Set `FramedLeaves` = the leaf ids of the entry's node when `id` has an
       entry in `FocusLayout(Document.Root)`, otherwise empty.
    4. Clear the zoom memory (`ZoomedAlone = false`,
       `ZoomReturnNodeId = null`).

    B31 sets the zoom memory **after** calling `FrameNode`.
  - **Sync** runs after every `Document` change made by this feature
    (findings 43):
    - `FramedNodeId` null → `FramedLeaves` is empty.
    - The id still has an entry in `FocusLayout(Document.Root)` →
      `FramedLeaves` = that entry's leaf ids.
    - Otherwise, let `S` = the old `FramedLeaves` that are still leaf ids of
      the tree.
      - `S` empty → frame nothing.
      - Otherwise frame the non-virtual `LayoutRects` entry whose leaf ids
        contain all of `S` and that has the fewest leaf ids. Ties go to the
        later entry in table order (the deeper one). Then recompute
        `FramedLeaves`.
    - A sync change is **not** recorded in history. It clears the zoom memory
      when the id changes.
  - **SetLayout** (C10):
    - sets `Document` and `FramedNodeId` as before;
    - sets `FramedLeaves` = the leaf ids of that id's entry, or empty;
    - clears the zoom memory;
    - does not touch the history.
- **B35 (FocusAfterLanding, findings 40 with H4).** In this order:
  1. Candidates = the managed windows, in registration order, whose target
     has `Kind == Tile`.
  2. The chosen window is the first match of:
     1. `LandingFocus`, if it is a candidate;
     2. `FocusedKey`, if it is a candidate;
     3. among candidates whose `LastFocused` is finite (focused at least once
        while managed), the one with the greatest `LastFocused`. Ties go to
        the earliest in registration order.
     4. Otherwise **none** (H4). A candidate that was never focused is never
        chosen by this step.
  3. If a window is chosen → `Activate(chosen)`. If none is chosen, no
     `Focus` call is made, and `FocusedKey` and `EngineActivatedKey` are
     unchanged.
  4. Always: `SuppressRevealUntil = now + Tuning.LandingRevealQuietSeconds`,
     then `LandingFocus = null`.
- **B36 (Activate, findings 41).** For a managed key, in this order:
  1. `s.LastFocused = now`;
  2. `FocusedKey = key`;
  3. `EngineActivatedKey = key`;
  4. `Services.Windows.Focus(key)`, synchronously.

  The pid map is not touched. The next `WindowFocused` of that key is not a
  reveal trigger (B21–B22). `PresentDirectly`'s next-turn focus of a new
  window (writer B4) does **not** go through `Activate`. That is unchanged
  #26 code (A13).

### G. Move(toward) and ArrangeAll (findings 34–39; H2, H3)

- **B37 (Move guard).** Not started, or suspended → nothing. The re-entry
  guard applies.
- **B38 (current node).**
  - `current = FramedNodeId` when it is non-null and has an entry in
    `FocusLayout(Document.Root)`.
  - Otherwise (H2), `current` = the tree panel id of `FocusedKey`. If there
    is no focused key, or the focused window has no tree panel → nothing.
- **B39 (neighbour).**
  `n = DirectionalFocus.Neighbour(FocusLayout(Document.Root), current, toward)`.
  - `n` is null → nothing: no history, no write, no render, no clock action.
  - Otherwise `FrameNode(n, record: true)`, then `ReLand()`. `LandingFocus`
    is not set. The tree is never edited.

  At rest, the result is:
  - the neighbour's unit rect becomes the camera (stretched to a square);
  - tiles outside it park;
  - focus follows B35, so a neighbour that was never focused gets no `Focus`
    call (H4);
  - the settle saves.
- **B40 (ArrangeAll).** Not started, or suspended → nothing. Otherwise, in
  this order:
  1. **Build the order.**
     - First, for each `k` in `Services.Windows.FrontToBack()`: keep
       `k.Value` iff all of these hold:
       - `k` is managed and `Info.Dockable`;
       - `k` has a panel in `Document`, and that panel is not in
         `Document.Hidden`;
       - `k` is not already listed.
     - Then, for each window key `v` of the document (document order) not
       already listed: keep it iff it is **not** a managed key, or it is a
       managed key whose `Info.Dockable` is true.
  2. `doc′ = Placement.ArrangeDwindle(Document, order, landscape, Ids)`.
     Docked panels not in the order become cascaded floats (Core). There is
     no cap (H3).
  3. `FrameNode(null, record: Phase != Overview)`.
  4. `Commit(doc′, focus: null)`. This always commits, even when `doc′`
     equals `Document`, so `ArrangeAll` always saves once.
- **B41.** All other command stubs keep doing nothing (inbound B44). These
  include `ToggleMaximize`, `FloatAll`, `ToggleDocked`, `SplitForNewWindow`,
  `NativePointerDragged` and `NativeMouseUp`.

### H. Saving (findings 45–46)

- **B42 (live record, C8).** Save is a no-op before the first `Start` (A11
  of #23). Otherwise it calls:
  ```
  Services.Displays.Save(Key, new SavedDisplay(Document, FramedNodeId,
      base.Framings, base.Owners, base.DockSpots, base.FloatSizes))
  ```
  `base` is the record chosen in B2 or B3.
  - Saved views, owners, dock spots and float sizes are carried over
    unchanged (A3).
  - No key is filtered out of the saved copy (A14).
- **B43 (who saves).** Every save is synchronous; none uses a timer or
  debounce.

  **Saved:**
  - `Start`, before its landing (B7.3);
  - every landing settle (writer B25). This covers `Start`'s first landing,
    reveals, zoom/un-zoom, `Move`, refusal re-lands and the re-frame re-land;
  - every `WindowAdded` and `WindowRemoved` on a started engine (through a
    commit or the #23 path);
  - `ArrangeAll`;
  - `PresentDirectly` (a re-frame at rest), `Suspend` and `Save()`
    (unchanged).

  **Not saved:**
  - `WindowFocused`, including a reveal, until its settle;
  - `WindowTitleChanged`, `WindowBecameDockable`, `CaptureArrived`,
    `AppActivated`;
  - snap-back and insist learning, the parked re-park, echoes and ignored
    moves;
  - `Move` itself, until its settle.

### I. Other events

- **B44.** `WindowTitleChanged`, `WindowBecameDockable`, `CaptureArrived` and
  `AppActivated` are unchanged (findings 33).

## Edge cases & errors

- **E1. Argument errors.**
  - A null `key` or `info` → `ArgumentNullException` before any change
    (unchanged).
  - `Ids = null` → `ArgumentNullException`.
  - `Move` with an undefined `Edge` value, on a started engine whose current
    node exists → `ArgumentOutOfRangeException` from Core, with no state
    change. The check runs before `FrameNode`.
- **E2.** `Move`, `ArrangeAll`, `Activate`, `FocusAfterLanding` and
  `FrameNode` called from inside a callback → `InvalidOperationException`.
- **E3.** B2–B4 cover these saved documents:
  - all window keys gone;
  - a stale framed id;
  - no desktop node.

  A saved document with `HiddenDesktop` set and no desktop node gets no
  inserted desktop.
- **E4.** `Document.Root == null` after start can only come from
  `SetLayout`. Then:
  - `WindowAdded` floats (M22);
  - `Move` finds no current node;
  - `ArrangeAll` builds from the order (Core).
- **E5.** A `WindowMoved` for a non-selected tab view (it has no target):
  `tile = frame`, so there is no zoom. The snap-back rewrite issues nothing
  for that window.
- **E6.** `StageAspect` is NaN or ∞ when the work area is degenerate. Core's
  edge rule handles it (NaN → Bottom).
- **E7.** A reveal or `Move` while the phase is `Moving` or `Landing` →
  `ReLand` passes at once (writer B22). In `Overview` the framing is stored
  and no pass runs.
- **E8.** The un-zoom's `ZoomReturnNodeId` may name a node that no longer
  exists. `FrameNode` stores it with empty leaves, and `ComputeTargets`
  treats it as nothing framed (writer B3).

## Acceptance criteria

All criteria are **[mac]**. They are xUnit tests in
`tests/CanvasWm.Engine.Tests/Events/` that use the existing fakes and need no
OS access.

### Rig

- **Services:**
  - `clock = new ManualClock(100)`;
  - `fake = new FakeWindowSystem()`, with one screen `(0,0,1440,900)`;
  - `InMemoryLimitStore`, `InMemoryDisplayStore`, `RecordingRenderer`,
    `FakeCapture`.
- **Engine:**
  - key `K = new EngineKey(new DisplayKey("D1"), "default")`;
  - `VA = (0,25,1440,875)`;
  - `EnginePolicy.Default` (gap 10, `CoverFade` 0.14);
  - then `DisplayFrame = (0,0,1440,900)` and `Ids = new SequentialIds()`.
- **Windows:**
  - `W(k, dockable = true, frame = F0)` =
    `new WindowInfo(new WindowKey(k), pid, "X", new AppId("com.x"), "t", frame, dockable)`,
    with `F0 = (100,100,400,300)` and a distinct pid per key;
  - each window is added to the fake with the same frame before the engine
    sees it;
  - `n` is `W("n", dockable: false, (200,200,400,300))`.
- **"Land"** = `fake.Pump(); clock.Advance(0.2); fake.Pump();`. 0.2 is more
  than the settle delay of 0.16.
- **Comparisons:** rects compare exactly. Weights and times compare within
  `1e-9`.
- **Notation:**
  - `D` is `new DesktopNode()` (id `"desktop"`);
  - `P(k)` is `PanelNode.ForWindow(k)`;
  - `X(id)[…]` and `Y(id)[…]` are `SplitNode`s with that axis; each child's
    weight is written after it.
- **Counts** such as "+1 save" are deltas of `SaveLog`, `Plans`, `Writes`
  (whole batches) and `FocusRequests` over the step described.
- **States:**
  - **S1** = AC1 completed. Now is 100.2 and nothing is focused.
  - **S2** = S1, then advance the clock to 100.7, then `WindowFocused(a)`.
    a is a Tile, so there is no reveal. In S2, `FocusedKey == a`,
    `LastFocusedTime(a) == 100.7` and `EngineActivatedKey` is null.

### Start

- **AC1** [mac] Fresh start (worked ex. 1, outcome changed by H4).
  `Start([a, b, n])` at 100, with a and b dockable and no saved record.
  - **During the call:**
    - `Document == new LayoutDocument(X(n1)[D .5, Y(n2)[P(a) .5, P(b) .5] .5])`;
    - `FramedNodeId` is null and `FramingHistory` is empty;
    - exactly one `SetFrames` batch
      `[(a,(725,35,705,423)), (b,(725,468,705,422))]`. There is never an
      entry for n;
    - +1 save, whose entry has that `Document` and `FramedNodeId` null;
    - +1 render;
    - `Phase == Landing` and `PlacementGeneration == 1`;
    - `FocusRequests` is empty; `OnChange` fires once.
  - **`fake.Pump()` at 100:**
    - `PlacementDone == 100` and `Phase == Fading`;
    - `FocusRequests` is **still empty**, because neither candidate was ever
      focused (H4);
    - `FocusedKey` and `EngineActivatedKey` are null;
    - `LastFocusedTime(a) == LastFocusedTime(b) == −∞`;
    - `SuppressRevealUntil == 100.6` and `LandingFocus` is null;
    - +1 render; `OnChange` fires once (phase change).
  - **`clock.Advance(0.2)`:**
    - `Phase == Desk`;
    - `Reads` gains `[a, b]`;
    - +1 save (2 in total) and +1 render;
    - `FocusRequests` is still empty.
  - `Window(n)` stays managed with frame `(200,200,400,300)` and has no
    target.
  - Variant: `WindowFocused(b)` before `fake.Pump()`. At the landing, b is
    `FocusedKey` and a candidate, so `FocusRequests == [b]` and
    `EngineActivatedKey == b`.

  (B1–B7, B35, B36, B42, H4)
- **AC2** [mac] Ignored saved records (worked ex. 2). Each of these pre-saved
  records under K, followed by `Start([a, b])`, gives exactly AC1's
  `Document`, batch and ids, with `FramedNodeId` null:
  - `SavedDisplay(new LayoutDocument(X("s")[P(x) .5, P(y) .5]), "s", framings {"1": ["s"]})`;
  - `SavedDisplay(new LayoutDocument(D))`.

  In both cases the save made during `Start` has empty `Framings` and
  `Owners`, and null `DockSpots` and `FloatSizes`.

  (B2, B42, A3)
- **AC3** [mac] Partly valid record (worked ex. 3). Pre-save
  `SavedDisplay(new LayoutDocument(X("s")[P(x) .5, P(a) .5]), null, framings {"1": ["a"]}, owners {a: "o"})`,
  then call `Start([a, c])` (both dockable):
  - `Document == X(n1)[Y(n2)[P(a) .5, P(c) .5] .5, D .5]`;
  - the batch is `[(a,(10,35,705,423)), (c,(10,468,705,422))]`;
  - the save made during `Start` carries `Framings {"1": ["a"]}` and
    `Owners {a: "o"}` unchanged.

  Variants:
  - saved `FramedNodeId "a"` → after `Start`, `FramedNodeId == "a"` and
    `FramedLeaves == ["a"]`;
  - saved `FramedNodeId "zzz"` → `FramedNodeId` is null;
  - saved document `X("s")[P(x) .5, P(a) .5]` with a non-null
    `HiddenDesktop` → no desktop is inserted. After `Start`,
    `LayoutTree.FindDesktop(Document.Root)` is null and
    `Document.HiddenDesktop` equals the saved one. The tree shape is not
    asserted.

  (B3, B4, B5, A4, A5)
- **AC4** [mac] Sort order and kept panels.
  - `Start([c, b, a])` (all dockable, no record) →
    `Document == X(n1)[D .5, Y(n2)[P(a) .5, X(n3)[P(b) .5, P(c) .5] .5] .5]`.
    That is the order a, b, c, whatever the list order. The batch entries
    are in registration order (c, b, a):
    `[(c,(1083,468,347,422)), (b,(725,468,348,422)), (a,(725,35,705,423))]`.
  - Pre-save `X("s")[P(b) .5, D .5]`, then call `Start([a, b])`. b keeps its
    panel, and a is placed next to the framed root with anchor b:
    `Document == X("s")[Y(n1)[P(b) .5, P(a) .5] .5, D .5]`.
  - A second `Start` → no service call and no callback.

  (B5)

### WindowAdded

- **AC5** [mac] New window tiles beside the focused window (worked ex. 4).
  From S1:
  - `WindowFocused(b)` at 100.2 → no reveal (100.2 < 100.6), and
    `FocusedKey == b`.
  - `WindowAdded(W(c))`:
    - `Document == X(n1)[D .5, Y(n2)[P(a) .5, X(n3)[P(b) .5, P(c) .5] .5] .5]`;
    - one batch `[(b,(725,468,348,422)), (c,(1083,468,347,422))]`; a is not
      written;
    - +1 save (with that document), +1 render, and `OnChange` fires once;
    - `FocusRequests` is unchanged during the call;
    - one clock action is due at 100.2.
  - `clock.Advance(0)` → `FocusRequests == [c]`.

  (B11.4, B12, Commit)
- **AC6** [mac] H1 flag (worked ex. 5). From S1, set
  `Policy = Policy with { FloatNewWindowsOnDesktop = true }`. Then:
  - `DesktopSlotUsable()` is true;
  - `WindowAdded(W(c))` → `Document` unchanged, no batch, +1 save, +1
    render, no clock action. c is managed and has no target.

  Variants:
  - Same flag, with `VA = (0,25,900,875)` from construction. The desktop's
    docked frame is 435 wide, so `DesktopSlotUsable()` is false. c is tiled
    by `PlaceNew`, and the document changes.
  - Flag on from construction, then `Start([a, b])` → AC1's document. The
    rule is not applied at start.
  - `EnginePolicy.Default.FloatNewWindowsOnDesktop` is false, and
    `RequiresRelayout(p, p with { FloatNewWindowsOnDesktop = true })` is
    false.

  (B11.3, B14, H1, C1)
- **AC7** [mac] Non-dockable new window (worked ex. 6). From S1,
  `WindowAdded(W(d, dockable: false))` gives:
  - `Document` unchanged and no batch;
  - +1 save and +1 render;
  - no clock action, and `FocusRequests` unchanged.

  The same holds for a dockable d while `Document.Root` is null after
  `SetLayout(new LayoutDocument(null), null)` (E4).

  (B11.5, M22)
- **AC8** [mac] Framed sub-node (worked ex. 7). From S1:
  - `SetLayout(Document, "n2")`, then `WindowFocused(a)` at 100.7. a's target
    is a Tile, so there is no reveal.
  - `WindowAdded(W(c))`:
    - `Document == X(n1)[D .5, Y(n2)[X(n3)[P(a) .5, P(c) .5] .5, P(b) .5] .5]`;
    - `FramedNodeId == "n2"`;
    - one batch
      `[(a,(10,35,705,423)), (b,(10,468,1420,422)), (c,(725,35,705,423))]`.

  (B11.4, B34 sync keeps "n2", A10)
- **AC9** [mac] Duplicate add (worked ex. 8). From S1, `WindowAdded(W(a))` →
  `Document` unchanged, no batch, +1 save, +1 render, no clock action.
  - Hidden panel: `SetLayout` a document with
    `Hidden = [HiddenPanel(P(c), someTarget)]`, with c managed. Then
    `WindowAdded(W(c))` drops the hidden panel and tiles c: `Document.Hidden`
    is empty and c has a tree panel.
  - A record with a non-null `RememberedSpot` (set with `SetRememberedSpot`
    before a temporary removal and re-add) is still placed by `PlaceNew`
    (M19).

  (B10, B11.1)

### WindowRemoved

- **AC10** [mac] Close (worked ex. 9). Start from the AC5 state after
  `fake.Pump(); clock.Advance(0)`. `WindowRemoved(b, false)` at 100.2 gives:
  - `Document == X(n1)[D .5, Y(n2)[P(a) .5, P(c) .5] .5]`;
  - one batch `[(c,(725,468,705,422))]`;
  - `FocusedKey` null (it was b) and `CloseQuietUntil == 101.2`;
  - +1 save, +1 render, and `OnChange` fires once;
  - capture is stopped for b.

  (B17, B19)
- **AC11** [mac] Sharing vs adjacent (worked ex. 10). Start with `[a, b, c]`,
  then
  `SetLayout(new LayoutDocument(Y("t")[X("s")[P(a) 1/3, P(b) 1/3, P(c) 1/3] .5, D .5]), null)`,
  then Land. Then `WindowRemoved(b, false)`:
  - `ShareRemovedSpace` true → `X("s")` weights `[.5, .5]`. Batch targets:
    a `(10,35,705,423)`, c `(725,35,705,423)`.
  - `ShareRemovedSpace` false → weights `[1/3, 2/3]`. Batch targets:
    a `(10,35,467,423)`, c `(487,35,943,423)`.

  (B19)
- **AC12** [mac] Temporary removal minimum (worked ex. 11). From S1:
  - `WindowRemoved(a, true)`:
    - `Document == X(n1)[D .5, P(b) .5]` and `Document.Hidden` is empty;
    - `Away == {a: null}`;
    - one batch `[(b,(725,35,705,855))]`;
    - `FocusedKey` stays null; +1 save.
  - `fake.Pump()`, then `WindowFocused(b)` at 100.7. b is on stage, so there
    is no reveal.
  - `WindowAdded(W(a))`:
    - `Away` is empty;
    - `Document == X(n1)[D .5, Y(n3)[P(b) .5, P(a) .5] .5]`;
    - one batch `[(b,(725,35,705,423)), (a,(725,468,705,422))]`. This is
      registration order, because a was re-registered last;
    - after `clock.Advance(0)`, `FocusRequests` ends with a.

  (B17, B18, M19)
- **AC13** [mac] Navigator sync on removal. From S1:
  - `SetLayout(Document, "n2")`, then `WindowRemoved(b, false)` → n2
    collapses. `FramedNodeId == "a"`, `FramedLeaves == ["a"]`, and
    `FramingHistory` is unchanged.
  - From S1 with `SetLayout(Document, "b")`, `WindowRemoved(b, false)` →
    `FramedNodeId` is null.
  - From S1 with `SetLayout(Document, "n1")`, `WindowRemoved(b, false)` →
    `FramedNodeId == "n1"`.
  - `WindowRemoved(n, false)` (n has no panel) → `Document` unchanged, no
    batch, +1 save, `CloseQuietUntil == now + 1`.
  - Before `Start`, `WindowRemoved(w, false)` leaves
    `CloseQuietUntil == −∞`.

  (B17, B34)

### WindowFocused and reveal

- **AC14** [mac] No reveal on stage (worked ex. 12). From S1 at 100.7,
  `WindowFocused(b)` gives:
  - `FocusedKey == b` and `LastFocusedTime(b) == 100.7`;
  - +1 render, no batch, no clock action, no save;
  - `FramedNodeId` null, and `SuppressRevealUntil` still 100.6.

  (B21, B22)
- **AC15** [mac] Reveal (worked ex. 13, timings rebased). From S2:
  1. `Move(Bottom)` at 100.7 (as in AC28), `clock.Advance(0.034)`,
     `fake.Pump()`.
     - b was never focused, so the landing makes no `Focus` call (H4).
     - `FocusedKey` stays a, and `SuppressRevealUntil == 101.334`.

     Then `clock.Advance(0.2); fake.Pump()`. Now `FramedNodeId == "b"`, a is
     parked and `Phase == Desk`.
  2. Advance to 101.0, then `WindowFocused(a)` → no reveal (101.0 < 101.334).
     `FramedNodeId == "b"`, no clock action, generation unchanged.
  3. Advance to 101.5, then `WindowFocused(a)`:
     - `SuppressRevealUntil == 102.5` and `LandingFocus == a`;
     - `FramedNodeId == "a"`, and `FramingHistory` ends with `"b"`;
     - generation +1 and one clock action due at 101.534;
     - one Log containing "a"; no save.
  4. `clock.Advance(0.034)` → one batch containing `(a,(10,35,1420,855))`
     and b with `ComputeTargets()[b]` (kind Park).
  5. `fake.Pump()`:
     - `FocusRequests` ends with a, and `EngineActivatedKey == a`;
     - `SuppressRevealUntil == 102.134` and `LandingFocus` is null.

  (B22, B23, B35, B36)
- **AC16** [mac] Close quiet and boundaries (worked ex. 14). Start from AC15
  after step 1 (b framed, a parked), with the clock advanced to 102.0:
  - `WindowRemoved(n, false)` at 102.0 → `CloseQuietUntil == 103.0`;
  - `WindowFocused(a)` at 102.4 → no reveal (`FramedNodeId == "b"`);
  - `WindowFocused(a)` at 103.0, equal to `CloseQuietUntil` → reveal.

  Boundaries:
  - A focus exactly at `SuppressRevealUntil` reveals.
  - Not at rest (`InteractionActive = true`) with every other condition true
    → `PendingReveal == a`, no framing change and no clock action.

  (B22, B17.1)
- **AC17** [mac] Engine-activated marker. Start from AC15 after step 1 (a
  parked), clock at 102.0:
  - `Activate(a)`:
    - `FocusRequests` ends with a;
    - `FocusedKey == a` and `EngineActivatedKey == a`;
    - `LastFocusedTime(a) == 102.0`;
    - the pid map is unchanged.
  - `WindowFocused(a)` at 102.0 → no reveal, and `EngineActivatedKey` is
    null.
  - `WindowFocused(a)` again at 102.1 → reveal (`FramedNodeId == "a"`).
  - `Activate` of an unmanaged key → no call.

  (B21, B36, A11)
- **AC18** [mac] Focus after landing choice (findings 40, H4). From S1, call
  `FocusAfterLanding()` directly at 100.7 in each case:
  - (i) `WindowFocused(a)` at 100.3, then `LandingFocus = b` → `Focus(b)`.
  - (ii) `WindowFocused(a)` at 100.3, then `LandingFocus = n` (no target) →
    `Focus(a)`.
  - (iii) `WindowFocused(b)` at 100.3, then `WindowFocused(n)` at 100.4.
    `FocusedKey == n`, which is not a candidate, and `LandingFocus` is null.
    b is the only candidate ever focused → `Focus(b)`.
  - (iv) `SetLayout(new LayoutDocument(D), null)`, so there are no
    candidates → no `Focus` call.
  - (v) H4: no window ever focused, `FocusedKey` null, `LandingFocus` null →
    no `Focus` call. `FocusedKey` and `EngineActivatedKey` stay null.
  - (vi) `WindowFocused(n)` at 100.3 only (a and b never focused) → no
    `Focus` call. `FocusedKey` stays n.
  - Tie: `WindowFocused(a)` and `WindowFocused(b)` both at 100.3, then
    `WindowFocused(n)` → `Focus(a)` (registration order).

  In every case, afterwards `SuppressRevealUntil == 101.3` and
  `LandingFocus` is null.

  (B35, H4, A12)

### WindowMoved

- **AC19** [mac] Echoes and ignored moves (worked ex. 15). Use the AC5 state.
  c's write `(1083,468,347,422)` was issued at 100.2.
  - Before `fake.Pump()`, `WindowMoved(c,(1,2,3,4))` → ignored, because a
    write is in flight. No batch, `SnapBackTimes(c)` is empty, and the known
    frame is `(1,2,3,4)`.
  - After `fake.Pump()`, at 100.3, `WindowMoved(c,(1083,468,348,422))` →
    ignored as an expected echo.
  - `fake.MouseDown = true`, then `WindowMoved(c,(0,0,500,500))` → ignored.

  In each case there is no write, render or save, and the generation is
  unchanged.

  Also:
  - Not started: `WindowMoved` only updates the known frame.
  - A window without a panel (n) moved anywhere → no write, and
    `SnapBackTimes(n)` is empty.

  (B25, B26, B28)
- **AC20** [mac] Snap-back and the maximum (worked ex. 16). From S1, advance
  to 110.0. Each move below is preceded by `fake.SetFrameDirect` with the
  same frame, and followed by `fake.Pump()` after the call.
  - `WindowMoved(b,(300,200,705,422))` at 110.0 →
    `SnapBackTimes(b) == [110.0]`, one batch `[(b,(725,468,705,422))]`,
    +1 render, no save.
  - At 111.0 → `[110.0, 111.0]` and a batch.
  - At 112.0:
    - `SnapBackTimes(b)` is empty;
    - `WriterState(b).Maximum == (705,422)`;
    - the store is unchanged (`PersistCount` 0);
    - one Log containing "b";
    - batch `[(b,(725,468,705,422))]`; no save.
  - At 112.9 → `[112.9]` and a plain snap-back batch.

  Window boundary, in a fresh run: moves at 110.0, 111.0 and 113.0 give
  `[111.0, 113.0]`, with no accept. 110.0 is dropped because
  `113 − 110 ≥ 3`.

  The generation is unchanged throughout.

  (B33)
- **AC21** [mac] Insisting larger (worked ex. 17). From S1, call
  `WindowMoved(b,(725,468,705,470))` at 110.0, 110.5 and 111.0, each followed
  by `fake.Pump()`:
  - the first two → snap-back batches `[(b,(725,468,705,422))]`;
  - the third:
    - `WriterState(b).Minimum == (0,470)`;
    - the store has `Entries[com.x] == LimitRules.Learn(null, (0,470))`;
    - one batch whose single entry is b with `ComputeTargets()[b]` (kind
      `ParkWithPicture`);
    - `Window(b).Parked` is true; no save.

  (B29, B30 "grew" false, B33)
- **AC22** [mac] Insisting within limits (worked ex. 18). Call
  `Start([b, c])`, then
  `SetLayout(new LayoutDocument(Y("s")[P(b) 610/865, P(c) 255/865]), null)`,
  then `PresentDirectly()`, then Land. Tile b is `(10,35,1420,600)`.
  - Three moves to `(10,35,1420,655)` at 110.0, 110.5 and 111.0, each
    followed by Pump → after the third, `Minimum == (0,655)`, and the
    store's `Learn` is called with `(0,655)`.
  - Weights `660/865, 205/865` (tile `(10,35,1420,650)`) with frame height
    705 → after the third:
    - `Minimum` is null and the store's `PersistCount` is 0;
    - one Log;
    - b is still written back to `(10,35,1420,650)`.

  (B33)
- **AC23** [mac] Zoom request (worked ex. 19). From S1 at 110.0, call
  `WindowMoved(b,(100,100,1340,800))`:
  - `FramedNodeId == "b"`, `ZoomedAlone` is true, `ZoomReturnNodeId` is
    null;
  - `FramingHistory == [null]` and `LastZoomToggle == 110.0`;
  - `SnapBackTimes(b)` is empty;
  - no batch during the call, generation +1, no save, one Log.

  Then `clock.Advance(0.034)` → one batch
  `[(a, ComputeTargets()[a]), (b,(10,35,1420,855))]`. a's target has kind
  Park and equals `(1439,899,705,423)`. After `fake.Pump()` there is no
  `Focus` call, because b was never focused (H4).

  Variants:
  - The same move at 110.0 with `LastZoomToggle` set to 109.0 by a prior
    toggle → snap-back instead (spacing `≤ 1`).
  - Height-only grow `(725,468,705,483)` → zoom.
  - `(725,468,702,483)` → snap-back (702 < 703).

  (B29–B31)
- **AC24** [mac] Un-zoom (worked ex. 20). Start from AC23 after
  `clock.Advance(0.034); fake.Pump(); clock.Advance(0.2); fake.Pump()` (now
  110.234).
  - At 110.5, `WindowMoved(b,(300,300,600,400))` → snap-back:
    - `SnapBackTimes(b) == [110.5]`;
    - batch `[(b,(10,35,1420,855))]`;
    - `FramedNodeId == "b"`.

    Then `fake.Pump()`.
  - At 111.5, the same frame:
    - `FramedNodeId` is null and `ZoomedAlone` is false;
    - `LastZoomToggle == 111.5`;
    - `FramingHistory == [null, "b"]`.

    Then `clock.Advance(0.034)` → batch
    `[(a,(725,35,705,423)), (b,(725,468,705,422))]`.

  "Fills" variants. Each has b framed alone with tile `(10,35,1420,855)`, at
  least 1.01 s after the last toggle, with b known at its tile:
  - `(0,25,1440,875)` → un-zoom;
  - `(10,35,1424,855)` (drift exactly 4) → snap-back;
  - un-zoom while `ZoomedAlone` is false (b framed by `Move`) →
    `FramedNodeId` is null.

  (B30, B32, A15)
- **AC25** [mac] Parked window moved (worked ex. 21). Start from AC23 after
  the landing (a parked, `Window(a).Parked` true). Call
  `WindowMoved(a,(400,300,705,855))`:
  - exactly one batch `[(a, ComputeTargets()[a])]` (kind Park);
  - `Window(a).Parked` is true after the call;
  - `SnapBackTimes(a)` is empty;
  - +1 render, no save.

  (B27)

### ArrangeAll

- **AC26** [mac] Worked ex. 22. Start from the AC5 state after
  `fake.Pump()`. Add `WindowAdded(W(d, dockable: false))` and
  `fake.SetZOrder([c, d, a, b])`. Then `ArrangeAll()`:
  - `Document == X(n4)[P(c) .5, Y(n5)[P(a) .5, X(n6)[P(b) .5, D .5] .5] .5]`;
  - `FramedNodeId` is null;
  - one batch `[(c,(10,35,705,855))]`. a's and b's targets,
    `(725,35,705,423)` and `(725,468,348,422)`, equal their known frames;
  - +1 save, +1 render, `OnChange` fires once, no clock action;
  - d has no panel.

  Variants:
  - With `SetLayout(Document, "n2")` beforehand → `FramedNodeId` is null
    afterwards, and `FramingHistory` ends with `"n2"`.
  - After `SetPhase(Overview)` → history unchanged, no batch, one save, two
    renders (Commit step 3.2 renders, then step 3.5's `PresentLayout` renders
    again in Overview; corrected at test stage).

  (B40)
- **AC27** [mac] Worked ex. 23 and filters. Use the same state.
  - `fake.RemoveWindow(a); fake.RemoveWindow(c); fake.RemoveWindow(d)`, so
    `FrontToBack() == [b, n]` (n is non-dockable and ignored by ArrangeAll;
    corrected at test stage) →
    `Document == X(n4)[P(b) .5, Y(n5)[P(a) .5, X(n6)[P(c) .5, D .5] .5] .5]`.
  - Instead, `WindowAdded(W(a, dockable: false))` (a refresh) with z-order
    `[c, d, a, b]` →
    - `Document.Root == X(n4)[P(c) .5, Y(n5)[P(b) .5, D .5] .5]`;
    - `Document.Floating == [FloatingPanel(P(a), Placement.CascadeRect(0), 1)]`.
  - An unmanaged key `zz` in the document (added with `SetLayout`) is kept
    in the order, after the front-to-back part.
  - Suspended or not started → nothing.

  (B40, H3)

### Move(toward)

- **AC28** [mac] Worked ex. 24 (H2; outcome changed by H4). From S2 (nothing
  framed, a focused, b never focused):
  - `Move(Bottom)` at 100.7:
    - `FramedNodeId == "b"` and `FramingHistory == [null]`;
    - no batch during the call, generation +1, no save, `Document`
      unchanged.
  - `clock.Advance(0.034)` → batch
    `[(a,(1439,899,705,423)), (b,(10,35,1420,855))]`. a's target kind is
    Park.
  - `fake.Pump()`:
    - no new `Focus` call, because a is not a candidate and b was never
      focused (H4);
    - `FocusedKey == a`, `EngineActivatedKey` null;
    - `SuppressRevealUntil == 101.334`.
  - Then `clock.Advance(0.2)` → +1 save with `FramedNodeId "b"`.
  - Variant: if b was focused once before (`WindowFocused(b)` at 100.65,
    then `WindowFocused(a)` at 100.7), the landing calls `Focus(b)` and
    `FocusedKey == b`.

  (B38, B39, B35, H4)
- **AC29** [mac] Worked ex. 25 and 26. From S2:
  - `Move(Right)` → nothing. Generation, history, `Plans`, `Writes` and
    clock pending are all unchanged.
  - `Move(Left)` → `FramedNodeId == "desktop"`.
    - After `clock.Advance(0.034)`, the batch parks a and b (both kinds
      Park).
    - After `fake.Pump()`, `FocusRequests` is unchanged and
      `SuppressRevealUntil == now + 0.6`.
  - From S1 (nothing framed, `FocusedKey` null), `Move(Top)` → nothing.
  - `FocusedKey == n` (no panel) → nothing.
  - Suspended or not started → nothing.

  (B37–B39)
- **AC30** [mac] Worked ex. 27. Start from AC28 after the landing (b
  framed). `Move(Top)` → `FramedNodeId == "a"` and
  `FramingHistory == [null, "b"]`.

  (B38)

### Saving

- **AC31** [mac] Save triggers. Each step starts from S2 and counts `SaveLog`
  entries.
  - **0 saves** for each of these:
    - `WindowFocused(b)`;
    - `WindowTitleChanged(a,"x")`;
    - `WindowBecameDockable(n)`;
    - `CaptureArrived(a)`;
    - `AppActivated(1,false)`;
    - a snap-back (an AC20 step);
    - an echo (AC19);
    - a parked rewrite (AC25);
    - `Move(Bottom)`, until its settle (then exactly 1).
  - **1 save** for each of these:
    - `WindowAdded` of a new window;
    - `WindowAdded` of a managed window;
    - `WindowRemoved`;
    - `ArrangeAll()`.
  - No step leaves a save pending on the clock.
  - The last entry always equals
    `new SavedDisplay(Document, FramedNodeId, base…)`, with `base` carried
    over as in AC3.

  (B42, B43)

### Regression and hygiene

- **AC32** [mac] Earlier suites.
  - Every test of `specs/engine-inbound-api.md` and
    `specs/engine-frame-writer.md` passes after migration **recipe R** below,
    except the superseded assertions listed here, which take the new values
    given.
  - All tests of `specs/engine-interfaces.md`,
    `specs/engine-projection-targets.md` and the Core specs pass unchanged.

  **Recipe R** applies to tests that call `Start`:
  1. The rig's windows are constructed with `Dockable: false`.
  2. The clock is constructed at 99 instead of 100, and
     `clock.Advance(1); fake.Pump();` follows `Start` immediately. This lets
     the empty start landing settle before the test body. All later absolute
     times are unchanged.

     Because of H4, this start landing makes no `Focus` call, so
     `FocusRequests` and `FocusedKey` are unchanged by recipe R.

  **Superseded assertions:**
  - **inbound AC3** and
    `tests/CanvasWm.Engine.Tests/EngineShell/ConstructionPolicyTests.cs`:
    - line 83, `Assert.Null(p.MaxDockedTiles);`, is replaced by
      `Assert.False(p.FloatNewWindowsOnDesktop);` (C1, C12);
    - the count assertion at line 122 stays `38`;
    - add an assertion that no public instance property named
      `MaxDockedTiles` exists.

    The test file must not contain the licence-module word.
  - **inbound AC8** (asserts on the `Start` call itself, without R step 2):
    - two renders during `Start`: the start render and the empty landing's
      `LandingDone` render. The first lists w1 and w2;
    - no `Focus` call (H4);
    - the last `SaveLog` entry is the live record,
      `SavedDisplay(new LayoutDocument(D))`. A pre-saved record whose keys
      are disjoint from w1 and w2 is ignored, so the entry is the same.
  - **frame-writer AC2**, the bullet "after Start with a pre-saved record
    (L2, framed a)":
    - `Document` equals B4 applied to `Sanitize(L2)`: the saved tree with a
      desktop inserted on the Right of root `s`, with split id `n1` from
      `Ids = SequentialIds`. The exact shape after normalisation is whatever
      `InsertBeside` returns;
    - `FramedNodeId == "a"`;
    - `ComputeTargets()` gives the targets of that document with `a` framed.
  - **frame-writer C3** ("Start makes no write") no longer holds for
    dockable windows.

  (C2–C12, H4)
- **AC33** [mac] Build and inspection.
  - `dotnet build CanvasWm.slnx` gives 0 warnings.
  - The Engine still has exactly one `ProjectReference` (Core) and no
    `PackageReference`.
  - No file under `src/CanvasWm.Core`, `src/CanvasWm.Engine/Services`,
    `src/CanvasWm.Engine/Rendering` or `tests/CanvasWm.Engine.Tests/Fakes`
    is changed.
  - Reflection:
    - the public members of `DisplayEngine` are exactly those before this
      feature;
    - `EnginePolicy` has exactly 38 public instance properties, including
      `FloatNewWindowsOnDesktop`, and none named `MaxDockedTiles`;
    - `WindowEvents` is not public.
  - The `WindowEvents` constants equal 1.0, 1.0, 1.0, 0.5, 100, 60, 2, 100,
    0.92, 0.90, 4, 2, 0.8, 480 and 320.
  - `DisplayEngine.Events.cs` has no numeric literal `0.6`, and no literal
    `3` used as a snap-back limit or time window. These values are read from
    `Tuning`. A test with `Tuning.Default with { SnapBackLimit = 2 }` accepts
    on the second snap-back.
  - No file in `src/` or `tests/` contains the studied app's name, the
    licence-module word, or the identifier `MaxDockedTiles` (H3, C12).
  - The 31 remaining command stubs still do nothing (inbound AC32 for those
    names).

  (C1–C12, H3)

**Count: 33 [mac] (AC1–AC33), 0 [windows].**

The Windows side belongs to the Win32 window-system and event features:
- event mapping;
- treating MOVESIZESTART/END as "mouse down";
- restoring a maximised window before re-land writes;
- foreground-lock refusals;
- the dockable classification.

Their verification lists should include these checks:
- dragging a tiled window snaps it back on release;
- maximising a tiled window frames it alone, and restoring it un-frames it;
- minimising and restoring a window re-tiles it (with M19, possibly in a
  different place);
- a dialog (non-dockable) is never moved;
- starting CanvasWM does not steal focus from the current foreground window
  (H4).

## Assumptions

- **A1. Interim minimums M19 and M22** (human decision) are temporary.
  Removing them is the job of #19 and #22. The code must comment each site
  with `// SPEC-ASSUMPTION` naming M19 or M22.
- **A2. Start lands synchronously** (findings 7: CanvasWM lands
  immediately). The pass uses `PlaceWindows(++PlacementGeneration)` without
  the 0.034 s re-land delay.
- **A3. "Ignored entirely" also drops the saved views, owners, dock spots and
  float sizes**, because they refer to the ignored layout. Otherwise they are
  carried into every save unchanged, because nothing in #27 edits them.
  *Review.*
- **A4. Saved framed id.** It is kept only when it names a `FocusLayout`
  entry of the sanitised tree. Otherwise nothing is framed, because there are
  no saved framed leaves to repair it with.
- **A5. The desktop is inserted on the Right of the old root, with fraction
  0.5.** The findings leave the edge Unknown. *Review.*
- **A6. The desktop is ensured only at start.** The findings' document-setter
  rule depends on the hidden-desktop state, which belongs to #15. No #27 edit
  can remove a desktop node: `RemoveNode` never removes desktops, and
  `PlaceNew` and `ArrangeDwindle` keep or rebuild one.
- **A7. Hidden-list windows at start stay hidden** (findings Unknown). Under
  M19 the engine never writes the hidden list, so this only matters for
  hand-made documents. On a later `WindowAdded`, such a window is taken out
  of the hidden list and placed as new (B10).
- **A8. The decision order in B11** is spot, then split, then H1, then
  dockable, then float. The findings label this Inferred. Under the defaults
  only dockable and float matter.
- **A9. Findings rule 4 is deferred.** "Drop docked windows moved while
  unmanaged" needs remembered resting frames and parked flags, and
  `SavedDisplay` has neither. Owner: #14 or #19. *Review.*
- **A10. There is no explicit "include the new window" reframe.** At rest,
  `PlaceNew` always puts the window inside the framed node. When not at
  rest, the landing focus is set but the framing is not widened. Owner: #15.
- **A11. The engine-activated marker is consumed by the next `WindowFocused`
  of any key.** A literal reading of the findings would suppress reveals for
  that key forever. *Review.*
- **A12. Focus-after-landing ties (decision taken: H4).** When no candidate
  window has ever been focused, nothing is focused and the OS focus is left
  alone.
  - This applies to every landing, not only the first one after `Start`. The
    rule is the same function, and a never-focused tile is also the
    situation after `Move` or zoom.
  - It replaces the tie rule implied by findings worked examples 1 and 24.
  - Ties among ever-focused candidates still go to registration order.
- **A13. A new window's focus** still uses #26's next-turn `Focus` call, not
  `Activate`, so its echo is not marked. This is harmless because the new
  window is on stage.
- **A14. No saved-copy filtering.** The findings mention guests (Inferred).
  #24 adds the filtering.
- **A15. Zoom memory.**
  - It is set only by B31.
  - It is consumed by un-zoom.
  - It is cleared by any other framing change: Move, reveal, ArrangeAll, sync
    and SetLayout.
  - Un-zoom without memory frames nothing.

  *Review.*
- **A16. `LandingFocus` on re-lands.** Zoom, un-zoom and `Move` re-lands do
  not set `LandingFocus`. The reveal does, as the findings say. After
  landing, focus goes to the focused window if it is a candidate, otherwise
  as in B35.
- **A17. Snap-back rewrites and parked re-parks use `WriteTargets`.** That is
  one `SetFrames` batch with learning on. This follows writer A4, not the
  findings' "single write".
- **A18. `Release` also sets the close-quiet time and commits the layout
  removal**, because it delegates to `RemoveCore` (inbound A7).
- **A19. Logging.** Reveal, zoom, un-zoom and accept each log once, with text
  that contains the key. There is no other text contract.
- **A20. `MaxDockedTiles` removed (decision taken: H3, C12).** CanvasWM has
  no docked-tile cap and no policy field for one. Its removal keeps the
  policy property count at 38. Nothing in `src/` or `tests/` may refer to the
  reason using the licence-module word.

### Reported for review (earlier specs not changed by this spec)

- **`specs/engine-inbound-api.md`:**
  - apply the three C12 edits under "Required edits to earlier specs" (policy
    listing, property-count paragraph, AC3);
  - B10, B16, B17d, B19c, B20 and B21: their hooks are now filled;
  - B44 no longer covers `Move` and `ArrangeAll`;
  - AC8 changes as in AC32;
  - earlier test rigs need recipe R (AC32).
- **`specs/engine-frame-writer.md`:**
  - C3 ("Start still makes no write") and AC2's start bullet are superseded;
  - `LandingDone` now runs focus after landing (C9);
  - `SetLayout` now refreshes the navigator state (C10);
  - `WriterRecord` gains two fields (C11).
- **`findings/engine-window-events.md`:** worked examples 1 and 24 assume the
  first-registered or never-focused tile gets focus. Under H4 it does not.

## Traceability

| Spec item | Findings rule / example / decision |
|---|---|
| B1, B2, AC1, AC2, A3 | 1, 2; ex. 1 (focus outcome per H4), 2 |
| B3, B4, AC3, A4, A5, A6 | 3, 6; ex. 3 |
| B5, AC1, AC4, A7, A9 | 4 (deferred), 5; ex. 1 |
| B6, B7, B8, AC1, A2 | 6, 7, 8; H4 |
| B9–B12, AC5, AC7–AC9, A8, A10 | 9–13, 23 (M22); ex. 4, 6, 7, 8 |
| B11.3, B14, AC6 | 11.3, 22 (desktop slot); H1; ex. 5 |
| B13 | 14 |
| B16–B19, AC10–AC13 | 15–19; ex. 9, 10, 11 |
| B18, AC12, M19 | 17, 20; ex. 11 |
| B21–B24, AC14–AC17, A11 | 21, 22, 41; ex. 12, 13 (rebased), 14 |
| B25–B28, AC19, AC25 | 24–27; ex. 15, 21 |
| B29–B32, AC23, AC24, A15 | 28, 29; ex. 19, 20 |
| B33, AC20–AC22, A17 | 30, 31; ex. 16, 17, 18 |
| B34, AC13 | 43 |
| B35, B36, AC18, A12, A13 | 40, 41; H4 (overrides the tie in ex. 1, 24) |
| B37–B39, AC28–AC30 | 37–39; H2, H4; ex. 24–27 |
| B40, AC26, AC27 | 34–36; H3; ex. 22, 23 |
| B42, B43, AC31, A14 | 45, 46 |
| B44 | 33 |
| C1, AC6, AC33 | H1; data model "Policy (new)" |
| C12, A20, AC32, AC33 | H3 (human decision: remove tile-cap field) |
| AC32 | inbound/writer hooks; CLAUDE.md |
| AC33 | CLAUDE.md naming rule; tuning-constants (SnapBackLimit, SnapBackWindowSeconds, LandingRevealQuietSeconds) |
