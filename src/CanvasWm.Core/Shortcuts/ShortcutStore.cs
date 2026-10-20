using System.Globalization;

namespace CanvasWm.Core.Shortcuts;

/// <summary>Pure format/parse of the shortcut configuration to a flat string dictionary.</summary>
public static class ShortcutStore
{
    /// <summary>Key of the format version entry.</summary>
    public const string VersionKey = "hotkeys.version";

    /// <summary>The current format version.</summary>
    public const string CurrentVersion = "1";

    /// <summary>Key of the recall view set.</summary>
    public const string RecallViewsKey = "hotkeys.views.recall";

    /// <summary>Key of the save view set.</summary>
    public const string SaveViewsKey = "hotkeys.views.save";

    /// <summary>The value that means unbound.</summary>
    public const string UnboundValue = "none";

    private static readonly Dictionary<string, Key> KeysByName = BuildKeyNames();

    private static Dictionary<string, Key> BuildKeyNames()
    {
        var d = new Dictionary<string, Key>(StringComparer.Ordinal);
        foreach (Key k in Enum.GetValues<Key>()) d[k.ToString()] = k;
        return d;
    }

    /// <summary>"hotkeys." + action id.</summary>
    public static string ActionKey(ShortcutAction action) => "hotkeys." + ShortcutCatalog.Info(action).Id;

    /// <summary>Exactly 15 entries: version, 12 actions, 2 view sets.</summary>
    public static IReadOnlyDictionary<string, string> Format(ShortcutBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        // SPEC-ASSUMPTION A4: a flat dictionary; the chosen profile is not stored here.
        var d = new Dictionary<string, string>(StringComparer.Ordinal) { [VersionKey] = CurrentVersion };
        foreach (ShortcutActionInfo info in ShortcutCatalog.Actions)
            d[ActionKey(info.Action)] = FormatCombo(bindings[info.Action]);
        d[RecallViewsKey] = FormatViewModifiers(bindings.RecallViewModifiers);
        d[SaveViewsKey] = FormatViewModifiers(bindings.SaveViewModifiers);
        return d.AsReadOnly();
    }

    /// <summary>Parses a stored dictionary; missing entries come from defaults, malformed ones are unbound.</summary>
    public static ShortcutBindings Parse(IReadOnlyDictionary<string, string> values, ShortcutBindings defaults)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(defaults);
        // SPEC-ASSUMPTION A10: a version other than "1" ignores the whole stored set; missing means 1.
        if (values.TryGetValue(VersionKey, out string? version) && version != CurrentVersion) return defaults;
        // SPEC-ASSUMPTION A12: no conflict repair; duplicates and equal view sets are kept.
        ShortcutBindings result = ShortcutBindings.Unbound;
        foreach (ShortcutActionInfo info in ShortcutCatalog.Actions)
        {
            KeyCombo? combo = values.TryGetValue(ActionKey(info.Action), out string? v)
                ? (v is null ? null : ParseCombo(v))
                : defaults[info.Action];
            result = result.WithAction(info.Action, combo);
        }
        result = result.WithViewModifiers(ViewSet.Recall, ParseSet(values, RecallViewsKey, defaults.RecallViewModifiers));
        result = result.WithViewModifiers(ViewSet.Save, ParseSet(values, SaveViewsKey, defaults.SaveViewModifiers));
        return result;
    }

    private static Modifiers? ParseSet(IReadOnlyDictionary<string, string> values, string key, Modifiers? fallback) =>
        values.TryGetValue(key, out string? v) ? (v is null ? null : ParseViewModifiers(v)) : fallback;

    /// <summary>"KeyName,bits" or "none".</summary>
    public static string FormatCombo(KeyCombo? combo)
    {
        if (combo is not KeyCombo c) return UnboundValue;
        Guard.Combo(c, nameof(combo));
        return c.Key.ToString() + "," + ((int)c.Modifiers).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Bindable combo, or null for "none" and for anything malformed.</summary>
    public static KeyCombo? ParseCombo(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int comma = value.IndexOf(',');
        if (comma < 0 || value.IndexOf(',', comma + 1) >= 0) return null;
        if (!KeysByName.TryGetValue(value[..comma], out Key key)) return null;
        if (!TryBits(value[(comma + 1)..], out Modifiers m)) return null;
        var combo = new KeyCombo(key, m);
        // SPEC-ASSUMPTION A11: a combo lacking Ctrl/Alt/Win or using Escape is treated as unbound.
        return ShortcutRules.IsBindable(combo) ? combo : null;
    }

    /// <summary>Decimal bits or "none".</summary>
    public static string FormatViewModifiers(Modifiers? modifiers)
    {
        if (modifiers is not Modifiers m) return UnboundValue;
        Guard.Mods(m, nameof(modifiers));
        return ((int)m).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Modifiers, or null for "none" and for anything malformed.</summary>
    public static Modifiers? ParseViewModifiers(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return TryBits(value, out Modifiers m) && ShortcutRules.HasRequiredModifier(m) ? m : null;
    }

    private static bool TryBits(string s, out Modifiers modifiers)
    {
        modifiers = Modifiers.None;
        if (s.Length == 0) return false;
        int v = 0;
        foreach (char ch in s)
        {
            if (ch < '0' || ch > '9') return false;
            v = v * 10 + (ch - '0');
            if (v > (int)Modifiers.All) return false;
        }
        modifiers = (Modifiers)v;
        return true;
    }
}
