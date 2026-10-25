using System.Globalization;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Engine.Routing;

namespace CanvasWm.Engine.Hosting;

/// <summary>Connects hotkey triggers, the OS hotkey registration and the router (B39-B41).
/// Engine-thread only.</summary>
public sealed class ShortcutRelay
{
    private readonly HotkeyTriggers _triggers;
    private readonly EngineRouter _router;
    private readonly Func<IReadOnlyList<KeyCombo>, IReadOnlyList<HotkeyReport>> _apply;
    private readonly Action<string>? _log;

    /// <summary>Wires the relay: router.Started starts the triggers, and apply requests go to apply.</summary>
    /// <param name="triggers">The triggers.</param>
    /// <param name="router">The router.</param>
    /// <param name="apply">Registers exactly the given combos and returns one report per distinct
    /// combo (the App passes Win32HotkeySource.Apply).</param>
    /// <param name="log">One-line diagnostics; may be null.</param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public ShortcutRelay(HotkeyTriggers triggers, EngineRouter router,
                         Func<IReadOnlyList<KeyCombo>, IReadOnlyList<HotkeyReport>> apply,
                         Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(apply);
        _triggers = triggers;
        _router = router;
        _apply = apply;
        _log = log;
        LastReports = Array.Empty<HotkeyReport>();
        router.Started += triggers.Start;
        triggers.ApplyRequested += OnApply;
    }

    /// <summary>Reports of the latest apply; empty before the first.</summary>
    public IReadOnlyList<HotkeyReport> LastReports { get; private set; }

    /// <summary>Raised after every apply with its request and reports.</summary>
    public event Action<HotkeyApplyRequest, IReadOnlyList<HotkeyReport>>? Applied;

    /// <summary>A registered combo was pressed (B41). Returns the route, or null when nothing ran.</summary>
    /// <param name="combo">The pressed combo.</param>
    /// <returns>The route, or null.</returns>
    public CommandRoute? Pressed(KeyCombo combo)
    {
        ShortcutCommand? cmd = _triggers.Pressed(combo);
        return cmd is ShortcutCommand c ? _router.Execute(c) : null;
    }

    private void OnApply(HotkeyApplyRequest req)
    {
        IReadOnlyList<HotkeyReport> reports = _apply(req.Desired);
        LastReports = reports;
        if (_log is not null)
        {
            int active = 0;
            var others = new Dictionary<HotkeyStatus, int>();
            foreach (HotkeyReport r in reports)
            {
                if (r.IsActive) active++;
                else others[r.Status] = others.GetValueOrDefault(r.Status) + 1;
            }

            string extra = string.Concat(others.OrderBy(k => k.Key)
                .Select(k => string.Create(CultureInfo.InvariantCulture, $", {k.Value} {k.Key}")));
            _log($"hotkeys {req.Reason}: {req.Desired.Count} requested, {active} active{extra}");
            foreach (HotkeyReport r in reports)
            {
                if (!r.IsActive)
                {
                    _log(string.Create(CultureInfo.InvariantCulture,
                        $"hotkey {ShortcutStore.FormatCombo(r.Combo)}: {r.Status} (error {r.ErrorCode})"));
                }
            }
        }

        Applied?.Invoke(req, reports);
    }
}
