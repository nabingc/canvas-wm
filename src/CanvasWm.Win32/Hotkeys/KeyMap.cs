using CanvasWm.Core.Shortcuts;

namespace CanvasWm.Win32.Hotkeys;

/// <summary>Key to virtual-key table (B20) and fixed labels (B19). Pure.</summary>
internal static class KeyMap
{
    /// <summary>Number of rows in the table.</summary>
    internal const int Count = 86;

    private static readonly Dictionary<Key, int> ToVkMap = Build();
    private static readonly Dictionary<int, Key> FromVkMap = ToVkMap.ToDictionary(p => p.Value, p => p.Key);

    private static Dictionary<Key, int> Build()
    {
        var map = new Dictionary<Key, int>();
        for (int i = 0; i < 26; i++) { map[Key.A + i] = 0x41 + i; }
        for (int i = 0; i < 10; i++) { map[Key.D0 + i] = 0x30 + i; }
        for (int i = 0; i < 24; i++) { map[Key.F1 + i] = 0x70 + i; }
        map[Key.Left] = 0x25;
        map[Key.Up] = 0x26;
        map[Key.Right] = 0x27;
        map[Key.Down] = 0x28;
        map[Key.Space] = 0x20;
        map[Key.Enter] = 0x0D;
        map[Key.Tab] = 0x09;
        map[Key.Backspace] = 0x08;
        map[Key.Delete] = 0x2E;
        map[Key.Escape] = 0x1B;
        map[Key.Insert] = 0x2D;
        map[Key.Home] = 0x24;
        map[Key.End] = 0x23;
        map[Key.PageUp] = 0x21;
        map[Key.PageDown] = 0x22;
        map[Key.Minus] = 0xBD;
        map[Key.Equals] = 0xBB;
        map[Key.LeftBracket] = 0xDB;
        map[Key.RightBracket] = 0xDD;
        map[Key.Backslash] = 0xDC;
        map[Key.Semicolon] = 0xBA;
        map[Key.Quote] = 0xDE;
        map[Key.Comma] = 0xBC;
        map[Key.Period] = 0xBE;
        map[Key.Slash] = 0xBF;
        map[Key.Backquote] = 0xC0;
        return map;
    }

    private static void Check(Key key)
    {
        if (!Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key), key, "Undefined key.");
        }
    }

    /// <summary>The virtual-key code of a key.</summary>
    internal static int ToVk(Key key)
    {
        Check(key);
        return ToVkMap[key];
    }

    /// <summary>Reverse of ToVk; false for every code not in the table.</summary>
    internal static bool TryFromVk(int vk, out Key key)
    {
        return FromVkMap.TryGetValue(vk, out key);
    }

    /// <summary>True exactly for the 11 punctuation keys.</summary>
    internal static bool IsLayoutAssigned(Key key)
    {
        Check(key);
        return key >= Key.Minus && key <= Key.Backquote;
    }

    /// <summary>Fixed label (US character for punctuation).</summary>
    internal static string DefaultLabel(Key key)
    {
        Check(key);
        if (key >= Key.A && key <= Key.Z) { return ((char)('A' + (key - Key.A))).ToString(); }
        if (key >= Key.D0 && key <= Key.D9) { return ((char)('0' + (key - Key.D0))).ToString(); }
        if (key >= Key.F1 && key <= Key.F24) { return "F" + (1 + (key - Key.F1)); }
        return key switch
        {
            Key.Minus => "-",
            Key.Equals => "=",
            Key.LeftBracket => "[",
            Key.RightBracket => "]",
            Key.Backslash => "\\",
            Key.Semicolon => ";",
            Key.Quote => "'",
            Key.Comma => ",",
            Key.Period => ".",
            Key.Slash => "/",
            Key.Backquote => "`",
            _ => key.ToString(),
        };
    }
}
