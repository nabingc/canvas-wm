namespace CanvasWm.Core.Shortcuts;

/// <summary>How firmly Windows owns a combo.</summary>
public enum ReservedKind
{
    /// <summary>Windows (or a Windows component that is on by default) always owns it.</summary>
    Blocked = 0,
    /// <summary>Owned only while a Windows feature is open or running, or by some graphics drivers.</summary>
    Conditional = 1,
}

/// <summary>One combo that Windows reserves.</summary>
/// <param name="Combo">The combo.</param>
/// <param name="Kind">How firmly it is owned.</param>
/// <param name="Owner">Our own short note on what Windows does with it.</param>
/// <param name="NeedsConfirmation">True when the fact is believed but not yet checked on a Windows PC.</param>
public sealed record ReservedCombo(KeyCombo Combo, ReservedKind Kind, string Owner, bool NeedsConfirmation);

/// <summary>Combos the defaults must avoid. Not exhaustive; data only.</summary>
public static class ReservedCombos
{
    private const Modifiers W = Modifiers.Win;
    private const Modifiers A = Modifiers.Alt;
    private const Modifiers C = Modifiers.Ctrl;
    private const Modifiers S = Modifiers.Shift;

    /// <summary>The reserved combos in table order; combos are unique.</summary>
    public static IReadOnlyList<ReservedCombo> All { get; } = Build();

    /// <summary>The entry for this exact combo, or null.</summary>
    public static ReservedCombo? Find(KeyCombo combo)
    {
        Guard.Combo(combo, nameof(combo));
        foreach (ReservedCombo r in All)
        {
            if (r.Combo == combo) return r;
        }
        return null;
    }

    private static IReadOnlyList<ReservedCombo> Build()
    {
        var list = new List<ReservedCombo>(84);
        void Add(Key k, Modifiers m, ReservedKind kind, string owner, bool nc) =>
            list.Add(new ReservedCombo(new KeyCombo(k, m), kind, owner, nc));
        const ReservedKind B = ReservedKind.Blocked;
        const ReservedKind Cd = ReservedKind.Conditional;

        Add(Key.L, W, B, "Lock the PC", false);
        Add(Key.Delete, C | A, B, "Secure sign-in screen", false);
        Add(Key.Escape, C | S, B, "Task Manager", false);
        Add(Key.Escape, C, B, "Start menu", false);
        Add(Key.Tab, A, B, "Switch windows", false);
        Add(Key.Tab, C | A, B, "Persistent window switcher", false);
        Add(Key.F4, A, B, "Close window", false);
        Add(Key.Space, A, B, "Window menu", false);
        Add(Key.D, W, B, "Show desktop", false);
        Add(Key.Tab, W, B, "Task view", false);
        Add(Key.Space, W, B, "Switch input language", false);
        foreach (Key k in new[] { Key.Left, Key.Right, Key.Up, Key.Down }) Add(k, W, B, "Snap", false);
        foreach (Key k in new[] { Key.Left, Key.Right, Key.Up, Key.Down }) Add(k, W | S, B, "Move to monitor or stretch", false);
        Add(Key.Left, W | C, B, "Virtual desktops", false);
        Add(Key.Right, W | C, B, "Virtual desktops", false);
        Add(Key.D, W | C, B, "Virtual desktops", false);
        for (int i = 0; i <= 9; i++) Add(Key.D0 + i, W, B, $"Taskbar app {i}", false);
        for (int i = 0; i <= 9; i++) Add(Key.D0 + i, W | S, B, $"New instance of taskbar app {i}", false);
        for (int i = 0; i <= 9; i++) Add(Key.D0 + i, W | C, B, $"Last window of taskbar app {i}", false);
        for (int i = 0; i <= 9; i++) Add(Key.D0 + i, W | A, B, $"Jump list of taskbar app {i}", true);
        Add(Key.Up, W | A, B, "Snap to top half", true);
        Add(Key.Down, W | A, B, "Snap to bottom half", true);
        Add(Key.B, W | A, B, "Toggle HDR", true);
        Add(Key.D, W | A, B, "Show taskbar date and time", true);
        Add(Key.K, W | A, B, "Mute microphone in calls", true);
        Add(Key.R, W | A, B, "Game Bar: start or stop recording", true);
        Add(Key.G, W | A, B, "Game Bar: record recent play", true);
        Add(Key.T, W | A, B, "Game Bar: recording timer", true);
        Add(Key.M, W | A, B, "Game Bar: recording microphone", true);
        Add(Key.H, W | A, Cd, "Voice typing: focus keyboard", true);
        Add(Key.Enter, W | A, Cd, "Taskbar item settings", true);
        Add(Key.Space, C | A, Cd, "Magnifier preview (while running)", true);
        foreach (Key k in new[] { Key.D, Key.F, Key.I, Key.L, Key.M, Key.R })
            Add(k, C | A, Cd, "Magnifier views and options (while running)", true);
        foreach (Key k in new[] { Key.Left, Key.Right, Key.Up, Key.Down })
            Add(k, C | A, Cd, "Magnifier pan (while running); display rotation on some graphics drivers", true);
        return list.AsReadOnly();
    }
}
