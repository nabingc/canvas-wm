using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Hotkeys;

/// <summary>System-wide hotkeys via RegisterHotKey on a private message-only window. Mechanism
/// only: it registers exactly what Apply is given and reports presses; which combos to register
/// and what a press means are decided by the caller. Construct it on the owner thread (the
/// message-pumping engine thread that also owns Win32WindowSource).</summary>
public sealed unsafe class Win32HotkeySource : IDisposable
{
    /// <summary>Name of the message-only window class.</summary>
    internal const string WindowClassName = "CanvasWm.HotkeyWindow";

    private static readonly object ClassGate = new();
    private static bool s_classRegistered;
    private static readonly object MapGate = new();
    private static readonly Dictionary<nint, Win32HotkeySource> Instances = [];

    private readonly Action<Action> _post;
    private readonly int _ownerThreadId;
    private readonly PressQueue _queue;
    private readonly object _regGate = new();
    private readonly Dictionary<int, KeyCombo> _registered = [];
    private nint _hwnd;
    private int _disposed;

    /// <summary>Records the calling thread as the owner thread and creates the message-only
    /// window on it (B7). Registers nothing.</summary>
    /// <param name="post">Schedules an action to run LATER on the owner thread, from its message
    /// loop, in FIFO order. It must never run the action inline (#10 A13).</param>
    /// <exception cref="System.ComponentModel.Win32Exception">The window class or window could not
    /// be created.</exception>
    public Win32HotkeySource(Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        _post = post;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _queue = new PressQueue(post, Deliver);
        EnsureClass();
        nint hInstance = NativeMethods.GetModuleHandleW(null);
        nint hwnd = NativeMethods.CreateWindowExW(0, WindowClassName, null, 0, 0, 0, 0, 0,
            NativeConstants.HWND_MESSAGE, 0, hInstance, 0);
        if (hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        _hwnd = hwnd;
        lock (MapGate)
        {
            Instances[hwnd] = this;
        }
    }

    /// <summary>Raised on the owner thread, through post, once per accepted WM_HOTKEY (B13-B15).
    /// Never raised inline inside Apply, Dispose or the window procedure, and never after
    /// Dispose.</summary>
    public event Action<KeyCombo>? Pressed;

    internal nint WindowHandle => Volatile.Read(ref _hwnd);

    internal IReadOnlyCollection<KeyCombo> RegisteredCombos
    {
        get { lock (_regGate) { return _registered.Values.ToArray(); } }
    }

    /// <summary>Makes the registered set equal to the distinct combos of desired (B9-B11).
    /// Returns one report per distinct desired combo, in first-occurrence order. Combos that are
    /// desired but not registered (for example Taken last time) are attempted again.</summary>
    /// <exception cref="ArgumentNullException">desired is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">a combo is invalid.</exception>
    /// <exception cref="ArgumentException">a combo has none of Ctrl, Alt, Win.</exception>
    /// <exception cref="InvalidOperationException">called on a thread other than the owner
    /// thread.</exception>
    /// <exception cref="ObjectDisposedException">after Dispose.</exception>
    public IReadOnlyList<HotkeyReport> Apply(IReadOnlyList<KeyCombo> desired)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Apply must be called on the owner thread.");
        }

        ArgumentNullException.ThrowIfNull(desired);
        for (int i = 0; i < desired.Count; i++)
        {
            KeyCombo c = desired[i];
            if (!Enum.IsDefined(c.Key) || ((int)c.Modifiers & ~(int)Modifiers.All) != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(desired), c, "Invalid key combo.");
            }
        }

        for (int i = 0; i < desired.Count; i++)
        {
            // SPEC-ASSUMPTION A7: a combo with none of Ctrl, Alt, Win is a call-contract error.
            if (!ShortcutRules.HasRequiredModifier(desired[i].Modifiers))
            {
                throw new ArgumentException("A combo needs at least one of Ctrl, Alt, Win.", nameof(desired));
            }
        }

        nint hwnd = WindowHandle;
        HotkeyDiff diff = HotkeyPlan.Diff(RegisteredCombos, desired);
        foreach (KeyCombo c in diff.ToUnregister)
        {
            int id = HotkeyIds.For(c);
            _ = NativeMethods.UnregisterHotKey(hwnd, id);
            lock (_regGate)
            {
                _registered.Remove(id);
            }
        }

        // SPEC-ASSUMPTION A3: NotOnLayout uses the owner thread's layout, read once per Apply.
        nint hkl = NativeMethods.GetKeyboardLayout(0);
        var kept = new HashSet<KeyCombo>(RegisteredCombos);
        var seen = new HashSet<KeyCombo>();
        var reports = new List<HotkeyReport>();
        for (int i = 0; i < desired.Count; i++)
        {
            KeyCombo c = desired[i];
            if (!seen.Add(c))
            {
                continue;
            }

            bool onLayout = Win32KeyInput.IsOnLayout(c.Key, hkl);
            if (kept.Contains(c))
            {
                reports.Add(HotkeyMessages.ReportFor(c, true, 0, onLayout));
                continue;
            }

            int id = HotkeyIds.For(c);
            bool ok = NativeMethods.RegisterHotKey(hwnd, id, ModMap.RegisterFlags(c.Modifiers), (uint)KeyMap.ToVk(c.Key));
            int err = ok ? 0 : Marshal.GetLastPInvokeError();
            if (ok)
            {
                lock (_regGate)
                {
                    _registered[id] = c;
                }
            }

            reports.Add(HotkeyMessages.ReportFor(c, ok, err, onLayout));
        }

        return reports.AsReadOnly();
    }

    /// <summary>Unregisters every hotkey and destroys the window (B16). Idempotent. May be called
    /// from any thread; off the owner thread the OS cleanup is passed to post.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.Close();
        if (Environment.CurrentManagedThreadId == _ownerThreadId)
        {
            Cleanup();
            return;
        }

        try
        {
            // SPEC-ASSUMPTION A11: off-thread cleanup is posted; if it never runs the OS frees it at process exit.
            _post(Cleanup);
        }
        catch
        {
            // The registrations then live until the process exits.
        }
    }

    private void Cleanup()
    {
        nint hwnd = Interlocked.Exchange(ref _hwnd, 0);
        if (hwnd == 0)
        {
            return;
        }

        KeyValuePair<int, KeyCombo>[] regs;
        lock (_regGate)
        {
            regs = _registered.ToArray();
            _registered.Clear();
        }

        foreach (KeyValuePair<int, KeyCombo> p in regs)
        {
            _ = NativeMethods.UnregisterHotKey(hwnd, p.Key);
        }

        _ = NativeMethods.DestroyWindow(hwnd);
        lock (MapGate)
        {
            Instances.Remove(hwnd);
        }
    }

    private void Deliver(KeyCombo combo)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Pressed?.Invoke(combo);
    }

    private void OnHotkey(nint wParam, nint lParam)
    {
        Dictionary<int, KeyCombo> snapshot;
        lock (_regGate)
        {
            snapshot = new Dictionary<int, KeyCombo>(_registered);
        }

        KeyCombo? c = HotkeyMessages.DecodeHotkey(wParam, lParam, snapshot);
        if (c is KeyCombo combo)
        {
            _queue.Enqueue(combo);
        }
    }

    private static void EnsureClass()
    {
        lock (ClassGate)
        {
            if (s_classRegistered)
            {
                return;
            }

            nint hInstance = NativeMethods.GetModuleHandleW(null);
            fixed (char* name = WindowClassName)
            {
                var wc = new NativeWndClassEx
                {
                    cbSize = (uint)sizeof(NativeWndClassEx),
                    lpfnWndProc = &WndProc,
                    hInstance = hInstance,
                    lpszClassName = name,
                };
                ushort atom = NativeMethods.RegisterClassExW(&wc);
                if (atom == 0)
                {
                    int err = Marshal.GetLastPInvokeError();
                    if (err != NativeConstants.ERROR_CLASS_ALREADY_EXISTS)
                    {
                        throw new Win32Exception(err);
                    }
                }
            }

            s_classRegistered = true;
        }
    }

    [UnmanagedCallersOnly]
    private static nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == NativeConstants.WM_HOTKEY)
            {
                Win32HotkeySource? self;
                lock (MapGate)
                {
                    Instances.TryGetValue(hwnd, out self);
                }

                self?.OnHotkey(wParam, lParam);
                return 0;
            }

            return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
        catch
        {
            // Exceptions must not cross the native boundary (#10 A17); discarded (A13).
            return 0;
        }
    }
}
