namespace CanvasWm.Core.Shortcuts;

/// <summary>The built-in default profiles.</summary>
public enum ShortcutProfile
{
    /// <summary>Win+Alt (the default).</summary>
    WinAlt = 0,
    /// <summary>Ctrl+Alt (alternative).</summary>
    CtrlAlt = 1,
}

/// <summary>The default shortcut profiles.</summary>
public static class ShortcutProfiles
{
    private const Modifiers WA = Modifiers.Win | Modifiers.Alt;
    private const Modifiers CA = Modifiers.Ctrl | Modifiers.Alt;

    /// <summary>Default profile (human decision): Win+Alt. Equals WinAlt.</summary>
    public static ShortcutBindings Default => WinAlt;

    // SPEC-ASSUMPTION A2: several keys replace the studied ones because a Win+Alt combo is reserved
    // (Overview O, StepOut Minus, StepIn Equals, Maximize F, Desktop W); reserved facts need confirming on Windows.
    // SPEC-ASSUMPTION A1: recall = Win+Alt+Shift+1..9, save = Win+Ctrl+Alt+1..9 (human decision; needs confirming on Windows).
    /// <summary>The Win+Alt profile.</summary>
    public static ShortcutBindings WinAlt { get; } = Make(
        new(Key.O, WA), new(Key.Minus, WA), new(Key.Equals, WA),
        new(Key.Left, WA), new(Key.Right, WA),
        new(Key.Up, WA | Modifiers.Shift), new(Key.Down, WA | Modifiers.Shift),
        new(Key.LeftBracket, WA), new(Key.RightBracket, WA),
        new(Key.F, WA), new(Key.W, WA), new(Key.N, WA),
        WA | Modifiers.Shift, WA | Modifiers.Ctrl);

    // SPEC-ASSUMPTION A13: mirrors the studied layout exactly, although it overlaps Conditional reserved
    // combos and is not AltGr-safe; offered as an alternative only.
    /// <summary>The Ctrl+Alt profile (mirrors the studied layout; not AltGr-safe).</summary>
    public static ShortcutBindings CtrlAlt { get; } = Make(
        new(Key.Space, CA), new(Key.Up, CA), new(Key.Down, CA),
        new(Key.Left, CA), new(Key.Right, CA),
        new(Key.Up, CA | Modifiers.Shift), new(Key.Down, CA | Modifiers.Shift),
        new(Key.LeftBracket, CA), new(Key.RightBracket, CA),
        new(Key.Enter, CA), new(Key.D, CA), new(Key.N, CA),
        CA, CA | Modifiers.Shift);

    /// <summary>The bindings of a profile.</summary>
    /// <exception cref="ArgumentOutOfRangeException">undefined profile.</exception>
    public static ShortcutBindings Get(ShortcutProfile profile)
    {
        if (!Enum.IsDefined(profile)) throw new ArgumentOutOfRangeException(nameof(profile), profile, "Undefined profile.");
        return profile == ShortcutProfile.WinAlt ? WinAlt : CtrlAlt;
    }

    /// <summary>True when bindings equal the profile's bindings.</summary>
    public static bool AreDefaults(ShortcutBindings bindings, ShortcutProfile profile)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        return bindings.Equals(Get(profile));
    }

    private static ShortcutBindings Make(
        KeyCombo overview, KeyCombo stepOut, KeyCombo stepIn, KeyCombo left, KeyCombo right,
        KeyCombo up, KeyCombo down, KeyCombo back, KeyCombo forward, KeyCombo maximize,
        KeyCombo desktop, KeyCombo split, Modifiers recall, Modifiers save) =>
        ShortcutBindings.Unbound
            .WithAction(ShortcutAction.Overview, overview)
            .WithAction(ShortcutAction.StepOut, stepOut)
            .WithAction(ShortcutAction.StepIn, stepIn)
            .WithAction(ShortcutAction.Left, left)
            .WithAction(ShortcutAction.Right, right)
            .WithAction(ShortcutAction.Up, up)
            .WithAction(ShortcutAction.Down, down)
            .WithAction(ShortcutAction.Back, back)
            .WithAction(ShortcutAction.Forward, forward)
            .WithAction(ShortcutAction.Maximize, maximize)
            .WithAction(ShortcutAction.Desktop, desktop)
            .WithAction(ShortcutAction.Split, split)
            .WithViewModifiers(ViewSet.Recall, recall)
            .WithViewModifiers(ViewSet.Save, save);
}
