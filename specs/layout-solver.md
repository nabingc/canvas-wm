# Spec: layout-solver

## Goal

Turn one layout tree (from `specs/layout-tree.md`) into an **ordered table of
entries**, one per node id, giving each node a rectangle as a fraction of the
**unit square** `(0, 0, 1, 1)`. Two modes:

- **weights mode**: each split child gets its weight share;
- **minimum-aware mode**: given the size (in DIPs) the unit square stands for
  and a caller-supplied `min(node, axis)` function, space is re-distributed so
  no child falls below its minimum; when minimums cannot fit, proportions are
  kept and a shrink factor (`Scale`) is recorded for the descendants instead
  of overflowing.

Plus: the `Distribute` pinning algorithm, **range entries** (virtual groups of
2+ adjacent siblings, focus-layout variant only) with an exact id format, and
a small **caller-owned memoisation cache** (capacity 6).

Out of scope (separate queue items): the minimum provider and its constants
(#17 `layout-metrics`, findings rules 31–34), seam drag bounds
(#18 `seam-drag-bounds`), floating-panel entries (findings rule 37, floating
feature), mapping unit-square rects to screen/work-area coordinates, gaps,
pixel snapping, and whatever the engine does with `Scale < 1`.

## Placement

- Project: `src/CanvasWm.Core` (`net10.0`, BCL only; no new package or project
  references).
- Namespace: `CanvasWm.Core.Layout` (all new types). Uses
  `CanvasWm.Core.Geometry.Rect` / `Size` unchanged.
- Suggested files: `src/CanvasWm.Core/Layout/LayoutEntry.cs`,
  `LayoutEntries.cs`, `LayoutMetrics.cs`, `LayoutSolver.cs`, `LayoutCache.cs`.
- Tests: `tests/CanvasWm.Core.Tests`, suggested files
  `Layout/LayoutSolverWeightsTests.cs`, `Layout/LayoutSolverMinimumTests.cs`,
  `Layout/DistributeTests.cs`, `Layout/RangeEntryTests.cs`,
  `Layout/LayoutCacheTests.cs`.
- Depends on: `specs/solution-skeleton.md` (conventions, XML docs required),
  `specs/layout-tree.md` (`LayoutNode`, `SplitNode`, `PanelNode`,
  `DesktopNode`, `Axis` / `AxisExtensions.Cross`, `LayoutDocument` structural
  equality, `LayoutNormalizer.MissingWeight` / `BadWeightReplacement`,
  `Rect`, `Size`). **No existing type is changed.**

## Public API

All types public, documented with XML docs; string comparisons **ordinal**.

```csharp
namespace CanvasWm.Core.Layout;

/// <summary>One row of a layout result.</summary>
/// <param name="Node">The node laid out. In minimum-aware mode a split's entry carries a
/// rebuilt SplitNode with effective weights (B13); a range entry carries a synthetic split (B19).</param>
/// <param name="Rect">Rectangle as fractions of the unit square (y-down, origin top-left).</param>
/// <param name="Parent">Id of the parent node; null for the root.</param>
/// <param name="Scale">Accumulated shrink factor in minimum-aware mode; null in weights mode
/// and for range entries.</param>
/// <param name="Virtual">True only for range entries.</param>
public sealed record LayoutEntry(LayoutNode Node, Rect Rect, string? Parent, double? Scale, bool Virtual);
// Node null → ArgumentNullException. Compiler-generated equality (Node uses its structural Equals).

/// <summary>Immutable, insertion-ordered map from node id to entry.</summary>
public sealed class LayoutEntries : IReadOnlyDictionary<string, LayoutEntry>
{
    /// <summary>The empty table.</summary>
    public static LayoutEntries Empty { get; }
    public int Count { get; }
    /// <exception cref="KeyNotFoundException">id not present.</exception>
    /// <exception cref="ArgumentNullException">id is null.</exception>
    public LayoutEntry this[string id] { get; }
    public bool ContainsKey(string id);
    public bool TryGetValue(string id, [MaybeNullWhen(false)] out LayoutEntry value);
    /// <summary>Ids in insertion order.</summary>
    public IEnumerable<string> Keys { get; }
    /// <summary>Entries in insertion order.</summary>
    public IEnumerable<LayoutEntry> Values { get; }
    /// <summary>Enumerates (id, entry) pairs in insertion order.</summary>
    public IEnumerator<KeyValuePair<string, LayoutEntry>> GetEnumerator();
}
// No public constructor or mutators; the solver builds instances through an internal builder.
// (A later feature, e.g. floating, may add an additive API; that is not part of this spec.)

/// <summary>Inputs for minimum-aware solving.</summary>
/// <param name="Width">Width, in DIPs, that the unit square stands for. Finite, ≥ 0.</param>
/// <param name="Height">Height, in DIPs, that the unit square stands for. Finite, ≥ 0.</param>
/// <param name="Min">Minimum extent, in DIPs, of a node along an axis. Supplied by the caller
/// (the minimum provider is feature #17).</param>
public sealed record LayoutMetrics(double Width, double Height, Func<LayoutNode, Axis, double> Min);
// Width/Height NaN, ±∞ or < 0 → ArgumentOutOfRangeException; Min null → ArgumentNullException.

public static class LayoutSolver
{
    /// <summary>(0, 0, 1, 1): the rect the root is laid out in.</summary>
    public static Rect UnitRect { get; }

    /// <summary>Pin tolerance of Distribute: 1e-9.</summary>
    public const double PinTolerance = 1e-9;

    /// <summary>Lays out the tree: weights mode when metrics is null, minimum-aware mode
    /// otherwise. No range entries. Null root → empty table.</summary>
    public static LayoutEntries LayoutRects(LayoutNode? root, LayoutMetrics? metrics = null);

    /// <summary>LayoutRects plus range entries for every split with ≥ 3 children (B18–B20).</summary>
    public static LayoutEntries FocusLayout(LayoutNode? root, LayoutMetrics? metrics = null);

    /// <summary>Iterative pinning: splits total among items in proportion to weights while
    /// giving every item at least its minimum where possible (B16–B17).</summary>
    /// <exception cref="ArgumentNullException">weights or mins is null.</exception>
    /// <exception cref="ArgumentException">weights.Count != mins.Count.</exception>
    public static ImmutableArray<double> Distribute(double total, IReadOnlyList<double> weights,
                                                    IReadOnlyList<double> mins);

    /// <summary>Id of a range entry: range:[ + quoted items joined by "," + ] (B21).</summary>
    /// <exception cref="ArgumentNullException">splitId, childIds or any child id is null.</exception>
    public static string RangeId(string splitId, IEnumerable<string> childIds);
}

/// <summary>Caller-owned most-recently-computed cache of layout results keyed by
/// structural document equality. Not thread-safe.</summary>
public sealed class LayoutCache
{
    /// <summary>Maximum number of documents kept: 6.</summary>
    public const int Capacity = 6;

    /// <summary>Cache whose compute function is doc => LayoutSolver.FocusLayout(doc.Root, null).</summary>
    public LayoutCache();

    /// <summary>Cache with a custom compute function (used by tests and by later features that
    /// add entries, e.g. floating panels).</summary>
    /// <exception cref="ArgumentNullException">compute is null.</exception>
    public LayoutCache(Func<LayoutDocument, LayoutEntries> compute);

    /// <summary>Returns the cached result for a structurally equal document, else computes,
    /// caches and returns it (B24–B27).</summary>
    /// <exception cref="ArgumentNullException">document is null.</exception>
    /// <exception cref="InvalidOperationException">compute returned null.</exception>
    public LayoutEntries GetOrCompute(LayoutDocument document);
}
```

Internal (not public API; named only so rules can refer to them):
`LayoutByWeights(node, rect, parent, builder)` and
`LayoutWithMinimums(node, rect, size, scale, parent, metrics, builder)`.

## Behaviour

Notation: for a split, *along* = the split axis, *cross* = `Axis.Cross()`. For
a `Rect r`, origin/extent along X are `r.X`/`r.Width`, along Y `r.Y`/`r.Height`.
For a `Size s`, extent along X is `s.Width`, along Y `s.Height`.

### Entry table

- **B1. Ordered map.** The builder keeps insertion order. Setting an id that
  is not present appends it. Setting an id that is already present
  **replaces the entry in place** (position unchanged; A3). `Keys`, `Values`
  and enumeration all follow that order. Id comparison is ordinal.
- **B2. Pre-order.** A node's own entry is set before any of its descendants'
  entries; children are visited in list order. (In minimum mode a split's
  entry is computed after its distribution, but still set before its
  children's entries.)
- **B3. Root.** The root is laid out in `UnitRect` with `Parent = null`. A
  null root produces an empty table. No gap, padding, inset or tab strip is
  subtracted anywhere in the solver; no rounding or snapping; all arithmetic
  is `double`.
- **B4. Coordinates.** Unit-square space is **y-down, origin top-left**, as in
  the rest of Core. Axis X lays children left→right (first child at the
  smallest x); axis Y lays them top→bottom (first child at the smallest y).

### Child weights used by the solver

- **B5. Effective raw weight.** For child `i` of a split, the solver uses
  `w_i = Weights[i]` if `i < Weights.Length`, else
  `LayoutNormalizer.MissingWeight` (1.0); then, if
  `!(w_i > 0 && double.IsFinite(w_i))`, `w_i = LayoutNormalizer.BadWeightReplacement`
  (0.001). Extra weights beyond `Children.Length` are ignored. For a
  normalised tree (layout-tree B14) this is just `Weights[i]`. (A4)

### Weights mode (`LayoutByWeights`)

- **B6.** Every node sets its entry `(Node = the node itself, Rect = given
  rect, Parent = given parent, Scale = null, Virtual = false)`.
- **B7. Panel** — leaf, nothing more.
- **B8. Desktop** — if `Child` is non-null, the child is laid out in the
  **same** rect with `Parent = desktop.Id`. An empty desktop is a leaf.
- **B9. Split** — with `n` children: if `n == 0`, nothing more. Otherwise
  `S = Σ w_i` (B5; raw sum, not assumed to be 1), `f_i = w_i / S`. Keep a
  running fraction `acc` starting at 0. For child `i` in order: along the
  split axis its rect has origin `origin + extent × acc` and extent
  `extent × f_i`; across the axis it has the split's own origin and extent;
  then `acc += f_i`. `Parent = split.Id`. Child offsets are always computed
  from the running fraction, never from previous children's rects.

### Minimum-aware mode (`LayoutWithMinimums`)

- **B10. Root call.** `rect = UnitRect`, `size = Size(metrics.Width,
  metrics.Height)`, `scale = 1.0`, `parent = null`.
- **B11. Every node** sets its entry with the given rect and parent,
  `Scale = incoming scale` (always non-null), `Virtual = false`. `Node` is the
  node itself, except for splits (B12, B13). **Panel**: leaf. **Desktop**:
  child (if any) laid out with the same rect, same size and same scale,
  `Parent = desktop.Id`.
- **B12. Split, minimums and shrink factor.** Let `n` = number of children.
  0. If `n == 0`: the entry stores the original node; nothing more (A8).
  1. **Degenerate size guard (A7):** if `size.Width <= 0` or
     `size.Height <= 0`, the split and its whole subtree are laid out by the
     weights-mode rules B6–B9 except that every entry in the subtree gets
     `Scale = incoming scale`; the split's entry stores the original node;
     `Min` is not called anywhere in this subtree. Stop here.
  2. For each child `c` in order: `mA_i = clamp(Min(c, along))`,
     `mC_i = clamp(Min(c, cross))`, where
     `clamp(m) = (double.IsFinite(m) && m > 0) ? m : 0` (A6). `Min` is called
     exactly once per (child, axis) for each split visit — `2n` calls — and
     never for the root or for a node that is not a child of the split being
     visited. Call order is unspecified.
  3. `ΣA = Σ mA_i`; `maxC = max_i mC_i`.
  4. `alongPx` = `size` extent along the split axis; `crossPx` = the other.
     `k = 1`; if `ΣA > 0`, `k = min(k, alongPx / ΣA)`; if `maxC > 0`,
     `k = min(k, crossPx / maxC)`.
  5. If `k < 1`: `alongPx ← alongPx / k`, `crossPx ← crossPx / k`. The rect is
     **not** changed.
- **B13. Split, distribution.** `sizes = Distribute(alongPx, w, mA)` with `w`
  from B5. Effective weights `e_i = sizes_i / alongPx`. The split's entry
  stores a **rebuilt** `new SplitNode(split.Id, split.Axis, e, split.Children)`;
  the input node (and so the document) is not modified.
- **B14. Split, children.** As B9 but with fractions `e_i` in place of `f_i`
  (running fraction `acc += e_i`). Child `i` receives size `sizes_i` along the
  split axis and `crossPx` across it — i.e. for axis X `Size(sizes_i, crossPx)`,
  for axis Y `Size(crossPx, sizes_i)` — and `scale = incoming scale × k`,
  `Parent = split.Id`.
- **B15. Consequences.** Rects in minimum mode tile the parent exactly (up to
  floating-point slack) because step 5 guarantees `ΣA ≤ alongPx`; minimums
  change only proportions, and an impossible fit changes only descendants'
  `Scale`. Shrink factors compound: a nested split computes its own `k` from
  the (possibly enlarged) size it was given and multiplies it in. A split's
  own entry carries the incoming scale, not its own `k`.

### `Distribute(total, weights, mins)`

- **B16.** `n = weights.Count`; `n == 0` → empty array. Start with an empty
  pinned set. Repeat:
  - free = indices not pinned, in order;
  - `remaining = total − Σ mins[p]` over pinned `p`;
  - `Σw = Σ weights[i]` over free `i`;
  - size of pinned `i` = `mins[i]`; size of free `i` =
    `remaining × weights[i] / Σw` if `Σw > 0`, else `remaining / |free|`;
  - violators = free `i` with `size_i < mins[i] − PinTolerance`;
  - no violators → return sizes; otherwise pin **all** violators and repeat.
- **B17.** Each round pins ≥ 1 index, so at most `n + 1` rounds. If all
  indices are pinned the result is `mins` (may exceed `total`). There are no
  maximum sizes. `weights` and `mins` are not modified; values are used raw
  (no clamping, no B5 replacement inside `Distribute`; A13).

### Range entries (`FocusLayout` only)

- **B18.** `FocusLayout(root, metrics)` = `LayoutRects(root, metrics)`, then a
  snapshot of the entries (in order) is scanned; for every snapshot entry
  whose `Node` is a `SplitNode` with `Children.Length >= 3`, range entries are
  appended for each contiguous run `[s, e)` with `e − s >= 2` except the full
  run `[0, n)`. Order: `s = 0 … n−2`; for each `s`, `e = s+2 … n`, skipping
  `e = n` when `s = 0`. A split with `n` children yields `n(n−1)/2 − 1`
  ranges. Range entries are not themselves scanned. Ranges of different
  splits appear in snapshot order of their splits, all after every
  non-range entry.
- **B19. Range entry contents.** Using the split's **entry node** `E`
  (effective weights in minimum mode; the original split in weights mode):
  - `Id = RangeId(E.Id, ids of E.Children[s..e))`;
  - `Node = new SplitNode(Id, E.Axis, r, E.Children[s..e))` where
    `r_j = w_j / Σ_{j in run} w_j` and `w_j` is `E`'s B5 weight of child `j`;
  - `Rect`: let `F` / `L` be the current entries' rects for the run's first /
    last child id (a missing entry counts as `Rect(0,0,0,0)`); rect =
    `(F.X, F.Y, L.X + L.Width − F.X, L.Y + L.Height − F.Y)`;
  - `Parent = E.Id`, `Scale = null`, `Virtual = true`;
  - set via B1 (so an id collision replaces in place).
- **B20.** `LayoutRects` never produces range entries; for the same inputs,
  `LayoutRects` equals `FocusLayout` with the virtual entries removed, in the
  same order.
- **B21. `RangeId`.** Items = `splitId` followed by `childIds` in order. Each
  item is escaped by replacing `\` with `\\` first, then `"` with `\"`, and
  wrapped in `"`. Result = `range:[` + items joined by `,` + `]`. Example
  (C# literal): `RangeId("split-k3", ["a","b"]) == "range:[\"split-k3\",\"a\",\"b\"]"`.

### Purity and determinism

- **B22.** All `LayoutSolver` functions are pure: no static mutable state, no
  I/O, inputs never modified, a new `LayoutEntries` per call (except possibly
  the shared `Empty`). Same inputs (and a deterministic `Min`) → equal
  results.
- **B23.** Minimum mode is chosen exactly when `metrics` is non-null; weights
  mode has no `Min` to call and produces `Scale == null` everywhere.

### `LayoutCache`

- **B24.** The cache holds an ordered list of `(document, result)` pairs, most
  recently **computed** first, initially empty.
- **B25. Lookup.** Linear scan from the front using `LayoutDocument.Equals`
  (structural: tree, floating, hidden, hidden desktop). Hit → return the
  stored result instance; the list order is **not** changed.
- **B26. Miss.** Call `compute(document)` exactly once; if it returns null
  throw `InvalidOperationException` and leave the cache unchanged. Otherwise
  insert `(document, result)` at the front; if the list then holds more than
  `Capacity` (6) pairs, drop the last one. Return the result.
- **B27. Unobservable.** Apart from instance identity of returned results and
  the number of `compute` calls, a cache is unobservable: results equal
  `compute(document)`. There is no explicit invalidation and no process-wide
  instance; each owner (e.g. the engine) creates its own. Not thread-safe
  (A10).

## Edge cases & errors

- **E1.** Null `root` → empty table (both functions, both modes).
- **E2.** `Distribute`: null lists → `ArgumentNullException`; different
  lengths → `ArgumentException`; `total` and element values are not
  validated (NaN in → NaN out is acceptable).
- **E3.** `LayoutMetrics`: invalid `Width`/`Height` → `ArgumentOutOfRangeException`;
  null `Min` → `ArgumentNullException`. `Min` may return any `double`; B12.2
  clamps. Exceptions thrown by `Min` propagate unchanged.
- **E4.** Non-normalised trees are accepted: short/long weight lists, bad
  weights (B5), splits with 0 or 1 child (0 → own entry only; 1 → the child
  fills the split's rect), same-axis nested splits (laid out as nested, not
  flattened), nested desktops.
- **E5.** Duplicate ids in a tree: later entries replace earlier ones in
  place (B1); `Count` is then less than the node count. Not detected.
- **E6.** A range id equal to some node id replaces that entry in place
  (B1, B19). Not detected.
- **E7.** If all indices end up pinned (only through floating-point slack in
  the solver), effective weights may sum slightly above 1; no correction.
- **E8.** `RangeId`, `GetOrCompute`, the `LayoutCache` constructor and the
  `LayoutEntries` lookup members throw `ArgumentNullException` for null
  arguments.

## Acceptance criteria

Notation (as findings): `X{s}[...]` / `Y{s}[...]` = split with id `s`;
letters = `PanelNode.ForWindow(letter)`; weights in parentheses; rects as
`(x, y, w, h)`. Unless stated, rect and weight comparisons use absolute
tolerance `1e-12` and DIP sizes `1e-9`. All criteria are xUnit tests in
`tests/CanvasWm.Core.Tests` with no OS access.

### Entry table and weights mode
- **AC1** [mac] `LayoutEntries.Empty.Count == 0`; `LayoutRects(null)` and
  `FocusLayout(null)` (with and without metrics) have `Count == 0`;
  `TryGetValue("x", out _)` is false and the indexer throws
  `KeyNotFoundException`; indexer/`ContainsKey`/`TryGetValue` with null throw
  `ArgumentNullException`. (B3, E1, E8)
- **AC2** [mac] `FocusLayout(X{s}[a(0.5), b(0.5)])` → exactly `s`
  (0,0,1,1) parent null; `a` (0,0,0.5,1) parent `s`; `b` (0.5,0,0.5,1) parent
  `s`; all `Scale == null`, `Virtual == false`; `Count == 3`; `s`'s `Node` is
  the input instance (`ReferenceEquals`). (Ex. 1, B6, B9)
- **AC3** [mac] `Y{s}[a(1), b(3)]` → `a` (0,0,1,0.25), `b` (0,0.25,1,0.75).
  (Ex. 2, B4, B9)
- **AC4** [mac] `X{s}[a(0.25), Y{t}[b(0.5), c(0.5)](0.75)]` → `a`
  (0,0,0.25,1); `t` (0.25,0,0.75,1) parent `s`; `b` (0.25,0,0.75,0.5) parent
  `t`; `c` (0.25,0.5,0.75,0.5) parent `t`; `Keys` order exactly
  `s, a, t, b, c`; `Values` and enumeration follow the same order. (Ex. 3, 5,
  B1, B2)
- **AC5** [mac] `Desktop{desktop}[X{s}[a(0.5), b(0.5)]]` → `desktop`
  (0,0,1,1) parent null; `s` (0,0,1,1) parent `desktop`; `a`, `b` parent `s`.
  A lone empty `DesktopNode` → one entry (0,0,1,1); a lone panel `a` → one
  entry (0,0,1,1) parent null. (Ex. 4, B7, B8)
- **AC6** [mac] Duplicate ids: `X{s}[P1, P2]` where both panels have id `"p"`
  (views `[a]` and `[b]`) → `Count == 2`, `Keys == [s, p]`, `p`'s entry `Node`
  equals `P2` with rect (0.5,0,0.5,1). (B1, E5)
- **AC7** [mac] Robust weights: `X{s}[a, b, c]` with `Weights = [1]` → widths
  `1/3` each; `X{s}[a(0), b(1)]` → `a` width `0.001/1.001`;
  `X{s}[a(NaN), b(1), c(2)]` plus an extra 4th weight `5` → widths
  `0.001/3.001, 1/3.001, 2/3.001`; a split with 0 children → only its own
  entry; a split with one child `a` → `a` (0,0,1,1). No exceptions. (B5, B9, E4)

### Range entries
- **AC8** [mac] `FocusLayout(X{s}[a(0.2), b(0.3), c(0.5)])` → keys in order
  `s, a, b, c, range:["s","a","b"], range:["s","b","c"]` (`Count == 6`).
  `range:["s","a","b"]`: `Node` is a `SplitNode` with that `Id`, axis X,
  children `[a, b]`, weights `[0.4, 0.6]`, rect (0,0,0.5,1), parent `s`,
  `Virtual == true`, `Scale == null`. `range:["s","b","c"]`: children
  `[b, c]`, weights `[0.375, 0.625]`, rect (0.2,0,0.8,1). No key
  `range:["s","a","b","c"]`. (Ex. 6, B18, B19)
- **AC9** [mac] 4 children `a,b,c,d` (0.25 each) under `X{s}` → range keys in
  order `[a,b]`, `[a,b,c]`, `[b,c]`, `[b,c,d]`, `[c,d]`; `[a,b,c]` rect
  (0,0,0.75,1). Under `Y{s}` the `[b,c]` rect is (0,0.25,1,0.5). (Ex. 7, B4,
  B18)
- **AC10** [mac] Range counts: 3/4/5 children → 2/5/9 range entries; a
  5-child split's table has `Count == 1 + 5 + 9` (ranges are not expanded).
  `X{s}[a(0.5), Y{t}[b, c, d](0.5)]` → exactly 2 range entries (`t` has 3
  children), both with parent `t`, appended after all 6 node entries
  (`s, a, t, b, c, d`); `s` (2 children) yields none. (B18)
- **AC11** [mac] `RangeId("s", ["a\"b\\c"])` (the child id is the 5-character
  text `a"b\c`) returns the text `range:["s","a\"b\\c"]`, i.e. the C# literal
  `"range:[\"s\",\"a\\\"b\\\\c\"]"`;
  `RangeId("split-k3", ["a","b"]) == "range:[\"split-k3\",\"a\",\"b\"]"`;
  `RangeId("s", [])` returns `range:["s"]`; null `splitId`, null list or null
  element → `ArgumentNullException`. (Ex. 8, B21, E8)
- **AC12** [mac] `LayoutRects` on AC8's tree → exactly `s, a, b, c`. For the
  AC8, AC9, AC10 and AC22 trees (with and without the AC22 metrics),
  `LayoutRects` equals `FocusLayout` with `Virtual` entries removed: same
  keys, same order, equal entries. (Ex. 9, B20)

### Distribute
- **AC13** [mac] `(1000, [0.5,0.5], [100,100])` → `[500, 500]`;
  `(1000, [0.8,0.2], [0,300])` → `[700, 300]`;
  `(1000, [0.6,0.2,0.2], [0,300,300])` → `[400, 300, 300]`;
  `(1000, [0.5,0.3,0.2], [0,250,450])` → `[300, 250, 450]`. (Ex. 10–13, B16)
- **AC14** [mac] Overflow `(500, [0.5,0.5], [300,300])` → `[300, 300]`;
  tolerance `(1000, [0.5,0.5], [500.0000000005, 0])` → `[500, 500]` (within
  1e-9; index 0 not pinned); zero free weights `(100, [0,0], [0,0])` →
  `[50, 50]`; `(42, [], [])` → empty. (Ex. 14–17, B16, B17)
- **AC15** [mac] `Distribute` with null weights or null mins →
  `ArgumentNullException`; lengths 2 vs 3 → `ArgumentException`; input arrays
  are unchanged after a call. (E2, B17)

### Minimum-aware mode
Metrics `W = 1000, H = 600` and `Min` from a lookup table (unlisted
(node, axis) pairs → 0) unless stated.
- **AC16** [mac] `X{s}[a(0.5), b(0.5)]`, min x `a = b = 300`, min y
  `a = b = 200` → `a` (0,0,0.5,1), `b` (0.5,0,0.5,1); `Scale == 1.0` on `s`,
  `a`, `b`; `Virtual == false`. (Ex. 18, B10–B14)
- **AC17** [mac] `X{s}[a(0.8), b(0.2)]`, min x `[0, 300]` → `a` (0,0,0.7,1),
  `b` (0.7,0,0.3,1); `entries["s"].Node` is a `SplitNode` with `Id == "s"`,
  axis X, the same children (each `ReferenceEquals` the input child) and
  weights `[0.7, 0.3]`; the input split still has weights `[0.8, 0.2]` and
  equals a deep copy taken before the call. (Ex. 19, B13)
- **AC18** [mac] `X{s}[a(0.5), b(0.5)]`, min x `[800, 800]` → rects as weights
  mode; `s` scale 1.0; `a`, `b` scale `0.625`. (Ex. 20, B12.4–5, B14)
- **AC19** [mac] Cross overflow: `X{s}[a(0.5), b(0.5)]`, min y `a = 900` →
  rects as weights mode; `a`, `b` scale `2/3`. (Ex. 21)
- **AC20** [mac] Nested `X{s}[a(0.5), Y{t}[b(0.5), c(0.5)](0.5)]`,
  min(a,x)=100, min(a,y)=100, min(t,x)=100, min(t,y)=800, min(b|c,x)=100,
  min(b|c,y)=400 → `s` scale 1.0; `a` (0,0,0.5,1) scale 0.75; `t`
  (0.5,0,0.5,1) scale 0.75; `b` (0.5,0,0.5,0.5) and `c` (0.5,0.5,0.5,0.5)
  scale 0.75 (so `t`'s own k is 1 — it received size (666.67, 800), not the
  unenlarged (500, 600), which would give k = 0.75 and scale 0.5625).
  (Ex. 22, B14, B15)
- **AC21** [mac] Compounding: `X{s}[a(0.5), Y{t}[b(0.5), c(0.5)](0.5)]`,
  min x `a = t = 1000`, min y `b = c = 1000`, all others 0 → `s` scale 1.0;
  `a`, `t` scale 0.5; `b`, `c` scale `0.3` (= 0.5 × 1200/2000); rects as
  weights mode. (B15)
- **AC22** [mac] `FocusLayout(X{s}[a(0.6), b(0.2), c(0.2)], metrics)` with
  min x `[0, 300, 300]` → `a` width 0.4, `b`, `c` width 0.3;
  `range:["s","a","b"]` weights `[4/7, 3/7]`, rect (0,0,0.7,1);
  `range:["s","b","c"]` weights `[0.5, 0.5]`, rect (0.4,0,0.6,1); both range
  entries `Scale == null`, `Virtual == true`. (Ex. 23, B19)
- **AC23** [mac] `Min` call contract: with a recording `Min` on
  `X{s}[a, Y{t}[b, c]]` (no shrink), the multiset of calls is exactly
  `{(a,X),(a,Y),(t,X),(t,Y),(b,X),(b,Y),(c,X),(c,Y)}` — no call with `s`.
  For `Desktop{X{s}[a, b]}` the calls are exactly `{(a,X),(a,Y),(b,X),(b,Y)}`.
  With `metrics == null`, every entry has `Scale == null`. (B12.2, B23)
- **AC24** [mac] `Desktop{desktop}[X{s}[a(0.5), b(0.5)]]` with min x
  `a = b = 1000` → `desktop` and `s` scale 1.0, both (0,0,1,1); `a`, `b`
  scale 0.5. (B11)
- **AC25** [mac] Clamping: `X{s}[a(0.5), b(0.5)]` with `Min` returning `NaN`
  for `(a,X)`, `-50` for `(b,X)` and `+∞` for `(a,Y)` (others 0) → same result
  as all minimums 0: rects (0,0,0.5,1) / (0.5,0,0.5,1), all scale 1.0.
  (B12.2, A6)
- **AC26** [mac] Degenerate size: metrics `(0, 600)` on
  `X{s}[a(0.25), b(0.75)]` with min x 100 → rects (0,0,0.25,1) /
  (0.25,0,0.75,1), all scale 1.0, `entries["s"].Node` is the input instance,
  `Min` never called. `new LayoutMetrics(-1, 1, f)`, `(NaN, 1, f)`,
  `(1, +∞, f)` → `ArgumentOutOfRangeException`; `Min == null` →
  `ArgumentNullException`. (B12.1, E3, A7)
- **AC27** [mac] Property test over ≥ 300 normalised trees from a seeded
  `System.Random` (fixed seed; depth ≤ 4; fan-out 2–5; random axis; unique
  ids; optional desktop root), each solved in weights mode and in minimum mode
  with random `W, H ∈ [100, 4000]` and random table minimums in `[0, 1500]`:
  (a) every node id appears exactly once in `LayoutRects` and `Count` equals
  the node count; (b) for every split entry, its children's rects tile it:
  the first child starts at the split's origin along the axis, each next
  child starts where the previous ends, the last ends at the split's far
  edge, and every child has the split's cross origin/extent (tolerance
  1e-9); (c) in minimum mode every `Scale` is in `(0, 1]` and a child's scale
  ≤ its parent's; in weights mode every `Scale` is null; (d) the input tree
  equals a deep copy taken before the call; (e) two calls give equal
  results. (B2, B9, B14, B15, B22)

### Cache
- **AC28** [mac] With a counting `compute`: `GetOrCompute(d)` twice returns
  the same instance (`ReferenceEquals`) and `compute` ran once; a separately
  constructed document equal to `d` is also a hit. A default-constructed
  cache returns a result whose entries sequence-equal
  `FocusLayout(d.Root, null)`. (B25, B27)
- **AC29** [mac] Calling `GetOrCompute` for 7 distinct documents `d1 … d7`,
  then `d1` again, recomputes `d1` (compute count 8, a new instance) while a
  following `d7` is a hit (count stays 8). (Ex. 31, B26)
- **AC30** [mac] Hit does not reorder: the call sequence
  `d1, d2, d3, d4, d5, d6, d1, d7, d2, d1, d2` (distinct documents) yields
  compute counts after each call of `1,2,3,4,5,6,6,7,7,8,9`: the hit on `d1`
  does not protect it, so `d7` evicts `d1`; `d2` is still cached (same
  instance as its first result); re-inserting `d1` evicts `d2`. (B25, B26)
- **AC31** [mac] Two `LayoutCache` instances are independent (a document
  cached in one is a miss in the other). `GetOrCompute(null!)` →
  `ArgumentNullException`; `new LayoutCache(null!)` →
  `ArgumentNullException`. With a `compute` that returns null on its first
  call and a table on later calls: the first `GetOrCompute(d)` throws
  `InvalidOperationException`; the second calls `compute` again (count 2)
  and returns the table; the third is a hit (count stays 2). (B26, B27, E8)

### Architecture
- **AC32** [mac] `dotnet build CanvasWm.slnx` exits 0 with 0 warnings; the
  existing `CoreReferencesNoCanvasWmAssembly` test still passes;
  `CanvasWm.Core.csproj` has no `PackageReference`/`ProjectReference`; all
  layout-tree tests still pass (no existing type changed).

Count: 32 [mac] (AC1–AC32), 0 [windows].

## Assumptions

- **A1. Meaning of `Scale` (Inferred).** `Scale` is recorded only; this spec
  defines no consumer. Default reading: "this subtree was solved in a virtual
  space `1/Scale` times larger and would be displayed shrunk by `Scale`".
  Rects are never changed by it. What the engine does with `Scale < 1`
  (scaled preview, camera zoom-out, refusing the edit) is decided by the
  camera/engine features. *Needs human review.*
- **A2. Y-down (Inferred).** Unit space is y-down, origin top-left; the first
  child of a Y split is the top one. Matches Core's convention, so mapping to
  a work area is `x = area.X + area.Width × fx` etc. with no flip (done by a
  later feature). *Review*: confirm.
- **A3. Ordered-map semantics (Inferred).** Re-setting an existing id keeps
  its original position and replaces the value. Only observable with
  duplicate ids (E5) or range-id collisions (E6). *Needs human review*
  (alternative: remove and append at the end).
- **A4. Defensive weights (B5).** The studied app traps on a short weights
  list; CanvasWM instead reuses layout-tree's `Normalize` per-weight rule
  (missing → 1.0, bad → 0.001). No effect on normalised trees.
- **A5. Units.** `LayoutMetrics.Width/Height` and `Min` results are DIPs (the
  findings say "pixels"; Core works in DIPs, conversion to device pixels is
  Win32's job). The solver itself is unit-agnostic.
- **A6. `Min` clamping (deviation).** NaN, negative and ±∞ minimums are
  treated as 0. The study fed raw values (only a negative cross maximum was
  treated as 0). In particular **+∞ → 0** ("no constraint") rather than
  "impossible fit". *Needs human review.*
- **A7. Degenerate size guard (CanvasWM addition).** With a zero-width or
  zero-height usable area (findings rule 32 allows `max(0, …)`), the studied
  arithmetic would divide by zero and produce NaN rects. Default: such a
  subtree falls back to weights-mode rects with the incoming scale and no
  `Min` calls. *Needs human review.*
- **A8. Empty split in minimum mode** stores the original node and lays out
  nothing; only reachable with non-normalised input.
- **A9. Cache key = `LayoutDocument`, compute pluggable.** Findings rule 35
  keys by the whole document while floating-panel entries (rule 37) are out
  of scope; the default compute is `FocusLayout(doc.Root, null)` and the
  floating feature supplies its own `compute` via the second constructor
  without changing this spec. The cache serves weights mode only (metrics
  hold a delegate and cannot be keyed structurally), matching the study,
  where only metric-free arrangement was cached.
- **A10. Cache thread-safety.** Not thread-safe (the study's global cache had
  no lock); one cache per owner, used from one thread. `LayoutEntries`
  results are immutable and safe to share.
- **A11. Low-level recursions internal.** `LayoutByWeights` /
  `LayoutWithMinimums` are internal. If #18 seam-drag-bounds needs to solve a
  subtree with a given rect/size/scale, it should request an additive public
  overload (reported, not silently added).
- **A12. Range-entry id format kept exactly** (the findings say this matters
  only if ids are persisted/compared) because later features may persist
  focus targets.
- **A13. `Distribute` uses raw inputs.** No B5 replacement or clamping inside
  `Distribute`; the solver passes already-adjusted weights and clamped mins.

## Traceability

| Spec item | Findings rule / example |
|---|---|
| `LayoutEntry`, `LayoutEntries`, B1, AC1, AC4, AC6, A3 | 1, 2; Data model |
| B2, AC4 | 3; Ex. 5 |
| B3, B4, AC2–AC5, A2 | 4, 5, 6 |
| B5, AC7, A4 | 11 (trap note) |
| B6–B9, AC2–AC5 | 7–11; Ex. 1–4 |
| B10–B14, AC16–AC20, AC23, AC24 | 12–18; Ex. 18–22 |
| B15, AC21, AC27 | 19, 20 |
| B12.1, AC26, A7 | 32 (`max(0, …)`); Unknowns (metrics size) |
| B12.2 clamp, AC25, A6 | 15 (negative max → 0) |
| B16, B17, AC13–AC15, A13 | 21–27; Ex. 10–17 |
| B18–B20, AC8–AC10, AC12, AC22 | 28–30; Ex. 6, 7, 9, 23 |
| B21, AC11, A12 | 29; Ex. 8; Windows notes (range ids) |
| B22, B23, AC23, AC27 | 6, 30, 36 |
| B24–B27, AC28–AC31, A9, A10 | 35, 36; Ex. 31; Unknowns (thread-safety) |
| A1 | 16 meaning (Inferred); Unknowns (`scale < 1` downstream) |
| A5 | 12; Windows notes (DIPs) |
| A11 | Scope note (seam drag bounds) |
| Out of scope | 31–34 and Ex. 24–30 (#17), 37 (floating), seam drag (#18) |
| AC32 | CLAUDE.md architecture; specs/solution-skeleton.md |
