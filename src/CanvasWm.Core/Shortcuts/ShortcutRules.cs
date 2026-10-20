namespace CanvasWm.Core.Shortcuts;

/// <summary>Pure rule helpers.</summary>
public static class ShortcutRules
{
    /// <summary>Ctrl | Alt | Win: a binding needs at least one of these.</summary>
    public const Modifiers Required = Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win;

    /// <summary>True iff (modifiers &amp; Required) != 0.</summary>
    public static bool HasRequiredModifier(Modifiers modifiers) => (modifiers & Required) != 0;

    /// <summary>True iff the combo may be stored as a binding: HasRequiredModifier and key != Escape.</summary>
    public static bool IsBindable(KeyCombo combo)
    {
        Guard.Combo(combo, nameof(combo));
        return HasRequiredModifier(combo.Modifiers) && combo.Key != Key.Escape;
    }

    /// <summary>Slot 1..9 for D1..D9; null for every other key (including D0).</summary>
    public static int? SlotOf(Key key) =>
        key >= Key.D1 && key <= Key.D9 ? (int)key - (int)Key.D0 : null;

    /// <summary>D1..D9 for slot 1..9.</summary>
    /// <exception cref="ArgumentOutOfRangeException">slot outside 1..9.</exception>
    public static Key SlotKey(int slot)
    {
        Guard.Slot(slot, nameof(slot));
        return (Key)((int)Key.D0 + slot);
    }

    /// <summary>True iff the combo is Ctrl+Alt without Win (Shift may be present) and the key types a
    /// character (letters, digits, punctuation, Space). A settings warning only; never a refusal.</summary>
    public static bool MayClashWithAltGr(KeyCombo combo)
    {
        Guard.Combo(combo, nameof(combo));
        if ((combo.Modifiers & ~Modifiers.Shift) != (Modifiers.Ctrl | Modifiers.Alt)) return false;
        Key k = combo.Key;
        return (k >= Key.A && k <= Key.Z) || (k >= Key.D0 && k <= Key.D9) || k == Key.Space
            || (k >= Key.Minus && k <= Key.Backquote);
    }
}
