namespace CanvasWm.Core.Shortcuts;

/// <summary>The 12 bindable actions. Raw values are fixed; this order is also the match order.</summary>
public enum ShortcutAction
{
    /// <summary>Show or hide the overview.</summary>
    Overview = 0,
    /// <summary>Zoom out one level.</summary>
    StepOut = 1,
    /// <summary>Zoom in one level.</summary>
    StepIn = 2,
    /// <summary>Move to the tile on the left.</summary>
    Left = 3,
    /// <summary>Move to the tile on the right.</summary>
    Right = 4,
    /// <summary>Move to the tile above.</summary>
    Up = 5,
    /// <summary>Move to the tile below.</summary>
    Down = 6,
    /// <summary>Go back.</summary>
    Back = 7,
    /// <summary>Go forward.</summary>
    Forward = 8,
    /// <summary>Toggle maximize.</summary>
    Maximize = 9,
    /// <summary>Toggle the desktop area.</summary>
    Desktop = 10,
    /// <summary>Split for a new window.</summary>
    Split = 11,
}
