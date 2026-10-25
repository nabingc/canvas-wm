using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.EngineShellSpec;
using CanvasWm.Engine.Tests.TrackingSpec;
using CanvasWm.Engine.Tracking;
using RigT = CanvasWm.Engine.Tests.TrackingSpec.Rig;
using RigE = CanvasWm.Engine.Tests.EngineShellSpec.Rig;

namespace CanvasWm.Engine.Tests.RoutingSpec;

public sealed class AdditionsTests
{
    private static DisplayInfo D(string k = "A", Rect? frame = null, Rect? work = null, int dpi = 96, bool main = true) =>
        new(new DisplayKey(k), frame ?? new Rect(0, 0, 100, 100), work ?? new Rect(0, 0, 100, 90), dpi, main);

    // ---- AC2 ----

    [Fact]
    public void AC2_SignatureEqualities()
    {
        var a = D("A", main: true);
        var b = D("B", new Rect(100, 0, 50, 50), main: false);
        string baseSig = DisplayInfo.SignatureOf(new[] { a, b });
        Assert.Equal("A@0,0,100,100@96;B@100,0,50,50@96", baseSig);
        Assert.Equal(baseSig, DisplayInfo.SignatureOf(new[] { a with { WorkArea = new Rect(0, 0, 100, 10) }, b }));
        Assert.Equal(baseSig, DisplayInfo.SignatureOf(new[] { a with { IsMain = false }, b with { IsMain = true } }));
        Assert.Equal(baseSig, DisplayInfo.SignatureOf(new[] { b, a }));
    }

    [Fact]
    public void AC2_SignatureDifferences()
    {
        var a = D("A");
        var b = D("B", new Rect(100, 0, 50, 50), main: false);
        string baseSig = DisplayInfo.SignatureOf(new[] { a, b });
        Assert.NotEqual(baseSig, DisplayInfo.SignatureOf(new[] { a, b with { Frame = new Rect(101, 0, 50, 50) } }));
        Assert.NotEqual(baseSig, DisplayInfo.SignatureOf(new[] { a, b with { Frame = new Rect(100, 0, 51, 50) } }));
        Assert.NotEqual(baseSig, DisplayInfo.SignatureOf(new[] { a, b with { Dpi = 120 } }));
        Assert.NotEqual(baseSig, DisplayInfo.SignatureOf(new[] { a }));
        Assert.NotEqual(baseSig, DisplayInfo.SignatureOf(new[] { a, b, D("C", main: false) }));
    }

    [Fact]
    public void AC2_NullHandling()
    {
        Assert.Throws<ArgumentNullException>(() => DisplayInfo.SignatureOf(null!));
        Assert.Throws<ArgumentNullException>(() => DisplayInfo.SignatureOf(new DisplayInfo[] { null! }));
    }

    // ---- AC3 ----

    [Fact]
    public void AC3_Validation()
    {
        var ok = new Rect(0, 0, 10, 10);
        Assert.Throws<ArgumentNullException>(() => new DisplayInfo(null!, ok, ok, 96, true));
        foreach (var bad in new[]
        {
            new Rect(double.NaN, 0, 1, 1), new Rect(0, double.PositiveInfinity, 1, 1),
            new Rect(0, 0, double.NegativeInfinity, 1), new Rect(0, 0, 1, double.NaN),
            new Rect(0, 0, -1, 1), new Rect(0, 0, 1, -1),
        })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayInfo(new DisplayKey("A"), bad, ok, 96, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayInfo(new DisplayKey("A"), ok, bad, 96, true));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayInfo(new DisplayKey("A"), ok, ok, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayInfo(new DisplayKey("A"), ok, ok, -1, true));
        var zero = new DisplayInfo(new DisplayKey("A"), new Rect(0, 0, 0, 0), new Rect(5, 5, 0, 0), 96, true);
        Assert.Equal(0, zero.Frame.Width);
    }

    // Spec AC3 / API: "(also via `with`)" - init accessors must validate.
    [Fact]
    public void AC3_ValidationViaWith()
    {
        var good = D();
        foreach (var bad in new[] { new Rect(double.NaN, 0, 1, 1), new Rect(0, 0, -1, 1), new Rect(0, 0, 1, double.PositiveInfinity) })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => good with { Frame = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => good with { WorkArea = bad });
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => good with { Dpi = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => good with { Dpi = -1 });
        Assert.Throws<ArgumentNullException>(() => good with { Key = null! });
    }

    // ---- AC4 ----

    [Fact]
    public void AC4_OptionsDefaults()
    {
        var d = RouterOptions.Default;
        Assert.Equal(1.5, d.DisplaySetChangeDelaySeconds);
        Assert.Equal(0.4, d.WorkAreaRefreshDelaySeconds);
        Assert.Equal(0.8, d.ReshowDelaySeconds);
        Assert.Equal(2.0, d.ShutdownTimeoutSeconds);
        Assert.True(ReferenceEquals(RouterOptions.Default, RouterOptions.Default));
    }

    [Fact]
    public void AC4_OptionsRejectBadValues()
    {
        foreach (double bad in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RouterOptions.Default with { DisplaySetChangeDelaySeconds = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => RouterOptions.Default with { WorkAreaRefreshDelaySeconds = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => RouterOptions.Default with { ReshowDelaySeconds = bad });
            Assert.Throws<ArgumentOutOfRangeException>(() => RouterOptions.Default with { ShutdownTimeoutSeconds = bad });
        }

        var o = RouterOptions.Default with { ReshowDelaySeconds = 3 };
        Assert.Equal(3, o.ReshowDelaySeconds);
        Assert.Equal(0.8, RouterOptions.Default.ReshowDelaySeconds);
    }

    [Fact]
    public void AC4_ExactlyFourPublicInstanceProperties()
    {
        var props = typeof(RouterOptions).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.Name != "EqualityContract").ToList();
        Assert.Equal(4, props.Count);
    }

    // ---- AC5 ----

    [Fact]
    public void AC5_TrackerIdentity()
    {
        var r = new RigT();
        r.Put(RigT.Std(42, 7) with { ThreadId = 9 });
        var w42 = RigT.K(42);
        var w43 = RigT.K(43);
        Assert.Null(r.Tracker.IdentityOf(w42));   // before Start
        Assert.Null(r.Tracker.KeyOf(42));
        r.Tracker.Start();
        r.Target.Clear();
        int en = r.Source.EnumerateCount, de = r.Source.DescribeCount, calls = r.Target.Calls.Count;

        Assert.Equal(new TrackedIdentity(42, 7, 9), r.Tracker.IdentityOf(w42));
        Assert.Null(r.Tracker.IdentityOf(w43));
        Assert.Equal(w42, r.Tracker.KeyOf(42));
        Assert.Null(r.Tracker.KeyOf(43));
        Assert.Null(r.Tracker.KeyOf(0));
        Assert.Throws<ArgumentNullException>(() => r.Tracker.IdentityOf(null!));
        Assert.Equal(en, r.Source.EnumerateCount);
        Assert.Equal(de, r.Source.DescribeCount);
        Assert.Equal(calls, r.Target.Calls.Count);

        r.Source.Remove(42);
        r.Source.Raise(CanvasWm.Engine.Services.RawWindowEventKind.Destroyed, 42);
        Assert.Null(r.Tracker.IdentityOf(w42));
        Assert.Null(r.Tracker.KeyOf(42));
    }

    // ---- AC6 ----

    private static RigE Focused(out WindowInfo w1)
    {
        var rig = new RigE();
        w1 = RigE.W("w1", 100);
        rig.StartWith(w1);
        rig.Win.Pump();
        rig.Clock.Advance(1);
        rig.Win.Pump();
        rig.E.WindowFocused(w1.Key);
        rig.Win.Pump();
        return rig;
    }

    [Fact]
    [Trait("Assumption", "A12")]
    public void AC6_FocusLeftWindowsClearsFocusOnly()
    {
        var rig = Focused(out var w1);
        Assert.Equal(w1.Key, rig.E.FocusedKey);
        rig.Log.Clear();
        string before = rig.Snap();
        rig.E.FocusLeftWindows(100);
        Assert.Null(rig.E.FocusedKey);
        Assert.Equal(new[] { "change" }, rig.Log);
        Assert.Equal(before, rig.Snap());   // no plan, no save, no write, no focus request, no clock action
    }

    [Fact]
    public void AC6_UnrelatedPidSameEffect()
    {
        var rig = Focused(out _);
        rig.Log.Clear();
        string before = rig.Snap();
        rig.E.FocusLeftWindows(999);
        Assert.Null(rig.E.FocusedKey);
        Assert.Equal(new[] { "change" }, rig.Log);
        Assert.Equal(before, rig.Snap());
    }

    [Fact]
    public void AC6_SuspendedEngineKeepsFocus()
    {
        var rig = Focused(out var w1);
        rig.E.Suspend();
        rig.E.FocusLeftWindows(100);
        Assert.Equal(w1.Key, rig.E.FocusedKey);
    }

    [Fact]
    public void AC6_ReentryFromOnChangeThrows()
    {
        var rig = Focused(out _);
        Exception? caught = null;
        rig.E.OnChange = () =>
        {
            try { rig.E.FocusLeftWindows(1); }
            catch (Exception ex) { caught = ex; }
        };
        rig.E.FocusLeftWindows(100);
        Assert.IsType<InvalidOperationException>(caught);
    }

    [Fact]
    public void AC6_AlreadyClearNoChange()
    {
        var rig = new RigE();
        rig.StartWith(RigE.W("w1", 100));
        rig.Log.Clear();
        rig.E.FocusLeftWindows(1);
        Assert.Empty(rig.Log);
    }
}
