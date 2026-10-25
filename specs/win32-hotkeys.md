# Spec: win32-hotkeys

## Goal

Make the CanvasWM shortcuts (spec `shortcut-model`) work as system-wide hotkeys
on Windows, using `RegisterHotKey` on a message-only window. The design follows
findings Part 2 (CanvasWM design input) and the human-approved scope. The
feature covers only the mechanism:

1. **Core (pure, Mac-tested), small addition.**
   - `HotkeyPlan.Desired(table, recording)`: which combos should be registered.
     While recording, the answer is "none".
   - `HotkeyPlan.Diff(registered, desired)`: what to unregister and what to
     register.
   - `HotkeyStatus` and `HotkeyReport`: the per-combo result (Registered,
     NotOnLayout, Taken, Failed with an error code).
   - A doc-comment fix on the 11 punctuation members of `Key`.
2. **Win32 (adapter, no policy).**
   - `Win32HotkeySource`: a message-only window on the owner thread, `Apply`,
     a posted `Pressed` event and `Dispose`.
   - `Win32KeyInput`: a key-message reader for the settings recorder and a key
     label from the current keyboard layout.
   - Internal pure helpers: the Key↔VK table, the Modifiers↔`MOD_*` map, the
     hotkey id function, `WM_HOTKEY` decoding, failure mapping, a press queue.
3. **App (#29), documented here only.** The App owns the bindings and the
   recording flag. It calls `Apply(HotkeyPlan.Desired(...))` and, on
   `Pressed`, calls `ShortcutDispatch.Match` and then the engine. There is no
   Engine `HotkeyBinder`, so #9's "no IHotkeys" decision stands.

Out of scope, each with its owner:
- **`ReservedCombos` additions.** These follow the Windows probe (AC28) as a
  separate small Core change once the human has decided. This feature changes
  no `ReservedCombos` data.
- **Any `WH_KEYBOARD_LL` hook.** There is none. Overview key capture and the
  Escape interceptor belong to #16 (see "Notes for #16").
- **Any `SendInput` masking of Alt/Win side effects.** These are only observed
  here (AC30). A mitigation needs a later human decision (see #11 A10).
- **The settings UI, its recorder window, warnings and retry button** belong
  to #29 / settings.

## Placement

### Core
- Project `src/CanvasWm.Core`, existing namespace `CanvasWm.Core.Shortcuts`.
- New file: `src/CanvasWm.Core/Shortcuts/HotkeyPlan.cs`. It holds
  `HotkeyStatus`, `HotkeyReport`, `HotkeyDiff` and `HotkeyPlan`.
- Changed file: `src/CanvasWm.Core/Shortcuts/Key.cs`. Only the XML doc
  comments of the 11 punctuation members change (B1). No member, value or
  order changes.
- Tests go in `tests/CanvasWm.Core.Tests/Shortcuts/HotkeyPlanTests.cs`,
  namespace `CanvasWm.Core.Tests.ShortcutsSpec` (the existing folder and
  namespace).
- Reuses, unchanged: `Key`, `Modifiers`, `KeyCombo`, `ShortcutRules`,
  `DispatchEntry`, `ShortcutDispatch.Build`/`Match` (spec `shortcut-model`).
- No other Core file changes.

### Win32
- Project `src/CanvasWm.Win32` (`net10.0-windows`). The csproj does not
  change: unsafe code and `InternalsVisibleTo` are already set.
- New namespace **`CanvasWm.Win32.Hotkeys`**, in folder
  `src/CanvasWm.Win32/Hotkeys/`:

  | File | Visibility | Content |
  |---|---|---|
  | `Win32HotkeySource.cs` | public | the source (window, Apply, Pressed, Dispose) |
  | `Win32KeyInput.cs` | public | `ReadKeyMessage`, `KeyLabel` (+ internal hkl overloads) |
  | `KeyMap.cs` | internal | Key ↔ VK table, default labels |
  | `ModMap.cs` | internal | Modifiers ↔ `MOD_*` |
  | `HotkeyIds.cs` | internal | combo ↔ hotkey id |
  | `HotkeyMessages.cs` | internal | `ReportFor`, `DecodeHotkey`, `TranslateKeyMessage` |
  | `PressQueue.cs` | internal | FIFO between the window procedure and `Pressed` |

- Changed interop files (additive only):
  - `Interop/NativeMethods.User32.cs`: `RegisterHotKey`, `UnregisterHotKey`,
    `RegisterClassExW`, `CreateWindowExW`, `DestroyWindow`, `DefWindowProcW`,
    `GetKeyState`, `GetKeyboardLayout`, `MapVirtualKeyExW`.
  - `Interop/NativeMethods.Kernel32.cs`: `GetModuleHandleW`.
  - `Interop/NativeConstants.cs` and `Interop/NativeStructs.cs`: the B21
    constants and `NativeWndClassEx`.
- Reuses, unchanged: the `post` contract and owner-thread rule of
  `specs/win32-window-enum.md` A13/B13, and the interop style of #10 B1 /
  #11 B21.
- **Tests:** `tests/CanvasWm.Win32.Tests/Hotkeys/*.cs`, namespace
  `CanvasWm.Win32.Tests.HotkeysSpec`. They reuse `MessageLoopThread` and
  `Native` from `tests/CanvasWm.Win32.Tests/WindowEnum/` unchanged. Test-only
  P/Invoke (`PostMessageW`, `SendInput`, `SetKeyboardState`,
  `GetKeyboardState`, `GetKeyboardLayoutList`, `FindWindowExW`, `IsWindow`,
  `GetForegroundWindow`, `GetClassNameW`) goes in
  `tests/CanvasWm.Win32.Tests/Hotkeys/HotkeyNative.cs`.
- The probe tools live in `tests/CanvasWm.Win32.Tests/Hotkeys/HotkeyProbeManual.cs`
  (AC27–AC34).
- **Earlier test files edited (required, see "Required edits to earlier
  specs" R2):**
  - `tests/CanvasWm.Win32.Tests/WindowEnum/PureHelperTests.cs` (AC15 list);
  - `tests/CanvasWm.Win32.Tests/FrameWriter/PureHelperTests.cs` (AC12 list).
- **Manual checklist:** `windows-verification/win32-hotkeys.md`. It holds
  every [windows-manual] AC with the steps written here.

### Not touched
- No file in `src/CanvasWm.Engine`, `tests/CanvasWm.Engine.Tests`,
  `src/CanvasWm.App` or `Directory.Build.props` changes.
- No text in `src/` or `tests/` may contain the studied app's name or the
  name of its licence module (CLAUDE.md).

## Public API

All public members carry XML docs. A null non-nullable reference argument
throws `ArgumentNullException`. As in `shortcut-model`, an argument `KeyCombo`
with an undefined key (including `default`) or modifier bits outside `All`
throws `ArgumentOutOfRangeException`. All argument checks happen before any
work is done.

### Core: `CanvasWm.Core.Shortcuts`

```csharp
namespace CanvasWm.Core.Shortcuts;

/// <summary>Outcome of asking the OS to own one combo as a system-wide hotkey.</summary>
public enum HotkeyStatus
{
    /// <summary>Registered; presses will be reported.</summary>
    Registered = 0,
    /// <summary>Registered, but the current keyboard layout has no key that produces this
    /// key's code, so it probably cannot be pressed. A warning, not an error.</summary>
    NotOnLayout = 1,
    /// <summary>Not registered: another program (or another registration) already owns the
    /// exact combo.</summary>
    Taken = 2,
    /// <summary>Not registered for any other reason; see HotkeyReport.ErrorCode.</summary>
    Failed = 3,
}

/// <summary>Per-combo result of a registration pass.</summary>
/// <param name="Combo">The combo.</param>
/// <param name="Status">The outcome.</param>
/// <param name="ErrorCode">The OS error code for Taken and Failed (may be 0 for Failed when
/// the OS gave none); 0 for Registered and NotOnLayout.</param>
public readonly record struct HotkeyReport(KeyCombo Combo, HotkeyStatus Status, int ErrorCode)
{
    /// <summary>True for Registered and NotOnLayout (the OS holds the combo for us).</summary>
    public bool IsActive { get; }
}

/// <summary>The OS calls needed to move from the registered set to the desired list.
/// Unregister every ToUnregister entry first, then register ToRegister in order.
/// Value equality of the lists is by reference; compare contents with SequenceEqual.</summary>
/// <param name="ToUnregister">Registered but not desired, sorted ascending by
/// ((int)Key, (int)Modifiers).</param>
/// <param name="ToRegister">Desired but not registered, distinct, in first-occurrence order
/// of the desired list.</param>
public sealed record HotkeyDiff(IReadOnlyList<KeyCombo> ToUnregister, IReadOnlyList<KeyCombo> ToRegister);

/// <summary>Pure planning of which combos should be OS hotkeys (B2-B5).</summary>
public static class HotkeyPlan
{
    /// <summary>The combos to register for a dispatch table: empty while recording; otherwise
    /// the distinct ShortcutRules.IsBindable combos of table, in table order (first occurrence
    /// wins, as in ShortcutDispatch.Match).</summary>
    /// <exception cref="ArgumentNullException">table is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">an entry's combo is invalid (also when
    /// recording).</exception>
    public static IReadOnlyList<KeyCombo> Desired(IReadOnlyList<DispatchEntry> table, bool recording);

    /// <summary>B4. Duplicates in either argument are treated as one.</summary>
    /// <exception cref="ArgumentNullException">registered or desired is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">a combo in either argument is
    /// invalid.</exception>
    public static HotkeyDiff Diff(IEnumerable<KeyCombo> registered, IReadOnlyList<KeyCombo> desired);
}
```

### Win32: `CanvasWm.Win32.Hotkeys` (public)

```csharp
namespace CanvasWm.Win32.Hotkeys;

/// <summary>System-wide hotkeys via RegisterHotKey on a private message-only window. Mechanism
/// only: it registers exactly what Apply is given and reports presses; which combos to register
/// and what a press means are decided by the caller. Construct it on the owner thread (the
/// message-pumping engine thread that also owns Win32WindowSource).</summary>
public sealed class Win32HotkeySource : IDisposable
{
    /// <summary>Records the calling thread as the owner thread and creates the message-only
    /// window on it (B7). Registers nothing.</summary>
    /// <param name="post">Schedules an action to run LATER on the owner thread, from its message
    /// loop, in FIFO order. It must never run the action inline (#10 A13).</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The window class or window could not
    /// be created.</exception>
    public Win32HotkeySource(Action<Action> post);

    /// <summary>Raised on the owner thread, through post, once per accepted WM_HOTKEY (B13-B15).
    /// Never raised inline inside Apply, Dispose or the window procedure, and never after
    /// Dispose.</summary>
    public event Action<KeyCombo>? Pressed;

    /// <summary>Makes the registered set equal to the distinct combos of desired (B9-B11).
    /// Returns one report per distinct desired combo, in first-occurrence order. Combos that are
    /// desired but not registered (for example Taken last time) are attempted again.</summary>
    /// <exception cref="ArgumentNullException">desired is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">a combo is invalid.</exception>
    /// <exception cref="ArgumentException">a combo has none of Ctrl, Alt, Win.</exception>
    /// <exception cref="InvalidOperationException">called on a thread other than the owner
    /// thread.</exception>
    /// <exception cref="ObjectDisposedException">after Dispose.</exception>
    public IReadOnlyList<HotkeyReport> Apply(IReadOnlyList<KeyCombo> desired);

    /// <summary>Unregisters every hotkey and destroys the window (B16). Idempotent. May be called
    /// from any thread; off the owner thread the OS cleanup is passed to post.</summary>
    public void Dispose();
}

/// <summary>Keyboard helpers for the settings UI. Pure apart from reading the calling thread's
/// key state and keyboard layout.</summary>
public static class Win32KeyInput
{
    /// <summary>Translates a WM_KEYDOWN / WM_SYSKEYDOWN that the calling thread is handling right now
    /// into a combo, reading Ctrl/Alt/Shift/Win with GetKeyState (B18). Returns null for any other
    /// message, an auto-repeat, or a key that is not a Core Key. The combo may be unbindable
    /// (Escape, no modifier): the caller passes it to ShortcutEditor, which decides.</summary>
    public static KeyCombo? ReadKeyMessage(uint message, nint wParam, nint lParam);

    /// <summary>A short display label for key on the calling thread's current keyboard layout
    /// (B19). Never null or empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">key undefined.</exception>
    public static string KeyLabel(Key key);
}
```

### Win32: internal surface (names are binding; tests use them)

```csharp
namespace CanvasWm.Win32.Hotkeys;

internal static class KeyMap
{
    /// <summary>86.</summary>
    internal const int Count = 86;
    /// <summary>Table B20. AOORE for an undefined key.</summary>
    internal static int ToVk(Key key);
    /// <summary>Reverse of ToVk; false for every VK not in the table (including values outside 0..255).</summary>
    internal static bool TryFromVk(int vk, out Key key);
    /// <summary>True exactly for the 11 punctuation keys (VK_OEM_*).</summary>
    internal static bool IsLayoutAssigned(Key key);
    /// <summary>Fixed label (B19 table). AOORE for an undefined key.</summary>
    internal static string DefaultLabel(Key key);
}

internal static class ModMap
{
    /// <summary>Exact map B21 (Alt→MOD_ALT 0x1, Ctrl→MOD_CONTROL 0x2, Shift→0x4, Win→0x8). No MOD_NOREPEAT.
    /// AOORE for bits outside Modifiers.All.</summary>
    internal static uint ToModFlags(Modifiers modifiers);
    /// <summary>ToModFlags(modifiers) | MOD_NOREPEAT.</summary>
    internal static uint RegisterFlags(Modifiers modifiers);
    /// <summary>Reverse map of the four MOD_* bits; every other bit (MOD_NOREPEAT included) is ignored.</summary>
    internal static Modifiers FromModFlags(uint flags);
}

internal static class HotkeyIds
{
    /// <summary>0xBFFF: the highest id an application may use.</summary>
    internal const int MaxApplicationId = 0xBFFF;
    /// <summary>(int)combo.Key * 16 + (int)combo.Modifiers. AOORE for an invalid combo.</summary>
    internal static int For(KeyCombo combo);
    /// <summary>Inverse of For; false when id is not For(c) for any valid combo c.</summary>
    internal static bool TryDecode(int id, out KeyCombo combo);
}

internal static class HotkeyMessages
{
    internal const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    /// <summary>B12.</summary>
    internal static HotkeyReport ReportFor(KeyCombo combo, bool registered, int lastError, bool onLayout);
    /// <summary>B14. registeredById maps hotkey id → combo for the current registrations.</summary>
    internal static KeyCombo? DecodeHotkey(nint wParam, nint lParam, IReadOnlyDictionary<int, KeyCombo> registeredById);
    /// <summary>B18 with the modifier state passed in.</summary>
    internal static KeyCombo? TranslateKeyMessage(uint message, nint wParam, nint lParam,
                                                  bool ctrl, bool alt, bool shift, bool win);
}

/// <summary>FIFO between the window procedure and the Pressed event (B15). No OS access.</summary>
internal sealed class PressQueue
{
    internal PressQueue(Action<Action> post, Action<KeyCombo> deliver);
    internal void Enqueue(KeyCombo combo);
    /// <summary>Drops queued presses; later Enqueue calls and pending drains deliver nothing.</summary>
    internal void Close();
    internal int QueuedCount { get; }
}

// On Win32HotkeySource, for tests:
//   internal nint WindowHandle { get; }                          // 0 after the window is destroyed
//   internal IReadOnlyCollection<KeyCombo> RegisteredCombos { get; }
//   internal const string WindowClassName = "CanvasWm.HotkeyWindow";
// On Win32KeyInput, for tests:
//   internal static string KeyLabel(Key key, nint hkl);
//   internal static bool IsOnLayout(Key key, nint hkl);
```

## Behaviour

### A. Core

- **B1 (Key doc fix).** In `Key.cs`, the summary of each of the 11 punctuation
  members becomes exactly:
  `The punctuation key that types "X" on the US layout. It is identified by the
  key code the layout gives it, not by its position: on other layouts it may be
  a different physical key, type a different character, or be absent.`
  X is, in member order: `-`, `=`, `[`, `]`, `\`, `;`, `'`, `,`, `.`, `/` and
  `` ` `` (for Quote the doc text is `"'"`; XML-escape as needed). The phrase
  "US-layout punctuation position" no longer appears in `src/CanvasWm.Core`.
  No other text in `Key.cs` changes.
- **B2 (Desired).**
  1. Validate first: `table` non-null, and every entry's `Combo` valid. This
     happens even when `recording` is true.
  2. When `recording` is true, the result is empty (findings Part 1 rule 7,
     D8).
  3. Otherwise walk `table` in order. Keep an entry's combo when
     `ShortcutRules.IsBindable(combo)` holds and the combo has not been kept
     already. Skipped entries are not an error: CanvasWM never registers a
     bare key or Escape (shortcut-model A11, D6 "Escape never registered").
  4. The result is a new read-only list.
- **B3 (Desired equals what Match can hit).** For a table from
  `ShortcutDispatch.Build(b)`, `Desired(table, false)` holds exactly the
  distinct combos of `table`, in order, because `Build` only emits bindable
  combos. Every combo `c` in it has `Match(table, c) != null`.
- **B4 (Diff).** Let `R` be the set of `registered` and `D` the distinct
  `desired` in first-occurrence order.
  - `ToUnregister` = `R − D`, sorted ascending by `((int)Key,
    (int)Modifiers)`.
  - `ToRegister` = `D − R`, in `D` order.
  - A combo in both is left alone: no OS call. A command change for a kept
    combo needs nothing, because the command is looked up by combo at press
    time (D4).
  - Neither input is mutated. The result lists are new and read-only.
- **B5 (HotkeyReport.IsActive).** `IsActive` is true exactly when `Status`
  is `Registered` or `NotOnLayout`.

### B. Win32: interop and threading

- **B6 (interop style).** The interop style is the same as #10 B1 and #11
  B21:
  - `[LibraryImport]` only, no `[DllImport]`;
  - UTF-16;
  - `SetLastError = true` on `RegisterHotKey`, `UnregisterHotKey`,
    `RegisterClassExW`, `CreateWindowExW` and `DestroyWindow`;
  - the window procedure is one `[UnmanagedCallersOnly]` static method
    passed as `delegate* unmanaged<nint, uint, nint, nint, nint>` in
    `NativeWndClassEx.lpfnWndProc`. There is no managed delegate for the GC
    to collect.

  The code never declares or calls `SendInput`, `keybd_event`,
  `SetWindowsHookEx*`, `SendMessage*` or any timer API.
- **B7 (construction).** The constructor:
  1. checks `post` for null, then records `Environment.CurrentManagedThreadId`
     as the owner thread;
  2. registers the window class `WindowClassName` once per process with
     `RegisterClassExW`. `hInstance = GetModuleHandleW(null)`, the window
     procedure is B8, and every other field is 0. A failure with
     `ERROR_CLASS_ALREADY_EXISTS` (1410) counts as success. Any other failure
     throws `Win32Exception(lastError)`;
  3. calls `CreateWindowExW(0, WindowClassName, null, 0, 0, 0, 0, 0,
     HWND_MESSAGE (−3), 0, hInstance, 0)`. If it returns 0, it throws
     `Win32Exception(lastError)`;
  4. adds `hwnd → this` to a static, lock-guarded map that the window
     procedure uses. Messages that arrive before this step (`WM_NCCREATE`,
     `WM_CREATE`, …) go to `DefWindowProcW`.

  The window is message-only. It is never shown, gets no input focus and
  receives no broadcasts. Messages are dispatched to it inside modal loops,
  which a thread-level hotkey (`hWnd = NULL`) would not get (D1).
- **B8 (window procedure).** For `WM_HOTKEY` (0x0312):
  - look up the instance by `hwnd` (unknown → return 0);
  - `c = DecodeHotkey(wParam, lParam, registeredById)`;
  - when `c` is non-null, call `pressQueue.Enqueue(c)`;
  - return 0.

  Every other message → `DefWindowProcW`. The procedure catches every
  exception and discards it, because exceptions must not cross the native
  boundary (#10 A17). Note that the procedure never raises `Pressed` itself.
- **B9 (Apply: validation).** The checks run in this order, before any OS
  call:
  1. not disposed (`ObjectDisposedException`);
  2. owner thread (`InvalidOperationException`);
  3. `desired` non-null;
  4. every combo valid (`ArgumentOutOfRangeException`);
  5. every combo has `ShortcutRules.HasRequiredModifier`
     (`ArgumentException`). This is a call-contract guard taken from Core: a
     bare global key is never registered. Escape combos with a modifier are
     accepted at this level, because the probe needs Ctrl+Shift+Escape;
     `Desired` already excludes them for the App.

  When any check fails, the registered set is unchanged.
- **B10 (Apply: OS calls).**
  1. `diff = HotkeyPlan.Diff(RegisteredCombos, desired)`.
  2. For each combo in `diff.ToUnregister`, in order: call
     `UnregisterHotKey(hwnd, HotkeyIds.For(c))` and remove the combo from the
     registered map whatever the result. A failure means the OS does not
     hold it.
  3. `hkl = GetKeyboardLayout(0)`, read once per Apply.
  4. For each distinct desired combo `c`, in first-occurrence order:
     - **Kept** (registered before this call): no OS call. The report is
       `ReportFor(c, true, 0, IsOnLayout(c.Key, hkl))`.
     - **Otherwise**: `ok = RegisterHotKey(hwnd, HotkeyIds.For(c),
       ModMap.RegisterFlags(c.Modifiers), KeyMap.ToVk(c.Key))`, and `err = ok
       ? 0 : Marshal.GetLastPInvokeError()`. When `ok`, add `c` to the
       registered map. The report is `ReportFor(c, ok, err,
       IsOnLayout(c.Key, hkl))`.
  5. Return the reports as a new read-only list.

  Registration is per combo (D3). One failed combo never stops or rolls back
  the others. `MOD_NOREPEAT` is always set (D2, D5).
- **B11 (Apply: what it is not).**
  - Apply holds no binding table, recording flag, retry timer, reserved list
    or command knowledge.
  - Retrying a `Taken` combo happens only because the next `Apply` sees it
    as desired-but-unregistered. When to call `Apply` is the App's choice
    (A5).
  - Apply sends no window message and creates no thread or timer.
- **B12 (ReportFor).**

  | registered | lastError | onLayout | Report |
  |---|---|---|---|
  | true | any | true | `(c, Registered, 0)` |
  | true | any | false | `(c, NotOnLayout, 0)` |
  | false | 1409 | any | `(c, Taken, 1409)` |
  | false | other (incl. 0) | any | `(c, Failed, lastError)` |

### C. Win32: presses

- **B13 (delivery chain).** The chain runs as follows:
  1. `WM_HOTKEY` reaches the window procedure (B8);
  2. `DecodeHotkey` turns it into a combo;
  3. `PressQueue.Enqueue` stores it;
  4. `post(Drain)` runs later;
  5. `Pressed(combo)` is raised with the subscribers at that moment.

  With no subscribers, the press is discarded. `Pressed` runs on the owner
  thread. A key-up is never reported. Auto-repeat is never reported either
  (`MOD_NOREPEAT`).
- **B14 (DecodeHotkey).** The checks, in order:
  1. `id = (int)(long)wParam`. When `id < 0` (`IDHOT_SNAPWINDOW` −1,
     `IDHOT_SNAPDESKTOP` −2, the system ids) or `id > MaxApplicationId` →
     null.
  2. `registeredById` has no `id` → null. This is a stale message: the combo
     was unregistered after the message was queued.
  3. Let `c = registeredById[id]`, `low = lParam & 0xFFFF` and `high =
     (lParam >> 16) & 0xFFFF`. When `ModMap.FromModFlags(low) !=
     c.Modifiers` or `high != KeyMap.ToVk(c.Key)` → null. A mismatch is
     ignored silently, because Win32 has no logger (A4).
  4. Otherwise → `c`.
- **B15 (PressQueue).** This has the same semantics as #10 B17 `EventPump`,
  with one deliver callback:
  - `Enqueue(c)` appends `c` to a FIFO. If no drain is pending or running,
    it marks one pending and calls `post(Drain)`.
  - `Drain` clears the pending mark and sets `running`. It then dequeues and
    delivers in FIFO order until the queue is empty, and clears `running`.
  - Presses enqueued during a drain are delivered by the same drain.
  - A `Drain` that is called while another drain is running on the stack (a
    handler that pumps messages) returns at once, so handlers never nest.
  - If `post` runs `Drain` inline, during `Enqueue`, `Drain` throws
    `InvalidOperationException` and the press stays queued. The window
    procedure discards that exception (B8). This is the detectable form of a
    contract violation by #29.
  - If `deliver` throws, the exception propagates out of `Drain`, and the
    press is not re-delivered. If presses remain queued, a new drain is
    posted before the exception propagates.
  - After `Close()`, queued presses are dropped. `Enqueue` stores nothing,
    and a drain that is already posted delivers nothing.
- **B16 (Dispose).** `Dispose` is idempotent.
  - **On the owner thread:**
    1. mark the source disposed;
    2. call `pressQueue.Close()`;
    3. call `UnregisterHotKey` for every registered id and clear the map;
    4. call `DestroyWindow(hwnd)`, remove the map entry and set
       `WindowHandle` to 0.
  - **On another thread:** steps 1–2 happen at once, so no `Pressed` is
    raised after `Dispose` returns. Steps 3–4 are passed to `post`, because
    `UnregisterHotKey` and `DestroyWindow` must run on the window's thread.
    If `post` throws, or the loop never runs the action, the registrations
    live until the process exits. Windows frees them then (D4).
  - After `Dispose`, `Apply` throws `ObjectDisposedException`. Removing a
    `Pressed` handler still works.

### D. Win32: settings recorder and labels

- **B17 (recorder flow; #29 owns it, documented for the developer of
  #29).**
  1. When recording starts, the App calls `Apply(HotkeyPlan.Desired(table,
     true))`, which unregisters everything. That lets the settings window
     receive a combo that is currently bound (findings rule 14, D8).
  2. The focused settings window handles `WM_KEYDOWN` **and**
     `WM_SYSKEYDOWN`. It calls `Win32KeyInput.ReadKeyMessage`, feeds a
     non-null result to `ShortcutEditor.RecordForAction` / `RecordForViews`,
     and marks the message handled. It also swallows `WM_SYSCHAR`, so Alt
     combos do not open menu mode.
  3. Recording stops when the window deactivates (`WM_ACTIVATE` inactive or
     `WM_KILLFOCUS`), or when the editor finishes. On stop, the App calls
     `Apply(HotkeyPlan.Desired(table, false))`.
- **B18 (TranslateKeyMessage / ReadKeyMessage).**
  1. `message` is not `WM_KEYDOWN` (0x0100) or `WM_SYSKEYDOWN` (0x0104) →
     null.
  2. `lParam` bit 30 (previous key state, meaning auto-repeat) is set → null
     (A6).
  3. `vk = (long)wParam`. If it is outside 0..255, or `KeyMap.TryFromVk`
     fails → null. This covers modifier VKs, numpad, `VK_OEM_102`,
     PrintScreen, `VK_PROCESSKEY` and `VK_PACKET`.
  4. Otherwise the result is `new KeyCombo(key, mods)`, where `mods`
     contains Ctrl, Alt, Shift and Win exactly for the true flags.
  5. The extended-key bit (lParam bit 24) is ignored (A6).

  `ReadKeyMessage` supplies the flags as follows:
  - `ctrl = GetKeyState(VK_CONTROL) < 0`;
  - `alt = GetKeyState(VK_MENU) < 0`;
  - `shift = GetKeyState(VK_SHIFT) < 0`;
  - `win = GetKeyState(VK_LWIN) < 0 || GetKeyState(VK_RWIN) < 0`.

  Left and right modifiers are not told apart (findings rule 11). AltGr
  arrives as Ctrl+Alt and is recorded as Ctrl+Alt (D8). The UI may warn
  using `MayClashWithAltGr`.
- **B19 (labels).** `KeyLabel(key)` is `KeyLabel(key, GetKeyboardLayout(0))`.
  - **Not `IsLayoutAssigned(key)`** → `DefaultLabel(key)`:
    - letters `"A"`…`"Z"`;
    - digits `"0"`…`"9"`;
    - `"F1"`…`"F24"`;
    - `"Left"`, `"Right"`, `"Up"`, `"Down"`;
    - `"Space"`, `"Enter"`, `"Tab"`, `"Backspace"`, `"Delete"`, `"Escape"`,
      `"Insert"`, `"Home"`, `"End"`, `"PageUp"`, `"PageDown"`.
  - **The 11 punctuation keys:**
    1. `r = MapVirtualKeyExW(ToVk(key), MAPVK_VK_TO_CHAR (2), hkl)`.
    2. `ch = (char)(r & 0xFFFF)`. The high bit, which marks a dead key, is
       ignored.
    3. If `ch < 0x20` (including 0, no translation) → `DefaultLabel(key)`,
       the US character: `"-"`, `"="`, `"["`, `"]"`, `"\"`, `";"`, `"'"`,
       `","`, `"."`, `"/"`, `` "`" ``.
    4. Otherwise → `char.ToUpperInvariant(ch).ToString()`.

  **Why `MapVirtualKeyExW(VK_TO_CHAR)`.** It is stateless and returns the
  unshifted base character of the key. `ToUnicodeEx` needs a key-state array
  and can consume or clear a pending dead key in the calling thread's
  keyboard state. `GetKeyNameTextW` returns localised words or names of
  combining marks, which are inconsistent as labels. (Decision for this
  spec; see A2.)

  `IsOnLayout(key, hkl)` is `!IsLayoutAssigned(key) ||
  MapVirtualKeyExW(ToVk(key), MAPVK_VK_TO_VSC_EX (4), hkl) != 0`. Identity
  VKs (letters, digits, F-keys, navigation) always count as on the layout
  (A3).

### E. Win32: tables

- **B20 (Key → VK; VK identity, human-approved v1 = findings D6 option a).**
  All values are written in hex in source (see AC8).

  | Core `Key` | VK |
  |---|---|
  | A … Z | 0x41 … 0x5A |
  | D0 … D9 | 0x30 … 0x39 (never `VK_NUMPAD*`) |
  | F1 … F24 | 0x70 … 0x87 |
  | Left, Up, Right, Down | 0x25, 0x26, 0x27, 0x28 |
  | Space 0x20 · Enter 0x0D · Tab 0x09 · Backspace 0x08 · Delete 0x2E · Escape 0x1B | |
  | Insert 0x2D · Home 0x24 · End 0x23 · PageUp 0x21 · PageDown 0x22 | |
  | Minus 0xBD · Equals 0xBB · LeftBracket 0xDB · RightBracket 0xDD · Backslash 0xDC | |
  | Semicolon 0xBA · Quote 0xDE · Comma 0xBC · Period 0xBE · Slash 0xBF · Backquote 0xC0 | |

  The table is bijective over its 86 rows. `IsLayoutAssigned` is true
  exactly for the 11 rows from Minus to Backquote.
- **B21 (constants).**

  | Name | Value | Name | Value |
  |---|---|---|---|
  | MOD_ALT | 0x0001 | MOD_CONTROL | 0x0002 |
  | MOD_SHIFT | 0x0004 | MOD_WIN | 0x0008 |
  | MOD_NOREPEAT | 0x4000 | WM_HOTKEY | 0x0312 |
  | WM_KEYDOWN | 0x0100 | WM_SYSKEYDOWN | 0x0104 |
  | HWND_MESSAGE | (nint)(−3) | IDHOT_SNAPWINDOW / IDHOT_SNAPDESKTOP | −1 / −2 |
  | ERROR_HOTKEY_ALREADY_REGISTERED | 1409 | ERROR_CLASS_ALREADY_EXISTS | 1410 |
  | MAPVK_VK_TO_CHAR | 2 | MAPVK_VK_TO_VSC_EX | 4 |
  | VK_SHIFT / VK_CONTROL / VK_MENU | 0x10 / 0x11 / 0x12 | VK_LWIN / VK_RWIN | 0x5B / 0x5C |

  **Pitfall:** Core `Modifiers.Ctrl` is 1 and `Alt` is 2, the reverse of the
  `MOD_*` values. `ModMap` maps member by member, never by numeric cast.
- **B22 (ids).** `HotkeyIds.For(c) = (int)c.Key * 16 + (int)c.Modifiers`.
  Every valid combo gets a distinct id in [16, 1775] ⊂ [0, 0xBFFF]. Ids are
  per window, so two `Win32HotkeySource` instances may use the same id.
  `TryDecode(id)` succeeds exactly when `id / 16` is a defined `Key`, with
  `Modifiers = id % 16`, and `id` ≥ 0.

### F. App wiring (#29; documentation only, no code here)

- **B23.** The App works as follows:
  - it builds `table = ShortcutDispatch.Build(bindings)` and holds
    `recording`;
  - on start-up, after the engines start (studied order, rule 5), on every
    bindings change, and on recording start and stop, it calls
    `source.Apply(HotkeyPlan.Desired(table, recording))` on the owner
    thread;
  - on `Pressed(c)`: when `recording` is true, it does nothing. Otherwise
    `cmd = ShortcutDispatch.Match(table, c)`, and a non-null `cmd` goes to
    the engine per shortcut-model B3/B4. A press with no target engine does
    nothing (it was consumed by the OS anyway);
  - engine suspend, an open overview and "no active engine" do **not**
    change the desired set (findings rule 7, D4);
  - it does not drive `ShortcutDispatch.KeyDown`/`KeyUp`
    (shortcut-model "Notes for #12");
  - the settings UI shows reports where `!IsActive` as "unavailable", and
    `NotOnLayout` as a warning. It may also warn on `ReservedCombos.Find`,
    `MayClashWithAltGr`, and F12 (reserved for debuggers, D6).

## Edge cases & errors

- **E1.** `new Win32HotkeySource(null!)` → `ArgumentNullException`, with no
  window created. `Apply(null!)` → `ArgumentNullException`.
- **E2.** `Apply` with duplicates: each combo is registered once and
  reported once, at its first position.
- **E3.** `Apply([])` unregisters everything and returns an empty list.
- **E4.** Two sources (on the same or different threads) registering the same
  combo: the second gets `Taken` (1409). When the first unregisters, the
  second's next `Apply` registers it.
- **E5.** A `WM_HOTKEY` that was queued before `Apply` unregistered its combo
  is dropped (B14.2). A `WM_HOTKEY` for a combo that `Apply` kept is
  delivered.
- **E6.** `Pressed` subscribed after a press was enqueued but before the
  drain still receives it (subscribers are read at delivery).
- **E7.** A `Pressed` handler that calls `Apply` or `Dispose` on the owner
  thread is allowed. After `Dispose`, the remaining queued presses are
  dropped.
- **E8.** Numpad duplicates: with NumLock off, numpad Home/End/arrows/Ins/Del
  and numpad Enter produce the same VKs as the dedicated keys. Hotkeys on
  them fire from either key, and the recorder records either as the
  dedicated `Key` (D6, A6).
- **E9.** Shell-owned combos (Win+L, Win+D, …) may register successfully and
  never fire, or fire while Windows also acts. Only the AC28 probe tells.
  `Registered` is not a promise that the combo works (D3).
- **E10.** `KeyLabel` on a layout where a punctuation VK has no key returns
  the US character. `Apply` reports such a combo as `NotOnLayout`.
- **E11.** `HotkeyPlan.Desired` with a hand-built table holding `(K, Shift)`
  or `(Escape, Win+Alt)` skips those entries. With a `default(KeyCombo)`
  entry it throws `ArgumentOutOfRangeException`.

## Acceptance criteria

Legend:
- **[mac]**: an xUnit test in `tests/CanvasWm.Core.Tests/Shortcuts/`, or a
  command or inspection on the Mac.
- **[windows]**: an automated xUnit test in
  `tests/CanvasWm.Win32.Tests/Hotkeys/`, run with `dotnet test
  tests/CanvasWm.Win32.Tests` in an interactive Windows session.
- **[windows-manual]**: an item in `windows-verification/win32-hotkeys.md`.

Notation (as shortcut-model):
- `WA = Win|Alt`, `CA = Ctrl|Alt`, `WCA = Win|Ctrl|Alt`, `+S` adds Shift;
- `k(K, m) = new KeyCombo(K, m)`;
- test combos `T1 = k(F24, Ctrl|Alt|Shift|Win)`, `T2 = k(F22, Ctrl|Alt|Shift)`,
  `T3 = k(F21, Ctrl|Alt|Shift)`;
- `LP(m, vk) = (nint)((vk << 16) | m)`, a `WM_HOTKEY` lParam.

### Mac

- **AC1** [mac] `HotkeyPlan.Desired`:
  - **(a)** `Desired(Build(WinAlt), false)` has 30 combos and equals
    `Build(WinAlt).Select(e => e.Combo)` in order. Entry 0 is `k(O, WA)`,
    entry 12 is `k(D1, WA+S)` and entry 13 is `k(D1, WCA)`.
  - **(b)** `Desired(Build(WinAlt), true)` is empty.
  - **(c)** With Back and Forward both `k(B, WA)` (via `WithAction`), the
    result has 29 combos and `k(B, WA)` appears once, at Back's position.
  - **(d)** A hand-built table `[(k(K, Shift), X), (k(Escape, WA), X),
    (k(O, WA), X), (k(O, WA), Y)]` gives `[k(O, WA)]`.
  - **(e)** `Desired([], false)` is empty, and `Desired(Build(Unbound),
    false)` is empty.
  - **(f)** A null table → `ArgumentNullException`. A table with
    `default(KeyCombo)` → `ArgumentOutOfRangeException` for both `recording`
    values.
  - **(g)** For every combo `c` of (a), `Match(Build(WinAlt), c)` is
    non-null.

  (B2, B3; findings rule 6/7, D4, D8)
- **AC2** [mac] `HotkeyPlan.Diff`:
  - **(a)** registered `{k(C,WA), k(A,WA), k(B,WA)}`, desired `[k(C,WA),
    k(D,WA), k(A,WA), k(D,WA)]` → `ToUnregister == [k(B,WA)]`, `ToRegister
    == [k(D,WA)]`.
  - **(b)** registered `{k(O,WA), k(A,CA), k(A,WA), k(A,Ctrl)}`, desired
    `[]` → `ToUnregister == [k(A,Ctrl), k(A,CA), k(A,WA), k(O,WA)]`
    (sorted by `(int)Key`, then by `(int)Modifiers` ascending: Ctrl = 1,
    CA = 3, WA = 10), and `ToRegister` is empty.
  - **(c)** registered `{}`, desired `[T2, T1, T2]` → `ToUnregister` empty,
    `ToRegister == [T2, T1]`.
  - **(d)** registered given with duplicates `[T1, T1]`, desired `[T1]` →
    both lists empty.
  - **(e)** Equal sets in a different order → both lists empty.
  - **(f)** The inputs are unchanged afterwards. Both result lists are
    read-only (casting to `IList<KeyCombo>` and adding throws
    `NotSupportedException`).
  - **(g)** A null in either argument → `ArgumentNullException`. A
    `default(KeyCombo)` in either → `ArgumentOutOfRangeException`.

  (B4)
- **AC3** [mac] Recording round trip, as the App does it:
  - from `reg = {}`, `d1 = Diff(reg, Desired(Build(WinAlt), false))`
    registers 30;
  - then with `reg = those 30`, `Diff(reg, Desired(…, true))` unregisters
    all 30 (sorted per B4) and registers none;
  - then from `{}` again, `Diff({}, Desired(…, false))` equals `d1`
    elementwise.
  - Rebinding Back from `k(LeftBracket, WA)` to `k(B, WA)`: `Diff(30 old,
    Desired(new))` → `ToUnregister == [k(LeftBracket, WA)]` and
    `ToRegister == [k(B, WA)]` (findings worked example 3).

  (B2, B4, B17; findings D8 example 5)
- **AC4** [mac] Types:
  - `HotkeyStatus` raw values are Registered 0, NotOnLayout 1, Taken 2,
    Failed 3;
  - `HotkeyReport.IsActive` is true for Registered and NotOnLayout, false
    for Taken and Failed;
  - `new HotkeyReport(T1, Taken, 1409) == new HotkeyReport(T1, Taken,
    1409)`.

  (B5)
- **AC5** [mac] `Key` doc fix:
  - `rg -n "US-layout punctuation position" src/CanvasWm.Core` prints
    nothing;
  - `rg -c "on the US layout. It is identified by the key code"
    src/CanvasWm.Core/Shortcuts/Key.cs` prints `11`;
  - the Core shortcut AC4 test (86 members, raw values) still passes.

  (B1)
- **AC6** [mac] Architecture:
  - `HotkeyStatus`, `HotkeyReport`, `HotkeyDiff` and `HotkeyPlan` are public
    types in `CanvasWm.Core.Shortcuts` in assembly `CanvasWm.Core`;
  - the existing shortcut AC30 tests pass with them present: public surface
    only `System.*`/`CanvasWm.Core.*`, and Core references no `CanvasWm.*`
    assembly.

  (Placement)
- **AC7** [mac] Build and change set:
  - `dotnet build CanvasWm.slnx` gives 0 warnings and 0 errors;
  - `dotnet test tests/CanvasWm.Core.Tests` and `dotnet test
    tests/CanvasWm.Engine.Tests` pass;
  - `git status` shows changes only to:
    - `src/CanvasWm.Core/Shortcuts/{Key.cs, HotkeyPlan.cs}`;
    - `tests/CanvasWm.Core.Tests/Shortcuts/`;
    - `src/CanvasWm.Win32/{Hotkeys/, Interop/}`;
    - `tests/CanvasWm.Win32.Tests/{Hotkeys/, WindowEnum/PureHelperTests.cs,
      FrameWriter/PureHelperTests.cs}`;
    - `windows-verification/win32-hotkeys.md`;
    - pipeline files under `orchestration/`;
  - nothing under `src/CanvasWm.Engine`, `src/CanvasWm.App`,
    `tests/CanvasWm.Engine.Tests` or `Directory.Build.props` changes.

  (Placement)
- **AC8** [mac] Source scans. Each prints nothing, run from the repo root:
  - the #10 AC4 and AC5 commands and the #11 AC7 commands, unchanged. This
    includes the numeric-literal scan, so VK values are written in hex;
  - `rg -n "TrellisWM|Licensing" src tests`;
  - `rg -n "WH_KEYBOARD_LL|SetWindowsHookEx|SendInput|keybd_event" src/CanvasWm.Win32`;
  - `rg -n "ShortcutDispatch|ShortcutBindings|ShortcutProfiles|ShortcutProfile\b|ReservedCombos|ShortcutEditor|DispatchState|ShortcutCommand|CommandKind|Desired\(" src/CanvasWm.Win32`
    (no policy in Win32: no tables, profiles, reserved lists, commands or
    recording);
  - `rg -n "class Win32HotkeySource|Win32KeyInput" src/CanvasWm.Engine src/CanvasWm.Core`.

  Also, `rg -c "UnmanagedCallersOnly" src/CanvasWm.Win32/Hotkeys` is ≥ 1, and
  `rg -c "MOD_NOREPEAT" src/CanvasWm.Win32/Hotkeys` is ≥ 1.

  (B6, B11; CLAUDE.md; #10 AC4/AC5; #11 AC7)
- **AC9** [mac] No Engine binder. The existing Engine `ArchitectureSpecTests`
  ("no `IHotkeys`") passes, and `rg -n "HotkeyBinder|IHotkeys"
  src/CanvasWm.Engine` prints nothing. (Scope; #9)

### Windows: pure helpers (automated)

- **AC10** [windows] `KeyMap`:
  - for each of the 86 `Key` members, `ToVk` equals table B20;
  - the 86 VKs are distinct;
  - `TryFromVk(ToVk(k))` gives `k` for all 86;
  - `TryFromVk` is false for 0x60 (`VK_NUMPAD0`), 0x69, 0x0C, 0xE2
    (`VK_OEM_102`), 0x2C (PrintScreen), 0x10, 0x11, 0x12, 0xA0, 0xA5, 0x5B,
    0x5C, 0xE5, 0xE7, 0x00, 0xFF, −1 and 256;
  - `IsLayoutAssigned` is true for exactly the 11 punctuation keys;
  - `DefaultLabel` equals B19 for `A`, `D7`, `F13`, `PageDown`, `Space`,
    `Backslash` (`"\"`), `Quote` (`"'"`) and `Backquote` (`` "`" ``);
  - `ToVk((Key)0)` → `ArgumentOutOfRangeException`.

  (B19, B20; D6)
- **AC11** [windows] `ModMap`:
  - `ToModFlags`: Ctrl → 0x2, Alt → 0x1, Shift → 0x4, Win → 0x8, All →
    0xF, None → 0;
  - for all 16 modifier sets, `FromModFlags(ToModFlags(m)) == m`;
  - `RegisterFlags(CA) == 0x4003` (findings worked example 7);
  - `RegisterFlags(WA) == 0x4009`;
  - `FromModFlags(0x4003) == CA`;
  - `FromModFlags(0xFFF0) == None`;
  - `ToModFlags((Modifiers)16)` → `ArgumentOutOfRangeException`.

  (B21; D2)
- **AC12** [windows] `HotkeyIds`:
  - `For(k(D, CA)) == 67` (worked example 7);
  - `For(k(A, None)) == 16`;
  - `For(k(Backquote, All)) == 1775`;
  - over all 86 × 16 combos the ids are distinct, lie in
    [16, 1775] and are ≤ `MaxApplicationId` (0xBFFF), and `TryDecode(For(c))`
    gives `c`;
  - `TryDecode` is false for −1, 0, 15, 27×16+3 (key 27 undefined) and 1776;
  - `For(default)` → `ArgumentOutOfRangeException`.

  (B22; D2)
- **AC13** [windows] `ReportFor`. Every row of B12 is checked, including:
  - `(T1, true, 5, false)` → `(T1, NotOnLayout, 0)`;
  - `(T1, false, 1409, false)` → `(T1, Taken, 1409)`;
  - `(T1, false, 5, true)` → `(T1, Failed, 5)`;
  - `(T1, false, 0, true)` → `(T1, Failed, 0)`.

  (B12; D3)
- **AC14** [windows] `DecodeHotkey`, with map `{For(T1): T1}`:
  - `(For(T1), LP(0xF, 0x87))` → T1;
  - `(For(T1), LP(0x400F, 0x87))` → T1 (stray bits ignored);
  - `(For(T1), LP(0x7, 0x87))` → null (modifier mismatch);
  - `(For(T1), LP(0xF, 0x86))` → null (VK mismatch);
  - `(−1, …)` and `(−2, …)` → null;
  - `(For(T2), LP(0x7, 0x85))` → null (not registered);
  - `(0xC000, …)` → null.

  (B14; D2)
- **AC15** [windows] Key messages:
  - **(a)** `TranslateKeyMessage`:
    - `(WM_SYSKEYDOWN, 'O', 0, ctrl:false, alt:true, shift:false, win:true)`
      → `k(O, WA)`;
    - `(WM_KEYDOWN, 0xDB, 0, true, true, false, false)` →
      `k(LeftBracket, CA)`;
    - `(WM_KEYDOWN, 0x1B, 0, false, false, false, false)` →
      `k(Escape, None)`;
    - `(WM_KEYDOWN, 0x08, 0, …none)` → `k(Backspace, None)`;
    - `(WM_SYSKEYDOWN, 0x79 /*F10*/, 0, none)` → `k(F10, None)`;
    - `(WM_KEYDOWN, 0x0D, 1 << 24, none)` → `k(Enter, None)` (extended bit
      ignored);
    - `(WM_KEYDOWN, 'O', 1 << 30, …)` → null (repeat);
    - `(WM_KEYUP 0x0101, 'O', …)` → null, and `(WM_CHAR 0x0102, …)` → null;
    - `(WM_KEYDOWN, 0x12 /*VK_MENU*/, …)` → null;
    - `(WM_KEYDOWN, 0x61 /*numpad 1*/, …)` → null;
    - `(WM_KEYDOWN, 0x1_0000_0041, …)` → null (out of range).
  - **(b)** `ReadKeyMessage`, on a `MessageLoopThread`. Save the keyboard
    state with `GetKeyboardState`. `SetKeyboardState` with 0x80 at
    `VK_CONTROL`, `VK_MENU` and `VK_LWIN` (all else 0). Then
    `ReadKeyMessage(WM_SYSKEYDOWN, 'D', 0) == k(D, WCA)`. With 0x80 at
    `VK_RWIN` and `VK_SHIFT` only → `k(D, Win|Shift)`. Restore the saved
    state.

  (B18; D8; findings rule 11)
- **AC16** [windows] `PressQueue`, with `ManualPost` (appends to list `P`;
  runs on request) and a recording `deliver`:
  - **(a)** `Enqueue(T1)`, `Enqueue(T2)` → `P.Count == 1`, nothing
    delivered. Run `P[0]` → delivered `[T1, T2]`.
  - **(b)** A `deliver` that enqueues T3 while delivering T1 → order
    `[T1, T2, T3]` in one drain, with no extra post.
  - **(c)** A `deliver` that runs a nested `Drain` (by running a later
    posted action from inside the handler) → the nested drain delivers
    nothing, and the handlers do not nest.
  - **(d)** A `post` that runs inline → `Enqueue` surfaces
    `InvalidOperationException` from the inline `Drain`. `QueuedCount == 1`,
    nothing delivered.
  - **(e)** A `deliver` that throws on T1, with T2 queued → the exception
    propagates from `Drain`, a new drain is posted first, and running it
    delivers T2 only.
  - **(f)** `Close()` with T1 queued, then run the posted drain → nothing
    delivered. A later `Enqueue` posts nothing and `QueuedCount == 0`.

  (B15)
- **AC17** [windows] Public surface. Reflection over `CanvasWm.Win32`:
  - the exported types are exactly the seven of #11 plus
    `CanvasWm.Win32.Hotkeys.Win32HotkeySource` and
    `CanvasWm.Win32.Hotkeys.Win32KeyInput` (nine);
  - `Win32HotkeySource` implements `IDisposable` and no Engine interface;
  - its public members are exactly the constructor, `Pressed`, `Apply` and
    `Dispose`;
  - `Win32KeyInput`'s public members are exactly `ReadKeyMessage` and
    `KeyLabel(Key)`.

  The #10 AC15 and #11 AC12 tests pass with their expected lists amended
  (R2). (Placement; Public API)

### Windows: real registrations (automated)

Rig: the source is built on a `MessageLoopThread` `L` via `L.Invoke(() =>
new Win32HotkeySource(L.Post))`. Every `Apply` and `Dispose` goes through
`L.Invoke`. A "holder" is a second source `H` on a second loop. "Free" means
`H.Apply([c])` reports Registered (afterwards `H.Apply([])`). "Held" means
`H.Apply([c])` reports `Taken` with 1409. Before the tests, `H.Apply([T1,
T2, T3])` must report all Registered; otherwise the test fails with the
message "test combo owned by another program".

- **AC18** [windows] Construction:
  - `WindowHandle != 0`;
  - `FindWindowExW(HWND_MESSAGE, 0, "CanvasWm.HotkeyWindow", null)` finds a
    window and `IsWindow(WindowHandle)` is true;
  - a second source on the same loop constructs fine (class already
    registered);
  - `new Win32HotkeySource(null!)` → `ArgumentNullException`.

  (B7; D1)
- **AC19** [windows] Register and unregister:
  - `Apply([T1, T2])` → `[(T1, Registered, 0), (T2, Registered, 0)]`, and
    both are held;
  - `Apply([T2])` → `[(T2, Registered, 0)]`. T1 is free, T2 still held, and
    `RegisteredCombos == {T2}`;
  - `Apply([])` → empty, and T1 and T2 are free.

  (B10; D4)
- **AC20** [windows] Taken and retry:
  - while `H` holds T1, `Apply([T1, T2])` → `[(T1, Taken, 1409), (T2,
    Registered, 0)]`, and `RegisteredCombos == {T2}`;
  - after `H.Apply([])`, `Apply([T1, T2])` → both Registered, and T1 is held
    by the source.

  (B10, B12, E4; D3, findings worked example 2)
- **AC21** [windows] Apply errors. Start from `Apply([T2])`. Each call below
  throws and leaves `RegisteredCombos == {T2}` and T1 free:
  - `Apply([T1])` called from the test thread →
    `InvalidOperationException`;
  - `Apply(null!)` → `ArgumentNullException`;
  - `Apply([T1, default])` → `ArgumentOutOfRangeException`;
  - `Apply([T1, k(F24, Shift)])` → `ArgumentException`.

  Then `Apply([k(Escape, Ctrl|Shift|Alt)])` is accepted (any status may be
  reported), followed by `Apply([])`. Finally, after `Dispose`,
  `Apply([T1])` → `ObjectDisposedException`. (B9)
- **AC22** [windows] Duplicates and order:
  - `Apply([T2, T1, T2])` → reports `[T2, T1]`, and both are registered;
  - a second `Apply([T1, T2])` → reports `[(T1, Registered, 0), (T2,
    Registered, 0)]` (kept combos, no OS call), and both remain held.

  (B10, E2)
- **AC23** [windows] Posted `WM_HOTKEY`. Use a source whose `post` is a
  `ManualPost` that is drained on `L`. Subscribe `Pressed` to record
  combos. `Apply([T1])`, then `PostMessageW(WindowHandle, WM_HOTKEY,
  For(T1), LP(0xF, 0x87))`, and let `L` pump for 200 ms:
  - `Pressed` has not been raised, and `ManualPost` holds exactly 1 action;
  - running it on `L` → `Pressed` received `[T1]` on `L`'s managed thread;
  - posting `(For(T1), LP(0x7, 0x87))`, `(−1, 0)` and `(For(T2), LP(0x7,
    0x85))` (T2 not registered) → no action is posted;
  - after `Apply([])`, posting the T1 message again → nothing is posted
    (E5).

  (B8, B13–B15; D1)
- **AC24** [windows] Real key input with `MOD_NOREPEAT`, using the default
  rig (`post = L.Post`). `Apply([T2])` and subscribe `Pressed`. Then
  `SendInput`:
  - Ctrl, Alt and Shift down;
  - F22 down, then 10 more F22 down events at 30 ms spacing;
  - F22 up, then Shift, Alt and Ctrl up.

  Within 2 s, `Pressed` is raised exactly once, with T2. Afterwards,
  `Apply([])`. (B10, B13, B14 against a real lParam; D5, findings worked
  example 6)
- **AC25** [windows] Dispose:
  - **(a)** After `Apply([T1, T2])`, `Dispose` on `L`: T1 and T2 are free,
    `IsWindow(old handle)` is false, `WindowHandle == 0`, and a second
    `Dispose` does nothing.
  - **(b)** With a `ManualPost` source: `Apply([T1])`, post the T1
    `WM_HOTKEY`, and let `L` pump so the drain is posted. Then `Dispose`
    from the test thread. T1 stays held until the posted cleanup runs on
    `L`, and is free afterwards. Running the earlier posted drain raises no
    `Pressed`.

  (B16; D4)
- **AC26** [windows] Labels. Find a US layout in `GetKeyboardLayoutList`
  (low word 0x0409, high word 0x0409). If there is none, the test writes
  "US layout not installed" and returns. With it:
  - `KeyLabel(k, hkl)` equals `DefaultLabel(k)` for all 86 keys;
  - `IsOnLayout(k, hkl)` is true for all 86.

  `KeyLabel(key)` on the current layout returns a non-empty string for all
  86. (B19)

### Windows: manual checklist

All probe tools are `[Fact]`s in `HotkeyProbeManual`. They write a TSV to
`%TEMP%\canvaswm-hotkeys-<CANVASWM_LABEL or "run">.tsv` with these columns:

`Test, Group, Combo, RegisterResult (ok | taken | error:<code>), Status, Fired (yes | no | skipped), Ms, ForegroundBefore, ForegroundAfter, Note`

`ForegroundBefore` and `ForegroundAfter` hold the class name of the
foreground window just before the prompt and 1 s after the press window
ends. This is a hint that Windows acted (Start, Game Bar or a jump list took
the foreground).

Probe groups:

| Group | Combos |
|---|---|
| `nc` | the 32 `ReservedCombos.All` rows with `NeedsConfirmation` (B13 #53–84) |
| `defaults` | `HotkeyPlan.Desired(Build(ShortcutProfiles.WinAlt), false)`: the 12 actions + Win+Alt+Shift+1–9 + Win+Ctrl+Alt+1–9 |
| `controls` | Win+L (**register only, never prompted**), Win+D, Win+Left, Ctrl+Shift+Escape |
| `candidates` | Ctrl+Alt+Shift+Win+{W,T,Y,O,P,D,L,X,N,Space}; Win+Shift+F23; Win+Shift+S; Win+Ctrl+Shift+B; Win+Ctrl+Enter; Win+Ctrl+O; Win+Ctrl+C (16) |

- **AC27** [windows-manual] Probe off by default. With `CANVASWM_MANUAL`
  unset, `dotnet test tests/CanvasWm.Win32.Tests` runs every
  `HotkeyProbeManual` test as an immediate pass, writes no TSV and registers
  nothing.
- **AC28** [windows-manual] Reserved-combo probe. With
  `CANVASWM_MANUAL=1`:
  1. `RegisterAll` builds one source and calls `Apply` with all four groups
     (distinct). It writes one row per combo with `Fired = skipped`, then
     disposes.
  2. `PressEach` (groups chosen by `CANVASWM_HOTKEY_GROUPS`, default
     `nc,defaults,controls,candidates`) registers the group. For each combo
     whose result is ok, except Win+L, it prints "Press <combo> now (6 s; do
     nothing to skip)". It records whether `Pressed` arrived (`Fired`,
     `Ms`) and the foreground classes.
  3. With Notepad focused before each prompt, the human notes per combo, in
     the checklist, one of:
     - **fails** (taken / error);
     - **succeeds-never-fires**;
     - **succeeds-and-Windows-also-acts**;
     - **works cleanly**.

  Runs required:
  - Windows 11 with Game Bar on, and with Game Bar off;
  - Windows 10 with Game Bar on, and with Game Bar off;
  - for the `nc` Ctrl+Alt rows, once with Magnifier closed and once open.

  Each run gets its own `CANVASWM_LABEL`. Pass condition: the TSVs and notes
  exist for every run. The results feed the separate `ReservedCombos` change
  (A12). (D3, D9; shortcut-model "Notes for #12")
- **AC29** [windows-manual] WinAlt default profile. From the AC28 runs on a
  clean Windows 11 (and on Windows 10):
  - all 30 `defaults` combos report `ok` and `Fired = yes`;
  - Windows takes no visible action for them.

  Here, "30" means the 12 action combos (Win+Alt+O, Minus, Equals, Left,
  Right, Shift+Up, Shift+Down, [, ], F, W, N) plus the 18 view combos. Any
  failure is written up for a new human decision; the spec is not changed
  silently. (findings worked example 1; shortcut-model A1/A2)
- **AC30** [windows-manual] Alt/Win side effects, observation only. On
  Windows 10 and on Windows 11, with Notepad (and a classic Win32 app with
  a menu bar) focused:
  - press Win+Alt+O while `PressEach` (`defaults`) runs;
  - record whether the app's menu bar gets focus or the access-key
    underlines appear after release;
  - record whether the Start menu opens when Win is released;
  - repeat with Win+Alt+Left.

  No mitigation is built. If either effect occurs, raise it for a human
  decision (#11 A10 Win+Alt `SendInput` risk). (D5; scope)
- **AC31** [windows-manual] Hold and repeat with a real keyboard. Hold
  Win+Alt+Right for 2 s during `PressEach` (`defaults`). The TSV shows
  exactly one press for that prompt. (D5; findings worked example 6)
- **AC32** [windows-manual] Non-US layouts. The `LayoutLabels` probe writes,
  for the current layout, `KeyLabel` and `IsOnLayout` for the 11
  punctuation keys and for D1. Then run `PressEach` with
  `CANVASWM_HOTKEY_GROUPS=defaults`:
  - **German (QWERTZ):**
    - the LeftBracket label is `"ß"` (B19);
    - Back's combo `k(LeftBracket, WA)` fires when Win+Alt+ß is pressed;
    - Win+Alt+O fires from the key labelled O.
  - **French (AZERTY):**
    - Win+Alt+Shift + the top-row key labelled 1/& fires `k(D1, WA+S)`;
    - Win+Alt+O fires from the key labelled O.
  - Record any punctuation key whose `IsOnLayout` is false.

  (D6 option a, findings worked example 8; A1, A3)
- **AC33** [windows-manual] Elevated foreground. Start an elevated Notepad,
  focus it, and run `PressEach` (`defaults`) from a non-elevated terminal.
  Record whether Win+Alt+O fires. (Expected yes; findings Unknown 5)
- **AC34** [windows-manual] Recorder probe. `RecorderProbe` opens a visible
  test window. Its window procedure passes `WM_KEYDOWN`/`WM_SYSKEYDOWN` to
  `ReadKeyMessage` and logs each non-null combo. It swallows those messages
  and `WM_SYSCHAR`. With the window focused, press each of these and check
  the logged combo:
  - Win+Alt+Shift+Up → `k(Up, WA+S)`;
  - Ctrl+Alt+[ → `k(LeftBracket, CA)`;
  - Escape → `k(Escape, None)`;
  - F10 → `k(F10, None)`;
  - numpad 1 with NumLock on → nothing;
  - on a layout with AltGr, AltGr+Q → `k(Q, CA)`.

  Then record what happens with Win+D (expected: never reaches the window)
  and whether releasing Win after Win+Alt+Shift+Up opens Start. No menu mode
  is entered after Alt combos. (B17, B18; D8)

## Assumptions

- **A1 (human decision taken: VK identity).** The punctuation keys register
  their fixed `VK_OEM_*` code (B20). There is no re-registration on a layout
  change. The physical-position option (D6 b) is deferred, because
  `WM_INPUTLANGCHANGE` never reaches a message-only window and per-app input
  methods make one global VK ambiguous.
- **A2 (needs review: label source).** Labels come from
  `MapVirtualKeyExW(VK_TO_CHAR)`, and only for the 11 punctuation keys. All
  other keys get fixed English names (B19). Reasons: the call is stateless,
  so it cannot disturb a pending dead key as `ToUnicodeEx` can, and it gives
  the unshifted base character. Localised names such as "Bild auf" for
  PageUp are left to the settings UI. Letters keep Latin labels even on
  Cyrillic or Greek layouts, because their VKs are identity.
- **A3 (needs review: NotOnLayout scope and timing).** `NotOnLayout` is
  checked only for the 11 punctuation keys. It uses the owner thread's
  layout (`GetKeyboardLayout(0)`) once per `Apply`. It is not re-evaluated
  when the layout changes until the next `Apply`. With "per-app input
  method" off (the Windows default), every thread shares the layout.
- **A4 (strict WM_HOTKEY check).** A `WM_HOTKEY` whose lParam modifiers or
  VK disagree with the registered combo is dropped (B14). The check uses
  the four modifier bits only. AC24 checks a real lParam on Windows. If that
  check ever drops real presses, relax it to "id only".
- **A5 (needs human decision: retry policy).** `Apply` re-attempts every
  desired-but-unregistered combo, so a `Taken` combo is retried whenever the
  App calls `Apply`. Recommended for #29: on bindings change, on recording
  stop, when the settings window opens, and on an explicit "Retry" button.
  There is **no** periodic retry: Win32 has no timers (AC8). Whether to
  notify the user when a combo becomes Taken is a settings-UI decision.
- **A6 (recorder details).**
  - Auto-repeat key messages are ignored, so a key held from before
    recording started must be pressed again.
  - The extended-key bit is ignored. Numpad Enter records as Enter, and
    numpad navigation keys with NumLock off record as the dedicated keys.
    This matches what `RegisterHotKey` will fire on (E8).
- **A7 (bare-key guard in Win32).** `Apply` rejects combos without
  Ctrl/Alt/Win. The rule comes from Core (`HasRequiredModifier`); it is a
  call-contract check, not a policy decision. Escape combos with a modifier
  pass at this level for the probe. `HotkeyPlan.Desired` removes every
  unbindable combo before the App calls `Apply`.
- **A8 (human decision taken: side effects).** Alt/Win side effects are only
  observed (AC30). No `SendInput` masking is built.
- **A9 (delivery).** `Pressed` is a .NET event raised through `post` with a
  non-nesting FIFO that copies #10 `EventPump` semantics. A new class
  (`PressQueue`) is used instead of generalising `EventPump`, so no #10 code
  is refactored. Handler exceptions propagate to the message loop, as in #10
  B17.
- **A10 (owner thread).** The owner thread is the engine thread that owns
  `Win32WindowSource` (#10 A13). It must pump messages; this cannot be
  checked. More than one instance is allowed. The window class is
  registered once per process and never unregistered.
- **A11 (Dispose off-thread).** Cleanup is posted. If the loop is gone, the
  OS frees the registrations at process exit.
- **A12 (human decision taken: ReservedCombos unchanged).** This feature
  changes no `ReservedCombos` data. The probe results (AC28) feed a
  separate, small Core change once the human has decided which candidate
  rows and which NC confirmations to apply.
- **A13 (no logging).** Win32 has no logger. Dropped or mismatched
  `WM_HOTKEY` messages, failed `UnregisterHotKey` calls and exceptions
  inside the window procedure are discarded silently.
- **A14 (no Engine binder; human decision taken).** Press → command →
  engine routing lives in the App (#29). So it is not Mac-testable beyond
  `Match` and `HotkeyPlan`, which are.
- **A15 (studied deviation).** The studied app consumes a key-up during
  recording when its key-down was consumed earlier (findings rule 9).
  `RegisterHotKey` reports no key-ups, so this has no counterpart here.

## Notes for #16 (overview key capture)

- Any `WH_KEYBOARD_LL` hook installed by #16 runs **before** hotkey
  processing. If it swallows a key, a registered hotkey on that key cannot
  fire. The hook must therefore pass through every key event while Ctrl, Alt
  or Win is held (the studied capture path likewise ignores Command and
  Ctrl+Opt chords), or call `ShortcutDispatch.Match` itself. Install it only
  while the overview or a drag is active (D7).

## Required edits to earlier specs (reported, not made here)

- **R1 `specs/shortcut-model.md`.**
  - In the Public API, the `Key` comment "US-layout punctuation positions;
    the character printed may differ on other layouts" should read "The
    punctuation keys that type these characters on the US layout,
    identified by key code, not position (#12 B1)".
  - In "Notes for #12", "Map the punctuation keys to `VK_OEM_*` by US
    position" should read "… to the `VK_OEM_*` codes of table #12 B20 (VK
    identity)".
  - No behaviour changes. The code-side doc fix is part of this feature
    (B1).
- **R2 `specs/win32-window-enum.md` AC15 and `specs/win32-frame-writer.md`
  AC12.**
  - The exact exported-type lists gain
    `CanvasWm.Win32.Hotkeys.Win32HotkeySource` and
    `CanvasWm.Win32.Hotkeys.Win32KeyInput`.
  - The two test files are updated in this feature (Placement). The spec
    texts need the matching amendment note.
- **R3 `specs/win32-window-enum.md` B1.** Its entry-point list is scoped to
  fact gathering and events. This feature adds hotkey entry points (B6,
  Placement) under the same style rules. No change to #10 behaviour.

## Traceability

| Spec item | Source |
|---|---|
| B1, AC5, R1 | D6 option a ("Flag for Core"); human scope |
| B2, B3, AC1 | Part 1 rules 5, 6, 7; D4 desired set; D8; shortcut-model B7–B9, A11 |
| B4, AC2, AC3 | D4 diff (unregister first; kept combos need no call); worked example 3 |
| B5, B12, AC4, AC13 | D3 per-combo status (Registered / Taken 1409 / Failed / NotOnLayout); data model |
| B6, AC8 | D1 interop style; #10 B1, #11 B21; D7 (no LL hook); D5 (no SendInput) |
| B7, B8, AC18 | D1 message-only window, owner thread, UnmanagedCallersOnly, modal-loop reason |
| B9, A7, AC21 | D1 owner-thread rule; shortcut-model A11 (never a bare global key) |
| B10, B11, AC19, AC20, AC22 | D2 call shape + MOD_NOREPEAT; D3 per-combo, never per batch; D4 |
| B13, B15, AC16, AC23, A9 | D1 "post, never inline"; #10 B17 EventPump |
| B14, AC14, A4 | D2 WM_HOTKEY layout, system ids ignored, mismatch ignored |
| B16, AC25, A11 | D4 shutdown |
| B17, B18, AC15, AC34, A6 | D8 recorder; Part 1 rules 12–14; rule 11 (no L/R) |
| B19, AC26, AC32, A2, A3 | D6 option a (labels, NotOnLayout) |
| B20, AC10 | D6 table (86 rows, bijective) |
| B21, AC11 | D2 modifier map + pitfall |
| B22, AC12 | D2 id allocation |
| B23 | Recommended split (App #29); Part 1 rule 7; D4 (keep registered while suspended / overview) |
| AC24, AC31 | D5 (no repeat with MOD_NOREPEAT); worked example 6 |
| AC28, AC29, A12 | D9 probe; shortcut-model Notes for #12; Unknown 1 |
| AC30, A8 | D5 modifier side effects; Unknown 2 |
| AC33 | D7 elevation row; Unknown 5 |
| AC9, A14 | Recommended split; #9 "no IHotkeys" |
| Notes for #16 | D7 |
| A15 | Part 1 rule 9 |
| D10 (foreground rights) | No item needed; #11 AC37 already passed |
