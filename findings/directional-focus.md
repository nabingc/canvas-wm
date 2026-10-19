# Feature: directional-focus

## Summary
Given the currently framed node and a direction (left, right, up, down), the
navigator picks one "neighbouring space" from the layout's entry table: any
tile, split, range group or empty desktop slot whose opposite edge **touches**
the current node's edge on that side and that overlaps it across the
direction. Among those it keeps the one that is most similar in size and best
aligned with the current node, with a small penalty for virtual range groups.
The engine then frames (pans/zooms to and focuses) that neighbour and records
it in history. Nothing in the layout tree changes: "move toward" moves the
camera/focus, not a window. There is no wrap-around and no direction memory.

## Behaviour

### Inputs
1. The search runs on the navigator's entry table, which is exactly the
   output of `Arrangement.entries(document)`: the weights-mode
   `FocusLayout` table (no metrics) **including range entries and floating
   panels**, in unit-square coordinates (y-down, origin top-left). No pixel
   sizes, display aspect ratio or camera transform are applied. See
   `findings/layout-solver.md` rules 28–29 and 35–37.
2. The *current* node id is the navigator's **framed** node id if there is one,
   otherwise the navigator's **root id**. It is a node id of any kind
   (panel, split, desktop, or a virtual `range:[...]` id) — not a window key
   and not the focused window. (The engine passes the framed id, not the
   focused window.)
3. Direction is an `Edge`: 0 = left, 1 = right, 2 = top (up), 3 = bottom
   (down). `Edge.axis` = x for left/right, y for top/bottom.
4. If the current id has no entry, the result is "no neighbour".

### Candidate filter (all must hold; entries are scanned in table order)
5. The candidate's id differs from the current id.
6. The candidate's **parent id is not the literal default desktop id
   `"desktop"`**. This removes floating panels (they are entered with parent =
   desktop) and the desktop's direct child. An entry with no parent is not
   excluded by this rule.
7. The candidate's node does not contain the current id (it is not the current
   node and not an ancestor of it), using id containment over the node's
   subtree (`containsNode`: the node's own id or any descendant's id).
8. The current node does not contain the candidate id (not a descendant).
   Note: for a range current, containment is checked against the synthetic
   range node, i.e. its run children are excluded but the real parent split is
   not excluded by this rule (rule 9 removes it geometrically in practice).
9. A **desktop node that has a child** is skipped. An **empty desktop** (no
   child) is an ordinary candidate. Splits, panels and range (virtual)
   entries are candidates.
10. **Touching test** (tolerance `ε = 1e-6`), with current rect C and
    candidate rect K (`max = origin + extent`):
    - left:   `|K.maxX − C.x|    < ε`
    - right:  `|K.x − C.maxX|    < ε`
    - top:    `|K.maxY − C.y|    < ε`
    - bottom: `|K.y − C.maxY|    < ε`
    It is an absolute-difference test: the candidate must share the edge, not
    merely lie somewhere beyond it. A tile further away (not adjacent) is never
    a candidate.
11. **Overlap test** across the direction must be strictly greater than ε:
    - left/right: `min(K.maxY, C.maxY) − max(K.y, C.y) > 1e-6`
    - top/bottom: `min(K.maxX, C.maxX) − max(K.x, C.x) > 1e-6`
    Corner-touching tiles (zero overlap) are excluded.

### Score (lower is better)
12. `score = |ln(K.w / C.w)| + |ln(K.h / C.h)| + |Kc⊥ − Cc⊥| / C.extent⊥ + (K.virtual ? 0.05 : 0)`
    - `ln` = natural log; both width and height ratios count, so the score
      prefers a candidate of the same size as the current node.
    - `⊥` = the axis across the direction: for left/right use y
      (`Kc = K.y + K.h/2`, `Cc = C.y + C.h/2`, divide by `C.h`); for top/bottom
      use x (centres and `C.w`).
    - `0.05` is added for virtual (range) entries — a *penalty*, not a bonus
      (the study's "−0.1 if flag" is corrected here).
13. Initial best score is +∞. A candidate replaces the best only if its score
    is **strictly less**; on an exact tie the earlier entry in table order wins
    (table order = pre-order of the tree, then range entries, then floats).
14. Result = the id of the best candidate, or none if no candidate passed.

### Effect (`Engine.move(toward:)`)
15. If a neighbour id is found: `Engine.frame(neighbourId, land: !inOverview,
    record: true)` — the camera frames the neighbour (which may be a split or a
    range group, not just a tile), it becomes the framed node, and the step is
    pushed onto back/forward history. Then the engine's `onDid` callback is
    invoked with event code 8 (meaning Unknown; likely a "did navigate"
    notification for UI/onboarding).
16. If none is found: nothing happens (no camera move, no history entry, no
    callback). No wrap-around.
17. The layout tree is **never modified** by this feature. There is no swap,
    dock, remove or insert on the directional path. `Navigator.neighbour` has
    exactly one caller (`Engine.move(toward:)`). `Arrangement.swap(_:_:_:)`
    exists but is not reached from the directional commands.
18. No direction history: reversing direction re-runs the same scoring; it
    returns to the previous node only if that node scores best.
19. With nothing framed the current node is the root, which contains every
    tree entry (rule 7/8) and floats are excluded (rule 6), so the move does
    nothing (Inferred).

### Consumers (out of scope here)
20. Hotkeys `left`/`right`/`up`/`down` (study §3.2) call `move(toward:)` with
    edges 0/1/2/3. Camera animation, landing and OS focus are done by
    `Engine.frame` — separate features.

## Data model
- Input: `Entries` (ordered id → `Entry{Node, Rect, Parent?, Scale?, Virtual}`)
  as produced by `FocusLayout(root, null)` plus floating entries.
- `currentId: string` (framed id, else root id); `edge: Edge {Left=0, Right=1, Top=2, Bottom=3}`.
- Output: `string?` neighbour id.
- Constants: `ε = 1e-6`, `virtualPenalty = 0.05`, desktop id `"desktop"`.
- Suggested Core API: `Navigation.Neighbour(Entries entries, string currentId, Edge edge) → string?`
  (pure). Engine: `MoveToward(edge)` → frame + record history if non-null.

## Worked examples
Notation as in `findings/layout-solver.md`: `X{id}[...]`/`Y{id}[...]` splits,
equal weights unless given; rects (x, y, w, h) in the unit square; scores
rounded to 4 dp (ln 2 = 0.6931, ln 1.5 = 0.4055, ln 1.1 = 0.0953, ln 0.9 = −0.1054).

1. `X{s}[a, b]`. From `a` toward right → `b` (score 0). From `a` left → none.
   From `a` down → none. From `b` left → `a`.
2. `X{s}[a, Y{t}[b, c]]`: a (0,0,.5,1), t (.5,0,.5,1), b (.5,0,.5,.5), c (.5,.5,.5,.5).
   - From `a` right: `t` = 0; `b` = 0 + 0.6931 + |0.25−0.5|/1 = 0.9431; `c` = 0.9431 → **`t`** (the whole column).
   - From `b` left: only `a`: 0 + 0.6931 + |0.5−0.25|/0.5 = 1.1931 → **`a`**.
   - From `b` down → **`c`** (0). From `t` left → **`a`** (0). From `c` right → none.
3. Range wins: `Y{r}[X{t}[a, b], X{u}[c, d, e, f]]` (u has ranges
   [c,d], [c,d,e], [d,e], [d,e,f], [e,f]). From `a` (0,0,.5,.5) down:
   `u` 1.1931; `c` 0.9431; `d` 0.9431; `range:["u","c","d"]` 0 + 0.05 = **0.05**;
   `[c,d,e]` 0.4055+0.25+0.05 = 0.7055; `[d,e]` 0.5+0.05 = 0.55;
   `[d,e,f]` 0.4055+0.75+0.05 = 1.2055; `e`, `f`, `[e,f]` fail overlap
   (overlap exactly 0 or negative). Result **`range:["u","c","d"]`**.
4. Penalty decides: `Y{r}[X{t}[a, b], X{u}[c(0.45), d(0.1), e(0.45)]]`.
   From `a` down: `c` (0,.5,.45,.5) = 0.1054 + |0.225−0.25|/0.5 = 0.1554;
   `range:["u","c","d"]` (0,.5,.55,.5) = 0.0953 + 0.05 + 0.05 = 0.1953;
   `d` (overlap 0.05) = 1.6094 + |0.5−0.25|/0.5 = 2.1094. Result **`c`** (without the
   penalty the range, 0.1453, would have won).
5. Tie → table order: `Y{r}[X{t}[a(0.25), m(0.5), b(0.25)], X{u}[c, d]]`.
   From `m` (.25,0,.5,.5) down: `c` = 0 + 0 + |0.25−0.5|/0.5 = 0.5;
   `d` = 0.5; `u` = 0.6931. Ranges of t contain m → excluded. Result **`c`**
   (comes before `d`).
6. Empty desktop is a target: `X{s}[a, desktop]` (desktop empty) → from `a`
   right → **`"desktop"`** (score 0).
7. Floats ignored: same tree as 1 plus a floating panel `f` whose entry rect
   touches `a`'s right edge → `f` has parent `"desktop"` → skipped; result `b`.
   (Requires a desktop node in the tree for floats to have entries.)
8. Not adjacent: `X{s}[a, b, c]`, from `a` right → `b` (0) — `c` never
   qualifies (|0.6667−0.3333| ≥ ε) and `range:["s","b","c"]` scores
   0.6931 + 0.05 = 0.7431. From `range:["s","a","b"]` (0,0,.6667,1) right →
   `c`: |ln 0.5| = 0.6931 → **`c`** (only candidate; `range:["s","b","c"]`
   starts at x = 0.3333, so it does not touch).
9. Tolerance: current maxX = 0.5, candidate x = 0.5000005 → touching
   (|gap| = 5e-7 < 1e-6); x = 0.500002 → not touching. Overlap of exactly
   1e-6 → excluded (must be > 1e-6).
10. No framed node (current = root id) → none, nothing happens.

## Evidence
- Rules 1, 19: `Navigator.sync(_:)` @0x1000be90c calls
  `Arrangement.entries(_:)`; neighbour reads `Navigator.entries` at offset
  0x40 (`evidence/asm-directional-focus.json`, `dec-neighbour.json`).
- Rule 2: `Engine.move(toward:)` @0x1000a93a8 — uses `Engine.nav`
  (offset 0x208) field 0 (`Navigator.framed`, getter @0x1000bfbc4 reads
  offset 0) else `Engine+0x258` = nav+0x50 (`asm-directional-focus.json`,
  `dec-engineMoveToward.json`).
- Rule 3: `Edge.axis` @0x100114800 (raw > 1 → y); study §2.2 Edge raw values.
- Rules 5–14: `Navigator.neighbour(of:toward:)` @0x1000bf61c–0x1000bfa84
  disassembly in `asm-directional-focus.json`; `DesktopNode.defaultID`
  compared with the candidate's parent field; `containsNode` @0x1001065b0
  (tag 0 split / 1 panel / 2 desktop); constants `0x3eb0c6f7a0b5ed8d`
  (1e-6), `0x3fa999999999999a` (0.05), `0x7ff0…` (+∞); `log` import
  (libSystem `_log`, stub 0x10020b240); virtual flag read from entry byte
  after `scale`.
- Rules 15–17: `Engine.move(toward:)` calls `Engine.frame(_:land:record:)`
  with land = !`Engine.inOverview` (0x279), record = 1, then `Engine.onDid`
  (0xac0) with 8; sole caller of `neighbour` (whole-binary call scan).
- Rule 20: study §3.2.

## Confidence
| Rule | Confidence |
|---|---|
| 1 | Observed (call to `Arrangement.entries`; floats/ranges via layout-solver findings) |
| 2 | Observed (framed); Inferred (fallback field = rootID, by offset order) |
| 3, 4 | Observed |
| 5, 7, 8 | Observed |
| 6 | Observed (compares parent field with default desktop id) |
| 9 | Observed (tag/child check), Inferred (tag 2 = desktop from containsNode) |
| 10, 11 | Observed |
| 12 | Observed (operand mapping checked against entry field offsets) |
| 13, 14 | Observed |
| 15 | Observed (call/args); Unknown (meaning of onDid code 8) |
| 16, 17 | Observed |
| 18 | Observed (no state read besides entries) |
| 19 | Inferred |
| 20 | Observed (study) |

## Unknowns
- Meaning of `onDid(8)` after a successful move (probably an onboarding /
  UI notification). Not needed for the Core algorithm.
- Exact identity of the fallback start field (nav+0x50, Inferred `rootID`);
  only matters when nothing is framed (result is then empty anyway).
- How `Engine.frame` treats a range or empty-desktop target (camera and
  focus behaviour) — belongs to the camera/framing feature.
- Behaviour with degenerate rects (zero width/height current node → division
  by zero, NaN scores never win since comparisons with NaN are false).
  Not expected from a normalised tree.

## Windows notes
- Pure Core algorithm on unit-space entries; no Win32 dependency. Do not use
  `GetWindowRect` — entries cover off-screen/parked tiles too.
- Use `Math.Log` (natural log) and the exact constants; keep strict `<` and
  table-order iteration for deterministic ties.
- Default hotkeys (study §3.2) map to edges 0–3; keep the "move = reframe,
  not move window" semantics. If a "move window toward" command is wanted for
  Windows it is a new feature, not TrellisWM behaviour.
