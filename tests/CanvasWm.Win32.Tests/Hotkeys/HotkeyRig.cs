using CanvasWm.Core.Shortcuts;
using CanvasWm.Win32.Hotkeys;
using CanvasWm.Win32.Tests.WindowEnumSpec;

namespace CanvasWm.Win32.Tests.HotkeysSpec;

/// <summary>An action list that only runs when asked (the "ManualPost" of the spec).</summary>
internal sealed class ManualPost
{
    private readonly object _gate = new();
    private readonly List<Action> _actions = [];

    internal void Post(Action a)
    {
        lock (_gate) { _actions.Add(a); }
    }

    internal int Count
    {
        get { lock (_gate) { return _actions.Count; } }
    }

    internal Action this[int i]
    {
        get { lock (_gate) { return _actions[i]; } }
    }
}

/// <summary>Source under test on loop L, plus a holder source H on a second loop used to ask whether
/// a combo is free or held by someone else.</summary>
internal sealed class HotkeyRig : IDisposable
{
    internal static readonly KeyCombo T1 = new(Key.F24, Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift | Modifiers.Win);
    internal static readonly KeyCombo T2 = new(Key.F22, Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift);
    internal static readonly KeyCombo T3 = new(Key.F21, Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift);

    internal readonly MessageLoopThread L = new();
    internal readonly MessageLoopThread HLoop = new();
    internal readonly Win32HotkeySource H;
    internal Win32HotkeySource? S;

    internal HotkeyRig(bool createSource = true, ManualPost? manual = null)
    {
        H = HLoop.Invoke(() => new Win32HotkeySource(HLoop.Post));
        var all = HLoop.Invoke(() => H.Apply([T1, T2, T3]));
        HLoop.Invoke(() => H.Apply([]));
        Assert.True(all.All(r => r.Status == HotkeyStatus.Registered && r.ErrorCode == 0), "test combo owned by another program");
        if (createSource)
        {
            S = L.Invoke(() => new Win32HotkeySource(manual is null ? L.Post : manual.Post));
        }
    }

    internal Win32HotkeySource Source => S ?? throw new InvalidOperationException("no source");

    internal IReadOnlyList<HotkeyReport> Apply(params KeyCombo[] combos) => L.Invoke(() => Source.Apply(combos));

    /// <summary>True when H can register the combo (and releases it again).</summary>
    internal bool IsFree(KeyCombo c)
    {
        var r = HLoop.Invoke(() => H.Apply([c]));
        HLoop.Invoke(() => H.Apply([]));
        return r[0].Status == HotkeyStatus.Registered;
    }

    /// <summary>True when H gets Taken with 1409 for the combo.</summary>
    internal bool IsHeld(KeyCombo c)
    {
        var r = HLoop.Invoke(() => H.Apply([c]));
        HLoop.Invoke(() => H.Apply([]));
        return r[0].Status == HotkeyStatus.Taken && r[0].ErrorCode == 1409;
    }

    public void Dispose()
    {
        try { if (S is not null) { L.Invoke(() => S.Dispose()); } } catch { /* best effort */ }
        try { HLoop.Invoke(() => H.Dispose()); } catch { /* best effort */ }
        L.Dispose();
        HLoop.Dispose();
    }
}
