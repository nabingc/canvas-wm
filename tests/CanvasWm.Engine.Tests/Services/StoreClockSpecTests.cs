using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.ServicesSpec;

public sealed class StoreClockSpecTests
{
    [Fact]
    public void AC8_LimitStoreSequence()
    {
        var st = new InMemoryLimitStore();
        var app = new AppId("com.x");
        st.Learn(app, new Size(500, 300));
        Assert.Equal(new Size(500, 300), st.Minimum(app));
        st.Learn(app, new Size(400, 350));
        Assert.Equal(new Size(500, 350), st.Minimum(app));
        st.Observe(app, new Size(499, 349));
        Assert.Equal(new Size(500, 350), st.Minimum(app));
        st.Observe(app, new Size(450, 360));
        Assert.Equal(new Size(450, 350), st.Minimum(app));
    }

    [Fact]
    public void AC8_FreshStoreAndNonFinite()
    {
        var st = new InMemoryLimitStore();
        var app = new AppId("com.x");
        st.Observe(app, new Size(100, 100));
        Assert.Null(st.Minimum(app));
        Assert.Empty(st.Entries);
        st.Learn(app, new Size(double.NaN, 1));
        Assert.Null(st.Minimum(app));
        Assert.Empty(st.Entries);
        Assert.Equal(0, st.PersistCount);
    }

    [Fact]
    public void AC8_NullApp()
    {
        var st = new InMemoryLimitStore();
        st.Learn(null, new Size(1, 1));
        st.Observe(null, new Size(1, 1));
        Assert.Null(st.Minimum(null));
        Assert.Empty(st.Entries);
        Assert.Equal(0, st.PersistCount);
    }

    [Fact]
    public void AC8_EqualAppIdsShareEntry_AndPersistCount()
    {
        var st = new InMemoryLimitStore();
        st.Learn(new AppId("a"), new Size(10, 10));
        st.Learn(new AppId("a"), new Size(20, 5));
        Assert.Single(st.Entries);
        Assert.Equal(new Size(20, 10), st.Minimum(new AppId("a")));
        Assert.Equal(2, st.PersistCount);
        st.Observe(new AppId("a"), new Size(19.5, 10)); // within slack, unchanged
        Assert.Equal(2, st.PersistCount);
        st.Observe(new AppId("a"), new Size(1, 1)); // changes
        Assert.Equal(3, st.PersistCount);
    }

    private static SavedDisplay Rec(string id) => new(new LayoutDocument(PanelNode.ForWindow(id)));

    [Fact]
    public void AC9_DisplayStore()
    {
        var st = new InMemoryDisplayStore();
        var k = new EngineKey(new DisplayKey("ABC"), "7");
        Assert.Null(st.Load(k));
        var r1 = Rec("a");
        var r2 = Rec("b");
        st.Save(k, r1);
        Assert.Same(r1, st.Load(new EngineKey(new DisplayKey("ABC"), "7")));
        Assert.Null(st.Load(new EngineKey(new DisplayKey("ABC"), "8")));
        st.Save(k, r2);
        Assert.Same(r2, st.Load(k));
        Assert.Equal(2, st.SaveLog.Count);
        Assert.Same(r1, st.SaveLog[0].Display);
        Assert.Same(r2, st.SaveLog[1].Display);
        Assert.Equal(k, st.SaveLog[0].Key);
        Assert.Throws<ArgumentNullException>(() => st.Save(null!, r1));
        Assert.Throws<ArgumentNullException>(() => st.Save(k, null!));
        Assert.Throws<ArgumentNullException>(() => st.Load(null!));
        Assert.Equal(2, st.SaveLog.Count);
    }

    [Fact]
    public void AC10_ClockBasic()
    {
        var c = new ManualClock(1000);
        int n = 0;
        c.After(0.25, () => n++);
        c.Advance(0.2);
        Assert.Equal(0, n);
        Assert.Equal(1000.2, c.Now, 12);
        c.Advance(0.05);
        Assert.Equal(1, n);
        Assert.Equal(1000.25, c.Now, 12);
        c.Advance(10);
        Assert.Equal(1, n);
    }

    [Fact]
    public void AC11_ClockOrdering()
    {
        var c = new ManualClock();
        var log = new List<(string, double)>();
        c.After(0.3, () => log.Add(("a", c.Now)));
        c.After(0.1, () => log.Add(("b", c.Now)));
        c.After(0.1, () => log.Add(("c", c.Now)));
        c.After(0.2, () => log.Add(("d", c.Now)));
        c.Advance(1);
        Assert.Equal(new[] { "b", "c", "d", "a" }, log.Select(x => x.Item1));
        Assert.Equal(new[] { 0.1, 0.1, 0.2, 0.3 }, log.Select(x => x.Item2));
        Assert.Equal(1.0, c.Now);
    }

    [Fact]
    public void AC12_ClockReentrant()
    {
        var c = new ManualClock();
        var log = new List<(string, double)>();
        c.After(0.5, () =>
        {
            c.After(0.1, () => log.Add(("e", c.Now)));
            c.After(0.9, () => log.Add(("g", c.Now)));
        });
        c.Advance(1);
        Assert.Single(log);
        Assert.Equal("e", log[0].Item1);
        Assert.Equal(0.6, log[0].Item2, 12);
        Assert.Equal(1, c.PendingCount);
        c.Advance(1);
        Assert.Equal(2, log.Count);
    }

    [Fact]
    public void AC13_ClockEdges()
    {
        var c = new ManualClock();
        int g = 0, h = 0, i = 0, j = 0;
        c.After(0, () => g++);
        Assert.Equal(0, g);
        c.Advance(0);
        Assert.Equal(1, g);
        c.After(-1, () => h++);
        c.After(double.NaN, () => i++);
        Assert.Equal(0, h + i);
        c.Advance(0);
        Assert.Equal(1, h);
        Assert.Equal(1, i);
        c.After(double.PositiveInfinity, () => j++);
        c.Advance(1e9);
        Assert.Equal(0, j);
        Assert.Equal(1, c.PendingCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Advance(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Advance(double.NaN));
        Assert.Throws<ArgumentNullException>(() => c.After(1, null!));
    }

    [Fact]
    public void AC13_E3_ThrowingActionPropagates()
    {
        var c = new ManualClock();
        c.After(0.1, () => throw new InvalidOperationException("x"));
        Assert.Throws<InvalidOperationException>(() => c.Advance(1));
        Assert.Equal(0, c.PendingCount);
    }

    [Fact]
    public void AC25_RecordingRenderer()
    {
        var r = new RecordingRenderer();
        Assert.Null(r.Last);
        var p1 = new CanvasWm.Engine.Rendering.RenderPlan(new[] { new CanvasWm.Engine.Rendering.WindowPlan(new WindowKey("a"), new Rect(0, 0, 1, 1), true) });
        var p2 = CanvasWm.Engine.Rendering.RenderPlan.Empty;
        r.Render(p1);
        r.Render(p2);
        Assert.Equal(2, r.Plans.Count);
        Assert.Same(p1, r.Plans[0]);
        Assert.Same(p2, r.Plans[1]);
        Assert.Same(p2, r.Last);
        Assert.Throws<ArgumentNullException>(() => r.Render(null!));
        Assert.Equal(2, r.Plans.Count);
    }
}
