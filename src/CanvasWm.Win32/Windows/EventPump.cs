using CanvasWm.Engine.Services;

namespace CanvasWm.Win32.Windows;

/// <summary>FIFO queue between the hook callback and the handlers (B16-B18). No OS access.</summary>
internal sealed class EventPump
{
    private sealed class Entry(Action<RawWindowEvent> handler)
    {
        internal readonly Action<RawWindowEvent> Handler = handler;
        internal volatile bool Disposed;
    }

    private sealed class Registration(EventPump owner, Entry entry) : IDisposable
    {
        public void Dispose() => owner.Remove(entry);
    }

    private readonly Action<Action> _post;
    private readonly object _gate = new();
    private readonly Queue<RawWindowEvent> _queue = new();
    private readonly List<Entry> _handlers = [];
    private bool _pending;
    private bool _running;
    private Thread? _postingThread;

    internal EventPump(Action<Action> post)
    {
        _post = post ?? throw new ArgumentNullException(nameof(post));
    }

    /// <summary>Number of live handlers.</summary>
    internal int HandlerCount
    {
        get { lock (_gate) { return _handlers.Count; } }
    }

    /// <summary>Number of events waiting for delivery.</summary>
    internal int QueuedCount
    {
        get { lock (_gate) { return _queue.Count; } }
    }

    // SPEC-ASSUMPTION A12: no coalescing or de-duplication of any event kind, LocationChanged included.
    /// <summary>Appends an event; posts a drain unless one is pending or running.</summary>
    internal void Enqueue(RawWindowEvent e)
    {
        bool needPost;
        lock (_gate)
        {
            _queue.Enqueue(e);
            needPost = !_pending && !_running;
            if (needPost)
            {
                _pending = true;
            }
        }

        if (needPost)
        {
            PostDrain();
        }
    }

    /// <summary>Adds a handler; disposing the result removes it at once.</summary>
    internal IDisposable Add(Action<RawWindowEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var entry = new Entry(handler);
        lock (_gate)
        {
            _handlers.Add(entry);
        }

        return new Registration(this, entry);
    }

    private void Remove(Entry entry)
    {
        lock (_gate)
        {
            entry.Disposed = true;
            _handlers.Remove(entry);
        }
    }

    private void PostDrain()
    {
        _postingThread = Thread.CurrentThread;
        try
        {
            _post(Drain);
        }
        catch
        {
            lock (_gate)
            {
                _pending = false;
            }

            throw;
        }
        finally
        {
            _postingThread = null;
        }
    }

    private void Drain()
    {
        if (ReferenceEquals(_postingThread, Thread.CurrentThread))
        {
            throw new InvalidOperationException("The post delegate ran the action inline; it must defer to the owner thread's message loop.");
        }

        lock (_gate)
        {
            if (_running)
            {
                return;
            }

            _pending = false;
            _running = true;
        }

        try
        {
            while (true)
            {
                RawWindowEvent e;
                Entry[] snapshot;
                lock (_gate)
                {
                    if (_queue.Count == 0)
                    {
                        _running = false;
                        return;
                    }

                    e = _queue.Dequeue();
                    snapshot = _handlers.ToArray();
                }

                foreach (Entry entry in snapshot)
                {
                    if (!entry.Disposed)
                    {
                        entry.Handler(e);
                    }
                }
            }
        }
        catch
        {
            bool repost;
            lock (_gate)
            {
                _running = false;
                repost = _queue.Count > 0 && !_pending;
                if (repost)
                {
                    _pending = true;
                }
            }

            if (repost)
            {
                PostDrain();
            }

            throw;
        }
    }
}
