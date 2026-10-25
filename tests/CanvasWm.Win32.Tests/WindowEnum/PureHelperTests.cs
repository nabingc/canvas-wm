using System.Reflection;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Windows;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.WindowEnumSpec;

/// <summary>AC7-AC13 and AC15 of specs/win32-window-enum.md: pure helpers, no window needed.</summary>
public sealed class PureHelperTests
{
    private const double Tol = 1e-9;

    private static void AssertRect(double x, double y, double w, double h, Rect actual)
    {
        Assert.InRange(actual.X, x - Tol, x + Tol);
        Assert.InRange(actual.Y, y - Tol, y + Tol);
        Assert.InRange(actual.Width, w - Tol, w + Tol);
        Assert.InRange(actual.Height, h - Tol, h + Tol);
    }

    [Fact]
    public void AC7_HandleConversion_RoundTrips()
    {
        foreach (nint x in new nint[] { 0, 1, 0x10010, nint.MaxValue, nint.MinValue, -1 })
        {
            Assert.Equal(x, HandleConversion.ToHwnd(HandleConversion.ToHandle(x)));
        }

        Assert.Equal(ulong.MaxValue, HandleConversion.ToHandle(-1));
        foreach (ulong u in new ulong[] { 0, 4211, ulong.MaxValue })
        {
            Assert.Equal(u, HandleConversion.ToHandle(HandleConversion.ToHwnd(u)));
        }
    }

    [Fact]
    public void AC8_DipSpace_ScaleFor()
    {
        Assert.Equal(1.0, DipSpace.ScaleFor(96), Tol);
        Assert.Equal(1.5, DipSpace.ScaleFor(144), Tol);
        Assert.Equal(2.0, DipSpace.ScaleFor(192), Tol);
        Assert.Throws<ArgumentOutOfRangeException>(() => DipSpace.ScaleFor(0));
    }

    [Fact]
    public void AC8_DipSpace_ToDip_WorkedExamples()
    {
        var m = new PixelRect(0, 0, 3840, 2160);
        AssertRect(0, 0, 2560, 1440, DipSpace.ToDip(m, m, 144));
        AssertRect(200, 100, 1000, 600, DipSpace.ToDip(new PixelRect(300, 150, 1800, 1050), m, 144));

        var n = new PixelRect(3840, 0, 5760, 1080);
        AssertRect(4000, 100, 800, 600, DipSpace.ToDip(new PixelRect(4000, 100, 4800, 700), n, 96));

        var l = new PixelRect(-3840, 0, 0, 2160);
        AssertRect(-3840, 0, 1920, 1080, DipSpace.ToDip(l, l, 192));
        AssertRect(-3740, 100, 500, 500, DipSpace.ToDip(new PixelRect(-3640, 200, -2640, 1200), l, 192));
    }

    [Fact]
    public void AC8_DipSpace_NonOverlap_And_NegativeWidth_And_ZeroDpi()
    {
        var a = new PixelRect(0, 0, 1920, 1080);
        var b = new PixelRect(1920, 0, 5760, 2160);
        AssertRect(0, 0, 1920, 1080, DipSpace.ToDip(a, a, 96));
        AssertRect(1920, 0, 1920, 1080, DipSpace.ToDip(b, b, 192));

        Rect neg = DipSpace.ToDip(new PixelRect(100, 0, 40, 10), a, 96);
        Assert.Equal(-60.0, neg.Width, Tol);
        Assert.Throws<ArgumentOutOfRangeException>(() => DipSpace.ToDip(a, a, 0));
        Assert.Equal(-60, new PixelRect(100, 0, 40, 10).Width);
    }

    [Fact]
    public void AC9_DecodeStyles_TruthTable()
    {
        StyleFacts s = FactMapping.DecodeStyles(0x00CF0000, 0);
        Assert.Equal(new StyleFacts(false, true, true, true, false, false, false), s);

        s = FactMapping.DecodeStyles(0x00C00000, 0);
        Assert.True(s.HasCaption);
        Assert.False(s.IsResizable);

        Assert.False(FactMapping.DecodeStyles(0x00800000, 0).HasCaption);
        Assert.True(FactMapping.DecodeStyles(0x40000000, 0).IsChild);

        s = FactMapping.DecodeStyles(0, 0x80 | 0x40000 | 0x08000000);
        Assert.True(s.IsToolWindow);
        Assert.True(s.IsAppWindow);
        Assert.True(s.IsNoActivate);
    }

    [Fact]
    public void AC9_IsShellClass()
    {
        foreach (string c in new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            Assert.True(FactMapping.IsShellClass(c), c);
        }

        foreach (string c in new[] { "progman", "Notepad", "", "ApplicationFrameWindow" })
        {
            Assert.False(FactMapping.IsShellClass(c), c);
        }
    }

    [Fact]
    public void AC10_ToCloakKind()
    {
        Assert.Equal(CloakKind.None, FactMapping.ToCloakKind(false, 2));
        Assert.Equal(CloakKind.None, FactMapping.ToCloakKind(true, 0));
        Assert.Equal(CloakKind.Shell, FactMapping.ToCloakKind(true, 2));
        Assert.Equal(CloakKind.App, FactMapping.ToCloakKind(true, 1));
        Assert.Equal(CloakKind.Inherited, FactMapping.ToCloakKind(true, 4));
        Assert.Equal(CloakKind.Shell, FactMapping.ToCloakKind(true, 3));
        Assert.Equal(CloakKind.App, FactMapping.ToCloakKind(true, 5));
        Assert.Equal(CloakKind.Shell, FactMapping.ToCloakKind(true, 6));
    }

    [Fact]
    [Trait("Assumption", "A10")]
    public void AC10_ToCloakKind_UnknownBitsMapToApp()
    {
        Assert.Equal(CloakKind.App, FactMapping.ToCloakKind(true, 8));
    }

    [Fact]
    public void AC10_MaximizeButtonEnabled()
    {
        Assert.Null(FactMapping.MaximizeButtonEnabled(false, 0));
        Assert.True(FactMapping.MaximizeButtonEnabled(true, 0));
        Assert.False(FactMapping.MaximizeButtonEnabled(true, 0x1));
        Assert.Null(FactMapping.MaximizeButtonEnabled(true, 0x8000));
        Assert.Null(FactMapping.MaximizeButtonEnabled(true, 0x8001));
        Assert.True(FactMapping.MaximizeButtonEnabled(true, 0x40));
    }

    [Fact]
    [Trait("Assumption", "A18")]
    public void AC11_IsFullScreen()
    {
        var m = new PixelRect(0, 0, 1920, 1080);
        Assert.True(FactMapping.IsFullScreen(m, m, false));
        Assert.True(FactMapping.IsFullScreen(new PixelRect(-8, -8, 1928, 1088), m, false));
        Assert.False(FactMapping.IsFullScreen(m, m, true));
        // Normal maximised window: caption, borders overhang, bottom stops at the taskbar.
        Assert.False(FactMapping.IsFullScreen(new PixelRect(-8, -8, 1936, 1056), m, true));
        Assert.False(FactMapping.IsFullScreen(new PixelRect(0, 0, 1920, 1040), m, false));
        Assert.False(FactMapping.IsFullScreen(new PixelRect(1, 0, 1920, 1080), m, false));
    }

    [Fact]
    [Trait("Assumption", "A3")]
    public void AC12_NormalizeAppId()
    {
        Assert.Equal("c:\\program files\\app\\x.exe", FactMapping.NormalizeAppId("C:\\Program Files\\App\\X.EXE"));
        Assert.Equal("microsoft.windowscalculator_8wekyb3d8bbwe!app",
            FactMapping.NormalizeAppId("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));
    }

    [Fact]
    public void AC12_AppNameFrom()
    {
        Assert.Equal("Editor", FactMapping.AppNameFrom(" Editor ", "c:\\a\\ed.exe"));
        Assert.Equal("ed", FactMapping.AppNameFrom("  ", "c:\\a\\ed.exe"));
        Assert.Null(FactMapping.AppNameFrom(null, null));
        Assert.Null(FactMapping.AppNameFrom(null, ""));
    }

    [Fact]
    [Trait("Assumption", "A7")]
    public void AC12_IsAccessible()
    {
        Assert.True(FactMapping.IsAccessible(true, null, 0x2000));
        Assert.False(FactMapping.IsAccessible(false, null, 0x2000));
        Assert.True(FactMapping.IsAccessible(false, 0x2000, 0x2000));
        Assert.True(FactMapping.IsAccessible(false, 0x1000, 0x2000));
        Assert.False(FactMapping.IsAccessible(false, 0x3000, 0x2000));
        Assert.True(FactMapping.IsAccessible(false, 0x3000, 0x3000));
        Assert.False(FactMapping.IsAccessible(false, 0x4000, 0x3000));
    }

    [Fact]
    public void AC13_HookRanges_AreTheSixRangesInOrder()
    {
        (uint, uint)[] expected =
        [
            (0x0003, 0x0003), (0x0016, 0x0017), (0x8000, 0x8003),
            (0x8005, 0x8005), (0x800B, 0x800C), (0x8017, 0x8018),
        ];
        Assert.Equal(expected, WinEventMapping.HookRanges.Select(r => (r.Min, r.Max)).ToArray());
    }

    [Fact]
    public void AC13_KindFor_MapsExactlyTwelveIds()
    {
        var expected = new Dictionary<uint, RawWindowEventKind>
        {
            [0x0003] = RawWindowEventKind.Foreground,
            [0x0016] = RawWindowEventKind.Minimized,
            [0x0017] = RawWindowEventKind.Restored,
            [0x8000] = RawWindowEventKind.Created,
            [0x8001] = RawWindowEventKind.Destroyed,
            [0x8002] = RawWindowEventKind.Shown,
            [0x8003] = RawWindowEventKind.Hidden,
            [0x8005] = RawWindowEventKind.FocusChanged,
            [0x800B] = RawWindowEventKind.LocationChanged,
            [0x800C] = RawWindowEventKind.TitleChanged,
            [0x8017] = RawWindowEventKind.Cloaked,
            [0x8018] = RawWindowEventKind.Uncloaked,
        };
        Assert.Equal(12, expected.Count);
        foreach (var (id, kind) in expected)
        {
            Assert.Equal(kind, WinEventMapping.KindFor(id));
        }

        int mapped = 0;
        for (uint id = 0; id <= 0x8100; id++)
        {
            if (WinEventMapping.KindFor(id) is not null)
            {
                mapped++;
            }
        }

        Assert.Equal(12, mapped);
        foreach (uint id in new uint[] { 0x0001, 0x0015, 0x0018, 0x8004, 0x8006, 0x800A, 0x800D, 0x800E, 0x8016, 0x8019 })
        {
            Assert.Null(WinEventMapping.KindFor(id));
        }
    }

    [Fact]
    [Trait("Assumption", "A11")]
    public void AC13_Translate_WithFakes()
    {
        Func<nint, bool> isWindow = h => h is 10 or 11;
        Func<nint, nint> rootOf = h => h switch { 10 => 10, 11 => 10, _ => 0 };
        RawWindowEvent? T(uint ev, nint h, int o, int c) => WinEventMapping.Translate(ev, h, o, c, isWindow, rootOf);

        Assert.Equal(new RawWindowEvent(RawWindowEventKind.Created, 10), T(0x8000, 10, 0, 0));
        Assert.Null(T(0x8000, 11, 0, 0));
        Assert.Equal(new RawWindowEvent(RawWindowEventKind.Destroyed, 99), T(0x8001, 99, 0, 0));
        Assert.Null(T(0x8002, 10, -4, 0));
        Assert.Null(T(0x800C, 10, 0, 5));
        Assert.Null(T(0x800B, 0, 0, 0));
        Assert.Equal(new RawWindowEvent(RawWindowEventKind.FocusChanged, 10), T(0x8005, 11, -4, 3));
        Assert.Equal(new RawWindowEvent(RawWindowEventKind.FocusChanged, 0), T(0x8005, 0, 0, 0));
        Assert.Equal(new RawWindowEvent(RawWindowEventKind.FocusChanged, 99), T(0x8005, 99, 0, 0));
        Assert.Equal(new RawWindowEvent(RawWindowEventKind.Foreground, 10), T(0x0003, 10, 0, 0));
        Assert.Null(T(0x800E, 10, 0, 0));
    }

    [Fact]
    public void AC15_PublicSurface()
    {
        Assembly asm = typeof(Win32WindowSource).Assembly;
        string[] names = asm.GetExportedTypes().Select(t => t.FullName!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        string[] expected =
        [
            "CanvasWm.Win32.Coordinates.DipSpace",
            "CanvasWm.Win32.Coordinates.PixelRect",
            "CanvasWm.Win32.Windows.HandleConversion",
            "CanvasWm.Win32.Windows.Win32WindowSource",
        ];
        Assert.Equal(expected, names);
        Assert.True(typeof(IWindowSource).IsAssignableFrom(typeof(Win32WindowSource)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(Win32WindowSource)));

        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var offenders = new List<string>();
        foreach (Type t in asm.GetTypes())
        {
            foreach (FieldInfo f in t.GetFields(All)) { Check(t, f.Name, f.FieldType); }
            foreach (PropertyInfo p in t.GetProperties(All)) { Check(t, p.Name, p.PropertyType); }
            foreach (MethodBase m in t.GetMethods(All).Cast<MethodBase>().Concat(t.GetConstructors(All)))
            {
                foreach (ParameterInfo pa in m.GetParameters()) { Check(t, m.Name, pa.ParameterType); }
                if (m is MethodInfo mi) { Check(t, m.Name, mi.ReturnType); }
            }
        }

        Assert.Empty(offenders);

        void Check(Type owner, string member, Type type)
        {
            if (Forbidden(type))
            {
                offenders.Add($"{owner.FullName}.{member}: {type.FullName}");
            }
        }

        static bool Forbidden(Type type)
        {
            if (type.HasElementType && type.GetElementType() is { } e && Forbidden(e)) { return true; }
            if (type.IsGenericType && type.GetGenericArguments().Any(Forbidden)) { return true; }
            return type.Namespace == "CanvasWm.Engine.Tracking" || type.Name == "ClassifierPolicy";
        }
    }
}
