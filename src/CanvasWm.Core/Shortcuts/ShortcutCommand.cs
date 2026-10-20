using CanvasWm.Core.Layout;

namespace CanvasWm.Core.Shortcuts;

/// <summary>What the app layer executes.</summary>
public enum CommandKind
{
    /// <summary>Toggle the overview.</summary>
    ToggleOverview = 0,
    /// <summary>Zoom out.</summary>
    ZoomOut = 1,
    /// <summary>Zoom in.</summary>
    ZoomIn = 2,
    /// <summary>Move toward an edge.</summary>
    Move = 3,
    /// <summary>Go back.</summary>
    Back = 4,
    /// <summary>Go forward.</summary>
    Forward = 5,
    /// <summary>Toggle maximize.</summary>
    ToggleMaximize = 6,
    /// <summary>Toggle the desktop area.</summary>
    ToggleDesktopHidden = 7,
    /// <summary>Split for a new window.</summary>
    SplitForNewWindow = 8,
    /// <summary>Recall a saved view.</summary>
    GoToView = 9,
    /// <summary>Save the current view.</summary>
    SaveView = 10,
}

/// <summary>Which engine a command goes to.</summary>
public enum TargetEngine
{
    /// <summary>The engine of the active (display, desktop).</summary>
    Active = 0,
    /// <summary>The engine under the pointer when the step-toward-pointer preference is on and such
    /// an engine exists, else the active engine (StepIn only).</summary>
    PointerThenActive = 1,
    /// <summary>The engine under the pointer only; none means nothing (Split only).</summary>
    Pointer = 2,
}

/// <summary>One command produced by a match. Create only through the factories.</summary>
public readonly record struct ShortcutCommand
{
    private ShortcutCommand(CommandKind kind, ShortcutAction? action, Edge? toward, int? slot)
    {
        Kind = kind;
        Action = action;
        Toward = toward;
        Slot = slot;
    }

    /// <summary>The command kind.</summary>
    public CommandKind Kind { get; }

    /// <summary>The action, for the 12 action commands; null for view commands.</summary>
    public ShortcutAction? Action { get; }

    /// <summary>Direction for Kind == Move; otherwise null.</summary>
    public Edge? Toward { get; }

    /// <summary>1..9 for GoToView / SaveView; otherwise null.</summary>
    public int? Slot { get; }

    /// <summary>The command of an action.</summary>
    /// <exception cref="ArgumentOutOfRangeException">undefined action.</exception>
    public static ShortcutCommand ForAction(ShortcutAction action)
    {
        Guard.Action(action, nameof(action));
        return action switch
        {
            ShortcutAction.Overview => new(CommandKind.ToggleOverview, action, null, null),
            ShortcutAction.StepOut => new(CommandKind.ZoomOut, action, null, null),
            ShortcutAction.StepIn => new(CommandKind.ZoomIn, action, null, null),
            ShortcutAction.Left => new(CommandKind.Move, action, Edge.Left, null),
            ShortcutAction.Right => new(CommandKind.Move, action, Edge.Right, null),
            ShortcutAction.Up => new(CommandKind.Move, action, Edge.Top, null),
            ShortcutAction.Down => new(CommandKind.Move, action, Edge.Bottom, null),
            ShortcutAction.Back => new(CommandKind.Back, action, null, null),
            ShortcutAction.Forward => new(CommandKind.Forward, action, null, null),
            ShortcutAction.Maximize => new(CommandKind.ToggleMaximize, action, null, null),
            ShortcutAction.Desktop => new(CommandKind.ToggleDesktopHidden, action, null, null),
            _ => new(CommandKind.SplitForNewWindow, action, null, null),
        };
    }

    /// <summary>The recall-view command for a slot.</summary>
    /// <exception cref="ArgumentOutOfRangeException">slot outside 1..9.</exception>
    public static ShortcutCommand GoToView(int slot)
    {
        Guard.Slot(slot, nameof(slot));
        return new(CommandKind.GoToView, null, null, slot);
    }

    /// <summary>The save-view command for a slot.</summary>
    /// <exception cref="ArgumentOutOfRangeException">slot outside 1..9.</exception>
    public static ShortcutCommand SaveView(int slot)
    {
        Guard.Slot(slot, nameof(slot));
        return new(CommandKind.SaveView, null, null, slot);
    }
}
