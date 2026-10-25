using System.Reflection;
using System.Runtime.CompilerServices;
using CanvasWm.Core.Frames;
using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Hotkeys;
using CanvasWm.Win32.Tests.WindowEnumSpec;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.FrameWriterSpec;

/// <summary>AC8-AC13. Requires Windows (the project targets net10.0-windows).</summary>
public sealed class PureHelperTests
{
    private static readonly PixelRect A = new(0, 0, 1920, 1080);
    private static readonly PixelRect B = new(1920, 0, 3840, 1440);
    private static readonly PixelRect L = new(-3840, 0, 0, 2160);

    [Fact]
    public void AC8_ToPixels_worked_examples()
    {
        Assert.Equal(new PixelRect(1935, 15, 2835, 615), DipSpace.ToPixels(new Rect(1930, 10, 600, 400), B, 144));
        Assert.Equal(new PixelRect(0, 0, 960, 1040), DipSpace.ToPixels(new Rect(0, 0, 960, 1040), A, 96));
        Assert.Equal(new PixelRect(-3640, 200, -2640, 1200), DipSpace.ToPixels(new Rect(-3740, 100, 500, 500), L, 192));
    }

    [Fact]
    public void AC8_ToPixels_half_rounds_away_from_zero()
    {
        var m = new PixelRect(0, 0, 1920, 1080);
        Assert.Equal(1, DipSpace.ToPixels(new Rect(0.4, 0, 10, 10), m, 120).Left);
        Assert.Equal(-1, DipSpace.ToPixels(new Rect(-0.4, 0, 10, 10), m, 120).Left);
    }

    [Fact]
    public void AC8_ToPixels_invalid_input_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DipSpace.ToPixels(new Rect(0, 0, 1, 1), A, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DipSpace.ToPixels(new Rect(double.NaN, 0, 1, 1), A, 96));
        Assert.Throws<ArgumentOutOfRangeException>(() => DipSpace.ToPixels(new Rect(0, 0, double.PositiveInfinity, 1), A, 96));
    }

    [Fact]
    public void AC8_ToPixels_saturates()
    {
        Assert.Equal(int.MaxValue, DipSpace.ToPixels(new Rect(1e12, 0, 10, 10), A, 96).Left);
    }

    [Theory]
    [InlineData(0, 0, 960, 1040, 0, 96u)]
    [InlineData(1935, 15, 2835, 615, 1, 144u)]
    [InlineData(-3640, 200, -2640, 1200, 2, 192u)]
    [InlineData(1, 1, 2, 2, 0, 120u)]
    public void AC9_pixel_round_trip_exact(int l, int t, int r, int b, int mon, uint dpi)
    {
        PixelRect m = mon switch { 0 => A, 1 => B, _ => L };
        var p = new PixelRect(l, t, r, b);
        Assert.Equal(p, DipSpace.ToPixels(DipSpace.ToDip(p, m, dpi), m, dpi));
    }

    [Theory]
    [InlineData(0, 96u)]
    [InlineData(0, 120u)]
    [InlineData(1, 144u)]
    public void AC9_fractional_dip_round_trip_bounds(int mon, uint dpi)
    {
        PixelRect m = mon == 0 ? A : B;
        double s = dpi / 96.0;
        foreach (Rect r in new[] { new Rect(10.3, 20.7, 705.5, 422.25), new Rect(1930.1, 0.9, 600.6, 400.4) })
        {
            Rect back = DipSpace.ToDip(DipSpace.ToPixels(r, m, dpi), m, dpi);
            Assert.InRange(Math.Abs(back.X - r.X), 0, 0.5 / s + 1e-9);
            Assert.InRange(Math.Abs(back.Y - r.Y), 0, 0.5 / s + 1e-9);
            Assert.InRange(Math.Abs(back.Width - r.Width), 0, 1 / s + 1e-9);
            Assert.InRange(Math.Abs(back.Height - r.Height), 0, 1 / s + 1e-9);
        }
    }

    [Fact]
    public void AC9_adjacent_rects_share_pixel_edge()
    {
        PixelRect first = DipSpace.ToPixels(new Rect(0, 0, 705.3, 100), A, 120);
        PixelRect second = DipSpace.ToPixels(new Rect(705.3, 0, 700, 100), A, 120);
        Assert.Equal(first.Right, second.Left);
    }

    [Fact]
    public void AC10_point_ToDip()
    {
        Point p = DipSpace.ToDip(2235, 315, B, 144);
        Assert.Equal(2130, p.X, 9);
        Assert.Equal(210, p.Y, 9);
        Point q = DipSpace.ToDip(100, 100, A, 96);
        Assert.Equal(100, q.X, 9);
        Assert.Equal(100, q.Y, 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => DipSpace.ToDip(1, 1, A, 0));
    }

    [Fact]
    public void AC11_Expand_and_ClampOrigin()
    {
        Assert.Equal(new PixelRect(-7, 0, 967, 1047), FrameMath.Expand(new PixelRect(0, 0, 960, 1040), new FrameInsets(7, 0, 7, 7)));
        Assert.Equal(new PixelRect(-30000, -50, -29000, 950), FrameMath.ClampOrigin(new PixelRect(-40000, -50, -39000, 950)));
        Assert.Equal(new PixelRect(30000, 30000, 30100, 30100), FrameMath.ClampOrigin(new PixelRect(31000, 40000, 31100, 40100)));
        var ok = new PixelRect(-100, 5, 800, 600);
        Assert.Equal(ok, FrameMath.ClampOrigin(ok));
        Assert.Equal(0x01, FrameMath.PrimaryButtonKey(false));
        Assert.Equal(0x02, FrameMath.PrimaryButtonKey(true));
    }

    [Fact]
    public void AC12_public_surface()
    {
        Assembly asm = typeof(Win32WindowSystem).Assembly;
        string[] expected =
        {
            nameof(Win32WindowSource), nameof(HandleConversion), nameof(DipSpace), nameof(PixelRect),
            nameof(Win32WindowSystem), nameof(IManagedWindowMap), nameof(ManagedWindowIdentity),
            nameof(Win32HotkeySource), nameof(Win32KeyInput),
            "Win32HostWindow", "HostNotification", "DwmComposition", "HighResolutionTimer", "Win32Displays", "DirectoryTextStore",
        };
        Assert.Equal(expected.OrderBy(x => x), asm.GetExportedTypes().Select(t => t.Name).OrderBy(x => x));
        Assert.True(typeof(IWindowSystem).IsAssignableFrom(typeof(Win32WindowSystem)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(Win32WindowSystem)));

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (Type t in asm.GetTypes().Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null))
        {
            var used = new List<Type>();
            foreach (FieldInfo f in t.GetFields(All)) { used.Add(f.FieldType); }
            foreach (PropertyInfo p in t.GetProperties(All)) { used.Add(p.PropertyType); }
            foreach (MethodBase m in t.GetMethods(All).Cast<MethodBase>().Concat(t.GetConstructors(All)))
            {
                used.AddRange(m.GetParameters().Select(x => x.ParameterType));
                if (m is MethodInfo mi) { used.Add(mi.ReturnType); }
            }

            Assert.DoesNotContain(used, u => u.Namespace == "CanvasWm.Engine.Tracking");
        }
    }

    [Fact]
    public void AC13_Describe_frame_equals_WindowGeometry()
    {
        using var rig = new Rig();
        nint hwnd = rig.CreateWindow(0, "Static", "CanvasWmSelf", Native.WS_OVERLAPPEDWINDOW | Native.WS_VISIBLE, 100, 100, 800, 600);
        Rect? described = rig.Describe(Rig.H(hwnd))!.Frame;
        Rect? direct = rig.Loop.Invoke(() =>
        {
            using var scope = new DpiScope();
            return WindowGeometry.ReadDipFrame(hwnd);
        });
        Assert.NotNull(described);
        Assert.Equal(described, direct);
        // "every existing #10 test still passes" is checked by running the whole Win32.Tests project.
    }
}
