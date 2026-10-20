namespace CanvasWm.Core.Shortcuts;

/// <summary>The full shortcut configuration. Immutable; value equality over all 12 action entries and
/// both view sets. Invariant: every stored combo is bindable and every stored view set has the required
/// modifier. Uniqueness is not an invariant (Parse may produce duplicates; the dispatcher resolves them
/// by order).</summary>
public sealed record ShortcutBindings
{
    private const int ActionCount = 12;

    private readonly KeyCombo?[] _actions;
    private readonly Modifiers? _recall;
    private readonly Modifiers? _save;

    private ShortcutBindings(KeyCombo?[] actions, Modifiers? recall, Modifiers? save)
    {
        _actions = actions;
        _recall = recall;
        _save = save;
    }

    /// <summary>All 12 actions unbound, both view sets unbound.</summary>
    public static ShortcutBindings Unbound { get; } = new(new KeyCombo?[ActionCount], null, null);

    /// <summary>The action's combo, or null when unbound.</summary>
    /// <exception cref="ArgumentOutOfRangeException">undefined action.</exception>
    public KeyCombo? this[ShortcutAction action]
    {
        get
        {
            Guard.Action(action, nameof(action));
            return _actions[(int)action];
        }
    }

    /// <summary>A copy with that action set (null = unbound). Applies no editing rule.</summary>
    /// <exception cref="ArgumentException">combo non-null and not bindable.</exception>
    /// <exception cref="ArgumentOutOfRangeException">undefined action, or invalid combo value.</exception>
    public ShortcutBindings WithAction(ShortcutAction action, KeyCombo? combo)
    {
        Guard.Action(action, nameof(action));
        if (combo is KeyCombo c && !ShortcutRules.IsBindable(c))
            throw new ArgumentException("Combo is not bindable.", nameof(combo));
        var copy = (KeyCombo?[])_actions.Clone();
        copy[(int)action] = combo;
        return new ShortcutBindings(copy, _recall, _save);
    }

    /// <summary>Modifiers for "go to view n" (digit n); null = no recall bindings.</summary>
    /// <exception cref="ArgumentException">init with a value lacking the required modifier.</exception>
    public Modifiers? RecallViewModifiers
    {
        get => _recall;
        init => _recall = CheckSet(value);
    }

    /// <summary>Modifiers for "save view n"; null = no save bindings.</summary>
    /// <exception cref="ArgumentException">init with a value lacking the required modifier.</exception>
    public Modifiers? SaveViewModifiers
    {
        get => _save;
        init => _save = CheckSet(value);
    }

    /// <summary>The modifiers of one view set, or null.</summary>
    /// <exception cref="ArgumentOutOfRangeException">undefined set.</exception>
    public Modifiers? ViewModifiers(ViewSet set)
    {
        Guard.Set(set, nameof(set));
        return set == ViewSet.Recall ? _recall : _save;
    }

    /// <summary>A copy with that set changed. Applies no editing rule.</summary>
    /// <exception cref="ArgumentException">non-null value lacking the required modifier.</exception>
    public ShortcutBindings WithViewModifiers(ViewSet set, Modifiers? modifiers)
    {
        Guard.Set(set, nameof(set));
        return set == ViewSet.Recall
            ? this with { RecallViewModifiers = modifiers }
            : this with { SaveViewModifiers = modifiers };
    }

    /// <summary>First action (ShortcutAction order) bound to exactly this combo, or null.</summary>
    public ShortcutAction? ActionBoundTo(KeyCombo combo)
    {
        Guard.Combo(combo, nameof(combo));
        for (int i = 0; i < ActionCount; i++)
        {
            if (_actions[i] == combo) return (ShortcutAction)i;
        }
        return null;
    }

    /// <summary>Value equality over all action entries and both view sets.</summary>
    public bool Equals(ShortcutBindings? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_recall != other._recall || _save != other._save) return false;
        for (int i = 0; i < ActionCount; i++)
        {
            if (_actions[i] != other._actions[i]) return false;
        }
        return true;
    }

    /// <summary>Hash consistent with <see cref="Equals(ShortcutBindings?)"/>.</summary>
    public override int GetHashCode()
    {
        var h = new HashCode();
        for (int i = 0; i < ActionCount; i++) h.Add(_actions[i]);
        h.Add(_recall);
        h.Add(_save);
        return h.ToHashCode();
    }

    private static Modifiers? CheckSet(Modifiers? value)
    {
        if (value is Modifiers m)
        {
            Guard.Mods(m, nameof(value));
            if (!ShortcutRules.HasRequiredModifier(m))
                throw new ArgumentException("A view set needs Ctrl, Alt or Win.", nameof(value));
        }
        return value;
    }
}
