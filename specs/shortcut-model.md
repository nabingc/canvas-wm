# Spec: shortcut-model

## Goal

A pure Core model of CanvasWM's keyboard shortcuts:

1. **Catalogue.** 12 fixed actions, in a fixed order. Each has a stable id,
   our own short description, the engine command it stands for (a Core enum
   value), which engine it targets, and "repeats on hold = never".
2. **Saved views.** 9 slots on the top-row digits 1–9. Two shared modifier
   sets: one recalls a view, one saves it. Either set may be unbound. There is
   no binding for forgetting a view.
3. **Binding model.** Per action: one binding or unbound. A binding is a key
   from our own platform-neutral `Key` enum plus an exact modifier set (Ctrl,
   Alt, Shift, Win). There are no chords, no key-up bindings and no gestures.
4. **Matching.** Exact key and exact modifiers. The first match in list order
   wins. The matched key-down and its key-up are consumed. Auto-repeat is
   swallowed. Nothing matches while the settings UI is recording a shortcut.
   This is modelled as pure functions over an immutable state that the
   Win32/App layer drives.
5. **Editing rules** as values, with no UI: the required modifier,
   unbind/cancel keys, moving a combo from another action, conflicts with the
   view sets, and recall ≠ save.
6. **Persistence** as pure format/parse to a string dictionary. Missing =
   default, `none` = unbound, malformed = unbound. There is no file I/O.
7. **Default profiles.** **Win+Alt** is the default (human decision). There is
   also an alternative **Ctrl+Alt** profile. A documented list of combos that
   Windows reserves is included, and the defaults are checked against it.

Out of scope, each with its owner:
- #12 win32-hotkeys: `RegisterHotKey`, mapping `Key` to a Windows virtual-key
  code, `MOD_NOREPEAT`, and "unavailable" reporting.
- #15: what a saved view contains.
- #29: engine wiring, meaning which engine is called and the pointer and
  file-manager arguments.
- The settings UI: beeps, hints, buttons, and storing which profile was
  chosen.
- Findings rules 24–26 (the Escape interceptor, key capture while the
  overview is open, the scroll-zoom modifier) belong to #16 and #15.
- Findings rule 27 (showing combos in menus) belongs to the settings UI.

## Placement

- **Project:** `src/CanvasWm.Core` (`net10.0`, BCL only). There is no new
  project or package reference, no I/O and no OS access.
- **Namespace:** `CanvasWm.Core.Shortcuts`. This is new; no other namespace is
  touched.
- **Suggested files:** `src/CanvasWm.Core/Shortcuts/{Key.cs, Modifiers.cs,
  KeyCombo.cs, ShortcutRules.cs, ShortcutAction.cs, ShortcutCommand.cs,
  ShortcutCatalog.cs, ShortcutBindings.cs, ShortcutProfiles.cs,
  ReservedCombos.cs, ShortcutDispatch.cs, ShortcutEditor.cs,
  ShortcutStore.cs}`.
- **Tests:** `tests/CanvasWm.Core.Tests/ShortcutTests/*Tests.cs`, in namespace
  `CanvasWm.Core.Tests.ShortcutTests`.
- **Reuses, unchanged:** `CanvasWm.Core.Layout.Edge` (`Left = 0`, `Right = 1`,
  `Top = 2`, `Bottom = 3`). **No existing type is changed.**
- **Must not reference** `CanvasWm.Engine` or `CanvasWm.Win32`. Engine command
  names below are documentation for #29 only.
- **Naming rule:** no text in `src/` or `tests/` may contain the studied app's
  name or the word for its licence module. Our ids, descriptions and key names
  are our own wording.

## Public API

All public members carry XML docs. A null reference argument throws
`ArgumentNullException`. An argument of an enum type whose value is not a
defined member (for `Modifiers`: any bit outside `Modifiers.All`) throws
`ArgumentOutOfRangeException`. Both checks happen before any work is done.

### Keys and modifiers

```csharp
namespace CanvasWm.Core.Shortcuts;

/// <summary>A physical key, platform-neutral. Raw values are fixed (persisted by NAME, see
/// ShortcutStore; #12 maps each member to a Windows virtual-key code). There is no member 0.
/// Modifier keys are not members: they are represented only by Modifiers.</summary>
public enum Key
{
    A = 1, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z, // 1..26
    /// <summary>Top-row digits (not the numeric keypad).</summary>
    D0 = 30, D1, D2, D3, D4, D5, D6, D7, D8, D9,                                         // 30..39
    F1 = 40, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,                          // 40..63
    Left = 70, Right = 71, Up = 72, Down = 73,
    Space = 80, Enter = 81, Tab = 82,
    /// <summary>Deletes backwards (the "Delete" role in the editing rules).</summary>
    Backspace = 83,
    /// <summary>Deletes forwards (the "Forward-Delete" role).</summary>
    Delete = 84,
    Escape = 85, Insert = 86, Home = 87, End = 88, PageUp = 89, PageDown = 90,
    /// <summary>The punctuation keys that type these characters on the US layout, identified by key code, not position (#12 B1).</summary>
    Minus = 100, Equals = 101, LeftBracket = 102, RightBracket = 103, Backslash = 104,
    Semicolon = 105, Quote = 106, Comma = 107, Period = 108, Slash = 109, Backquote = 110,
}
// 86 members in total.

/// <summary>An exact modifier set. Left/right variants, Caps Lock and Fn are not represented.
/// Mapping from the studied platform: Control → Ctrl, Option → Alt, Command → Win.</summary>
[Flags]
public enum Modifiers
{
    None = 0, Ctrl = 1, Alt = 2, Shift = 4, Win = 8,
    /// <summary>Ctrl | Alt | Shift | Win (15).</summary>
    All = 15,
}

/// <summary>A key plus an exact modifier set. Equality is exact on both.</summary>
public readonly record struct KeyCombo
{
    /// <exception cref="ArgumentOutOfRangeException">key undefined, or modifiers outside All.</exception>
    public KeyCombo(Key key, Modifiers modifiers);
    public Key Key { get; }
    public Modifiers Modifiers { get; }
}
// default(KeyCombo) has Key 0, which is invalid. Every function below that takes a KeyCombo
// throws ArgumentOutOfRangeException for it.

/// <summary>Pure rule helpers.</summary>
public static class ShortcutRules
{
    /// <summary>Ctrl | Alt | Win: a binding needs at least one of these (B12).</summary>
    public const Modifiers Required = Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win;
    /// <summary>True iff (modifiers &amp; Required) != 0.</summary>
    public static bool HasRequiredModifier(Modifiers modifiers);
    /// <summary>True iff the combo may be stored as a binding: HasRequiredModifier and key != Escape.</summary>
    public static bool IsBindable(KeyCombo combo);
    /// <summary>Slot 1..9 for D1..D9; null for every other key (including D0).</summary>
    public static int? SlotOf(Key key);
    /// <summary>D1..D9 for slot 1..9.</summary>
    /// <exception cref="ArgumentOutOfRangeException">slot outside 1..9.</exception>
    public static Key SlotKey(int slot);
    /// <summary>True iff the combo is Ctrl+Alt without Win (Shift may be present) and the key types a
    /// character (letters, digits, punctuation, Space). On layouts with AltGr such a global combo can
    /// take over a typed character. A settings warning only; never a refusal.</summary>
    public static bool MayClashWithAltGr(KeyCombo combo);
}
```

### Actions and commands

```csharp
/// <summary>The 12 bindable actions. Raw values are fixed; this order is also the match order.</summary>
public enum ShortcutAction
{
    Overview = 0, StepOut = 1, StepIn = 2, Left = 3, Right = 4, Up = 5, Down = 6,
    Back = 7, Forward = 8, Maximize = 9, Desktop = 10, Split = 11,
}

/// <summary>What the app layer executes. The #29 mapping to DisplayEngine is in B3.</summary>
public enum CommandKind
{
    ToggleOverview = 0, ZoomOut = 1, ZoomIn = 2, Move = 3, Back = 4, Forward = 5,
    ToggleMaximize = 6, ToggleDesktopHidden = 7, SplitForNewWindow = 8,
    GoToView = 9, SaveView = 10,
}

/// <summary>Which engine a command goes to (#29 resolves it).</summary>
public enum TargetEngine
{
    /// <summary>The engine of the active (display, desktop).</summary>
    Active = 0,
    /// <summary>The engine under the pointer when the step-toward-pointer preference is on and such
    /// an engine exists, else the active engine (StepIn only, B4).</summary>
    PointerThenActive = 1,
    /// <summary>The engine under the pointer only; none → nothing (Split only).</summary>
    Pointer = 2,
}

/// <summary>One command produced by a match. Create only through the factories.</summary>
public readonly record struct ShortcutCommand
{
    public CommandKind Kind { get; }
    /// <summary>The action, for the 12 action commands; null for view commands.</summary>
    public ShortcutAction? Action { get; }
    /// <summary>Direction for Kind == Move; otherwise null.</summary>
    public Edge? Toward { get; }
    /// <summary>1..9 for GoToView / SaveView; otherwise null.</summary>
    public int? Slot { get; }

    public static ShortcutCommand ForAction(ShortcutAction action);
    /// <exception cref="ArgumentOutOfRangeException">slot outside 1..9.</exception>
    public static ShortcutCommand GoToView(int slot);
    /// <exception cref="ArgumentOutOfRangeException">slot outside 1..9.</exception>
    public static ShortcutCommand SaveView(int slot);
}

/// <summary>Static description of one action.</summary>
public sealed record ShortcutActionInfo(
    ShortcutAction Action, string Id, string Description,
    ShortcutCommand Command, TargetEngine Target, bool RepeatsOnHold);

/// <summary>The two saved-view modifier sets.</summary>
public enum ViewSet { Recall = 0, Save = 1 }

public static class ShortcutCatalog
{
    /// <summary>The 12 actions in ShortcutAction order (B1).</summary>
    public static IReadOnlyList<ShortcutActionInfo> Actions { get; }
    public static ShortcutActionInfo Info(ShortcutAction action);
    /// <summary>Exact, ordinal id lookup.</summary>
    public static bool TryParseId(string id, out ShortcutAction action);
    /// <summary>"Go to saved view n" / "Save the current view as view n" (B2).</summary>
    /// <exception cref="ArgumentOutOfRangeException">slot outside 1..9.</exception>
    public static string DescribeView(ViewSet set, int slot);
    /// <summary>9.</summary>
    public const int ViewSlots = 9;
}
```

### Bindings and profiles

```csharp
/// <summary>The full shortcut configuration. Immutable; value equality over all 12 action entries and
/// both view sets. Invariant: every stored combo is ShortcutRules.IsBindable, and every stored view
/// set has the required modifier. Uniqueness is NOT an invariant (Parse may produce duplicates; the
/// dispatcher resolves them by order, B8).</summary>
public sealed record ShortcutBindings
{
    /// <summary>All 12 actions unbound, both view sets unbound.</summary>
    public static ShortcutBindings Unbound { get; }

    /// <summary>The action's combo, or null when unbound.</summary>
    public KeyCombo? this[ShortcutAction action] { get; }
    /// <summary>A copy with that action set (null = unbound). Applies no editing rule.</summary>
    /// <exception cref="ArgumentException">combo non-null and not IsBindable.</exception>
    public ShortcutBindings WithAction(ShortcutAction action, KeyCombo? combo);

    /// <summary>Modifiers for "go to view n" (digit n); null = no recall bindings.</summary>
    /// <exception cref="ArgumentException">init with a value lacking the required modifier.</exception>
    public Modifiers? RecallViewModifiers { get; init; }
    /// <summary>Modifiers for "save view n"; null = no save bindings.</summary>
    /// <exception cref="ArgumentException">as above.</exception>
    public Modifiers? SaveViewModifiers { get; init; }
    public Modifiers? ViewModifiers(ViewSet set);
    /// <summary>A copy with that set changed. Applies no editing rule.</summary>
    /// <exception cref="ArgumentException">non-null value lacking the required modifier.</exception>
    public ShortcutBindings WithViewModifiers(ViewSet set, Modifiers? modifiers);

    /// <summary>First action (ShortcutAction order) bound to exactly this combo, or null.</summary>
    public ShortcutAction? ActionBoundTo(KeyCombo combo);

    public bool Equals(ShortcutBindings? other);
    public override int GetHashCode();
}

public enum ShortcutProfile { WinAlt = 0, CtrlAlt = 1 }

public static class ShortcutProfiles
{
    /// <summary>Default profile (human decision): Win+Alt. Equals WinAlt.</summary>
    public static ShortcutBindings Default { get; }
    /// <summary>Table B10.</summary>
    public static ShortcutBindings WinAlt { get; }
    /// <summary>Table B11 (mirrors the studied layout; not AltGr-safe).</summary>
    public static ShortcutBindings CtrlAlt { get; }
    public static ShortcutBindings Get(ShortcutProfile profile);
    /// <summary>"Are defaults": bindings == Get(profile).</summary>
    public static bool AreDefaults(ShortcutBindings bindings, ShortcutProfile profile);
}

public enum ReservedKind
{
    /// <summary>Windows (or a Windows component that is on by default) always owns it.</summary>
    Blocked = 0,
    /// <summary>Owned only while a Windows feature is open or running (Magnifier, voice typing,
    /// taskbar focus) or by some graphics drivers.</summary>
    Conditional = 1,
}

/// <param name="Owner">Our own short note on what Windows does with it.</param>
/// <param name="NeedsConfirmation">True when the fact is believed but not yet checked on a Windows PC.</param>
public sealed record ReservedCombo(KeyCombo Combo, ReservedKind Kind, string Owner, bool NeedsConfirmation);

/// <summary>Combos the defaults must avoid (table B13). Not exhaustive; data only.</summary>
public static class ReservedCombos
{
    /// <summary>Table B13 in table order; combos are unique.</summary>
    public static IReadOnlyList<ReservedCombo> All { get; }
    /// <summary>The entry for this exact combo, or null.</summary>
    public static ReservedCombo? Find(KeyCombo combo);
}
```

### Matching

```csharp
/// <summary>One entry of the dispatch list.</summary>
public readonly record struct DispatchEntry(KeyCombo Combo, ShortcutCommand Command);

public enum KeyVerdict { PassThrough = 0, Consume = 1 }

/// <param name="Command">The command to run, or null (nothing to run, even when consumed).</param>
public readonly record struct KeyDownOutcome(KeyVerdict Verdict, ShortcutCommand? Command);

/// <summary>Runtime state of the dispatcher. Immutable; transitions return new instances.</summary>
public sealed record DispatchState
{
    /// <summary>Not recording, nothing held.</summary>
    public static DispatchState Initial { get; }
    /// <summary>True while the settings UI records a shortcut (matching suspended).</summary>
    public bool Recording { get; }
    /// <summary>Keys whose key-down was consumed and whose key-up has not arrived yet.</summary>
    public IReadOnlySet<Key> Held { get; }
    public DispatchState WithRecording(bool recording);
}

public static class ShortcutDispatch
{
    /// <summary>Ordered dispatch list (B7): the bound actions, then for slot 1..9: recall, save.</summary>
    public static IReadOnlyList<DispatchEntry> Build(ShortcutBindings bindings);
    /// <summary>Command of the first entry whose combo equals pressed exactly, or null.</summary>
    public static ShortcutCommand? Match(IReadOnlyList<DispatchEntry> table, KeyCombo pressed);
    /// <summary>B14–B16, B19.</summary>
    public static (DispatchState State, KeyDownOutcome Outcome) KeyDown(
        DispatchState state, IReadOnlyList<DispatchEntry> table, Key key, Modifiers modifiers, bool isRepeat);
    /// <summary>B17–B19.</summary>
    public static (DispatchState State, KeyVerdict Verdict) KeyUp(DispatchState state, Key key);
}
```

### Editing

```csharp
public enum EditOutcome
{
    /// <summary>Combo or set stored (possibly equal to before).</summary>
    Stored = 0,
    /// <summary>Stored; OtherAction lost this combo and is now unbound.</summary>
    Moved = 1,
    /// <summary>The action or set is now unbound.</summary>
    Cleared = 2,
    /// <summary>Recording cancelled (Escape); nothing changed.</summary>
    Cancelled = 3,
    /// <summary>Refused: no Ctrl, Alt or Win.</summary>
    NeedsModifier = 4,
    /// <summary>Refused: key cannot be bound (Escape).</summary>
    NotBindable = 5,
    /// <summary>Refused: a view set must be recorded with a digit 1–9.</summary>
    NotADigit = 6,
    /// <summary>Refused: the action combo is digit 1–9 with a view set's modifiers (OtherViews says which).</summary>
    ConflictsWithViews = 7,
    /// <summary>Refused: OtherAction is bound to a digit 1–9 with these modifiers.</summary>
    ConflictsWithAction = 8,
    /// <summary>Refused: the set would equal the other view set (OtherViews).</summary>
    RecallEqualsSave = 9,
}

/// <param name="Bindings">The result; the input instance's value when refused or cancelled.</param>
/// <param name="OtherAction">Moved: the action that lost the combo. ConflictsWithAction: the blocking action.
/// Otherwise null.</param>
/// <param name="OtherViews">ConflictsWithViews / RecallEqualsSave: the other set. Otherwise null.</param>
public sealed record EditResult(EditOutcome Outcome, ShortcutBindings Bindings,
                                ShortcutAction? OtherAction, ViewSet? OtherViews)
{
    /// <summary>Outcome is Stored, Moved or Cleared.</summary>
    public bool Accepted { get; }
}

public static class ShortcutEditor
{
    /// <summary>A key press recorded while editing an action (B21).</summary>
    public static EditResult RecordForAction(ShortcutBindings bindings, ShortcutAction action, Key key, Modifiers modifiers);
    /// <summary>A key press recorded while editing a view set (B24).</summary>
    public static EditResult RecordForViews(ShortcutBindings bindings, ViewSet set, Key key, Modifiers modifiers);
    /// <summary>Set an action's combo with rules B22.</summary>
    public static EditResult SetAction(ShortcutBindings bindings, ShortcutAction action, KeyCombo combo);
    /// <summary>Set a view set with rules B25.</summary>
    public static EditResult SetViewModifiers(ShortcutBindings bindings, ViewSet set, Modifiers modifiers);
    /// <summary>Unbind an action → Cleared.</summary>
    public static EditResult ClearAction(ShortcutBindings bindings, ShortcutAction action);
    /// <summary>Unbind a view set → Cleared.</summary>
    public static EditResult ClearViews(ShortcutBindings bindings, ViewSet set);
}
```

### Persistence

```csharp
public static class ShortcutStore
{
    public const string VersionKey = "hotkeys.version";
    public const string CurrentVersion = "1";
    /// <summary>"hotkeys." + action id.</summary>
    public static string ActionKey(ShortcutAction action);
    public const string RecallViewsKey = "hotkeys.views.recall";
    public const string SaveViewsKey = "hotkeys.views.save";
    public const string UnboundValue = "none";

    /// <summary>Exactly 15 entries: version, 12 actions, 2 view sets (B27).</summary>
    public static IReadOnlyDictionary<string, string> Format(ShortcutBindings bindings);
    /// <summary>B28–B31. Never throws for content; unknown keys are ignored.</summary>
    /// <param name="defaults">Used for missing entries (normally a profile).</param>
    public static ShortcutBindings Parse(IReadOnlyDictionary<string, string> values, ShortcutBindings defaults);

    /// <summary>"KeyName,bits" or "none".</summary>
    public static string FormatCombo(KeyCombo? combo);
    /// <summary>B29: Bindable combo, or null for "none" and for anything malformed.</summary>
    public static KeyCombo? ParseCombo(string value);
    /// <summary>Decimal bits or "none".</summary>
    public static string FormatViewModifiers(Modifiers? modifiers);
    /// <summary>B30: modifiers, or null for "none" and for anything malformed.</summary>
    public static Modifiers? ParseViewModifiers(string value);
}
```

## Behaviour

### A. Catalogue (findings 1–7)

- **B1 (actions).** `ShortcutCatalog.Actions` has exactly these 12 entries in
  this order. Every entry has `RepeatsOnHold == false` and `Command ==
  ShortcutCommand.ForAction(action)`. The descriptions are exact strings.

  | # | Action | Id | Description | Command (Kind, Toward) | Target |
  |---|---|---|---|---|---|
  | 0 | Overview | `overview` | Show or hide the overview of all windows | ToggleOverview | Active |
  | 1 | StepOut | `step-out` | Zoom out one level | ZoomOut | Active |
  | 2 | StepIn | `step-in` | Zoom in one level | ZoomIn | PointerThenActive |
  | 3 | Left | `left` | Go to the tile on the left | Move, Left | Active |
  | 4 | Right | `right` | Go to the tile on the right | Move, Right | Active |
  | 5 | Up | `up` | Go to the tile above | Move, Top | Active |
  | 6 | Down | `down` | Go to the tile below | Move, Bottom | Active |
  | 7 | Back | `back` | Go back to the previous view | Back | Active |
  | 8 | Forward | `forward` | Go forward to the next view | Forward | Active |
  | 9 | Maximize | `maximize` | Show the focused window alone, or undo | ToggleMaximize | Active |
  | 10 | Desktop | `desktop` | Show or hide the empty desktop area | ToggleDesktopHidden | Active |
  | 11 | Split | `split` | Open a new Explorer window beside the current tile | SplitForNewWindow | Pointer |

- **B2 (views).** For slot `n` in 1..9:
  - `DescribeView(Recall, n)` is `"Go to saved view n"`;
  - `DescribeView(Save, n)` is `"Save the current view as view n"` (n in
    decimal).

  `GoToView(n)` has Kind GoToView, Slot n, and Action and Toward null.
  `SaveView(n)` is the same with Kind SaveView. View commands never repeat.
  There is no forget command (findings 6).
- **B3 (engine mapping for #29; documentation only, no Core code).**

  | CommandKind | `DisplayEngine` call |
  |---|---|
  | ToggleOverview | `ToggleOverview()` (**not** `ShowEverything()`) |
  | ZoomOut | `ZoomOut()` (**not** `StepOut()`) |
  | ZoomIn | `ZoomIn(Point?)` (**not** `StepIn(Point)`), argument per B4 |
  | Move | `Move(Toward)` |
  | Back / Forward | `Back()` / `Forward()` |
  | ToggleMaximize | `ToggleMaximize(null)` (null = the focused window) |
  | ToggleDesktopHidden | `ToggleDesktopHidden()` (≡ `SetDesktopHidden(!hidden)`) |
  | SplitForNewWindow | `SplitForNewWindow(...)` on the pointer engine, then ask Explorer for a new window (see A9) |
  | GoToView / SaveView | `GoToFraming(Slot)` / `SaveFraming(Slot)` |

  The target engine is resolved per `TargetEngine`. When the target engine
  does not exist, nothing runs, but the key is still consumed (findings 2).
- **B4 (StepIn target, for #29).** The step-toward-pointer preference
  defaults to on. When it is on and an engine exists under the pointer, that
  engine gets `ZoomIn(pointer)`. If that engine is in the overview, it gets
  `ZoomIn(null)` instead. Otherwise the active engine gets `ZoomIn(null)`.
- **B5.** `ForAction` for every action returns the Kind/Toward of B1, with
  `Action` set and `Slot` null. `TryParseId` matches only the exact ids of B1
  (ordinal, case-sensitive).

### B. Binding model (findings 9–10)

- **B6.** A binding is a `KeyCombo`. Each action holds one or none.
  `ShortcutBindings.WithAction` and `WithViewModifiers` replace exactly one
  value and leave every other value unchanged. They return a new instance and
  never mutate the receiver. Saved-view bindings are derived: slot `n` uses
  combo `(SlotKey(n), set)`.

### C. Matching (findings 11–15)

- **B7 (Build).** The list is:
  1. for each action in `ShortcutAction` order whose combo is non-null:
     `(combo, ForAction(action))`;
  2. then for `n = 1..9`:
     - if the recall set is non-null: `((D_n, recall), GoToView(n))`;
     - then, if the save set is non-null: `((D_n, save), SaveView(n))`.

  Unbound entries are skipped. #12/#29 call `Build` again whenever the
  bindings change (findings 15).
- **B8 (Match).** Returns the command of the **first** entry whose `Combo`
  equals the pressed combo exactly, meaning the same key **and** the same
  full modifier set. An extra or missing modifier does not match.
- **B9.** Duplicates (possible after `Parse`) are not an error. The earlier
  entry wins.

### D. Default profiles

- **B10 (Win+Alt, the default; human decision).** Action combos, then view
  sets.

  | Action | Combo | Why this key |
  |---|---|---|
  | Overview | Win+Alt+O | "O" for overview. Win+Alt+Space is avoided because it is the default of a widely used launcher (PowerToys Command Palette). |
  | StepOut | Win+Alt+Minus | Zoom-out convention ("−"). Win+Alt+Up is reserved (snap top half). |
  | StepIn | Win+Alt+Equals | Zoom-in convention ("=/+"). Win+Alt+Down is reserved (snap bottom half). |
  | Left | Win+Alt+Left | Arrow keys for direction. Believed free. |
  | Right | Win+Alt+Right | As above. |
  | Up | Win+Alt+Shift+Up | Shift+arrow for vertical moves, as in the studied layout. Avoids the reserved Win+Alt+Up. It shares modifiers with the recall set, but the key is an arrow, not a digit 1–9, so it is not a view combo. |
  | Down | Win+Alt+Shift+Down | As above. Avoids Win+Alt+Down. |
  | Back | Win+Alt+LeftBracket | `[` = back, as studied. With Win+Alt no AltGr is involved. |
  | Forward | Win+Alt+RightBracket | `]` = forward. |
  | Maximize | Win+Alt+F | "F" for fill. Win+Alt+Enter is conditionally reserved (taskbar settings). |
  | Desktop | Win+Alt+W | "W" for wallpaper area. Win+Alt+D (date/time) and Win+Alt+H (voice typing) are reserved. |
  | Split | Win+Alt+N | "N" for new window, as studied. |
  | Recall views | Win+Alt+Shift + D1..D9 | Human decision (A1). This avoids Win+Alt+digit, which opens taskbar jump lists. Believed free. |
  | Save views | Win+Ctrl+Alt + D1..D9 | Human decision (A1). It differs from recall. Believed free. |

  No WinAlt action uses Ctrl, and no WinAlt action key is a digit, so no
  action combo equals a view combo.
- **B11 (Ctrl+Alt, alternative; mirrors the studied layout).**

  | Action | Combo |
  |---|---|
  | Overview | Ctrl+Alt+Space |
  | StepOut | Ctrl+Alt+Up |
  | StepIn | Ctrl+Alt+Down |
  | Left | Ctrl+Alt+Left |
  | Right | Ctrl+Alt+Right |
  | Up | Ctrl+Alt+Shift+Up |
  | Down | Ctrl+Alt+Shift+Down |
  | Back | Ctrl+Alt+LeftBracket |
  | Forward | Ctrl+Alt+RightBracket |
  | Maximize | Ctrl+Alt+Enter |
  | Desktop | Ctrl+Alt+D |
  | Split | Ctrl+Alt+N |
  | Recall views | Ctrl+Alt + D1..D9 |
  | Save views | Ctrl+Alt+Shift + D1..D9 |

  This profile is not AltGr-safe: Ctrl+Alt+digits and Ctrl+Alt+brackets type
  characters on many layouts (`MayClashWithAltGr`). It overlaps some
  Conditional entries of B13 (the Magnifier keys, some graphics drivers).
- **B12.** These hold within each profile:
  - the 12 action combos are pairwise distinct;
  - no action combo is a view combo, so B22.3 (`ConflictsWithViews`) never
    fires for a default;
  - recall ≠ save;
  - every combo is `IsBindable`;
  - `Default` equals `WinAlt`.
- **B13 (reserved combos).** `ReservedCombos.All`, in this order. NC = needs
  confirmation on Windows (`NeedsConfirmation == true`).

  | # | Combo | Kind | Owner (our wording) | NC |
  |---|---|---|---|---|
  | 1 | Win+L | Blocked | Lock the PC | no |
  | 2 | Ctrl+Alt+Delete | Blocked | Secure sign-in screen | no |
  | 3 | Ctrl+Shift+Escape | Blocked | Task Manager | no |
  | 4 | Ctrl+Escape | Blocked | Start menu | no |
  | 5 | Alt+Tab | Blocked | Switch windows | no |
  | 6 | Ctrl+Alt+Tab | Blocked | Persistent window switcher | no |
  | 7 | Alt+F4 | Blocked | Close window | no |
  | 8 | Alt+Space | Blocked | Window menu | no |
  | 9 | Win+D | Blocked | Show desktop | no |
  | 10 | Win+Tab | Blocked | Task view | no |
  | 11 | Win+Space | Blocked | Switch input language | no |
  | 12–15 | Win+Left/Right/Up/Down | Blocked | Snap | no |
  | 16–19 | Win+Shift+Left/Right/Up/Down | Blocked | Move to monitor / stretch | no |
  | 20–22 | Win+Ctrl+Left, Win+Ctrl+Right, Win+Ctrl+D | Blocked | Virtual desktops | no |
  | 23–32 | Win+D0..D9 | Blocked | Taskbar app n | no |
  | 33–42 | Win+Shift+D0..D9 | Blocked | New instance of taskbar app n | no |
  | 43–52 | Win+Ctrl+D0..D9 | Blocked | Last window of taskbar app n | no |
  | 53–62 | Win+Alt+D0..D9 | Blocked | Jump list of taskbar app n | yes |
  | 63 | Win+Alt+Up | Blocked | Snap to top half | yes |
  | 64 | Win+Alt+Down | Blocked | Snap to bottom half | yes |
  | 65 | Win+Alt+B | Blocked | Toggle HDR | yes |
  | 66 | Win+Alt+D | Blocked | Show taskbar date and time | yes |
  | 67 | Win+Alt+K | Blocked | Mute microphone in calls | yes |
  | 68 | Win+Alt+R | Blocked | Game Bar: start/stop recording | yes |
  | 69 | Win+Alt+G | Blocked | Game Bar: record recent play | yes |
  | 70 | Win+Alt+T | Blocked | Game Bar: recording timer | yes |
  | 71 | Win+Alt+M | Blocked | Game Bar: recording microphone | yes |
  | 72 | Win+Alt+H | Conditional | Voice typing: focus keyboard | yes |
  | 73 | Win+Alt+Enter | Conditional | Taskbar item settings | yes |
  | 74 | Ctrl+Alt+Space | Conditional | Magnifier preview (while running) | yes |
  | 75–80 | Ctrl+Alt+D, F, I, L, M, R | Conditional | Magnifier views and options (while running) | yes |
  | 81–84 | Ctrl+Alt+Left/Right/Up/Down | Conditional | Magnifier pan (while running); display rotation on some graphics drivers | yes |

  There are 84 entries, and their combos are unique.
  - Win+Alt+PrintScreen (a Game Bar screenshot) is also reserved. It cannot
    be expressed, because `PrintScreen` is not a `Key`, so it is documented
    here only.
  - Win+Alt+D0..D9 stay listed as Blocked even though no default uses them
    any more. The settings UI can then warn a user who picks them.
  - Win+Alt+Shift+D1..D9 and Win+Ctrl+Alt+D1..D9 (the new WinAlt view sets)
    are **not** listed. They are believed free, but this **needs confirming
    on Windows** (see the notes for #12).

### E. Dispatcher state machine (findings 11–14)

- **B14 (key-down, not recording).** Let `cmd = Match(table, (key,
  modifiers))`.
  - `cmd` null → `PassThrough`, no command, state unchanged.
  - `cmd` non-null → `Consume`, and `key` is added to `Held`.
    - The outcome's `Command` is `cmd` when `!isRepeat`.
    - It is `null` when `isRepeat` (B15).
- **B15 (repeat).** A repeated key-down that matches is consumed and runs
  nothing. No action and no view command ever repeats on hold (findings 12).
  Whether a key-down is a repeat comes only from `isRepeat`. `Held`
  membership does not make a key-down a repeat (A6).
- **B16.** A key-down that matches nothing passes through even if its key is
  in `Held`. `Held` is not changed.
- **B17 (key-up, not recording).**
  - `key` in `Held` → remove it, `Consume`.
  - Otherwise → `PassThrough`, state unchanged.

  A key-up never produces a command.
- **B18.** `DispatchState.Initial` has `Recording == false` and `Held` empty.
  `WithRecording(r)` keeps `Held`.
- **B19 (recording, findings 14).** While `Recording`:
  - every key-down → `PassThrough`, no command, state unchanged;
  - every key-up → `PassThrough`, and `key` is removed from `Held` if present
    (A7).
- **B20.** `KeyDown`/`KeyUp` never mutate their input state. A null `state`
  or `table` → `ArgumentNullException`. An undefined `key`, or `modifiers`
  outside `All` → `ArgumentOutOfRangeException`.

### F. Editing (findings 16–20)

- **B21 (RecordForAction).** The first rule that applies decides:
  1. `key == Escape` (any modifiers) → `Cancelled` (A8).
  2. `modifiers == None` and `key` is `Backspace` or `Delete` → the same as
     `ClearAction`.
  3. `!HasRequiredModifier(modifiers)` → `NeedsModifier`.
  4. Otherwise → `SetAction(bindings, action, new KeyCombo(key,
     modifiers))`.
- **B22 (SetAction).** Let `c` be the combo. The first rule that applies
  decides:
  1. `c.Key == Escape` → `NotBindable`.
  2. `!HasRequiredModifier(c.Modifiers)` → `NeedsModifier`.
  3. `SlotOf(c.Key) != null` and `c.Modifiers` equals the non-null recall
     set → `ConflictsWithViews` with `OtherViews = Recall`. Else the same
     check against the save set, with `OtherViews = Save` (findings 18a).
  4. Some **other** action `o` (the first in order) has exactly `c` → `o`
     becomes unbound and `action` gets `c`. The result is `Moved` with
     `OtherAction = o` (findings 18b).
  5. Otherwise → `action` gets `c`, and the result is `Stored`. This includes
     the case where `action` already had `c`.

  Refusals return bindings equal to the input. Reserved combos (B13) are
  **not** refused here: `ReservedCombos.Find` is a warning source for the
  settings UI only.
- **B23 (Clear).** `ClearAction` sets the action to null. `ClearViews` sets
  the set to null. Both return `Cleared`, even when the value was already
  unbound. Clearing a view set removes all nine bindings of that kind
  (findings 7).
- **B24 (RecordForViews).** The first rule that applies decides:
  1. `key == Escape` → `Cancelled`.
  2. `modifiers == None` and `key` is Backspace or Delete → `ClearViews`.
  3. `!HasRequiredModifier(modifiers)` → `NeedsModifier`.
  4. `SlotOf(key) == null` → `NotADigit`. This includes D0. Which digit 1–9
     was pressed does not matter.
  5. Otherwise → `SetViewModifiers(bindings, set, modifiers)`.
- **B25 (SetViewModifiers).** The first rule that applies decides:
  1. `!HasRequiredModifier(m)` → `NeedsModifier`.
  2. The other set is non-null and equals `m` → `RecallEqualsSave` with
     `OtherViews =` the other set.
  3. Some action (the first in order) is bound to `(D1..D9, m)` →
     `ConflictsWithAction` with `OtherAction =` that action. No combo is
     taken from it (findings 19).
  4. Otherwise → stored, and the result is `Stored`.
- **B26 (restore defaults).** Restoring defaults means taking
  `ShortcutProfiles.Get(profile)`. It covers all 12 actions **and** both view
  sets. `AreDefaults` is plain value equality.

### G. Persistence (findings 21–22)

- **B27 (Format).** The result has exactly 15 entries:
  - `VersionKey → "1"`;
  - for each action, `ActionKey(a) = "hotkeys." + id → FormatCombo(combo)`;
  - `RecallViewsKey` and `SaveViewsKey` → `FormatViewModifiers(set)`.

  The formats are:
  - `FormatCombo(c)` = `c.Key` member name + `","` + `(int)c.Modifiers` in
    invariant decimal, for example `"O,10"`. `null` → `"none"`.
  - `FormatViewModifiers(m)` = `(int)m` in decimal, for example `"14"`.
    `null` → `"none"`.
- **B28 (Parse).** The steps are:
  1. If `values` has `VersionKey` and its value is not exactly `"1"` → return
     `defaults` unchanged (A10).
  2. Otherwise, for each action:
     - key absent → `defaults[action]`;
     - key present → `ParseCombo(value)` (null = unbound).
  3. The same applies to the two view sets, with `ParseViewModifiers` and
     `defaults.ViewModifiers(set)`.
  4. Unknown keys are ignored.

  No editing rule (move or conflict) is applied. The result may contain
  duplicates (B9) or equal view sets. A `null` dictionary value counts as
  malformed (unbound).
- **B29 (ParseCombo).** The value is malformed, and the result is null, unless
  **all** of these hold:
  - it contains exactly one `','`;
  - the left part is exactly (ordinal, case-sensitive) the name of a defined
    `Key` member. Numeric strings are rejected, so a naive `Enum.TryParse`
    must not be used;
  - the right part is 1 or more ASCII digits `0-9` only (no sign, no
    whitespace), with a value ≤ 15;
  - the combo is `IsBindable` (A11).

  `"none"` (exact, lowercase) → null.
- **B30 (ParseViewModifiers).** Valid iff the value is 1 or more ASCII digits
  with a value ≤ 15 **and** has the required modifier. Otherwise, and for
  `"none"`, the result is null.
- **B31 (round trip).** For every `ShortcutBindings b` and any `d`,
  `Parse(Format(b), d) == b`.

## Edge cases & errors

- **E1.** `new KeyCombo((Key)0, …)`, `new KeyCombo((Key)999, …)` or modifiers
  `(Modifiers)16` → `ArgumentOutOfRangeException`. Passing `default(KeyCombo)`
  to `SetAction`, `Match`, `ActionBoundTo`, `IsBindable`, `MayClashWithAltGr`
  or `Find` → `ArgumentOutOfRangeException`.
- **E2.** `ShortcutBindings` indexer, `WithAction`, `Info`, `ForAction` with
  an undefined action → `ArgumentOutOfRangeException`. An undefined `ViewSet`
  → `ArgumentOutOfRangeException`.
- **E3.** `WithAction(a, Shift+K)` or `WithAction(a, Win+Escape)` →
  `ArgumentException`. `Unbound with { RecallViewModifiers = Modifiers.Shift
  }` → `ArgumentException`. `Modifiers.None` → `ArgumentException`.
- **E4.** Null `bindings`, `values`, `defaults`, `value`, `id` →
  `ArgumentNullException`.
- **E5.** Holding a matched key and then releasing a modifier: the next
  repeat key-down no longer matches and passes through (B16). The key-up is
  still consumed (B17).
- **E6.** `Build` with recall == save (only possible through `Parse` or
  direct `with`): both entries are listed, and recall wins (B8).
- **E7.** `SetAction` on a digit 1–9 combo while the matching view set is
  unbound → allowed. A later `SetViewModifiers` with those modifiers is then
  refused (B25.3).

## Acceptance criteria

All criteria are **[mac]**. They are xUnit tests in
`tests/CanvasWm.Core.Tests/ShortcutTests/` with no OS access. There are no
[windows] criteria. The Windows checks are notes for #12 (see "Notes for #12").

Notation: `WA = Modifiers.Win | Modifiers.Alt`, `CA = Modifiers.Ctrl |
Modifiers.Alt`, `WCA = Modifiers.Win | Modifiers.Ctrl | Modifiers.Alt`, and
`+S` adds `Modifiers.Shift`. `k(K, m)` = `new KeyCombo(K, m)`. A state with
`Held == {Right}` is obtained by
`KeyDown(Initial, Build(WinAlt), Right, WA, false)`.

### Catalogue and types
- **AC1** Catalogue (B1):
  - `ShortcutCatalog.Actions.Count == 12`;
  - entry `i` has `(int)Action == i`;
  - the ids, descriptions, Command Kind/Toward and Target equal table B1
    exactly;
  - every entry has `RepeatsOnHold == false`, `Info(a) == Actions[(int)a]`,
    `Command.Action == a` and `Command.Slot == null`;
  - `Toward` is non-null exactly for the four Move actions.

  (B1, B5)
- **AC2** `TryParseId`:
  - each B1 id → its action;
  - `"Overview"`, `"overview "`, `""` and `"everything"` → false.

  Ids and descriptions are unique. No id or description (and no
  `DescribeView` text, and no `ReservedCombo.Owner`) contains `"Trellis"` or
  `"Licens"` (ordinal, ignore case). (B1, B5, naming rule)
- **AC3** View commands:
  - `GoToView(3)` has Kind GoToView, Slot 3, and Action and Toward null;
  - `SaveView(9)` has Kind SaveView, Slot 9;
  - `GoToView(0)`, `GoToView(10)` and `SaveView(-1)` →
    `ArgumentOutOfRangeException`;
  - `DescribeView(Recall, 3) == "Go to saved view 3"`;
  - `DescribeView(Save, 3) == "Save the current view as view 3"`;
  - `DescribeView(Recall, 0)` throws.

  (B2)
- **AC4** Enums:
  - `Key` has 86 members and no member with value 0;
  - `(int)Key.A == 1`, `Key.Z == 26`, `D0 == 30`, `D9 == 39`, `F1 == 40`,
    `F24 == 63`, `Left == 70`, `Backspace == 83`, `Delete == 84`,
    `Escape == 85`, `Backquote == 110`;
  - `Modifiers`: Ctrl 1, Alt 2, Shift 4, Win 8, All 15;
  - `ShortcutAction`, `CommandKind`, `TargetEngine`, `ViewSet`, `EditOutcome`,
    `ReservedKind`, `KeyVerdict` and `ShortcutProfile` have the raw values in
    the Public API.

  (Public API)
- **AC5** `KeyCombo` and rules:
  - `k(O, WA) == k(O, WA)`; `k(O, WA) != k(O, WA+S)` and `!= k(P, WA)`;
  - E1 throws as specified;
  - `HasRequiredModifier`: true for Ctrl, Alt, Win and any superset; false for
    None and Shift;
  - `IsBindable(k(Escape, WA))` is false and `IsBindable(k(Backspace, WA))`
    is true;
  - `SlotOf(D1..D9) == 1..9`; `SlotOf(D0)` and `SlotOf(A)` are null;
  - `SlotKey(5) == D5`; `SlotKey(0)` throws;
  - `MayClashWithAltGr`: true for `k(D5, CA)`, `k(LeftBracket, CA+S)`,
    `k(Q, CA)` and `k(Space, CA)`; false for `k(D5, WA)`, `k(D5, WCA)`,
    `k(Left, CA)` and `k(F1, CA)`.

  (B12 helpers, E1)
- **AC6** `ShortcutBindings`:
  - `Unbound` has all 12 entries null and both sets null;
  - `b2 = b.WithAction(Back, k(B, WA))` leaves `b` unchanged and differs from
    `b` only at Back;
  - two separately built equal configurations are `Equals` and have equal
    `GetHashCode`;
  - E3 throws as specified;
  - `ActionBoundTo` returns the first action in order when two actions share
    a combo;
  - E2 throws as specified.

  (B6, E2, E3)

### Defaults and reserved combos
- **AC7** `ShortcutProfiles.WinAlt` equals table B10 exactly: all 12 combos,
  `RecallViewModifiers == WA+S` and `SaveViewModifiers == WCA`. (B10)
- **AC8** `ShortcutProfiles.CtrlAlt` equals table B11 exactly, with recall
  `CA` and save `CA+S`. (B11)
- **AC9** For each profile:
  - B12 holds: the 12 combos are distinct; no action combo equals any of its
    18 view combos; recall ≠ save; all are bindable;
  - for each action `a`, `SetAction(p, a, p[a])` → Stored with bindings equal
    to `p`. So `ConflictsWithViews` never fires for a default;
  - for each set `s`, `SetViewModifiers(p, s, p.ViewModifiers(s))` → Stored.
    So `ConflictsWithAction` and `RecallEqualsSave` never fire for a default;
  - `Default == WinAlt` and `Get(WinAlt) == WinAlt`;
  - `AreDefaults(WinAlt, WinAlt)` is true;
  - `AreDefaults(WinAlt.WithAction(Split, null), WinAlt)` is false.

  (B12, B22, B25, B26)
- **AC10** Reserved list:
  - `ReservedCombos.All.Count == 84` and its combos are unique;
  - `Find` returns entries for Win+L, Ctrl+Alt+Delete, Win+Alt+R, G, B, D, K,
    Up, Down and Win+Alt+D5, all of Kind Blocked;
  - Win+Alt+H and Ctrl+Alt+Space are Conditional;
  - `Find(k(O, WA))`, `Find(k(D5, WA+S))` and `Find(k(D5, WCA))` are null.

  (B13)
- **AC11** No reserved combo in the defaults:
  - **(a)** No **action** combo of either profile is `Find`-able with Kind
    Blocked.
  - **(b)** No action combo of WinAlt is `Find`-able at all (Blocked or
    Conditional).
  - **(c)** None of CtrlAlt's 18 view combos is Blocked.
  - **(d)** None of WinAlt's 18 view combos (`k(D1..D9, WA+S)`,
    `k(D1..D9, WCA)`) is Blocked.
  - **(e)** None of WinAlt's 18 view combos is `Find`-able at all.
  - **(f)** So no Blocked combo appears in either default profile, while
    `k(D1..D9, WA)` remain in `ReservedCombos.All` as Blocked.

  (B10, B11, B13, A1)

### Matching
- **AC12** Build:
  - `Build(WinAlt)` has 30 entries: the 12 actions in order, then for
    `n = 1..9` the recall entry and the save entry. For example, entry 12 is
    `(k(D1, WA+S), GoToView(1))` and entry 13 is `(k(D1, WCA), SaveView(1))`;
  - with Split unbound → 29 entries;
  - with recall unbound → 21 entries and no GoToView;
  - `Build(Unbound)` is empty.

  (B7)
- **AC13** Match with WinAlt:
  - `k(O, WA)` → ForAction(Overview);
  - `k(Up, WA+S)` → ForAction(Up), not StepOut and not a view command;
  - `k(Right, WA | Ctrl)` → null (extra modifier);
  - `k(Right, Win)` → null (missing modifier);
  - `k(D3, WA+S)` → GoToView(3) and `k(D3, WCA)` → SaveView(3);
  - `k(D3, WA)` → null (not bound; reserved for jump lists);
  - `k(D0, WA+S)` → null.

  (B8; findings examples 1, 2, 4, 5)
- **AC14** First match wins. In bindings where Back and Forward are both
  `k(B, WA)` (built with `WithAction`), Match gives ForAction(Back). With
  recall == save == `WA+S` (built via `with`), `k(D2, WA+S)` → GoToView(2).
  (B8, B9, E6)
- **AC15** Hold sequence (findings example 3), WinAlt table, Initial state:
  - `KeyDown(Right, WA, false)` → Consume, command ForAction(Right), `Held ==
    {Right}`;
  - 10× `KeyDown(Right, WA, true)` → each Consume with null command;
  - `KeyUp(Right)` → Consume, `Held` empty.

  Exactly one non-null command over the sequence. The input state instances
  are unchanged after each call. (B14, B15, B17, B20)
- **AC16** Pass-through cases:
  - `KeyUp(Q)` from Initial → PassThrough, `Held` empty and `Recording`
    false;
  - `KeyDown(Q, WA, false)` → PassThrough, no command, `Held` unchanged;
  - with `Held == {Right}`: `KeyDown(Right, Win, true)` (Alt released) →
    PassThrough and `Held` still `{Right}`; then `KeyUp(Right)` → Consume;
  - `KeyDown(O, WA, true)` from Initial (repeat flag, never held) → Consume
    with null command.

  (B14–B17, E5, A6)
- **AC17** Recording:
  - with `Held == {Right}`, `WithRecording(true)` keeps `Held`;
  - then `KeyDown(O, WA, false)` → PassThrough, null command, `Held` still
    `{Right}`;
  - `KeyUp(O)` → PassThrough;
  - `KeyUp(Right)` → PassThrough and `Held` empty;
  - after `WithRecording(false)`, `KeyDown(O, WA, false)` → Consume with
    ForAction(Overview).

  (B18, B19, A7)
- **AC18** `KeyDown` with `(Key)0` or `(Modifiers)32` →
  `ArgumentOutOfRangeException`. A null state or table →
  `ArgumentNullException`. (B20)

### Editing (all start from `b = WinAlt` unless stated)
- **AC19** Cancel and clear:
  - `RecordForAction(b, Maximize, Escape, None)` and `(…, Escape, WA)` →
    Cancelled, bindings equal to `b`;
  - `RecordForAction(b, Maximize, Backspace, None)` and `(…, Delete, None)` →
    Cleared, Maximize null, everything else unchanged;
  - `(…, Backspace, WA)` → Stored with Maximize = `k(Backspace, WA)`;
  - `RecordForViews(b, Save, Delete, None)` → Cleared, save null, and
    `Build` has no SaveView entries;
  - `RecordForViews(b, Recall, Escape, None)` → Cancelled.

  (B21, B23, B24; findings example 10)
- **AC20** Required modifier:
  - `RecordForAction(b, Split, K, Shift)` and `(…, K, None)` → NeedsModifier,
    unchanged;
  - `(…, K, Ctrl)`, `(…, K, Alt)` and `(…, K, Win)` → Stored;
  - `SetAction(b, Split, k(Escape, WA))` → NotBindable;
  - `SetAction(b, Split, k(K, Shift))` → NeedsModifier;
  - `RecordForViews(b, Recall, D4, Shift)` → NeedsModifier;
  - `SetViewModifiers(b, Recall, Shift)` → NeedsModifier.

  (B21, B22, B24, B25)
- **AC21** Move (findings example 6): `SetAction(b, Back, k(O, WA))` → Moved
  with `OtherAction == Overview`. Overview is null, Back is `k(O, WA)`, and
  everything else is unchanged. `SetAction(b, Back, k(LeftBracket, WA))` (its
  own combo) → Stored, bindings equal `b`. (B22)
- **AC22** View conflicts for actions (findings example 7):
  - `SetAction(b, Desktop, k(D5, WA+S))` → ConflictsWithViews with
    `OtherViews == Recall`, Desktop still `k(W, WA)`;
  - `k(D5, WCA)` → ConflictsWithViews with `OtherViews == Save`;
  - `k(D0, WA+S)` → Stored;
  - `k(D5, CA)` → Stored;
  - `k(D5, WA)` → Stored. It is reserved, but reserved combos are not refused
    by the editor (B22);
  - with recall cleared first, `k(D5, WA+S)` → Stored;
  - `SetAction(b, Up, k(Up, WA+S))` → Stored (an arrow is not a view digit).

  (B22, E7)
- **AC23** View sets:
  - `SetViewModifiers(b, Recall, WCA)` → RecallEqualsSave with
    `OtherViews == Save`, unchanged (findings example 8);
  - `SetViewModifiers(b, Recall, WA+S)` (its own value) → Stored;
  - `RecordForViews(b, Recall, K, CA)` and `(…, D0, CA)` → NotADigit;
  - `RecordForViews(b, Recall, D7, CA)` → Stored with recall = `CA`, and
    Build now has `(k(D7, CA), GoToView(7))`;
  - findings example 9: `b1 = SetAction(b, Split, k(D7, CA)).Bindings`
    (Stored); then `SetViewModifiers(b1, Recall, CA)` → ConflictsWithAction
    with `OtherAction == Split`, Split still bound and recall still `WA+S`;
  - with save cleared first (`b′`), `SetViewModifiers(b′, Save, WA+S)` →
    RecallEqualsSave with `OtherViews == Recall`, and
    `SetViewModifiers(b′, Save, CA)` → Stored.

  (B24, B25)
- **AC24** `ClearAction` and `ClearViews` on an already unbound value →
  Cleared, equal bindings. `Accepted` is true exactly for Stored, Moved and
  Cleared. Every refused or cancelled result has `Bindings.Equals(input)`.
  (B23, EditResult)

### Persistence
- **AC25** `Format(WinAlt)` has exactly 15 entries, including:
  - `"hotkeys.version" → "1"`;
  - `"hotkeys.overview" → "O,10"`;
  - `"hotkeys.up" → "Up,14"`;
  - `"hotkeys.back" → "LeftBracket,10"`;
  - `"hotkeys.step-out" → "Minus,10"`;
  - `"hotkeys.views.recall" → "14"`;
  - `"hotkeys.views.save" → "11"`.

  `Format(CtrlAlt)["hotkeys.desktop"] == "D,3"`.
  `Format(Unbound)` has every action and view value `"none"`. (B27)
- **AC26** Round trip (B31): `Parse(Format(x), Unbound) == x` for `x` in
  WinAlt, CtrlAlt, Unbound, and for 200 configurations from a fixed-seed
  generator. The generator picks each action as unbound or a random bindable
  combo, and each set as null or a random modifier set with the required
  modifier.
- **AC27** Missing and none (findings example 11):
  - `Parse({}, WinAlt) == WinAlt`;
  - `Parse({"hotkeys.overview": "none"}, WinAlt)` has Overview null and the
    rest from WinAlt;
  - `Parse({"hotkeys.views.recall": "none"}, WinAlt)` → recall null, save
    still `WCA`, and `Build` has 21 entries;
  - unknown keys such as `"hotkeys.everything"` and `"x"` are ignored.

  (B28)
- **AC28** Malformed values → unbound (B29, B30). Each of these action
  values parses to null:
  `"abc"`, `"O"`, `"O,10,1"`, `"o,10"`, `"15,10"`, `"O,16"`, `"O,-1"`,
  `"O,+2"`, `" O,10"`, `"O, 10"`, `"O,"`, `",10"`, `"O,4"`, `"O,0"`,
  `"Escape,10"`, `"None"`, `""`, and a null dictionary value.

  These parse to valid combos:
  - `"O,010"` → `k(O, WA)`;
  - `"F24,1"` → `k(F24, Ctrl)`.

  Each of these view values parses to null: `"abc"`, `"16"`, `"4"`, `"0"`,
  `""`, `"-2"`, `"None"`, `"10 "`. `"3"` → `CA`.
- **AC29** Version (A10):
  - `Parse({"hotkeys.version": "2", "hotkeys.overview": "none"}, WinAlt) ==
    WinAlt`;
  - with `"1"` or no version entry, Overview is null.
- **AC30** Architecture:
  - the existing Core reference test still passes, so Core references no
    `CanvasWm.*` assembly;
  - every public type listed in this spec is in namespace
    `CanvasWm.Core.Shortcuts` in assembly `CanvasWm.Core`;
  - reflection over these types' public members finds no parameter, return or
    property type outside `System.*` and `CanvasWm.Core.*`.

  (Placement)

## Assumptions

- **A1 (human decision taken).** In the WinAlt profile:
  - recall views = **Win+Alt+Shift+1–9**;
  - save views = **Win+Ctrl+Alt+1–9**.

  Reason: Windows uses `Win+Alt+<number>` to open the jump list of taskbar
  app n. So `Win+Alt+1–9` would very likely fail in `RegisterHotKey`, or the
  shell would act first.
  - `Win+Alt+D0..D9` stay in `ReservedCombos` as Blocked.
  - Recall and save still differ.
  - No WinAlt action combo collides with the new sets. Win+Alt+Shift+Up/Down
    use arrow keys, not digits, and no action uses Ctrl.
  - Both new sets are believed free. They are marked "needs confirming on
    Windows" for #12.
- **A2 (needs review).** Several B10 choices replace the studied keys
  because a Win+Alt combo is reserved: StepOut/StepIn on Minus/Equals,
  Maximize on F, Desktop on W, Overview on O. The reserved facts in B13
  marked NC come from general knowledge of Windows 10/11 and must be
  confirmed by #12 (see Notes).
- **A3.** Persisted ids and key names are our own (`overview`, not the
  studied id). Values are `"KeyName,modifierBits"` with our `Modifiers`
  bits, not platform codes. Key names were chosen over numbers so the
  settings file stays readable and is safe against renumbering.
- **A4.** `ShortcutStore.Format`/`Parse` work on a flat string dictionary.
  #29 decides how it is embedded in the settings JSON. Which profile the
  user chose is not stored here. The App passes the profile as `defaults`.
- **A5.** `Key` covers letters, digits, F1–F24, arrows, navigation keys,
  Space/Enter/Tab/Backspace/Delete/Escape and US-position punctuation. The
  numeric keypad, PrintScreen, Pause and media keys are excluded (defer). The
  saved-view slots use the top-row digits only (findings 5).
- **A6.** "Repeat" is decided only by the caller's `isRepeat` flag, as
  observed. A held-set membership is not treated as a repeat, so a lost
  key-up can never block a shortcut. With `RegisterHotKey` + `MOD_NOREPEAT`,
  #12 simply never reports repeats.
- **A7 (small deviation).** A key-up while recording removes the key from
  `Held` (it still passes through). Otherwise a stale entry could swallow an
  unrelated key-up later. The observed handler appears to return early.
- **A8.** Escape cancels recording with **any** modifiers, and Escape can
  never be bound. The studied app intercepts Escape before shortcut matching
  (findings 24).
- **A9 (for #29).** `DisplayEngine.SplitForNewWindow(WindowKey)` takes a
  window key. The studied command passes the file manager's app id and then
  asks it for a new window. #29 must decide the argument (and may need an
  Engine signature change, which is not made here).
- **A10.** A version value other than `"1"` → the whole stored set is
  ignored (defaults). A missing version means 1.
- **A11 (deviation, safety).** A stored combo or view set lacking
  Ctrl/Alt/Win, or using Escape, parses as unbound. The studied app would
  load it as is. CanvasWM must never register a bare global key.
- **A12.** Parse does not repair conflicts (duplicates, recall == save,
  action on a view digit). The dispatcher resolves them by order. The
  editor prevents them for user edits. The editor does not refuse reserved
  combos; the settings UI may warn using `ReservedCombos.Find`.
- **A13.** The Ctrl+Alt profile mirrors the studied layout exactly, even
  though it overlaps Conditional Magnifier and driver combos and is not
  AltGr-safe. It is offered as an alternative only.

## Notes for #12 win32-hotkeys (not ACs here)

- [windows] Register every `Build` entry with `MOD_NOREPEAT` (B15).
  Map Ctrl→`MOD_CONTROL`, Alt→`MOD_ALT`, Shift→`MOD_SHIFT`, Win→`MOD_WIN`.
  Map digits to the top-row VKs `'0'..'9'`, never `VK_NUMPAD*`. Map Backspace
  to `VK_BACK` and Delete to `VK_DELETE`. Map the punctuation keys to
  `VK_OEM_*` by US position.
- [windows] Confirm each NC row of B13 on Windows 10 and 11. Above all,
  confirm Win+Alt+0–9 (jump lists), Win+Alt+Up/Down, and Game Bar
  R/G/T/M/PrintScreen. The Game Bar keys can be changed, so check them with
  Game Bar on and off.
- [windows] **Needs confirming on Windows:** the new WinAlt view sets
  **Win+Alt+Shift+1–9** (recall) and **Win+Ctrl+Alt+1–9** (save) are free on
  Windows 10 and 11. Check that they register and that no shell or taskbar
  action fires. If either is taken, add it to `ReservedCombos` and raise it
  for a new human decision.
- [windows] Confirm that every B10 action default registers on a clean
  Windows 11 install: Win+Alt+O, Minus, Equals, Left, Right, Shift+Up,
  Shift+Down, [, ], F, W, N.
- [windows] Report "unavailable" per entry when `RegisterHotKey` fails.
- The dispatcher state (B14–B19) is needed only if #12 uses a `WH_KEYBOARD_LL`
  hook. With `RegisterHotKey`, call `Match` on `WM_HOTKEY`.

## Traceability

| Spec item | Findings rule |
|---|---|
| B1, B3, B5, AC1, AC2 | 1 (ids reworded to our own) |
| B3, B4 | 2, 3, 4 |
| B2, B7, AC3, AC12 | 5, 6, 7 |
| — (out of scope, #15) | 8 |
| Key, Modifiers, KeyCombo, AC4, AC5 | 9 (our own key enum and bits, not platform codes) |
| B6, AC6 | 10 |
| B7, B8, B9, AC12–AC14 | 11, 15 |
| B14, B15, AC15, AC16, A6 | 11, 12 |
| B16, B17, AC16 | 13 |
| B18, B19, AC17, A7 | 14 |
| B21, B24, AC19, AC20, A8 | 16, 17, 24 |
| B22, AC21, AC22 | 18a–c |
| B25, AC23 | 19 |
| B26, AC9 | 20 |
| B27–B31, AC25–AC29, A3, A10, A11 | 21, 22 (shape kept, codes replaced) |
| B10–B13, AC7–AC11, A1, A2, A13 | 23 + findings "Windows notes" (modifier mapping, AltGr, reserved combos) + human decisions (Win+Alt profile; WinAlt view sets Win+Alt+Shift / Win+Ctrl+Alt) |
| Out of scope | 24, 25, 26, 27 |

## Amendments from #12 (win32-hotkeys, R1)

- `Key` punctuation members are the keys that type these characters on the US layout, identified by key code (VK_OEM_*), not physical position; see specs/win32-hotkeys.md B1 and table B20. The "by US position" wording in "Notes for #12" is superseded.
