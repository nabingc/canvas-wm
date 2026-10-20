namespace CanvasWm.Core.Shortcuts;

/// <summary>Static description of one action.</summary>
/// <param name="Action">The action.</param>
/// <param name="Id">Stable persisted id.</param>
/// <param name="Description">Short human description.</param>
/// <param name="Command">The command the action stands for.</param>
/// <param name="Target">Which engine receives the command.</param>
/// <param name="RepeatsOnHold">Always false.</param>
public sealed record ShortcutActionInfo(
    ShortcutAction Action, string Id, string Description,
    ShortcutCommand Command, TargetEngine Target, bool RepeatsOnHold);

/// <summary>The two saved-view modifier sets.</summary>
public enum ViewSet
{
    /// <summary>Recalls a view.</summary>
    Recall = 0,
    /// <summary>Saves a view.</summary>
    Save = 1,
}

/// <summary>The fixed catalogue of the 12 actions.</summary>
public static class ShortcutCatalog
{
    /// <summary>Number of saved-view slots (9).</summary>
    public const int ViewSlots = 9;

    private static ShortcutActionInfo I(ShortcutAction a, string id, string text, TargetEngine t = TargetEngine.Active) =>
        new(a, id, text, ShortcutCommand.ForAction(a), t, false);

    /// <summary>The 12 actions in ShortcutAction order.</summary>
    public static IReadOnlyList<ShortcutActionInfo> Actions { get; } = Array.AsReadOnly(new[]
    {
        I(ShortcutAction.Overview, "overview", "Show or hide the overview of all windows"),
        I(ShortcutAction.StepOut, "step-out", "Zoom out one level"),
        I(ShortcutAction.StepIn, "step-in", "Zoom in one level", TargetEngine.PointerThenActive),
        I(ShortcutAction.Left, "left", "Go to the tile on the left"),
        I(ShortcutAction.Right, "right", "Go to the tile on the right"),
        I(ShortcutAction.Up, "up", "Go to the tile above"),
        I(ShortcutAction.Down, "down", "Go to the tile below"),
        I(ShortcutAction.Back, "back", "Go back to the previous view"),
        I(ShortcutAction.Forward, "forward", "Go forward to the next view"),
        I(ShortcutAction.Maximize, "maximize", "Show the focused window alone, or undo"),
        I(ShortcutAction.Desktop, "desktop", "Show or hide the empty desktop area"),
        // SPEC-ASSUMPTION A9: the argument of the engine split call is decided by #29, not here.
        I(ShortcutAction.Split, "split", "Open a new Explorer window beside the current tile", TargetEngine.Pointer),
    });

    /// <summary>The description of one action.</summary>
    /// <exception cref="ArgumentOutOfRangeException">undefined action.</exception>
    public static ShortcutActionInfo Info(ShortcutAction action)
    {
        Guard.Action(action, nameof(action));
        return Actions[(int)action];
    }

    /// <summary>Exact, ordinal id lookup.</summary>
    public static bool TryParseId(string id, out ShortcutAction action)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (ShortcutActionInfo info in Actions)
        {
            if (string.Equals(info.Id, id, StringComparison.Ordinal))
            {
                action = info.Action;
                return true;
            }
        }
        action = default;
        return false;
    }

    /// <summary>"Go to saved view n" / "Save the current view as view n".</summary>
    /// <exception cref="ArgumentOutOfRangeException">slot outside 1..9, or undefined set.</exception>
    public static string DescribeView(ViewSet set, int slot)
    {
        Guard.Set(set, nameof(set));
        Guard.Slot(slot, nameof(slot));
        return set == ViewSet.Recall
            ? $"Go to saved view {slot}"
            : $"Save the current view as view {slot}";
    }
}
