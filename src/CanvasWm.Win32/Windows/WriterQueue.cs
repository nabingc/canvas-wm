namespace CanvasWm.Win32.Windows;

/// <summary>Single FIFO worker thread (B2-B4).</summary>
internal sealed class WriterQueue : IDisposable
{
    private readonly object _gate = new();
    private readonly Queue<(Action Work, Action OnDropped)> _items = new();
    private readonly Thread _thread;
    private bool _disposed;

    internal WriterQueue(string threadName)
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = threadName };
        _thread.Start();
    }

    /// <summary>Items waiting (not including the one running).</summary>
    internal int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    /// <summary>Enqueue work; false (nothing enqueued) after Dispose.</summary>
    internal bool TryEnqueue(Action work, Action onDropped)
    {
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(onDropped);
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            _items.Enqueue((work, onDropped));
            Monitor.Pulse(_gate);
            return true;
        }
    }

    /// <summary>Stops the worker; items not yet started are dropped (their onDropped runs on the
    /// disposing thread). The running item finishes. Does not join (A14).</summary>
    public void Dispose()
    {
        List<Action> dropped;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            dropped = new List<Action>(_items.Count);
            while (_items.Count > 0)
            {
                dropped.Add(_items.Dequeue().OnDropped);
            }

            Monitor.PulseAll(_gate);
        }

        foreach (Action d in dropped)
        {
            try
            {
                d();
            }
            catch (Exception)
            {
                // A drop notification must not break disposal.
            }
        }
    }

    private void Loop()
    {
        while (true)
        {
            Action work;
            lock (_gate)
            {
                while (_items.Count == 0)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    Monitor.Wait(_gate);
                }

                if (_disposed)
                {
                    return;
                }

                work = _items.Dequeue().Work;
            }

            try
            {
                work();
            }
            catch (Exception)
            {
                // Work items handle their own failures; the worker keeps going.
            }
        }
    }
}
