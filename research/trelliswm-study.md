# TrellisWM — behaviour study for a Windows reimplementation

Target: `/Applications/TrellisWM.app` 0.9.5 (build 200), `Contents/MacOS/TrellisWM`,
thin arm64 Mach-O, SHA-256 `d9148bb061ac096f438a882d5ca3b496ab3a76f867043cf7fa597213fdbb6304`.
Tooling: REA 6.5.0 (source checkout) + Hopper 6.6.0-demo, `nm`, `swift-demangle`.
All work is static (no execution, no patching).

Purpose: understand *behaviour and architecture* well enough to design an
independent Windows window manager. This file is a spec in our own words; it
deliberately does not transcribe TrellisWM code. Build the Windows version
from this spec, not from the decompiled output in `evidence/`. Do not reuse
TrellisWM's name, icons, lesson content, or UI artwork, and do not study or
reproduce its licensing code. Check TrellisWM's EULA for reverse-engineering
terms (this is not legal advice).

Confidence labels: **Observed** = read directly from symbols/data/decompiled
calls. **Inferred** = our interpretation of observed code. **Unknown** = not
yet examined.

---

## 1. Architecture (Observed: Swift module/class names)

| Module | Role | Key types |
|---|---|---|
| `SpacesModel` | Pure layout data + algorithms, platform-free | `LayoutDocument`, `LayoutNode`, `DesktopNode`, `SplitNode`, `PanelNode`, `Entries`/`Entry`, `Rect`, `Size`, `LayoutMetrics`, `WindowLimits`, `Navigator`, `Projection`, `Stretch` |
| `SpacesEngine` | Interaction/state machine over the model | `Engine`, `Arrangement` (static layout edits), `Scene`, `Presenter`, `CameraCurve`, `LayoutTween`, `DropResolver`, `MoveSession`, `Feel` (tuning constants), `Policy` |
| `TrellisWM` | macOS glue | `Coordinator` (`World`, `Display`), `MacWindowSystem`, `AX`, `EventTap`, `Hotkeys`, `AppObserver`, `MacCapture`, `WallpaperCapture`, `MissionControl`, `StageRenderer`, `CollapsedGroupPanel`, `SettingsWindow`, `MenuBarStrip`, stores |
| `Licensing` | out of scope | — |

Inferred design rule: the engine talks to the OS only through injected
interfaces — `Engine.init(displayKey:visibleArea:policy:system:capture:clock:renderer:limits:store:)`.
**Copy this split.** Port model+engine as portable code; write a new
`system`/`capture`/`renderer` layer for Win32.

## 2. Layout model (Observed types, Inferred semantics)

```
LayoutDocument
 ├─ root: LayoutNode           // tree, one per display
 ├─ floating: [FloatingPanel]  // panel, rect, z, size
 ├─ hidden:   [HiddenPanel]    // panel + restore info
 └─ hiddenDesktop: HiddenDesktop?

LayoutNode = Desktop(DesktopNode{id, child?})
           | Split(SplitNode{id, axis: horizontal|vertical, weights:[Double], children:[LayoutNode]})
           | Panel(PanelNode{id, views:[windowKey], selected, tabbed})   // a tab group of 1..n windows
```

- A **panel** is a tab group: several real windows share one tile; one is shown.
- **Desktop node** marks where "the desktop" (empty space) sits in the tree.
- Floating windows live outside the tree with their own rect and z-order.
- IDs come from `UID.make(prefix, ...)` (e.g. prefix `"split"`).

### 2.1 Tree → rectangles
`Arrangement.entries(doc)` → memoised (small `recent` cache keyed by document
equality) → `Arrangement.layOut` → `focusLayout(root, metrics:)`:

1. **Weights only** (`layoutByWeights`, Observed): node gets the rect it is
   given. A split sums its weights; child *i* gets `wᵢ / Σw` of the split's
   extent along its axis, laid out consecutively; recurse.
2. **With minimums** (`layoutWithMinimums` + `distribute`, Inferred from
   structure): compute each subtree's minimum size from window limits; if the
   minimums don't fit, scale by `need/available` (≥1). `distribute` is an
   iterative constraint solver: share space by weight, pin any child that falls
   below its minimum to that minimum, remove it from the free set, repeat with
   the rest (flexbox-style).
3. For splits with ≥3 children, extra "range" entries are emitted for each
   contiguous run of siblings (`rangeId`) — Inferred: these let navigation /
   zoom target a group of adjacent tiles, not just single tiles.
4. Floating panels are mapped into the desktop node's rect.

Each `Entry` = `(node, rect, parent, scale, virtual)`. `scale`/`virtual`
(Inferred) mark tiles laid out in a virtual space larger than the screen.

### 2.2 Placing new windows

**`Arrangement.placeNew(doc, window, framed:, focused:, stageAspect:)`**
(decompiled; `evidence/dec-placeNew.json`). `framed` = the subtree the camera
is currently framing; `focused` = the focused window, optional.

1. Look up `framed`'s rect. If it has none → **float** the new window. (Observed)
2. Choose an **anchor** tile (Observed):
   - the focused window's panel, if it is a real panel and lies inside `framed`;
   - otherwise walk `framed`'s leaves **from last to first** and take the
     last leaf that is a real panel;
   - otherwise the last leaf of `framed`.
3. If the anchor has no rect → float. Otherwise **dock** the new window
   beside the anchor (`dock(..., at: DockTargetSpec(id: anchor, edge:, ...))`),
   with the edge chosen as (Observed operands; field mapping Inferred from
   struct offsets):

   ```
   if (anchor.h / framed.h) <= stageAspect * (anchor.w / framed.w):  edge = right
   else:                                                            edge = bottom
   ```
   i.e. split the anchor along its longer side, measured in units normalised
   to the framed area and scaled by the display's aspect ratio. On a 16:10
   display this alternates right, bottom, right, ... (a dwindle spiral).
   `Edge` raw values (Observed): 0 = left, 1 = right, 2 = top, 3 = bottom.

So new windows always land in the area you are looking at, next to what you
were working on, splitting the longer side. Windows are never inserted to the
left or top by this path.

**`Arrangement.dock(doc, window, at: DockTargetSpec)`** (Observed;
`evidence/dec-dock.json`, `dec-dockUnit.json`, `dec-insertBeside.json`, `fn-normalize.json`):

1. Remove the window's current panel from the tree (`removePanel`). If the
   global `shareRemovedSpace` flag is set, the freed weight goes to its siblings.
2. Insert:
   - `DockTargetSpec.seam` present → `insertAtSeam` (drop between two
     existing tiles on a divider);
   - otherwise `insertBeside(target, edge, fraction f)`: descend to `target`
     and replace it with a new split:

     | edge | axis | children | weights |
     |---|---|---|---|
     | left | horizontal | [new, target] | [f, 1−f] |
     | right | horizontal | [target, new] | [1−f, f] |
     | top | vertical | [new, target] | [f, 1−f] |
     | bottom | vertical | [target, new] | [1−f, f] |

     (axis = vertical iff edge ∈ {top, bottom}; new-first iff edge ∈ {left, top})
   - if the target is not found, insert beside the root instead.
3. `normalize` the tree (Observed):
   - recurse into children;
   - **flatten** a child split with the **same axis** as its parent into the
     parent, scaling the grandchildren's weights by the child's share
     (`w_grand × w_child / Σ w_grand_siblings`);
   - renormalise weights to sum to 1; drop empty children;
   - a split left with one child is replaced by that child.
4. `removeSharing` cleanup (**Unknown** detail).

Consequence: docking right of a tile that is already in a horizontal row adds
a **new column to that row** (flattened), not a nested split, whereas docking
on the other axis nests. The `f` value `placeNew` passes is **Unknown**
(probably 0.5, matching dwindle).

**`arrangeDwindle(_, _, landscape:)`** (Observed) rebuilds a whole set of
windows as a BSP spiral:
`build(i, horizontal) = Split(axis: horizontal, weights: [0.5, 0.5], children: [panel(i), build(i+1, !horizontal)])`,
terminating in a `DesktopNode`. Split weights constant read from binary: **[0.5, 0.5]**.

Other edit ops (names Observed, behaviour Unknown): `dock`, `redock`,
`swap`, `tab`, `splitOffTab`, `float`, `floatBehind`, `floatAll`, `lift`,
`raise`, `hide`, `hideTab`, `remove`, `removePanel`, `showDesktop`,
`hideDesktop`, `contain(_:margin:)`, `floatingSize(original:desktop:minimum:keepSize:)`.

### 2.3 Directional focus — `Navigator.neighbour(of:toward:)`

Decompiled (`evidence/dec-neighbour.json`, `dec-engineMoveToward.json`).
Direction is an `Edge` (left/right/top/bottom). Geometric search over all
laid-out entries, keeping the lowest score (initial best = +∞):

**Candidate filter** (Observed):
- skip the current tile itself and the desktop node;
- skip any node that contains the current tile or is contained by it
  (ancestors/descendants), so only "sideways" tiles qualify;
- the candidate must lie on the requested side of the current tile, and
  overlap it on the perpendicular axis
  (`min(maxA, maxB) − max(minA, minB) > 0`).

**Score** (operations Observed; exact operand mapping partly Inferred):
```
score = |log(size ratio of candidate vs current)|         // prefer similar-sized tiles
      + |candidate.centre⊥ − current.centre⊥| / current.extent⊥   // prefer aligned tiles
      − 0.1  if <flag>                                     // small tie-break bonus
```
`centre⊥` and `extent⊥` are on the axis perpendicular to the move
(`Edge.axis` picks x vs y). The `<flag>` bonus condition is **Unknown**
(likely "candidate was visited recently" or "shares a parent"). Lowest score wins.

**Effect** (`Engine.move(toward:)`, Observed): start from the focused tile
(or the framed one), find the neighbour, then `Engine.frame(neighbour, land:, record:)`.
The camera **frames the neighbour** (pans/zooms to it) and focuses it, and the
move is recorded in back/forward history. If there is no neighbour, nothing
happens. Focus never wraps around.

Windows port: implement on the model's rects (world coordinates), not on
`GetWindowRect`, so it works for tiles that are currently off-screen or parked.

## 3. Camera / zoomable canvas (Observed names, Inferred behaviour)

TrellisWM is not a fixed-grid tiler. The layout lives in **world
coordinates**; a **camera** maps world → screen (`Projection(camera:stage:stretch:)`,
`screen(_:)`, `world(_:)`). The engine can:

- pan / pinch-zoom / scroll (`scroll(dx:dy:precise:zoomModifier:...)`, `pinch`, `touchPinchPan`)
- step in/out of subtrees (`Navigator.stepIn/stepOut`, `zoomInto`, `zoomOut`)
- keep history (`back`, `forward`, `Navigator.history`)
- animate camera moves (`CameraCurve` with `Feel.cameraDuration/cameraDecay/cameraStepCap`)
- show an **overview** with minimap (`enterOverview`, `pickMinimap`, `overviewFrames`)

Inferred: while the camera moves, real windows are **not** moved per frame;
the engine renders **live captures** (`MacCapture`, `ProxyLayer`,
`Feel.pictureRate*`) and only writes real window frames when motion "lands"
(`land`, `beginMotion(land:)`, `placeWindows(generation:)`). Windows outside
the view are **parked** (`parkFrame`, `restsParked`, `Feel.parkSettle`).

### 3.0 Parking off-view windows

Sources: `Engine.parkFrame(_:minimum:)`, `isParkFrame`, `restsParked`
(`evidence/dec-parkFrame.json`, `dec-isParkFrame.json`, `dec-restsParked.json`);
names `Policy.parkOutOfViewSmall`, `Engine.parkedSmall`, `ManagedWindow.parked`,
`SettingsWindow.parkSmallChanged`.

Windows whose tile is outside the camera view are **parked**, i.e. moved
(almost) entirely off every display, rather than hidden or minimised.

**Park size** (Observed):
1. scale the window's size down to fit the display's visible area, keeping
   its aspect ratio, rounding to whole points;
2. never smaller than the window's minimum size (`minimum`, rounded up);
3. a user setting "park out-of-view windows small" (`Policy.parkOutOfViewSmall`)
   shrinks parked windows further (exact factor **Unknown**).

**Park position** (Observed structure; exact coordinates partly Inferred):
- If the window-system layer provides its own park frame, use it.
- Otherwise build **5 candidates**, each a `(spot, reach)` pair of rects:
  spots just outside the visible area near its corners (left/right × above/below,
  plus one more), and a `reach` = the spot extended by **60 pt** toward the
  screen (`±60` constants). Reach models where the OS may clamp the window
  back on-screen.
- Score each candidate = total area of `reach` that intersects **any**
  display (`Rect.intersection`, summed `w × h`). Take the first candidate with
  zero visible area; otherwise the least visible one.

**Recognising a parked window** (Observed): `isParkFrame(frame)` is true when
the frame equals the park frame computed with or without the minimum;
`restsParked` accepts a window as "at rest" when its actual position is within
**1 pt** of the park spot (plus a size check), so the engine stops rewriting it.

**Windows port**:
- Win32 lets windows sit fully off-screen, so park outside the **virtual
  screen** (`GetSystemMetrics(SM_XVIRTUALSCREEN/…CXVIRTUALSCREEN)`), choosing
  the side with no monitors, and test candidates against every
  `EnumDisplayMonitors` rect, as TrellisWM does.
- Keep the parked window's DPI stable: park beyond the monitor it last lived
  on so `WM_DPICHANGED` does not resize it.
- **Crash safety is essential:** persist each window's real frame and restore
  all parked windows on exit, on crash restart, and on display changes.
  Users lose windows otherwise.
- Alternatives and why they are worse: minimise (animations, taskbar churn,
  some apps pause), `SW_HIDE` (drops taskbar button, can break apps), DWM
  cloaking (only for your own windows).

## 3.1 Tuning constants — `Feel` (Observed values)

Read directly from the constant getters (`tools/feel.mjs`, full table in
`symbols/feel-constants.tsv`). Values are exact; **units and meaning are
Inferred from names** (points for sizes, seconds for times, fractions for
0–1 values) and should be confirmed by usage before relying on them.

| Group | Constant | Value | Likely meaning |
|---|---|---|---|
| Camera | `cameraDuration` | 0.5 | camera animation length (s) |
| | `cameraDecay` | 5.5 | exponential decay rate for camera ease |
| | `cameraStepCap` | 0.032 | max time step per frame (s, ≈ 31 fps floor) |
| | `layoutEase` | 0.46 | layout tween duration/ease (s) |
| Gestures | `dragThreshold` | 5 | px before a press becomes a drag |
| | `pinchThreshold` | 0.04 | pinch delta to start zoom |
| | `touchPinchStart` / `touchPanStart` | 0.06 / 0.03 | touch gesture start thresholds |
| | `touchPanScale` | 1.3 | touch pan multiplier |
| | `pinchStep` | 1.2 | zoom factor per step |
| Drag & drop | `pickup` | 0.28 | pickup animation (s) |
| | `dropSettle` | 1.2 | settle time after drop (s) |
| | `tileEdgeBand` | 0.28 | fraction of a tile treated as an edge drop zone |
| | `desktopEdgeBand` / `desktopEdgeMax` | 0.2 / 64 | desktop edge zone: fraction, capped at 64 px |
| | `minSeamHitWidth` | 8 | px hit area for a split seam |
| | `gapHandleReach` | 5 | px reach of a gap handle |
| | `gapDragRest` | 1.2 | s |
| | `handoffWithin` | 16 | px |
| | `groupHold` / `groupHoldSlop` | 0.2 / 6 | hold time (s) / movement slop (px) to group windows |
| Sizes | `fixedMinimum` | 480 × 320 | minimum size assumed for a tile |
| | `usableFloor` | 200 × 120 | smallest usable tile |
| | `compact` | 380 × 260 | below this, use compact chrome |
| | `compactGrabY` | 18 | px grab height in compact mode |
| | `squeezeFloor` | 120 | px |
| | `collapseBelow` | 120 | px; collapse a group below this |
| | `collapsedOutline` | 2 | px outline |
| | `floatCap` | 0.667 | floating window max fraction of area |
| | `floatPadding` | 16 | px |
| | `dockedDim` | 0.25 | dim amount |
| Tabs | `tabContentHeight` | 20 | px |
| | `tabStripUnderlap` | 10 | px |
| | `tabLightsLead` | 72 | px reserved for traffic lights |
| | `stripTuck` | 0.14 | |
| | `nativeTabWatch` / `nativeTabRecheck` | 0.25 / 1.2 | s |
| Overview | `overviewTop` / `overviewBottom` | 28 / 96 | px margins |
| Live previews | `pictureRateChanging` / `pictureRateMoving` | 10 / 3 | **Unknown units** |
| | `pictureRateLive` / `Preview` / `Unseen` / `Away` | 0.2 / 0.167 / 0.033 / 0.0167 | likely intervals or rates; verify |
| Covering | `coverBeyond` / `uncoverWithin` | 0.01 / 0.005 | |
| Robustness | `insistCount` / `insistWindow` | 3 / 3 | retry a refused frame up to 3× within 3 s |
| | `refusalSlack` / `refusalRecheck` | 24 / 0.25 | px tolerance / recheck delay (s) |
| | `placementWait` / `reflowTimeout` | 1.2 / 1.2 | s |
| | `captureWait` | 0.35 | s to wait for a first capture |
| | `parkSettle` | 60 | |
| | `splitSettles` | 1 | |

## 3.2 Keyboard shortcuts and input (Observed)

Sources: `ShortcutAction.rawValue/title` jump tables (decoded with
`tools/swiftstrings.mjs`), `Shortcuts.combo` (`evidence/dec-shortcutCombo.json`),
default tables at `0x1002182c8` / `0x100218328`, `AppDelegate.framingBindings`,
`EventTap.start/handle`.

**Storage**: each action is user-configurable, stored in `NSUserDefaults` as
`shortcut.<rawValue>` (string, parsed by `Shortcuts.decode`). If unset, the
default is `keyCode | (carbonModifiers << 32)` from two constant tables.
Settings has a "restore shortcuts" button (`Shortcuts.restoreDefaults`).

**Defaults** — all on ⌃⌥ (Carbon `0x1800`); ⇧ added = `0x1a00`:

| # | rawValue | Title (as shown in app) | Default | macOS keycode |
|---|---|---|---|---|
| 0 | `everything` | Show everything | ⌃⌥ Space | 0x31 |
| 1 | `stepOut` | Step out a level | ⌃⌥ ↑ | 0x7E |
| 2 | `stepIn` | Step in a level | ⌃⌥ ↓ | 0x7D |
| 3 | `left` | Neighbouring space to the left | ⌃⌥ ← | 0x7B |
| 4 | `right` | Neighbouring space to the right | ⌃⌥ → | 0x7C |
| 5 | `up` | Neighbouring space above | ⌃⌥⇧ ↑ | 0x7E |
| 6 | `down` | Neighbouring space below | ⌃⌥⇧ ↓ | 0x7D |
| 7 | `back` | Back | ⌃⌥ [ | 0x21 |
| 8 | `forward` | Forward | ⌃⌥ ] | 0x1E |
| 9 | `maximize` | Frame the focused window on its own | ⌃⌥ Return | 0x24 |
| 10 | `desktop` | Hide or show the Desktop in this Space | ⌃⌥ D | 0x02 |
| 11 | `split` | Split the active tile with a new Finder window | ⌃⌥ N | 0x2D |

**Saved views ("framings")**, 1–9 (keycodes `0x12 0x13 0x14 0x15 0x17 0x16 0x1A 0x1C 0x19`):
recall = modifiers from `shortcut.views` (default ⌃⌥), save = `shortcut.saveViews`
(default ⌃⌥⇧). So ⌃⌥3 jumps the camera to saved view 3; ⌃⌥⇧3 saves the current view there.

Design note: plain ↑/↓ change **zoom level** (step out/in of the tree);
⇧↑/⇧↓ move **spatially**. ←/→ have no zoom meaning, so they need no ⇧.

**Event tap** (`CGEventTapCreate`, session-level, listen + modify):
- key events are treated as shortcuts only with ⌃ and ⌥ both down and ⌘
  not down (`flags & 0xC0000 == 0xC0000`, `flags & 0x100000 == 0`);
- Escape (keycode 0x35) → `onEscape` (cancel overview/drag);
- mouse down / dragged / up → drag-to-dock, gap dragging, title-bar double-click;
- trackpad gesture / magnify events (types 29 / 30) → `onGesture` → `Engine.pinch`;
- scroll → `Engine.scroll(dx:dy:precise:zoomModifier:...)` (pan; zoom with a modifier — which modifier is **Unknown**);
- `capturesKeys` / `swallowUp` flags let it eat events while an overview or drag is active.

**Windows mapping**:
- Register with `RegisterHotKey` (MOD_CONTROL|MOD_ALT[|MOD_SHIFT] + VK_SPACE,
  VK_UP/DOWN/LEFT/RIGHT, VK_OEM_4 `[`, VK_OEM_6 `]`, VK_RETURN, 'D', 'N',
  '1'–'9'). Use `MOD_NOREPEAT` except for navigation keys.
- **Caution:** Ctrl+Alt is AltGr on many non-US layouts (Ctrl+Alt+[ / ] / digits
  type characters there). Offer Win+Alt as an alternative default, and keep
  bindings user-configurable as TrellisWM does. Avoid Win+Ctrl+←/→ (virtual
  desktop switching) and Win+arrows (Snap).
- Escape/capture during overview: `WH_KEYBOARD_LL`, active only while needed.
- Pinch/pan: precision touchpad via `WM_POINTER`/DirectManipulation or
  `RegisterRawInputDevices` (HID digitizer); Ctrl+wheel as zoom fallback.

## 4. Writing window frames (Observed — decompiled)

`MacWindowSystem.setFrame` → `AX.withoutEnhancedUserInterface(pid) { AX.setFrame(...) }`:

1. Get app element for pid; read `AXEnhancedUserInterface`. If on, set it off
   for the duration of the write, then restore (defer). Ref-counted per pid
   under a lock (`enhancedHolds: [pid: (count, restore)]`) so overlapping
   writes toggle once. Logged once per pid.
2. `AX.setFrame(target, current)`: write **size**, then **position**, skipping
   any component equal to current; then read the frame back and return it.
3. Afterwards, if position drifted by > **0.02** (constant `0x3f947ae147ae147b`),
   rewrite position.
4. `Feel.insistCount/insistWindow/refusalRecheck/refusalSlack` (names only)
   suggest bounded retries for windows that refuse a size.

Other AX writers: `MacWindowSystem.focus`, `StageRenderer.press/pressGroup`
(custom traffic-light buttons for grouped windows).

## 5. OS surfaces used (Observed imports) → Windows equivalents

| macOS (TrellisWM) | Purpose | Windows |
|---|---|---|
| `AXUIElementSetAttributeValue` pos/size | move/resize | `BeginDeferWindowPos`/`DeferWindowPos`/`EndDeferWindowPos` (batch); `SetWindowPos` with `SWP_NOACTIVATE\|SWP_NOZORDER\|SWP_NOOWNERZORDER\|SWP_ASYNCWINDOWPOS` |
| read-back + drift fix | apps clamp sizes | `GetWindowRect` after write; correct with `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` for invisible borders; apps' `WM_GETMINMAXINFO` limits → feed into `WindowLimits` |
| `AXEnhancedUserInterface` off | stop app animating/refusing | no direct analogue; `ShowWindow(SW_RESTORE)` maximised/minimised first; skip elevated windows (UIPI) unless running elevated |
| `AXObserver`, `NSWorkspace` notifications | window/app lifecycle | `SetWinEventHook`: `EVENT_OBJECT_CREATE/DESTROY/SHOW/HIDE`, `EVENT_SYSTEM_FOREGROUND`, `EVENT_SYSTEM_MOVESIZESTART/END`, `EVENT_SYSTEM_MINIMIZESTART/END`, `EVENT_OBJECT_NAMECHANGE`, `EVENT_OBJECT_LOCATIONCHANGE` |
| `CGWindowListCopyWindowInfo` | enumerate windows | `EnumWindows` + filter: visible, not `WS_EX_TOOLWINDOW`, no owner, not cloaked (`DWMWA_CLOAKED`), has caption |
| `_AXUIElementGetWindow` (private) | element → window id | not needed; HWND is the id |
| `CGEventTapCreate` | global mouse/keys, drags | `SetWindowsHookEx(WH_MOUSE_LL / WH_KEYBOARD_LL)`; Raw Input for precision touchpad |
| Carbon hotkeys | shortcuts | `RegisterHotKey` |
| `ScreenCaptureKit` | live previews for camera/overview | `DwmRegisterThumbnail` (live, cheap, GPU-composited) — best fit; `Windows.Graphics.Capture` if pixels are needed |
| `CGSSetConnectionProperty` (private) | **Unknown** property | — |
| Mission Control / Spaces | virtual desktops | `IVirtualDesktopManager` (public, limited) |
| overlay panels (tabs, collapsed groups) | own UI over windows | layered top-most windows `WS_EX_LAYERED\|WS_EX_TOOLWINDOW\|WS_EX_NOACTIVATE\|WS_EX_TRANSPARENT` (as needed) |
| visible area per display | work area | `EnumDisplayMonitors` + `GetMonitorInfo().rcWork`; Per-Monitor-V2 DPI awareness |
| Sparkle | updates | Velopack / WinSparkle |
| `trelliswm://` URL scheme | external commands | registered URL protocol or named pipe |

## 6. Suggested Windows build order

1. Port `SpacesModel` as a pure library (C# or Rust) + unit tests:
   tree, weights layout, minimum-aware distribute, dwindle insert.
2. Win32 `WindowSystem`: enumerate, WinEvent hook, batched `DeferWindowPos`,
   read-back/drift correction, DPI + invisible-border handling.
3. Engine without camera: tile on one monitor, focus/neighbour navigation, hotkeys.
4. Tabs (panel = tab group) + overlay tab strip.
5. Camera/overview using DWM thumbnails; park off-view windows; land on stop.
6. Multi-monitor, virtual desktops, persistence (`SavedDisplay`), settings.

## 7. Open questions / next traces

- `neighbour` tie-break flag (the −0.1 bonus) and what the `log` terms compare exactly.
- Confirm `Feel` units by usage (esp. `pictureRate*`, `parkSettle`, `stripTuck`).
- Fraction `f` passed by `placeNew` → `dock`; `insertAtSeam`; `removeSharing`.
- Exact park candidate coordinates and the "park small" factor.
- Scroll zoom modifier and gesture thresholds in `Engine.scroll` / `Engine.pinch`.
- `trelliswm://` command set.
- `CGSSetConnectionProperty` key and purpose.

## 8. Evidence index (`evidence/`)

| File | What |
|---|---|
| `analyze.json` | Hopper overview: 11,228 procedures, 3,666 strings |
| `dispatch.json` | ObjC/Swift class + method metadata (REA, no engine) |
| `fn-stub.json`, `xrefs-axset.json` | callers of `AXUIElementSetAttributeValue` |
| `dec-setframe.json` | `AX.setFrame` |
| `dec-noeui.json` | `AX.withoutEnhancedUserInterface` + `MacWindowSystem.setFrame` closure |
| `dec-dwindleBuild.json` | dwindle BSP builder |
| `dec-entries.json`, `dec-layOut.json`, `fn-focusLayout.json` | tree → rects pipeline |
| `dec-layoutByWeights.json`, `dec-layoutWithMinimums.json`, `dec-distribute.json` | layout solvers |
| `dec-placeNew.json` | new-window placement (§2.2) |
| `dec-neighbour.json`, `dec-engineMoveToward.json` | directional focus (§2.3) |
| `dec-dock.json`, `dec-dockUnit.json`, `dec-insertBeside.json`, `fn-normalize.json` | docking + tree normalisation (§2.2) |
| `dec-actionRaw.json`, `dec-actionTitle.json`, `dec-shortcutCombo.json`, `dec-shortcutDecode.json`, `dec-areDefaults.json`, `dec-shortcutDefaultsInit.json`, `dec-digitsInit.json`, `dec-framingBindings.json`, `dec-hotkeysHandle.json` | shortcuts (§3.2) |
| `dec-eventTapStart.json`, `dec-eventTapHandle.json` | event tap routing (§3.2) |
| `dec-parkFrame.json`, `dec-isParkFrame.json`, `dec-restsParked.json` | parking (§3.0) |
| `trellis-snap.json` | REA snapshot (reuse with `--snapshot`) |

`symbols/`: `all-defined.{mangled,demangled}.txt` (address-aligned),
`imports.txt`, `spaces-api.txt` (SpacesModel/SpacesEngine API surface),
`feel-constants.tsv` (decoded `Feel` values).

`tools/swiftstrings.mjs`: decodes the String returned by each case of a
jump-table enum getter: `node tools/swiftstrings.mjs <bin> <fnStart> <fnEnd>`
(entries past the real case count are garbage).

`tools/feel.mjs`: decodes `Feel` getters with `objdump` (no Hopper):
`node tools/feel.mjs /Applications/TrellisWM.app/Contents/MacOS/TrellisWM symbols/all-defined.mangled.txt`
(Size constants print only the width; heights were read by hand.)

Reproduce a query:
```sh
cd ~/Documents/rea && nvm use
node scripts/rea.mjs decompile /Applications/TrellisWM.app/Contents/MacOS/TrellisWM <addr> \
  --provider hopper --snapshot ~/Documents/trelliswm-re/evidence/trellis-snap.json --format json
```
Addresses: look up in `symbols/all-defined.demangled.txt` (same line number as the mangled file).
