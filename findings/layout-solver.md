# Feature: layout-solver

## Summary
The solver turns one layout tree into an ordered table of *entries*, one per
node id: the node, its rectangle, its parent's id, an optional "scale" and a
"virtual" flag. Rectangles are always expressed as fractions of a **unit
square** (0,0)–(1,1) that stands for the area being laid out; the solver never
works in pixels for its output and never rounds. There are two modes: a pure
**weights** mode (each child gets its weight share) and a **minimum-aware**
mode that, given a pixel size for the unit square and a per-node minimum
function, re-distributes space so no child falls below its minimum and, when
the minimums cannot fit, keeps the proportions but records a shrink factor
(`scale`) for the descendants instead of overflowing. A wrapper additionally
emits **range entries** (virtual groups of 2+ adjacent siblings) for every
split with 3+ children, and the engine-level call is memoised in a small
most-recent cache keyed by document equality.

Scope note: this is one feature, but two neighbouring pieces were found that
are better split off — (a) *seam drag bounds* (`dragBoundary` /
`resizeBoundary` / `BoundaryDrag{axis,start,min,max,to}`), which consume the
minimum-aware solver, and (b) the engine's *minimum provider*
(`Engine.pushMetrics` + its `minOf` closure), which supplies the minimum
function. (b) is described below in rules 30–37 because the task asked where
minimums come from; recommend it becomes its own spec item ("layout-metrics")
or is folded into this spec explicitly.

## Behaviour

### Entry table (output)
1. Output is an *ordered* map from node id (string) to an entry. Inserting an
   id that is already present replaces that entry; iteration follows insertion
   order (the map keeps a separate key-order list). Callers look entries up by
   id and also iterate them.
2. An entry holds: `node` (the node value laid out — see rule 17 for the
   minimum mode, where a split's entry carries a rebuilt node), `rect`
   (x, y, w, h as fractions of the unit square), `parent` (id of the parent
   node, absent for the root), `scale` (optional number), `virtual` (bool).
3. Entries are written **pre-order**: a node's own entry is written before its
   children are laid out; children in list order.

### Coordinate convention
4. The root is laid out in the unit rect `(x=0, y=0, w=1, h=1)`. Every rect in
   the output is a sub-rect of it. There is no gap, padding, inset or tab
   strip subtracted inside the solver (gaps only enter through the minimum
   function and through the pixel size of the unit square, rules 31–33).
5. Axis `x` lays children left→right along x; axis `y` lays them along y with
   the **first child at the smaller y**. Since docking at the *top* edge puts
   the new tile first in a `y` split, smaller y = top: the unit space is
   **y-down, origin top-left** — the same convention as CanvasWM Core.
6. No rounding or pixel snapping anywhere in the solver; all arithmetic is
   double precision. Child offsets are accumulated as a running fraction
   (sum of previous fractions), not as pixel positions.

### Weights mode (`layoutByWeights`)
7. Inputs: node (optional), rect, parent id (optional), entry table. A missing
   node writes nothing.
8. Every node writes its entry with the given rect and parent, `scale` absent,
   `virtual` false.
9. **Panel**: nothing more (leaf).
10. **Desktop**: if it has a child, the child is laid out in the *same* rect
    with parent = the desktop's id. An empty desktop is a leaf.
11. **Split**: let `S = Σ weights` (raw sum, not assumed to be 1). For child
    *i* in order: fraction `fᵢ = wᵢ / S`; along the split axis the child gets
    offset `rect.origin + rect.extent × (f₀ + … + fᵢ₋₁)` and extent
    `rect.extent × fᵢ`; across the axis it gets the split's full origin and
    extent. Parent = the split's id. (The original code traps if the weights
    list is shorter than the children list; normalisation from feature
    `layout-tree` guarantees equal lengths.)

### Minimum-aware mode (`layoutWithMinimums`)
12. Extra inputs: a pixel `size` (W, H) that the given rect currently
    represents, an accumulated `scale` (root call: 1.0) and *metrics*:
    `width`, `height`, and a function `min(node, axis) → number` (pixels).
    At the root: rect = unit, size = (metrics.width, metrics.height),
    scale = 1.0, parent absent.
13. Every node writes its entry with the given rect and parent,
    `scale` = the incoming accumulated scale (present, a number), `virtual`
    false. (Split entries: see rule 17 for which node value is stored.)
14. **Panel**: leaf. **Desktop**: child (if any) laid out with the same rect,
    size and scale, parent = desktop id.
15. **Split** — step 1, minimums: for each child call `min(child, splitAxis)`
    (list `mA`) and `min(child, crossAxis)` (list `mC`). `ΣA = sum(mA)`,
    `maxC = max(mC)` (absent when there are no children; a negative max is
    treated as 0).
16. **Split** — step 2, shrink factor `k` (start 1):
    - along = W if axis x else H; cross = the other.
    - if `ΣA > 0`: candidate `along / ΣA`;
    - if `maxC > 0`: candidate `cross / maxC`;
    - `k = min(1, candidates…)` (only candidates that exist).
    - If `k < 1`: **enlarge** the working size: `W ← W / k`, `H ← H / k`. The
      rect is **not** changed. Effect: the subtree is solved in a virtual
      space large enough for every minimum, and its descendants are marked as
      displayed at scale `k` (rule 18).
17. **Split** — step 3, distribution: `sizes = distribute(total = along (after
    step 2), weights = the split's weights, mins = mA)` (rules 21–27). New
    effective weights `eᵢ = sizesᵢ / total`. The split's **entry stores a
    rebuilt split node** with the same id, axis and children but weights `e`
    (the document's stored weights are not modified).
18. **Split** — step 4, children: laid out exactly like rule 11 but with
    fractions `eᵢ`; child *i* receives size `(sizesᵢ, cross)` (along axis
    first, cross extent = the possibly enlarged cross size), scale =
    `incoming scale × k`, parent = split id.
19. Consequence: in minimum mode the rects still tile the parent rect exactly
    (Σ eᵢ = 1 whenever Σ mins ≤ total, which step 2 guarantees); minimums
    change only the *proportions*, and an impossible fit changes only the
    descendants' `scale`.
20. Shrink factors compound: a nested split computes its own `k` from the size
    its parent gave it (already enlarged), and multiplies it into the scale.

### `distribute(total, weights, mins) → sizes` (iterative pinning)
21. Start with an empty *pinned* set of indices; `n` = number of weights.
22. Repeat:
    - free = indices not pinned (in order);
    - `used = Σ mins[p]` over pinned; `remaining = total − used`;
    - `Σw = Σ weights[i]` over free;
    - size of each index: pinned → `mins[i]`; free → `remaining × wᵢ / Σw`
      if `Σw > 0`, otherwise `remaining / |free|` (equal share);
    - violators = free indices with `size < mins[i] − 1e-9`;
    - if none → return sizes; else add **all** violators to pinned and repeat.
23. Each round pins at least one index, so it ends after ≤ n+1 rounds.
24. If everything ends up pinned the result is simply `mins` (may sum to more
    than `total`); in the solver this cannot happen after rule 16's
    enlargement except through floating-point slack.
25. Pinned children get exactly their minimum; leftover space is shared among
    the unpinned children in proportion to their weights (re-normalised over
    the unpinned set).
26. There are no maximum sizes in the solver (`WindowLimits.maximum` exists
    in the model but is not read here).
27. `n = 0` returns an empty list.

### Range entries (`focusLayout` only)
28. After laying out the whole tree, `focusLayout` takes a snapshot of the
    entries and, for every entry whose node is a **split with ≥ 3 children**,
    adds one entry per contiguous run of children `[s, e)` with
    `e − s ≥ 2`, **excluding the full run** `[0, n)`. Enumeration order:
    s = 0, 1, …, n−2; for each s, e = s+2 … n (skipping e = n when s = 0).
    A split with n children yields `n(n−1)/2 − 1` ranges (3 → 2, 4 → 5, 5 → 9).
    Range entries are *not* themselves expanded (only the snapshot is
    scanned).
29. Each range entry:
    - id = `rangeId(splitId, childIds)` = the text `range:[` + the quoted
      items joined by `,` + `]`, items = split id then the run's child ids in
      order; each item is wrapped in `"` after escaping `\` → `\\` then
      `"` → `\"`. Example: `range:["split-k3","a","b"]`;
    - node = a synthetic split with that id, the split's axis, the run's
      children, and weights = the run's weights (taken from the split's
      *entry* node, so effective weights in minimum mode) divided by their
      sum;
    - rect = from the first child's origin to the last child's far corner:
      `(first.x, first.y, last.x + last.w − first.x, last.y + last.h − first.y)`;
      a child with no entry contributes a zero rect;
    - parent = the split's id; scale absent; **virtual = true**.
    These are the "virtual" groups that let focus/zoom target several adjacent
    tiles at once (consumers: `navParent`, `navNode`, framing).
30. `layoutRects(root, metrics?)` is the same as `focusLayout` but **without**
    range entries. Both choose minimum mode when metrics are supplied and
    weights mode otherwise.

### Minimum provider (engine side; where minimums come from)
31. Only interactive seam drags and live resizes build metrics
    (`Engine.pushMetrics(tree, framing)`, called from `beginGapDrag`,
    `moveGapDrag`, `resizeLive`). Ordinary arrangement (`Arrangement.layOut`,
    used by `Arrangement.entries`) calls the solver **without metrics**, i.e.
    pure weights.
32. Metrics size: usable = display visible area minus `gap` (half a gap on
    each side): `usableW = max(0, visibleW − gap)`, `usableH = max(0,
    visibleH − gap)`. The unit square's pixel size is then the usable size
    divided by the framed subtree's unit-rect size (so the framed subtree
    fills the usable area); it is further mapped through the camera's
    `Stretch`. (Inferred in detail.)
33. `min(node, axis)` (memoised per metrics instance by key `"<id>|x"` /
    `"<id>|y"`):
    - **Panel**: `gap + base`, where base is
      - 120 on both axes if `Policy.squeezeBelowMinimum` is on;
      - else per axis `max(floor, learned)`, floor = 480 × 320 if
        `Policy.fixedMinimum` is on, otherwise 200 × 120; learned = the
        minimum size stored for the panel's **selected** window in
        `Engine.limitsByKey()` (absent → 0 × 0) — this is the LimitStore seam;
      - plus, on the y axis only, if the panel draws its own tab strip
        (tabbed and > 1 window): `tabStripHeight = 2 × tabPadding + 20`.
    - **Desktop**: with a child → the child's minimum; empty → `gap + 120`.
    - **Split**: children's minimums on the same axis are **summed** when the
      query axis equals the split axis, otherwise the **maximum** is taken
      (0 for no children). No gap is added at split level (each panel already
      carries one gap).
    - Finally the result is **capped** at the node's current pixel extent on
      that axis, taken from a weights-only layout of the tree passed to
      `pushMetrics` (fraction × unit-square size). A node absent from that
      layout gets extent 0 (minimum 0).
34. Effect of rule 33's cap (Inferred): a tile that is already smaller than its
    minimum is allowed to stay at its current size rather than being forced
    to grow; candidate trees produced during a drag are then solved in
    minimum mode so the drag cannot squeeze any tile below
    `min(its minimum, its pre-drag size)`.

### Memoisation
35. `Arrangement.entries(document)` keeps a process-wide list of recent
    `(document, entries)` pairs. Lookup: linear scan, structural equality of
    the whole document (tree, floating, hidden, hidden-desktop). Hit → the
    cached entries are returned (order of the list is not changed on a hit).
    Miss → compute (`layOut`), **insert at the front**, and if the list then
    holds ≥ 7 items drop the last one — so at most **6** documents are kept
    (most-recently-computed first).
36. The cache is a pure optimisation: layout is a deterministic function of
    the document, so results are identical with or without it. There is no
    explicit invalidation; any document change makes a different key.
37. `layOut` = `focusLayout(root, no metrics)` plus floating panels: if the
    entries contain the desktop (`"desktop"` id), every floating panel (after
    sorting the floating list, presumably by z) gets an entry with rect
    `x = desk.x + desk.w × fx`, `y = desk.y + desk.h × fy`, and size =
    `(desk.w × fw, desk.h × fh)` when the float has no stored size, else the
    stored size as-is; parent = desktop id; scale absent; virtual false.
    Without a desktop entry floats get no entries. (Belongs to the floating
    feature; listed because it shares the output table.)

## Data model
- `Rect { X, Y, W, H }` (doubles, unit-square fractions here).
- `Size { W, H }` (doubles, pixels/points).
- `Entry { Node, Rect, Parent: string?, Scale: double?, Virtual: bool }`.
- `Entries`: insertion-ordered map `id → Entry` (keys list + dictionary).
- `LayoutMetrics { Width, Height, Min: (Node, Axis) → double }`.
- `WindowLimits { Minimum: Size?, Maximum: Size?, Natural: Size? }` — only
  `Minimum` is used, via the minimum provider.
- Policy inputs used by the minimum provider: `gap`, `fixedMinimum` (bool),
  `squeezeBelowMinimum` (bool), `tabPadding` (→ `tabStripHeight`).
- Functions: `LayoutByWeights(node, rect, parent, entries)`,
  `LayoutWithMinimums(node, rect, size, scale, parent, metrics, entries)`,
  `Distribute(total, weights, mins) → sizes`, `RangeId(splitId, childIds)`,
  `LayoutRects(root, metrics?)`, `FocusLayout(root, metrics?)`.

## Worked examples
Notation: `X[...]`/`Y[...]` split (id in braces), panels by id, weights in
parentheses; rects as (x, y, w, h).

Weights mode (`FocusLayout(root, null)` unless noted)
1. `X{s}[a(0.5), b(0.5)]` → `s` (0,0,1,1) parent none; `a` (0,0,0.5,1)
   parent s; `b` (0.5,0,0.5,1) parent s; all scale none, virtual false;
   no range entries (only 2 children).
2. `Y{s}[a(1), b(3)]` (unnormalised) → `a` (0,0,1,0.25), `b` (0,0.25,1,0.75).
3. `X{s}[a(0.25), Y{t}[b(0.5), c(0.5)](0.75)]` → `a` (0,0,0.25,1);
   `t` (0.25,0,0.75,1) parent s; `b` (0.25,0,0.75,0.5) parent t;
   `c` (0.25,0.5,0.75,0.5) parent t.
4. `Desktop{desktop}[X{s}[a(0.5), b(0.5)]]` → `desktop` (0,0,1,1) parent
   none; `s` (0,0,1,1) parent `desktop`; `a`,`b` parent `s`.
5. Entry order for example 3: s, a, t, b, c.

Range entries
6. `X{s}[a(0.2), b(0.3), c(0.5)]` → besides s/a/b/c:
   - `range:["s","a","b"]`: axis x, children [a,b], weights [0.4, 0.6],
     rect (0,0,0.5,1), parent s, virtual true, scale none;
   - `range:["s","b","c"]`: children [b,c], weights [0.375, 0.625],
     rect (0.2,0,0.8,1).
   No `range:["s","a","b","c"]`.
7. 4 children a,b,c,d (0.25 each) → ranges [a,b], [a,b,c], [b,c], [b,c,d],
   [c,d] in that order; [a,b,c] rect (0,0,0.75,1).
8. Escaping: split id `s`, child id `a"b\c` →
   `range:["s","a\"b\\c"]` … (item text `"a\"b\\c"`).
9. `LayoutRects` on example 6 → only s, a, b, c (no ranges).

`Distribute(total, weights, mins)`
10. (1000, [0.5,0.5], [100,100]) → [500, 500].
11. (1000, [0.8,0.2], [0,300]) → round 1 [800,200] pins index 1 → round 2
    remaining 700 → [700, 300].
12. (1000, [0.6,0.2,0.2], [0,300,300]) → [400, 300, 300] (both pinned in one
    round).
13. Cascade: (1000, [0.5,0.3,0.2], [0,250,450]) → round 1 [500,300,200] pins
    2; round 2 remaining 550 → [343.75, 206.25, 450] pins 1; round 3 →
    [300, 250, 450].
14. Overflow: (500, [0.5,0.5], [300,300]) → [300, 300] (sum 600 > 500).
15. Tolerance: (1000, [0.5,0.5], [500.0000000005, 0]) → [500, 500] (not
    pinned: 500 ≥ min − 1e-9).
16. Zero free weights: (100, [0,0], [0,0]) → [50, 50].
17. (anything, [], []) → [].

Minimum mode (metrics W=1000, H=600; `min` given as a table per node/axis)
18. `X{s}[a(0.5), b(0.5)]`, min x a=b=300, min y a=b=200 → k = 1;
    sizes [500,500]; `a` (0,0,0.5,1), `b` (0.5,0,0.5,1); scale 1.0 on s, a, b.
19. `X{s}[a(0.8), b(0.2)]`, min x [0,300], min y 0 → sizes [700,300]; `a`
    (0,0,0.7,1), `b` (0.7,0,0.3,1); entry `s`.node.weights = [0.7, 0.3]
    while the document still says [0.8, 0.2].
20. `X{s}[a(0.5), b(0.5)]`, min x [800,800] → k = 1000/1600 = 0.625; working
    size (1600, 960); sizes [800,800]; rects as weights mode; `s` scale 1.0,
    `a`,`b` scale 0.625; `a` receives size (800, 960).
21. Cross overflow: `X{s}[a(0.5), b(0.5)]`, min x 0, min y a=900 → ΣA = 0,
    k = 600/900 ≈ 0.6667; rects unchanged; `a`,`b` scale ≈ 0.6667.
22. Nested: `X{s}[a(0.5), Y{t}[b(0.5), c(0.5)](0.5)]`, min(a,x)=100,
    min(a,y)=100, min(t,x)=100, min(t,y)=800, min(b|c,x)=100,
    min(b|c,y)=400 → at s: ΣA = 200 (ratio 5), maxC = 800 (ratio 0.75) →
    k = 0.75, working (1333.33, 800), sizes [666.67, 666.67]; `t` gets rect
    (0.5,0,0.5,1), size (666.67, 800), scale 0.75; at t: along 800,
    ΣA = 800 → ratio 1; cross 666.67/100 → k = 1; `b` (0.5,0,0.5,0.5),
    `c` (0.5,0.5,0.5,0.5), both scale 0.75.
23. Same as 19 through `FocusLayout` with 3 children `X{s}[a(0.6),b(0.2),
    c(0.2)]`, min x [0,300,300] → sizes [400,300,300]; range
    `range:["s","a","b"]` weights [400/700, 300/700] (effective weights).

Minimum provider (engine; gap = 10, tabPadding = 4)
24. Panel, no learned limit, fixedMinimum off, squeeze off → x 210, y 130.
25. Same with fixedMinimum on → x 490, y 330.
26. Learned minimum 500 × 700, fixedMinimum off → x 510, y 710.
27. Tabbed panel with 3 windows → y adds 2×4+20 = 28 (e.g. 130 → 158);
    x unchanged.
28. squeezeBelowMinimum on → 130 on both axes (+28 on y if it has a strip).
29. `X[a, b]` with a,b each 210 on x and 130 on y → min x = 420, min y = 130;
    a `Y[a, b]` → min x = 210, min y = 260; an empty desktop → 130.
30. Cap: a panel whose current tile is 150 px wide has min x = min(210, 150)
    = 150.

Cache
31. Calling `entries(d1)` … `entries(d7)` for 7 distinct documents, then
    `entries(d1)` again recomputes (d1 was evicted); `entries(d7)` is a hit.

## Evidence
All addresses refer to the binary with SHA-256 `d9148bb0…6304`.
- Rules 1–3: `Entries` has `storage` + `order` (`Entries.order.getter`,
  `Entries.values.getter` @ 0x100112a84 iterates `order` and looks up
  storage; setter @ 0x100112c9c removes or updates by key); pre-order writes
  in `layoutByWeights` @ 0x10011078c (entry set at 0x1001108bc before the
  child loop). `Entry.init(node:rect:parent:scale:virtual:)` with
  `scale: Double?`, `virtual: Bool` (demangled symbol).
- Rule 4: `focusLayout` @ 0x100105900 and `layoutRects` @ 0x10010fc64 pass
  rect (0,0,1,1) (`movi v0/v1 #0`, `fmov d2/d3 #1.0`); no gap constants in
  either solver (evidence `objdump-layout-solver.json`).
- Rule 5: axis byte test in both solvers (axis 0 → x offset); top-edge →
  new-first in `insertBeside` (study §2.2 table; `findings/layout-tree.md`).
- Rule 6: no rounding instructions in `layoutByWeights`,
  `layoutWithMinimums`, `distribute` (objdump).
- Rules 7–11: `layoutByWeights` 0x10011078c–0x100110b0c (weights sum
  0x100110930–0x1001109c8, fraction `fdiv` 0x100110a98, offset/extent
  `fmul/fadd` 0x100110a10–0x100110ab8, desktop recursion 0x1001108cc–
  0x10011090c, scale-absent/virtual-false half-word `1` at 0x100110848);
  also `evidence/dec-layoutByWeights.json`.
- Rules 12–20: `layoutWithMinimums` 0x10010fee4–0x100110770 (argument roles
  from demangled signature `(LayoutNode?, Rect, Size, Double, String?,
  LayoutMetrics, inout Entries)`; root call in `focusLayout`
  0x10010595c–0x1001059a8 with size = metrics width/height, scale 1.0;
  min calls for split and cross axes 0x10011007c–0x1001100c8 and
  0x100110304–0x100110350; `max` 0x100110384; shrink-factor logic
  0x1001103a4–0x100110478; `distribute` call 0x100110488; effective weights
  `fdiv` 0x1001104e8; rebuilt split node 0x100110528–0x100110548; child
  loop 0x10011061c–0x100110710 with scale `fmul d14,d8` 0x100110608);
  `evidence/dec-layoutWithMinimums.json`.
- Rules 21–27: `distribute` @ 0x100110b18–0x100111258 (signature
  `(Double, [Double], [Double]) -> [Double]`; pinned set inserts
  0x1001111a4; free filter 0x100110c40–0x100110d14; pinned-min sum via map
  0x100110d38; equal-share `ucvtf/fdiv` 0x100110f54; weighted share
  0x100110fd0–0x100110fdc; tolerance constant `0xbe112e0be826d695` (= −1e-9)
  0x1001110ec–0x100111108); `evidence/dec-distribute.json`.
- Rule 26: `WindowLimits.init(minimum:maximum:natural:)`; no maximum read in
  the solvers.
- Rules 28–29: `focusLayout` range loop 0x100105a8c–0x100106080 (`cmp
  x21,#3`, start/end loop with full-run skip via flag at sp+0xdc and `cbnz
  x28`), range entry build 0x100105f90–0x100106060 (rect `fadd/fsub`,
  half-word `0x101` = scale absent + virtual), weights normalisation
  0x100105e54–0x100105f84; `rangeId` @ 0x100106278 (prefix bytes
  `range:[`, escapes `\`→`\\` and `"`→`\"`, `","` join, `]`);
  `evidence/fn-focusLayout.json`.
- Rule 30: `layoutRects(_:metrics:)` @ 0x10010fc64 (no `rangeId` call).
- Rule 31: callers of `layoutWithMinimums`/metrics — `Engine.pushMetrics`
  @ 0x1000db204 called from `beginGapDrag` 0x1000da5bc, `moveGapDrag`
  0x1000daa70, `resizeLive` 0x1000dc984; `dragBoundary` @ 0x10010098c;
  `Arrangement.layOut` @ 0x10001d798 passes nil metrics (x1–x4 = 0 at
  0x10001d7d0–0x10001d7dc).
- Rule 32: `pushMetrics` 0x1000db258–0x1000db27c (visible rect fields, gap =
  policy offset 0), 0x1000db73c–0x1000db7ac (half-gap, `max(0,…)`, divide by
  framing rect), `Stretch.map` calls 0x1000db7ec–0x1000db82c.
- Rule 33: `minOf` @ 0x1000dba9c (memo key `|x`/`|y` 0x1000dbb1c–0x1000dbb3c;
  squeeze flag engine+0x123 = `Policy.squeezeBelowMinimum` @ 0x1000916fc
  offset 0xa3; floors 200/480 and 120/320 from `fixedMinimum` @ 0x100090f9c
  offset 0x1e; learned limits dictionary lookup 0x1000dbdf4–0x1000dbe38 and
  `Size.zero` fallback; strip term 0x1000dbfbc–0x1000dbfcc matches
  `Policy.tabStripHeight` @ 0x1000901b0 = 2×tabPadding+20; split
  sum/max 0x1000dbeb4–0x1000dbef8; empty desktop 120; cap `fcsel … mi`
  0x1000dbfec–0x1000dbff0 against `extent` @ 0x1000db9c4); closure context
  from partial-apply @ 0x1000de6f8 (initial value = gap, extent entries =
  `layoutRects(tree, nil)` result stored at context+0x200);
  `evidence/dec-pushMetrics-minOf.json`, `evidence/objdump-layout-solver.json`.
- Rules 35–36: `Arrangement.entries` @ 0x10001db88: `recent` static list,
  `LayoutDocument.__derived_struct_equals` loop, `replaceSubrange(0..<0)`
  insert 0x10001dd80, `count ≥ 7` → `removeLast` 0x10001ddac–0x10001de10;
  `evidence/dec-entries.json`.
- Rule 37: `Arrangement.layOut` 0x10001d7e0–0x10001d9bc (desktop lookup by
  `DesktopNode.defaultID`, floating sort, rect arithmetic, size-nil select
  `fcsel` 0x10001d8fc); `evidence/dec-layOut.json`; study §2.1 item 4.

## Confidence
| Rule | Confidence |
|---|---|
| 1 (ordered map, replace on duplicate) | Inferred (from storage/order fields and setter calls) |
| 2, 3 | Observed |
| 4 (unit square, no gaps in solver) | Observed |
| 5 (y-down) | Inferred (axis arithmetic Observed; "top = smaller y" from docking order) |
| 6 | Observed |
| 7–11 | Observed |
| 12–18 | Observed |
| 16 meaning ("virtual larger space shown at scale k") | Inferred |
| 19, 20 | Inferred (follow from 15–18) |
| 21–25, 27 | Observed |
| 24 "cannot happen in solver" | Inferred |
| 26 | Observed (absence in these functions) |
| 28, 29 | Observed |
| 29 consumers (`navParent`, `navNode`, framing) | Inferred (names only) |
| 30 | Observed |
| 31 | Observed |
| 32 | Observed structure; exact framing/Stretch mapping Inferred |
| 33 | Observed (constants, keys, sum/max, cap); "selected window" key Inferred from field offsets |
| 34 | Inferred |
| 35, 36 | Observed (36 "pure optimisation" Inferred) |
| 37 | Observed; sort key and float `size` units Unknown |

## Unknowns
- **Exact metrics size under zoom.** How `Stretch.map` and the framing rect
  combine into `metrics.width/height` (rule 32) was only read structurally.
  For CanvasWM's first non-camera version, using usable size directly
  (framing = whole tree) is sufficient.
- **What `scale < 1` does downstream.** The solver only records it; whether
  the engine renders such tiles as scaled previews, zooms the camera out, or
  refuses the edit is in the camera/engine code (`visualStretch`,
  `restStretch`), not traced.
- **Seam drag bounds.** `dragBoundary` (with `walk`, `solve`, closure #8
  producing candidate trees) and `resizeBoundary(_:_:_:min:)` compute the
  allowed drag range (`BoundaryDrag{axis,start,min,max}`); not decompiled.
  Recommend a separate feature.
- **Learned-limit key.** The limits dictionary is looked up with a panel field
  that by offset is `selected`; behaviour when `selected` is absent was not
  traced.
- **Float size units / sort key** in `layOut` (floating feature).
- Where gaps are actually applied when converting tile rects to window frames
  (frame-writing/presenter feature) — not part of the solver.
- Thread-safety of the global `recent` cache (no lock observed; Swift access
  enforcement only).

## Windows notes
- Entirely platform-neutral; belongs in `CanvasWm.Core` as pure functions over
  the immutable tree from `layout-tree`. Return a new ordered entries object
  per call.
- The unit square is already y-down/top-left, matching Core's DIP convention:
  map to the desktop/monitor work area by `x = area.X + area.W × fx` etc. —
  no flip needed. Convert DIPs to physical pixels only in Win32.
- Keep the solver free of rounding; snap to whole device pixels at the
  Win32 boundary (per-monitor DPI) so seams stay consistent.
- Minimum sizes on Windows come from `WM_GETMINMAXINFO`
  (`ptMinTrackSize`) or are learned when an app refuses a size (the
  LimitStore seam); express them in DIPs before passing to Core, and include
  the invisible resize border (DWM extended frame) consistently.
- The engine-level cache can be a small MRU list keyed by structural
  equality; C# records holding arrays need custom equality (see
  `layout-tree` notes). Since it is only an optimisation, tests should assert
  equal results with the cache on and off.
- Range-entry ids are strings built from user-visible ids; reproduce the
  exact format only if persisted/compared — otherwise any unambiguous
  encoding works, but keep the `virtual` flag semantics.
