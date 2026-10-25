using CanvasWm.Core.Shortcuts;

namespace CanvasWm.Engine.Persistence;

/// <summary>The settings this build uses (B27).</summary>
/// <param name="Profile">Shortcut profile used as defaults for missing entries.</param>
/// <param name="Shortcuts">Effective bindings.</param>
public sealed record HostSettings(ShortcutProfile Profile, ShortcutBindings Shortcuts)
{
    /// <summary>Key of the profile entry: "shortcuts.profile".</summary>
    public const string ProfileKey = "shortcuts.profile";

    private readonly ShortcutProfile _profile = CheckProfile(Profile);
    private readonly ShortcutBindings _shortcuts = CheckShortcuts(Shortcuts);

    /// <summary>Shortcut profile used as defaults for missing entries.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is not a defined profile.</exception>
    public ShortcutProfile Profile
    {
        get => _profile;
        init => _profile = CheckProfile(value);
    }

    /// <summary>Effective bindings.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public ShortcutBindings Shortcuts
    {
        get => _shortcuts;
        init => _shortcuts = CheckShortcuts(value);
    }

    private static ShortcutProfile CheckProfile(ShortcutProfile value) =>
        Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(Profile), value, "Undefined profile.");

    private static ShortcutBindings CheckShortcuts(ShortcutBindings value) =>
        value ?? throw new ArgumentNullException(nameof(Shortcuts));

    /// <summary>WinAlt profile with its bindings.</summary>
    public static HostSettings Default { get; } = new(ShortcutProfile.WinAlt, ShortcutProfiles.WinAlt);

    /// <summary>Reads a flat map (B27).</summary>
    /// <param name="values">The map.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentNullException">values is null.</exception>
    public static HostSettings FromValues(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ShortcutProfile profile = ShortcutProfile.WinAlt;
        if (values.TryGetValue(ProfileKey, out string? name))
        {
            if (string.Equals(name, nameof(ShortcutProfile.CtrlAlt), StringComparison.Ordinal)) profile = ShortcutProfile.CtrlAlt;
        }

        return new HostSettings(profile, ShortcutStore.Parse(values, ShortcutProfiles.Get(profile)));
    }

    /// <summary>ShortcutStore.Format(Shortcuts) plus the profile entry: 16 entries (B27).</summary>
    /// <returns>The map.</returns>
    public IReadOnlyDictionary<string, string> ToValues()
    {
        var d = new Dictionary<string, string>(ShortcutStore.Format(Shortcuts), StringComparer.Ordinal)
        {
            [ProfileKey] = Profile.ToString(),
        };
        return d;
    }
}
