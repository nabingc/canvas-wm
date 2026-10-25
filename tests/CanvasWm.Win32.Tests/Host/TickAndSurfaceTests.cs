using System.Diagnostics;
using System.Reflection;
using CanvasWm.Engine.Persistence;
using CanvasWm.Win32.Hosting;
using CanvasWm.Win32.Storage;

namespace CanvasWm.Win32.Tests.HostSpec;

/// <summary>AC27 and AC28 of specs/app-host.md.</summary>
public sealed class TickAndSurfaceTests
{
    // ---- AC27 ----

    [Fact]
    public void AC27_DwmFlushAndRefreshRate()
    {
        Assert.True(DwmComposition.Flush());
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 30; i++) Assert.True(DwmComposition.Flush());
        sw.Stop();
        Assert.InRange(sw.Elapsed.TotalSeconds, 0.1, 3.0);
        double? hz = DwmComposition.RefreshRate();
        if (hz is double v) Assert.InRange(v, 20, 1000);
    }

    [Fact]
    public void AC27_TimerWait()
    {
        using var timer = new HighResolutionTimer();
        double best = double.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            Assert.True(timer.Wait(0.01));
            sw.Stop();
            Assert.True(sw.Elapsed.TotalMilliseconds >= 9, $"waited {sw.Elapsed.TotalMilliseconds} ms");
            best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
        }

        Assert.True(best <= 50, $"best of five 10 ms waits took {best} ms");
        var z = Stopwatch.StartNew();
        Assert.True(timer.Wait(0));
        Assert.True(z.Elapsed.TotalMilliseconds < 20);
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Wait(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Wait(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => timer.Wait(double.PositiveInfinity));
    }

    [Fact]
    public void AC27_TimerAfterDispose()
    {
        var timer = new HighResolutionTimer();
        timer.Dispose();
        Assert.Throws<ObjectDisposedException>(() => timer.Wait(0.001));
        timer.Dispose();
    }

    // ---- AC28 ----

    private static IEnumerable<Type> TypesOf(Type t)
    {
        if (t.HasElementType) { foreach (var x in TypesOf(t.GetElementType()!)) yield return x; yield break; }
        yield return t;
        if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) foreach (var x in TypesOf(a)) yield return x;
    }

    [Fact]
    public void AC28_ExportedTypesExactly15()
    {
        string[] names = typeof(Win32HostWindow).Assembly.GetExportedTypes().Select(t => t.FullName!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        string[] expected =
        [
            "CanvasWm.Win32.Coordinates.DipSpace",
            "CanvasWm.Win32.Coordinates.PixelRect",
            "CanvasWm.Win32.Displays.Win32Displays",
            "CanvasWm.Win32.Hosting.DwmComposition",
            "CanvasWm.Win32.Hosting.HighResolutionTimer",
            "CanvasWm.Win32.Hosting.HostNotification",
            "CanvasWm.Win32.Hosting.Win32HostWindow",
            "CanvasWm.Win32.Hotkeys.Win32HotkeySource",
            "CanvasWm.Win32.Hotkeys.Win32KeyInput",
            "CanvasWm.Win32.Storage.DirectoryTextStore",
            "CanvasWm.Win32.Windows.HandleConversion",
            "CanvasWm.Win32.Windows.IManagedWindowMap",
            "CanvasWm.Win32.Windows.ManagedWindowIdentity",
            "CanvasWm.Win32.Windows.Win32WindowSource",
            "CanvasWm.Win32.Windows.Win32WindowSystem",
        ];
        Assert.Equal(15, names.Length);
        Assert.Equal(expected, names);
    }

    [Fact]
    public void AC28_NoForbiddenMemberTypes()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        string[] bad = ["CanvasWm.Engine.Tracking", "System.Windows.Forms", "CanvasWm.Engine.Hosting"];
        var offenders = new List<string>();
        foreach (Type t in typeof(Win32HostWindow).Assembly.GetExportedTypes())
        {
            foreach (MemberInfo m in t.GetMembers(all))
            {
                if (m is MethodBase { IsPublic: false, IsFamily: false }) continue;
                if (m is FieldInfo { IsPublic: false, IsFamily: false }) continue;
                IEnumerable<Type> used = m switch
                {
                    FieldInfo f => TypesOf(f.FieldType),
                    PropertyInfo p => TypesOf(p.PropertyType),
                    EventInfo e => TypesOf(e.EventHandlerType!),
                    MethodBase mb => mb.GetParameters().SelectMany(p => TypesOf(p.ParameterType))
                        .Concat(mb is MethodInfo mi ? TypesOf(mi.ReturnType) : []),
                    _ => [],
                };
                foreach (Type u in used)
                    if (u.Namespace is string ns && bad.Any(b => ns == b || ns.StartsWith(b + ".", StringComparison.Ordinal)))
                        offenders.Add(t.Name + "." + m.Name + " : " + u.FullName);
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void AC28_DirectoryTextStoreImplementsInterface()
    {
        Assert.True(typeof(ITextFileStore).IsAssignableFrom(typeof(DirectoryTextStore)));
    }
}
