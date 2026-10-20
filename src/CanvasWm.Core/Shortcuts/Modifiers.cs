namespace CanvasWm.Core.Shortcuts;

/// <summary>An exact modifier set. Left/right variants, Caps Lock and Fn are not represented.
/// Mapping from the studied platform: Control to Ctrl, Option to Alt, Command to Win.</summary>
[Flags]
public enum Modifiers
{
    /// <summary>No modifier.</summary>
    None = 0,
    /// <summary>Control.</summary>
    Ctrl = 1,
    /// <summary>Alt.</summary>
    Alt = 2,
    /// <summary>Shift.</summary>
    Shift = 4,
    /// <summary>The Windows key.</summary>
    Win = 8,
    /// <summary>Ctrl | Alt | Shift | Win (15).</summary>
    All = 15,
}
