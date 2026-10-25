using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.FrameWriterSpec;

/// <summary>AC14-AC21: FrameWriteRunner over FakeOps. Requires Windows to run (project TFM).</summary>
public sealed class AlgorithmTests
{
    private static Rect R(double x, double y, double w, double h) => new(x, y, w, h);

    private static WriteRequest Req(string key, nint h, Rect target, int pid = 100, int tid = 200) =>
        new(new WindowKey(key), new ManagedWindowIdentity(HandleConversion.ToHandle(h), pid, tid), target);

    private static (FakeOps Ops, FrameWriteRunner Runner) Rig(params nint[] handles)
    {
        var ops = new FakeOps();
        foreach (nint h in handles) { ops.Add(h); }
        return (ops, new FrameWriteRunner(ops));
    }

    private static void SameRect(Rect e, Rect? a)
    {
        Assert.NotNull(a);
        Assert.Equal(e.X, a!.Value.X, 9);
        Assert.Equal(e.Y, a.Value.Y, 9);
        Assert.Equal(e.Width, a.Value.Width, 9);
        Assert.Equal(e.Height, a.Value.Height, 9);
    }

    private static IEnumerable<string> Writes(FakeOps ops) =>
        ops.Calls.Where(c => FakeOps.Name(c) is "SetWindowPos" or "TryDeferBatch" or "ReadDipFrame");

    [Fact]
    public void AC14_single_write_W1()
    {
        var (ops, runner) = Rig(7);
        ops.W[7].Reads.Enqueue(R(0, 0, 960, 1040));
        var res = runner.Run(new[] { Req("k", 7, R(0, 0, 960, 1040)) });
        SameRect(R(0, 0, 960, 1040), res[0]);
        Assert.Equal(new[] { "SetWindowPos(7,-7,0,967,1047,False)" }, ops.Named("SetWindowPos").ToArray());
        Assert.Empty(ops.Named("TryDeferBatch"));

        string[] names = ops.Names().ToArray();
        int iLive = Array.IndexOf(names, "IsLive"), iHung = Array.IndexOf(names, "IsHung");
        int iMin = Array.IndexOf(names, "IsMinimized"), iMax = Array.IndexOf(names, "IsMaximized");
        int iWrite = Array.IndexOf(names, "SetWindowPos"), iRead = Array.IndexOf(names, "ReadDipFrame");
        Assert.True(iLive < iHung && iHung < iMin && iMin < iMax);
        foreach (string meas in new[] { "MonitorIndexOf", "WindowRect", "VisibleRect" })
        {
            int i = Array.IndexOf(names, meas);
            Assert.True(i > iMax && i < iWrite, meas);
        }

        Assert.True(iWrite < iRead);
        Assert.Single(ops.Named("ReadDipFrame"));
    }

    [Fact]
    public void AC15_settle_two_writes_no_third()
    {
        var (ops, runner) = Rig(7);
        ops.W[7].Reads.Enqueue(R(0, 0, 500, 600));
        ops.W[7].Reads.Enqueue(R(0, 0, 500, 600));
        var res = runner.Run(new[] { Req("k", 7, R(0, 0, 400, 600)) });
        SameRect(R(0, 0, 500, 600), res[0]);
        Assert.Equal(2, ops.Named("SetWindowPos").Count());
        Assert.Equal(2, ops.Named("ReadDipFrame").Count());
    }

    [Fact]
    public void AC15_settle_reread_fails_keeps_first_read()
    {
        var (ops, runner) = Rig(7);
        ops.W[7].Reads.Enqueue(R(0, 0, 500, 600));
        ops.W[7].Reads.Enqueue(null);
        SameRect(R(0, 0, 500, 600), runner.Run(new[] { Req("k", 7, R(0, 0, 400, 600)) })[0]);
    }

    [Fact]
    public void AC15_sub_tolerance_difference_one_write()
    {
        var (ops, runner) = Rig(7);
        ops.W[7].Reads.Enqueue(R(0.6, 0, 400, 600));
        SameRect(R(0.6, 0, 400, 600), runner.Run(new[] { Req("k", 7, R(0, 0, 400, 600)) })[0]);
        Assert.Single(ops.Named("SetWindowPos"));
    }

    [Fact]
    public void AC15_second_write_fails_result_is_first_read_no_more_reads()
    {
        var (ops, runner) = Rig(7);
        ops.W[7].Reads.Enqueue(R(0, 0, 500, 600));
        ops.W[7].Reads.Enqueue(R(0, 0, 400, 600));
        ops.W[7].SetPosResults.Enqueue(true);
        ops.W[7].SetPosResults.Enqueue(false);
        SameRect(R(0, 0, 500, 600), runner.Run(new[] { Req("k", 7, R(0, 0, 400, 600)) })[0]);
        Assert.Equal(2, ops.Named("SetWindowPos").Count());
        Assert.Single(ops.Named("ReadDipFrame"));
    }

    private static (FakeOps Ops, FrameWriteRunner Runner) RigAOnly()
    {
        var (ops, runner) = Rig(7);
        ops.MonitorList = new List<MonitorInfo> { FakeOps.MonA };
        return (ops, runner);
    }

    [Fact]
    public void AC16_offscreen_repush_sequence()
    {
        var (ops, runner) = RigAOnly();
        ops.W[7].Reads.Enqueue(R(1000, 860, 705, 423));
        ops.W[7].Reads.Enqueue(R(1000, 860, 705, 423));
        ops.W[7].Reads.Enqueue(R(1919, 1079, 705, 423));
        var res = runner.Run(new[] { Req("k", 7, R(1919, 1079, 705, 423)) });
        SameRect(R(1919, 1079, 705, 423), res[0]);
        string[] seq = Writes(ops).Select(c => c.StartsWith("SetWindowPos") ? (c.EndsWith("True)") ? "PosOnly" : "Write") : "Read").ToArray();
        Assert.Equal(new[] { "Write", "Read", "Write", "Read", "PosOnly", "Read" }, seq);
    }

    [Fact]
    public void AC16_onscreen_target_no_position_only_write()
    {
        var (ops, runner) = RigAOnly();
        ops.W[7].Reads.Enqueue(R(1000, 860, 705, 423));
        ops.W[7].Reads.Enqueue(R(1000, 860, 705, 423));
        runner.Run(new[] { Req("k", 7, R(100, 100, 705, 423)) });
        Assert.DoesNotContain(ops.Named("SetWindowPos"), c => c.EndsWith("True)"));
    }

    [Fact]
    public void AC16_low_visible_fraction_no_repush()
    {
        var (ops, runner) = RigAOnly();
        ops.W[7].Reads.Enqueue(R(1900, 1070, 705, 423));
        ops.W[7].Reads.Enqueue(R(1900, 1070, 705, 423));
        runner.Run(new[] { Req("k", 7, R(1919, 1079, 705, 423)) });
        Assert.Equal(2, ops.Named("SetWindowPos").Count());
        Assert.DoesNotContain(ops.Named("SetWindowPos"), c => c.EndsWith("True)"));
    }

    [Fact]
    public void AC16_failed_reread_keeps_previous_reading()
    {
        var (ops, runner) = RigAOnly();
        ops.W[7].Reads.Enqueue(R(1000, 860, 705, 423));
        ops.W[7].Reads.Enqueue(R(1000, 860, 705, 423));
        ops.W[7].Reads.Enqueue(null);
        SameRect(R(1000, 860, 705, 423), runner.Run(new[] { Req("k", 7, R(1919, 1079, 705, 423)) })[0]);
    }

    [Fact]
    public void AC17_batch_of_three_uses_one_defer()
    {
        var (ops, runner) = Rig(1, 2, 3);
        Rect[] t = { R(0, 0, 400, 300), R(500, 0, 400, 300), R(0, 400, 400, 300) };
        for (int i = 0; i < 3; i++) { ops.W[i + 1].Reads.Enqueue(t[i]); }
        var res = runner.Run(new[] { Req("a", 1, t[0]), Req("b", 2, t[1]), Req("c", 3, t[2]) });
        Assert.Single(ops.Named("TryDeferBatch"));
        Assert.Equal(new nint[] { 1, 2, 3 }, ops.LastDefer!.Select(e => e.Hwnd).ToArray());
        Assert.Empty(ops.Named("SetWindowPos"));
        for (int i = 0; i < 3; i++) { SameRect(t[i], res[i]); }
    }

    [Fact]
    public void AC17_W4_defer_fails_falls_back_individually()
    {
        var (ops, runner) = Rig(1, 2, 3);
        ops.DeferResult = false;
        Rect[] t = { R(0, 0, 400, 300), R(500, 0, 400, 300), R(0, 400, 400, 300) };
        ops.W[1].Reads.Enqueue(t[0]);
        ops.W[3].Reads.Enqueue(t[2]);
        ops.W[2].SetPosResults.Enqueue(false);
        var res = runner.Run(new[] { Req("a", 1, t[0]), Req("b", 2, t[1]), Req("c", 3, t[2]) });
        Assert.Equal(new[] { "SetWindowPos(1", "SetWindowPos(2", "SetWindowPos(3" },
            ops.Named("SetWindowPos").Select(c => c[..c.IndexOf(',')]).ToArray());
        SameRect(t[0], res[0]);
        Assert.Null(res[1]);
        SameRect(t[2], res[2]);
        Assert.DoesNotContain(ops.Calls, c => c == "ReadDipFrame(2)");
    }

    [Fact]
    public void AC18_prefilter_W5()
    {
        var (ops, runner) = Rig(1, 2, 3, 4, 5);
        ops.W[1].Hung = true;
        ops.W[2].Live = false;
        ops.W[3].Minimized = true;
        Rect t5 = R(0, 0, 400, 300);
        ops.W[5].Reads.Enqueue(t5);
        var res = runner.Run(new[]
        {
            Req("a", 1, R(0, 0, 400, 300)), Req("b", 2, R(0, 0, 400, 300)), Req("c", 3, R(0, 0, 400, 300)),
            Req("d", 4, R(double.NaN, 0, 400, 300)), Req("e", 5, t5),
        });
        Assert.Null(res[0]); Assert.Null(res[1]); Assert.Null(res[2]); Assert.Null(res[3]);
        SameRect(t5, res[4]);
        Assert.Single(ops.Named("SetWindowPos"));
        Assert.StartsWith("SetWindowPos(5,", ops.Named("SetWindowPos").Single());
        Assert.Empty(ops.Named("TryDeferBatch"));
        string[] banned = { "IsMaximized", "MonitorIndexOf", "WindowRect", "VisibleRect", "SetWindowPos", "RestoreNoActivate", "ReadDipFrame" };
        foreach (int h in new[] { 1, 2, 3, 4 })
        {
            Assert.DoesNotContain(ops.Calls, c => banned.Contains(FakeOps.Name(c)) && c.Contains($"({h}"));
        }

        Assert.DoesNotContain(ops.Calls, c => c.Contains("(4)"));
    }

    [Fact]
    public void AC18_null_identity_and_empty_monitors()
    {
        var (ops, runner) = Rig(1);
        var res = runner.Run(new[] { new WriteRequest(new WindowKey("x"), null, R(0, 0, 10, 10)) });
        Assert.Null(res[0]);
        Assert.All(ops.Calls, c => Assert.Equal("Monitors", c));

        ops.Reset();
        ops.MonitorList = new List<MonitorInfo>();
        var res2 = runner.Run(new[] { Req("a", 1, R(0, 0, 10, 10)), Req("b", 1, R(0, 0, 10, 10)) });
        Assert.All(res2, Assert.Null);
        Assert.Empty(ops.Named("SetWindowPos"));
        Assert.Empty(ops.Named("TryDeferBatch"));
    }

    [Fact]
    public void AC19_maximised_restored_before_measuring()
    {
        var (ops, runner) = Rig(1);
        ops.W[1].Maximized = true;
        ops.W[1].Reads.Enqueue(R(100, 100, 800, 600));
        runner.Run(new[] { Req("a", 1, R(100, 100, 800, 600)) });
        string[] n = ops.Names().ToArray();
        int restore = Array.IndexOf(n, "RestoreNoActivate");
        Assert.True(restore >= 0);
        Assert.True(restore < Array.IndexOf(n, "WindowRect"));
        Assert.True(restore < Array.IndexOf(n, "VisibleRect"));
        Assert.DoesNotContain("Restore", n);
    }

    [Fact]
    public void AC19_batch_restore_before_defer()
    {
        var (ops, runner) = Rig(1, 2);
        ops.W[2].Maximized = true;
        ops.W[1].Reads.Enqueue(R(0, 0, 400, 300));
        ops.W[2].Reads.Enqueue(R(500, 0, 400, 300));
        runner.Run(new[] { Req("a", 1, R(0, 0, 400, 300)), Req("b", 2, R(500, 0, 400, 300)) });
        string[] n = ops.Names().ToArray();
        Assert.True(Array.IndexOf(n, "RestoreNoActivate") < Array.IndexOf(n, "TryDeferBatch"));
        Assert.DoesNotContain("Restore", n);
    }

    [Fact]
    public void AC20_W3_crossing_dpi()
    {
        var (ops, runner) = Rig(7);
        ops.W[7].Reads.Enqueue(R(1930, 10, 400, 266.667));
        ops.W[7].Reads.Enqueue(R(1930, 10, 600, 400));
        ops.OnSetWindowPos = (o, h, outer, posOnly, n) =>
        {
            if (n == 1)
            {
                o.W[h].MonitorIdx = 1;
                o.W[h].WindowRect = new PixelRect(1924, 15, 2846, 626);
                o.W[h].VisibleRect = new PixelRect(1935, 15, 2835, 615);
            }
        };
        var res = runner.Run(new[] { Req("k", 7, R(1930, 10, 600, 400)) });
        string[] writes = ops.Named("SetWindowPos").ToArray();
        Assert.Equal(new[] { "SetWindowPos(7,1924,15,2846,626,False)", "SetWindowPos(7,1924,15,2846,626,False)" }, writes);
        SameRect(R(1930, 10, 600, 400), res[0]);
    }

    [Fact]
    public void AC21_exceptions_are_contained()
    {
        var (ops, runner) = Rig(1, 2, 3);
        Rect[] t = { R(0, 0, 400, 300), R(500, 0, 400, 300), R(0, 400, 400, 300) };
        for (int i = 0; i < 3; i++) { ops.W[i + 1].Reads.Enqueue(t[i]); }
        ops.W[1].ThrowOnRead = true;
        var res = runner.Run(new[] { Req("a", 1, t[0]), Req("b", 2, t[1]), Req("c", 3, t[2]) });
        Assert.Null(res[0]);
        SameRect(t[1], res[1]);
        SameRect(t[2], res[2]);

        var (ops2, runner2) = Rig(1, 2, 3);
        ops2.DeferThrows = true;
        var res2 = runner2.Run(new[] { Req("a", 1, t[0]), Req("b", 2, t[1]), Req("c", 3, t[2]) });
        Assert.All(res2, Assert.Null);
    }
}
