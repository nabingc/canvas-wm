# Spec: directional-focus

## Goal

A pure Core function that answers "which space lies next to the current one in
direction left / right / up / down?" over a layout entry table.

Given an ordered `LayoutEntries` table (unit-square rects, y-down), the id of
the *current* node (any kind: panel, split, desktop or a virtual `range:[...]`
entry) and an `Edge` direction, `DirectionalFocus.Neighbour` returns the id of
the single best neighbouring entry, or `null`. A neighbour must **share the
current node's edge** on that side (within 1e-6) and **overlap it across the
direction** (by more than 1e-6). Among those, the one most similar in size and
best aligned wins; virtual range entries pay a small penalty. Ties go to table
order. There is no wrap-around and no direction memory.

The function never edits a tree. Moving in a direction moves the camera/focus
to another space; it does not move a window.

Out of scope:
- Engine/camera (#13, #15): picking the current id (framed id, else root id),
  framing the result (pan/zoom, landing, OS focus), recording back/forward
  history, and the notification sent after a successful move. Hotkey binding.
- Floating-panel entries (#22): this spec only fixes how such entries must be
  marked so the exclusion rule holds once they exist (A2).
- Any "move window toward" tree-editing command. It is not part of the studied
  behaviour. If wanted, it is a new feature.

## Placement

- Project: `src/CanvasWm.Core` (`net10.0`, BCL only; no new package or project
  references).
- Namespace: `CanvasWm.Core.Navigation` (new).
- Suggested file: `src/CanvasWm.Core/Navigation/DirectionalFocus.cs`.
- Tests: `tests/CanvasWm.Core.Tests`, suggested files
  `Navigation/DirectionalFocusTests.cs` (rules and worked examples),
  `Navigation/DirectionalFocusHelpersTests.cs` (`IsAdjacent`, `Score`),
  `Navigation/DirectionalFocusPropertyTests.cs`.
- Depends on:
  - `specs/layout-tree.md`: `LayoutNode`, `SplitNode`, `PanelNode`,
    `DesktopNode` (`DefaultId = "desktop"`, `Child`), `Axis`.
  - `specs/layout-solver.md`: `LayoutEntries` (ordered, ordinal keys),
    `LayoutEntry` (`Node`, `Rect`, `Parent`, `Scale`, `Virtual`),
    `LayoutSolver.FocusLayout`, `LayoutSolver.RangeId`, `Rect`
    (`CanvasWm.Core.Geometry`).
  - `specs/docking.md`: `Edge` (`Left = 0, Right = 1, Top = 2, Bottom = 3`).
    It is reused as the direction (Top = up, Bottom = down).
  - **No existing type is changed** (A6).

## Public API

All members are public and have XML docs. All id comparisons are **ordinal**.
Reference arguments not marked nullable throw `ArgumentNullException` when
null.

```csharp
namespace CanvasWm.Core.Navigation;

using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;

/// <summary>Pure directional neighbour search over a layout entry table.
/// Moving in a direction picks another space to frame; it never edits a tree.</summary>
public static class DirectionalFocus
{
    /// <summary>Tolerance of the shared-edge test: |gap| must be strictly below 1e-6.</summary>
    public const double TouchTolerance = 1e-6;

    /// <summary>Overlap across the direction must be strictly greater than 1e-6.</summary>
    public const double OverlapTolerance = 1e-6;

    /// <summary>Added to the score of virtual (range) entries: 0.05. This is a penalty, so
    /// it makes them less likely to win.</summary>
    public const double VirtualPenalty = 0.05;

    /// <summary>Id of the best neighbour of <paramref name="currentId"/> in
    /// <paramref name="direction"/>, or null if there is none. Entries are scanned in table
    /// order (B1–B14).</summary>
    /// <param name="entries">Entry table, normally LayoutSolver.FocusLayout(root, null),
    /// later plus floating entries (A2).</param>
    /// <param name="currentId">Id of the current node (any kind, including a range id).
    /// An id with no entry gives null.</param>
    /// <param name="direction">Left, Right, Top (up) or Bottom (down).</param>
    /// <exception cref="ArgumentNullException">entries or currentId is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">direction is not a defined Edge value.</exception>
    public static string? Neighbour(LayoutEntries entries, string currentId, Edge direction);

    /// <summary>Same search over an explicit ordered sequence of (id, entry) rows. Use it when a
    /// table is assembled outside the solver, for example by tests or by a later feature that
    /// appends floating entries. Neighbour(LayoutEntries, ...) gives the same result as calling
    /// this overload on the table's enumeration (B15).</summary>
    /// <exception cref="ArgumentNullException">rows or currentId is null, or a row has a null
    /// key or a null value.</exception>
    /// <exception cref="ArgumentOutOfRangeException">direction is not a defined Edge value.</exception>
    public static string? Neighbour(IEnumerable<KeyValuePair<string, LayoutEntry>> rows,
                                    string currentId, Edge direction);

    /// <summary>Geometric part of the filter: candidate shares current's edge on the
    /// direction side (B7) and overlaps it across the direction (B8).</summary>
    /// <exception cref="ArgumentOutOfRangeException">direction is not a defined Edge value.</exception>
    public static bool IsAdjacent(Rect current, Rect candidate, Edge direction);

    /// <summary>Score of a candidate (lower is better, B9). Does not check adjacency.
    /// May return +Infinity or NaN for degenerate rects (E5).</summary>
    /// <exception cref="ArgumentOutOfRangeException">direction is not a defined Edge value.</exception>
    public static double Score(Rect current, Rect candidate, Edge direction, bool isVirtual);
}
```

Internal (not public API; named only so the rules can refer to it):
`SubtreeContains(LayoutNode node, string id)`. It is true when `node.Id == id`
or any descendant's id equals `id`. Descendants are `SplitNode.Children`
(recursively) and `DesktopNode.Child` (recursively). A `PanelNode` has none.
Window keys in `PanelNode.Views` are **not** ids and are never matched.

## Behaviour

Notation: `C` = the current entry's rect, `K` = a candidate entry's rect.
`maxX = X + Width`, `maxY = Y + Height`. The *cross axis* of a direction is Y
for Left/Right and X for Top/Bottom. All arithmetic is `double`, with no
rounding.

### Validation and current entry

- **B1.** Argument checks happen first, in this order: `entries`/`rows`
  null → `ArgumentNullException`; `currentId` null → `ArgumentNullException`;
  `direction` not in {0,1,2,3} → `ArgumentOutOfRangeException`. For `rows`, a
  null key or null value is reported (`ArgumentNullException`) when that row
  is reached during enumeration (A7).
- **B2. Current entry.** The current entry is the **first** row whose key
  equals `currentId`. If there is no such row (including an empty table), the
  result is `null`.

### Candidate filter (every row, in table order; all conditions must hold)

- **B3. Not self.** `key != currentId`.
- **B4. Not a desktop-parented entry.** `entry.Parent != DesktopNode.DefaultId`
  (the literal `"desktop"`). An entry with `Parent == null` passes this rule.
  The rule compares with the literal default id, not with the id of whatever
  desktop is in the tree (A1, A2). In today's solver output it removes the
  direct child of a desktop whose id is `"desktop"`. Once floating entries are
  added it also removes floats (A2).
- **B5. Not an ancestor or descendant.**
  (a) `!SubtreeContains(entry.Node, currentId)`: the candidate is not the
  current node and does not contain it.
  (b) `!SubtreeContains(currentEntry.Node, key)`: the current node does not
  contain the candidate.
  Containment uses the entries' `Node` values. For a range entry that is the
  synthetic split, whose children are the run's real children. So a range
  current excludes the run's members and their descendants. It does **not**
  exclude the real parent split under (b). That split is removed under (a) only
  if its subtree contains the range id, which it never does. In practice B7
  removes it.
- **B6. Not an occupied desktop.** Skip if `entry.Node is DesktopNode { Child: not null }`.
  An empty desktop (`Child == null`) is an ordinary candidate. Panels, splits
  and virtual range entries are candidates.
- **B7. Touch.** The candidate's opposite edge must lie on the current edge:
  - Left:   `|K.maxX − C.X|    < TouchTolerance`
  - Right:  `|K.X    − C.maxX| < TouchTolerance`
  - Top:    `|K.maxY − C.Y|    < TouchTolerance`
  - Bottom: `|K.Y    − C.maxY| < TouchTolerance`
  The test uses the absolute difference. A rect further away, even one that
  lies entirely beyond the edge, never passes.
- **B8. Cross overlap.**
  - Left/Right: `min(K.maxY, C.maxY) − max(K.Y, C.Y) > OverlapTolerance`
  - Top/Bottom: `min(K.maxX, C.maxX) − max(K.X, C.X) > OverlapTolerance`
  Corner contact (overlap 0) and overlap of exactly 1e-6 fail.
  `IsAdjacent(C, K, d)` is exactly `B7 && B8`.

### Score and selection

- **B9. Score** (lower is better):
  `Score = |ln(K.Width / C.Width)| + |ln(K.Height / C.Height)| + |Kc − Cc| / Cext + (isVirtual ? VirtualPenalty : 0)`,
  where `ln` is `Math.Log` (natural log). For Left/Right:
  `Kc = K.Y + K.Height / 2`, `Cc = C.Y + C.Height / 2`, `Cext = C.Height`. For
  Top/Bottom: `Kc = K.X + K.Width / 2`, `Cc = C.X + C.Width / 2`, `Cext = C.Width`.
  In `Neighbour`, `isVirtual = entry.Virtual`. `Score` and `Neighbour` use the
  same formula, evaluated in the same order of operations, so a recomputed
  score is bit-identical.
- **B10. Selection.** `best = +∞`, `bestId = null`. For each candidate that
  passes B3–B8, in table order: if `score < best` (strictly), set
  `best = score`, `bestId = key`. So on an exact tie the earlier row wins. A
  NaN or +∞ score never wins (E5).
- **B11. Result** is `bestId`. Only the key is returned, never the entry.
- **B12. No wrap, no memory.** The function keeps no state. Reversing the
  direction re-runs the same scoring, and it returns to the previous node only
  if that node scores best. Nothing outside the table is consulted, including
  pixel sizes, display aspect, camera, focus, history and `Scale`.
- **B13. Root current.** When the current id is the tree root, every tree
  entry fails B3 or B5(a). Under A2, floats fail B4. The result is `null`. The
  Engine passes the root id when nothing is framed, so a move with nothing
  framed does nothing.
- **B14. Table kinds.** Any table is accepted. The intended input is the
  weights-mode `LayoutSolver.FocusLayout(root, null)` (which equals the
  default `LayoutCache` compute), including its range entries. Tables from the
  minimum-aware mode work too, but the Engine must pass the weights-mode table
  (A4).
- **B15. Overloads.** `Neighbour(LayoutEntries, …)` must return exactly what
  `Neighbour(IEnumerable<KeyValuePair<…>>, …)` returns for the table's
  insertion-order enumeration. In the `rows` overload, duplicate keys are
  allowed. B2 takes the first matching row as the current entry, and every
  row is checked as a candidate on its own (B3 drops all rows whose key equals
  `currentId`).
- **B16. Purity.** No input is modified. No I/O, randomness or clock. The
  same inputs always give the same output.

## Edge cases & errors

- **E1.** Unknown `currentId`, or an empty table → `null` (B2).
- **E2.** `currentId` that is a range id (`range:[...]`) is a normal current
  (worked example 8).
- **E3.** A desktop with a child is never a result, even when it is
  adjacent (B6). An empty desktop can be a result (example 6).
- **E4.** Nested default desktop: in `X{s}[a, Desktop{desktop}[b]]`, `b` has
  `Parent == "desktop"` (B4) and the desktop is occupied (B6). So from `a`
  toward Right the result is `null`. This is the literal rule, kept faithfully
  (A1).
- **E5. Degenerate rects.** There is no special handling. A current with
  `Width` or `Height` ≤ 0 or non-finite makes the ratios or offset infinite or
  NaN. A candidate with zero width or height gives `|ln 0| = +∞`. Such scores
  never satisfy the strict `<`, so the candidate is never chosen. The function
  does not throw for them. Solver output from a normalised tree has no such
  rects.
- **E6.** NaN coordinates make B7/B8 comparisons false, so the row is not a
  candidate.
- **E7.** Undefined `Edge` values (e.g. `(Edge)4`) throw
  `ArgumentOutOfRangeException` from all four public methods (A5).

## Acceptance criteria

Notation for trees follows `specs/layout-solver.md`: `X{id}[...]` / `Y{id}[...]`
are splits on axis X / Y with equal weights unless a weight is given in
parentheses, and bare letters are `PanelNode.ForWindow(letter)`. Tables are
`LayoutSolver.FocusLayout(root, null)` unless stated. "Hand-built rows" are a
`List<KeyValuePair<string, LayoutEntry>>` passed to the rows overload, where
each panel row is `new LayoutEntry(PanelNode.ForWindow(id), rect, parent, null, false)`.
Score comparisons use an absolute tolerance of `1e-9` against the exact
expression given, with `ln = Math.Log`.

### Worked examples (findings examples 1–10)

- **AC1** [mac] `X{s}[a, b]`: `Neighbour(a, Right) == "b"`, with
  `Score(a.Rect, b.Rect, Right, false) == 0`; `Neighbour(a, Left) == null`;
  `Neighbour(a, Bottom) == null`; `Neighbour(a, Top) == null`;
  `Neighbour(b, Left) == "a"`.
- **AC2** [mac] `X{s}[a, Y{t}[b, c]]` (rects: a (0,0,.5,1), t (.5,0,.5,1),
  b (.5,0,.5,.5), c (.5,.5,.5,.5)):
  - `Neighbour(a, Right) == "t"`. Scores: t = 0; b = c = `ln 2 + 0.25`.
  - `Neighbour(b, Left) == "a"`, with score `ln 2 + 0.5`.
  - `Neighbour(b, Bottom) == "c"` (score 0); `Neighbour(t, Left) == "a"`
    (score 0); `Neighbour(c, Right) == null`.
- **AC3** [mac] Range wins: `Y{r}[X{t}[a, b], X{u}[c, d, e, f]]`.
  `Neighbour(a, Bottom) == LayoutSolver.RangeId("u", ["c","d"])` (i.e.
  `range:["u","c","d"]`). Scores against a (0,0,.5,.5): u = `ln 2 + 0.5`;
  c = d = `ln 2 + 0.25`; `[c,d]` = `0.05`; `[c,d,e]` = `ln 1.5 + 0.25 + 0.05`;
  `[d,e]` = `0.5 + 0.05`; `[d,e,f]` = `ln 1.5 + 0.75 + 0.05`.
  `IsAdjacent(a, x, Bottom)` is false for x ∈ {e, f, `[e,f]`} (zero or negative
  overlap).
- **AC4** [mac] Penalty decides: `Y{r}[X{t}[a, b], X{u}[c(0.45), d(0.1), e(0.45)]]`.
  `Neighbour(a, Bottom) == "c"`. Scores: c (0,.5,.45,.5) = `|ln 0.9| + 0.05`;
  `range:["u","c","d"]` (0,.5,.55,.5) = `ln 1.1 + 0.05 + 0.05`;
  d (overlap 0.05) = `ln 5 + 0.5`. Also `Score(a, [c,d], Bottom, false)`
  = `ln 1.1 + 0.05`, which is less than c's score. This shows the penalty is
  what decides.
- **AC5** [mac] Tie goes to table order: `Y{r}[X{t}[a(0.25), m(0.5), b(0.25)], X{u}[c, d]]`.
  `Neighbour(m, Bottom) == "c"`. The scores of c and d are both exactly
  `0.5` (`==`, bit-identical), and u = `ln 2`. t's ranges contain m and are
  excluded.
- **AC6** [mac] Empty desktop is a target: `X{s}[a, Desktop{desktop}]` (empty) →
  `Neighbour(a, Right) == "desktop"`.
- **AC7** [mac] Floats ignored. Rows = the enumeration of
  `FocusLayout(Desktop{desktop}[X{s}[a, b]])`, plus an appended row
  `("f", new LayoutEntry(PanelNode.ForWindow("f"), new Rect(0.5, 0.25, 0.5, 0.5), "desktop", null, false))`.
  `Neighbour(rows, "a", Right) == "b"`. With f's `Parent` changed to
  `"other"` (and b's entry removed), the result is `"f"`. This shows that B4
  alone excluded f.
- **AC8** [mac] Not adjacent: `X{s}[a, b, c]`:
  - `Neighbour(a, Right) == "b"`. c is not adjacent to a.
    `Score(a, range:["s","b","c"], Right, true) = ln 2 + 0.05`.
  - `Neighbour(range:["s","a","b"], Right) == "c"`, with score `ln 2`.
    `range:["s","b","c"]` is not adjacent to it, and a and b are excluded as
    descendants.
- **AC9** [mac] Tolerances (hand-built rows; current `"cur"` = (0,0,0.5,1),
  parent `"p"`; candidate `"k"`, parent `"p"`; direction Right):
  - k at (0.5000005, 0, 0.5, 1) → `"k"`; at (0.500002, 0, 0.5, 1) → `null`;
    at (0.4999995, 0, 0.5, 1) → `"k"` (absolute difference).
  - k at (0.5, 0, 0.5, 1e-6) (overlap exactly 1e-6) → `null`; at
    (0.5, 0, 0.5, 2e-6) → `"k"`.
  - k at (0.5, 1, 0.5, 1) (corner contact) → `null`.
- **AC10** [mac] Root current: for `X{s}[a, b]` and for
  `Desktop{desktop}[X{s}[a, b]]`, `Neighbour(rootId, d) == null` for all four
  directions.

### Rules

- **AC11** [mac] Unknown id `"zzz"` → `null`. `LayoutEntries.Empty` with any id
  → `null`. Null `entries`, `rows` or `currentId` → `ArgumentNullException`.
  `(Edge)4` → `ArgumentOutOfRangeException` from `Neighbour`, `IsAdjacent` and
  `Score`.
- **AC12** [mac] Occupied desktop skipped (B6): hand-built rows with current
  `"a"` (0,0,.5,1) parent `"s"`, and `"d2"` = `new DesktopNode(PanelNode.ForWindow("z"), "d2")`
  at (.5,0,.5,1) parent `"s"`, followed by `"z"` at the same rect with parent
  `"d2"`. `Neighbour(rows, "a", Right) == "z"`. Without the `"z"` row → `null`.
- **AC13** [mac] Nested default desktop (E4): `X{s}[a, Desktop{desktop}[b]]`,
  `Neighbour(a, Right) == null`.
- **AC14** [mac] Entry with `Parent == null` is not excluded by B4:
  hand-built rows `"cur"` (0,0,.5,1) parent `"p"`, `"k"` (.5,0,.5,1) parent
  `null` → `Neighbour(rows, "cur", Right) == "k"`.
- **AC15** [mac] Score helper: `Score((0,0,.5,.5), (0,.5,.25,.5), Bottom, false) == ln 2 + 0.25`.
  The same rects with `isVirtual = true` give `+ 0.05`. For Left/Right the
  offset uses Y centres divided by `C.Height`:
  `Score((0,0,.5,1), (.5,0,.5,.5), Right, false) == ln 2 + 0.25`.
- **AC16** [mac] `IsAdjacent` covers all four directions: with
  C = (0.25,0.25,0.5,0.5) and one tile on each side
  (Left (0,0.25,0.25,0.5), Right (0.75,0.25,0.25,0.5), Top (0.25,0,0.5,0.25),
  Bottom (0.25,0.75,0.5,0.25)), each tile is adjacent only in its own direction
  (16 checks: 4 true, 12 false).
- **AC17** [mac] Degenerate rects (E5), hand-built rows, direction Right, no
  exception in any case:
  - current `"cur"` (0,0,0,1), candidate `"k"` (0,0,.5,1) → `null`.
  - current `"cur"` (0,0,.5,1), candidate `"k"` (.5,0,0,1) (zero width;
    touches and overlaps) → `null`, and `Score` for it is `+∞`.
  - adding a third row `"k2"` (.5,0,.5,1) after `"k"` → `"k2"`.
- **AC18** [mac] Overload equivalence (B15): for every table and query in AC1–AC10
  built through `FocusLayout`, `Neighbour(entries, …) == Neighbour(entries.AsEnumerable(), …)`.
  With duplicate rows `"cur"` twice (first at (0,0,.5,1), second at
  (.5,0,.5,1)) and `"k"` at (.5,0,.5,1), `Neighbour(rows, "cur", Right) == "k"`.
  This shows that the first row is the current entry and that duplicate rows
  of the current key are never candidates.
- **AC19** [mac] No memory (B12): in AC2, calling `Neighbour(a, Right)` before
  or after any other queries always gives `"t"`, and `Neighbour(t, Left)`
  gives `"a"`. The table is unchanged afterwards (structurally equal to a
  freshly computed `FocusLayout`).
- **AC20** [mac] Scale is ignored (B12): rebuilding every row of AC3 with
  `Scale = 0.5` gives the same result.

### Property test

- **AC21** [mac] Seeded property test, with `new Random(seed)` for seeds 1–200.
  Generate a tree of depth ≤ 4 with unique ids. Use random X/Y splits of 2–4
  children with weights in [0.1, 3], panels, empty desktops as leaves, and an
  optional `Desktop{desktop}` root. Build `FocusLayout(root, null)`. For every
  key as current and every direction, with `r = Neighbour(…)`:
  1. `r` is null or a key of the table, and `r != current`.
  2. If non-null: `Parent(r) != "desktop"`; r's entry is not an occupied
     desktop; neither of r and current subtree-contains the other's id;
     `IsAdjacent(C, R, d)` is true.
  3. Minimality: let *eligible* = rows passing B3–B8, computed independently
     in the test. If non-null, `Score(r)` is finite, `Score(r) <= Score(x)`
     for every eligible x, and no eligible row **before** r in table order has
     `Score == Score(r)`. If null, no eligible row has a score `< +∞`.
  4. Determinism: a second call, and a call on a freshly recomputed table
     for a structurally equal tree, return the same value.

## Assumptions

- **A1. Literal desktop-parent exclusion.** B4 compares `Parent` with the
  literal `DesktopNode.DefaultId` (`"desktop"`), as the findings record. Our
  solver sets a desktop child's `Parent` to the desktop's own id. So for a
  default-id desktop its direct child is excluded, and for a desktop with
  another id it is not. This is harmless for a root desktop (its child
  contains every other node), but it gives E4/AC13 for a nested default
  desktop. Chosen: keep the literal rule for fidelity. *Review:* whether
  nested default-id desktops can occur in our trees, and whether the quirk is
  acceptable.
- **A2. Float entries (future #22).** Our `FocusLayout` table has no float
  entries today, so B4 currently only affects desktop children. Chosen
  contract for whichever feature adds floats to the table: each float
  row must have `Parent = DesktopNode.DefaultId`, `Virtual = false`, and be
  appended **after** the tree and range rows. Then B4 excludes floats
  without any change to this function. If floats are instead marked another
  way (e.g. a new flag), this spec must be updated. *Review.*
- **A3. Rows overload instead of changing `LayoutEntries`.** `LayoutEntries`
  has no public constructor or factory (its builder is internal and there is
  no `InternalsVisibleTo`). To test floats, tolerances and degenerate rects
  without changing an existing type, the spec adds the
  `IEnumerable<KeyValuePair<string, LayoutEntry>>` overload, with first-match
  current and duplicate-tolerant scanning (B15). The alternative is a public
  `LayoutEntries` factory, which would change `specs/layout-solver.md`.
  *Review* if a factory is preferred (the floating feature may want one
  anyway).
- **A4. Weights-mode table.** Findings rule 1 says the navigator uses the
  weights-mode table (no metrics). The function accepts any table, and the
  Engine (#13) must pass `FocusLayout(root, null)` (e.g. via the default
  `LayoutCache`) plus floats.
- **A5. Argument handling.** Null `currentId` throws, matching the project
  convention for non-nullable references. Undefined `Edge` values throw
  `ArgumentOutOfRangeException`. The studied app has no such inputs.
- **A6. `Edge` reused as direction.** The existing `Edge` enum (docking) is
  reused unchanged. Its XML doc talks about insertion sides, but the raw
  values match the findings' direction codes. No new enum is added.
- **A7. Lazy row validation.** In the rows overload, a null key or value
  throws when reached. A table whose current row is never found therefore
  still throws if a null row exists before the end. Chosen for simplicity.
  Solver tables never contain null rows.
- **A8. Return value is the table key.** For solver tables `key == entry.Node.Id`.
  For hand-built rows the key is authoritative for B3, B5(b) and the result.

## Traceability

| Spec | Findings rule / example |
|---|---|
| B1, E7, A5 | — (C# argument conventions) |
| B2, E1, AC11 | 4 |
| B3 | 5 |
| B4, A1, A2, E4, AC7, AC13, AC14 | 6, example 7 |
| B5, AC5 (ranges of t), AC8 | 7, 8 |
| B6, E3, AC6, AC12 | 9, example 6 |
| B7, B8, AC9, AC16 | 10, 11, example 9 |
| B9, AC15, AC3, AC4 | 12, examples 3–4 |
| B10, B11, AC5 | 13, 14, example 5 |
| B12, AC19, AC20 | 17, 18 |
| B13, AC10 | 2, 19, example 10 |
| B14, A4 | 1 |
| B15, AC18, A3 | — (API design) |
| B16, AC21 | 13, 17, 18 |
| E5, AC17 | Unknowns (degenerate rects) |
| AC1, AC2, AC8 | examples 1, 2, 8 |
| Out of scope | 15, 16, 20 (Engine/camera #13/#15) |
