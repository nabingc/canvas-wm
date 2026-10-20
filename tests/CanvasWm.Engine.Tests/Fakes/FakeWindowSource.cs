using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tests.Fakes;

/// <summary>Scriptable OS window list with synchronous event delivery.</summary>
public sealed class FakeWindowSource : IWindowSource
{
    private readonly List<OsWindowFacts> _windows = new();   // front first
    private readonly List<Sub> _subs = new();
    private int _depth;

    private sealed class Sub
    {
        public Sub(FakeWindowSource owner, Action<RawWindowEvent> handler)
        {
            Owner = owner;
            Handler = handler;
        }

        public FakeWindowSource Owner { get; }

        public Action<RawWindowEvent> Handler { get; }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Sub _sub;

        public Subscription(Sub sub) => _sub = sub;

        public void Dispose() => _sub.Owner._subs.Remove(_sub);
    }

    /// <summary>Adds a window as frontmost, or replaces the facts with the same Handle in place.</summary>
    public void Put(OsWindowFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        int i = _windows.FindIndex(w => w.Handle == facts.Handle);
        if (i >= 0) _windows[i] = facts;
        else _windows.Insert(0, facts);
    }

    /// <summary>The window no longer exists. Unknown handle: no-op.</summary>
    public void Remove(ulong handle) => _windows.RemoveAll(w => w.Handle == handle);

    /// <summary>Listed handles first (in the given order), then the rest in their previous order.</summary>
    public void SetZOrder(IEnumerable<ulong> frontToBack)
    {
        ArgumentNullException.ThrowIfNull(frontToBack);
        var result = new List<OsWindowFacts>();
        foreach (ulong h in frontToBack)
        {
            OsWindowFacts? w = _windows.FirstOrDefault(x => x.Handle == h);
            if (w is not null && !result.Contains(w)) result.Add(w);
        }

        foreach (OsWindowFacts w in _windows)
        {
            if (!result.Contains(w)) result.Add(w);
        }

        _windows.Clear();
        _windows.AddRange(result);
    }

    /// <summary>Current facts for handle, or null.</summary>
    public OsWindowFacts? Get(ulong handle) => _windows.FirstOrDefault(w => w.Handle == handle);

    /// <summary>Replaces the facts of handle with change(current).</summary>
    /// <exception cref="KeyNotFoundException">Unknown handle.</exception>
    public void Update(ulong handle, Func<OsWindowFacts, OsWindowFacts> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        OsWindowFacts current = Get(handle) ?? throw new KeyNotFoundException($"No window {handle}.");
        Put(change(current));
    }

    /// <summary>Delivers synchronously to every live subscriber, in subscription order.</summary>
    public void Raise(RawWindowEvent e)
    {
        foreach (Sub s in _subs.ToList())
        {
            if (_subs.Contains(s)) s.Handler(e);
        }
    }

    /// <summary>Delivers an event built from kind and handle.</summary>
    public void Raise(RawWindowEventKind kind, ulong handle) => Raise(new RawWindowEvent(kind, handle));

    /// <summary>Number of live subscriptions.</summary>
    public int SubscriberCount => _subs.Count;

    /// <summary>Number of Enumerate calls.</summary>
    public int EnumerateCount { get; private set; }

    /// <summary>Number of Describe calls.</summary>
    public int DescribeCount { get; private set; }

    /// <summary>Deepest nesting of Enumerate calls observed.</summary>
    public int MaxEnumerateDepth { get; private set; }

    /// <summary>Hook run inside Enumerate, before it returns.</summary>
    public Action? OnEnumerate { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<OsWindowFacts> Enumerate()
    {
        EnumerateCount++;
        _depth++;
        try
        {
            MaxEnumerateDepth = Math.Max(MaxEnumerateDepth, _depth);
            OnEnumerate?.Invoke();
            return _windows.ToList();
        }
        finally
        {
            _depth--;
        }
    }

    /// <inheritdoc />
    public OsWindowFacts? Describe(ulong handle)
    {
        DescribeCount++;
        return Get(handle);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Action<RawWindowEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var s = new Sub(this, handler);
        _subs.Add(s);
        return new Subscription(s);
    }
}
