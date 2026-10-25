using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Parking;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Tests.WindowEnumSpec;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.FrameWriterSpec;

/// <summary>AC32-AC39 probe tools used by windows-verification/win32-frame-writer.md.
/// With CANVASWM_MANUAL unset every test returns at once and writes nothing (AC38).
/// Windows are picked by title substring (env var) or by click-to-pick after a 5 s countdown.
/// Output: %TEMP%\canvaswm-writes.tsv.</summary>
public sealed class FrameWriterManual
{
    private static bool Enabled => Environment.GetEnvironmentVariable("CANVASWM_MANUAL") == "1";
    private static string TsvPath => Path.Combine(Path.GetTempPath(), "canvaswm-writes.tsv");

    private const string Header = "Test\tLabel\tKey\tTargetX\tTargetY\tTargetW\tTargetH\tResultX\tResultY\tResultW\tResultH\tMs\tNote";

    private static void Begin() => File.WriteAllText(TsvPath, Header + Environment.NewLine);

    private static string F(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static void Row(string test, string label, string key, Rect t, Rect? r, long ms, string note = "")
    {
        string res = r is { } x ? $"{F(x.X)}\t{F(x.Y)}\t{F(x.Width)}\t{F(x.Height)}" : "null\tnull\tnull\tnull";
        File.AppendAllText(TsvPath, $"{test}\t{label}\t{key}\t{F(t.X)}\t{F(t.Y)}\t{F(t.Width)}\t{F(t.Height)}\t{res}\t{ms}\t{note}{Environment.NewLine}");
    }

    private static nint FindByTitle(string part)
    {
        nint found = 0;
        foreach (nint h in Native.CaptureEnumWindows())
        {
            if (!Native3.IsWindowVisible(h)) { continue; }
            var buf = new char[256];
            int n = Native3.GetWindowTextW(h, buf, 256);
            if (n > 0 && new string(buf, 0, n).Contains(part, StringComparison.OrdinalIgnoreCase)) { found = h; break; }
        }

        return found;
    }

    /// <summary>envVar holds a title substring; otherwise count down 5 s and take the foreground window.</summary>
    private static nint Pick(string envVar, string prompt)
    {
        string? title = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(title))
        {
            nint h = FindByTitle(title);
            Assert.NotEqual((nint)0, h);
            return h;
        }

        for (int i = 5; i > 0; i--)
        {
            Console.WriteLine($"{prompt}: click it now ({i}s)");
            Thread.Sleep(1000);
        }

        nint fg = Native2.GetForegroundWindow();
        Assert.NotEqual((nint)0, fg);
        return fg;
    }

    private static List<MonitorInfo> Mons(RealRig r) => r.Rig.Loop.Invoke(() =>
    {
        using var s = new DpiScope();
        return Monitors.Enumerate().ToList();
    });

    private static (Rect Work, Rect Frame) WorkAreaDip(RealRig r, nint hwnd) => Native.WithPmv2(() =>
    {
        nint hm = Native.MonitorFromWindow(hwnd, 2);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Assert.True(Native.GetMonitorInfoW(hm, ref mi));
        Assert.Equal(0, Native.GetDpiForMonitor(hm, 0, out uint dpi, out _));
        var mon = new PixelRect(mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom);
        var work = new PixelRect(mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom);
        return (DipSpace.ToDip(work, mon, dpi), DipSpace.ToDip(mon, mon, dpi));
    });

    private static void Check(Rect t, Rect? r, double tol = 1.0)
    {
        Assert.NotNull(r);
        Assert.InRange(Math.Abs(r!.Value.X - t.X), 0, tol);
        Assert.InRange(Math.Abs(r.Value.Y - t.Y), 0, tol);
        Assert.InRange(Math.Abs(r.Value.Width - t.Width), 0, tol);
        Assert.InRange(Math.Abs(r.Value.Height - t.Height), 0, tol);
    }

    [Fact]
    public void MoveAcross()
    {
        if (!Enabled) { return; }
        Begin();
        using var r = new RealRig();
        nint hwnd = Pick("CANVASWM_TITLE", "Pick the window on monitor 1");
        WindowKey k = r.Add("w", hwnd);
        var mons = Mons(r);
        Assert.True(mons.Count >= 2, "needs two monitors");
        Rect home = r.Read(k).Result!.Value;
        int cur = mons.FindIndex(m => ParkGeometry.IntersectionArea(home, m.DipFrame) > 0);
        Rect other = mons[cur == 0 ? 1 : 0].DipFrame;
        var there = new Rect(other.X + other.Width / 4, other.Y + other.Height / 4, other.Width / 2, other.Height / 2);
        var (res, _, ms) = r.Write(k, there);
        Row("MoveAcross", "there", "w", there, res, ms);
        Check(there, res);
        var (back, _, ms2) = r.Write(k, home);
        Row("MoveAcross", "back", "w", home, back, ms2, "settle pass ran if two SetWindowPos; not observable here");
        Check(home, back);
    }

    [Fact]
    public void TileLeftHalf()
    {
        if (!Enabled) { return; }
        Begin();
        using var r = new RealRig();
        nint hwnd = Pick("CANVASWM_TITLE", "Pick the window to tile");
        WindowKey k = r.Add("w", hwnd);
        (Rect work, _) = WorkAreaDip(r, hwnd);
        var t = new Rect(work.X, work.Y, work.Width / 2, work.Height);
        var (res, _, ms) = r.Write(k, t);
        Row("TileLeftHalf", "tile", "w", t, res, ms, res is null ? "null (refused/elevated/failed)" : "");
    }

    [Fact]
    public void WriteLoop()
    {
        if (!Enabled) { return; }
        Begin();
        using var r = new RealRig();
        nint h1 = Pick("CANVASWM_TITLE", "Pick Notepad 1");
        nint h2 = Pick("CANVASWM_TITLE2", "Pick Notepad 2");
        WindowKey k1 = r.Add("w1", h1), k2 = r.Add("w2", h2);
        ProbeProcess? probe = null;
        WindowKey? kp = null;
        if (Environment.GetEnvironmentVariable("CANVASWM_HANG") == "1")
        {
            probe = ProbeProcess.Start();
            kp = r.Add("probe", HandleConversion.ToHwnd(probe.Handle));
            probe.Send("hang");
        }

        try
        {
            (Rect work, _) = WorkAreaDip(r, h1);
            var left = new Rect(work.X, work.Y, work.Width / 2, work.Height);
            var right = new Rect(work.X + work.Width / 2, work.Y, work.Width / 2, work.Height);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 30; i++)
            {
                long due = i * 1000L;
                while (sw.ElapsedMilliseconds < due) { Thread.Sleep(10); }
                var (a, _, ms) = r.Write(k1, i % 2 == 0 ? left : right);
                Row("WriteLoop", $"t={sw.ElapsedMilliseconds}", "w1", i % 2 == 0 ? left : right, a, ms);
                var (b, _, ms2) = r.Write(k2, i % 2 == 0 ? right : left);
                Row("WriteLoop", $"t={sw.ElapsedMilliseconds}", "w2", i % 2 == 0 ? right : left, b, ms2);
                if (kp is { } pk)
                {
                    var (p, _, ms3) = r.Write(pk, left);
                    Row("WriteLoop", $"t={sw.ElapsedMilliseconds}", "probe", left, p, ms3, "expect null once flagged hung (~5 s)");
                }
            }
        }
        finally
        {
            probe?.Dispose();
        }
    }

    [Fact]
    public void ParkAndWait()
    {
        if (!Enabled) { return; }
        Begin();
        using var r = new RealRig();
        nint hwnd = Pick("CANVASWM_TITLE", "Pick the window to park");
        WindowKey k = r.Add("w", hwnd);
        var mons = Mons(r);
        Rect home = r.Read(k).Result!.Value;
        // Bottom-right park spot of the primary monitor: the default fallback origin (1 DIP overlap) with the window's size.
        Point o = ParkGeometry.DefaultFallbackOrigin(mons.Take(1).Select(m => m.DipFrame).ToList());
        var t = new Rect(o.X, o.Y, home.Width, home.Height);
        var (res, _, ms) = r.Write(k, t);
        Row("ParkAndWait", "park", "w", t, res, ms, "check TSV/ScreenShot: re-push ran if visible sliver > 2 px");
        Check(t, res);
        (PixelRect b, uint d) = RealRig.Primary();
        PixelRect px = DipSpace.ToPixels(t, b, d);
        Assert.InRange(px.Left, -30000, 30000);
        Assert.InRange(px.Top, -30000, 30000);
        Thread.Sleep(60_000);
        var (again, _) = r.Read(k);
        Row("ParkAndWait", "after60s", "w", t, again, 0);
        Assert.Equal(res, again);
        r.Write(k, home); // restore
    }

    [Fact]
    public void FocusHotkey()
    {
        if (!Enabled) { return; }
        Begin();
        using var r = new RealRig();
        nint hwnd = Pick("CANVASWM_TITLE", "Pick Notepad (the window to focus)");
        WindowKey k = r.Add("w", hwnd);
        var done = new ManualResetEventSlim();
        string note = "hotkey not pressed within 20 s";
        bool? ok = null;
        bool foreground = false;
        var t = new Thread(() =>
        {
            Native3.PeekMessageW(out _, 0, 0, 0, 0);
            if (!Native3.RegisterHotKey(0, 1, 0x4006 /* MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT */, 0x7B /* F12 */))
            {
                note = "RegisterHotKey failed (Ctrl+Shift+F12 already taken?)";
                done.Set();
                return;
            }

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(20))
            {
                if (Native3.PeekMessageW(out Native.MSG m, 0, 0, 0, 1) && m.message == 0x0312)
                {
                    r.Sys.Focus(k);
                    Rig.WaitFor(() => r.Sys.LastFocusSucceeded is not null, TimeSpan.FromSeconds(5));
                    ok = r.Sys.LastFocusSucceeded;
                    Thread.Sleep(300);
                    foreground = Native2.GetForegroundWindow() == hwnd;
                    note = "hotkey handled";
                    break;
                }

                Thread.Sleep(10);
            }

            Native3.UnregisterHotKey(0, 1);
            done.Set();
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Console.WriteLine("Click a terminal, then press Ctrl+Shift+F12 within 20 s");
        Assert.True(done.Wait(TimeSpan.FromSeconds(40)));
        Row("FocusHotkey", note, "w", default, null, 0, $"LastFocusSucceeded={ok?.ToString() ?? "null"};IsForeground={foreground}");
    }

    [Fact]
    public void RepeatTile()
    {
        if (!Enabled) { return; }
        Begin();
        using var r = new RealRig();
        nint[] hs = { Pick("CANVASWM_TITLE", "Pick window 1"), Pick("CANVASWM_TITLE2", "Pick window 2"), Pick("CANVASWM_TITLE3", "Pick window 3") };
        var keys = hs.Select((h, i) => r.Add("w" + i, h)).ToArray();
        (Rect w, _) = WorkAreaDip(r, hs[0]);
        Rect[] tiles =
        {
            new(w.X, w.Y, w.Width / 2, w.Height),
            new(w.X + w.Width / 2, w.Y, w.Width / 2, w.Height / 2),
            new(w.X + w.Width / 2, w.Y + w.Height / 2, w.Width / 2, w.Height / 2),
        };
        for (int pass = 0; pass < 3; pass++)
        {
            for (int i = 0; i < 3; i++)
            {
                var (res, _, ms) = r.Write(keys[i], tiles[i]);
                var (read, _) = r.Read(keys[i]);
                Row("RepeatTile", $"pass{pass}", "w" + i, tiles[i], read, ms, "write result then read-back");
                Check(tiles[i], res);
                Check(tiles[i], read);
            }
        }
    }
}
