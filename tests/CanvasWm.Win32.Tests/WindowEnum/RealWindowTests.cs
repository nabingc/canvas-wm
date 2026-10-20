using CanvasWm.Core.Geometry;
using System.Diagnostics;
using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.WindowEnumSpec;

/// <summary>AC16-AC18 and AC27: Enumerate/Describe against real in-process windows.
/// Requires an interactive Windows session. Not runnable on the Mac.</summary>
public sealed class RealWindowTests : IDisposable
{
    private const uint Overlapped = Native.WS_OVERLAPPEDWINDOW | Native.WS_VISIBLE;
    private readonly Rig _rig = new();

    public void Dispose() => _rig.Dispose();

    private (Native.RECT Monitor, uint Dpi) PrimaryMonitor() => Native.WithPmv2(() =>
    {
        nint hm = Native.MonitorFromPoint(default, 1);
        var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
        Assert.True(Native.GetMonitorInfoW(hm, ref mi));
        Assert.Equal(0, Native.GetDpiForMonitor(hm, 0, out uint dpi, out _));
        return (mi.rcMonitor, dpi);
    });

    private static PixelRect Px(Native.RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    [Fact]
    public void AC16_Enumerate_MatchesIndependentEnumWindows_AndShellFacts()
    {
        for (int attempt = 1; ; attempt++)
        {
            List<nint> before = Native.CaptureEnumWindows();
            IReadOnlyList<OsWindowFacts> facts = _rig.Enumerate();
            Assert.True(facts.Count >= 1);
            Assert.All(facts, f => Assert.NotEqual(0UL, f.Handle));
            Assert.Equal(facts.Count, facts.Select(f => f.Handle).Distinct().Count());

            ulong[] expected = before.Where(Native.IsWindow).Select(Rig.H).ToArray();
            ulong[] actual = facts.Select(f => f.Handle).Where(h => Native.IsWindow(HandleConversion.ToHwnd(h))).ToArray();
            if (expected.SequenceEqual(actual))
            {
                break;
            }

            if (attempt == 3)
            {
                Assert.Equal(expected, actual);
            }
        }

        IReadOnlyList<OsWindowFacts> all = _rig.Enumerate();
        nint shell = Native.GetShellWindow();
        Assert.NotEqual((nint)0, shell);
        OsWindowFacts shellFacts = Assert.Single(all, f => f.Handle == Rig.H(shell));
        Assert.True(shellFacts.IsShellWindow);
        Assert.True(shellFacts.OwnsDesktop);

        nint tray = Native.FindWindowW("Shell_TrayWnd", null);
        Assert.NotEqual((nint)0, tray);
        Assert.True(Assert.Single(all, f => f.Handle == Rig.H(tray)).IsShellWindow);
    }

    [Fact]
    public void AC17_InProcessWindow_Facts()
    {
        nint hwnd = _rig.CreateWindow(0, "Static", "CanvasWmSelf", Overlapped, 100, 100, 800, 600);
        ulong h = Rig.H(hwnd);

        OsWindowFacts f = Assert.IsType<OsWindowFacts>(_rig.Describe(h));
        Assert.True(f.IsOwnProcess);
        Assert.True(f.IsTopLevel);
        Assert.True(f.IsVisible);
        Assert.False(f.IsOwned);
        Assert.False(f.IsChild);
        Assert.True(f.HasCaption);
        Assert.True(f.IsResizable);
        Assert.True(f.HasMaximizeButton);
        Assert.True(f.MaximizeEnabled);
        Assert.Equal("CanvasWmSelf", f.Title);
        Assert.Equal((int)_rig.Loop.NativeThreadId, f.ThreadId);
        Assert.Equal(Environment.ProcessId, f.ProcessId);
        Assert.True(f.IsAccessible);
        Assert.Equal(CloakKind.None, f.Cloak);
        Assert.False(f.IsFullScreen);

        Rect expected = Native.WithPmv2(() =>
        {
            Assert.Equal(0, Native.DwmGetWindowAttribute(hwnd, 9, out Native.RECT dwm, 16));
            nint hm = Native.MonitorFromWindow(hwnd, 2);
            var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.MONITORINFO>() };
            Assert.True(Native.GetMonitorInfoW(hm, ref mi));
            Assert.Equal(0, Native.GetDpiForMonitor(hm, 0, out uint dpi, out _));
            return DipSpace.ToDip(Px(dwm), Px(mi.rcMonitor), dpi);
        });
        Assert.NotNull(f.Frame);
        Assert.Equal(expected.X, f.Frame!.Value.X, 9);
        Assert.Equal(expected.Y, f.Frame.Value.Y, 9);
        Assert.Equal(expected.Width, f.Frame.Value.Width, 9);
        Assert.Equal(expected.Height, f.Frame.Value.Height, 9);

        Assert.Contains(_rig.Enumerate(), x => x.Handle == h);

        _rig.Loop.Invoke(() => Native.ShowWindow(hwnd, Native.SW_MINIMIZE));
        Assert.True(_rig.Describe(h)!.IsMinimized);
        _rig.Loop.Invoke(() => Native.ShowWindow(hwnd, Native.SW_MAXIMIZE));
        Assert.False(_rig.Describe(h)!.IsFullScreen);
        _rig.Loop.Invoke(() => Native.ShowWindow(hwnd, Native.SW_HIDE));
        Assert.False(_rig.Describe(h)!.IsVisible);
        Assert.Contains(_rig.Enumerate(), x => x.Handle == h);
    }

    [Fact]
    public void AC18_Describe_EdgeCases()
    {
        Assert.Null(_rig.Describe(0));

        nint doomed = _rig.CreateWindow(0, "Static", "doomed", Overlapped, 50, 50, 200, 200);
        _rig.Loop.Invoke(() => Native.DestroyWindow(doomed));
        Assert.Null(_rig.Describe(Rig.H(doomed)));

        nint parent = _rig.CreateWindow(0, "Static", "CanvasWmSelf", Overlapped, 100, 100, 800, 600);
        nint child = _rig.CreateWindow(0, "Static", "kid", Native.WS_CHILD | Native.WS_VISIBLE, 5, 5, 50, 50, parent);
        OsWindowFacts c = _rig.Describe(Rig.H(child))!;
        Assert.False(c.IsTopLevel);
        Assert.True(c.IsChild);
        Assert.DoesNotContain(_rig.Enumerate(), x => x.Handle == Rig.H(child));

        nint owned = _rig.CreateWindow(0, "Static", "owned", Native.WS_OVERLAPPEDWINDOW | Native.WS_VISIBLE, 150, 150, 300, 300, parent);
        Assert.True(_rig.Describe(Rig.H(owned))!.IsOwned);

        nint tool = _rig.CreateWindow(Native.WS_EX_TOOLWINDOW, "Static", "tool", Overlapped, 200, 200, 300, 300);
        Assert.True(_rig.Describe(Rig.H(tool))!.IsToolWindow);

        Native.RECT m = PrimaryMonitor().Monitor;
        nint popup = _rig.CreateWindow(0, "Static", "popup", Native.WS_POPUP | Native.WS_VISIBLE,
            m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top);
        OsWindowFacts p = _rig.Describe(Rig.H(popup))!;
        Assert.False(p.HasCaption);
        Assert.True(p.IsFullScreen);
    }

    [Fact]
    public void AC27_DpiScope_FrameIdenticalUnderUnaware_AndContextRestored()
    {
        nint hwnd = _rig.CreateWindow(0, "Static", "CanvasWmSelf", Overlapped, 100, 100, 800, 600);
        ulong h = Rig.H(hwnd);
        Rect? baseline = _rig.Describe(h)!.Frame;
        Assert.NotNull(baseline);

        (Rect? frame, bool restored) = _rig.Loop.Invoke(() =>
        {
            nint old = Native.SetThreadDpiAwarenessContext(Native.Unaware);
            try
            {
                Rect? fr = _rig.Source.Describe(h)!.Frame;
                bool same = Native.AreDpiAwarenessContextsEqual(Native.GetThreadDpiAwarenessContext(), Native.Unaware);
                return (fr, same);
            }
            finally
            {
                Native.SetThreadDpiAwarenessContext(old);
            }
        });

        Assert.True(restored);
        Assert.NotNull(frame);
        Assert.Equal(baseline!.Value.X, frame!.Value.X, 9);
        Assert.Equal(baseline.Value.Y, frame.Value.Y, 9);
        Assert.Equal(baseline.Value.Width, frame.Value.Width, 9);
        Assert.Equal(baseline.Value.Height, frame.Value.Height, 9);
    }
}
