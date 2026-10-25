namespace CanvasWm.Core.Shortcuts;

/// <summary>A physical key, platform-neutral. Raw values are fixed (persisted by name; the Win32 layer maps
/// each member to a virtual-key code). There is no member 0. Modifier keys are not members.</summary>
// SPEC-ASSUMPTION A5: numeric keypad, PrintScreen, Pause and media keys are excluded.
public enum Key
{
    /// <summary>The letter A key.</summary>
    A = 1,
    /// <summary>The letter B key.</summary>
    B = 2,
    /// <summary>The letter C key.</summary>
    C = 3,
    /// <summary>The letter D key.</summary>
    D = 4,
    /// <summary>The letter E key.</summary>
    E = 5,
    /// <summary>The letter F key.</summary>
    F = 6,
    /// <summary>The letter G key.</summary>
    G = 7,
    /// <summary>The letter H key.</summary>
    H = 8,
    /// <summary>The letter I key.</summary>
    I = 9,
    /// <summary>The letter J key.</summary>
    J = 10,
    /// <summary>The letter K key.</summary>
    K = 11,
    /// <summary>The letter L key.</summary>
    L = 12,
    /// <summary>The letter M key.</summary>
    M = 13,
    /// <summary>The letter N key.</summary>
    N = 14,
    /// <summary>The letter O key.</summary>
    O = 15,
    /// <summary>The letter P key.</summary>
    P = 16,
    /// <summary>The letter Q key.</summary>
    Q = 17,
    /// <summary>The letter R key.</summary>
    R = 18,
    /// <summary>The letter S key.</summary>
    S = 19,
    /// <summary>The letter T key.</summary>
    T = 20,
    /// <summary>The letter U key.</summary>
    U = 21,
    /// <summary>The letter V key.</summary>
    V = 22,
    /// <summary>The letter W key.</summary>
    W = 23,
    /// <summary>The letter X key.</summary>
    X = 24,
    /// <summary>The letter Y key.</summary>
    Y = 25,
    /// <summary>The letter Z key.</summary>
    Z = 26,
    /// <summary>Top-row digit 0 (not the numeric keypad).</summary>
    D0 = 30,
    /// <summary>Top-row digit 1 (not the numeric keypad).</summary>
    D1 = 31,
    /// <summary>Top-row digit 2 (not the numeric keypad).</summary>
    D2 = 32,
    /// <summary>Top-row digit 3 (not the numeric keypad).</summary>
    D3 = 33,
    /// <summary>Top-row digit 4 (not the numeric keypad).</summary>
    D4 = 34,
    /// <summary>Top-row digit 5 (not the numeric keypad).</summary>
    D5 = 35,
    /// <summary>Top-row digit 6 (not the numeric keypad).</summary>
    D6 = 36,
    /// <summary>Top-row digit 7 (not the numeric keypad).</summary>
    D7 = 37,
    /// <summary>Top-row digit 8 (not the numeric keypad).</summary>
    D8 = 38,
    /// <summary>Top-row digit 9 (not the numeric keypad).</summary>
    D9 = 39,
    /// <summary>Function key F1.</summary>
    F1 = 40,
    /// <summary>Function key F2.</summary>
    F2 = 41,
    /// <summary>Function key F3.</summary>
    F3 = 42,
    /// <summary>Function key F4.</summary>
    F4 = 43,
    /// <summary>Function key F5.</summary>
    F5 = 44,
    /// <summary>Function key F6.</summary>
    F6 = 45,
    /// <summary>Function key F7.</summary>
    F7 = 46,
    /// <summary>Function key F8.</summary>
    F8 = 47,
    /// <summary>Function key F9.</summary>
    F9 = 48,
    /// <summary>Function key F10.</summary>
    F10 = 49,
    /// <summary>Function key F11.</summary>
    F11 = 50,
    /// <summary>Function key F12.</summary>
    F12 = 51,
    /// <summary>Function key F13.</summary>
    F13 = 52,
    /// <summary>Function key F14.</summary>
    F14 = 53,
    /// <summary>Function key F15.</summary>
    F15 = 54,
    /// <summary>Function key F16.</summary>
    F16 = 55,
    /// <summary>Function key F17.</summary>
    F17 = 56,
    /// <summary>Function key F18.</summary>
    F18 = 57,
    /// <summary>Function key F19.</summary>
    F19 = 58,
    /// <summary>Function key F20.</summary>
    F20 = 59,
    /// <summary>Function key F21.</summary>
    F21 = 60,
    /// <summary>Function key F22.</summary>
    F22 = 61,
    /// <summary>Function key F23.</summary>
    F23 = 62,
    /// <summary>Function key F24.</summary>
    F24 = 63,
    /// <summary>The Left key.</summary>
    Left = 70,
    /// <summary>The Right key.</summary>
    Right = 71,
    /// <summary>The Up key.</summary>
    Up = 72,
    /// <summary>The Down key.</summary>
    Down = 73,
    /// <summary>The Space key.</summary>
    Space = 80,
    /// <summary>The Enter key.</summary>
    Enter = 81,
    /// <summary>The Tab key.</summary>
    Tab = 82,
    /// <summary>Deletes backwards (the Delete role in the editing rules).</summary>
    Backspace = 83,
    /// <summary>Deletes forwards (the Forward-Delete role).</summary>
    Delete = 84,
    /// <summary>The Escape key.</summary>
    Escape = 85,
    /// <summary>The Insert key.</summary>
    Insert = 86,
    /// <summary>The Home key.</summary>
    Home = 87,
    /// <summary>The End key.</summary>
    End = 88,
    /// <summary>The PageUp key.</summary>
    PageUp = 89,
    /// <summary>The PageDown key.</summary>
    PageDown = 90,
    /// <summary>The punctuation key that types "-" on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    Minus = 100,
    /// <summary>The punctuation key that types "=" on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    Equals = 101,
    /// <summary>The punctuation key that types "[" on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    LeftBracket = 102,
    /// <summary>The punctuation key that types "]" on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    RightBracket = 103,
    /// <summary>The punctuation key that types "\" on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    Backslash = 104,
    /// <summary>The punctuation key that types ";" on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    Semicolon = 105,
    /// <summary>The punctuation key that types "'" on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    Quote = 106,
    /// <summary>The punctuation key that types "," on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    Comma = 107,
    /// <summary>The punctuation key that types "." on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    Period = 108,
    /// <summary>The punctuation key that types "/" on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    Slash = 109,
    /// <summary>The punctuation key that types "`" on the US layout. It is identified by the key code the layout gives it, not by its position: on other layouts it may be a different physical key, type a different character, or be absent.</summary>
    Backquote = 110,
}
