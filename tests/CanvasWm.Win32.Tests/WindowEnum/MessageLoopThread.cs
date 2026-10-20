using System.Collections.Concurrent;

namespace CanvasWm.Win32.Tests.WindowEnumSpec;

/// <summary>A dedicated thread that pumps Win32 messages and runs posted actions in FIFO order.</summary>
internal sealed class MessageLoopThread : IDisposable
{
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private volatile bool _stop;
    private uint _nativeId;

    internal MessageLoopThread()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "CanvasWm.Test.Loop" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        Assert.True(_ready.Wait(TimeSpan.FromSeconds(10)), "message loop did not start");
    }

    internal int ManagedThreadId { get; private set; }

    internal uint NativeThreadId => _nativeId;

    /// <summary>Enqueues and wakes the loop. Never runs the action inline.</summary>
    internal void Post(Action a)
    {
        _queue.Enqueue(a);
        Native.PostThreadMessageW(_nativeId, Native.WM_APP, 0, 0);
    }

    internal T Invoke<T>(Func<T> f)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { tcs.SetResult(f()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        Assert.True(tcs.Task.Wait(TimeSpan.FromSeconds(20)), "loop invoke timed out");
        return tcs.Task.Result;
    }

    internal void Invoke(Action a) => Invoke<int>(() => { a(); return 0; });

    private void Run()
    {
        _nativeId = Native.GetCurrentThreadId();
        ManagedThreadId = Environment.CurrentManagedThreadId;
        Native.PeekMessageW(out _, 0, 0, 0, 0); // force creation of the thread message queue
        _ready.Set();
        while (!_stop)
        {
            while (_queue.TryDequeue(out Action? a))
            {
                a();
            }

            Native.MsgWaitForMultipleObjectsEx(0, null, 50, Native.QS_ALLINPUT, Native.MWMO_INPUTAVAILABLE);
            while (Native.PeekMessageW(out Native.MSG m, 0, 0, 0, Native.PM_REMOVE))
            {
                Native.TranslateMessage(ref m);
                Native.DispatchMessageW(ref m);
            }
        }
    }

    public void Dispose()
    {
        _stop = true;
        Native.PostThreadMessageW(_nativeId, Native.WM_APP, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
