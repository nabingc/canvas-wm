using CanvasWm.Core.Shortcuts;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Hotkeys;

/// <summary>Keyboard helpers for the settings UI. Pure apart from reading the calling thread's
/// key state and keyboard layout.</summary>
public static class Win32KeyInput
{
    private const uint LowWordMask = 0xFFFF;
    private const char FirstPrintable = (char)0x20;

    /// <summary>Translates a WM_KEYDOWN / WM_SYSKEYDOWN that the calling thread is handling right now
    /// into a combo, reading Ctrl/Alt/Shift/Win with GetKeyState (B18). Returns null for any other
    /// message, an auto-repeat, or a key that is not a Core Key. The combo may be unbindable
    /// (Escape, no modifier): the caller passes it on to the shortcut editor, which decides.</summary>
    public static KeyCombo? ReadKeyMessage(uint message, nint wParam, nint lParam)
    {
        bool ctrl = NativeMethods.GetKeyState(NativeConstants.VK_CONTROL) < 0;
        bool alt = NativeMethods.GetKeyState(NativeConstants.VK_MENU) < 0;
        bool shift = NativeMethods.GetKeyState(NativeConstants.VK_SHIFT) < 0;
        bool win = NativeMethods.GetKeyState(NativeConstants.VK_LWIN) < 0
            || NativeMethods.GetKeyState(NativeConstants.VK_RWIN) < 0;
        return HotkeyMessages.TranslateKeyMessage(message, wParam, lParam, ctrl, alt, shift, win);
    }

    /// <summary>A short display label for key on the calling thread's current keyboard layout
    /// (B19). Never null or empty.</summary>
    /// <exception cref="ArgumentOutOfRangeException">key undefined.</exception>
    public static string KeyLabel(Key key)
    {
        if (!Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "Undefined key.");
        }

        return KeyLabel(key, NativeMethods.GetKeyboardLayout(0));
    }

    internal static string KeyLabel(Key key, nint hkl)
    {
        if (!KeyMap.IsLayoutAssigned(key))
        {
            return KeyMap.DefaultLabel(key);
        }

        // SPEC-ASSUMPTION A2: MapVirtualKeyExW(VK_TO_CHAR) is the label source; the dead-key high bit is ignored.
        uint r = NativeMethods.MapVirtualKeyExW((uint)KeyMap.ToVk(key), NativeConstants.MAPVK_VK_TO_CHAR, hkl);
        char ch = (char)(r & LowWordMask);
        if (ch < FirstPrintable)
        {
            return KeyMap.DefaultLabel(key);
        }

        return char.ToUpperInvariant(ch).ToString();
    }

    internal static bool IsOnLayout(Key key, nint hkl)
    {
        // SPEC-ASSUMPTION A3: only the 11 punctuation keys can be off the layout.
        return !KeyMap.IsLayoutAssigned(key)
            || NativeMethods.MapVirtualKeyExW((uint)KeyMap.ToVk(key), NativeConstants.MAPVK_VK_TO_VSC_EX, hkl) != 0;
    }
}
