using CanvasWm.Core.Shortcuts;

namespace CanvasWm.Win32.Hotkeys;

/// <summary>Combo to hotkey id mapping (B22).</summary>
internal static class HotkeyIds
{
    /// <summary>The highest id an application may use.</summary>
    internal const int MaxApplicationId = 0xBFFF;

    private const int Stride = 0x10;

    /// <summary>(int)Key * 16 + (int)Modifiers.</summary>
    internal static int For(KeyCombo combo)
    {
        if (!Enum.IsDefined(combo.Key) || ((int)combo.Modifiers & ~(int)Modifiers.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(combo), combo, "Invalid key combo.");
        }

        return ((int)combo.Key * Stride) + (int)combo.Modifiers;
    }

    /// <summary>Inverse of For.</summary>
    internal static bool TryDecode(int id, out KeyCombo combo)
    {
        combo = default;
        if (id < 0)
        {
            return false;
        }

        var key = (Key)(id / Stride);
        if (!Enum.IsDefined(key))
        {
            return false;
        }

        combo = new KeyCombo(key, (Modifiers)(id % Stride));
        return true;
    }
}
