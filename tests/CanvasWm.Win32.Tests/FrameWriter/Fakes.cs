using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.FrameWriterSpec;

/// <summary>Scripted per-window state for FakeOps.</summary>
internal sealed class FakeWin
{
    public int Pid = 100;
    public int Tid = 200;
    public bool Live = true, Hung, Minimized, Maximized, Visible = true, Cloaked;
    public int MonitorIdx; // index into FakeOps.MonitorList; -1 = unknown
    public PixelRect? WindowRect = new(93, 100, 907, 707);
    public PixelRect? VisibleRect = new(100, 100, 900, 700);
    /// <summary>FIFO of scripted ReadDipFrame results (null entries are failed reads).</summary>
    public readonly Queue<Rect?> Reads = new();
    /// <summary>Returned by ReadDipFrame once Reads is empty.</summary>
    public Rect? Current;
    /// <summary>Scripted SetWindowPos results; true once empty.</summary>
    public readonly Queue<bool> SetPosResults = new();
    public bool ThrowOnRead;
}

/// <summary>INativeWindowOps over scripted state. Every call is recorded as "Name(arg,...)".</summary>
internal sealed class FakeOps : INativeWindowOps
{
    internal static readonly MonitorInfo MonA = new(1, new PixelRect(0, 0, 1920, 1080), 96, new Rect(0, 0, 1920, 1080));
    internal static readonly MonitorInfo MonB = new(2, new PixelRect(1920, 0, 3840, 1440), 144, new Rect(1920, 0, 1280, 960));

    public readonly List<string> Calls = new();
    public List<MonitorInfo> MonitorList = new() { MonA, MonB };
    public List<nint> Top = new();
    public readonly Dictionary<nint, FakeWin> W = new();

    public bool DeferResult = true;
    public bool DeferThrows;
    public List<(nint Hwnd, PixelRect Outer)>? LastDefer;

    public (int X, int Y)? Cursor;
    public nint PointMonitor = 1;
    public bool Swapped;
    public readonly HashSet<int> KeysDown = new();
    public bool ForegroundResult = true;

    /// <summary>Called after every SetWindowPos (handle, outer, positionOnly, 1-based call number).</summary>
    public Action<FakeOps, nint, PixelRect, bool, int>? OnSetWindowPos;
    private int _setPosCount;

    public FakeWin Add(nint h, Action<FakeWin>? configure = null)
    {
        var w = new FakeWin();
        configure?.Invoke(w);
        W[h] = w;
        return w;
    }

    public void Reset() => Calls.Clear();

    public static string Name(string call) => call.Contains('(') ? call[..call.IndexOf('(')] : call;

    public IEnumerable<string> Names() => Calls.Select(Name);

    public IEnumerable<string> Named(string name) => Calls.Where(c => Name(c) == name);

    private void Rec(string call) => Calls.Add(call);

    public IReadOnlyList<MonitorInfo> Monitors() { Rec("Monitors"); return MonitorList.ToArray(); }
    public IReadOnlyList<nint> TopLevelWindows() { Rec("TopLevelWindows"); return Top.ToArray(); }

    public bool IsLive(nint h, int processId, int threadId)
    {
        Rec($"IsLive({h})");
        return W.TryGetValue(h, out FakeWin? w) && w.Live && w.Pid == processId && w.Tid == threadId;
    }

    public bool IsHung(nint h) { Rec($"IsHung({h})"); return W[h].Hung; }
    public bool IsMinimized(nint h) { Rec($"IsMinimized({h})"); return W[h].Minimized; }
    public bool IsMaximized(nint h) { Rec($"IsMaximized({h})"); return W[h].Maximized; }
    public bool IsVisible(nint h) { Rec($"IsVisible({h})"); return W[h].Visible; }
    public bool IsCloaked(nint h) { Rec($"IsCloaked({h})"); return W[h].Cloaked; }
    public int MonitorIndexOf(nint h, IReadOnlyList<MonitorInfo> monitors) { Rec($"MonitorIndexOf({h})"); return W[h].MonitorIdx; }
    public PixelRect? WindowRect(nint h) { Rec($"WindowRect({h})"); return W[h].WindowRect; }
    public PixelRect? VisibleRect(nint h) { Rec($"VisibleRect({h})"); return W[h].VisibleRect; }

    public Rect? ReadDipFrame(nint h)
    {
        Rec($"ReadDipFrame({h})");
        FakeWin w = W[h];
        if (w.ThrowOnRead)
        {
            throw new InvalidOperationException("scripted read failure");
        }

        return w.Reads.Count > 0 ? w.Reads.Dequeue() : w.Current;
    }

    public void RestoreNoActivate(nint h) { Rec($"RestoreNoActivate({h})"); W[h].Maximized = false; }
    public void Restore(nint h) { Rec($"Restore({h})"); W[h].Minimized = false; }

    public bool TryDeferBatch(IReadOnlyList<(nint Hwnd, PixelRect Outer)> entries)
    {
        Rec("TryDeferBatch(" + string.Join(";", entries.Select(e => $"{e.Hwnd}:{e.Outer.Left},{e.Outer.Top},{e.Outer.Right},{e.Outer.Bottom}")) + ")");
        LastDefer = entries.ToList();
        if (DeferThrows)
        {
            throw new InvalidOperationException("scripted batch failure");
        }

        return DeferResult;
    }

    public bool SetWindowPos(nint h, PixelRect outer, bool positionOnly)
    {
        Rec($"SetWindowPos({h},{outer.Left},{outer.Top},{outer.Right},{outer.Bottom},{positionOnly})");
        FakeWin w = W[h];
        bool ok = w.SetPosResults.Count > 0 ? w.SetPosResults.Dequeue() : true;
        OnSetWindowPos?.Invoke(this, h, outer, positionOnly, ++_setPosCount);
        return ok;
    }

    public bool SetForeground(nint h) { Rec($"SetForeground({h})"); return ForegroundResult; }

    public (int X, int Y)? CursorPos() { Rec("CursorPos"); return Cursor; }
    public nint MonitorFromPoint(int x, int y) { Rec($"MonitorFromPoint({x},{y})"); return PointMonitor; }
    public bool ButtonsSwapped() { Rec("ButtonsSwapped"); return Swapped; }
    public bool AsyncKeyDown(int virtualKey) { Rec($"AsyncKeyDown({virtualKey})"); return KeysDown.Contains(virtualKey); }
}

/// <summary>Dictionary-backed managed-window map.</summary>
internal sealed class FakeMap : IManagedWindowMap
{
    private readonly Dictionary<WindowKey, ManagedWindowIdentity> _byKey = new();
    private readonly Dictionary<ulong, WindowKey> _byHandle = new();

    public int ResolveCalls;

    public WindowKey Add(string key, nint h, int pid = 100, int tid = 200)
    {
        var k = new WindowKey(key);
        ulong handle = HandleConversion.ToHandle(h);
        _byKey[k] = new ManagedWindowIdentity(handle, pid, tid);
        _byHandle[handle] = k;
        return k;
    }

    public void Add(WindowKey k, ManagedWindowIdentity id)
    {
        _byKey[k] = id;
        _byHandle[id.Handle] = k;
    }

    public ManagedWindowIdentity? Resolve(WindowKey key)
    {
        ResolveCalls++;
        return _byKey.TryGetValue(key, out ManagedWindowIdentity id) ? id : null;
    }

    public WindowKey? KeyOf(ulong handle) => _byHandle.TryGetValue(handle, out WindowKey? k) ? k : null;
}

/// <summary>post that runs actions only on request, FIFO.</summary>
internal sealed class ManualPost
{
    public readonly List<Action> Actions = new();
    public void Post(Action a) { lock (Actions) { Actions.Add(a); } }

    public int Count { get { lock (Actions) { return Actions.Count; } } }

    /// <summary>Runs everything posted so far and anything posted while running, FIFO.</summary>
    public void RunAll()
    {
        int i = 0;
        while (true)
        {
            Action a;
            lock (Actions)
            {
                if (i >= Actions.Count) { Actions.Clear(); return; }
                a = Actions[i++];
            }

            a();
        }
    }
}

/// <summary>A recording writer: work items are held until Step.</summary>
internal sealed class ManualWriter
{
    public readonly List<Action> Items = new();
    public void Enqueue(Action a) => Items.Add(a);

    public void StepAll()
    {
        var copy = Items.ToList();
        Items.Clear();
        foreach (Action a in copy) { a(); }
    }

    public void StepOne()
    {
        Action a = Items[0];
        Items.RemoveAt(0);
        a();
    }
}

/// <summary>Everything a Win32WindowSystem test needs.</summary>
internal sealed class SysRig : IDisposable
{
    public readonly FakeOps Ops = new();
    public readonly FakeMap Map = new();
    public readonly ManualPost Post = new();
    public readonly ManualWriter Writer = new();
    public readonly List<string> Logs = new();
    public readonly Win32WindowSystem Sys;

    public SysRig()
    {
        Sys = new Win32WindowSystem(Post.Post, Map, Logs.Add, Ops, Writer.Enqueue);
    }

    /// <summary>Adds a default window (live, on A, default insets) with this key.</summary>
    public WindowKey Win(string key, nint h, Action<FakeWin>? configure = null)
    {
        Ops.Add(h, configure);
        return Map.Add(key, h);
    }

    public void Dispose() => Sys.Dispose();

    public static Rect R(double x, double y, double w, double h) => new(x, y, w, h);
}
