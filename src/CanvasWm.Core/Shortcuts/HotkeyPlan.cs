namespace CanvasWm.Core.Shortcuts;

/// <summary>Outcome of asking the OS to own one combo as a system-wide hotkey.</summary>
public enum HotkeyStatus
{
    /// <summary>Registered; presses will be reported.</summary>
    Registered = 0,
    /// <summary>Registered, but the current keyboard layout has no key that produces this
    /// key's code, so it probably cannot be pressed. A warning, not an error.</summary>
    NotOnLayout = 1,
    /// <summary>Not registered: another program (or another registration) already owns the
    /// exact combo.</summary>
    Taken = 2,
    /// <summary>Not registered for any other reason; see HotkeyReport.ErrorCode.</summary>
    Failed = 3,
}

/// <summary>Per-combo result of a registration pass.</summary>
/// <param name="Combo">The combo.</param>
/// <param name="Status">The outcome.</param>
/// <param name="ErrorCode">The OS error code for Taken and Failed (may be 0 for Failed when
/// the OS gave none); 0 for Registered and NotOnLayout.</param>
public readonly record struct HotkeyReport(KeyCombo Combo, HotkeyStatus Status, int ErrorCode)
{
    /// <summary>True for Registered and NotOnLayout (the OS holds the combo for us).</summary>
    public bool IsActive => Status is HotkeyStatus.Registered or HotkeyStatus.NotOnLayout;
}

/// <summary>The OS calls needed to move from the registered set to the desired list.
/// Unregister every ToUnregister entry first, then register ToRegister in order.
/// Value equality of the lists is by reference; compare contents with SequenceEqual.</summary>
/// <param name="ToUnregister">Registered but not desired, sorted ascending by
/// ((int)Key, (int)Modifiers).</param>
/// <param name="ToRegister">Desired but not registered, distinct, in first-occurrence order
/// of the desired list.</param>
public sealed record HotkeyDiff(IReadOnlyList<KeyCombo> ToUnregister, IReadOnlyList<KeyCombo> ToRegister);

/// <summary>Pure planning of which combos should be OS hotkeys.</summary>
public static class HotkeyPlan
{
    /// <summary>The combos to register for a dispatch table: empty while recording; otherwise
    /// the distinct ShortcutRules.IsBindable combos of table, in table order (first occurrence
    /// wins, as in ShortcutDispatch.Match).</summary>
    /// <exception cref="ArgumentNullException">table is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">an entry's combo is invalid (also when
    /// recording).</exception>
    public static IReadOnlyList<KeyCombo> Desired(IReadOnlyList<DispatchEntry> table, bool recording)
    {
        ArgumentNullException.ThrowIfNull(table);
        for (int i = 0; i < table.Count; i++)
        {
            Guard.Combo(table[i].Combo, nameof(table));
        }

        var result = new List<KeyCombo>();
        if (recording)
        {
            return result.AsReadOnly();
        }

        var seen = new HashSet<KeyCombo>();
        for (int i = 0; i < table.Count; i++)
        {
            KeyCombo c = table[i].Combo;
            if (ShortcutRules.IsBindable(c) && seen.Add(c))
            {
                result.Add(c);
            }
        }

        return result.AsReadOnly();
    }

    /// <summary>Computes what to unregister and register. Duplicates in either argument are
    /// treated as one.</summary>
    /// <exception cref="ArgumentNullException">registered or desired is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">a combo in either argument is
    /// invalid.</exception>
    public static HotkeyDiff Diff(IEnumerable<KeyCombo> registered, IReadOnlyList<KeyCombo> desired)
    {
        ArgumentNullException.ThrowIfNull(registered);
        ArgumentNullException.ThrowIfNull(desired);
        var reg = new HashSet<KeyCombo>();
        foreach (KeyCombo c in registered)
        {
            Guard.Combo(c, nameof(registered));
            reg.Add(c);
        }

        var des = new List<KeyCombo>();
        var desSet = new HashSet<KeyCombo>();
        for (int i = 0; i < desired.Count; i++)
        {
            KeyCombo c = desired[i];
            Guard.Combo(c, nameof(desired));
            if (desSet.Add(c))
            {
                des.Add(c);
            }
        }

        var unreg = reg.Where(c => !desSet.Contains(c))
            .OrderBy(c => (int)c.Key).ThenBy(c => (int)c.Modifiers).ToList();
        var toReg = des.Where(c => !reg.Contains(c)).ToList();
        return new HotkeyDiff(unreg.AsReadOnly(), toReg.AsReadOnly());
    }
}
