# Feature: layout-tree

## Summary
Each display owns one layout document: an optional tree of nodes plus lists
of floating windows, hidden windows and an optional "hidden desktop" record.
The tree has exactly three node kinds: a *split* (an axis, a list of children
and one weight per child), a *panel* (a tab group of one or more window keys)
and a *desktop* (a container for at most one child; with no child it marks
empty desktop space). After every structural edit the tree is normalised:
children are normalised first, empty panels and empty splits disappear,
invalid weights are replaced, a child split on the same axis as its parent is
merged into the parent with its weights scaled by its share, the weights are
re-summed to 1, and a split left with one child is replaced by that child.
A separate "sanitize" pass, run on whole documents (e.g. after loading),
additionally removes unknown/duplicate windows and duplicate desktops before
normalising.

## Behaviour

### Document and node shapes
1. **Document.** A layout document holds: `root` (an optional node — a
   document may have no tree at all), `floating` (list of floating panels),
   `hidden` (list of hidden panels) and `hiddenDesktop` (optional record).
2. **Split node.** Fields: `id` (string), `axis`, `weights` (list of doubles),
   `children` (list of nodes). Axis has two values with string raw values
   `"x"` and `"y"`. `"x"` lays children out side by side along the x axis
   (a row, used when docking left/right); `"y"` stacks them top to bottom
   (a column, used when docking top/bottom). The axis has a "cross" operation
   that returns the other axis.
3. **Panel node.** Fields: `id` (string), `views` (ordered list of window-key
   strings), `selected` (optional window key — the visible tab), `tabbed`
   (bool). A panel is a tab group. Two derived flags:
   - *has strip* = `tabbed` and more than one view (the app draws its own tab
     strip);
   - *is native group* = not `tabbed` and more than one view (the windows are
     an OS-native tab group the app merely tracks).
4. **Panel for a single window.** The standard constructor for a one-window
   panel uses the window key as the panel id, `views = [key]`,
   `selected = key`, `tabbed = false`. So a fresh panel's id equals its first
   window's key.
5. **Desktop node.** Fields: `id` (string), `child` (optional node). The
   default desktop id is the literal `"desktop"`. When laid out, the desktop
   passes its whole rectangle unchanged to its child; with no child it is an
   empty tile representing free desktop space ("empty desktop" = desktop node
   whose child is absent). The dwindle rebuild ends its spiral in a desktop
   node, so a desktop node normally sits as one leaf-like slot in the tree.
6. **Floating panel.** `panel` (a panel node), `rect`, `z` (integer order),
   `size` (optional size). **Hidden panel.** `panel` plus a `restore` target.
   **Hidden desktop.** `group` (optional string), `floats` (list of floating
   panels), `restore` (optional restore target), `neighbourLeaves` (list of
   ids), `weight` (optional double). These live outside the tree; they are
   listed only because sanitize treats their panels together with the tree's.
7. **Node identity.** Every node kind exposes an `id`; layout results are
   stored in a dictionary keyed by node id, so ids must be unique within a
   document's tree.

### ID generation
8. **Split ids** are the only ids minted by the generator. Every observed
   call site passes the prefix `"split"`. The generated form is
   `<prefix>-<R><C>` where:
   - `R` = the first (up to) 5 characters of a random unsigned 32-bit number
     written in lowercase base 36 (shorter if the number is small);
   - `C` = a process-wide counter written in lowercase base 36, with no
     separator between `R` and `C`.
   The counter is guarded by a lock, incremented *before* use, and kept
   modulo 1,000,000 (so it runs 1, 2, …, 999999, 0, 1, …). Example shape:
   `split-3kq9x1`, `split-a07zbk2`.
9. **Panel ids** are window keys (rule 4); **desktop ids** default to
   `"desktop"`.
10. **Stability.** Normalisation never renames anything: a rebuilt split keeps
    its original id and axis; panels and desktops are kept as-is. Ids are lost
    only when a node disappears (an emptied panel/split, or a single-child
    split that is replaced by its child, or a same-axis child split that is
    merged into its parent — the merged child's id vanishes, the parent's id
    stays).

### `normalize(node) → node or nothing`
Applied recursively, bottom-up. Returns "nothing" when the node should be
removed.

11. **Panel:** kept unchanged if it has at least one view; removed if `views`
    is empty. `selected`/`tabbed` are not checked here.
12. **Desktop:** never removed. If it has a child, the child is normalised; if
    the child normalises to nothing, the desktop is kept with no child (an
    empty desktop). If nothing changed the original node is returned.
13. **Split** — process children in order. For child *i*:
    1. Normalise the child. If it is removed, drop it (its weight is
       discarded).
    2. Take its weight: `weights[i]` if the weights list is long enough,
       otherwise **1.0**.
    3. Sanitise the weight: if it is strictly positive and finite keep it,
       otherwise (zero, negative, NaN, ±infinity) replace it with **0.001**.
    4. If the normalised child is a split with the **same axis** as this
       split, splice its children in place of it, each with weight
       `w_child × w_grand / Σ(child's weights)`. Because the child was already
       normalised, one level of splicing is enough, and a child that became a
       same-axis split only after collapsing (rule 13.6) is also merged.
    5. Otherwise append the child with weight `w_child`.
    6. After all children:
       - no children left → the split is removed;
       - exactly one child → the split is replaced by that child (the child's
         own weight is irrelevant; the parent keeps the weight it had for the
         split);
       - two or more → divide every weight by their sum (sum becomes 1).
    7. **No-change shortcut:** if the resulting child count equals the
       original child count, every resulting child is structurally equal to
       the original child, and every new weight is within **1e-12** of the
       original weight, the original split is returned untouched. Otherwise a
       new split with the same id and axis, the new children and the new
       weights is returned.
14. Consequences: after normalisation every split has ≥ 2 children, has
    exactly one weight per child, all weights are > 0 and sum to 1 (within
    floating error), and no split has a child split with the same axis. The
    0.001 floor means a bad weight yields a tiny but non-zero tile, not a
    removed one.
15. Normalisation does **not** enforce minimum sizes, does not touch the
    document's floating/hidden lists, does not de-duplicate windows, and does
    not check that there is a desktop node.

### When normalisation runs
16. It is called after: inserting beside a target (dock), inserting at a seam,
    removing a node, the "remove sharing" cleanup, the dwindle rebuild, and at
    the end of document sanitize. Edits produce a raw tree and rely on this
    pass to restore the invariants.

### `sanitize(document, isKnown)` — whole-document repair
17. Two sets are shared across the whole document: panel ids already seen and
    window keys already seen. The tree root is repaired first, then the
    floating and hidden lists, so tree occurrences win over later duplicates.
18. **Panel repair:** if its id was already seen, the panel is dropped. Else
    the id is recorded and its views are filtered, in order, to those that are
    not already seen *and* for which `isKnown(key)` is true (the caller decides
    which windows still exist); kept keys are recorded as seen. If no view
    survives, the panel is dropped. If `selected` is not among the survivors
    it becomes the first surviving view. `tabbed` is kept.
19. **Desktop repair:** only the first desktop met (depth-first, children in
    order) is kept as a desktop; its child is repaired, and if the repaired
    child is itself a desktop it is discarded (desktop left empty). Any later
    desktop node is replaced by its repaired child (or removed if it has none),
    so a tree contains at most one desktop node.
20. **Split repair:** children are repaired and dropped when they vanish;
    weights are taken per surviving child (missing → 1.0, non-positive or
    non-finite → **1.0** here, not 0.001); an empty split is removed;
    otherwise a split with the same id and axis is rebuilt and then normalised.
21. The repaired root is normalised once more.
22. A helper finds "the desktop" of a tree: depth-first, children in order,
    first desktop node found.

### Serialisation (constrains the model only)
23. Nodes are encoded as a **flat JSON object with a discriminator key
    `"kind"`** whose value is `"split"`, `"panel"` or `"desktop"`, plus that
    kind's fields at the same level. Keys used: `kind`, `id`, `axis`,
    `weights`, `children`, `views`, `selected`, `child`, `tabbed`. Axis is
    written as `"x"`/`"y"`.
24. The document is encoded with keys `root`, `floating`, `hidden`,
    `hiddenDesktop`. The per-display saved record wraps the document as `doc`
    with other engine state (framed id, framings, owners, float sizes, dock
    spots) — those are other features.

## Data model
- `Axis` = { X (row, raw "x"), Y (column, raw "y") }; `Cross(axis)` flips it.
- `Node` = one of:
  - `Split { Id: string, Axis, Weights: double[], Children: Node[] }`
  - `Panel { Id: string, Views: string[] (window keys), Selected: string?, Tabbed: bool }`
  - `Desktop { Id: string (default "desktop"), Child: Node? }`
- `Document { Root: Node?, Floating: FloatingPanel[], Hidden: HiddenPanel[], HiddenDesktop: HiddenDesktop? }`
- Equality used by the no-change shortcut is plain structural (value)
  equality over all fields.
- Id generator: process-global, thread-safe counter (mod 1,000,000) plus a
  random 32-bit component.

## Worked examples
Notation: `X[...]` / `Y[...]` = split on that axis, `a,b,c` = one-window
panels, weights in parentheses, `∅` = panel with no views.

1. `X[a(0.5), X[b(0.5), c(0.5)](0.5)]` → `X[a(0.5), b(0.25), c(0.25)]`;
   outer id kept, inner id gone.
2. `X[a(0.3), X[b(1), c(3)](0.7)]` → inner normalises to `[0.25, 0.75]`;
   result `X[a(0.3), b(0.175), c(0.525)]`.
3. `X[a(0.5), Y[b(1)](0.5)]` → `Y` collapses to `b` → `X[a(0.5), b(0.5)]`.
4. `X[Y[∅(0.5), X[b(1), c(1)](0.5)](0.5), a(0.5)]` → `∅` dropped, `Y`
   collapses to the inner `X`, which is then merged into the outer `X`:
   `X[b(0.25), c(0.25), a(0.5)]` (order preserved).
5. `Y[a(2), b(0)]` → `b`'s weight becomes 0.001 → `Y[a(0.9995002), b(0.0004998)]`
   (2/2.001 and 0.001/2.001).
6. `X[a, b, c]` with `weights = [1]` → missing weights are 1.0 →
   `[1/3, 1/3, 1/3]`.
7. `X[a(-1), b(NaN)]` → both 0.001 → `[0.5, 0.5]`.
8. `X[∅, ∅]` → removed (normalise returns nothing). As a root this gives a
   document with no tree.
9. `Desktop{child: X[∅]}` → `Desktop{child: none}` (desktop survives, empty).
10. `X[a(0.5), b(0.5)]` already normal → the same instance is returned
    (no-change shortcut); `X[a(0.5), b(0.5 + 1e-13)]` also counts as unchanged.
11. `Y[a(0.4), Desktop(0.6)]` with the desktop empty → unchanged (desktop
    never removed, even when empty).
12. Sanitize with `isKnown(b) = false`: panel `{views:[b,c], selected:b}` →
    `{views:[c], selected:c}`; a second panel elsewhere also containing `c`
    loses `c`; a panel whose id repeats an earlier panel's id is dropped.
13. Sanitize with two desktop nodes `X[Desktop{a}, Desktop{b}]` → the second
    is replaced by `b`: `X[Desktop{a}, b]`.
14. Sanitize `X[a(0), b(1)]` → weight 0 becomes 1.0 (not 0.001) →
    `X[a(0.5), b(0.5)]`.
15. Id generator: first call in a process yields `split-<≤5 base36 chars>1`,
    the next `…2`; after `…lflr` (999,999 in base 36) comes `…0`.

## Evidence
- Rules 1–3, 5–6 (field lists, types): constructor symbols
  `LayoutDocument.init(root:floating:hidden:hiddenDesktop:)`,
  `SplitNode.init(id:axis:weights:children:)`,
  `PanelNode.init(id:views:selected:tabbed:)`, `DesktopNode.init(id:child:)`,
  `FloatingPanel.init(panel:rect:z:size:)`, `HiddenPanel.init(panel:restore:)`,
  `HiddenDesktop.init(group:floats:restore:neighbourLeaves:weight:)`
  (`symbols/all-defined.mangled.txt`, decoded by hand); study §2.
- Rule 2 axis raw values: `Axis.rawValue` getter @ 0x1001144c0 ("x" for case
  0, "y" for case 1); `Axis.cross` @ 0x100113464. Row/column meaning:
  `insertBeside` @ 0x10010ed84 sets axis = (edge > 1), edge raw values from
  study §2.2.
- Rule 3 derived flags: `PanelNode.hasStrip` @ 0x10011549c,
  `PanelNode.isNativeGroup` @ 0x1001154c0 (objdump).
- Rule 4: `PanelNode.window(_:)` @ 0x1001154e4 (objdump).
- Rule 5: `DesktopNode.defaultID` @ 0x100115b04 ("desktop");
  `LayoutNode.isEmptyDesktop` @ 0x1001162d4; desktop pass-through in
  `evidence/dec-layoutByWeights.json`; dwindle terminator study §2.2.
- Rule 7: `Entries` subscript keyed by node id string in
  `dec-layoutByWeights.json` / `dec-entries.json`.
- Rules 8–9: `UID.make` specialisation @ 0x10011d86c (lock, counter at
  0x100261da0, modulo 0xF4240, random UInt32, radix 36, prefix(5), "-");
  call sites listed by scanning objdump for calls to 0x100112794 /
  0x10011d86c (17 sites in `Arrangement.dock/redock/regroup/groupFloats/
  showDesktop/arrangeDwindle`, `Engine.start`, `Engine.applyChange`,
  `insertPanel`) — all load the string `"split"`.
- Rules 10–16: `evidence/fn-normalize.json` (`normalize(LayoutNode) ->
  LayoutNode?` @ 0x10010f340): constants 1.0 default @ 0x10010f424,
  positive-finite test + 0.001 @ 0x10010f454–0x10010f494, same-axis splice
  @ 0x10010f4cc–0x10010f6f0, renormalise @ 0x10010f948–0x10010fa94,
  1-child collapse @ 0x10010f904, empty → nothing @ 0x10010f8fc,
  1e-12 no-change test @ 0x10010fb70–0x10010fb98; callers list in the same
  file (rule 16).
- Rules 17–22: `evidence/dec-sanitize.json` (`sanitize(_:isKnown:)`
  @ 0x1000fd7d4 and its `fixNode` @ 0x1000fe6a4); `fixPanel` @ 0x1000fe300 and
  `findDesktop` @ 0x10010f22c in `evidence/bundle-layout-tree.json`.
- Rules 23–24: `LayoutNode.CodingKeys.rawValue` @ 0x1001167c4 (keys kind, id,
  axis, weights, children, views, selected, child, tabbed);
  `LayoutNode.encode(to:)` @ 0x100116ae8 (writes kind "panel"/"split"/
  "desktop" then fields in the same container); document key strings in the
  binary; `SavedDisplay.init(doc:framed:framings:owners:floatSizes:dockSpots:)`.

## Confidence
| Rule | Confidence |
|---|---|
| 1, 2 (fields, raw "x"/"y"), 3, 4, 5 (default id, empty-desktop flag) | Observed |
| 2 (x = row, y = column) | Observed (from insertBeside axis choice + study edge table) |
| 5 (desktop passes its rect to its child) | Observed |
| 5 (desktop as "free space slot" meaning), 6 meaning of hidden/floating fields | Inferred |
| 7 (ids unique in tree) | Inferred (results keyed by id) |
| 8, 9 (id format, counter, modulo, prefix "split" at every site) | Observed |
| 10 (ids kept by normalise) | Observed |
| 11–13 (all normalise rules and constants 1.0, 0.001, 1e-12) | Observed |
| 14 (resulting invariants) | Inferred (follows from 11–13) |
| 15, 16 | Observed |
| 17, 18, 20, 21, 22 | Observed |
| 17 (order root → floating → hidden) | Inferred (code order) |
| 19 (one desktop kept; later ones replaced by child) | Observed; "child that is a desktop is discarded" Inferred (relies on a signed compare reading) |
| 23, 24 key names and flat "kind" layout | Observed; optional-field handling on decode Unknown |

## Unknowns
- **Decoding defaults.** Whether missing `selected`, `tabbed`, `child`,
  `weights` keys are tolerated on load (and with what default) was not traced
  (`LayoutNode.init(from:)` @ 0x10011ec5c not decompiled). Our implementation
  should tolerate them and let sanitize repair.
- **Short weights list crash path.** If a split has fewer weights than
  children and nothing else changes, the original's no-change comparison reads
  past the end of its weights list (the original would trap). Missing weights
  are clearly meant to default to 1.0; treat that as the rule.
- **Window key format** (what string a "window key" is) belongs to the
  window-system feature; not determined here.
- **Panel ids after tab edits** (`tab`, `splitOffTab`, `regroup`): whether a
  merged panel keeps the anchor's id or takes another key is not traced.
- **Where sanitize is invoked** (load from store, display reconfiguration,
  every change?) and what `isKnown` checks exactly were not traced.
- Floating/hidden list repair details inside sanitize (z, rect, restore
  targets) were not read; they belong to the floating/hide features.

## Windows notes
- Nothing in this feature is OS-specific; it belongs entirely in
  `CanvasWm.Core` as immutable records plus pure functions.
- C# records give value equality for the no-change shortcut, but arrays/lists
  inside records compare by reference — implement explicit structural
  equality (or use an immutable collection with sequence equality).
- "Native tab group" (`tabbed = false` with several views) mirrors macOS
  native window tabs; Windows has no general equivalent, so CanvasWM will
  normally create `tabbed = true` multi-window panels. Keep the flag in the
  model for format parity.
- Use a thread-safe counter (`Interlocked`) and a random 32-bit value for the
  id generator; ids need only be unique, so any lowercase base-36 rendering of
  the same scheme is fine. Tests should inject the generator for determinism.
- JSON: use a `"kind"` discriminator with flat fields (System.Text.Json
  polymorphism with `TypeDiscriminatorPropertyName = "kind"` fits); write
  axis as `"x"`/`"y"`.
