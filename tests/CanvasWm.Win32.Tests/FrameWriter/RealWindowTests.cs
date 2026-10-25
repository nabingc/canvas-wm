using System.Diagnostics;
using System.Runtime.InteropServices;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Parking;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Tests.WindowEnumSpec;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.FrameWriterSpec;

/// <summary>Extra test-only P/Invokes (independent of the product's interop).</summary>
internal static class Native2
{
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern bool IsHungAppWindow(nint hwnd);
}

/// <summary>A Rig plus a Win32WindowSystem built on the loop thread over a FakeMap. Interactive Windows session needed.</summary>
internal sealed class RealRig : IDisposable
{
    internal readonly Rig Rig = new();
    internal readonly FakeMap Map = new();
    internal readonly Win32WindowSystem Sys;
    internal readonly List<string> Logs = new();

    internal RealRig()
    {
        Sys = Rig.Loop.Invoke(() => new Win32WindowSystem(Rig.Loop.Post, Map, s => { lock (Logs) { Logs.Add(s); } }));
    }

    internal WindowKey Add(string key, nint hwnd, int? tidOverride = null)
    {
        (int pid, int tid) = Native.PidTid(hwnd);
        var k = new WindowKey(key);
        Map.Add(k, new ManagedWindowIdentity(HandleConversion.ToHandle(hwnd), pid, tidOverride ?? tid));
        return k;
    }

    internal nint Create(string title = "CanvasWmSelf") =>
        Rig.CreateWindow(0, "Static", title, Native.WS_OVERLAPPEDWINDOW | Native.WS_VISIBLE, 100, 100, 800, 600);

    /// <summary>Issues SetFrame on the loop thread and waits for the completion; returns the result and the thread it ran on.</summary>
    internal (Rect? Result, int Thread, long Ms) Write(WindowKey k, Rect target)
    {
        var tcs = new TaskCompletionSource<(Rect?, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = Stopwatch.StartNew();
        Rig.Loop.Invoke(() => Sys.SetFrame(k, target, r => tcs.SetResult((r, Environment.CurrentManagedThreadId))));
        Assert.True(tcs.Task.Wait(TimeSpan.FromSeconds(20)), "SetFrame completion did not arrive");
        return (tcs.Task.Result.Item1, tcs.Task.Result.Item2, sw.ElapsedMilliseconds);
    }

    internal (Rect? Result, long Ms) Read(WindowKey k)
    {
        var tcs = new TaskCompletionSource<Rect?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sw = Stopwatch.StartNew();
        Rig.Loop.Invoke(() => Sys.ReadFrame(k, r => tcs.SetResult(r)));
        Assert.True(tcs.Task.Wait(TimeSpan.FromSeconds(20)), "ReadFrame completion did not arrive");
        return (tcs.Task.Result, sw.ElapsedMilliseconds);
    }

    internal IReadOnlyList<FrameWriteResult> WriteMany(params FrameWrite[] writes)
    {
        var tcs = new TaskCompletionSource<IReadOnlyList<FrameWriteResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Rig.Loop.Invoke(() => Sys.SetFrames(writes, r => tcs.SetResult(r)));
        Assert.True(tcs.Task.Wait(TimeSpan.FromSeconds(20)), "SetFrames completion did not arrive");
        return tcs.Task.Result;
    }

    internal static (PixelRect Bounds, uint Dpi) Primary() => Native.WithPmv2(() =>
    {
        nint hm = Native.MonitorFromPoint(default, 1);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Assert.True(Native.GetMonitorInfoW(hm, ref mi));
        Assert.Equal(0, Native.GetDpiForMonitor(hm, 0, out uint dpi, out _));
        return (new PixelRect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom), dpi);
    });

    public void Dispose()
    {
        try { Rig.Loop.Invoke(() => Sys.Dispose()); } finally { Rig.Dispose(); }
    }
}

/// <summary>AC26-AC31. Interactive Windows session required; not runnable on the Mac.</summary>
public sealed class RealWindowTests
{
    private static void Within(Rect target, Rect? actual, double tol = 1.0)
    {
        Assert.NotNull(actual);
        Assert.InRange(Math.Abs(actual!.Value.X - target.X), 0, tol);
        Assert.InRange(Math.Abs(actual.Value.Y - target.Y), 0, tol);
        Assert.InRange(Math.Abs(actual.Value.Width - target.Width), 0, tol);
        Assert.InRange(Math.Abs(actual.Value.Height - target.Height), 0, tol);
    }

    [Fact]
    public void AC26_in_process_window_write_read_and_insets()
    {
        using var r = new RealRig();
        nint hwnd = r.Create();
        WindowKey k = r.Add("self", hwnd);
        var target = new Rect(100, 100, 800, 600);
        var (result, thread, _) = r.Write(k, target);
        Assert.Equal(r.Rig.Loop.ManagedThreadId, thread);
        Within(target, result);

        var (read, _) = r.Read(k);
        Rect? described = r.Rig.Describe(Rig.H(hwnd))!.Frame;
        Assert.NotNull(read);
        Assert.NotNull(described);
        Assert.Equal(described!.Value.X, read!.Value.X, 9);
        Assert.Equal(described.Value.Y, read.Value.Y, 9);
        Assert.Equal(described.Value.Width, read.Value.Width, 9);
        Assert.Equal(described.Value.Height, read.Value.Height, 9);

        // The visible (extended) frame, not the outer rect, matches the target in pixels.
        (PixelRect bounds, uint dpi) = RealRig.Primary();
        PixelRect expected = DipSpace.ToPixels(target, bounds, dpi);
        Native.RECT dwm = Native.WithPmv2(() =>
        {
            Assert.Equal(0, Native.DwmGetWindowAttribute(hwnd, 9, out Native.RECT d, 16));
            return d;
        });
        Assert.InRange(Math.Abs(dwm.Left - expected.Left), 0, 1);
        Assert.InRange(Math.Abs(dwm.Top - expected.Top), 0, 1);
        Assert.InRange(Math.Abs(dwm.Right - expected.Right), 0, 1);
        Assert.InRange(Math.Abs(dwm.Bottom - expected.Bottom), 0, 1);
    }

    [Fact]
    public void AC27_maximised_window_is_restored_without_activation()
    {
        using var r = new RealRig();
        nint hwnd = r.Create();
        WindowKey k = r.Add("self", hwnd);
        r.Rig.Loop.Invoke(() => Native.ShowWindow(hwnd, Native.SW_MAXIMIZE));
        Assert.True(Native2.IsZoomed(hwnd));
        nint before = Native2.GetForegroundWindow();
        var target = new Rect(100, 100, 800, 600);
        var (result, _, _) = r.Write(k, target);
        nint after = Native2.GetForegroundWindow();
        Assert.False(Native2.IsZoomed(hwnd));
        Within(target, result);
        Assert.Equal(before, after);
    }

    [Fact]
    public void AC28_probe_and_self_batch_zorder_and_showing()
    {
        using var r = new RealRig();
        using ProbeProcess probe = ProbeProcess.Start();
        nint self = r.Create();
        WindowKey ks = r.Add("self", self);
        WindowKey kp = r.Add("probe", HandleConversion.ToHwnd(probe.Handle));
        var ts = new Rect(50, 50, 700, 500);
        var tp = new Rect(300, 200, 640, 480);
        var res = r.WriteMany(new FrameWrite(ks, ts), new FrameWrite(kp, tp));
        Within(ts, res[0].Actual);
        Within(tp, res[1].Actual);

        var order = Native.CaptureEnumWindows();
        IReadOnlyList<WindowKey> fb = r.Rig.Loop.Invoke(() => r.Sys.FrontToBack());
        Assert.Contains(ks, fb);
        Assert.Contains(kp, fb);
        int expectedSelfFirst = order.IndexOf(self) < order.IndexOf(HandleConversion.ToHwnd(probe.Handle)) ? 1 : -1;
        Assert.Equal(expectedSelfFirst, Math.Sign(fb.ToList().IndexOf(kp) - fb.ToList().IndexOf(ks)));

        Assert.True(r.Rig.Loop.Invoke(() => r.Sys.IsShowing(kp)));
        Native.ShowWindow(HandleConversion.ToHwnd(probe.Handle), Native.SW_HIDE);
        Assert.True(Rig.WaitFor(() => !r.Rig.Loop.Invoke(() => r.Sys.IsShowing(kp)), TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void AC29_hung_probe_does_not_block_the_writer()
    {
        using var r = new RealRig();
        using ProbeProcess probe = ProbeProcess.Start();
        nint self = r.Create();
        WindowKey ks = r.Add("self", self);
        WindowKey kp = r.Add("probe", HandleConversion.ToHwnd(probe.Handle));
        probe.Send("hang");
        Thread.Sleep(6000);

        var (hungResult, _, ms1) = r.Write(kp, new Rect(100, 100, 400, 300));
        Assert.Null(hungResult);
        Assert.True(ms1 < 1000, $"hung write took {ms1} ms");

        var target = new Rect(100, 100, 800, 600);
        var (selfResult, _, ms2) = r.Write(ks, target);
        Within(target, selfResult);
        Assert.True(ms2 < 1000, $"self write took {ms2} ms");

        var (read, _) = r.Read(kp);
        Assert.NotNull(read);
    }

    [Fact]
    public void AC30_dead_handle_and_wrong_thread_are_null()
    {
        using var r = new RealRig();
        nint alive = r.Create("alive");
        WindowKey wrongTid = r.Add("wrongtid", alive, tidOverride: 1);
        Assert.Null(r.Write(wrongTid, new Rect(0, 0, 200, 200)).Result);
        Assert.Null(r.Read(wrongTid).Result);

        nint doomed = r.Create("doomed");
        WindowKey kd = r.Add("doomed", doomed);
        r.Rig.Loop.Invoke(() => Native.DestroyWindow(doomed));
        Assert.Null(r.Write(kd, new Rect(0, 0, 200, 200)).Result);
        Assert.Null(r.Read(kd).Result);
    }

    [Fact]
    public void AC31_pointer_buttons_and_displays()
    {
        using var r = new RealRig();
        (PixelRect bounds, uint dpi) = RealRig.Primary();
        int cx = (bounds.Left + bounds.Right) / 2, cy = (bounds.Top + bounds.Bottom) / 2;
        Native.WithPmv2(() => Native2.SetCursorPos(cx, cy));
        Point expected = DipSpace.ToDip(cx, cy, bounds, dpi);
        Point p = r.Rig.Loop.Invoke(() => r.Sys.Pointer);
        Assert.Equal(expected.X, p.X, 9);
        Assert.Equal(expected.Y, p.Y, 9);
        Assert.False(r.Rig.Loop.Invoke(() => r.Sys.MouseDown));

        Rect frame = DipSpace.ToDip(bounds, bounds, dpi);
        Assert.Equal(1.0, r.Rig.Loop.Invoke(() => r.Sys.VisibleFraction(frame)), 12);

        var frames = r.Rig.Loop.Invoke(() =>
        {
            using var scope = new DpiScope();
            return Monitors.Enumerate().Select(m => m.DipFrame).ToList();
        });
        Point origin = r.Rig.Loop.Invoke(() => r.Sys.ParkingOrigin);
        Point want = ParkGeometry.DefaultFallbackOrigin(frames);
        Assert.Equal(want.X, origin.X, 9);
        Assert.Equal(want.Y, origin.Y, 9);
    }
}

internal static class Native3
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowTextW(nint hwnd, char[] text, int max);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern int GetMessageW(out Native.MSG m, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool PeekMessageW(out Native.MSG m, nint hwnd, uint min, uint max, uint remove);
}
