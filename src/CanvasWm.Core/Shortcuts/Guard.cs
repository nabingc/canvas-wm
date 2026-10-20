namespace CanvasWm.Core.Shortcuts;

/// <summary>Shared argument checks for the shortcut model.</summary>
internal static class Guard
{
    internal static void Key(Key key, string name)
    {
        if (!Enum.IsDefined(key)) throw new ArgumentOutOfRangeException(name, key, "Undefined key.");
    }

    internal static void Mods(Modifiers modifiers, string name)
    {
        if (((int)modifiers & ~(int)Modifiers.All) != 0)
            throw new ArgumentOutOfRangeException(name, modifiers, "Modifier bits outside Modifiers.All.");
    }

    internal static void Combo(KeyCombo combo, string name)
    {
        if (!Enum.IsDefined(combo.Key))
            throw new ArgumentOutOfRangeException(name, combo, "Invalid (default) key combo.");
        Mods(combo.Modifiers, name);
    }

    internal static void Action(ShortcutAction action, string name)
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(name, action, "Undefined action.");
    }

    internal static void Set(ViewSet set, string name)
    {
        if (!Enum.IsDefined(set)) throw new ArgumentOutOfRangeException(name, set, "Undefined view set.");
    }

    internal static void Slot(int slot, string name)
    {
        if (slot < 1 || slot > 9) throw new ArgumentOutOfRangeException(name, slot, "Slot must be 1..9.");
    }

    internal static void NotNull(object? value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
    }
}
