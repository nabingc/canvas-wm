namespace CanvasWm.Core.Shortcuts;

/// <summary>A key plus an exact modifier set. Equality is exact on both.</summary>
public readonly record struct KeyCombo
{
    /// <summary>Creates a combo.</summary>
    /// <exception cref="ArgumentOutOfRangeException">key undefined, or modifiers outside All.</exception>
    public KeyCombo(Key key, Modifiers modifiers)
    {
        Guard.Key(key, nameof(key));
        Guard.Mods(modifiers, nameof(modifiers));
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>The key.</summary>
    public Key Key { get; }

    /// <summary>The exact modifier set.</summary>
    public Modifiers Modifiers { get; }
}
