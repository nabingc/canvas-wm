using CanvasWm.Core.Shortcuts;

namespace CanvasWm.Engine.Routing;

/// <summary>Why the App should call the hotkey source's Apply now (#12 A5).</summary>
public enum HotkeyApplyReason
{
    /// <summary>The engines started.</summary>
    Start = 0,
    /// <summary>The bindings were replaced.</summary>
    BindingsChanged = 1,
    /// <summary>Shortcut recording began.</summary>
    RecordingStarted = 2,
    /// <summary>Shortcut recording ended.</summary>
    RecordingStopped = 3,
    /// <summary>The settings window opened.</summary>
    SettingsOpened = 4,
    /// <summary>An explicit retry.</summary>
    Retry = 5,
    /// <summary>Shutting down; register nothing.</summary>
    Stop = 6,
}

/// <summary>One apply request: register exactly Desired (B36).</summary>
/// <param name="Reason">Why.</param>
/// <param name="Desired">The combos to register.</param>
public sealed record HotkeyApplyRequest(HotkeyApplyReason Reason, IReadOnlyList<KeyCombo> Desired);

/// <summary>Owns the bindings, dispatch table and recording flag, and says when hotkeys must be
/// re-applied. No OS types. Single-threaded (engine thread).</summary>
public sealed class HotkeyTriggers
{
    private ShortcutBindings _bindings;
    private IReadOnlyList<DispatchEntry> _table;

    /// <summary>Creates triggers in the not-started state.</summary>
    /// <param name="bindings">The initial bindings.</param>
    /// <exception cref="ArgumentNullException">bindings is null.</exception>
    public HotkeyTriggers(ShortcutBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings;
        _table = ShortcutDispatch.Build(bindings);
    }

    /// <summary>The current bindings.</summary>
    public ShortcutBindings Bindings => _bindings;

    /// <summary>ShortcutDispatch.Build(Bindings).</summary>
    public IReadOnlyList<DispatchEntry> Table => _table;

    /// <summary>True while a shortcut is being recorded.</summary>
    public bool Recording { get; private set; }

    /// <summary>True after Start.</summary>
    public bool IsStarted { get; private set; }

    /// <summary>True after Stop.</summary>
    public bool IsStopped { get; private set; }

    /// <summary>HotkeyPlan.Desired(Table, Recording); empty after Stop.</summary>
    public IReadOnlyList<KeyCombo> Desired =>
        IsStopped ? Array.Empty<KeyCombo>() : HotkeyPlan.Desired(_table, Recording);

    /// <summary>Raised synchronously by the trigger methods (B36, B37).</summary>
    public event Action<HotkeyApplyRequest>? ApplyRequested;

    /// <summary>Marks started and requests the first apply (wire to EngineRouter.Started).</summary>
    public void Start()
    {
        if (IsStarted || IsStopped) return;
        IsStarted = true;
        Raise(HotkeyApplyReason.Start);
    }

    /// <summary>Replaces the bindings and requests an apply (always, even when equal).</summary>
    /// <param name="bindings">The new bindings.</param>
    /// <exception cref="ArgumentNullException">bindings is null.</exception>
    public void SetBindings(ShortcutBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        _bindings = bindings;
        _table = ShortcutDispatch.Build(bindings);
        Raise(HotkeyApplyReason.BindingsChanged);
    }

    /// <summary>Recording began; hotkeys are released.</summary>
    public void BeginRecording()
    {
        if (Recording) return;
        Recording = true;
        Raise(HotkeyApplyReason.RecordingStarted);
    }

    /// <summary>Recording ended; hotkeys are restored.</summary>
    public void EndRecording()
    {
        if (!Recording) return;
        Recording = false;
        Raise(HotkeyApplyReason.RecordingStopped);
    }

    /// <summary>The settings window opened; requests an apply.</summary>
    public void SettingsOpened() => Raise(HotkeyApplyReason.SettingsOpened);

    /// <summary>Explicit retry; requests an apply.</summary>
    public void Retry() => Raise(HotkeyApplyReason.Retry);

    /// <summary>Stops for good; requests an apply of nothing if it was started.</summary>
    public void Stop()
    {
        if (IsStopped) return;
        IsStopped = true;
        if (IsStarted)
        {
            ApplyRequested?.Invoke(new HotkeyApplyRequest(HotkeyApplyReason.Stop, Array.Empty<KeyCombo>()));
        }
    }

    /// <summary>The command for a pressed combo, or null while recording, before Start, after
    /// Stop, or when nothing matches (B38).</summary>
    /// <param name="combo">The pressed combo.</param>
    /// <returns>The command or null.</returns>
    public ShortcutCommand? Pressed(KeyCombo combo)
    {
        if (!IsStarted || IsStopped || Recording) return null;
        return ShortcutDispatch.Match(_table, combo);
    }

    private void Raise(HotkeyApplyReason reason)
    {
        if (!IsStarted || IsStopped) return;
        ApplyRequested?.Invoke(new HotkeyApplyRequest(reason, Desired));
    }
}
