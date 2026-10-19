# Feature: docking

## Summary
Docking moves a tab group (panel) to a new place in a display's layout tree.
It is built from four pure tree operations: **remove** a node (two variants
that differ only in who inherits the freed space), **insert beside** a target
node on one of its four edges, **insert at a seam** (drop on the line between
tiles of a split), and **dock**, which removes the window's panel and then
inserts it with insert-beside or insert-at-seam, falling back to "right of
(or below) the whole tree" when the target no longer exists. Every structural
edit finishes with the layout-tree `Normalize` pass, which is what turns
"dock right of a tile that is already in a row" into a new column of that row.

Tab grouping (drop onto the middle of a tile), "redock" (put a window back in
its remembered spot), `insertPanel`/`DockTarget` and drop-target hit testing
are related but **separate features** (see Unknowns / recommended split).

## Behaviour

Notation: `X[...]` = split on axis x (row), `Y[...]` = split on axis y
(column); weights in parentheses; `D` = desktop node; letters = one-window
panels whose id is the window key. Edge raw values: 0 left, 1 right, 2 top,
3 bottom (from the study).

### A. Remove a node — two variants
Both take `(node?, id)` and return `node?`; they recurse over the whole tree.

1. **Matching rule (both variants).** If the current node's id equals `id`:
   a desktop node is **never removed** (returned unchanged); any other node
   (panel or split, matched by its own id) is removed (returns nothing). So
   removal works for any node id, not only panels — removing a split id
   removes its whole subtree.
2. **Panel** that does not match → unchanged. **Nothing in** → nothing out.
3. **Desktop** that does not match: no child → unchanged. Child whose id
   matches → the desktop is kept with **no child** (empty desktop, i.e. free
   space). Otherwise recurse into the child; child vanished → empty desktop;
   child unchanged → original desktop returned; child changed → desktop with
   same id and new child.
4. **Split, a direct child matches** (first matching child, index `k`):
   remove the child and its weight `w`.
   - *Adjacent variant* (`removeNode`): `w` is **added to the sibling that now
     sits at index `k`** (the one that was right after it); if the removed
     child was the last one, to the new last child (the one before it).
   - *Sharing variant* (`removeSharing`): `w` is simply discarded; the
     renormalisation step then spreads the freed space over **all**
     remaining siblings in proportion to their weights.
   - If no children remain, the split is removed (returns nothing). Otherwise
     a split with the **same id and axis** is rebuilt and `Normalize`d
     (so a split left with one child collapses into that child; a same-axis
     child is flattened).
5. **Split, no direct child matches:** recurse into every child, keeping the
   original weights. A child that comes back *changed* keeps its weight.
   A child that *vanished* completely:
   - adjacent variant: its weight is added to the previous surviving child,
     or, if there is none yet, to the next surviving child;
   - sharing variant: dropped with its weight.
   If nothing changed, the original split is returned untouched; else the
   split (same id/axis) is rebuilt and `Normalize`d.
6. **Document-level remove panel** (`removePanel(doc, panelId)`): locate the
   panel first — in the tree, else in the floating list, else in the hidden
   list. Tree → `root = removeNode(root, panelId)` (adjacent variant). Floating
   or hidden → the entry with that panel id is removed from that list. Not
   found → document unchanged. Normalize is not called again here (it already
   ran inside the split rebuilds).
7. **Which variant dock uses.** Dock consults the global flag
   `shareRemovedSpace`. If it is **true** *and* the panel is in the tree, the
   tree root is replaced by `removeSharing(root, panelId)`. In every other
   case (flag false, or the panel is floating/hidden/absent) it uses
   `removePanel(doc, panelId)` from rule 6.
8. **Default of `shareRemovedSpace` is true.** The policy field initialises to
   true and the user preference of the same name reads as true when the key is
   missing or not a boolean. So by default a removed tile's space is shared
   proportionally by all its siblings.

### B. Insert beside a target — `insertBeside(node?, targetId, newNode, edge, splitId, f)`
9. **Empty tree:** if `node` is nothing, the result is `newNode` itself.
10. **Fraction clamp:** `f` is clamped to **[0.1, 0.9]**. Every caller found
    passes **f = 0.5** (dock, its root fallback, the seam code, and the
    model-level `applyDockTarget` helper).
11. **Match test comes first, for any node kind:** if the current node's id
    equals `targetId` (panel, split or desktop), it is replaced by a new split:
    - id = `splitId` (supplied by the caller, minted from the id generator
      with prefix `"split"`);
    - axis = y if edge is top/bottom, x if left/right;
    - new node first if edge is left/top, target first if right/bottom;
    - weights: new node gets `f`, target gets `1 − f`, in child order.

      | edge | axis | children | weights |
      |---|---|---|---|
      | left | x | [new, target] | [f, 1−f] |
      | right | x | [target, new] | [1−f, f] |
      | top | y | [new, target] | [f, 1−f] |
      | bottom | y | [target, new] | [1−f, f] |

    The new split is then `Normalize`d. Consequence: if the target is itself
    a split on the **same axis** as the new split, the target is flattened
    into the new split (its children scaled by `1 − f`) and the **target
    split's id disappears**, the new split keeping `splitId`.
12. **No match, panel** → unchanged.
13. **No match, desktop:** no child → unchanged; with a child → recurse; if
    the child changed, rebuild the desktop with the same id (no normalise at
    this level); else unchanged.
14. **No match, split:** recurse into **every** child with the same arguments.
    If any child changed, rebuild the split with the same id, axis and
    **original weights** and the new children, then `Normalize`. Because the
    changed child's weight is kept, the wrapped target's former share is now
    divided `1 − f : f` between target and new node. If the new wrapper split
    has the same axis as this split, `Normalize` flattens it into this split
    and the wrapper's `splitId` vanishes (this is the "add a column to the
    existing row" behaviour). If nothing changed, the split is unchanged.
15. **Target not found** anywhere → tree returned unchanged (no error).
    Callers detect this afterwards (rule 25).

### C. Seams — `SplitSeam` and `seamTarget`
16. **Seam value:** `SplitSeam { axis, leaves: [id], before: [id] }`. It is a
    *snapshot by leaf ids*, not a reference to a split id or a solver range id.
17. **Building a seam** (`seamTarget(split, index)` → dock target spec):
    `axis` = the split's axis; `leaves` = all leaf ids of the split (in order);
    `before` = leaf ids of the split's first `index` children (index is
    clamped to the child count). The returned target spec has `id` = the
    split's id and `edge` = left (axis x) or top (axis y); no boundary.
    `index` 1..n−1 are the inner dividers; 0 and n are the split's outer
    edges.
18. **Leaf ids** (`leafIds`): nothing → []; panel → [panel id]; desktop with
    no child → [desktop id]; desktop with a child → leaf ids of the child;
    split → concatenation over children in order.

### D. Insert at a seam — `insertAtSeam(root, seam, newNode, splitId)`
19. **Live set:** `S` = leaf ids of the current `root` that are listed in
    `seam.leaves` (leaves that no longer exist are ignored). If `S` is empty
    → `root` returned unchanged.
20. Otherwise a recursive insert (below) is applied to `root`, then the result
    is `Normalize`d; if normalisation yields nothing, the original `root` is
    returned.
21. **Recursive insert — descend:** in a split, if one child's leaf ids
    contain **all** of `S`, insert into that child only and rebuild the split
    with the same id, axis and weights. A desktop whose child contains all of
    `S` (and whose own id is not in `S`) recurses into its child and is rebuilt
    with the same id.
22. **Recursive insert — split that `S` spans (several children touch `S`):**
    let `first`/`last` be the first and last child indices that contain any
    leaf of `S`; the *range* is every child from `first` to `last` inclusive
    (`m = last − first + 1` children, including untouched ones in between);
    `W` = sum of their weights.
    - **Split axis = seam axis:** the insertion index is the first child in
      the range that contains a leaf **not** in `seam.before` (i.e. the first
      child after the divider). Each range child's weight is multiplied by
      `1 − 1/(m+1)`, and the new node is inserted at the insertion index with
      weight `W/(m+1)`. Children outside the range are untouched. Split id
      and axis are kept. (Net effect: the new tile takes an equal
      `1/(m+1)` share of the space of the tiles that bordered the seam.)
    - **Split axis ≠ seam axis:** the range children are wrapped in a group
      split with id `splitId + "-range"`, the parent's axis, and their weights
      divided by `W`; the new node is placed beside that group with
      insert-beside (rule 11, `f = 0.5`, id `splitId`), the edge chosen as in
      rule 23; the result replaces the range in the parent with weight `W`.
23. **Recursive insert — leaf** (a panel, an empty desktop, or a node that
    does not contain all of `S` and is not a spanning split): insert beside the
    node itself with `f = 0.5` and id `splitId`. Edge: if **any** leaf of `S`
    is in `seam.before`, the new node goes **after** (right for seam axis x,
    bottom for y); otherwise **before** (left / top).
24. A fallback path for a same-axis split with no child touching `S` (not
    reachable when rule 21 is followed) inserts with weight `1/(n+1)` and
    scales all `n` normalised siblings by `n/(n+1)`.

### E. Dock — `dock(doc, windowKey, target)`
25. Steps (Observed order):
    1. **Find the unit:** the panel that contains `windowKey` anywhere in the
       document. If none, a fresh one-window panel (`id = views[0] =
       selected = windowKey`, not tabbed). The whole tab group moves; its id,
       views, selection and tabbed flag are preserved.
    2. **Remove it** (rule 7).
    3. **Mint one split id** (`"split"` prefix) — always, even when unused.
    4. **Insert** into the post-removal root:
       - root is empty → the panel becomes the root;
       - target has a seam → `insertAtSeam(root, seam, panel, splitId)`
         (target `id` and `edge` are ignored);
       - else → `insertBeside(root, target.id, panel, target.edge, splitId,
         0.5)`.
    5. **Verify:** collect the panels of the resulting tree. If the panel id
       is present → done. If not (target id missing, seam leaves all gone,
       etc.) → **fallback:** mint a second split id and
       `insertBeside(result, <id of the result's root node>, panel, edge, id2,
       0.5)` where edge = **bottom** if the target carried a seam whose axis is
       y, otherwise **right**. Because the root is always matched, this
       always succeeds; the new split wraps the whole tree (and, if the root
       already has that axis, the root is flattened into it so the root id
       becomes `id2`).
    6. The rest of the document (floating, hidden, hidden-desktop lists) is
       carried over unchanged except for the removal in step 2.
26. **Self/neighbour cases** (consequences of 25):
    - Docking a panel beside **itself**: after removal the target id no
      longer exists → fallback → the panel ends up at the right of (or below)
      the whole tree taking half of it.
    - Docking beside a **split id** that collapsed because of the removal
      (e.g. its parent split now has one child) → same fallback.
    - Docking beside its **own neighbour** works normally; the neighbour's
      weight already includes the freed space from step 2 before it is split.
    - Docking onto a seam it was itself part of works: its own leaf id is
      just absent from the live set (rule 19).
27. **Dock a detached unit** (`dock(doc, unit: panel, at:)`): same as rule 25
    steps 3–5 but **without** step 1–2 (no lookup, no removal). Used when the
    panel is not currently in the document (or has already been taken out).
28. **Return value:** a new document; there is no failure result. All no-op
    cases are expressed as "unchanged tree" from the primitives plus the root
    fallback in dock.

### F. Ids — what survives
29. Panel ids never change during dock (the moved panel keeps its id).
30. A split rebuilt by remove/insert keeps its id; ids are lost only through
    `Normalize` (collapse of a one-child split → parent id lost; same-axis
    flatten → the inner split's id lost, the outer one kept).
31. Dock consumes exactly one generated id, plus a second one only when it
    falls back. The seam cross-axis group id is derived (`<id>-range`), not
    generated.

### G. Callers (context only)
32. New-window placement (#4) calls dock with an edge target (right/bottom,
    never a seam). Drag-and-drop and seam drops build targets with
    `seamTarget` or an id+edge; hit-testing (`tileDropTarget`,
    `frameDropTarget`, `Engine.seam(at:)`, `Feel.minSeamHitWidth`) is out of
    scope here.

## Data model
- `Edge` = { Left = 0, Right = 1, Top = 2, Bottom = 3 }.
- `SplitSeam { Axis Axis; string[] Leaves; string[] Before }`.
- `DockTargetSpec { string Id; Edge Edge; SplitSeam? Seam; int? Boundary }`
  (`Boundary` is not read by dock; meaning Unknown).
- `PanelLocation` = Tiled(panel) | Floating(...) | Hidden(...) (tag order
  0/1/2; payload details belong to floating/hidden features).
- Policy flag `ShareRemovedSpace : bool = true`.
- Pure functions (Core): `LeafIds(node?)`, `PanelsOf(node?)`,
  `RemoveNode(node?, id)`, `RemoveSharing(node?, id)`,
  `RemovePanel(doc, panelId)`, `InsertBeside(node?, targetId, newNode, edge,
  splitId, f)`, `SeamTarget(split, index)`, `InsertAtSeam(root, seam, newNode,
  splitId)`, `Dock(doc, windowKey, target, idGen, shareRemovedSpace)`,
  `DockUnit(doc, panel, target, idGen)`.

## Worked examples
Ids: `s`, `t` existing splits; `n1`, `n2` ids returned by an injected
generator in order. All results are after normalisation.

Insert beside
1. `insertBeside(a, "a", n, right, n1, 0.5)` → `X(n1)[a .5, n .5]`.
2. Same with left → `X(n1)[n .5, a .5]`; top → `Y(n1)[n .5, a .5]`;
   bottom → `Y(n1)[a .5, n .5]`.
3. `f = 0.05`, right → clamped to 0.1 → `X(n1)[a .9, n .1]`; `f = 0.95` →
   `X(n1)[a .1, n .9]`.
4. `X(s)[a .5, b .5]`, target b, right → `X(s)[a .5, b .25, n .25]`
   (`n1` vanished by flattening).
5. `X(s)[a .5, b .5]`, target b, bottom → `X(s)[a .5, Y(n1)[b .5, n .5] .5]`.
6. `X(s)[a .5, b .5]`, target `s`, right → `X(n1)[a .25, b .25, n .5]`
   (`s` vanished).
7. `X(s)[a 1/3, b 1/3, c 1/3]`, target b, right →
   `X(s)[a 1/3, b 1/6, n 1/6, c 1/3]`.
8. `Y(s)[a .6, D .4]` (empty desktop), target `desktop`, right →
   `Y(s)[a .6, X(n1)[D .5, n .5] .4]`.
9. Target `"zz"` absent → tree unchanged. Empty tree → result is `n`.

Remove
10. Adjacent: `X(s)[a .2, b .3, c .5]` remove b → `X(s)[a .2, c .8]`;
    remove c → `X(s)[a .2, b .8]`; remove a → `X(s)[b .5, c .5]`.
11. Sharing: `X(s)[a .2, b .3, c .5]` remove b → `X(s)[a 2/7, c 5/7]`
    (≈ .285714, .714286).
12. `X(s)[a .5, Y(t)[b .5, c .5] .5]` remove b (either variant) →
    `X(s)[a .5, c .5]` (`t` collapsed).
13. `D{child: a}` remove a → `D{}` (empty desktop). Remove `"desktop"` →
    unchanged. Root `a` remove a → no tree.
14. `X(s)[a .5, Y(t)[b, c] .5]` remove `"t"` → `a` (s collapsed).

Seams
15. `seamTarget(X(s)[a, b, c], 1)` → `{id: s, edge: left, seam: {x,
    [a,b,c], before [a]}}`; index 0 → before `[]`; index 3 → before
    `[a,b,c]`; for a `Y` split the edge is top.
16. `insertAtSeam(X(s)[a .5, b .5], {x,[a,b],[a]}, n, n1)` →
    `X(s)[a 1/3, n 1/3, b 1/3]`.
17. `X(s)[a .5, b .25, c .25]`, seam index 1 (`[a,b,c]`, before `[a]`) →
    m = 3, new = 1/4 → `X(s)[a .375, n .25, b .1875, c .1875]`.
18. `X(s)[b .6, c .4]`, seam `{x,[a,b,c],[a,b]}` (a already gone) → live set
    {b,c}, m = 2, insertion before c → `X(s)[b .4, n 1/3, c .2(6)]`.
19. Root is the panel `b`, seam `{x,[a,b],[a]}` → live set {b}; b not in
    before → left → `X(n1)[n .5, b .5]`.
20. Cross axis: `Y(u)[b .5, c .25, d .25]`, seam `{x,[a,b,c],[a]}` →
    live {b,c} spans Y (axis y ≠ x); no live leaf in before → left →
    `Y(u)[X(n1)[n .5, Y(n1-range)[b 2/3, c 1/3] .5] .75, d .25]`.

Dock (default `shareRemovedSpace = true`)
21. `X(s)[a .5, b .25, c .25]`, dock window a onto seam index 2 of s
    (`{x,[a,b,c],[a,b]}`): remove a (sharing) → `X(s)[b .5, c .5]`; insert →
    `X(s)[b 1/3, a 1/3, c 1/3]`. One id consumed.
22. `X(s)[a .5, b .5]`, dock a beside b, right → remove a → `b`; →
    `X(n1)[b .5, a .5]`.
23. `X(s)[a .5, b .5]`, dock a beside a, right → remove a → `b`; a not found
    → fallback right of root → `X(n2)[b .5, a .5]` (two ids consumed).
24. `X(s)[a .5, b .5]`, dock a beside `"zz"`, edge left → fallback is still
    **right**: remove a → `b` → `X(n2)[b .5, a .5]`.
25. Seam with axis y whose leaves are all gone → fallback edge bottom.
26. Window w not in any panel, empty document → root becomes panel w.
27. `X(s)[a .2, b .3, c .5]`, dock b beside c, bottom, with flag **false**:
    remove b adjacent → `X(s)[a .2, c .8]` → `X(s)[a .2, Y(n1)[c .5, b .5] .8]`.
    With flag true: `X(s)[a 2/7, Y(n1)[c .5, b .5] 5/7]`.

## Evidence
- Rules 1–5: `removeNode(_:_:)` @ 0x10010e468 and `Arrangement.removeSharing`
  @ 0x10000e10c — `evidence/dec-docking-batch1.json` (decompiled). Neighbour
  index `min(k, count−1)` and `+=` of the removed weight; sharing variant only
  `Array.remove` of the weight then `normalize`; desktop-keeps-on-match
  (`tag == 2 → return self`).
- Rule 6: `removePanel(_:_:)` @ 0x1000f94a4 (batch1): `locatePanel`, tag 0 →
  `removeNode` on root, tag 1 → FloatingPanel list filter, tag 2 →
  HiddenPanel list filter. `locatePanel` @ 0x1000f85f4 (batch1): `findNode`,
  then floating (stride 0x70), then hidden (stride 0x68).
- Rule 7: `Arrangement.dock(_:_:at:)` @ 0x1000156b0, `evidence/dec-dock.json`
  (flag test, `locatePanel` tag check, `removeSharing` vs `removePanel`).
- Rule 8: `evidence/objdump-docking-constants.json` (Policy initialiser
  @ 0x100017594 returns 1; `Preferences.shareRemovedSpace` @ 0x1001bffd0
  returns 1 when absent).
- Rules 9–15: `insertBeside` @ 0x10010ed84, `evidence/dec-insertBeside.json`;
  clamp constants 0.1/0.9 and axis `cset hi` in
  `objdump-docking-constants.json`; f = 0.5 at dock 0x1000159ac/0x100015b28,
  `applyDockTarget` 0x10010bb50, seam insert 0x10010b1a0/0x10010b918.
- Rules 16–18: `SplitSeam.init(axis:leaves:before:)` symbol;
  `seamTarget(_:_:)` @ 0x100109384 and `leafIds(_:)` @ 0x10010569c —
  `evidence/dec-docking-batch2.json`; `DockTargetSpec.init(id:edge:seam:boundary:)`.
- Rules 19–24: `insertAtSeam` @ 0x10010a384 and its closure `insert #1`
  @ 0x10010a57c (batch1, very large inlined pseudocode); weight arithmetic
  0x10010b3c8–0x10010b4c0 and fallback 0x10010b504–0x10010b604 and
  `"-range"` string append (objdump file / batch1).
- Rules 25–28: `evidence/dec-dock.json` (panelOfView, UID.make "split",
  seam branch, panelsOf check, fallback edge `seam.axis == 1 ? 3 : 1`,
  root-id reuse); `dock(_:unit:at:)` @ 0x10001565c / specialised
  0x100021140 `evidence/dec-dockUnit.json`; `panelsOf` @ 0x10010dcdc
  (batch2).
- Rules 29–31: follow from the above plus `findings/layout-tree.md` rule 10.
- Rule 32: study §2.2 (`placeNew`), symbol names `tileDropTarget`,
  `frameDropTarget`, `Engine.seam(at:)`, `Feel.minSeamHitWidth`.

## Confidence
| Rule | Confidence |
|---|---|
| 1–4, 6, 7 | Observed |
| 5 (vanished-child weight goes to previous, else next, survivor) | Inferred (loop structure; rarely reachable) |
| 8 | Observed |
| 9–15 | Observed |
| 16, 17, 18 | Observed |
| 17 (index 0 / n meaning outer edges) | Inferred |
| 19, 20, 21 | Observed (21 desktop-id-in-set guard Inferred) |
| 22 same-axis (range, 1/(m+1), W/(m+1), insertion index) | Observed arithmetic; insertion-index rule Inferred from loop; index when all range leaves are "before" (append after range) Inferred |
| 22 cross-axis grouping, `-range` id, weight W | Inferred (structure + string observed; exact group child slice Inferred) |
| 23 | Observed |
| 24 | Inferred |
| 25, 27, 28 | Observed |
| 26 | Inferred (consequences) |
| 29–31 | Observed / Inferred (31 id count Observed in dock) |
| 32 | Observed names only |

## Unknowns
- **Exact behaviour inside the cross-axis seam path** (rule 22, second
  bullet): the 630 KB inlined closure was only partly read; whether the group
  is built when the range covers the whole split, and how ids then collapse,
  is Inferred. It only arises after removal reshapes the tree; tests should
  focus on the same-axis path.
- **`DockTargetSpec.boundary`** (Int?) is never read by dock; its role
  (probably gap-drag or seam index for the engine) is unknown.
- **Insertion index when every range child is entirely "before"** (seam at
  the far edge after a removal): assumed to append after the range.
- `insertPanel(doc, panel, DockTarget)` with the `DockTarget` enum,
  `Arrangement.redock(_:_:at:unit:)`, `dockSpot`/`restoreTargetFor`
  (remembered spots) and `Arrangement.tab` (tab grouping) were not traced —
  they are separate features.
- Whether `Arrangement.shareRemovedSpace` is refreshed from the preference on
  every change or only at start-up (both default true).
- `panelOfView` search scope (tree + floating + hidden assumed).

## Windows notes
- Everything here is pure tree manipulation for `CanvasWm.Core`; no OS calls.
- Inject the id generator so tests can assert ids (`n1`, `n2`); dock must
  consume one id per call and a second only on fallback.
- Expose `ShareRemovedSpace` as a setting (default true) passed into Core,
  not a global.
- Use exact rationals in tests with a tolerance (1e-9); weights like 1/3 come
  from division.
- Recommended split for the queue: (a) remove + insert-beside + dock
  (this file's core), (b) seams (`SeamTarget`, `InsertAtSeam`) can land in the
  same change but the cross-axis path may be deferred, (c) tab grouping,
  redock/remembered spots and drop-target hit testing as separate features.
