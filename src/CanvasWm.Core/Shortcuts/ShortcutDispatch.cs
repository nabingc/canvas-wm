using System.Collections.Frozen;

namespace CanvasWm.Core.Shortcuts;

/// <summary>One entry of the dispatch list.</summary>
/// <param name="Combo">The exact combo.</param>
/// <param name="Command">The command it runs.</param>
public readonly record struct DispatchEntry(KeyCombo Combo, ShortcutCommand Command);

/// <summary>Whether a key event is swallowed.</summary>
public enum KeyVerdict
{
    /// <summary>Let the key through to the system.</summary>
    PassThrough = 0,
    /// <summary>Swallow the key.</summary>
    Consume = 1,
}

/// <summary>Result of a key-down.</summary>
/// <param name="Verdict">Pass through or consume.</param>
/// <param name="Command">The command to run, or null (nothing to run, even when consumed).</param>
public readonly record struct KeyDownOutcome(KeyVerdict Verdict, ShortcutCommand? Command);

/// <summary>Runtime state of the dispatcher. Immutable; transitions return new instances.</summary>
public sealed record DispatchState
{
    private static readonly IReadOnlySet<Key> EmptyHeld = FrozenSet<Key>.Empty;

    private DispatchState(bool recording, IReadOnlySet<Key> held)
    {
        Recording = recording;
        Held = held;
    }

    /// <summary>Not recording, nothing held.</summary>
    public static DispatchState Initial { get; } = new(false, EmptyHeld);

    /// <summary>True while the settings UI records a shortcut (matching suspended).</summary>
    public bool Recording { get; }

    /// <summary>Keys whose key-down was consumed and whose key-up has not arrived yet.</summary>
    public IReadOnlySet<Key> Held { get; }

    /// <summary>A copy with the recording flag set; Held is kept.</summary>
    public DispatchState WithRecording(bool recording) => new(recording, Held);

    internal DispatchState WithHeld(IEnumerable<Key> held) => new(Recording, held.ToFrozenSet());

    /// <summary>Value equality over Recording and Held contents.</summary>
    public bool Equals(DispatchState? other) =>
        other is not null && Recording == other.Recording && Held.SetEquals(other.Held);

    /// <summary>Hash consistent with Equals.</summary>
    public override int GetHashCode()
    {
        int h = 0;
        foreach (Key k in Held) h ^= (int)k * 397;
        return HashCode.Combine(Recording, h);
    }
}

/// <summary>Pure matching of key events against the bindings.</summary>
public static class ShortcutDispatch
{
    /// <summary>Ordered dispatch list: the bound actions, then for slot 1..9: recall, save.</summary>
    public static IReadOnlyList<DispatchEntry> Build(ShortcutBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var list = new List<DispatchEntry>();
        foreach (ShortcutActionInfo info in ShortcutCatalog.Actions)
        {
            if (bindings[info.Action] is KeyCombo c) list.Add(new DispatchEntry(c, info.Command));
        }
        for (int n = 1; n <= ShortcutCatalog.ViewSlots; n++)
        {
            Key key = ShortcutRules.SlotKey(n);
            if (bindings.RecallViewModifiers is Modifiers r)
                list.Add(new DispatchEntry(new KeyCombo(key, r), ShortcutCommand.GoToView(n)));
            if (bindings.SaveViewModifiers is Modifiers s)
                list.Add(new DispatchEntry(new KeyCombo(key, s), ShortcutCommand.SaveView(n)));
        }
        return list.AsReadOnly();
    }

    /// <summary>Command of the first entry whose combo equals pressed exactly, or null.</summary>
    public static ShortcutCommand? Match(IReadOnlyList<DispatchEntry> table, KeyCombo pressed)
    {
        ArgumentNullException.ThrowIfNull(table);
        Guard.Combo(pressed, nameof(pressed));
        for (int i = 0; i < table.Count; i++)
        {
            if (table[i].Combo == pressed) return table[i].Command;
        }
        return null;
    }

    /// <summary>Handles a key-down.</summary>
    public static (DispatchState State, KeyDownOutcome Outcome) KeyDown(
        DispatchState state, IReadOnlyList<DispatchEntry> table, Key key, Modifiers modifiers, bool isRepeat)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(table);
        Guard.Key(key, nameof(key));
        Guard.Mods(modifiers, nameof(modifiers));
        var pass = new KeyDownOutcome(KeyVerdict.PassThrough, null);
        if (state.Recording) return (state, pass);
        ShortcutCommand? cmd = Match(table, new KeyCombo(key, modifiers));
        if (cmd is null) return (state, pass);
        // SPEC-ASSUMPTION A6: a repeat is decided only by isRepeat, never by Held membership.
        DispatchState next = state.Held.Contains(key) ? state : state.WithHeld(state.Held.Append(key));
        return (next, new KeyDownOutcome(KeyVerdict.Consume, isRepeat ? null : cmd));
    }

    /// <summary>Handles a key-up.</summary>
    public static (DispatchState State, KeyVerdict Verdict) KeyUp(DispatchState state, Key key)
    {
        ArgumentNullException.ThrowIfNull(state);
        Guard.Key(key, nameof(key));
        if (!state.Held.Contains(key)) return (state, KeyVerdict.PassThrough);
        DispatchState next = state.WithHeld(state.Held.Where(k => k != key));
        // SPEC-ASSUMPTION A7: while recording, a key-up still drops the key from Held but passes through.
        return (next, state.Recording ? KeyVerdict.PassThrough : KeyVerdict.Consume);
    }
}
