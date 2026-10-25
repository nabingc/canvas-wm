using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Hosting;

/// <summary>Messages the host window turns into notifications (B52).</summary>
public enum HostNotification
{
    /// <summary>The display configuration changed.</summary>
    DisplayChanged = 0,
    /// <summary>A system setting changed (includes the work area).</summary>
    SettingChanged = 1,
    /// <summary>A DPI change was broadcast.</summary>
    DpiChanged = 2,
    /// <summary>The machine resumed from sleep.</summary>
    PowerResumed = 3,
    /// <summary>The session is about to end.</summary>
    QueryEndSession = 4,
    /// <summary>The session is ending.</summary>
    EndSession = 5,
    /// <summary>Something asked the window to close.</summary>
    CloseRequested = 6,
    /// <summary>A second instance signalled this one.</summary>
    InstanceActivated = 7,
}

/// <summary>Hidden top-level window owned by the engine thread: the post queue for every
/// Win32 component and the receiver of broadcast messages. Mechanism only.</summary>
public sealed unsafe class Win32HostWindow : IDisposable
{
    /// <summary>Window class name: "CanvasWm.HostWindow".</summary>
    public const string WindowClassName = "CanvasWm.HostWindow";

    /// <summary>Registered message name used by a second instance: "CanvasWm.ActivateInstance".</summary>
    public const string ActivateMessageName = "CanvasWm.ActivateInstance";

    private const uint DrainMessage = NativeConstants.WM_APP + 1;
    private const int MaxPumped = 10_000;

    private static readonly object ClassGate = new();
    private static bool s_classRegistered;
    private static readonly object MapGate = new();
    private static readonly Dictionary<nint, Win32HostWindow> Instances = [];
    private static uint s_activateId;

    private readonly Action<Exception> _onError;
    private readonly Action<string>? _log;
    private readonly int _ownerThreadId;
    private readonly ConcurrentQueue<Action> _queue = new();
    private nint _hwnd;
    private int _wakePending;
    private int _disposed;
    private int _droppedLogged;

    /// <summary>Creates the window on the calling thread, which becomes the owner thread and must
    /// run a message loop (B49).</summary>
    /// <param name="onError">Receives exceptions thrown by posted actions or notification
    /// handlers, on the owner thread (B51).</param>
    /// <param name="log">Thread-safe one-line diagnostics; may be null.</param>
    /// <exception cref="Win32Exception">class or window creation failed.</exception>
    public Win32HostWindow(Action<Exception> onError, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(onError);
        _onError = onError;
        _log = log;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        EnsureClass();
        nint hInstance = NativeMethods.GetModuleHandleW(null);
        nint hwnd = NativeMethods.CreateWindowExW(
            (uint)(NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE),
            WindowClassName, "CanvasWM", NativeConstants.WS_POPUP, 0, 0, 0, 0, 0, 0, hInstance, 0);
        if (hwnd == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        lock (MapGate)
        {
            Instances[hwnd] = this;
        }

        Volatile.Write(ref _hwnd, hwnd);
        // Failure is ignored: a second instance at another integrity level then cannot signal.
        _ = NativeMethods.ChangeWindowMessageFilterEx(hwnd, ActivateId(), NativeConstants.MSGFLT_ALLOW, 0);
    }

    /// <summary>Actions queued and not yet run.</summary>
    public int PendingCount => _queue.Count;

    /// <summary>Raised on the owner thread, synchronously inside the window procedure (B52).</summary>
    public event Action<HostNotification>? Notified;

    /// <summary>Schedules action to run later on the owner thread, FIFO. Any thread. Never runs
    /// the action inside this call (B50). After Dispose the action is dropped.</summary>
    /// <param name="action">The action.</param>
    /// <exception cref="ArgumentNullException">action is null.</exception>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (Volatile.Read(ref _disposed) != 0)
        {
            if (Interlocked.Exchange(ref _droppedLogged, 1) == 0)
            {
                _log?.Invoke("host window: action posted after dispose was dropped");
            }

            return;
        }

        _queue.Enqueue(action);
        Arm();
    }

    /// <summary>Posts the activate message to another running host window (B53). True if posted.</summary>
    /// <returns>True if the message was posted.</returns>
    public static bool SignalExisting()
    {
        nint found = NativeMethods.FindWindowW(WindowClassName, null);
        if (found == 0)
        {
            return false;
        }

        return NativeMethods.PostMessageW(found, ActivateId(), 0, 0);
    }

    /// <summary>Waits up to waitMilliseconds for input on the calling thread's queue, then
    /// dispatches what is queued (at most 10 000 messages). Returns the number dispatched (B54).</summary>
    /// <param name="waitMilliseconds">Longest wait, 0 or more.</param>
    /// <returns>The number of messages dispatched.</returns>
    /// <exception cref="ArgumentOutOfRangeException">waitMilliseconds is negative.</exception>
    public static int PumpPending(int waitMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(waitMilliseconds);
        _ = NativeMethods.MsgWaitForMultipleObjectsEx(0, null, (uint)waitMilliseconds,
            NativeConstants.QS_ALLINPUT, NativeConstants.MWMO_INPUTAVAILABLE);
        int count = 0;
        while (count < MaxPumped && NativeMethods.PeekMessageW(out NativeMsg msg, 0, 0, 0, NativeConstants.PM_REMOVE))
        {
            if (msg.message == NativeConstants.WM_QUIT)
            {
                NativeMethods.PostQuitMessage((int)msg.wParam);
                break;
            }

            _ = NativeMethods.TranslateMessage(in msg);
            _ = NativeMethods.DispatchMessageW(in msg);
            count++;
        }

        return count;
    }

    /// <summary>Destroys the window; owner thread only, idempotent (B55).</summary>
    /// <exception cref="InvalidOperationException">called on another thread.</exception>
    public void Dispose()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Dispose must be called on the owner thread.");
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        nint hwnd = Interlocked.Exchange(ref _hwnd, 0);
        if (hwnd != 0)
        {
            _ = NativeMethods.DestroyWindow(hwnd);
            lock (MapGate)
            {
                Instances.Remove(hwnd);
            }
        }

        int dropped = 0;
        while (_queue.TryDequeue(out _))
        {
            dropped++;
        }

        if (dropped > 0)
        {
            _log?.Invoke($"host window: {dropped} queued action(s) dropped at dispose");
        }
    }

    private static uint ActivateId()
    {
        uint id = Volatile.Read(ref s_activateId);
        if (id == 0)
        {
            id = NativeMethods.RegisterWindowMessageW(ActivateMessageName);
            Volatile.Write(ref s_activateId, id);
        }

        return id;
    }

    private void Arm()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) != 0)
        {
            return;
        }

        nint hwnd = Volatile.Read(ref _hwnd);
        if (hwnd == 0 || !NativeMethods.PostMessageW(hwnd, DrainMessage, 0, 0))
        {
            Volatile.Write(ref _wakePending, 0);
            _log?.Invoke("host window: could not post the wake message");
        }
    }

    private void Drain()
    {
        Volatile.Write(ref _wakePending, 0);
        int n = _queue.Count;
        for (int i = 0; i < n; i++)
        {
            if (!_queue.TryDequeue(out Action? action))
            {
                break;
            }

            try
            {
                action();
            }
            catch (Exception e)
            {
                RaiseError(e);
                break;
            }
        }

        if (!_queue.IsEmpty && Volatile.Read(ref _disposed) == 0)
        {
            Arm();
        }
    }

    private void RaiseError(Exception e)
    {
        try
        {
            _onError(e);
        }
        catch (Exception inner)
        {
            _log?.Invoke("host window: error handler threw: " + inner.Message);
        }
    }

    private void Notify(HostNotification n)
    {
        try
        {
            Notified?.Invoke(n);
        }
        catch (Exception e)
        {
            RaiseError(e);
        }
    }

    private nint Handle(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case NativeConstants.WM_DISPLAYCHANGE:
                Notify(HostNotification.DisplayChanged);
                return 0;
            case NativeConstants.WM_SETTINGCHANGE:
                Notify(HostNotification.SettingChanged);
                return 0;
            case NativeConstants.WM_DPICHANGED:
                Notify(HostNotification.DpiChanged);
                return 0;
            case NativeConstants.WM_POWERBROADCAST:
                if (wParam == NativeConstants.PBT_APMRESUMEAUTOMATIC)
                {
                    Notify(HostNotification.PowerResumed);
                }

                return 1;
            case NativeConstants.WM_QUERYENDSESSION:
                Notify(HostNotification.QueryEndSession);
                return 1;
            case NativeConstants.WM_ENDSESSION:
                if (wParam != 0)
                {
                    Notify(HostNotification.EndSession);
                }

                return 0;
            case NativeConstants.WM_CLOSE:
                Notify(HostNotification.CloseRequested);
                return 0;
            case DrainMessage:
                Drain();
                return 0;
        }

        if (msg == Volatile.Read(ref s_activateId) && msg != 0)
        {
            Notify(HostNotification.InstanceActivated);
            return 0;
        }

        return NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam);
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
            Win32HostWindow? self;
            lock (MapGate)
            {
                Instances.TryGetValue(hwnd, out self);
            }

            return self is null
                ? NativeMethods.DefWindowProcW(hwnd, msg, wParam, lParam)
                : self.Handle(hwnd, msg, wParam, lParam);
        }
        catch
        {
            // Exceptions must not cross the native boundary (B51); the handlers already route theirs to onError.
            return 0;
        }
    }
}
