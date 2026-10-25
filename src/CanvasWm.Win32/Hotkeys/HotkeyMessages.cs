using CanvasWm.Core.Shortcuts;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Hotkeys;

/// <summary>Pure decoding and mapping helpers for hotkey and key messages.</summary>
internal static class HotkeyMessages
{
    internal const int ERROR_HOTKEY_ALREADY_REGISTERED = NativeConstants.ERROR_HOTKEY_ALREADY_REGISTERED;

    private const long WordMask = 0xFFFF;
    private const int RepeatBit = 30;
    private const long MaxVk = 0xFF;

    /// <summary>Maps a registration result to a report (B12).</summary>
    internal static HotkeyReport ReportFor(KeyCombo combo, bool registered, int lastError, bool onLayout)
    {
        if (registered)
        {
            return new HotkeyReport(combo, onLayout ? HotkeyStatus.Registered : HotkeyStatus.NotOnLayout, 0);
        }

        return lastError == ERROR_HOTKEY_ALREADY_REGISTERED
            ? new HotkeyReport(combo, HotkeyStatus.Taken, lastError)
            : new HotkeyReport(combo, HotkeyStatus.Failed, lastError);
    }

    /// <summary>Decodes a WM_HOTKEY into the registered combo, or null when it must be ignored (B14).</summary>
    internal static KeyCombo? DecodeHotkey(nint wParam, nint lParam, IReadOnlyDictionary<int, KeyCombo> registeredById)
    {
        long raw = wParam;
        if (raw < 0 || raw > HotkeyIds.MaxApplicationId)
        {
            return null;
        }

        int id = (int)raw;
        if (!registeredById.TryGetValue(id, out KeyCombo c))
        {
            return null;
        }

        long lp = lParam;
        uint low = (uint)(lp & WordMask);
        long high = (lp >> 16) & WordMask;
        if (ModMap.FromModFlags(low) != c.Modifiers || high != KeyMap.ToVk(c.Key))
        {
            // SPEC-ASSUMPTION A4: a mismatch is dropped silently (no logger in this layer, A13).
            return null;
        }

        return c;
    }

    /// <summary>Translates a key-down message plus modifier state into a combo (B18).</summary>
    internal static KeyCombo? TranslateKeyMessage(uint message, nint wParam, nint lParam,
                                                  bool ctrl, bool alt, bool shift, bool win)
    {
        if (message != NativeConstants.WM_KEYDOWN && message != NativeConstants.WM_SYSKEYDOWN)
        {
            return null;
        }

        // SPEC-ASSUMPTION A6: auto-repeat is ignored, and the extended-key bit is not looked at.
        if (((long)lParam >> RepeatBit & 1) != 0)
        {
            return null;
        }

        long vk = wParam;
        if (vk < 0 || vk > MaxVk || !KeyMap.TryFromVk((int)vk, out Key key))
        {
            return null;
        }

        Modifiers mods = Modifiers.None;
        if (ctrl) { mods |= Modifiers.Ctrl; }
        if (alt) { mods |= Modifiers.Alt; }
        if (shift) { mods |= Modifiers.Shift; }
        if (win) { mods |= Modifiers.Win; }
        return new KeyCombo(key, mods);
    }
}
