using CanvasWm.Core.Geometry;
using CanvasWm.Core.Windows;

namespace CanvasWm.Core.Tests.WindowsSpec;

public sealed class ClassifierTests
{
    internal static OsWindowFacts Std(ulong h, int pid) => new()
    {
        Handle = h, ProcessId = pid, ThreadId = pid * 10, AppId = "app" + pid, AppName = "App" + pid,
        Title = "t" + h, Frame = new Rect(100, 100, 900, 700),
        IsTopLevel = true, HasCaption = true, IsResizable = true, HasMaximizeButton = true,
        IsVisible = true, IsAccessible = true, MaximizeEnabled = true, Cloak = CloakKind.None,
    };

    private static readonly ClassifierPolicy P = ClassifierPolicy.Default;

    [Fact]
    public void AC1_StandardWindowIsManagedDockable()
    {
        var c = WindowClassifier.Classify(Std(4211, 7), P);
        Assert.True(c.IsManaged);
        Assert.True(c.Dockable);
        Assert.Equal(IgnoreReason.None, c.Reason);
        Assert.Equal(WindowClassification.Managed(true), c);
    }

    [Theory]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, null, true)]
    [InlineData(true, true, true, true)]
    public void AC2_DockableTruthTable(bool resizable, bool maxBtn, bool? maxEnabled, bool expected)
    {
        var f = Std(1, 7) with { IsResizable = resizable, HasMaximizeButton = maxBtn, MaximizeEnabled = maxEnabled };
        Assert.Equal(expected, WindowClassifier.IsDockable(f));
        var c = WindowClassifier.Classify(f, P);
        Assert.True(c.IsManaged);
        Assert.Equal(expected, c.Dockable);
    }

    [Theory]
    [InlineData(120, 80, true, IgnoreReason.None)]
    [InlineData(119.999, 80, false, IgnoreReason.TooSmall)]
    [InlineData(120, 79.999, false, IgnoreReason.TooSmall)]
    [InlineData(100, 60, false, IgnoreReason.TooSmall)]
    public void AC3_SizeBoundaries(double w, double h, bool managed, IgnoreReason reason)
    {
        var c = WindowClassifier.Classify(Std(1, 7) with { Frame = new Rect(0, 0, w, h) }, P);
        Assert.Equal(managed, c.IsManaged);
        Assert.Equal(reason, c.Reason);
    }

    [Fact]
    public void AC3_NoFrameCases()
    {
        Assert.Equal(IgnoreReason.NoFrame, WindowClassifier.Classify(Std(1, 7) with { Frame = null }, P).Reason);
        Assert.Equal(IgnoreReason.NoFrame,
            WindowClassifier.Classify(Std(1, 7) with { Frame = new Rect(double.NaN, 0, 500, 500) }, P).Reason);
        Assert.Equal(IgnoreReason.NoFrame,
            WindowClassifier.Classify(Std(1, 7) with { Frame = new Rect(0, 0, double.PositiveInfinity, 500) }, P).Reason);
    }

    [Fact]
    public void AC3_ZeroMinimumAdmitsOneByOne_AndNegativeFrameIsTooSmall()
    {
        var zero = P with { MinimumSize = new Size(0, 0) };
        Assert.True(WindowClassifier.Classify(Std(1, 7) with { Frame = new Rect(0, 0, 1, 1) }, zero).IsManaged);
        Assert.Equal(IgnoreReason.TooSmall,
            WindowClassifier.Classify(Std(1, 7) with { Frame = new Rect(0, 0, -1, 10) }, zero).Reason);
        Assert.Equal(IgnoreReason.TooSmall,
            WindowClassifier.Classify(Std(1, 7) with { Frame = new Rect(0, 0, -1, 10) }, P).Reason);
    }

    public static TheoryData<string, IgnoreReason> Rows() => new()
    {
        { "Handle0", IgnoreReason.NoHandle },
        { "Own", IgnoreReason.OwnProcess },
        { "Shell", IgnoreReason.ShellWindow },
        { "Excluded", IgnoreReason.ExcludedApp },
        { "NotTop", IgnoreReason.NotTopLevel },
        { "Child", IgnoreReason.NotTopLevel },
        { "Owned", IgnoreReason.NotStandard },
        { "Tool", IgnoreReason.NotStandard },
        { "NoAct", IgnoreReason.NotStandard },
        { "NoCapNoRes", IgnoreReason.NotStandard },
        { "Invisible", IgnoreReason.Hidden },
        { "CloakShell", IgnoreReason.Cloaked },
        { "CloakApp", IgnoreReason.Cloaked },
        { "CloakInh", IgnoreReason.Cloaked },
        { "Min", IgnoreReason.Minimized },
        { "Full", IgnoreReason.FullScreen },
        { "Hung", IgnoreReason.Hung },
        { "Inacc", IgnoreReason.Inaccessible },
    };

    private static (OsWindowFacts, ClassifierPolicy) Apply(string row)
    {
        var f = Std(5, 7);
        var p = P;
        switch (row)
        {
            case "Handle0": f = f with { Handle = 0 }; break;
            case "Own": f = f with { IsOwnProcess = true }; break;
            case "Shell": f = f with { IsShellWindow = true }; break;
            case "Excluded": p = P.WithExcludedApps(new[] { "app7" }); break;
            case "NotTop": f = f with { IsTopLevel = false }; break;
            case "Child": f = f with { IsChild = true }; break;
            case "Owned": f = f with { IsOwned = true }; break;
            case "Tool": f = f with { IsToolWindow = true }; break;
            case "NoAct": f = f with { IsNoActivate = true }; break;
            case "NoCapNoRes": f = f with { HasCaption = false, IsResizable = false }; break;
            case "Invisible": f = f with { IsVisible = false }; break;
            case "CloakShell": f = f with { Cloak = CloakKind.Shell }; break;
            case "CloakApp": f = f with { Cloak = CloakKind.App }; break;
            case "CloakInh": f = f with { Cloak = CloakKind.Inherited }; break;
            case "Min": f = f with { IsMinimized = true }; break;
            case "Full": f = f with { IsFullScreen = true }; break;
            case "Hung": f = f with { IsHung = true }; break;
            case "Inacc": f = f with { IsAccessible = false }; break;
        }

        return (f, p);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void AC4_SingleFactTruthTable(string row, IgnoreReason expected)
    {
        var (f, p) = Apply(row);
        var c = WindowClassifier.Classify(f, p);
        Assert.False(c.IsManaged);
        Assert.False(c.Dockable);
        Assert.Equal(expected, c.Reason);
    }

    [Fact]
    public void AC4_StayManaged()
    {
        Assert.True(WindowClassifier.Classify(Std(5, 7) with { IsOwned = true, IsAppWindow = true }, P).IsManaged);
        Assert.True(WindowClassifier.Classify(Std(5, 7) with { HasCaption = false, IsResizable = true }, P).IsManaged);
        Assert.True(WindowClassifier.Classify(Std(5, 7) with { AppId = null }, P.WithExcludedApps(new[] { "app7" })).IsManaged);
        Assert.True(WindowClassifier.Classify(Std(5, 7), P.WithExcludedApps(new[] { "APP7" })).IsManaged);
    }

    [Fact]
    public void AC5_Precedence()
    {
        Assert.Equal(IgnoreReason.OwnProcess,
            WindowClassifier.Classify(Std(5, 7) with { IsOwnProcess = true, IsMinimized = true }, P).Reason);
        Assert.Equal(IgnoreReason.Minimized,
            WindowClassifier.Classify(Std(5, 7) with { IsMinimized = true, Frame = new Rect(0, 0, 10, 10) }, P).Reason);
        Assert.Equal(IgnoreReason.NotStandard,
            WindowClassifier.Classify(Std(5, 7) with { IsToolWindow = true, IsVisible = false }, P).Reason);
        Assert.Equal(IgnoreReason.FullScreen,
            WindowClassifier.Classify(Std(5, 7) with { IsFullScreen = true, IsAccessible = false }, P).Reason);
    }

    public static TheoryData<string, TrackedFate> FateRows() => new()
    {
        { "null", TrackedFate.RemovePermanent },
        { "own", TrackedFate.RemovePermanent },
        { "shell", TrackedFate.RemovePermanent },
        { "excluded", TrackedFate.RemovePermanent },
        { "hungmin", TrackedFate.Keep },
        { "min", TrackedFate.RemoveTemporary },
        { "invisible", TrackedFate.RemoveTemporary },
        { "cloakapp", TrackedFate.RemoveTemporary },
        { "cloakshell", TrackedFate.Keep },
        { "cloakinh", TrackedFate.Keep },
        { "std", TrackedFate.Keep },
        { "nondock", TrackedFate.Keep },
        { "full", TrackedFate.RemoveTemporary },
        { "small", TrackedFate.RemoveTemporary },
        { "tool", TrackedFate.RemoveTemporary },
        { "inacc", TrackedFate.RemoveTemporary },
        { "noframe", TrackedFate.RemoveTemporary },
    };

    [Theory]
    [MemberData(nameof(FateRows))]
    public void AC6_FateTable(string row, TrackedFate expected)
    {
        var s = Std(5, 7);
        var p = P;
        OsWindowFacts? f = row switch
        {
            "null" => null,
            "own" => s with { IsOwnProcess = true },
            "shell" => s with { IsShellWindow = true },
            "excluded" => s,
            "hungmin" => s with { IsHung = true, IsMinimized = true },
            "min" => s with { IsMinimized = true },
            "invisible" => s with { IsVisible = false },
            "cloakapp" => s with { Cloak = CloakKind.App },
            "cloakshell" => s with { Cloak = CloakKind.Shell },
            "cloakinh" => s with { Cloak = CloakKind.Inherited },
            "std" => s,
            "nondock" => s with { IsResizable = false, HasMaximizeButton = false },
            "full" => s with { IsFullScreen = true },
            "small" => s with { Frame = new Rect(0, 0, 100, 60) },
            "tool" => s with { IsToolWindow = true },
            "inacc" => s with { IsAccessible = false },
            "noframe" => s with { Frame = null },
            _ => throw new InvalidOperationException(),
        };
        if (row == "excluded") p = P.WithExcludedApps(new[] { "app7" });
        Assert.Equal(expected, WindowClassifier.Fate(f, p));
    }

    [Fact]
    public void E1_NullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => WindowClassifier.Classify(null!, P));
        Assert.Throws<ArgumentNullException>(() => WindowClassifier.Classify(Std(1, 7), null!));
        Assert.Throws<ArgumentNullException>(() => WindowClassifier.IsDockable(null!));
        Assert.Throws<ArgumentNullException>(() => WindowClassifier.Fate(Std(1, 7), null!));
        Assert.Equal(TrackedFate.RemovePermanent, WindowClassifier.Fate(null, P));
    }
}
