using CanvasWm.Win32.Hosting;
using CanvasWm.Win32.Tests.WindowEnumSpec;

namespace CanvasWm.Win32.Tests.HostSpec;

/// <summary>AC20-AC24 of specs/app-host.md. Close any running CanvasWM first: SignalExisting and
/// FindWindow look at every host window on the desktop.</summary>
public sealed class HostWindowTests
{
    // ---- AC20 ----

    [Fact]
    public void AC20_WindowShape()
    {
        using var rig = new HostRig();
        nint h = rig.Hwnd;
        Assert.NotEqual((nint)0, h);
        Assert.False(HostNative.IsWindowVisible(h));
        Assert.Equal(HostNative.GetDesktopWindow(), HostNative.GetAncestor(h, HostNative.GA_PARENT));
        Assert.NotEqual((nint)0, HostNative.GetWindowLongPtrW(h, HostNative.GWL_EXSTYLE) & HostNative.WS_EX_TOOLWINDOW);
        Assert.Equal((int)rig.NativeThreadId, Native.PidTid(h).Tid);
        Assert.Equal("CanvasWm.HostWindow", Win32HostWindow.WindowClassName);
        Assert.Equal("CanvasWm.ActivateInstance", Win32HostWindow.ActivateMessageName);
    }

    [Fact]
    public void AC20_TwoHostsCoexist()
    {
        using var a = new HostRig();
        using var b = new HostRig();
        Assert.NotEqual((nint)0, a.Hwnd);
        Assert.NotEqual((nint)0, b.Hwnd);
        Assert.NotEqual(a.Hwnd, b.Hwnd);
    }

    [Fact]
    public void AC20_Dispose()
    {
        using var rig = new HostRig();
        Assert.Throws<InvalidOperationException>(() => rig.Host.Dispose());
        Assert.True(Native.IsWindow(rig.Hwnd));
        rig.Invoke(() => rig.Host.Dispose());
        Assert.False(Native.IsWindow(rig.Hwnd));
        rig.Invoke(() => rig.Host.Dispose());   // second Dispose is a no-op
    }

    // ---- AC21 ----

    [Fact]
    public void AC21_PostDoesNotRunInline_AndFifo()
    {
        using var rig = new HostRig();
        var order = new List<int>();
        bool ranInline = rig.Invoke(() =>
        {
            bool ran = false;
            rig.Host.Post(() => ran = true);
            return ran;
        });
        Assert.False(ranInline);

        rig.Invoke(() =>
        {
            for (int i = 0; i < 1000; i++) { int k = i; rig.Host.Post(() => order.Add(k)); }
        });
        Assert.True(rig.PumpUntil(() => rig.Invoke(() => order.Count) == 1000));
        Assert.Equal(Enumerable.Range(0, 1000), order);
        Assert.Equal(0, rig.Host.PendingCount);
    }

    [Fact]
    public void AC21_ConcurrentPostersAllRunOnceOnOwnerInPerThreadOrder()
    {
        using var rig = new HostRig();
        var seen = new List<(int Poster, int Seq)>();
        var runThreads = new HashSet<int>();
        var threads = Enumerable.Range(0, 4).Select(p => new Thread(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                int seq = i;
                rig.Host.Post(() => { seen.Add((p, seq)); runThreads.Add(Environment.CurrentManagedThreadId); });
            }
        })).ToArray();
        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();
        Assert.True(rig.PumpUntil(() => rig.Invoke(() => seen.Count) >= 4000, 15000));
        Assert.Equal(0, rig.Host.PendingCount);
        rig.Invoke(() =>
        {
            Assert.Equal(4000, seen.Count);
            Assert.Equal(4000, seen.Distinct().Count());
            Assert.Equal(new[] { rig.ManagedThreadId }, runThreads.ToArray());
            for (int p = 0; p < 4; p++)
                Assert.Equal(Enumerable.Range(0, 1000), seen.Where(s => s.Poster == p).Select(s => s.Seq));
        });
    }

    [Fact]
    public void AC21_SelfRepostingActionDoesNotStarveNotifications()
    {
        using var rig = new HostRig(autoPump: false);
        int loops = 0;
        bool stop = false;
        Action? spin = null;
        spin = () => { loops++; if (!stop) rig.Host.Post(spin!); };
        rig.Invoke(() =>
        {
            rig.Host.Post(spin);
            Native.PostMessageW(rig.Hwnd, HostNative.WM_DISPLAYCHANGE, 0, 0);
        });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int dispatched = rig.PumpOnce();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5));
        Assert.True(dispatched > 0);
        Assert.Contains(HostNotification.DisplayChanged, rig.Notifications);
        rig.Invoke(() => stop = true);
        rig.PumpUntil(() => rig.Host.PendingCount == 0);
    }

    [Fact]
    public void AC21_ActionPostedInsideDrainRunsAfterCurrentReturns()
    {
        using var rig = new HostRig();
        var log = new List<string>();
        rig.Host.Post(() =>
        {
            log.Add("A:start");
            rig.Host.Post(() => log.Add("B"));
            log.Add("A:end");
        });
        Assert.True(rig.PumpUntil(() => rig.Invoke(() => log.Count) == 3));
        Assert.Equal(new[] { "A:start", "A:end", "B" }, log);
    }

    [Fact]
    public void AC21_PostAfterDisposeIsDropped()
    {
        using var rig = new HostRig();
        rig.Invoke(() => rig.Host.Dispose());
        bool ran = false;
        rig.Invoke(() => rig.Host.Post(() => ran = true));
        Assert.Null(Record.Exception(() => rig.Host.Post(() => ran = true)));
        rig.Invoke(() => Win32HostWindow.PumpPending(20));
        Thread.Sleep(50);
        Assert.False(ran);
        Assert.Throws<ArgumentNullException>(() => rig.Host.Post(null!));
    }

    // ---- AC22 ----

    [Fact]
    public void AC22_ThrowingActionGoesToOnErrorOnOwnerThread()
    {
        using var rig = new HostRig();
        bool later = false;
        var boom = new InvalidOperationException("boom");
        rig.Host.Post(() => throw boom);
        rig.Host.Post(() => later = true);
        Assert.True(rig.PumpUntil(() => rig.Invoke(() => later)));
        Assert.True(rig.Errors.TryPeek(out var e));
        Assert.Same(boom, e.Error);
        Assert.Equal(rig.ManagedThreadId, e.ThreadId);
    }

    [Fact]
    public void AC22_ThrowingNotifiedHandlerGoesToOnError()
    {
        using var rig = new HostRig();
        var boom = new InvalidOperationException("handler");
        rig.Invoke(() => rig.Host.Notified += _ => throw boom);
        rig.Send(HostNative.WM_DISPLAYCHANGE);
        Assert.Same(boom, Assert.Single(rig.Errors).Error);
    }

    [Fact]
    public void AC22_ThrowingOnErrorDoesNotBreakLaterPosts()
    {
        using var rig = new HostRig();
        rig.OnErrorThrows = true;
        bool later = false;
        rig.Host.Post(() => throw new InvalidOperationException("x"));
        rig.Host.Post(() => later = true);
        Assert.True(rig.PumpUntil(() => rig.Invoke(() => later)));
        rig.OnErrorThrows = false;
    }

    // ---- AC23 ----

    private static (HostNotification? Raised, nint Result) SendAndObserve(HostRig rig, uint msg, nuint w = 0, nint l = 0)
    {
        while (rig.Notifications.TryDequeue(out _)) { }
        nint r = rig.Send(msg, w, l);
        return (rig.Notifications.TryDequeue(out var n) ? n : null, r);
    }

    [Fact]
    public void AC23_NotificationMapping()
    {
        using var rig = new HostRig();
        Assert.Equal(((HostNotification?)HostNotification.DisplayChanged, (nint)0), SendAndObserve(rig, HostNative.WM_DISPLAYCHANGE));
        Assert.Equal(((HostNotification?)HostNotification.SettingChanged, (nint)0), SendAndObserve(rig, HostNative.WM_SETTINGCHANGE, 0x2F));
        Assert.Equal(((HostNotification?)HostNotification.SettingChanged, (nint)0), SendAndObserve(rig, HostNative.WM_SETTINGCHANGE, 0));
        Assert.Equal(((HostNotification?)HostNotification.DpiChanged, (nint)0), SendAndObserve(rig, HostNative.WM_DPICHANGED));
        var pr = SendAndObserve(rig, HostNative.WM_POWERBROADCAST, 0x12);
        Assert.Equal(HostNotification.PowerResumed, pr.Raised);
        Assert.NotEqual((nint)0, pr.Result);
        var po = SendAndObserve(rig, HostNative.WM_POWERBROADCAST, 0x4);
        Assert.Null(po.Raised);
        Assert.NotEqual((nint)0, po.Result);
        var q = SendAndObserve(rig, HostNative.WM_QUERYENDSESSION);
        Assert.Equal(HostNotification.QueryEndSession, q.Raised);
        Assert.NotEqual((nint)0, q.Result);
        Assert.Equal(((HostNotification?)HostNotification.EndSession, (nint)0), SendAndObserve(rig, HostNative.WM_ENDSESSION, 1));
        Assert.Equal(((HostNotification?)null, (nint)0), SendAndObserve(rig, HostNative.WM_ENDSESSION, 0));
        Assert.Equal(((HostNotification?)HostNotification.CloseRequested, (nint)0), SendAndObserve(rig, HostNative.WM_CLOSE));
        Assert.True(Native.IsWindow(rig.Hwnd));
        uint activate = HostNative.RegisterWindowMessageW(Win32HostWindow.ActivateMessageName);
        Assert.Equal(((HostNotification?)HostNotification.InstanceActivated, (nint)0), SendAndObserve(rig, activate));
        Assert.Null(SendAndObserve(rig, HostNative.WM_USER + 5).Raised);
    }

    [Fact]
    public void AC23_PostedMessagesAreNotifiedToo()
    {
        using var rig = new HostRig();
        Native.PostMessageW(rig.Hwnd, HostNative.WM_DISPLAYCHANGE, 0, 0);
        Native.PostMessageW(rig.Hwnd, HostNative.WM_CLOSE, 0, 0);
        Assert.True(rig.PumpUntil(() => rig.Notifications.Count >= 2));
        Assert.Equal(new[] { HostNotification.DisplayChanged, HostNotification.CloseRequested }, rig.Notifications.ToArray());
    }

    // ---- AC24 ----

    [Fact]
    public void AC24_SignalExistingWithLiveHost()
    {
        using var rig = new HostRig();
        bool posted = rig.Invoke(Win32HostWindow.SignalExisting);
        Assert.True(posted);
        Assert.True(rig.PumpUntil(() => rig.Notifications.Contains(HostNotification.InstanceActivated)));
    }

    [Fact]
    public void AC24_SignalExistingWithoutHost()
    {
        if (Native.FindWindowW(Win32HostWindow.WindowClassName, null) != 0) return;   // a real CanvasWM is running; see windows-verification
        Assert.False(Win32HostWindow.SignalExisting());
    }

    [Fact]
    public void AC24_PumpPending()
    {
        using var rig = new HostRig(autoPump: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => Win32HostWindow.PumpPending(-1));
        rig.PumpOnce();   // drain anything left from window creation
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int n = rig.Invoke(() => Win32HostWindow.PumpPending(50));
        sw.Stop();
        Assert.Equal(0, n);
        Assert.InRange(sw.ElapsedMilliseconds, 40, 1000);
    }
}
