# Spec: docking

## Goal

Pure tree edits that move a tab group (panel) to a new place in one display's
layout document:

- **remove** any node from a tree, in two variants that differ only in who
  inherits the freed space (`shareRemovedSpace`: true = all siblings in
  proportion, false = the adjacent sibling), plus a document-level
  **remove panel** (tree, floating or hidden);
- **insert beside** a target node on one of its four edges;
- the **seam** snapshot (`SplitSeam`), building a seam target from a split
  divider, and **insert at a seam**;
- **dock**: find (or create) the window's panel, remove it, insert it beside a
  target or at a seam, and fall back to "right of / below the whole tree" so
  that it never fails.

Every structural edit ends with `LayoutNormalizer.Normalize`
(`specs/layout-tree.md`), which is what turns "dock right of a tile that is
already in a row" into a new column of that row.

Out of scope (queued separately): slot-restore / redock / remembered spots
(#19), tab grouping (#20), drop-target hit testing (#21), new-window placement
(#4). The study's `boundary` field of a dock target is not modelled (A1).

## Placement

- Project: `src/CanvasWm.Core` (`net10.0`, BCL only; no new package or project
  references).
- Namespace: `CanvasWm.Core.Layout` (all new types).
- Suggested files: `src/CanvasWm.Core/Layout/Edge.cs`, `SplitSeam.cs`,
  `DockTargetSpec.cs`, `LayoutEdits.cs`, `Docking.cs`.
- Tests: `tests/CanvasWm.Core.Tests`, suggested files
  `Layout/LayoutEditsRemoveTests.cs`, `Layout/InsertBesideTests.cs`,
  `Layout/SeamTests.cs`, `Layout/DockTests.cs`, `Layout/DockingPropertyTests.cs`,
  plus a test-only `SequenceIdGenerator : IIdGenerator` that returns `"n1"`,
  `"n2"`, … in order and counts calls.
- Depends on `specs/layout-tree.md`: `LayoutNode`, `SplitNode`, `PanelNode`,
  `DesktopNode`, `Axis`, `LayoutDocument`, `FloatingPanel`, `HiddenPanel`,
  `LayoutNormalizer.Normalize` / `MissingWeight`, `IIdGenerator`,
  `IdPrefixes.Split`, `UidGenerator`. Independent of `specs/layout-solver.md`.
  **No existing type is changed.**

## Public API

All types public, documented with XML docs; all string comparisons and id sets
are **ordinal**. Null reference arguments not marked nullable throw
`ArgumentNullException`.

```csharp
namespace CanvasWm.Core.Layout;

/// <summary>Side of a target node on which a new node is placed. Raw values are fixed.</summary>
public enum Edge { Left = 0, Right = 1, Top = 2, Bottom = 3 }

public static class EdgeExtensions
{
    /// <summary>Axis of the split created by inserting on this edge: X for Left/Right, Y for Top/Bottom.</summary>
    public static Axis ToAxis(this Edge edge);

    /// <summary>True for Left and Top (the new node goes before the target).</summary>
    public static bool IsLeading(this Edge edge);
}

/// <summary>Snapshot of one divider of a split, by leaf ids (not by split id).</summary>
public sealed record SplitSeam
{
    /// <exception cref="ArgumentNullException">leaves or before is null, or any element is null.</exception>
    public SplitSeam(Axis axis, IEnumerable<string> leaves, IEnumerable<string> before);

    /// <summary>Axis of the split the seam belongs to.</summary>
    public Axis Axis { get; init; }
    /// <summary>All leaf ids of that split, in order, at snapshot time.</summary>
    public ImmutableArray<string> Leaves { get; }
    /// <summary>Leaf ids that lay before the divider (left of it for X, above it for Y).</summary>
    public ImmutableArray<string> Before { get; }
}
// Structural equality (Axis, then Leaves and Before element-wise in order), like SplitNode.

/// <summary>Where to dock: beside node Id on Edge, or at Seam when Seam is non-null
/// (Id and Edge are then ignored by dock).</summary>
/// <param name="Id">Target node id (any kind). Non-null.</param>
public sealed record DockTargetSpec(string Id, Edge Edge, SplitSeam? Seam = null);
// Id null → ArgumentNullException. Compiler-generated equality.

/// <summary>Pure tree edits used by docking.</summary>
public static class LayoutEdits
{
    /// <summary>Lower clamp of the insert fraction: 0.1.</summary>
    public const double MinFraction = 0.1;
    /// <summary>Upper clamp of the insert fraction: 0.9.</summary>
    public const double MaxFraction = 0.9;
    /// <summary>Fraction every caller uses: 0.5.</summary>
    public const double DefaultFraction = 0.5;
    /// <summary>Suffix of the derived id of a cross-axis seam group: "-range".</summary>
    public const string RangeSuffix = "-range";

    /// <summary>Leaf ids in order (B1). Null → empty.</summary>
    public static ImmutableArray<string> LeafIds(LayoutNode? node);

    /// <summary>All PanelNodes of the tree in depth-first pre-order (B2). Null → empty.</summary>
    public static ImmutableArray<PanelNode> PanelsOf(LayoutNode? node);

    /// <summary>The panel whose Views contain windowKey: tree first (pre-order), then Floating,
    /// then Hidden (list order). Null when none (B3).</summary>
    public static PanelNode? FindPanelOfWindow(LayoutDocument document, string windowKey);

    /// <summary>Removes the node with this id (any kind except a desktop) and its subtree.
    /// shareRemovedSpace = true: freed weight is shared by all siblings in proportion;
    /// false: it goes to the adjacent sibling (B4–B9). Returns the same instance when nothing
    /// matched; null when the whole tree was removed.</summary>
    public static LayoutNode? RemoveNode(LayoutNode? node, string id, bool shareRemovedSpace = true);

    /// <summary>Removes the panel with this id from the tree (RemoveNode with the given variant),
    /// else from Floating, else from Hidden. Not found → same document instance (B10).</summary>
    public static LayoutDocument RemovePanel(LayoutDocument document, string panelId,
                                             bool shareRemovedSpace = true);

    /// <summary>Wraps the node whose id is targetId in a new split (id splitId) together with
    /// newNode on the given edge, newNode taking fraction (clamped to [0.1, 0.9]) of the
    /// target's space; then normalises (B11–B17). Null node → newNode. Target missing → the
    /// same instance.</summary>
    public static LayoutNode? InsertBeside(LayoutNode? node, string targetId, LayoutNode newNode,
                                           Edge edge, string splitId,
                                           double fraction = DefaultFraction);

    /// <summary>Dock target for divider index of split (B18): Id = split.Id, Edge = Left (X) or
    /// Top (Y), Seam = (split.Axis, LeafIds(split), leaf ids of the first index children).</summary>
    public static DockTargetSpec SeamTarget(SplitNode split, int index);

    /// <summary>Inserts newNode at the seam (B19–B25). No live seam leaf → same instance.</summary>
    public static LayoutNode? InsertAtSeam(LayoutNode? root, SplitSeam seam, LayoutNode newNode,
                                           string splitId);
}

/// <summary>Moves panels into a document's tree.</summary>
public static class Docking
{
    /// <summary>Moves the panel holding windowKey (or a new one-window panel) to target (B26–B30).
    /// Never fails. Consumes one id from ids, two when it falls back.</summary>
    public static LayoutDocument Dock(LayoutDocument document, string windowKey,
                                      DockTargetSpec target, IIdGenerator ids,
                                      bool shareRemovedSpace = true);

    /// <summary>Inserts a detached panel at target without looking it up or removing it (B31).</summary>
    public static LayoutDocument DockUnit(LayoutDocument document, PanelNode panel,
                                          DockTargetSpec target, IIdGenerator ids);
}
```

## Behaviour

Notation: `X(s)[...]` / `Y(s)[...]` = split on axis X (row) / Y (column) with
id `s`; weights in parentheses or after a child; letters = one-window panels
`PanelNode.ForWindow(letter)`; `D{...}` = `DesktopNode` (id `"desktop"`),
`D{}` = empty desktop; `n` = the inserted node; `n1`, `n2` = ids returned in
order by the injected generator.

### General

- **B0. Purity and identity.** All functions are pure: inputs are never
  modified; no static mutable state; `Dock`/`DockUnit` touch nothing but their
  return value and the generator. "**Unchanged**" in the rules below means the
  function returns **the same instance** it was given (A13). Weight of child
  `i` of a split, wherever read below, is `w_i = i < Weights.Length ?
  Weights[i] : LayoutNormalizer.MissingWeight` (A14); bad values are left to
  `Normalize`.

### Tree queries

- **B1. `LeafIds`.** null → `[]`; panel → `[panel.Id]`; desktop with no child
  → `[desktop.Id]`; desktop with a child → `LeafIds(child)`; split → the
  concatenation of `LeafIds` of its children in order.
- **B2. `PanelsOf`.** Depth-first pre-order, children in order, desktop child
  included; empty desktops contribute nothing.
- **B3. `FindPanelOfWindow`.** First `PanelNode` of `PanelsOf(document.Root)`
  whose `Views` contains `windowKey`; else the first `FloatingPanel.Panel` in
  `Floating` order that contains it; else the first `HiddenPanel.Panel` in
  `Hidden` order; else null (A5).

### Remove (`RemoveNode`, both variants)

- **B4. Match first.** If `node` is null → null. If `node.Id == id`: a
  `DesktopNode` is returned unchanged (desktops are never removed); any other
  node is removed (returns null) together with its subtree (including any
  desktop inside it).
- **B5. Panel** not matching → unchanged.
- **B6. Desktop** not matching: no child → unchanged. Child is not a
  `DesktopNode` and `child.Id == id` → `new DesktopNode(null, desktop.Id)`
  (free space). Otherwise `r = RemoveNode(child, id, share)`: `r` null → empty
  desktop with the same id; `r` same instance as child → unchanged; else
  `new DesktopNode(r, desktop.Id)` (no `Normalize` at this level) (A9).
- **B7. Split, a direct child matches.** Let `k` = index of the **first**
  child with `Children[k].Id == id` and the child is not a `DesktopNode`.
  Remove child `k` and its weight `w_k`; other children are kept as they are
  (not searched). Then:
  - adjacent variant (`share == false`): add `w_k` to the remaining child at
    index `min(k, remainingCount − 1)` (the child that followed it, or the new
    last child if it was last);
  - sharing variant (`share == true`): discard `w_k`.
  - No children remain → null. Otherwise return
    `Normalize(new SplitNode(split.Id, split.Axis, remainingWeights, remainingChildren))`.
- **B8. Split, no direct child matches.** Recurse into every child in order
  with the same arguments. A child returned as the same instance keeps its
  weight; a changed child keeps its weight; a child that **vanished** (null):
  - adjacent variant: its weight is added to the previous surviving child; if
    there is none yet, it is carried forward and added to the next surviving
    child (A16);
  - sharing variant: dropped with its weight.
  If every child came back as the same instance → unchanged. No survivors →
  null. Else `Normalize(new SplitNode(split.Id, split.Axis, weights, survivors))`.
- **B9. Result invariants.** For a normalised input tree (layout-tree B14) the
  result is null or normalised. A split rebuilt here keeps its id and axis;
  ids are lost only by `Normalize` (collapse / same-axis flatten).

### Remove panel from a document

- **B10. `RemovePanel(document, panelId, share)`.** Locate a `PanelNode` with
  `Id == panelId`:
  1. in the tree (`PanelsOf(Root)`) → result = document with
     `Root = RemoveNode(Root, panelId, share)` (lists unchanged);
  2. else, if any `Floating` entry has `Panel.Id == panelId` → all such
     entries are removed from `Floating` (order of the rest kept);
  3. else, if any `Hidden` entry has `Panel.Id == panelId` → all such entries
     are removed from `Hidden`;
  4. else → the same document instance.
  `HiddenDesktop` is always carried over unchanged. With `share == true` this
  is exactly the study's dock-time removal (tree → sharing variant; floating /
  hidden → list removal); with `share == false` it is the study's standalone
  remove-panel (A7).

### Insert beside

- **B11. Empty tree.** `node == null` → returns `newNode` (same instance).
- **B12. Fraction.** `f = fraction` clamped to `[0.1, 0.9]`; NaN → `0.5`;
  `+∞` → `0.9`; `−∞` → `0.1` (A8). All callers in this feature pass `0.5`.
- **B13. Match test first, any node kind.** If `node.Id == targetId` (panel,
  split or desktop) the node is replaced by
  `Normalize(new SplitNode(splitId, edge.ToAxis(), weights, children))` with
  the target taken as-is (its subtree is not searched):

  | edge | axis | children | weights |
  |---|---|---|---|
  | Left | X | [new, target] | [f, 1−f] |
  | Right | X | [target, new] | [1−f, f] |
  | Top | Y | [new, target] | [f, 1−f] |
  | Bottom | Y | [target, new] | [1−f, f] |

  If the target is a split on the same axis, `Normalize` flattens it into the
  new split (its children scaled by `1−f`) and the target's id disappears; the
  result keeps `splitId`.
- **B14. No match, panel** → unchanged.
- **B15. No match, desktop.** No child → unchanged. Otherwise recurse into
  the child; result the same instance → unchanged; else
  `new DesktopNode(result, desktop.Id)` (no `Normalize` at this level).
- **B16. No match, split.** Recurse into **every** child with the same
  arguments. If all came back as the same instance → unchanged. Otherwise
  `Normalize(new SplitNode(split.Id, split.Axis, originalWeights, newChildren))`
  (a null child result is dropped by `Normalize`). Consequence: the wrapped
  target's former share is divided `1−f : f`; a wrapper of the same axis as
  this split is flattened into it and `splitId` vanishes ("adds a column to
  the existing row").
- **B17. Target not found** anywhere → the same instance (no error).

### Seams

- **B18. `SeamTarget(split, index)`.** `i = clamp(index, 0, n)` with
  `n = split.Children.Length` (negative → 0, A11). Returns
  `DockTargetSpec(split.Id, split.Axis == X ? Left : Top,
  new SplitSeam(split.Axis, LeafIds(split), concat of LeafIds(Children[0..i))))`.
  Indices `1..n−1` are inner dividers; `0` and `n` are the outer edges.

### Insert at a seam

- **B19. Live set.** `S` = the ids in `LeafIds(root)` that also occur in
  `seam.Leaves` (a set). `S` empty (including `root == null`) → the same
  instance.
- **B20. Result.** `r = Insert(root)` (B21–B24) then `Normalize(r)`; if that
  yields null → `root` (same instance); else the normalised result.
- **B21. Edge rule (used by B22 and B24).** If any id of `S` is in
  `seam.Before` → the new node goes **after**: `Right` for seam axis X,
  `Bottom` for Y; otherwise **before**: `Left` / `Top`.
- **B22. `Insert(split)`.** For child `i` let `L_i = LeafIds(Children[i])`.
  1. **Descend:** if some child has `S ⊆ L_i`, take the first such child, set
     it to `Insert(child)` and return
     `new SplitNode(split.Id, split.Axis, split.Weights, children)` (not
     normalised here).
  2. **Span:** otherwise let `first` / `last` be the smallest / largest `i`
     with `L_i ∩ S ≠ ∅`; the *range* is children `first..last` inclusive
     (`m = last − first + 1`, untouched children in between included);
     `W = Σ w_i` over the range.
     - **Same axis (`split.Axis == seam.Axis`):** insertion index `p` = the
       first `i` in `first..last` whose `L_i` contains an id **not** in
       `seam.Before`; if none, `p = last + 1` (A3). Every range child's weight
       is multiplied by `m/(m+1)`; the new node is inserted at index `p` with
       weight `W/(m+1)`; children outside the range keep their weights. Id
       and axis kept. (The new tile takes an equal `1/(m+1)` share of the
       tiles bordering the seam.)
     - **Cross axis (`split.Axis ≠ seam.Axis`) (A2):** group =
       `new SplitNode(splitId + "-range", split.Axis, [w_i / W for the range],
       range children)`; `g = InsertBeside(group, group.Id, newNode, edge
       (B21), splitId, 0.5)`; the range is replaced in the split by the single
       child `g` with weight `W`; other children keep their weights. Id and
       axis kept.
  3. If no child touches `S` (unreachable from B19/B22.1) → leaf case B24 (A4).
- **B23. `Insert(desktop)`.** If it has a child, its own id is not in `S`,
  and `S ⊆ LeafIds(child)` → `new DesktopNode(Insert(child), desktop.Id)`.
  Otherwise → leaf case B24.
- **B24. Leaf case** (panel, empty desktop, B22.3, B23 otherwise):
  `InsertBeside(node, node.Id, newNode, edge (B21), splitId, 0.5)`.
- **B25. Ids.** Only `splitId` and the derived `splitId + "-range"` are
  introduced; no generator is called inside `LayoutEdits`.

### Dock

- **B26. Find the unit.** `panel = FindPanelOfWindow(document, windowKey)`;
  `fresh = panel is null`; if fresh, `panel = PanelNode.ForWindow(windowKey)`.
  A found panel moves as a whole: its `Id`, `Views`, `Selected` and `Tabbed`
  are preserved (it is the same instance).
- **B27. Remove.** If not fresh: `doc1 = RemovePanel(document, panel.Id,
  shareRemovedSpace)`. If fresh: `doc1 = document` (A6).
- **B28. Insert** = `DockUnit(doc1, panel, target, ids)` (B31).
- **B29. Fallback edge.** `Bottom` if `target.Seam` is non-null and its
  `Axis == Y`; otherwise `Right` (regardless of `target.Edge`).
- **B30. Result.** Never throws for non-null arguments; there is no failure
  result. `Floating`, `Hidden` and `HiddenDesktop` equal those of `doc1`.
- **B31. `DockUnit(document, panel, target, ids)`.**
  1. `id1 = ids.Make(IdPrefixes.Split)` — **always**, even if unused.
  2. `root = document.Root`;
     - `root == null` → `newRoot = panel`;
     - `target.Seam != null` → `newRoot = InsertAtSeam(root, target.Seam,
       panel, id1)` (`target.Id`/`Edge` ignored);
     - else `newRoot = InsertBeside(root, target.Id, panel, target.Edge, id1,
       0.5)`.
  3. **Verify:** if `PanelsOf(newRoot)` contains a panel with
     `Id == panel.Id` → done. Otherwise `id2 = ids.Make(IdPrefixes.Split)` and
     `newRoot = newRoot == null ? panel : InsertBeside(newRoot, newRoot.Id,
     panel, fallbackEdge (B29), id2, 0.5)`. The root always matches, so the
     panel ends up wrapping the whole tree at half its size; if the old root
     has the fallback axis it is flattened into the new split and the root id
     becomes `id2`.
  4. Return `document with { Root = newRoot }` (lists unchanged).
- **B32. Id accounting.** `Dock`/`DockUnit` call `ids.Make` exactly once, or
  exactly twice when B31.3 falls back, always with `IdPrefixes.Split`. Panel
  ids never change; existing split ids change only through `Normalize`.

## Edge cases & errors

- **E1.** `ArgumentNullException` for: `RemoveNode` `id`; `RemovePanel`
  `document`/`panelId`; `InsertBeside` `targetId`/`newNode`/`splitId`;
  `SeamTarget` `split`; `InsertAtSeam` `seam`/`newNode`/`splitId`;
  `FindPanelOfWindow` `document`/`windowKey`; `Dock` `document`/`windowKey`/
  `target`/`ids`; `DockUnit` `document`/`panel`/`target`/`ids`; `SplitSeam` and
  `DockTargetSpec` constructors as stated. Nullable node parameters accept
  null.
- **E2.** Self-dock (target id = the moved panel's id): after removal the
  target is gone → fallback. Docking beside a split id that collapsed because
  of the removal → fallback. Docking beside its own neighbour works; the
  neighbour already holds the freed space. Docking onto a seam the panel was
  part of works (its leaf is not live).
- **E3.** Removing the only panel of a tree → `Root == null` (or `D{}` when it
  was a desktop's child); a following insert makes the panel the root.
- **E4.** Duplicate ids (not detected, A12): `RemoveNode` removes the first
  direct match in a split but recurses into every non-matching child, so
  matches in different subtrees are all removed; `InsertBeside` wraps every
  matching node (which can duplicate `newNode`); `Dock` verifies by panel id.
- **E5.** A `newNode` panel with empty `Views` is dropped by `Normalize`;
  `Dock`/`DockUnit` then return a tree without it (only reachable with
  un-sanitized input; A17).
- **E6.** `DockUnit` with a panel that is already in the document adds a
  second copy of it; the caller must remove it first (that is what `Dock`
  does).
- **E7.** `RemoveNode` with the id of a split that contains the desktop
  removes the desktop with the subtree (only a desktop's *own* id is
  protected, B4).

## Acceptance criteria

All criteria are xUnit tests in `tests/CanvasWm.Core.Tests` with no OS access
and no TrellisWM. Weights compare with absolute tolerance `1e-9`. Trees in
criteria are normalised unless stated. "Same instance" = `ReferenceEquals`.
Ids `n1`, `n2` come from the test `SequenceIdGenerator`.

### Model and queries
- **AC1** [mac] `(int)Edge.Left/Right/Top/Bottom == 0/1/2/3`; `ToAxis` gives
  X, X, Y, Y; `IsLeading` gives true, false, true, false. (B13)
- **AC2** [mac] Two `SplitSeam(X, [a,b], [a])` are `Equals` with equal hash
  codes; differing axis, leaves or before → unequal. `SplitSeam` with null
  list or null element and `DockTargetSpec(null!, …)` throw
  `ArgumentNullException`. (Public API)
- **AC3** [mac] `LeafIds`: null → `[]`; `a` → `[a]`; `D{}` → `[desktop]`;
  `D{X(s)[a, Y(t)[b, c]]}` → `[a, b, c]`. `PanelsOf` on
  `X(s)[a, Y(t)[b, D{}]]` → `[a, b]` (instances from the tree); on `D{a}` →
  `[a]`; on null → `[]`. (B1, B2)
- **AC4** [mac] `FindPanelOfWindow`: a doc whose tree has panel
  `P{id p, views [x, y]}`, floating `F{id f, views [y, z]}`, hidden
  `H{id h, views [z, w]}`: key `y` → `P`; `z` → `F`; `w` → `H`; `q` → null.
  (B3)

### Remove (findings examples 10–14)
- **AC5** [mac] Adjacent (`share: false`): `X(s)[a .2, b .3, c .5]` remove
  `b` → `X(s)[a .2, c .8]`; remove `c` → `X(s)[a .2, b .8]`; remove `a` →
  `X(s)[b .5, c .5]`. (Ex. 10, B7)
- **AC6** [mac] Sharing (default): same tree remove `b` →
  `X(s)[a 2/7, c 5/7]`; remove `a` → `X(s)[b .375, c .625]`; `RemoveNode`
  called without the flag behaves as sharing. (Ex. 11, B7, findings rule 8)
- **AC7** [mac] `X(s)[a .5, Y(t)[b .5, c .5] .5]` remove `b`, both variants
  → `X(s)[a .5, c .5]`; no node with id `t` remains. (Ex. 12, B8)
- **AC8** [mac] `D{a}` remove `a` → `D{}` with id `desktop`; `D{a}` remove
  `"desktop"` → same instance; root `a` remove `a` → null; `RemoveNode(null,
  "a")` → null; `D{X(s)[a .5, b .5]}` remove `s` → `D{}`. (Ex. 13, B4, B6)
- **AC9** [mac] `X(s)[a .5, Y(t)[b .5, c .5] .5]` remove `t` → `a` (both
  variants); remove `s` → null. (Ex. 14, B4, B7)
- **AC10** [mac] Remove `"zz"` (absent) from the AC7 tree → same instance,
  both variants. (B5, B8)
- **AC11** [mac] Vanished nested child (un-normalised input):
  `X(s)[a .2, Y(t)[b] .3, c .5]` remove `b` → adjacent `X(s)[a .5, c .5]`,
  sharing `X(s)[a 2/7, c 5/7]`; `X(s)[Y(t)[b] .3, a .2, c .5]` remove `b`
  adjacent → `X(s)[a .5, c .5]`. (B8, A16)
- **AC12** [mac] `RemovePanel`: tree panel → as `RemoveNode` with the given
  variant (AC5/AC6 tree, `b`, both flags), lists unchanged; panel `f` only in
  `Floating` (two entries, `f` second) → `Floating` is the other entry only,
  tree same instance; panel only in `Hidden` → removed from `Hidden`;
  absent id → same document instance; `HiddenDesktop` preserved in all cases.
  (B10)

### Insert beside (findings examples 1–9)
- **AC13** [mac] `InsertBeside(a, "a", n, Right, "n1")` →
  `X(n1)[a .5, n .5]`; Left → `X(n1)[n .5, a .5]`; Top → `Y(n1)[n .5, a .5]`;
  Bottom → `Y(n1)[a .5, n .5]`. (Ex. 1–2, B13)
- **AC14** [mac] Right with `f = 0.05` → `X(n1)[a .9, n .1]`; `f = 0.95` →
  `X(n1)[a .1, n .9]`; `f = NaN` → `[.5, .5]`; `+∞` → `[.1, .9]`; `−∞` →
  `[.9, .1]`. (Ex. 3, B12, A8)
- **AC15** [mac] `X(s)[a .5, b .5]`, target `b`, Right →
  `X(s)[a .5, b .25, n .25]` (no node `n1`); target `b`, Bottom →
  `X(s)[a .5, Y(n1)[b .5, n .5] .5]`. (Ex. 4–5, B16)
- **AC16** [mac] `X(s)[a .5, b .5]`, target `s`, Right →
  `X(n1)[a .25, b .25, n .5]` (no node `s`); `X(s)[a 1/3, b 1/3, c 1/3]`,
  target `b`, Right → `X(s)[a 1/3, b 1/6, n 1/6, c 1/3]`. (Ex. 6–7, B13, B16)
- **AC17** [mac] `Y(s)[a .6, D{} .4]`, target `"desktop"`, Right →
  `Y(s)[a .6, X(n1)[D{} .5, n .5] .4]`; `D{a}`, target `a`, Right →
  `D{X(n1)[a .5, n .5]}` (desktop id kept); `D{a}`, target `"desktop"`, Left →
  `X(n1)[n .5, D{a} .5]`. (Ex. 8, B13, B15)
- **AC18** [mac] Target `"zz"` absent in the AC15 tree → same instance;
  `InsertBeside(null, "a", n, Right, "n1")` → `n` (same instance). (Ex. 9,
  B11, B17)
- **AC19** [mac] Deep target keeps ancestors:
  `X(s)[a .5, Y(t)[b .5, c .5] .5]`, target `c`, Right →
  `X(s)[a .5, Y(t)[b .5, X(n1)[c .5, n .5] .5] .5]`; target `c`, Bottom →
  `X(s)[a .5, Y(t)[b .5, c .25, n .25] .5]`. (B13, B16)

### Seams (findings examples 15–20)
- **AC20** [mac] `SeamTarget(X(s)[a, b, c], 1)` →
  `DockTargetSpec("s", Left, SplitSeam(X, [a,b,c], [a]))`; index 0 → before
  `[]`; 3 → `[a,b,c]`; −1 → `[]`; 7 → `[a,b,c]`; for `Y(t)[a, b]` the edge is
  `Top`; for `X(s)[a, Y(t)[b, c]]` index 1 → leaves `[a,b,c]`, before `[a]`.
  (Ex. 15, B18, A11)
- **AC21** [mac] `InsertAtSeam(X(s)[a .5, b .5], (X,[a,b],[a]), n, "n1")` →
  `X(s)[a 1/3, n 1/3, b 1/3]`. (Ex. 16, B22)
- **AC22** [mac] `X(s)[a .5, b .25, c .25]`, seam `(X,[a,b,c],[a])` →
  `X(s)[a .375, n .25, b .1875, c .1875]`. (Ex. 17, B22)
- **AC23** [mac] `X(s)[b .6, c .4]`, seam `(X,[a,b,c],[a,b])` →
  `X(s)[b .4, n 1/3, c 4/15]`. (Ex. 18, B19, B22)
- **AC24** [mac] Root `b`, seam `(X,[a,b],[a])` → `X(n1)[n .5, b .5]`; root
  `b`, seam `(X,[b,c],[b])` → `X(n1)[b .5, n .5]`; root `b`, seam
  `(Y,[a,b],[a])` → `Y(n1)[n .5, b .5]`. (Ex. 19, B21, B24)
- **AC25** [mac] Cross axis: `Y(u)[b .5, c .25, d .25]`, seam
  `(X,[a,b,c],[a])` → `Y(u)[X(n1)[n .5, Y(n1-range)[b 2/3, c 1/3] .5] .75,
  d .25]`. (Ex. 20, B22, A2)
- **AC26** [mac] Cross axis covering the whole split: `Y(u)[b .5, c .5]`,
  seam `(X,[b,c],[b])` → `X(n1)[Y(n1-range)[b .5, c .5] .5, n .5]` (no node
  `u`). (B20, B22, A2)
- **AC27** [mac] Descend: `X(s)[a .5, Y(t)[b .5, c .5] .5]`, seam
  `(Y,[b,c],[b])` → `X(s)[a .5, Y(t)[b 1/3, n 1/3, c 1/3] .5]`; same tree in
  `D{…}` → same result inside the desktop, desktop id kept. (B22.1, B23)
- **AC28** [mac] Outer edges: `X(s)[a .5, b .5]` with seam
  `(X,[a,b],[])` → `X(s)[n 1/3, a 1/3, b 1/3]`; with `(X,[a,b],[a,b])` →
  `X(s)[a 1/3, b 1/3, n 1/3]`. (B18, B22, A3)
- **AC29** [mac] No live leaf: seam `(X,[p,q],[p])` on the AC21 tree → same
  instance; `InsertAtSeam(null, …)` → null. (B19)

### Dock (findings examples 21–27, default `shareRemovedSpace = true`)
- **AC30** [mac] `X(s)[a .5, b .25, c .25]`, `Dock(window a, SeamTarget(s, 2))`
  → `X(s)[b 1/3, a 1/3, c 1/3]`; generator called once. (Ex. 21)
- **AC31** [mac] `X(s)[a .5, b .5]`, dock `a` beside `b` Right →
  `X(n1)[b .5, a .5]`; one call. (Ex. 22)
- **AC32** [mac] `X(s)[a .5, b .5]`, dock `a` beside `a` Right →
  `X(n2)[b .5, a .5]`; two calls. Dock `a` beside `"zz"` **Left** →
  `X(n2)[b .5, a .5]` (fallback is Right). (Ex. 23–24, B29, E2)
- **AC33** [mac] `X(s)[a .5, b .5]`, dock fresh window `c` with target
  `DockTargetSpec("t", Top, SplitSeam(Y, [p,q], [p]))` →
  `Y(n2)[X(s)[a .5, b .5] .5, c .5]`; same with a seam of axis X →
  `X(n2)[a .25, b .25, c .5]` (root id becomes `n2`). (Ex. 25, B29, B31.3)
- **AC34** [mac] `LayoutDocument.Empty`, dock window `w` beside `"x"` →
  `Root` is `PanelNode.ForWindow("w")`; generator called once. (Ex. 26, B26,
  B31)
- **AC35** [mac] `X(s)[a .2, b .3, c .5]`, dock `b` beside `c` Bottom:
  `shareRemovedSpace: false` → `X(s)[a .2, Y(n1)[c .5, b .5] .8]`; `true` →
  `X(s)[a 2/7, Y(n1)[c .5, b .5] 5/7]`. (Ex. 27, B27)
- **AC36** [mac] Neighbour: `X(s)[a .2, b .3, c .5]`, dock `a` beside `b`
  Right → `X(s)[b .1875, a .1875, c .625]`. Collapsed target:
  `X(s)[a .5, Y(t)[b .5, c .5] .5]`, dock `b` beside `t` Right →
  `X(n2)[a .25, c .25, b .5]`. (E2, findings rule 26)
- **AC37** [mac] Tab group moves whole: tree `X(s)[P{id p, views [x, y],
  selected y, tabbed true} .5, a .5]`, dock window `x` beside `a` Bottom →
  `Y(n1)[a .5, P .5]` where the moved panel is the same `P` instance. Window
  `y` held only by floating `F{id f, views [y, z], tabbed true}` with root
  `a`: dock `y` beside `a` Right → `X(n1)[a .5, F.Panel .5]`, `Floating`
  empty. Same with a hidden panel → `Hidden` empty. (B26, B27, B10)
- **AC38** [mac] Desktop: `D{a}`, dock `a` beside `"desktop"` Right →
  `X(n1)[D{} .5, a .5]`; `D{X(s)[a .5, b .5]}`, dock `a` beside `b` Bottom →
  `D{Y(n1)[b .5, a .5]}`. (B6, B13, B15)
- **AC39** [mac] `DockUnit`: doc `X(s)[a .5, b .5]`, panel `c` (not in doc),
  beside `b` Right → `X(s)[a .5, b .25, c .25]`, one call; beside `"zz"` →
  `X(n2)[a .25, b .25, c .5]`, two calls; empty doc → root `c`, one call.
  `Floating`/`Hidden`/`HiddenDesktop` unchanged. (B31)
- **AC40** [mac] Inputs are unchanged after every call in AC5–AC39 (each
  input equals a deep copy taken before the call). Every `ArgumentNullException`
  case listed in E1 throws. (B0, E1)

### Property test
- **AC41** [mac] Seeded `System.Random` (fixed seed), ≥ 500 documents:
  normalised trees (depth ≤ 4, fan-out 2–4, random axis, unique ids, panels
  with 1–3 unique window keys, at most one desktop as root or leaf), 0–2
  floating and 0–2 hidden panels with unique ids/keys. On each, apply a
  random sequence of 1–10 operations drawn from: `RemoveNode` (random
  existing id or absent id, random variant); `InsertBeside` of a new
  one-window panel (random target id incl. absent, random edge, random
  fraction in `[−0.5, 1.5]`); `InsertAtSeam` of a new panel with a seam from
  `SeamTarget` on a random split and index, taken possibly before a preceding
  remove; `Dock` of a random existing or fresh window key with a random
  beside or seam target and random flag. After every operation:
  (a) the tree is null or satisfies layout-tree B14 invariants (each split ≥ 2
  children, one weight per child, weights > 0 and finite, summing to 1 within
  1e-9, no same-axis child split, no empty panel);
  (b) for `Dock`: the multiset of window keys over tree + floating + hidden
  equals the previous one (plus the key once if it was fresh); the set of
  panel ids is the previous set (plus the key if fresh); the moved panel is in
  the tree exactly once and `Equals` its pre-dock value; the generator was
  called exactly 1 or 2 times;
  (c) for `InsertBeside`/`InsertAtSeam`: either the result is the same
  instance or the new panel appears exactly once and every previous panel is
  still present exactly once;
  (d) for `RemoveNode`: no leaf of the removed subtree remains, every other
  panel remains exactly once;
  (e) a desktop present before is present after (same id) for every op
  except a `RemoveNode` whose removed subtree contained it (E7);
  (f) inputs unchanged. (B0–B32, findings rules 29–31)

### Architecture
- **AC42** [mac] `dotnet build CanvasWm.slnx` exits 0 with 0 warnings; the
  existing `CoreReferencesNoCanvasWmAssembly` test still passes;
  `CanvasWm.Core.csproj` has no `PackageReference`/`ProjectReference`; all
  layout-tree and layout-solver tests still pass (no existing type changed).

Count: 42 [mac] (AC1–AC42), 0 [windows].

## Assumptions

- **A1. `boundary` omitted.** The study's dock target carries an optional
  integer `boundary` that dock never reads (meaning unknown).
  `DockTargetSpec` has no such member; #21 drop-hit-testing may add one
  (additive positional parameter with a default) if it finds a use.
- **A2. Cross-axis seam path (Inferred).** B22 cross-axis: the range is
  grouped as `splitId + "-range"` with the parent's axis and weights `w/W`,
  the new node is placed beside the group with `splitId`, and the result
  replaces the range with weight `W`. When the range covers the whole split
  the parent ends with one child and `Normalize` collapses it (its id is lost,
  AC26). Only the structure and the `-range` string were observed; the exact
  slice and collapse behaviour are inferred. *Needs human review.*
- **A3. Insertion index (Inferred).** Same-axis: the first range child that
  contains *any* leaf id not in `seam.Before` (an untouched middle child
  counts, since its leaves are not in the seam at all); if every range child
  is entirely "before" (seam at the far outer edge), the new node is appended
  after the range. *Needs human review.*
- **A4. Unreachable spanning fallback.** The study has a branch for a
  same-axis split with no child touching the live set (weight `1/(n+1)`); it
  cannot be reached via B19/B22.1, so CanvasWM treats it as the leaf case and
  does not test it.
- **A5. Window lookup scope.** Tree (pre-order) → floating → hidden, first
  match wins (the study's search scope was not traced).
- **A6. Fresh panel skips removal (deviation).** The study always calls
  remove-panel with the fresh panel's id (the window key); in a sanitized
  document no other panel can carry that id while not holding the key, so
  skipping is unobservable there and avoids deleting an unrelated panel in
  an inconsistent document.
- **A7. `RemovePanel` default `true`.** The study's standalone remove-panel
  always uses the adjacent variant; here the variant is a parameter with the
  scope-wide default `true` so that `Dock`'s removal is one call. Other
  callers (window closed, hide) must decide which variant they pass. *Needs
  human review.*
- **A8. Non-finite fraction.** NaN → 0.5, ±∞ → clamp bounds (CanvasWM
  addition; the study clamps without a NaN guard).
- **A9. Nested desktop.** In B6 a desktop's child that is itself a desktop is
  never removed by id (consistent with "desktops are never removed"); the
  study would empty the outer desktop. Unreachable in sanitized trees.
- **A10. `shareRemovedSpace` is a parameter** (default true) rather than a
  global; the Engine/settings feature owns the preference and passes it in.
- **A11. Seam index clamp.** Negative index → 0 (the study clamps only to the
  child count).
- **A12. Duplicate ids** are not detected (see E4); trees are assumed to have
  unique ids, as in layout-tree A10.
- **A13. Same-instance "unchanged" contract.** All no-op outcomes return the
  input instance; this makes "target not found" detectable by
  `ReferenceEquals` and keeps `Normalize`'s shortcut effective.
- **A14. Weights read with missing → 1.0** (layout-tree rule); non-positive or
  non-finite weights are passed through and repaired by `Normalize`.
- **A15. Names.** `Edge` (raw values kept) and `DockTargetSpec` (not
  `DockTarget`, which #19 slot-restore will need for its enum).
- **A16. Vanished-child weight (Inferred).** Adjacent variant gives a
  recursively vanished child's weight to the previous survivor, else the next.
  Only reachable with un-normalised input.
- **A17. Empty-views panels** are not guarded against; `Dock` of such a panel
  can drop it. Callers dock only panels from sanitized documents or
  `ForWindow`.

## Traceability

| Spec item | Findings rule / example |
|---|---|
| `Edge`, AC1 | Data model; 11 (edge table) |
| `SplitSeam`, `DockTargetSpec`, AC2, A1, A15 | 16; Data model; Unknowns (`boundary`) |
| B0, A13, A14 | 15, 28, 30 |
| B1, B2, AC3 | 18; Data model (`PanelsOf`) |
| B3, AC4, A5 | 25.1; Unknowns (`panelOfView` scope) |
| B4–B7, AC5–AC10, E7 | 1–4; Ex. 10–14 |
| B8, AC11, A16 | 5 |
| B9 | 30 |
| B10, AC12, A7 | 6, 7 |
| B11–B17, AC13–AC19, A8 | 9–15; Ex. 1–9 |
| B18, AC20, A11 | 17; Ex. 15 |
| B19–B21, B24, AC21–AC24, AC29 | 19, 20, 23; Ex. 16–19 |
| B22 same axis, AC21–AC23, AC28, A3 | 22 (first bullet); Unknowns (insertion index) |
| B22 cross axis, AC25, AC26, A2 | 22 (second bullet); Ex. 20; Unknowns (cross-axis) |
| B22.3, A4 | 24 |
| B23, AC27 | 21 |
| B25, B32 | 31 |
| B26–B31, AC30–AC39, A6, A10 | 7, 8, 25, 27, 28; Ex. 21–27 |
| E2, AC32, AC36 | 26 |
| AC41 | 29–31; Windows notes (injected generator) |
| AC42 | CLAUDE.md architecture |
| Out of scope | 32; Unknowns (`insertPanel`, `redock`, `tab`, hit testing) |
