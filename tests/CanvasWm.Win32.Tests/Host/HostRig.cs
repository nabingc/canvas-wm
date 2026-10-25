using System.Collections.Concurrent;
using CanvasWm.Win32.Hosting;
using CanvasWm.Win32.Tests.WindowEnumSpec;

namespace CanvasWm.Win32.Tests.HostSpec;

/// <summary>A dedicated STA thread that owns one Win32HostWindow and pumps it; tests run code on that thread.</summary>
internal sealed class HostRig : IDisposable
{
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private volatile bool _stop;

    public Win32HostWindow Host { get; private set; } = null!;
    public nint Hwnd { get; private set; }
    public uint NativeThreadId { get; private set; }
    public int ManagedThreadId { get; private set; }
    public ConcurrentQueue<(Exception Error, int ThreadId)> Errors { get; } = new();
    public ConcurrentQueue<HostNotification> Notifications { get; } = new();
    public ConcurrentQueue<string> Logs { get; } = new();
    public volatile bool OnErrorThrows;

    public HostRig(bool autoPump = true)
    {
        AutoPump = autoPump;
        _thread = new Thread(Run) { IsBackground = true, Name = "CanvasWm.Test.HostOwner" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        Assert.True(_ready.Wait(TimeSpan.FromSeconds(10)), "host owner thread did not start");
    }

    /// <summary>When false the owner thread only runs commands and does not pump by itself.</summary>
    public bool AutoPump { get; }

    private void Run()
    {
        NativeThreadId = HostNative.GetCurrentThreadId();
        ManagedThreadId = Environment.CurrentManagedThreadId;
        Host = new Win32HostWindow(e =>
        {
            Errors.Enqueue((e, Environment.CurrentManagedThreadId));
            if (OnErrorThrows) throw new InvalidOperationException("onError failed");
        }, Logs.Enqueue);
        Host.Notified += n => Notifications.Enqueue(n);
        Hwnd = HostNative.FindHostWindowOfThread(NativeThreadId);
        _ready.Set();
        while (!_stop)
        {
            while (_commands.TryDequeue(out Action? a)) a();
            if (AutoPump) Win32HostWindow.PumpPending(5);
            else Thread.Sleep(1);
        }
    }

    /// <summary>Runs f on the owner thread and returns its result (exceptions propagate unwrapped).</summary>
    public T Invoke<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            try { tcs.SetResult(f()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        Assert.True(((IAsyncResult)tcs.Task).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(20)), "owner invoke timed out");
        return tcs.Task.GetAwaiter().GetResult();
    }

    public void Invoke(Action a) => Invoke<int>(() => { a(); return 0; });

    /// <summary>One PumpPending(0) on the owner thread; returns the dispatched count.</summary>
    public int PumpOnce() => Invoke(() => Win32HostWindow.PumpPending(0));

    /// <summary>Pumps on the owner thread until done() or the timeout.</summary>
    public bool PumpUntil(Func<bool> done, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (done()) return true;
            Invoke(() => Win32HostWindow.PumpPending(5));
        }

        return done();
    }

    public nint Send(uint msg, nuint w = 0, nint l = 0) => Invoke(() => HostNative.SendMessageW(Hwnd, msg, w, l));

    public void Dispose()
    {
        try { Invoke(() => Host.Dispose()); } catch { /* best effort */ }
        _stop = true;
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
