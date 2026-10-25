using CanvasWm.Core.Shortcuts;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Hotkeys;

/// <summary>Core modifiers to MOD_* flags, member by member (the numeric values differ).</summary>
internal static class ModMap
{
    /// <summary>The MOD_* flags of a modifier set, without MOD_NOREPEAT.</summary>
    internal static uint ToModFlags(Modifiers modifiers)
    {
        if (((int)modifiers & ~(int)Modifiers.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(modifiers), modifiers, "Modifier bits outside Modifiers.All.");
        }

        uint f = 0;
        if (modifiers.HasFlag(Modifiers.Alt)) { f |= NativeConstants.MOD_ALT; }
        if (modifiers.HasFlag(Modifiers.Ctrl)) { f |= NativeConstants.MOD_CONTROL; }
        if (modifiers.HasFlag(Modifiers.Shift)) { f |= NativeConstants.MOD_SHIFT; }
        if (modifiers.HasFlag(Modifiers.Win)) { f |= NativeConstants.MOD_WIN; }
        return f;
    }

    /// <summary>ToModFlags plus MOD_NOREPEAT.</summary>
    internal static uint RegisterFlags(Modifiers modifiers) => ToModFlags(modifiers) | NativeConstants.MOD_NOREPEAT;

    /// <summary>Reverse map of the four MOD_* bits; every other bit is ignored.</summary>
    internal static Modifiers FromModFlags(uint flags)
    {
        Modifiers m = Modifiers.None;
        if ((flags & NativeConstants.MOD_ALT) != 0) { m |= Modifiers.Alt; }
        if ((flags & NativeConstants.MOD_CONTROL) != 0) { m |= Modifiers.Ctrl; }
        if ((flags & NativeConstants.MOD_SHIFT) != 0) { m |= Modifiers.Shift; }
        if ((flags & NativeConstants.MOD_WIN) != 0) { m |= Modifiers.Win; }
        return m;
    }
}
