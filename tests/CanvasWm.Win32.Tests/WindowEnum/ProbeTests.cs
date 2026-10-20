using System.Diagnostics;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Windows;
using Xunit.Abstractions;
using K = CanvasWm.Engine.Services.RawWindowEventKind;

namespace CanvasWm.Win32.Tests.WindowEnumSpec;

/// <summary>AC19-AC26: facts and events against an out-of-process PowerShell/WinForms probe window
/// (A20). Requires an interactive Windows session, a 64-bit test host and Windows PowerShell 5.1.
/// Not runnable on the Mac.</summary>
public sealed class ProbeTests : IDisposable
{
    private readonly Rig _rig = new();
    private readonly List<ProbeProcess> _probes = [];
    private readonly ITestOutputHelper _out;

    public ProbeTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        foreach (ProbeProcess p in _probes) { p.Dispose(); }
        _rig.Dispose();
    }

    private ProbeProcess NewProbe()
    {
        var p = ProbeProcess.Start();
        _probes.Add(p);
        return p;
    }

    private static nint Hwnd(ProbeProcess p) => HandleConversion.ToHwnd(p.Handle);

    [Fact]
    public void AC19_ProbeFacts()
    {
        ProbeProcess probe = NewProbe();
        var f = Assert.Single(_rig.Enumerate(), x => x.Handle == probe.Handle);
        Assert.False(f.IsOwnProcess);
        Assert.Equal(probe.ImagePath.ToLowerInvariant(), f.AppId);
        string? desc = FileVersionInfo.GetVersionInfo(probe.ImagePath).FileDescription;
        Assert.Equal(string.IsNullOrWhiteSpace(desc) ? "powershell" : desc.Trim(), f.AppName);
        Assert.StartsWith("CanvasWmProbe-", f.Title, StringComparison.Ordinal);
        Assert.True(f.IsAccessible);
        var (pid, tid) = Native.PidTid(Hwnd(probe));
        Assert.Equal(pid, f.ProcessId);
        Assert.Equal(tid, f.ThreadId);
        Assert.Equal(probe.ProcessId, f.ProcessId);
    }

    [Fact]
    public void AC20_ProbeEvents()
    {
        ProbeProcess probe = NewProbe();
        _rig.Subscribe();
        nint hwnd = Hwnd(probe);
        ulong h = probe.Handle;

        Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
        Assert.True(_rig.WaitForKinds(h, K.Minimized), "Minimized");
        Native.ShowWindow(hwnd, Native.SW_RESTORE);
        Assert.True(_rig.WaitForKinds(h, K.Minimized, K.Restored), "Restored");

        Native.GetWindowRect(hwnd, out Native.RECT r);
        Native.SetWindowPos(hwnd, 0, r.Left + 50, r.Top, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        Assert.True(_rig.WaitForKinds(h, K.LocationChanged), "LocationChanged");

        probe.Send("title X");
        Assert.True(_rig.WaitForKinds(h, K.TitleChanged), "TitleChanged");
        Assert.Equal("X", _rig.WaitForFacts(h, f => f.Title == "X", TimeSpan.FromSeconds(5))?.Title);

        Native.ShowWindow(hwnd, Native.SW_HIDE);
        Native.ShowWindow(hwnd, Native.SW_SHOW);
        Assert.True(_rig.WaitForKinds(h, K.Hidden, K.Shown), "Hidden then Shown");

        ProbeProcess second = NewProbe();
        Assert.True(_rig.WaitForKinds(second.Handle, K.Created, K.Shown), "second probe Created then Shown");

        Native.PostMessageW(hwnd, Native.WM_CLOSE, 0, 0);
        Assert.True(_rig.WaitForKinds(h, K.Destroyed), "Destroyed");

        Assert.All(_rig.Events.ToArray(), s => Assert.Equal(_rig.Loop.ManagedThreadId, s.ThreadId));
    }

    [Fact]
    public void AC20_NoHandlerCallInsideEnumerateOrDescribe()
    {
        ProbeProcess probe = NewProbe();
        _rig.Subscribe();
        for (int i = 0; i < 10; i++)
        {
            Native.ShowWindow(Hwnd(probe), i % 2 == 0 ? Native.SW_MINIMIZE : Native.SW_RESTORE);
            _rig.Enumerate();
            _rig.Describe(probe.Handle);
        }

        Assert.True(_rig.WaitForKinds(probe.Handle, K.Minimized));
        Assert.DoesNotContain(_rig.Events.ToArray(), s => s.InsideCall);
    }

    [Fact]
    public void AC21_OwnProcessEventsAreSkipped_ProbeStillDelivers()
    {
        ProbeProcess probe = NewProbe();
        nint own = _rig.CreateWindow(0, "Static", "CanvasWmSelf", Native.WS_OVERLAPPEDWINDOW | Native.WS_VISIBLE, 100, 100, 800, 600);
        _rig.Subscribe();

        _rig.Loop.Invoke(() =>
        {
            Native.ShowWindow(own, Native.SW_MINIMIZE);
            Native.SetWindowPos(own, 0, 300, 300, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            Native.SetWindowTextW(own, "renamed");
        });
        Native.ShowWindow(Hwnd(probe), Native.SW_MINIMIZE);
        Assert.True(_rig.WaitForKinds(probe.Handle, K.Minimized));
        Thread.Sleep(1000);
        Assert.Equal(0, _rig.CountFor(Rig.H(own)));
    }

    [Fact]
    public void AC22_ChildWindowsFiltered_FocusReportsTopLevel()
    {
        ProbeProcess probe = NewProbe();
        _rig.Subscribe();
        probe.Send("child");
        ulong child = probe.WaitChildHandle();
        Thread.Sleep(1000);
        Assert.DoesNotContain(_rig.Events.ToArray(),
            s => s.Event.Handle == child && s.Event.Kind is K.Created or K.LocationChanged);

        Native.SetForegroundWindow(Hwnd(probe));
        probe.Send("focus");
        Assert.True(_rig.WaitForKinds(probe.Handle, K.FocusChanged), "FocusChanged with top-level handle");
        Assert.DoesNotContain(_rig.Events.ToArray(), s => s.Event.Handle == child);
    }

    [Fact]
    public void AC23_SubscriptionLifecycle()
    {
        ProbeProcess probe = NewProbe();
        Assert.Equal(0, _rig.Source.ActiveHookCount);
        IDisposable s1 = _rig.Subscribe();
        Assert.Equal(6, _rig.Source.ActiveHookCount);
        IDisposable s2 = _rig.Subscribe();
        Assert.Equal(6, _rig.Source.ActiveHookCount);

        Assert.Throws<InvalidOperationException>(() => _rig.Source.Subscribe(_ => { }));

        _rig.Loop.Invoke(s1.Dispose);
        Assert.Equal(6, _rig.Source.ActiveHookCount);
        Native.ShowWindow(Hwnd(probe), Native.SW_MINIMIZE);
        Assert.True(_rig.WaitForKinds(probe.Handle, K.Minimized), "remaining subscription still delivers");

        // Dispose the last subscription on a non-loop thread: delivery stops at once, unhook is posted.
        s2.Dispose();
        int before = _rig.CountFor(probe.Handle);
        Assert.True(Rig.WaitFor(() => _rig.Source.ActiveHookCount == 0, TimeSpan.FromSeconds(5)), "hooks removed");
        Native.ShowWindow(Hwnd(probe), Native.SW_RESTORE);
        Native.ShowWindow(Hwnd(probe), Native.SW_MINIMIZE);
        Thread.Sleep(1000);
        Assert.Equal(before, _rig.CountFor(probe.Handle));

        _rig.Subscribe();
        Assert.Equal(6, _rig.Source.ActiveHookCount);
        _rig.Loop.Invoke(() => _rig.Source.Dispose());
        Assert.Equal(0, _rig.Source.ActiveHookCount);
        Assert.Throws<ObjectDisposedException>(() => _rig.Loop.Invoke(() => _rig.Source.Subscribe(_ => { })));
        Assert.NotEmpty(_rig.Enumerate());
    }

    [Fact]
    public void AC23_Subscribe_OnLoopThread_NullHandler_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _rig.Loop.Invoke(() => _rig.Source.Subscribe(null!)));
        Assert.Throws<ArgumentNullException>(() => new Win32WindowSource(null!));
        Assert.Null(_rig.Describe(0));
    }

    [Fact]
    public void AC24_GcSafety_HooksSurviveCollections()
    {
        ProbeProcess probe = NewProbe();
        _rig.Subscribe();
        _rig.Loop.Invoke(() =>
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        });
        Native.ShowWindow(Hwnd(probe), Native.SW_MINIMIZE);
        Native.ShowWindow(Hwnd(probe), Native.SW_RESTORE);
        Assert.True(_rig.WaitForKinds(probe.Handle, K.Minimized, K.Restored));
    }

    [Fact]
    public void AC25_HungApp_EnumerateAndDescribeDoNotBlock()
    {
        ProbeProcess probe = NewProbe();
        probe.Send("hang");
        Thread.Sleep(6000);

        for (int i = 0; i < 3; i++)
        {
            long ms = _rig.Loop.Invoke(() =>
            {
                var sw = Stopwatch.StartNew();
                _rig.Source.Enumerate();
                return sw.ElapsedMilliseconds;
            });
            Assert.True(ms < 1000, $"Enumerate #{i} took {ms} ms");
        }

        var f = Assert.Single(_rig.Enumerate(), x => x.Handle == probe.Handle);
        Assert.True(f.IsHung);
        Assert.Equal(probe.Title, f.Title);

        long d = _rig.Loop.Invoke(() =>
        {
            var sw = Stopwatch.StartNew();
            _rig.Source.Describe(probe.Handle);
            return sw.ElapsedMilliseconds;
        });
        Assert.True(d < 200, $"Describe took {d} ms");
    }

    [Fact]
    public void AC26_FixedSizeWindowFacts()
    {
        ProbeProcess probe = NewProbe();
        probe.Send("fixed");
        var f = _rig.WaitForFacts(probe.Handle, x => !x.IsResizable, TimeSpan.FromSeconds(5));
        Assert.NotNull(f);
        Assert.False(f!.IsResizable);
        Assert.False(f.HasMaximizeButton);
        Assert.True(f.MaximizeEnabled is null or false, $"MaximizeEnabled was {f.MaximizeEnabled}");
        _out.WriteLine($"AC26 MaximizeEnabled outcome: {(f.MaximizeEnabled is null ? "null (button invisible)" : "false (disabled)")}");
    }
}
