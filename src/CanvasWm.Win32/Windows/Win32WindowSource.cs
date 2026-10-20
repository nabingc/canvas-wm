using System.ComponentModel;
using System.Runtime.InteropServices;
using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Windows;

/// <summary>IWindowSource over user32/dwmapi. Reports facts only. Construct it on the engine
/// thread: that thread is the owner thread, must run a message loop, and is where hooks are
/// installed and events delivered.</summary>
public sealed unsafe class Win32WindowSource : IWindowSource, IDisposable
{
    private static readonly object HookMapGate = new();
    private static readonly Dictionary<nint, Win32WindowSource> HookMap = [];

    private readonly Action<Action> _post;
    private readonly int _ownerThreadId;
    private readonly EventPump _pump;
    private readonly FactGatherer _gatherer;
    private readonly object _gate = new();
    private readonly List<nint> _hooks = [];
    private readonly List<Subscription> _subscriptions = [];
    private bool _disposed;

    /// <summary>Creates the source. Records the calling thread as the owner thread and reads
    /// CanvasWM's own process id and integrity level. Installs nothing.</summary>
    /// <param name="post">Schedules an action to run LATER on the owner thread, from its
    /// message loop, in FIFO order. It must never run the action inline (B17).</param>
    public Win32WindowSource(Action<Action> post)
    {
        ArgumentNullException.ThrowIfNull(post);
        // SPEC-ASSUMPTION A13: threading via the injected post delegate; owner thread = constructing thread.
        _post = post;
        _ownerThreadId = Environment.CurrentManagedThreadId;
        _pump = new EventPump(post);
        _gatherer = new FactGatherer(new ProcessInfoCache());
    }

    /// <summary>Number of installed hooks: 0 or 6.</summary>
    internal int ActiveHookCount
    {
        get { lock (_gate) { return _hooks.Count; } }
    }

    /// <inheritdoc/>
    public IReadOnlyList<OsWindowFacts> Enumerate()
    {
        var result = new List<OsWindowFacts>();
        try
        {
            using var dpi = new DpiScope();
            var handles = new List<nint>();
            GCHandle gc = GCHandle.Alloc(handles);
            try
            {
                NativeMethods.EnumWindows(&EnumProc, GCHandle.ToIntPtr(gc));
            }
            finally
            {
                gc.Free();
            }

            var seen = new HashSet<int>();
            foreach (nint h in handles)
            {
                if (!NativeMethods.IsWindow(h))
                {
                    continue;
                }

                try
                {
                    result.Add(_gatherer.Gather(h, seen));
                }
                catch (Exception)
                {
                    // Facts only: an unreadable window is omitted.
                }
            }

            _gatherer.Prune(seen);
        }
        catch (Exception)
        {
            // Never throws for OS failures; return what was gathered.
        }

        return result;
    }

    /// <inheritdoc/>
    public OsWindowFacts? Describe(ulong handle)
    {
        if (handle == 0)
        {
            return null;
        }

        nint h = HandleConversion.ToHwnd(handle);
        try
        {
            using var dpi = new DpiScope();
            return NativeMethods.IsWindow(h) ? _gatherer.Gather(h, null) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Starts delivering raw events to handler (B13-B18).</summary>
    /// <param name="handler">The event handler.</param>
    /// <returns>The subscription.</returns>
    /// <exception cref="InvalidOperationException">Called on a thread other than the owner
    /// thread.</exception>
    /// <exception cref="ObjectDisposedException">After Dispose.</exception>
    /// <exception cref="Win32Exception">The OS refused a hook (A16).</exception>
    public IDisposable Subscribe(Action<RawWindowEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException("Subscribe must be called on the thread that created the source.");
        }

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            IDisposable registration = _pump.Add(handler);
            if (_hooks.Count == 0)
            {
                try
                {
                    InstallHooks();
                }
                catch
                {
                    registration.Dispose();
                    throw;
                }
            }

            var sub = new Subscription(this, registration);
            _subscriptions.Add(sub);
            return sub;
        }
    }

    /// <summary>Ends every subscription and removes all hooks. Idempotent. Enumerate and
    /// Describe keep working afterwards.</summary>
    public void Dispose()
    {
        Subscription[] subs;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            subs = [.. _subscriptions];
        }

        foreach (Subscription s in subs)
        {
            s.Dispose();
        }
    }

    // Must be called with _gate held, on the owner thread.
    private void InstallHooks()
    {
        const uint flags = NativeConstants.WINEVENT_OUTOFCONTEXT | NativeConstants.WINEVENT_SKIPOWNPROCESS;
        foreach ((uint min, uint max) in WinEventMapping.HookRanges)
        {
            nint hook = NativeMethods.SetWinEventHook(min, max, 0, &WinEventProc, 0, 0, flags);
            if (hook == 0)
            {
                // SPEC-ASSUMPTION A16: a refused hook throws Win32Exception; hooks already installed are removed.
                int error = Marshal.GetLastPInvokeError();
                RemoveHooksLocked();
                throw new Win32Exception(error);
            }

            lock (HookMapGate)
            {
                HookMap[hook] = this;
            }

            _hooks.Add(hook);
        }
    }

    private void RemoveHooksLocked()
    {
        foreach (nint hook in _hooks)
        {
            NativeMethods.UnhookWinEvent(hook);
            lock (HookMapGate)
            {
                HookMap.Remove(hook);
            }
        }

        _hooks.Clear();
    }

    private void OnSubscriptionDisposed(Subscription sub)
    {
        lock (_gate)
        {
            _subscriptions.Remove(sub);
            if (_subscriptions.Count > 0 || _hooks.Count == 0)
            {
                return;
            }

            if (Environment.CurrentManagedThreadId == _ownerThreadId)
            {
                RemoveHooksLocked();
                return;
            }
        }

        // UnhookWinEvent must run on the installing thread.
        _post(UnhookIfIdle);
    }

    private void UnhookIfIdle()
    {
        lock (_gate)
        {
            if (_subscriptions.Count == 0)
            {
                RemoveHooksLocked();
            }
        }
    }

    private void OnHookEvent(uint eventId, nint hwnd, int idObject, int idChild)
    {
        using var dpi = new DpiScope();
        RawWindowEvent? e = WinEventMapping.Translate(
            eventId, hwnd, idObject, idChild,
            NativeMethods.IsWindow,
            h => NativeMethods.GetAncestor(h, NativeConstants.GA_ROOT));
        if (e is { } ev)
        {
            _pump.Enqueue(ev);
        }
    }

    [UnmanagedCallersOnly]
    private static int EnumProc(nint hwnd, nint lParam)
    {
        try
        {
            if (GCHandle.FromIntPtr(lParam).Target is List<nint> list)
            {
                list.Add(hwnd);
            }
        }
        catch (Exception)
        {
            // Must not cross the native boundary.
        }

        return 1;
    }

    [UnmanagedCallersOnly]
    private static void WinEventProc(nint hook, uint eventId, nint hwnd, int idObject, int idChild, uint threadId, uint timeMs)
    {
        try
        {
            Win32WindowSource? source;
            lock (HookMapGate)
            {
                HookMap.TryGetValue(hook, out source);
            }

            source?.OnHookEvent(eventId, hwnd, idObject, idChild);
        }
        catch (Exception)
        {
            // SPEC-ASSUMPTION A17: exceptions in the native callback are swallowed; the event is dropped.
        }
    }

    private sealed class Subscription(Win32WindowSource owner, IDisposable registration) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            registration.Dispose();
            owner.OnSubscriptionDisposed(this);
        }
    }
}
