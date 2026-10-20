using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Rendering;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tests.ServicesSpec;

public sealed record TestRestore(string Tag) : RestoreTarget;

public sealed class ValueTypeSpecTests
{
    [Fact]
    public void AC1_WindowKey()
    {
        Assert.Equal(new WindowKey("w1"), new WindowKey("w1"));
        Assert.Equal(new WindowKey("w1").GetHashCode(), new WindowKey("w1").GetHashCode());
        Assert.NotEqual(new WindowKey("w1"), new WindowKey("W1"));
        Assert.Equal("w1", new WindowKey("w1").ToString());
        Assert.Throws<ArgumentNullException>(() => new WindowKey(null!));
        Assert.Throws<ArgumentException>(() => new WindowKey(""));
        Assert.Equal("w1", new WindowKey("w1").Value);
        Assert.Equal(" ", new WindowKey(" ").Value);
    }

    [Fact]
    public void AC1_DisplayKey()
    {
        Assert.Equal(new DisplayKey("d"), new DisplayKey("d"));
        Assert.Equal(new DisplayKey("d").GetHashCode(), new DisplayKey("d").GetHashCode());
        Assert.NotEqual(new DisplayKey("d"), new DisplayKey("D"));
        Assert.Equal("d", new DisplayKey("d").ToString());
        Assert.Throws<ArgumentNullException>(() => new DisplayKey(null!));
        Assert.Throws<ArgumentException>(() => new DisplayKey(""));
    }

    [Fact]
    public void AC1_AppId()
    {
        Assert.Equal(new AppId("a"), new AppId("a"));
        Assert.Equal(new AppId("a").GetHashCode(), new AppId("a").GetHashCode());
        Assert.NotEqual(new AppId("a"), new AppId("A"));
        Assert.Equal("a", new AppId("a").ToString());
        Assert.Throws<ArgumentNullException>(() => new AppId(null!));
        Assert.Throws<ArgumentException>(() => new AppId(""));
    }

    [Fact]
    public void AC2_EngineKey()
    {
        var k = new EngineKey(new DisplayKey("ABC"), "7");
        Assert.Equal("ABC|7", k.Value);
        Assert.Equal("ABC|7", k.ToString());
        Assert.Equal(k, new EngineKey(new DisplayKey("ABC"), "7"));
        Assert.Equal(k.GetHashCode(), new EngineKey(new DisplayKey("ABC"), "7").GetHashCode());
        Assert.NotEqual(k, new EngineKey(new DisplayKey("ABC"), "8"));
        Assert.Throws<ArgumentException>(() => new EngineKey(new DisplayKey("ABC"), "a|b"));
        Assert.Throws<ArgumentException>(() => new EngineKey(new DisplayKey("ABC"), ""));
        Assert.Throws<ArgumentNullException>(() => new EngineKey(null!, "7"));
        Assert.Throws<ArgumentNullException>(() => new EngineKey(new DisplayKey("ABC"), null!));
        Assert.Equal("default", EngineKey.SingleDesktopId);
        Assert.Equal('|', EngineKey.Separator);

        Assert.True(EngineKey.TryParse("ABC|7", out var p));
        Assert.Equal(k, p);
        Assert.True(EngineKey.TryParse("a|b|7", out var q));
        Assert.Equal("a|b", q!.Display.Value);
        Assert.Equal("7", q.DesktopId);
        foreach (var bad in new string?[] { null, "ABC", "ABC|", "|7" })
        {
            Assert.False(EngineKey.TryParse(bad, out var r));
            Assert.Null(r);
        }
    }

    [Fact]
    public void AC2_E7_RoundTripWithPipeInDisplay()
    {
        var k = new EngineKey(new DisplayKey("x|y|z"), "d1");
        Assert.True(EngineKey.TryParse(k.Value, out var p));
        Assert.Equal(k, p);
    }

    [Fact]
    public void AC3_WindowInfo()
    {
        var key = new WindowKey("w");
        var f = new Rect(0, 0, 1, 1);
        Assert.Throws<ArgumentNullException>(() => new WindowInfo(null!, 1, "n", null, "t", f, true));
        Assert.Throws<ArgumentNullException>(() => new WindowInfo(key, 1, null!, null, "t", f, true));
        Assert.Throws<ArgumentNullException>(() => new WindowInfo(key, 1, "n", null, null!, f, true));
        var a = new WindowInfo(key, 1, "n", null, "t", f, true);
        Assert.Null(a.App);
        Assert.Equal(a, new WindowInfo(new WindowKey("w"), 1, "n", null, "t", f, true));
        Assert.Equal(new CaptureFrame(new Size(10, 20), 5), new CaptureFrame(new Size(10, 20), 5));
    }

    private static SavedDisplay Make(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? framings = null,
        IReadOnlyDictionary<WindowKey, string>? owners = null,
        IReadOnlyDictionary<WindowKey, DockSpot>? docks = null,
        IReadOnlyDictionary<WindowKey, Size>? floats = null) =>
        new(new LayoutDocument(PanelNode.ForWindow("a")), "n1", framings, owners, docks, floats);

    [Fact]
    public void AC4_InsertionOrderIrrelevant()
    {
        var o1 = new Dictionary<WindowKey, string> { [new WindowKey("a")] = "x|1", [new WindowKey("b")] = "x|2" };
        var o2 = new Dictionary<WindowKey, string> { [new WindowKey("b")] = "x|2", [new WindowKey("a")] = "x|1" };
        var f1 = new Dictionary<string, IReadOnlyList<string>> { ["1"] = new[] { "p" }, ["2"] = new[] { "q" } };
        var f2 = new Dictionary<string, IReadOnlyList<string>> { ["2"] = new[] { "q" }, ["1"] = new[] { "p" } };
        var d1 = new Dictionary<WindowKey, DockSpot>
        {
            [new WindowKey("a")] = new DockSpot(new TestRestore("r"), new[] { "l1", "l2" }),
            [new WindowKey("b")] = new DockSpot(new TestRestore("s"), Array.Empty<string>()),
        };
        var d2 = new Dictionary<WindowKey, DockSpot>
        {
            [new WindowKey("b")] = new DockSpot(new TestRestore("s"), Array.Empty<string>()),
            [new WindowKey("a")] = new DockSpot(new TestRestore("r"), new[] { "l1", "l2" }),
        };
        var s1 = Make(f1, o1, d1, new Dictionary<WindowKey, Size> { [new WindowKey("a")] = new Size(1, 2), [new WindowKey("b")] = new Size(3, 4) });
        var s2 = Make(f2, o2, d2, new Dictionary<WindowKey, Size> { [new WindowKey("b")] = new Size(3, 4), [new WindowKey("a")] = new Size(1, 2) });
        Assert.Equal(s1, s2);
        Assert.Equal(s1.GetHashCode(), s2.GetHashCode());
    }

    [Fact]
    public void AC4_FramingListOrderMatters()
    {
        var a = Make(new Dictionary<string, IReadOnlyList<string>> { ["1"] = new[] { "p", "q" } });
        var b = Make(new Dictionary<string, IReadOnlyList<string>> { ["1"] = new[] { "q", "p" } });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void AC4_NullVersusEmptyOptional()
    {
        Assert.NotEqual(Make(), Make(docks: new Dictionary<WindowKey, DockSpot>()));
        Assert.NotEqual(Make(), Make(floats: new Dictionary<WindowKey, Size>()));
        Assert.Equal(Make(), Make());
    }

    [Fact]
    public void AC4_CopiesInputsAndDefaults()
    {
        var list = new List<string> { "p" };
        var fr = new Dictionary<string, IReadOnlyList<string>> { ["1"] = list };
        var ow = new Dictionary<WindowKey, string> { [new WindowKey("a")] = "o|1" };
        var dk = new Dictionary<WindowKey, DockSpot> { [new WindowKey("a")] = new DockSpot(new TestRestore("r"), new[] { "l" }) };
        var fs = new Dictionary<WindowKey, Size> { [new WindowKey("a")] = new Size(1, 1) };
        var s = Make(fr, ow, dk, fs);
        var before = Make(
            new Dictionary<string, IReadOnlyList<string>> { ["1"] = new[] { "p" } },
            new Dictionary<WindowKey, string> { [new WindowKey("a")] = "o|1" },
            new Dictionary<WindowKey, DockSpot> { [new WindowKey("a")] = new DockSpot(new TestRestore("r"), new[] { "l" }) },
            new Dictionary<WindowKey, Size> { [new WindowKey("a")] = new Size(1, 1) });
        list.Clear(); fr.Clear(); ow.Clear(); dk.Clear(); fs.Clear();
        Assert.Equal(before, s);

        var d = Make();
        Assert.Empty(d.Framings);
        Assert.Empty(d.Owners);
        Assert.Null(d.DockSpots);
        Assert.Null(d.FloatSizes);
        Assert.Throws<ArgumentNullException>(() => new SavedDisplay(null!));
    }

    [Fact]
    public void AC4_DockSpotStructural()
    {
        var a = new DockSpot(new TestRestore("r"), new[] { "x", "y" });
        Assert.Equal(a, new DockSpot(new TestRestore("r"), new[] { "x", "y" }));
        Assert.Equal(a.GetHashCode(), new DockSpot(new TestRestore("r"), new[] { "x", "y" }).GetHashCode());
        Assert.NotEqual(a, new DockSpot(new TestRestore("r"), new[] { "y", "x" }));
        Assert.NotEqual(a, new DockSpot(new TestRestore("z"), new[] { "x", "y" }));
        Assert.Throws<ArgumentNullException>(() => new DockSpot(null!, Array.Empty<string>()));
        Assert.Throws<ArgumentNullException>(() => new DockSpot(new TestRestore("r"), null!));
        Assert.Throws<ArgumentNullException>(() => new DockSpot(new TestRestore("r"), new string[] { null! }));
    }

    [Fact]
    public void AC5_RenderPlan()
    {
        Assert.Empty(RenderPlan.Empty.Windows);
        var w = new WindowPlan(new WindowKey("a"), new Rect(0, 0, 1, 1), true);
        var p1 = new RenderPlan(new[] { w });
        var p2 = new RenderPlan(new[] { new WindowPlan(new WindowKey("a"), new Rect(0, 0, 1, 1), true) });
        Assert.Equal(p1, p2);
        Assert.Equal(p1.GetHashCode(), p2.GetHashCode());
        Assert.NotEqual(p1, RenderPlan.Empty);
        Assert.Throws<ArgumentNullException>(() => new RenderPlan(new WindowPlan[] { null! }));
        Assert.Throws<ArgumentNullException>(() => new RenderPlan(null!));
        Assert.Throws<ArgumentNullException>(() => new WindowPlan(null!, new Rect(0, 0, 1, 1), false));
    }

    [Fact]
    public void AC6_Learn()
    {
        Assert.Equal(new Size(500, 300), LimitRules.Learn(null, new Size(500, 300)));
        Assert.Equal(new Size(500, 350), LimitRules.Learn(new Size(500, 300), new Size(400, 350)));
        Assert.Equal(new Size(0, 10), LimitRules.Learn(null, new Size(-5, 10)));
        Assert.Equal(new Size(500, 300), LimitRules.Learn(new Size(500, 300), new Size(double.NaN, 10)));
    }

    [Fact]
    public void AC7_Observe()
    {
        var s = new Size(500, 350);
        Assert.Equal(s, LimitRules.Observe(s, new Size(499, 349)));
        Assert.Equal(s, LimitRules.Observe(s, new Size(498, 350)));
        Assert.Equal(new Size(497.9, 350), LimitRules.Observe(s, new Size(497.9, 350)));
        Assert.Equal(new Size(450, 350), LimitRules.Observe(s, new Size(450, 360)));
        Assert.Equal(new Size(499, 100), LimitRules.Observe(s, new Size(499, 100)));
        Assert.Null(LimitRules.Observe(null, new Size(100, 100)));
        Assert.Equal(s, LimitRules.Observe(s, new Size(double.PositiveInfinity, 1)));
        Assert.Equal(2.0, LimitRules.ObserveSlack);
    }
}
