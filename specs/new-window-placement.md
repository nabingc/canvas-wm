# Spec: new-window-placement

## Goal

Two pure document operations that decide where windows go in one display's
layout tree:

- **Place new** (`Placement.PlaceNew`): tile one window next to an *anchor*
  tile inside the area the camera is framing. The anchor is split to the
  **right** when it is at least as wide as tall on screen, otherwise **below**
  it. This is a thin wrapper over `Docking.Dock` (fraction 0.5). When the
  framed node (or the anchor) has no rectangle, the result is a distinct
  **needs-float** outcome and the document is not changed. The float
  operation itself belongs to #22 floating-panels.
- **Dwindle rebuild** (`Placement.ArrangeDwindle`, "arrange all"): throw the
  current tree away and rebuild it from an ordered window list as a spiral of
  half/half splits on alternating axes, ending in an empty desktop slot. Tiled
  panels that are not in the list become floating windows in a small
  diagonal cascade.

Placement never creates or joins tab groups.

Out of scope:
- Engine (#13): computing `stageAspect` / `landscape` from the work area and
  gap, building the front-to-back ordered key list, the per-window
  eligibility flag, and deciding whether a new window is placed at all (float
  by rule, tab join, remembered spot, pending split).
- #22 floating-panels: the float operation and its default rect.
- **Licensing:** the studied app caps the number of docked tiles in "arrange
  all" through licensing entitlements. This is excluded under project rules.
  This spec has **no cap or limit parameter** of any kind. `ArrangeDwindle`
  tiles every eligible listed panel.

## Placement

- Project: `src/CanvasWm.Core` (`net10.0`, BCL only; no new package or project
  references).
- Namespace: `CanvasWm.Core.Layout`.
- Suggested files: `src/CanvasWm.Core/Layout/PlacementOutcome.cs`,
  `src/CanvasWm.Core/Layout/Placement.cs`.
- Tests: `tests/CanvasWm.Core.Tests`, suggested files
  `Layout/ChooseEdgeTests.cs`, `Layout/ChooseAnchorTests.cs`,
  `Layout/PlaceNewTests.cs`, `Layout/ArrangeDwindleTests.cs`,
  `Layout/PlacementPropertyTests.cs`. Reuse the existing test-only
  `SequenceIdGenerator` (returns `"n1"`, `"n2"`, … and counts calls).
- Depends on:
  - `specs/layout-tree.md`: `LayoutNode`, `SplitNode`, `PanelNode`,
    `DesktopNode` (`DefaultId = "desktop"`), `Axis`, `LayoutDocument`,
    `FloatingPanel`, `HiddenPanel`, `HiddenDesktop`,
    `LayoutNormalizer.Normalize`, `IIdGenerator`, `IdPrefixes.Split`.
  - `specs/layout-solver.md`: `LayoutSolver.LayoutRects` (weights mode, no
    metrics), `LayoutEntries`, `LayoutEntry`, `Rect`.
  - `specs/docking.md`: `Edge`, `DockTargetSpec`, `Docking.Dock`,
    `LayoutEdits.LeafIds`, `LayoutEdits.PanelsOf`,
    `LayoutEdits.FindPanelOfWindow`.
  - **No existing type is changed.**

## Public API

All types are public and documented with XML docs. All string comparisons and
id sets are **ordinal**. Null reference arguments that are not marked nullable
throw `ArgumentNullException`.

```csharp
namespace CanvasWm.Core.Layout;

/// <summary>Kind of result of <see cref="Placement.PlaceNew"/>.</summary>
public enum PlacementKind
{
    /// <summary>The window was docked into the tree; Document is the new document.</summary>
    Docked = 0,
    /// <summary>The framed node or the anchor has no layout rect. The caller must float the
    /// window (floating feature, no explicit rect or size). Document is the input instance.</summary>
    NeedsFloat = 1,
}

/// <summary>Result of placing a new window.</summary>
/// <param name="Kind">Docked or NeedsFloat.</param>
/// <param name="Document">Docked: the document after the dock. NeedsFloat: the input
/// document (same instance).</param>
/// <param name="AnchorId">Docked: the anchor node id that was chosen. NeedsFloat: the anchor
/// id when one was chosen but had no rect, otherwise null.</param>
/// <param name="Edge">Docked: the edge passed to Dock (Right or Bottom). NeedsFloat: null.</param>
public sealed record PlacementOutcome(PlacementKind Kind, LayoutDocument Document,
                                      string? AnchorId, Edge? Edge)
{
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public LayoutDocument Document { get; init; }
}
// Compiler-generated equality.

/// <summary>New-window placement and dwindle rebuild.</summary>
public static class Placement
{
    /// <summary>Weight of each child of a dwindle split: 0.5.</summary>
    public const double DwindleWeight = 0.5;
    /// <summary>Leftover cascade base x: 0.1.</summary>
    public const double CascadeBaseX = 0.1;
    /// <summary>Leftover cascade base y: 0.1.</summary>
    public const double CascadeBaseY = 0.1;
    /// <summary>Leftover cascade step in x: 0.03.</summary>
    public const double CascadeStepX = 0.03;
    /// <summary>Leftover cascade step in y: 0.04.</summary>
    public const double CascadeStepY = 0.04;
    /// <summary>Leftover floating width: 0.5.</summary>
    public const double CascadeWidth = 0.5;
    /// <summary>Leftover floating height: 0.55.</summary>
    public const double CascadeHeight = 0.55;
    /// <summary>Cascade wraps after this many leftovers: 6.</summary>
    public const int CascadeCycle = 6;

    /// <summary>Edge rule (B7): Right when (anchor.Height / framed.Height) is less than or equal to
    /// stageAspect * (anchor.Width / framed.Width), otherwise Bottom. Ties and +inf go Right;
    /// NaN anywhere gives Bottom. Never returns Left or Top.</summary>
    public static Edge ChooseEdge(Rect anchor, Rect framed, double stageAspect);

    /// <summary>Anchor id (B4-B5) for placing into the framed node of document.Root, or null when
    /// framedId is not a node of the tree (or the tree is null). focusedKey is optional.</summary>
    public static string? ChooseAnchor(LayoutDocument document, string framedId, string? focusedKey);

    /// <summary>Places windowKey beside the anchor inside framedId (B1-B10). Never throws for
    /// non-null arguments. Docked: calls ids.Make once (twice when Dock falls back).
    /// NeedsFloat: does not call ids.</summary>
    /// <param name="stageAspect">Stage width / height (computed by the Engine). Any double is
    /// accepted; see B7 for non-finite values.</param>
    /// <param name="shareRemovedSpace">Passed through to Docking.Dock.</param>
    public static PlacementOutcome PlaceNew(LayoutDocument document, string windowKey,
                                            string framedId, string? focusedKey,
                                            double stageAspect, IIdGenerator ids,
                                            bool shareRemovedSpace = true);

    /// <summary>Unit-space rect of the leftover with 0-based index i (B17):
    /// k = i mod 6 (non-negative), (0.1 + 0.03k, 0.1 + 0.04k, 0.5, 0.55).</summary>
    public static Rect CascadeRect(int index);

    /// <summary>Rebuilds the tree as a dwindle spiral of the panels holding orderedKeys
    /// (B11-B20). Calls ids.Make exactly once per tiled panel, outermost first.</summary>
    /// <param name="orderedKeys">Window keys in priority order; null elements throw.</param>
    /// <param name="landscape">True: the outermost split is a row (X).</param>
    public static LayoutDocument ArrangeDwindle(LayoutDocument document,
                                                IEnumerable<string> orderedKeys,
                                                bool landscape, IIdGenerator ids);
}
```

## Behaviour

Notation is the same as `specs/docking.md`. `X(s)[...]` / `Y(s)[...]` are
row / column splits with weights after each child. `D` = empty desktop (id
`"desktop"`), `D{x}` = a desktop holding `x`. Letters = one-window panels
`PanelNode.ForWindow(letter)`. `n1`, `n2`, … = ids returned in order by the
injected generator. Rects `(x, y, w, h)` are in unit space.

### General

- **B0. Purity.** All functions are pure. Inputs are never modified and there
  is no static mutable state. The only side effect is calls to `ids.Make`,
  always with `IdPrefixes.Split`.

### Edge rule

- **B7. `ChooseEdge(a, f, A)`.** Compute literally
  `lhs = a.Height / f.Height` and `rhs = A * (a.Width / f.Width)` in IEEE
  double. Return `Edge.Right` if `lhs <= rhs`, else `Edge.Bottom`. So ties go
  Right, and any NaN operand or result gives Bottom (every comparison with NaN
  is false). Left and Top are never returned. No argument validation.
  (Numbering matches findings rule 7.)

### Anchor choice

- **B4. Real-panel set.** `P` = ids of `LayoutEdits.PanelsOf(document.Root)`.
  Floating and hidden panels are not in `P`.
- **B5. `ChooseAnchor(document, framedId, focusedKey)`.** Let `F` be the node
  of `document.Root` whose id is `framedId`. Find it as
  `LayoutSolver.LayoutRects(document.Root)[framedId].Node`; when there is no
  such entry, or the root is null, return **null**. Then use the first rule
  that applies:
  1. `focusedKey` is non-null, `fp = LayoutEdits.FindPanelOfWindow(document,
     focusedKey)` is non-null, `fp.Id ∈ P`, and `fp.Id` is the id of `F` itself
     or of a node in `F`'s subtree (desktop children included) → `fp.Id`.
  2. Scan `LayoutEdits.LeafIds(F)` from **last to first**. The first id in `P`
     is the anchor (the last real panel in the framed area).
  3. `LeafIds(F)` is non-empty → its **last** element (for example an empty
     desktop).
  4. Otherwise (no leaves, e.g. an un-normalised split with no children) →
     `framedId`.
  Recency, size and floating windows play no role. A focused window that is
  floating, hidden, unknown, or outside `F` is ignored.

### Place new

- **B1. Inputs.** `windowKey` may or may not already be in the document (tree,
  floating or hidden). `framedId` may name any tree node kind, including
  `"desktop"`. `focusedKey` is optional.
- **B2. Rect table.** `E = LayoutSolver.LayoutRects(document.Root)` (weights
  mode, unit space) is computed once, on the document **before** any change.
  Every rect below comes from `E`.
- **B3. No framed rect.** If `E` has no entry for `framedId` (this includes a
  null root) → `PlacementOutcome(NeedsFloat, document, null, null)`. The
  document is the same instance and the generator is not called.
- **B6. Anchor.** `anchor = ChooseAnchor(document, framedId, focusedKey)`
  (non-null here). If `E` has no entry for `anchor` →
  `PlacementOutcome(NeedsFloat, document, anchor, null)`. This cannot happen
  for a tree whose ids are unique. It is kept for fidelity.
- **B8. Edge.** `edge = ChooseEdge(E[anchor].Rect, E[framedId].Rect,
  stageAspect)`.
- **B9. Dock.** `doc2 = Docking.Dock(document, windowKey,
  new DockTargetSpec(anchor, edge, Seam: null), ids, shareRemovedSpace)`.
  Return `PlacementOutcome(Docked, doc2, anchor, edge)`. Dock's own rules
  apply unchanged (docking B26–B32). It moves the window's whole existing tab
  group (or a fresh one-window panel), removes it from its old place, mints
  one split id, inserts beside the anchor with fraction 0.5, normalises, and
  falls back to "right of the whole tree" (second id, edge Right whatever the
  computed edge) when the anchor no longer exists after removal.
- **B10. No tabs.** `PlaceNew` never changes any panel's `Views`, `Selected`
  or `Tabbed`, and never merges panels. Anchor and edge are measured on the
  pre-removal tree (B2), even when the window moves from a sibling of the
  anchor.

### Dwindle rebuild

- **B11. Visible keys.** A key is *visible* when it is in the `Views` of a
  panel of `PanelsOf(document.Root)` or of a `document.Floating[i].Panel`
  (A1). A key that is only in `Hidden` (or nowhere) is not visible.
- **B12. Key filter.** Walk `orderedKeys` in order. Keep a key only if it is
  not already kept and it is visible. Result: `K`.
- **B13. Panel list.** For each key of `K` in order, take
  `LayoutEdits.FindPanelOfWindow(document, key)` (a tree or floating panel,
  non-null by B11). Skip it if a panel with the same `Id` is already in the
  list. Result: ordered list `L` of the **same panel instances**. A tab group
  moves as a whole, at the position of its first listed window.
- **B14. Included views.** `S` = the union of `Views` over all panels of `L`.
- **B15. Kept floats.** In list order, keep every `document.Floating` entry
  whose panel has **no** view in `S`, unchanged (same `Rect`, `Z`, `Size`,
  order). Drop the others; they are now tiled.
- **B16. Leftovers.** `leftovers` = the panels of `PanelsOf(document.Root)`
  (pre-order) that have no view in `S`. This includes a panel with empty
  `Views`.
- **B17. Cascade.** For leftover `i` (0-based) append to the floating list
  `new FloatingPanel(panel, CascadeRect(i), z, Size: null)`, where
  `z = 1 + max(Z over the floating list built so far)`, or `1` when that list
  is empty. "Built so far" means the kept floats plus the leftovers already
  appended. `CascadeRect(i)`: `k = i % 6` (for negative `i`, `k = ((i % 6) +
  6) % 6`); `x = 0.1 + 0.03 * k`, `y = 0.1 + 0.04 * k`, `w = 0.5`, `h = 0.55`,
  computed in that form. Final floating list = kept floats (B15), then the
  cascaded leftovers in order.
- **B18. Build.** `build(i, horizontal)`:
  - `i >= |L|` → `new DesktopNode()` (empty, id `"desktop"`), the terminator;
  - otherwise `id = ids.Make(IdPrefixes.Split)` is called **before** the
    recursion, then
    `new SplitNode(id, horizontal ? Axis.X : Axis.Y, [0.5, 0.5],
    [L[i], build(i + 1, !horizontal)])`.
  `newRoot = LayoutNormalizer.Normalize(build(0, landscape))`. Ids are minted
  outermost first, exactly `|L|` calls. Because the axes alternate,
  normalisation changes nothing structurally.
- **B19. Result.** `new LayoutDocument(newRoot, floating (B17),
  document.Hidden, document.HiddenDesktop)`. Hidden panels and the hidden
  desktop record are carried over unchanged (A2). Old split ids, old weights
  and the old desktop's id (when it was not `"desktop"`) are discarded.
- **B20. Shape (consequence).** Panel `i` gets `1/2^(i+1)` of the area along
  a spiral, starting with a row when `landscape` is true. The desktop slot
  shares the last split with the last panel and is the same size. `|L| = 0`
  → root `D`, every tiled panel becomes floating, and the generator is not
  called.

## Edge cases & errors

- **E1.** `ArgumentNullException` for: `ChooseAnchor` `document`/`framedId`;
  `PlaceNew` `document`/`windowKey`/`framedId`/`ids`; `ArrangeDwindle`
  `document`/`orderedKeys`/`ids`, or any null element of `orderedKeys`
  (checked before any generator call); `PlacementOutcome` with null
  `Document`. `focusedKey` accepts null. `ChooseEdge` and `CascadeRect` never
  throw.
- **E2.** `framedId` that names a floating or hidden panel, a range id, or
  anything not in the tree → NeedsFloat (B3).
- **E3.** Null root → `PlaceNew` gives NeedsFloat for every `framedId`.
  `ArrangeDwindle` on a null root builds from floating panels only, and with
  no visible keys gives root `D`.
- **E4.** Re-placing the anchor window itself (focused = itself, or it is the
  last panel): Dock removes it, the anchor vanishes, the fallback puts it
  right of the whole tree, and two ids are consumed (`Edge` in the outcome is
  still the computed edge).
- **E5.** `stageAspect` NaN → Bottom (unless the operands already make
  `lhs <= rhs` impossible to evaluate, which is also Bottom). `+∞` → Right
  whenever `a.Width / f.Width > 0`. Zero-size framed rects yield ∞/NaN
  operands and are handled by B7 literally.
- **E6.** Duplicate ids or a key held by several panels (un-sanitized input)
  are not detected. `LayoutRects` keeps the last entry per id, and
  `FindPanelOfWindow` precedence applies.

## Acceptance criteria

All criteria are xUnit tests in `tests/CanvasWm.Core.Tests` with no OS access
and no TrellisWM. Doubles compare with absolute tolerance `1e-9` unless stated.
"Same instance" = `ReferenceEquals`. `SequenceIdGenerator` counts calls.
Default `shareRemovedSpace = true`.

### Edge rule (findings examples 1–4)
- **AC1** [mac] `ChooseEdge((0,0,.5,1), (0,0,1,1), 1.6)` → Bottom;
  `((.5,.5,.5,.5), (0,0,1,1), 1.6)` → Right. (Ex. 1–2, B7)
- **AC2** [mac] Tie: `a = f = (0,0,1,1)`, `A = 1` → Right; `A = 0.9` →
  Bottom. Zoomed: `a = (.5,.5,.5,.5)`, `f = (.5,0,.5,1)`, `A = 0.5` → Right,
  `A = 0.4` → Bottom. (Ex. 3–4, B7)
- **AC3** [mac] `a = f = (0,0,1,1)`: `A = NaN` → Bottom; `A = +∞` → Right;
  `A = 0` → Bottom. `f = (0,0,1,0)`, `a = (0,0,1,0)`, `A = 1.6` → Bottom (0/0
  is NaN). No call throws, and the result is always Right or Bottom. (B7, E5)

### Anchor choice
- **AC4** [mac] Root `X(s)[a .5, D .5]`: framed `s`, focused `a` → `a`;
  focused null → `a` (leaves `[a, desktop]` scanned from the end). Root `D`,
  framed `"desktop"` → `"desktop"`. Root `D{a}`, framed `"desktop"` → `a`.
  Root `b`, framed `b` → `b`. (Ex. 5–7, B5)
- **AC5** [mac] Root `X(s)[a .5, Y(t)[b .5, c .5] .5]`: framed `t`, focused
  `a` → `c`; framed `t`, focused `b` → `b`; framed `s`, focused null → `c`;
  framed `"zz"` → null. `LayoutDocument.Empty` → null. (Ex. 10, B5)
- **AC6** [mac] Root `X(s)[a .5, b .5]` with floating `f`, framed `s`,
  focused `f` → `b`. Same with `f` hidden → `b`. Focused unknown key `q` →
  `b`. Tab group: root `X(s)[P{id p, views [x, y]} .5, a .5]`, framed `s`,
  focused `y` → `p`. (Ex. 11, B5.1)
- **AC7** [mac] Un-normalised `X(s)[]` (no children) as root, framed `s` →
  `s` (rule 5.4). (B5.4)

### Place new (findings examples 5–13)
- **AC8** [mac] Root `D`, framed `"desktop"`, focused null, place `w`:
  `A = 1.6` → Docked, `X(n1)[D .5, w .5]`, anchor `"desktop"`, edge Right;
  `A = 0.75` → `Y(n1)[D .5, w .5]`, edge Bottom. One generator call each.
  (Ex. 5, B9)
- **AC9** [mac] Root `X(s)[a .5, D .5]`, framed `s`, focused `a`, `A = 1.6`,
  place `b` → `X(s)[Y(n1)[a .5, b .5] .5, D .5]`, edge Bottom. Same with
  focused null → same result. (Ex. 6–7, with the existing split renamed `s`
  so it does not clash with generator ids)
- **AC10** [mac] Root `X(s)[a .5, b .5]`, framed `s`, focused `b`, `A = 1.6`,
  place `c` → `X(s)[a .5, Y(n1)[b .5, c .5] .5]`. On that result place `d`,
  framed `s`, focused `c` → `X(s)[a .5, Y(n1)[b .5, X(n2)[c .5, d .5] .5] .5]`,
  edge Right. (Ex. 8)
- **AC11** [mac] Root `X(s)[a .5, b .5]`, focused null, `A = 1.6`, place `c`:
  framed `s` → `X(s)[a .5, Y(n1)[b .5, c .5] .5]`; framed `b` →
  `X(s)[a .5, b .25, c .25]` (no node `n1`), edge Right. (Ex. 9, B9)
- **AC12** [mac] Root `X(s)[a .5, Y(t)[b .5, c .5] .5]`, framed `t`, focused
  `a`, `A = 1.6`, place `e` →
  `X(s)[a .5, Y(t)[b .5, X(n1)[c .5, e .5] .5] .5]`, anchor `c`, edge Right.
  (Ex. 10)
- **AC13** [mac] Floating `f` focused, root `X(s)[a .5, b .5]`, framed `s`,
  `A = 1.6`, place `c` → as AC11 framed `s`; `f` still floating, unchanged.
  (Ex. 11)
- **AC14** [mac] Re-place `a`: root `X(s)[a .5, b .5]`, framed `s`, focused
  `a`, `A = 1.6` → Docked, edge Bottom, anchor `a`, result `X(n2)[b .5, a .5]`,
  exactly two generator calls. (Ex. 12, E4)
- **AC15** [mac] Framed `"zz"` on root `X(s)[a .5, b .5]` → NeedsFloat,
  `Document` same instance, `AnchorId` null, `Edge` null, zero generator
  calls. Same for `LayoutDocument.Empty` with framed `"desktop"`, and for a
  framed id that names a floating panel. (Ex. 13, B3, E2, E3)
- **AC16** [mac] Window already floating: root `X(s)[a .5, b .5]`, floating
  `F{id w, views [w]}` z 2, place `w`, framed `s`, focused null, `A = 1.6` →
  `X(s)[a .5, Y(n1)[b .5, w .5] .5]` and `Floating` empty (Dock removed it).
  Window in a tab group: floating `F{id p, views [x, y], selected y, tabbed
  true}`, place `x` → the docked panel is the same `F.Panel` instance (views
  `[x, y]`, selected `y`, tabbed true), and no new panel `x` exists. (B9, B10)
- **AC17** [mac] Pre-removal measurement: root `Y(s)[a .5, X(t)[b .5, c .5] .5]`,
  framed `s`, focused `b`, `A = 1.6`, place `c`. Anchor `b` has rect
  `(0,.5,.5,.5)`; `.5 <= 1.6 × .5` → Right, measured before `c` is removed.
  Result `Y(s)[a .5, X(n1)[b .5, c .5] .5]` (the shared space is restored to
  `b`, and then `b` is split again). (B2, B10)
- **AC18** [mac] `shareRemovedSpace` is passed through: root
  `X(s)[a .2, b .3, c .5]`, framed `c`, focused null, `A = 0.5`, place `b`:
  `false` → `X(s)[a .2, Y(n1)[c .5, b .5] .8]`; `true` →
  `X(s)[a 2/7, Y(n1)[c .5, b .5] 5/7]`. (B9; docking AC35)

### Dwindle rebuild (findings examples 14–20, `landscape` true unless stated)
- **AC19** [mac] Root `X(s)[a .5, b .5]`, keys `[]` → root `D`; `Floating`
  = `[a @ (.1,.1,.5,.55) z 1, b @ (.13,.14,.5,.55) z 2]`; zero generator
  calls. (Ex. 14, B20)
- **AC20** [mac] Root `a`, keys `[a]` → `X(n1)[a .5, D .5]`;
  `LayoutRects` gives a `(0,0,.5,1)`, `"desktop"` `(.5,0,.5,1)`. One call.
  (Ex. 15)
- **AC21** [mac] Root `X(s)[a, b, c]` (thirds), keys `[a, b, c]` →
  `X(n1)[a .5, Y(n2)[b .5, X(n3)[c .5, D .5] .5] .5]`; rects a `(0,0,.5,1)`,
  b `(.5,0,.5,.5)`, c `(.5,.5,.25,.5)`, D `(.75,.5,.25,.5)`. With `landscape`
  false → `Y(n1)[a .5, X(n2)[b .5, Y(n3)[c .5, D .5] .5] .5]`. Ids in pre-order
  are `n1, n2, n3` (outermost first). (Ex. 16, B18)
- **AC22** [mac] Root `X(s)[P{id p, views [a, b], selected a, tabbed true}
  .5, c .5]`, keys `[b, c, a]` → `X(n1)[P .5, Y(n2)[c .5, D .5] .5]`, the first
  child is the same `P` instance, two calls. (Ex. 17, B13)
- **AC23** [mac] Root `a`, hidden panel `h` (with any restore target), keys
  `[a, a, h, z]` → `X(n1)[a .5, D .5]`, one call; `Hidden` equals the input
  list. (Ex. 18, B11, B12)
- **AC24** [mac] Root `X(s)[a 1/3, x 1/3, y 1/3]`, no floats, keys `[a]` →
  root `X(n1)[a .5, D .5]`; `Floating = [x @ (0.1, 0.1, 0.5, 0.55) z 1,
  y @ (0.13, 0.14, 0.5, 0.55) z 2]`, both `Size` null. `CascadeRect(0..7)`
  have x `0.1, 0.13, 0.16, 0.19, 0.22, 0.25, 0.1, 0.13` and y `0.1, 0.14,
  0.18, 0.22, 0.26, 0.30, 0.1, 0.14` (tolerance `1e-12`), w `0.5`, h `0.55`.
  `CascadeRect(-1)` equals `CascadeRect(5)`. (Ex. 19, B17)
- **AC25** [mac] Floating `[G{id g, views [g]} z 5, Q{id q} z 3 size (300,200)]`,
  root `x`, keys `[g]` → root `X(n1)[g .5, D .5]` (g is the same instance as
  `G.Panel`); `Floating = [q z 3 size (300,200) rect unchanged,
  x @ (.1,.1,.5,.55) z 4 size null]`. (Ex. 20, B15, B17)
- **AC26** [mac] Seven leftovers: root with 8 panels in a row
  `X(s)[k, l1, …, l7]`, keys `[k]` → 7 floats, z `1..7`; the 7th has
  rect `(0.1, 0.1, 0.5, 0.55)`. With a kept float of z `-3`, the first leftover
  gets z `-2`. (B17)
- **AC27** [mac] `HiddenDesktop` is carried over (`Equals` and the same
  instance). The old desktop's custom id: root `X(s)[a .5, DesktopNode(null,
  "home") .5]`, keys `[a]` → `X(n1)[a .5, D .5]` with desktop id
  `"desktop"`. Root `D{X(s)[a .5, b .5]}`, keys `[b]` →
  `X(n1)[b .5, D .5]`, `a` becomes floating at `CascadeRect(0)` z 1. (B18, B19,
  A2)
- **AC28** [mac] Null element in `orderedKeys` throws `ArgumentNullException`
  and the generator is called zero times. All E1 null cases throw. Inputs are
  unchanged after every call in AC4–AC27 (each equals a deep copy taken
  before). (B0, E1)

### Property test
- **AC29** [mac] Seeded `System.Random` (fixed seed), at least 500 documents
  built like docking AC41: normalised trees (depth ≤ 4, fan-out 2–4, random
  axes, unique ids, panels with 1–3 unique keys, at most one desktop as root
  or leaf), 0–3 floating panels (random z in `[-5, 10]`) and 0–2 hidden
  panels, all ids and keys unique across the document, and an optional
  `HiddenDesktop`. On each:
  1. **PlaceNew**, 5 random calls per document. Use a random `framedId`
     (any tree node id, or an absent id), a random focused key (tree,
     floating, hidden, unknown, or null), a random window key (existing in
     any list, or fresh), and `A` drawn from `[0.2, 5]` plus the specials
     `1`, NaN and `+∞`. After each call:
     (a) NeedsFloat ⇔ `framedId` is not a node id of the tree; in that case
     the document is the same instance and there are 0 generator calls;
     (b) Docked: the tree satisfies layout-tree B14 invariants; the multiset
     of window keys over tree + floating + hidden equals the previous one
     (plus the key once if it was fresh); the set of panel ids is the
     previous set (plus the key if fresh); the placed window's panel is in
     the tree exactly once and `Equals` its pre-call value (or
     `ForWindow(key)` if fresh); every panel's `Views`/`Selected`/`Tabbed`
     is unchanged; `Edge` ∈ {Right, Bottom}; `AnchorId` equals
     `ChooseAnchor(previous, framedId, focused)`; there were 1 or 2
     generator calls; a desktop present before is still present (same id).
  2. **ArrangeDwindle** with a random key list (a random subset and order of
     the document's keys, mixed with duplicates, hidden keys and unknown
     keys) and a random `landscape`. Compute `L` with an independent test
     oracle (B11–B13) and check:
     (a) `PanelsOf(newRoot)` is exactly `L`, in order, as the same instances;
     (b) generator calls `== |L|`, and the split ids in pre-order are the
     generated ids in call order;
     (c) the tree satisfies the B14 invariants; every split has weights
     `[0.5, 0.5]`; axes alternate from the root starting with X iff
     `landscape`; `LeafIds(newRoot)` ends with `"desktop"`, which is the only
     desktop;
     (d) no window lost or duplicated: the multiset of keys over tree +
     floating after equals that over tree + floating before; `Hidden` and
     `HiddenDesktop` are unchanged; the set of panel ids over all lists is
     unchanged;
     (e) the floating list = the kept floats in their original order and
     unchanged, followed by the leftovers in pre-order with
     `Rect == CascadeRect(i)`, `Size` null, and strictly increasing z, each
     `= 1 + max(previous z's)` (or 1);
     (f) the input is unchanged. (B0–B20)

### Architecture
- **AC30** [mac] `dotnet build CanvasWm.slnx` exits 0 with 0 warnings;
  `CanvasWm.Core.csproj` has no `PackageReference`/`ProjectReference`; the
  existing `CoreReferencesNoCanvasWmAssembly` test and all layout-tree,
  layout-solver and docking tests still pass (no existing type changed). By
  inspection, `Placement` has no parameter or constant that limits the number
  of tiles. (CLAUDE.md, Licensing exclusion)

Count: 30 [mac] (AC1–AC30), 0 [windows].

## Assumptions

- **A1. Hidden-window test.** The hidden feature is not specified yet. For the
  dwindle key filter, "not hidden and has a panel" is defined as "held by a
  tree panel or a floating panel" (B11). A key held only by a `Hidden` panel,
  only by `HiddenDesktop.Floats`, or by nothing is dropped. For sanitized
  documents this matches the findings rule exactly. When the hidden feature
  lands with its own predicate, it may replace this. *Needs human review.*
- **A2. Hidden desktop kept unchanged** by `ArrangeDwindle` (findings
  Unknown; default suggested there and approved in scope). *Needs human
  review.*
- **A3. Needs-float outcome instead of floating.** `PlaceNew` returns
  `NeedsFloat` with the unchanged document. The caller (Engine) then invokes
  the #22 float operation with no rect and no size. `AnchorId` / `Edge` in
  the outcome are diagnostics, not part of the studied behaviour.
- **A4. `ChooseAnchor` signature.** It takes the document rather than a
  precomputed rect table (findings suggested `entries` as a parameter). It
  finds the framed node through `LayoutRects` so "has an entry" and "is a
  tree node" are the same test. It returns null instead of a value when the
  framed node is absent.
- **A5. Subtree containment by id.** "The focused panel lies inside the framed
  subtree" is tested by id (`fp.Id` equals the id of the framed node or of
  one of its descendants). This is equivalent for unique ids.
- **A6. Literal edge arithmetic.** The comparison is evaluated exactly in the
  form `a.H / f.H <= A * (a.W / f.W)` so that ties and non-finite cases are
  reproducible. No guard for zero heights in Core (the Engine guards a zero
  stage, as noted in findings).
- **A7. Desktop terminator id.** The rebuilt desktop always has
  `DesktopNode.DefaultId`, even when the old desktop had another id
  (findings rule 17 says "default id").
- **A8. Empty-views tree panels** count as leftovers and are floated
  unchanged (findings: Inferred).
- **A9. Negative cascade index** wraps to `[0, 5]` (CanvasWM addition; only
  reachable through the public `CascadeRect` helper).
- **A10. Null elements in `orderedKeys`** throw before any work (CanvasWM
  choice; the study has no nulls).
- **A11. No tile cap.** The studied "arrange all" limits docked tiles through
  licensing entitlements. This is excluded by project rules and not modelled
  in any form.
- **A12. Example id renaming.** Findings example 6 uses an existing split id
  `n1` that clashes with the generator's first id. AC9 renames it to `s`.
  The expected structure is unchanged.

## Traceability

| Spec item | Findings rule / example |
|---|---|
| B1, PlacementOutcome, A3 | 1, 3; Data model ("needs float" result) |
| B2 | 2 |
| B3, AC15, E2, E3 | 3; Ex. 13 |
| B4 | 4 |
| B5, AC4–AC7, A4, A5 | 5.1–5.4; Ex. 5–7, 10, 11 |
| B6 | 6 |
| B7, AC1–AC3, E5, A6 | 7, 9 (NaN → Bottom); Ex. 1–4 |
| B8, B9, AC8–AC14, AC16, AC18 | 8, 11; Ex. 5–12 |
| B10, AC16, AC17 | 10, 11 (pre-removal measurement, no tabs) |
| E4, AC14 | 11 (re-placing the anchor); Ex. 12 |
| B11, B12, AC23, A1, A10 | 12; Ex. 18 |
| B13, B14, AC22 | 13, 14; Ex. 17 |
| B15, AC25 | 15; Ex. 20 |
| B16, B17, AC19, AC24–AC26, A8, A9 | 16; Ex. 19, 20 |
| B18, AC20, AC21, A7 | 17; Ex. 15, 16 |
| B19, AC27, A2 | 18; Unknowns (hidden desktop) |
| B20, AC19 | 19, 20; Ex. 14 |
| AC29 | 12–20; Windows notes (injected generator) |
| AC30, A11 | 21 (licensing cap excluded); CLAUDE.md |
| Out of scope | 9, 10, 21 (Engine #13); 3 float op (#22) |
