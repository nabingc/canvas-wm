using CanvasWm.Core.Shortcuts;

namespace CanvasWm.Win32.Hotkeys;

/// <summary>FIFO between the window procedure and the Pressed event (B15). No OS access.</summary>
internal sealed class PressQueue
{
    private readonly Action<Action> _post;
    private readonly Action<KeyCombo> _deliver;
    private readonly object _gate = new();
    private readonly Queue<KeyCombo> _queue = new();
    private bool _pending;
    private bool _running;
    private bool _closed;
    private Thread? _postingThread;

    internal PressQueue(Action<Action> post, Action<KeyCombo> deliver)
    {
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
    }

    /// <summary>Number of presses waiting for delivery.</summary>
    internal int QueuedCount
    {
        get { lock (_gate) { return _queue.Count; } }
    }

    /// <summary>Appends a press; posts a drain unless one is pending or running.</summary>
    internal void Enqueue(KeyCombo combo)
    {
        bool needPost;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _queue.Enqueue(combo);
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

    /// <summary>Drops queued presses; later Enqueue calls and pending drains deliver nothing.</summary>
    internal void Close()
    {
        lock (_gate)
        {
            _closed = true;
            _queue.Clear();
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
            if (_closed)
            {
                return;
            }

            _running = true;
        }

        try
        {
            while (true)
            {
                KeyCombo c;
                lock (_gate)
                {
                    if (_closed || _queue.Count == 0)
                    {
                        _running = false;
                        return;
                    }

                    c = _queue.Dequeue();
                }

                _deliver(c);
            }
        }
        catch
        {
            bool repost;
            lock (_gate)
            {
                _running = false;
                repost = !_closed && _queue.Count > 0 && !_pending;
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
