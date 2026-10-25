using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.FrameWriterSpec;

/// <summary>AC22-AC25: Win32WindowSystem over FakeOps with a manual post and stepped writer.</summary>
public sealed class SystemContractTests
{
    private static Rect R(double x, double y, double w, double h) => new(x, y, w, h);

    private static void SameRect(Rect e, Rect? a)
    {
        Assert.NotNull(a);
        Assert.Equal(e.X, a!.Value.X, 9);
        Assert.Equal(e.Y, a.Value.Y, 9);
        Assert.Equal(e.Width, a.Value.Width, 9);
        Assert.Equal(e.Height, a.Value.Height, 9);
    }

    [Fact]
    public void AC22_nothing_runs_until_writer_stepped_and_order_is_kept()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7);
        // the write changes the fake's state; the later read must see it
        r.Ops.OnSetWindowPos = (o, h, outer, posOnly, n) => o.W[h].Current = R(10, 10, 500, 400);
        var log = new List<string>();
        r.Sys.SetFrame(k, R(10, 10, 500, 400), a => log.Add("write:" + (a is null ? "null" : $"{a.Value.Width}")));
        r.Sys.ReadFrame(k, a => log.Add("read:" + (a is null ? "null" : $"{a.Value.Width}")));
        r.Sys.SetFrames(Array.Empty<FrameWrite>(), l => log.Add("batch:" + l.Count));
        Assert.Empty(log);
        Assert.Equal(0, r.Post.Count);
        Assert.Equal(3, r.Writer.Items.Count);

        r.Writer.StepAll();
        Assert.Empty(log);
        r.Post.RunAll();
        Assert.Equal(new[] { "write:500", "read:500", "batch:0" }, log);
    }

    [Fact]
    public void AC22_unknown_key_completes_null_never_inline()
    {
        using var r = new SysRig();
        var log = new List<string>();
        r.Sys.SetFrame(new WindowKey("nope"), R(0, 0, 10, 10), a => log.Add(a is null ? "null" : "val"));
        Assert.Empty(log);
        r.Writer.StepAll();
        r.Post.RunAll();
        Assert.Equal(new[] { "null" }, log);
    }

    [Fact]
    public void AC22_invalid_arguments_queue_and_post_nothing()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7);
        Action<IReadOnlyList<FrameWriteResult>> cb = _ => { };
        Assert.Throws<ArgumentException>(() => r.Sys.SetFrames(new[] { new FrameWrite(k, R(0, 0, 1, 1)), new FrameWrite(k, R(0, 0, 2, 2)) }, cb));
        Assert.Throws<ArgumentException>(() => r.Sys.SetFrames(new[] { default(FrameWrite) }, cb));
        Assert.Empty(r.Writer.Items);
        Assert.Equal(0, r.Post.Count);

        Assert.Throws<ArgumentNullException>(() => new Win32WindowSystem(null!, r.Map, null, r.Ops, r.Writer.Enqueue));
        Assert.Throws<ArgumentNullException>(() => new Win32WindowSystem(r.Post.Post, null!, null, r.Ops, r.Writer.Enqueue));
        Assert.Throws<ArgumentNullException>(() => r.Sys.ReadFrame(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => r.Sys.SetFrame(null!, R(0, 0, 1, 1), _ => { }));
        Assert.Throws<ArgumentNullException>(() => r.Sys.Focus(null!));
        Assert.Throws<ArgumentNullException>(() => r.Sys.IsShowing(null!));
        Assert.Throws<ArgumentNullException>(() => r.Sys.TabSibling(null!));
        Assert.Throws<ArgumentNullException>(() => r.Sys.ReadFrame(k, null!));
        Assert.Throws<ArgumentNullException>(() => r.Sys.SetFrame(k, R(0, 0, 1, 1), null!));
        Assert.Throws<ArgumentNullException>(() => r.Sys.SetFrames(null!, cb));
        Assert.Throws<ArgumentNullException>(() => r.Sys.SetFrames(Array.Empty<FrameWrite>(), null!));
        Assert.Empty(r.Writer.Items);
        Assert.Equal(0, r.Post.Count);
    }

    [Fact]
    public void AC23_dispose_drops_queued_items_with_null_results()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7);
        WindowKey k2 = r.Win("k2", 8);
        var log = new List<string>();
        r.Sys.SetFrame(k, R(0, 0, 100, 100), a => log.Add(a is null ? "null" : "val"));
        r.Sys.SetFrames(new[] { new FrameWrite(k2, R(0, 0, 100, 100)) },
            l => log.Add("batch:" + string.Join(",", l.Select(x => x.Actual is null ? "null" : "val"))));
        r.Sys.Dispose();
        r.Writer.StepAll();
        r.Post.RunAll();
        Assert.Equal(new[] { "null", "batch:null" }, log);
        Assert.Empty(r.Ops.Named("SetWindowPos"));
    }

    [Fact]
    public void AC23_after_dispose_no_ops_calls_and_double_dispose_ok()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7);
        r.Sys.Dispose();
        r.Ops.Reset();
        var log = new List<string>();
        r.Sys.SetFrame(k, R(0, 0, 10, 10), a => log.Add(a is null ? "null" : "val"));
        r.Sys.Focus(k);
        Assert.Empty(r.Ops.Calls);
        Assert.Empty(r.Writer.Items);
        r.Post.RunAll();
        Assert.Equal(new[] { "null" }, log);
        r.Sys.Dispose();
    }

    [Fact]
    public void AC23_throwing_completion_is_logged_and_later_ones_still_run()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7);
        var log = new List<string>();
        r.Sys.ReadFrame(k, _ => throw new InvalidOperationException("boom"));
        r.Sys.ReadFrame(k, _ => log.Add("second"));
        r.Writer.StepAll();
        r.Post.RunAll();
        Assert.Equal(new[] { "second" }, log);
        Assert.Single(r.Logs);
        Assert.Contains("InvalidOperationException", r.Logs[0]);
    }

    [Fact]
    public void AC24_FrontToBack_filters_to_managed_keys()
    {
        using var r = new SysRig();
        WindowKey k2 = r.Win("k2", 2);
        WindowKey k5 = r.Win("k5", 5);
        r.Ops.Top = new List<nint> { 9, 2, 5 };
        Assert.Equal(new[] { k2, k5 }, r.Sys.FrontToBack().ToArray());
    }

    [Fact]
    public void AC24_IsShowing_requires_all_four()
    {
        using var r = new SysRig();
        WindowKey ok = r.Win("ok", 1);
        WindowKey hidden = r.Win("hidden", 2, w => w.Visible = false);
        WindowKey min = r.Win("min", 3, w => w.Minimized = true);
        WindowKey cloaked = r.Win("cloaked", 4, w => w.Cloaked = true);
        WindowKey dead = r.Win("dead", 5, w => w.Live = false);
        r.Map.Add(new WindowKey("wrongtid"), new ManagedWindowIdentity(6, 100, 999));
        r.Ops.Add(6);
        Assert.True(r.Sys.IsShowing(ok));
        Assert.False(r.Sys.IsShowing(hidden));
        Assert.False(r.Sys.IsShowing(min));
        Assert.False(r.Sys.IsShowing(cloaked));
        Assert.False(r.Sys.IsShowing(dead));
        Assert.False(r.Sys.IsShowing(new WindowKey("unknown")));
        Assert.False(r.Sys.IsShowing(new WindowKey("wrongtid")));
    }

    [Fact]
    public void AC24_displays_pointer_and_buttons()
    {
        using var r = new SysRig();
        Assert.Equal(1.0, r.Sys.VisibleFraction(R(1340, 0, 200, 100)), 12);
        Point o = r.Sys.ParkingOrigin;
        Assert.Equal(3199, o.X, 9);
        Assert.Equal(959, o.Y, 9);

        r.Ops.Cursor = (2235, 315);
        r.Ops.PointMonitor = 2;
        Point p = r.Sys.Pointer;
        Assert.Equal(2130, p.X, 9);
        Assert.Equal(210, p.Y, 9);
        r.Ops.Cursor = null;
        Point again = r.Sys.Pointer;
        Assert.Equal(2130, again.X, 9);
        Assert.Equal(210, again.Y, 9);

        r.Ops.Swapped = true;
        r.Ops.KeysDown.Add(0x02);
        Assert.True(r.Sys.MouseDown);
        r.Ops.Swapped = false;
        Assert.False(r.Sys.MouseDown);

        Assert.Null(r.Sys.TabSibling(new WindowKey("x")));
    }

    [Fact]
    public void AC24_no_monitors()
    {
        using var r = new SysRig();
        r.Ops.MonitorList = new List<MonitorInfo>();
        Point o = r.Sys.ParkingOrigin;
        Assert.Equal(10000, o.X, 9);
        Assert.Equal(10000, o.Y, 9);
        Assert.Equal(0.0, r.Sys.VisibleFraction(R(0, 0, 10, 10)), 12);
        r.Ops.Cursor = (50, 60);
        Point p = r.Sys.Pointer;
        Assert.Equal(50, p.X, 9);
        Assert.Equal(60, p.Y, 9);
    }

    [Fact]
    public void AC25_focus_unknown_key_does_nothing()
    {
        using var r = new SysRig();
        var raised = new List<WindowKey>();
        r.Sys.Raising += raised.Add;
        r.Sys.Focus(new WindowKey("nope"));
        r.Post.RunAll();
        r.Writer.StepAll();
        Assert.Empty(raised);
        Assert.Empty(r.Ops.Calls);
    }

    [Fact]
    public void AC25_focus_known_raises_then_sets_foreground()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7);
        var raised = new List<WindowKey>();
        r.Sys.Raising += raised.Add;
        r.Sys.Focus(k);
        r.Writer.StepAll();
        r.Post.RunAll();
        Assert.Equal(new[] { k }, raised.ToArray());
        Assert.Single(r.Ops.Named("SetForeground"));
        Assert.Equal(true, r.Sys.LastFocusSucceeded);
    }

    [Fact]
    public void AC25_minimised_restores_before_foreground()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7, w => w.Minimized = true);
        r.Sys.Focus(k);
        r.Writer.StepAll();
        string[] n = r.Ops.Names().ToArray();
        Assert.True(Array.IndexOf(n, "Restore") >= 0);
        Assert.True(Array.IndexOf(n, "Restore") < Array.IndexOf(n, "SetForeground"));
    }

    [Fact]
    public void AC25_hung_not_focused_and_logged()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7, w => w.Hung = true);
        r.Sys.Focus(k);
        r.Writer.StepAll();
        r.Post.RunAll();
        Assert.Empty(r.Ops.Named("SetForeground"));
        Assert.Single(r.Logs);
        Assert.Null(r.Sys.LastFocusSucceeded);
    }

    [Fact]
    public void AC25_refused_foreground_is_logged()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7);
        r.Ops.ForegroundResult = false;
        r.Sys.Focus(k);
        r.Writer.StepAll();
        r.Post.RunAll();
        Assert.Equal(false, r.Sys.LastFocusSucceeded);
        Assert.Single(r.Logs);
        Assert.Contains("focus refused", r.Logs[0]);
    }

    [Fact]
    public void AC25_focus_runs_after_earlier_write()
    {
        using var r = new SysRig();
        WindowKey k = r.Win("k", 7);
        r.Ops.W[7].Current = R(0, 0, 100, 100);
        r.Sys.SetFrame(k, R(0, 0, 100, 100), _ => { });
        r.Sys.Focus(k);
        r.Writer.StepAll();
        string[] n = r.Ops.Names().ToArray();
        Assert.True(Array.IndexOf(n, "SetWindowPos") < Array.IndexOf(n, "SetForeground"));
    }
}
