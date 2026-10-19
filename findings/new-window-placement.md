# Feature: new-window-placement

## Summary
Two pure document operations decide where windows go in the tiling tree.
**Place new** takes one window and tiles it next to an *anchor* tile inside the
area the camera is currently framing, splitting the anchor to the **right** if
the anchor looks at least as wide as tall on screen, otherwise **below** it; it
is a thin wrapper over the docking feature's `Dock` (fraction 0.5). **Dwindle
rebuild** ("arrange all") throws the current tree away and rebuilds it from an
ordered window list as a spiral of half/half splits with alternating axes,
ending in an empty desktop slot; tiled panels that are not in the list become
floating windows in a small cascade.

Recommended scope split (both are small, can land together): (a) `PlaceNew`
(anchor + edge choice, then `Dock`), (b) `ArrangeDwindle` (build + leftover
floating). The engine-side policy that decides *whether* a new window is placed
at all (float, native tab join, remembered spot, pending split) and the order
list computation belong to the engine/window-events feature, not here.

## Behaviour

Notation as in `findings/docking.md`: `X(id)[...]` row split, `Y(id)[...]`
column split, weights in the brackets, `D` = desktop node (`"desktop"`), letters
= one-window panels whose id equals the window key. Rects are the weights-only
layout in **unit space** (root rect `(0,0,1,1)`), as produced by the
layout-solver feature's `Entries`/`LayOut` without metrics.

### A. Place new — `PlaceNew(doc, windowKey, framedId, focusedKey?, stageAspect)`
1. **Inputs.** `doc` (the layout document), `windowKey` (window to place; may
   or may not already be in the document), `framedId` (id of the tree node the
   camera is framing — any node kind, including `"desktop"`), `focusedKey`
   (optional window key of the focused window), `stageAspect` (a positive
   double, width ÷ height of the usable screen area — see rule 9). Output: a
   new document. There is no failure result.
2. **Rect table.** Compute the entries (rects) of `doc` as it is *before* any
   change (weights-only unit layout). Every rect used below comes from this
   table.
3. **No framed rect → float.** If `framedId` has no entry, the window is
   floated with no explicit rect or size (the floating feature's `Float(doc,
   window, rect: null, size: null)`), and placement ends.
4. **Real-panel set.** `P` = the set of ids of all panels in the document's
   **tree** (floating and hidden panels are not in it).
5. **Anchor choice** (first that applies):
   1. `focusedKey` is given, the panel that contains it (searched in tree,
      floating and hidden lists) exists, its id is in `P`, **and** that panel
      lies inside the framed subtree (the framed node itself or a descendant)
      → anchor = that panel's id.
   2. Otherwise take the leaf ids of the framed node (docking rule 18: panels,
      empty desktops, in tree order) and scan them **from last to first**; the
      first one whose id is in `P` is the anchor (i.e. the *last* real panel in
      the framed area).
   3. Otherwise (no real panel among them, e.g. the framed area is just an
      empty desktop) → anchor = the **last** leaf id of the framed node.
   4. If the framed node has no leaves at all → anchor = the framed node's own
      id.
   Tie-breaks: none needed — the order is fully determined by tree order. The
   focused window wins over position; "last leaf" wins otherwise. Recency,
   size, and floating windows play no role.
6. **No anchor rect → float.** If the anchor id has no entry, float as in
   rule 3.
7. **Edge.** Let `a` = anchor rect, `f` = framed rect.
   `edge = Right` if `(a.h / f.h) <= stageAspect × (a.w / f.w)`, else
   `edge = Bottom`. Meaning: scale the framed area to fill the stage, then the
   anchor's on-screen height is compared with its on-screen width; a tile that
   is at least as wide as tall is split side by side, a taller one top/bottom.
   **Ties go to Right.** Left and Top are never chosen by this path.
8. **Dock.** Call the docking feature's `Dock(doc, windowKey, target)` with
   `target = DockTargetSpec { Id = anchor, Edge = edge, Seam = null,
   Boundary = null }`. Dock then (docking rules 25–28): moves the window's whole
   existing tab group if it already has one (or makes a fresh one-window panel),
   removes it (honouring `ShareRemovedSpace`), mints one split id, inserts
   beside the anchor with fraction **0.5**, normalises, and falls back to
   "right of the whole tree" (second id) if the anchor no longer exists after
   removal. The new window therefore always takes **half of the anchor's
   share**, and docking right inside an existing row (or bottom inside a
   column) becomes a new equal column/row of that run via flattening.
9. **stageAspect at every call site** (context, for the engine): stage =
   visible work area inset by half the gap on every side, i.e.
   `stageW = max(0, areaW − gap)`, `stageH = max(0, areaH − gap)`,
   `stageAspect = stageW / stageH`. `gap` is a policy value, default **10**.
   (A zero stage height gives an infinite or NaN aspect; with NaN every
   comparison is false → Bottom.)
10. **Callers** (context): engine start-up placement (focused = none), window
    added (focused = the engine's focused window, framed = the navigator's base
    framing), and "toggle docked" / "toggle tab docked" which re-tile a floating
    window with `framedId = "desktop"`. Placement never makes a tab group:
    joining tabs, floating by rule, remembered spots and pending splits are
    decided by the caller before `PlaceNew` is reached.
11. **Edge cases (consequences of 5–8).**
    - Framed area is an **empty desktop** (or the root is an empty desktop):
      anchor = `"desktop"`; with ratios 1 and 1 the edge is Right iff
      `stageAspect >= 1`; the desktop is wrapped: `X(n1)[D .5, w .5]` (or
      `Y(n1)[D .5, w .5]`). The empty-desktop slot survives and keeps half.
    - Framed node is a **single panel**: it is its own only leaf → anchor =
      that panel; ratios 1/1 → Right iff `stageAspect >= 1`.
    - **Re-placing a window that is the anchor** (focused = itself, or it is the
      last panel): Dock removes it first, the anchor id is gone, the fallback
      puts it to the **right of the whole tree** (fallback edge is Right
      regardless of the computed edge) and two split ids are consumed.
    - The anchor rect is measured **before** the window is removed from its old
      place, so when the window moves from a sibling of the anchor, the edge
      decision uses the pre-removal shape.
    - A focused window that is floating, hidden, or outside the framed subtree
      is ignored (rule 5.2 applies).

### B. Dwindle rebuild — `ArrangeDwindle(doc, orderedKeys, landscape, idGen)`
12. **Key filter.** Walk `orderedKeys` in order; keep a key only if it is not
    already kept, a panel containing it exists in the document (tree, floating
    or hidden), and it is **not hidden** (the hidden-window test of the hidden
    feature). Result: `K`.
13. **Panel list.** For each key of `K` in order, take the panel that contains
    it (a fresh one-window panel if none — unreachable after rule 12); skip it
    if a panel with the same id is already in the list. Result: ordered panel
    list `L`. Whole tab groups move as one, at the position of their first
    listed window; panel id, views, selected view and tabbed flag are kept.
14. **Included views.** `S` = all window keys of all panels in `L`.
15. **Floating list.** Keep every existing floating panel that has **no** view
    in `S`; drop the others (they are now tiled). Kept floats retain their rect,
    z, size and order.
16. **Leftover tiled panels → floating.** For each panel of the current tree
    (tree order) that has **no** view in `S` (a panel with no views at all also
    counts as leftover), append a floating panel with: the panel unchanged;
    rect in desktop-unit space `x = 0.1 + 0.03·k`, `y = 0.1 + 0.04·k`,
    `w = 0.5`, `h = 0.55`, where `k = i mod 6` and `i` is the 0-based index of
    the panel among the leftovers; `z = 1 + (largest z in the floating list so
    far, 0 if empty)` (so successive leftovers get increasing z); stored size =
    none. They cascade diagonally and wrap every 6 windows.
17. **Build.** `build(i, horizontal)`:
    - if `i >= |L|` → an **empty desktop node** with the default id
      `"desktop"` (the terminator);
    - else mint a split id from the generator (prefix `"split"`) **before**
      recursing, then return `Split { id, axis = X if horizontal else Y,
      weights = [0.5, 0.5], children = [L[i], build(i+1, !horizontal)] }`.
    Root = `Normalize(build(0, landscape))`. Ids are therefore minted
    outermost first, one per panel. Because the axes alternate, normalisation
    changes nothing (no same-axis nesting, weights already sum to 1).
18. **What is discarded.** The old tree is not consulted beyond rule 16: all
    old split ids and old weights are lost; the desktop node is recreated empty
    at the deepest position (if the old desktop held a child, that child's
    panels are either in `L` or became leftovers). Hidden panels are untouched.
19. **Empty list.** `|L| = 0` → root = empty desktop; every tiled panel becomes
    floating (rule 16).
20. **Shape.** Panel `i` (0-based) gets `1/2^(i+1)` of the area along a spiral:
    with `landscape` = true the first split is a row (`X`), then column, row, …;
    the desktop slot shares the last split with the last panel and has the same
    size as it.
21. **Engine context ("arrange all"):** `landscape` = `stageW >= stageH`
    (stage as in rule 9; a square stage counts as landscape). The ordered
    keys are: the window system's **front-to-back** window list filtered to
    windows that have a panel, are not hidden and are eligible per a per-window
    flag the engine keeps (meaning not identified), followed by every other
    window of the document (document order) not already listed. A
    policy-level cap on the number of docked tiles also applies here; it is
    driven by the licensing entitlements and is **out of scope** (treat as
    unlimited).

## Data model
- Reuses `Document`, `Node` (Split/Panel/Desktop), `Axis {X, Y}`, `Edge {Left
  = 0, Right = 1, Top = 2, Bottom = 3}`, `DockTargetSpec`, `FloatingPanel {
  Panel, Rect (unit space of the desktop), Z: int, Size?: Size }`, the id
  generator and `Entries` from earlier features.
- New pure functions (Core):
  - `ChooseAnchor(doc, entries, framedId, focusedKey?) → string?`
  - `ChooseEdge(Rect anchor, Rect framed, double stageAspect) → Edge`
  - `PlaceNew(doc, windowKey, framedId, focusedKey?, stageAspect, idGen,
    shareRemovedSpace) → Document` (float branch delegates to the floating
    feature; until that exists, return a "needs float" result or call a
    placeholder).
  - `ArrangeDwindle(doc, orderedKeys, landscape, idGen) → Document`.
- Engine-side helpers (not Core): `StageAspect(workArea, gap)`,
  `IsLandscape(workArea, gap)`, order-list assembly.
- Constants: dock fraction 0.5; dwindle weights `[0.5, 0.5]`; leftover float
  rect base `(0.1, 0.1)`, step `(0.03, 0.04)`, size `(0.5, 0.55)`, cycle 6;
  default gap 10.

## Worked examples
Generator yields `n1, n2, …` in order. `A` = stageAspect. `shareRemovedSpace` =
true unless stated. Rects `(x, y, w, h)` in unit space.

Edge rule
1. `ChooseEdge(a=(0,0,.5,1), f=(0,0,1,1), A=1.6)`: `1 <= 0.8`? no → Bottom.
2. `a=(.5,.5,.5,.5)`, `f=(0,0,1,1)`, `A=1.6`: `.5 <= .8` → Right.
3. Tie: `a=f=(0,0,1,1)`, `A=1` → `1 <= 1` → Right. `A=0.9` → Bottom.
4. Zoomed: `a=(.5,.5,.5,.5)`, `f=(.5,0,.5,1)`, `A=0.5`: `.5 <= .5×1` → Right;
   `A=0.4` → Bottom.

Place new
5. Root `D` (empty), framed `"desktop"`, focused none, place `w`, `A=1.6` →
   `X(n1)[D .5, w .5]`. Same with `A=0.75` → `Y(n1)[D .5, w .5]`.
6. Root `X(n1)[a .5, D .5]`, framed `n1`, focused `a`, `A=1.6`, place `b` →
   anchor a, Bottom → `X(n1)[Y(n2)[a .5, b .5] .5, D .5]`.
7. Same as 6 but focused none → leaves `[a, desktop]` scanned from the end,
   `desktop` not a panel, `a` is → same result as 6.
8. Root `X(s)[a .5, b .5]`, framed `s`, focused `b`, `A=1.6`, place `c` →
   b rect `(.5,0,.5,1)` → Bottom → `X(s)[a .5, Y(n1)[b .5, c .5] .5]`.
   Then place `d`, focused `c` (rect `(.5,.5,.5,.5)`) → Right →
   `X(s)[a .5, Y(n1)[b .5, X(n2)[c .5, d .5] .5] .5]`.
9. Root `X(s)[a .5, b .5]`, focused none, `A=1.6`, place `c`:
   framed `s` → anchor b, Bottom → `X(s)[a .5, Y(n1)[b .5, c .5] .5]`;
   framed `b` → anchor b, ratios 1/1 → Right →
   `X(s)[a .5, b .25, c .25]` (`n1` flattened away).
10. Root `X(s)[a .5, Y(t)[b .5, c .5] .5]`, framed `t`, focused `a` (outside
    `t`) → anchor = last panel of `t` = c, `c=(.5,.5,.5,.5)`, `f=(.5,0,.5,1)`,
    `A=1.6`: `.5 <= 1.6` → Right → `X(s)[a .5, Y(t)[b .5, X(n1)[c .5, e .5] .5] .5]`
    for new window `e`.
11. Focused window `f` is floating; root `X(s)[a .5, b .5]`, framed `s` →
    focused ignored, anchor b (as example 9).
12. Re-place `a` with focused `a`: root `X(s)[a .5, b .5]`, framed `s`,
    `A=1.6` → edge Bottom computed, Dock removes a → `b`, anchor gone →
    fallback Right of root → `X(n2)[b .5, a .5]` (n1 minted and unused).
13. `framedId = "zz"` (no entry) → float path; tree unchanged.

Dwindle (`landscape` true unless stated)
14. `L = []` → root `D`.
15. keys `[a]` → `X(n1)[a .5, D .5]`; rects a `(0,0,.5,1)`, D `(.5,0,.5,1)`.
16. keys `[a, b, c]` → `X(n1)[a .5, Y(n2)[b .5, X(n3)[c .5, D .5] .5] .5]`;
    rects a `(0,0,.5,1)`, b `(.5,0,.5,.5)`, c `(.5,.5,.25,.5)`,
    D `(.75,.5,.25,.5)`. Portrait: `Y(n1)[a, X(n2)[b, Y(n3)[c, D]]]`, all .5.
17. Tab group `p{views [a, b]}` plus `c`; keys `[b, c, a]` → panels `[p, c]` →
    `X(n1)[p .5, Y(n2)[c .5, D .5] .5]` (p keeps id, views, selection, tabbed).
18. keys `[a, a, h, z]` where h is hidden and z is unknown → `L = [a]`.
19. Tree `X(s)[a 1/3, x 1/3, y 1/3]`, keys
    `[a]`, empty floating list → root `X(n1)[a .5, D .5]`; floating =
    `[x @ (0.1, 0.1, 0.5, 0.55) z 1, y @ (0.13, 0.14, 0.5, 0.55) z 2]`, no size.
    The 7th leftover (k = 6 mod 6 = 0) reuses `(0.1, 0.1)`.
20. Existing floats `[g z 5 (view in keys), q z 3]`, keys `[g]`, tree has `x`
    → floating = `[q z 3, x @ (0.1,0.1,0.5,0.55) z 4]`; root `X(n1)[g .5, D .5]`.

## Evidence
- Rules 1–3, 5–8: `Arrangement.placeNew(_:_:framed:focused:stageAspect:)` @
  0x1000162bc, `evidence/dec-placeNew.json` (entries lookup → `float(rect: nil,
  size: nil)`; `panelsOf(doc.root)` → `Set`; `panelOfView` + `Set.contains` +
  `containsNode(framed, panel)`; reverse `leafIds` scan; last-leaf / framed-id
  fallback; edge `3` default, `1` when `anchor.h/framed.h <= aspect ×
  anchor.w/framed.w`; `DockTargetSpec` → `Arrangement.dock(_:_:at:)`). Field
  mapping of the `Entry` rect (x, y, w, h after node) from consistent stack
  offsets of the framed and anchor entries.
- Rule 4: same function (`panelsOf` on `*doc`, i.e. the root).
- Rule 8: `findings/docking.md` rules 10, 25 (dock passes 0.5).
- Rule 9: call sites 0x100035d5c, 0x1000ece40, 0x1000aa640, 0x1000b8bc8 —
  `evidence/objdump-placement-constants.json`; `Engine.stage` getter
  @ 0x10002c898; Engine field offsets `visibleArea` 0x20, `policy` 0x80;
  `Policy.gap` getter reads offset 0, default 10 @ 0x100017444.
- Rule 10: same call-site table; `Engine.windowAdded(_:held:)` callees
  (`joinTabGroup`, `takeSplit`, `DockSpot`, `float`, `Scene.desktopUsable`,
  `placeNew`) from objdump (names only).
- Rules 12–20: `arrangeDwindle(_:_:landscape:)` specialised @ 0x100021604 and
  `build #1 (_:horizontal:)` @ 0x100016938 — `evidence/dec-arrangeDwindle.json`
  (new) and `evidence/dec-dwindleBuild.json`; weights static [0.5, 0.5] @
  0x100260558; leftover rect constants (0.1; 0.03/0.04 @ 0x100211be0;
  0.5/0.55 @ 0x100211bf0; `mod 6` @ 0x100022280) and z = max+1 loop —
  `evidence/objdump-placement-constants.json`.
- Rule 21: `Engine.arrangeAll()` @ 0x1000addf0 — `evidence/dec-arrangeAll.json`
  (new); landscape `fcmge` @ 0x1000ae5b4; window-system witness slot +0x20.

## Confidence
| Rule | Confidence |
|---|---|
| 1 (inputs/outputs), 2, 3, 4 | Observed |
| 5.1–5.3 | Observed |
| 5.4 (framed with no leaves → its own id) | Observed (practically unreachable) |
| 6 | Observed |
| 7 formula and tie → Right | Observed operands; "on-screen" interpretation Inferred |
| 8 | Observed (composition with docking) |
| 9 | Observed arithmetic; "work area inset by gap" naming Inferred |
| 10 | Observed call sites; caller decision tree Inferred from callee names |
| 11 | Inferred (consequences of observed rules) |
| 12, 13, 14, 15 | Observed |
| 16 rect/z/size constants | Observed; "unit space of the desktop" Inferred (matches floating mapping in layout-solver rule 37); empty-view panels counted as leftover Inferred |
| 17 | Observed (id minted before recursion, axis = X when horizontal) |
| 18 | Observed for tree/ids; hidden-desktop record handling Unknown |
| 19, 20 | Inferred (consequences) |
| 21 landscape | Observed |
| 21 order source | Inferred (`frontToBack` is the only list-returning window-system requirement); eligibility flag meaning Unknown |

## Unknowns
- **Hidden-desktop record** (`HiddenDesktop?`) after a dwindle rebuild: kept or
  cleared could not be read reliably from the heavily inlined copy code.
  Default suggestion: keep it unchanged.
- **Per-window eligibility flag** used by "arrange all" (ManagedWindow field at
  +0x78) — meaning not identified (maybe "is a standard/tileable window").
- **Float branch**: `Float(doc, window, rect: null, size: null)` belongs to the
  floating feature; its default rect is not described here.
- **Docked-tile cap** in "arrange all" comes from licensing entitlements — out
  of scope by project rules; not analysed.
- Whether the camera stretches or letter-boxes the framed area: the edge rule
  is exact as stated regardless; only the "on-screen" explanation depends on it.
- The window system's front-to-back list exact semantics on macOS (all spaces vs
  current space) — engine concern.

## Windows notes
- Everything in A (except computing `stageAspect`) and B is pure tree work for
  `CanvasWm.Core`; inject the id generator so tests can assert `n1, n2, …`.
- `stageAspect` / `landscape`: compute in the Engine from the monitor work area
  (`MONITORINFO.rcWork` via `IWindowSystem`) in device-independent units, minus
  the gap setting (default 10). Use the same rect for the camera so the edge rule
  matches what the user sees. Guard zero height (treat as Bottom / portrait).
- Front-to-back order on Windows: top-level z-order (`EnumWindows` /
  `GetWindow(GW_HWNDNEXT)`) filtered to managed windows; expose as
  `IWindowSystem.FrontToBack()`.
- Leftover floating rects are fractions of the desktop slot; the cascade must be
  mapped through the same desktop-rect mapping the solver uses for floats.
- No tab grouping on placement; Windows has no native tab groups to join, so the
  caller's "join tab group" branch will normally not apply.
