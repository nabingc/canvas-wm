namespace CanvasWm.Core.Shortcuts;

/// <summary>What an edit did.</summary>
public enum EditOutcome
{
    /// <summary>Combo or set stored (possibly equal to before).</summary>
    Stored = 0,
    /// <summary>Stored; OtherAction lost this combo and is now unbound.</summary>
    Moved = 1,
    /// <summary>The action or set is now unbound.</summary>
    Cleared = 2,
    /// <summary>Recording cancelled (Escape); nothing changed.</summary>
    Cancelled = 3,
    /// <summary>Refused: no Ctrl, Alt or Win.</summary>
    NeedsModifier = 4,
    /// <summary>Refused: key cannot be bound (Escape).</summary>
    NotBindable = 5,
    /// <summary>Refused: a view set must be recorded with a digit 1-9.</summary>
    NotADigit = 6,
    /// <summary>Refused: the action combo is digit 1-9 with a view set's modifiers (OtherViews says which).</summary>
    ConflictsWithViews = 7,
    /// <summary>Refused: OtherAction is bound to a digit 1-9 with these modifiers.</summary>
    ConflictsWithAction = 8,
    /// <summary>Refused: the set would equal the other view set (OtherViews).</summary>
    RecallEqualsSave = 9,
}

/// <summary>Result of an edit.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Bindings">The result; the input instance's value when refused or cancelled.</param>
/// <param name="OtherAction">Moved: the action that lost the combo. ConflictsWithAction: the blocking action. Otherwise null.</param>
/// <param name="OtherViews">ConflictsWithViews / RecallEqualsSave: the other set. Otherwise null.</param>
public sealed record EditResult(EditOutcome Outcome, ShortcutBindings Bindings,
                                ShortcutAction? OtherAction, ViewSet? OtherViews)
{
    /// <summary>Outcome is Stored, Moved or Cleared.</summary>
    public bool Accepted => Outcome is EditOutcome.Stored or EditOutcome.Moved or EditOutcome.Cleared;
}

/// <summary>Editing rules as pure functions.</summary>
public static class ShortcutEditor
{
    private static EditResult R(EditOutcome o, ShortcutBindings b, ShortcutAction? a = null, ViewSet? v = null) =>
        new(o, b, a, v);

    private static bool IsDeleteKey(Key key, Modifiers modifiers) =>
        modifiers == Modifiers.None && key is Key.Backspace or Key.Delete;

    /// <summary>A key press recorded while editing an action.</summary>
    public static EditResult RecordForAction(ShortcutBindings bindings, ShortcutAction action, Key key, Modifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Guard.Action(action, nameof(action));
        Guard.Key(key, nameof(key));
        Guard.Mods(modifiers, nameof(modifiers));
        // SPEC-ASSUMPTION A8: Escape cancels with any modifiers and can never be bound.
        if (key == Key.Escape) return R(EditOutcome.Cancelled, bindings);
        if (IsDeleteKey(key, modifiers)) return ClearAction(bindings, action);
        if (!ShortcutRules.HasRequiredModifier(modifiers)) return R(EditOutcome.NeedsModifier, bindings);
        return SetAction(bindings, action, new KeyCombo(key, modifiers));
    }

    /// <summary>A key press recorded while editing a view set.</summary>
    public static EditResult RecordForViews(ShortcutBindings bindings, ViewSet set, Key key, Modifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Guard.Set(set, nameof(set));
        Guard.Key(key, nameof(key));
        Guard.Mods(modifiers, nameof(modifiers));
        if (key == Key.Escape) return R(EditOutcome.Cancelled, bindings);
        if (IsDeleteKey(key, modifiers)) return ClearViews(bindings, set);
        if (!ShortcutRules.HasRequiredModifier(modifiers)) return R(EditOutcome.NeedsModifier, bindings);
        if (ShortcutRules.SlotOf(key) is null) return R(EditOutcome.NotADigit, bindings);
        return SetViewModifiers(bindings, set, modifiers);
    }

    /// <summary>Set an action's combo, applying the editing rules.</summary>
    public static EditResult SetAction(ShortcutBindings bindings, ShortcutAction action, KeyCombo combo)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Guard.Action(action, nameof(action));
        Guard.Combo(combo, nameof(combo));
        if (combo.Key == Key.Escape) return R(EditOutcome.NotBindable, bindings);
        if (!ShortcutRules.HasRequiredModifier(combo.Modifiers)) return R(EditOutcome.NeedsModifier, bindings);
        if (ShortcutRules.SlotOf(combo.Key) is not null)
        {
            if (bindings.RecallViewModifiers == combo.Modifiers)
                return R(EditOutcome.ConflictsWithViews, bindings, null, ViewSet.Recall);
            if (bindings.SaveViewModifiers == combo.Modifiers)
                return R(EditOutcome.ConflictsWithViews, bindings, null, ViewSet.Save);
        }
        // SPEC-ASSUMPTION A12: reserved combos are not refused here; the settings UI warns via ReservedCombos.Find.
        for (int i = 0; i < ShortcutCatalog.Actions.Count; i++)
        {
            var other = (ShortcutAction)i;
            if (other != action && bindings[other] == combo)
            {
                ShortcutBindings moved = bindings.WithAction(other, null).WithAction(action, combo);
                return R(EditOutcome.Moved, moved, other);
            }
        }
        return R(EditOutcome.Stored, bindings.WithAction(action, combo));
    }

    /// <summary>Set a view set, applying the editing rules.</summary>
    public static EditResult SetViewModifiers(ShortcutBindings bindings, ViewSet set, Modifiers modifiers)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Guard.Set(set, nameof(set));
        Guard.Mods(modifiers, nameof(modifiers));
        if (!ShortcutRules.HasRequiredModifier(modifiers)) return R(EditOutcome.NeedsModifier, bindings);
        ViewSet otherSet = set == ViewSet.Recall ? ViewSet.Save : ViewSet.Recall;
        if (bindings.ViewModifiers(otherSet) == modifiers)
            return R(EditOutcome.RecallEqualsSave, bindings, null, otherSet);
        for (int i = 0; i < ShortcutCatalog.Actions.Count; i++)
        {
            var a = (ShortcutAction)i;
            if (bindings[a] is KeyCombo c && ShortcutRules.SlotOf(c.Key) is not null && c.Modifiers == modifiers)
                return R(EditOutcome.ConflictsWithAction, bindings, a);
        }
        return R(EditOutcome.Stored, bindings.WithViewModifiers(set, modifiers));
    }

    /// <summary>Unbind an action.</summary>
    public static EditResult ClearAction(ShortcutBindings bindings, ShortcutAction action)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Guard.Action(action, nameof(action));
        return R(EditOutcome.Cleared, bindings.WithAction(action, null));
    }

    /// <summary>Unbind a view set (all nine bindings of that kind).</summary>
    public static EditResult ClearViews(ShortcutBindings bindings, ViewSet set)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Guard.Set(set, nameof(set));
        return R(EditOutcome.Cleared, bindings.WithViewModifiers(set, null));
    }
}
