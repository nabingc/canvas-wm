# Feature: engine-frame-writer

Sub-feature #26, carved out of `findings/engine-tiling-loop.md` (#13), part C
(rules 12–22 there). Lives in `CanvasWm.Engine`. It consumes the per-window
targets of #25 (`findings/engine-projection-targets.md`: Tile / Park /
ParkWithPicture) and the services of #9 (`findings/engine-interfaces.md`).
**This file supersedes #13 rules 13, 16, 17, 19 and 20 where they differ**
(notably: the in-flight skip rule, the per-branch completion logic, the 0.8
screen factor in the refusal recheck, and what bumps the generation).

## Summary
The writer turns a set of per-window targets into individual asynchronous
`SetFrame` calls, skipping windows that are already where they should be or
already being moved to the same place. Every write is remembered as an
"expected frame" for 1.5 s so the engine can recognise the OS echoes of its
own moves. When a write reports back, the engine compares what the app
actually took with what it asked for; a difference of more than 24 points
triggers a second look 0.25 s later, after which the engine learns a minimum
size (stored per app) or a maximum size (per window) and re-lays-out. Full
"landing" passes are numbered by a generation counter so that late results of
an abandoned pass are ignored, and a short while after a landing settles, a
drift check re-parks any parked window that has crept back onto the screen.

## Behaviour

### A. When a write pass runs
1. **Direct presentation (at rest).** When the stage is at rest (phase
   *desk*, not in overview, no interaction, no native drag, no layout tween,
   camera exactly at its target), every committed layout edit writes **all**
   current targets immediately (the skip rules of section B drop unchanged
   windows), using the *current* generation and **no** completion counting.
   Afterwards: if a focus key was requested and differs from the focused
   key, focus it on the next UI turn (clock delay 0); save; render; fire
   `onChange`. No settle, no drift check follows this path.
2. **Landing pass (`placeWindows(g)`).** Used when a re-layout must "land"
   (camera motion, refusal re-layout, store-minimum-came-down re-layout, end
   of a gap drag). It records `placedGeneration = g`, `placementStarted =
   now`, clears `placementDone`, writes all targets with generation g, and
   counts outstanding completions (counter starts at 1, plus the number of
   writes issued, minus 1; each completion decrements). When the count
   reaches 0 **and the engine generation is still g**, `placementDone = now`
   and the landing state machine advances (settle, rule 26). A pass that
   issues no writes is done immediately.
3. **Re-landing at rest.** From desk (or the settling phase), a re-layout
   request bumps the generation (`beginMotion(land: true)`) and about
   **0.034 s** later runs `placeWindows(newGeneration)` — only if no other
   bump happened meanwhile. From any other phase it calls
   `placeWindows(currentGeneration)` directly. For CanvasWM (no camera
   animation yet) a re-land = bump generation, then a landing pass with the
   new generation.
4. **Single-window writes** also go through the same writer (same skip and
   completion rules): drift re-park (rule 27), snap-back of an app-moved tile
   and the "parked window moved by itself → rewrite all" reaction
   (window-events feature).
5. **Overview shaping** writes use the same writer but with **learning off**
   (completion stops after recording the actual frame, rule 15). Out of scope
   until the overview feature exists.

### B. Skip rules (evaluated per target, in this order)
6. Key is in the caller's `except` set → skip.
7. Key is not a managed window → skip (no target is invented).
8. **Already resting parked.** If the target is a park frame **and** the
   window is currently flagged *parked* **and** its last known frame rests at
   the target (park-geometry `restsParked`: same size within 1, and either
   < 5 % visible or x within 1 and y between target.y − 61 and target.y + 1)
   → skip.
9. **Known frame equals target** exactly (all four components, no tolerance)
   → skip. The known frame is the last frame reported by `windowMoved`, a
   write completion or a recheck.
10. **Same target already in flight**: the last requested target equals this
    target exactly **and** at least one write for the window is still in
    flight → skip.
11. Otherwise **write**, even if another write for this window is still in
    flight (correction to #13 rule 13: a *different* new target is not held
    back; the service serialises writes per window, so the newest request
    lands last). Nothing is queued for "re-issue when the flight lands".

### C. Before each write
12. In order: the window's *parked* flag is set to "target is a park frame"
    (TrellisWM decides this geometrically; CanvasWM can use target kind Park
    or ParkWithPicture); for a tile target the window's *shown size* becomes
    the target size; `requested = target`; the expected-frame list is pruned
    (entries older than 1.5 s removed) and `(target, now)` appended;
    `writesInFlight += 1`; then `SetFrame(key, target, completion)`.
13. Picture hold (cover feature, optional without capture): a park write for
    a window in the *parked-small* set whose target is smaller than its shown
    size by more than 1 on an axis puts a capture hold on the window (once);
    that write will not release it. Any other write remembers whether a hold
    was active; when *that* write completes and the window's `requested`
    still equals that write's target, the hold is released.
14. **Store-minimum probe bookkeeping.** For a park write of a window whose
    app has a stored minimum, if the park target is smaller than that stored
    minimum by more than 2 on either axis, the app id is added to the
    engine's *probed-minimum apps* set. (This is the set that makes
    projection-targets rule 20's "park at tile size" probe happen only once
    per app; see Unknowns.)

### D. Completion of a write
The completion receives the read-back actual frame (or null). It always
compares with **this write's own target**, not with the window's latest
`requested`.
15. `writesInFlight −= 1`; release a picture hold if rule 13 says so. Then:
    - actual is null → stop (nothing else changes; a failed or unknown write
      is simply dropped).
    - window is no longer the managed record for that key (removed, released)
      → stop.
    - otherwise add `(actual, now)` to the expected list, set the known frame
      to actual; if learning is off → stop.
16. **Park write** (target was a park frame):
    - if actual ≥ target − 2 on **both** width and height (the app accepted
      the small park size) → `lowerMinimum(actual size)` (rule 22);
    - if actual exceeds target by **more than 24** on width **or** height →
      schedule a *larger* refusal recheck in **0.25 s**;
    - a park write that came out *smaller* is ignored.
17. **Tile write** (any other target):
    - if the window has its own minimum and actual is below it by more than
      2 on either axis → the own minimum is lowered to the per-axis minimum
      of (own minimum, actual);
    - `LimitStore.Observe(actual size, appId)` (always; the store itself
      only lowers when the actual is > 2 below the stored value);
    - if actual exceeds target by more than 24 on width or height →
      *larger* recheck in 0.25 s;
    - else if actual is below target by more than 24 on width or height →
      *smaller* recheck in 0.25 s;
    - else accepted, nothing more.
    The threshold is **per axis** (either axis triggers), strictly greater
    than 24. Position differences never trigger a recheck.
18. A scheduled recheck increments the window's *pending rechecks* counter
    and captures: the window, larger/smaller, the write's target, and the
    display's visible area at that moment. It is **not** tied to the
    generation.

### E. Refusal recheck (0.25 s later)
19. Read the window's frame again (`ReadFrame`). On completion: pending
    rechecks −= 1; stop if the window is no longer the managed record for
    its key. The re-read actual is the read result, or the known frame if
    the read returned null. The known frame is set to it (it is **not**
    added to the expected list).
20. **Larger recheck.** An axis *qualifies* if the actual still exceeds the
    target by > 24 **and** is smaller than **0.8 ×** the visible area's size
    on that axis (a window that grew to ≥ 80 % of the screen is not taken as
    a minimum — corrects the "0.9" noted in #13). No qualifying axis → no
    learning (landing just advances). Otherwise:
    - the window's own minimum becomes, per axis: qualifying → max(old own
      minimum or 0, actual); not qualifying → old own minimum or 0. The
      minimum is now present (an axis may be 0);
    - floating windows with a known natural size: natural raised to the
      actual on qualifying axes;
    - `LimitStore.Learn(that minimum, appId)` (store keeps per-axis max);
    - continue with rule 23.
21. **Smaller recheck.** An axis qualifies if the actual is still below the
    target by > 24. None → no change. Otherwise the window's own **maximum**
    becomes, per axis: qualifying → the actual (replacing any old value);
    not qualifying → old maximum or unlimited. Nothing is stored in the
    LimitStore. Continue with rule 23.
22. **lowerMinimum(size)**: read the app's stored minimum (before), call
    `LimitStore.Observe(size, appId)`, read it again (after). If the window
    has **no own minimum** and before and after both exist and differ →
    re-land (rule 3) and render. Otherwise nothing.
23. **After learning**: recompute all targets (projection #25). If this
    window still has a target within **1** of the old target on all four
    components → no rewrite (logged as a refusal; landing advances).
    Otherwise (target changed or gone) → re-land (rule 3) and render. With a
    raised minimum the tile usually no longer fits and the window becomes
    ParkWithPicture; with a lowered maximum the tile is clamped and centred.

### F. Expected-frame echoes
24. Each window keeps a list of (rect, time). Entries are added by every
    write (the target) and every write completion (the actual). Whenever the
    list is read or added to, entries **older than 1.5 s** (now − time >
    1.5) are removed first.
25. A reported frame is **expected** if some remaining entry matches it
    within **2.0** on **every** component (x, y, w, h; ≤ 2 counts). Matching
    does **not** consume the entry. `windowMoved` stores the reported frame
    as the known frame first, and then ignores it for layout purposes when it
    is expected, when the window has a write in flight, or while the mouse
    button is down (window-events feature).

### G. Generation
26. The engine generation is bumped **only** by: a re-land / camera motion
    (`beginMotion(land:)`), interrupting a landing, `suspend()` and
    `releaseAll()`. Direct presentation does not bump it. Effects of a bump:
    - a landing pass started with the old value never sets `placementDone`;
    - a scheduled re-land (rule 3) whose captured value is stale does not
      run;
    - the post-landing settle and the drift check ignore stale values.
    Write completions and refusal rechecks are **not** generation-guarded:
    they still update in-flight counts, known frames and expected lists, and
    can still learn limits, as long as the window is still managed by this
    engine.
27. **Settle and drift check.** When a landing completes, the engine enters
    a settling phase, focuses (tiling-loop rule 33), renders, and
    `coverFade + 0.02` s later (default 0.16 s) — only if the generation is
    unchanged — returns to desk, records laid-out sizes and runs the drift
    check, then settles floats, renders and saves. The drift check computes
    all targets and reads every window's frame. For each read (non-null)
    that arrives while the generation is unchanged, phase is desk, no
    interaction or native drag is running, the window is still managed and
    has **no write in flight**:
    - **park target**: if the actual frame is > 5 % visible and does not
      rest parked at the target → set known frame = actual and rewrite that
      one target (with the drift-check generation; rules 6–14 apply);
    - **tile target**: if the actual differs from the **known frame** (not
      the target) by more than 4 on any component → log only; the known
      frame is not updated and nothing is written.

### H. Interaction with suspend / releaseAll
28. `suspend()`: bumps the generation (rule 26); in-flight writes still
    complete and are processed; pending rechecks still run and may learn
    limits and request a re-land (TrellisWM does not check the frozen flag
    there — see Windows notes).
29. `releaseAll()`: bumps the generation and empties the managed table, so
    later completions only decrement in-flight counts and rechecks stop at
    the "still managed" test. Its own write-back writes are counted by
    releaseAll itself (engine-inbound-api rule 13), not by this writer.

## Data model
- Per managed window (adds to #23/#25 state): `knownFrame: Rect`,
  `requested: Rect?`, `writesInFlight: int`, `pendingRechecks: int`,
  `expected: list of (Rect, time)`, `parked: bool`, `shownSize: Size?`,
  `pictureHeld: bool`, `limits {minimum: Size?, maximum: Size?,
  natural: Size?}`, `appId?`.
- Engine: `generation: int`, `placedGeneration: int`,
  `placementStarted/placementDone: time`, `phase` (desk, moving, …,
  landing, settling), `parkedSmall: set of keys`,
  `probedMinimumApps: set of app ids`.
- Write request (internal): key, target, isPark, learns (true except
  overview), generation, completion callback.
- Constants: echo window 1.5 s, echo tolerance 2.0; refusal slack 24
  (strict >); recheck delay 0.25 s; accepted-park slack 2; own-minimum
  lowering slack 2; store-probe slack 2; refusal screen factor 0.8;
  retarget-unchanged tolerance 1.0; drift park visible fraction 0.05;
  drift tile log threshold 4; shown-size hold slack 1; settle delay
  coverFade + 0.02; re-land delay 0.034 s.

## Worked examples
Common: visible area (0, 25, 1440, 875); clock starts at t = 100.000;
completions arrive 0.050 s after the write unless stated; app id "com.x";
nothing stored in the LimitStore unless stated.

1. **Direct pass, one change.** Targets {a: (10,35,705,855), b:
   (725,35,705,423)}; a's known frame = a's target; b's known frame =
   (725,35,705,855), nothing in flight. Writes: only b → SetFrame(b,
   (725,35,705,423)) at 100.000; b.requested = that; b.expected =
   [((725,35,705,423), 100.000)]; b.inFlight = 1. At 100.050 actual =
   (725,35,705,423): inFlight 0; expected gains (same, 100.050); known =
   actual; tile write: Observe(705×423) (no-op), |Δ| = 0 → accepted.
2. **Same target again while in flight.** At 100.010 another direct pass
   with the same target for b → skipped (rule 10). No second write.
3. **New target while in flight.** At 100.010 b's target becomes
   (725,35,705,600) → second SetFrame issued (inFlight 2). Completion 1 at
   100.050 reports (725,35,705,423) and is compared with *its* target
   (…,423) → accepted. Completion 2 at 100.060 reports (725,35,705,600) →
   accepted. No re-issue logic needed.
4. **Echo.** After example 1, windowMoved(b, (726,35,706,423)) at 100.900 →
   matches the 100.000 entry within 2 → expected, ignored. windowMoved(b,
   (725,35,705,423)) at 101.560 → both entries are older than 1.5 s
   (1.560, 1.510) → pruned → not expected → handled as an app move
   (window-events feature).
5. **Refusal, larger (tile).** Target d = (725,684,348,206). At 100.050
   actual = (725,684,348,300). Tile write: no own minimum; Observe(348×300)
   (nothing stored → no-op); 300 > 206 + 24 → larger recheck scheduled for
   100.300 (pendingRechecks 1). At 100.300 ReadFrame → (725,684,348,300):
   pendingRechecks 0, known = that. Width: 348 > 372? no. Height: 300 > 230
   and 300 < 0.8 × 875 = 700 → qualifies. Own minimum = (0, 300);
   Learn((0,300), "com.x") → store (0,300). Retarget: fits needs height ≥
   max(120, 300) − 0.5 = 299.5; tile 206 → not fits → ParkWithPicture →
   target changed → re-land: generation 7 → 8; at ≈100.334 landing pass
   with generation 8 parks d.
6. **Larger, but near full screen.** Same target, actual height 760 →
   recheck → 760 ≥ 700 → does not qualify → nothing learned, no rewrite.
   (The actual was added to the expected list, so its echo is ignored for
   1.5 s.)
7. **Refusal, smaller.** Target (725,35,348,855), actual (725,35,300,855) →
   300 < 324 → smaller recheck → still 300 wide → maximum = (300,
   unlimited). Retarget: tile clamped to 300 wide and centred → (749, 35,
   300, 855) → changed → re-land.
8. **Within slack.** Target height 206, actual 228 → 228 − 206 = 22 ≤ 24 →
   accepted; Observe(348×228).
9. **Park accepted, store minimum comes down.** Store has "com.x" →
   (800, 600), window has no own minimum. Park write target (1439, 899,
   705, 423) for that window → probe bookkeeping: 705 < 798 → "com.x" added
   to probed-minimum apps. Actual (1439, 899, 705, 423) ≥ target − 2 →
   lowerMinimum(705×423): before (800,600); Observe → 705 < 798, so the
   store lowers to the per-axis minimum of (stored, observed) = (705, 423);
   after ≠ before and no own minimum → re-land.
10. **Park refused.** Park target (1439, 899, 705, 423), actual (1439, 899,
    705, 600) → 600 > 447 → larger recheck → still 600 (< 700) → own
    minimum (0, 600), Learn → re-land.
11. **Null completion.** SetFrame(x, …) for a window the OS lost → callback
    null → inFlight −1, nothing else; next pass will try again (known frame
    ≠ target, nothing in flight).
12. **Resting-parked skip.** Window p flagged parked, known frame (1439,
    860, 705, 855) (OS pulled it up 39), target (1439, 899, 705, 855): same
    size; assume 30 % visible → x equal, 838 ≤ 860 ≤ 900 → rests → skip. If
    the parked flag were false → write.
13. **Landing pass counting.** placeWindows(7) issues 3 writes → counter 3;
    completions at 100.05, 100.06, 100.08 → counter 0 at 100.08 → if
    generation is still 7: placementDone = 100.08, landing advances. If a
    suspend() at 100.07 bumped the generation to 8: placementDone is not
    set; the 100.08 completion is still processed (known frame, echo list,
    limits).
14. **Drift check.** Landing settled (generation 8) at 100.30; drift check
    reads: park target (1439, 899, 705, 855) reported at (1200, 400, 705,
    855), 60 % visible, not resting → known = that, rewrite park target.
    Tile target with known (10,35,705,855) reported at (16,35,705,855) →
    Δx 6 > 4 → log only.
15. **Own minimum lowered by a tile write.** Window own minimum (500, 400);
    tile target (10,35,450,855); actual (10,35,450,855) → 450 < 498 → own
    minimum becomes (450, 400); Observe(450×855); accepted.

## Evidence
Directory `~/Documents/trelliswm-re/evidence/`; summary in
`engine-frame-writer-notes.txt` (new).
- Rules 1, 26 (direct path, no bump): `Engine.presentDirectly(focus:)`
  0x100054f14 (`asm-tiling-presentDirectly.txt`), `canPresentDirectly`
  0x1000469c0 (`asm-tiling-canPresentDirectly.txt`).
- Rule 2: `Engine.placeWindows(generation:)` 0x10003e734 and closures
  0x100057ba4 / 0x100057c60 (`dec-tiling-placeWindows.json`,
  `dec-tiling-pwC1.json`, `dec-tiling-pwC2.json`).
- Rule 3: `beginMotion(land:)` 0x100039df8 generation += 1 at 0x100039e4c,
  after(0.034) at 0x10003a5c8, closure 0x100055094
  (`asm-frame-writer-beginMotion.txt`, `asm-tiling-presentDirectly.txt`
  tail); phase test `& ~4 == 0` in `lowerMinimum` and the recheck closure.
- Rule 4–5: callers of `writeTargets` (objdump): checkPlacement closure,
  snapBack, windowMoved, shapeForOverview (learns byte 0 at 0x10003d430).
- Rules 6–14: `Engine.writeTargets(_:except:generation:done:)` 0x100055500
  (`asm-frame-writer-writeTargets.txt`): parkFrame double compare
  0x100055770/0x1000557bc, rests-parked inline 0x1000557f0–0x100055908
  (0.05, 1, −60−1), known-frame equality 0x100055924, requested +
  in-flight test 0x10005594c–0x100055984, parked flag store 0x100055a3c,
  parkedSmall hold 0x100055a44–0x100055ad4 and 0x100055ea4, shownSize
  0x100055b04, expected prune/append 0x100055b1c–0x100055c0c (1.5),
  writesInFlight 0x100055c18, probedMinimums insert 0x100055c28–0x100055ce4
  (−2), closure context 0x100055d50–0x100055d90, setFrame 0x100055df0.
- Rules 15–18: write completion closure 0x100057cc4
  (`asm-frame-writer-writeCompletion.txt`; symbol
  `write(_:_:parking:generation:learns:done:)` closure #2): in-flight −1
  0x100057d34, hold release 0x100057d48–0x100057e08, nil / identity checks
  0x100057e0c–0x100057eac, `expect` 0x100057ee0, parking branch
  0x100057ef0–0x10005802c (−2, 24, 0.25), tile branch 0x10005808c–0x1000582b4
  (−2 min-merge, observe witness +0x18, ±24, 0.25).
- Rules 19–21, 23: `confirmRefusal` closure 0x1000582c8
  (`asm-frame-writer-confirmRefusalOuter.txt`, readFrame, no generation
  test) and inner 0x10005843c (`asm-tiling-confirmRefusal.txt`): nil
  fallback 0x100058560, 0.8 factor 0x10005859c, natural update
  0x1000587b4, minimum 0x100058810–0x10005887c, learn 0x1000588c0, maximum
  0x10005867c–0x1000586c8, realTargets + 1.0 compare 0x100058a2c–0x100058aa8,
  re-land 0x100058ba0–0x100058bd8.
- Rule 22: `Engine.lowerMinimum(_:to:)` 0x1000571d4
  (`dec-tiling-lowerMinimum.json`).
- Rules 24–25: `ManagedWindow.expect` 0x10002931c, `isExpected` 0x100029540
  (`asm-frame-writer-expect-isExpected.txt`); windowMoved use per
  `engine-tiling-loop-notes.txt`.
- Rule 26: generation stores found only in beginMotion, interruptLanding,
  suspend, releaseAll (objdump scan of `#0xb38]`).
- Rule 27: `advance` 0x10003bc7c (`asm-frame-writer-advance.txt`), `fade`
  0x10003ec7c (`asm-tiling-fade.txt`), fade closure 0x10003feb0
  (`asm-frame-writer-fadeClosureHead.txt`: generation and phase 4 checks,
  phase → 0), `checkPlacement` 0x10003fc50 and closure 0x1000574d4
  (`asm-tiling-checkPlacementClosure.txt`: 0.05, restsParked, 4.0 vs known
  frame, single-target writeTargets).
- Rules 28–29: `findings/engine-inbound-api.md` rules 11, 13; completion
  identity test (rule 15) and recheck identity test (rule 19).

## Confidence
| Rule | Label |
|---|---|
| 1 direct path | Observed |
| 2 landing counting | Observed |
| 3 re-land, 0.034 s | Observed (calls, constant); meaning of phase 4 as "settling" Inferred |
| 4 single-window writers | Observed (callers) |
| 5 overview learns off | Observed (flag value); purpose Inferred |
| 6–10 skip rules | Observed |
| 11 no hold-back while in flight | Observed |
| 12 pre-write bookkeeping | Observed |
| 13 picture hold | Observed; cover purpose Inferred |
| 14 probe set | Observed; link to projection rule 20 Inferred |
| 15 completion head | Observed |
| 16 park branch | Observed; "parking" = branch flag Observed by argument wiring from writeTargets |
| 17 tile branch | Observed |
| 18 recheck capture | Observed |
| 19 recheck read | Observed |
| 20 larger / 0.8 | Observed |
| 21 smaller / maximum | Observed |
| 22 lowerMinimum | Observed |
| 23 retarget / re-land | Observed |
| 24–25 echo list | Observed (writeTargets, completion, expect, isExpected); windowMoved filter Observed in #13 notes |
| 26 generation | Observed |
| 27 settle + drift | Observed (constants, checks); full landing state machine Inferred (camera feature) |
| 28 suspend | Observed (no guard); consequence Inferred |
| 29 releaseAll | Observed (identity test); counting per inbound-api |

## Unknowns
- **Pending-rechecks counter consumers.** The counter is maintained (rules
  18–19) but no reader was identified among the functions read; it may gate
  landing completion or capture. CanvasWM can keep it for diagnostics.
- **Store-minimum probe purpose.** The probed-minimum set (rule 14) is
  filled here and read by projection-targets rule 20; together they look
  like a one-time "try the tile size below the stored minimum" probe per
  app, but the end-to-end effect was not traced at runtime.
- **Landing state machine details** (`advance`: cameraRemaining < 16,
  `windowsNotYetShown`, `realWindowsInPlace` 1-pt checks, capture waits):
  camera/cover features #15/#16. For CanvasWM at rest, "landing done" =
  all completions in with an unchanged generation, then the settle delay.
- **Phase 4 ("settling") and phase 2** names are inferred from transitions
  (3 → 4 in fade, 4 → 0 in the settle closure).
- Log texts ("MINIMUM", "MAXIMUM", "REFUSED", "DRIFT") were identified but
  are diagnostics only.
- `confirmRefusal` has a generation parameter that is not used in any path
  read; rechecks therefore survive suspend (rule 28).

## Windows notes
- **SetFrame contract.** Keep it asynchronous with a read-back actual;
  `SetWindowPos` is synchronous, so the adapter should read back
  `DWMWA_EXTENDED_FRAME_BOUNDS` (converted to DIPs) after the call and post
  the completion to the UI dispatcher. Null on failure (destroyed window,
  UIPI-elevated target).
- **Echo tolerance.** Windows' DIP↔pixel rounding at 125 %/150 % can shift
  a reported edge by about 1 DIP; the 2.0 tolerance covers that, but only if
  the adapter reports the same visible frame it was asked to set (invisible
  DWM borders removed on both sides). `WM_WINDOWPOSCHANGED` /
  `EVENT_OBJECT_LOCATIONCHANGE` for the engine's own write usually arrives
  *before* the completion; the target entry added at write time is what
  absorbs it.
- **Refusal on Windows.** Apps clamp through `WM_GETMINMAXINFO`, so a
  refused size is usually visible immediately in the read-back; the 0.25 s
  recheck still matters for apps that resize themselves after
  `WM_SIZE`. Seeding the own minimum/maximum from `WM_GETMINMAXINFO`
  (`ptMinTrackSize`/`ptMaxTrackSize`) at admission makes most refusals
  disappear; keep this path for the rest.
- **Maximised/minimised windows** reject `SetWindowPos` sizes; the adapter
  should restore (`SW_RESTORE`) before writing, or report null so the writer
  simply retries on the next pass.
- **Suspend guard.** TrellisWM lets refusal rechecks re-land while frozen
  (rule 28). On Windows a virtual-desktop switch hides windows via
  cloaking; a spec may choose to drop the re-land when frozen — flag as a
  deliberate deviation if so.
- **Park writes.** Fully off-screen parking is allowed on Windows
  (park-geometry notes); the "accepted park" test (≥ target − 2) and
  rests-parked test still apply unchanged.
