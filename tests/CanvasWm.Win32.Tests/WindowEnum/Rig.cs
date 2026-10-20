using System.Collections.Concurrent;
using System.Diagnostics;
using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.WindowEnumSpec;

internal readonly record struct Seen(RawWindowEvent Event, int ThreadId, bool InsideCall);

/// <summary>A message-loop thread plus a Win32WindowSource constructed on it, and helpers.</summary>
internal sealed class Rig : IDisposable
{
    internal readonly MessageLoopThread Loop = new();
    internal readonly Win32WindowSource Source;
    internal readonly ConcurrentQueue<Seen> Events = new();
    internal volatile bool InsideCall;
    private readonly List<nint> _windows = [];

    internal Rig()
    {
        Source = Loop.Invoke(() => new Win32WindowSource(Loop.Post));
    }

    internal IDisposable Subscribe() =>
        Loop.Invoke(() => Source.Subscribe(e => Events.Enqueue(new Seen(e, Environment.CurrentManagedThreadId, InsideCall))));

    internal IReadOnlyList<OsWindowFacts> Enumerate() => Loop.Invoke(() =>
    {
        InsideCall = true;
        try { return Source.Enumerate(); } finally { InsideCall = false; }
    });

    internal OsWindowFacts? Describe(ulong handle) => Loop.Invoke(() =>
    {
        InsideCall = true;
        try { return Source.Describe(handle); } finally { InsideCall = false; }
    });

    internal nint CreateWindow(uint exStyle, string cls, string? title, uint style, int x, int y, int w, int h, nint parent = 0)
    {
        nint hwnd = Loop.Invoke(() => Native.WithPmv2(() =>
            Native.CreateWindowExW(exStyle, cls, title, style, x, y, w, h, parent, 0, 0, 0)));
        Assert.NotEqual((nint)0, hwnd);
        _windows.Add(hwnd);
        return hwnd;
    }

    internal static ulong H(nint hwnd) => HandleConversion.ToHandle(hwnd);

    /// <summary>Waits until the recorded events contain, as an ordered subsequence, the kinds for the handle.</summary>
    internal bool WaitForKinds(ulong handle, params RawWindowEventKind[] kinds) =>
        WaitFor(() => IsSubsequence(handle, kinds), TimeSpan.FromSeconds(5));

    internal bool IsSubsequence(ulong handle, RawWindowEventKind[] kinds)
    {
        int i = 0;
        foreach (Seen s in Events.ToArray())
        {
            if (s.Event.Handle == handle && i < kinds.Length && s.Event.Kind == kinds[i])
            {
                i++;
            }
        }

        return i == kinds.Length;
    }

    internal int CountFor(ulong handle) => Events.ToArray().Count(s => s.Event.Handle == handle);

    internal static bool WaitFor(Func<bool> cond, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (cond()) { return true; }
            Thread.Sleep(25);
        }

        return cond();
    }

    internal OsWindowFacts? WaitForFacts(ulong handle, Func<OsWindowFacts, bool> pred, TimeSpan timeout)
    {
        OsWindowFacts? last = null;
        WaitFor(() => (last = Describe(handle)) is { } f && pred(f), timeout);
        return last;
    }

    public void Dispose()
    {
        try
        {
            Loop.Invoke(() =>
            {
                foreach (nint w in _windows.AsEnumerable().Reverse())
                {
                    if (Native.IsWindow(w)) { Native.DestroyWindow(w); }
                }

                Source.Dispose();
            });
        }
        finally
        {
            Loop.Dispose();
        }
    }
}
