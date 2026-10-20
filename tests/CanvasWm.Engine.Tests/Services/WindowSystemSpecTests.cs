using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.ServicesSpec;

public sealed class WindowSystemSpecTests
{
    private static readonly WindowKey W1 = new("w1");
    private static readonly WindowKey W2 = new("w2");
    private static readonly WindowKey W9 = new("w9");

    private static FakeWindowSystem Scene()
    {
        var ws = new FakeWindowSystem();
        ws.AddWindow(W1, new Rect(0, 0, 800, 600));
        return ws;
    }

    [Fact]
    public void AC14_SetFrameIsAsync()
    {
        var ws = Scene();
        var got = new List<Rect?>();
        ws.SetFrame(W1, new Rect(10, 10, 700, 500), got.Add);
        Assert.Empty(got);
        Assert.Equal(new Rect(0, 0, 800, 600), ws.FrameOf(W1));
        Assert.Single(ws.Writes);
        Assert.Equal(new FrameWriteRecord(W1, new Rect(10, 10, 700, 500), null), ws.Writes[0]);
        Assert.Equal(1, ws.PendingCount);
        Assert.Equal(1, ws.Pump());
        Assert.Equal(new Rect?[] { new Rect(10, 10, 700, 500) }, got);
        Assert.Equal(0, ws.Pump());
        Assert.Single(got);
    }

    [Fact]
    public void AC14_ReadUnknownAndEmptyBatchAreAsync()
    {
        var ws = Scene();
        var reads = new List<Rect?>();
        ws.ReadFrame(W1, reads.Add);
        Assert.Empty(reads);
        ws.Pump();
        Assert.Equal(new Rect?[] { new Rect(0, 0, 800, 600) }, reads);

        var unk = new List<Rect?>();
        ws.SetFrame(W9, new Rect(0, 0, 1, 1), unk.Add);
        Assert.Empty(unk);
        ws.Pump();
        Assert.Equal(new Rect?[] { null }, unk);

        var batches = new List<IReadOnlyList<FrameWriteResult>>();
        ws.SetFrames(Array.Empty<FrameWrite>(), batches.Add);
        Assert.Empty(batches);
        Assert.Equal(1, ws.PendingCount);
        ws.Pump();
        Assert.Single(batches);
        Assert.Empty(batches[0]);
    }

    [Fact]
    public void AC15_ClampMin()
    {
        var ws = Scene();
        ws.SetConstraints(W1, min: new Size(900, 0));
        Rect? r = null;
        ws.SetFrame(W1, new Rect(0, 0, 800, 600), x => r = x);
        ws.Pump();
        Assert.Equal(new Rect(0, 0, 900, 600), r);
        Assert.Equal(new Rect(0, 0, 900, 600), ws.FrameOf(W1));
    }

    [Fact]
    public void AC15_ClampMax()
    {
        var ws = Scene();
        ws.SetConstraints(W1, max: new Size(500, 400));
        Rect? r = null;
        ws.SetFrame(W1, new Rect(0, 0, 800, 600), x => r = x);
        ws.Pump();
        Assert.Equal(new Rect(0, 0, 500, 400), r);
    }

    [Fact]
    public void AC15_RefusesAndFails()
    {
        var ws = Scene();
        ws.SetRefusesWrites(W1, true);
        Rect? r = null;
        ws.SetFrame(W1, new Rect(5, 5, 100, 100), x => r = x);
        ws.Pump();
        Assert.Equal(new Rect(0, 0, 800, 600), r);
        Assert.Equal(new Rect(0, 0, 800, 600), ws.FrameOf(W1));

        ws.SetRefusesWrites(W1, false);
        ws.SetFails(W1, true);
        var res = new List<Rect?>();
        ws.SetFrame(W1, new Rect(5, 5, 100, 100), res.Add);
        ws.ReadFrame(W1, res.Add);
        ws.Pump();
        Assert.Equal(new Rect?[] { null, null }, res);
        Assert.Equal(new Rect(0, 0, 800, 600), ws.FrameOf(W1));
    }

    [Fact]
    public void AC16_RemovalRaceAndNaN()
    {
        var ws = Scene();
        var res = new List<Rect?>();
        ws.SetFrame(W1, new Rect(1, 1, 10, 10), res.Add);
        ws.RemoveWindow(W1);
        ws.Pump();
        Assert.Equal(new Rect?[] { null }, res);

        var ws2 = Scene();
        Rect? r = new Rect(9, 9, 9, 9);
        ws2.SetFrame(W1, new Rect(double.NaN, 0, 10, 10), x => r = x);
        ws2.Pump();
        Assert.Null(r);
        Assert.Equal(new Rect(0, 0, 800, 600), ws2.FrameOf(W1));
    }

    [Fact]
    public void AC17_PerWindowOrder()
    {
        var ws = Scene();
        var a = new Rect(1, 1, 100, 100);
        var b = new Rect(2, 2, 200, 200);
        var order = new List<string>();
        Rect? c3 = null;
        ws.SetFrame(W1, a, _ => order.Add("c1"));
        ws.SetFrame(W1, b, _ => order.Add("c2"));
        ws.ReadFrame(W1, x => { order.Add("c3"); c3 = x; });
        ws.Pump();
        Assert.Equal(new[] { "c1", "c2", "c3" }, order);
        Assert.Equal(b, c3);
    }

    [Fact]
    public void AC17_CrossWindowStep()
    {
        var ws = Scene();
        ws.AddWindow(W2, new Rect(0, 0, 10, 10));
        var order = new List<string>();
        ws.SetFrame(W1, new Rect(1, 1, 5, 5), _ => order.Add("w1"));
        ws.SetFrame(W2, new Rect(2, 2, 5, 5), _ => order.Add("w2"));
        Assert.True(ws.Step(W2));
        Assert.Equal(new[] { "w2" }, order);
        Assert.True(ws.Step(W1));
        Assert.Equal(new[] { "w2", "w1" }, order);
        Assert.False(ws.Step(W1));
        Assert.False(ws.Step());
    }

    [Fact]
    public void AC18_Batch()
    {
        var ws = Scene();
        ws.AddWindow(W2, new Rect(800, 0, 100, 100));
        ws.SetConstraints(W1, min: new Size(900, 0));
        IReadOnlyList<FrameWriteResult>? res = null;
        int calls = 0;
        var rect9 = new Rect(1, 2, 3, 4);
        ws.SetFrames(new[]
        {
            new FrameWrite(W1, new Rect(0, 0, 800, 600)),
            new FrameWrite(W9, rect9),
            new FrameWrite(W2, new Rect(800, 0, 640, 900)),
        }, r => { res = r; calls++; });
        Assert.Equal(3, ws.Writes.Count);
        var id = ws.Writes[0].BatchId;
        Assert.NotNull(id);
        Assert.All(ws.Writes, w => Assert.Equal(id, w.BatchId));
        Assert.Null(res);
        ws.Pump();
        Assert.Equal(1, calls);
        Assert.Equal(new[]
        {
            new FrameWriteResult(W1, new Rect(0, 0, 900, 600)),
            new FrameWriteResult(W9, null),
            new FrameWriteResult(W2, new Rect(800, 0, 640, 900)),
        }, res!.ToArray());

        // later write applied after the batch
        var x = new Rect(7, 7, 70, 70);
        ws.SetFrames(new[] { new FrameWrite(W2, new Rect(1, 1, 1, 1)) }, _ => { });
        ws.SetFrame(W2, x, _ => { });
        Rect? read = null;
        ws.ReadFrame(W2, r => read = r);
        ws.Pump();
        Assert.Equal(x, read);
        Assert.NotEqual(id, ws.Writes[3].BatchId);
    }

    [Fact]
    public void AC18_InvalidBatchesThrowAndRecordNothing()
    {
        var ws = Scene();
        var dup = new[] { new FrameWrite(W1, new Rect(0, 0, 1, 1)), new FrameWrite(W1, new Rect(0, 0, 2, 2)) };
        Assert.Throws<ArgumentException>(() => ws.SetFrames(dup, _ => { }));
        var nul = new[] { new FrameWrite(W1, new Rect(0, 0, 1, 1)), default };
        Assert.Throws<ArgumentException>(() => ws.SetFrames(nul, _ => { }));
        Assert.Empty(ws.Writes);
        Assert.Equal(0, ws.PendingCount);
    }

    [Fact]
    public void AC19_PumpReentrancy()
    {
        var ws = Scene();
        ws.SetFrame(W1, new Rect(1, 1, 1, 1), _ => ws.SetFrame(W1, new Rect(2, 2, 2, 2), _ => { }));
        Assert.Equal(2, ws.Pump());
        Assert.Equal(new Rect(2, 2, 2, 2), ws.FrameOf(W1));

        var ws2 = Scene();
        Action<Rect?>? loop = null;
        loop = _ => ws2.ReadFrame(W1, loop!);
        ws2.ReadFrame(W1, loop);
        Assert.Throws<InvalidOperationException>(() => ws2.Pump());
    }

    [Fact]
    public void AC20_ZOrderAndShowing()
    {
        var ws = new FakeWindowSystem();
        var a = new WindowKey("A"); var b = new WindowKey("B"); var c = new WindowKey("C"); var x = new WindowKey("X");
        ws.AddWindow(b, new Rect(0, 0, 1, 1));
        ws.AddWindow(a, new Rect(0, 0, 1, 1));
        Assert.Equal(new[] { a, b }, ws.FrontToBack());
        ws.SetZOrder(new[] { a, x, b });
        Assert.Equal(new[] { a, b }, ws.FrontToBack());
        ws.AddWindow(c, new Rect(0, 0, 1, 1));
        Assert.Equal(c, ws.FrontToBack()[0]);
        Assert.False(ws.IsShowing(x));
        Assert.True(ws.IsShowing(a));
        ws.SetShowing(a, false);
        Assert.False(ws.IsShowing(a));
        Assert.Throws<ArgumentException>(() => ws.AddWindow(a, new Rect(0, 0, 1, 1)));
        var snap = ws.FrontToBack();
        ws.AddWindow(new WindowKey("D"), new Rect(0, 0, 1, 1));
        Assert.Equal(3, snap.Count);
    }

    [Fact]
    public void AC20_SetZOrderAppendsOmittedManaged()
    {
        var ws = new FakeWindowSystem();
        var a = new WindowKey("A"); var b = new WindowKey("B"); var c = new WindowKey("C");
        ws.AddWindow(c, new Rect(0, 0, 1, 1));
        ws.AddWindow(b, new Rect(0, 0, 1, 1));
        ws.AddWindow(a, new Rect(0, 0, 1, 1)); // a, b, c
        ws.SetZOrder(new[] { c });
        Assert.Equal(new[] { c, a, b }, ws.FrontToBack());
    }

    [Fact]
    public void AC21_Geometry()
    {
        var ws = Scene();
        ws.Screens = new[] { new Rect(0, 0, 1440, 900), new Rect(1440, 0, 1920, 1080) };
        Assert.Equal(1.0, ws.VisibleFraction(new Rect(1340, 0, 200, 100)), 12);
        Assert.Equal(0.5, ws.VisibleFraction(new Rect(-100, 0, 200, 100)), 12);
        Assert.Equal(new Point(3359, 1079), ws.ParkingOrigin);
        ws.Screens = Array.Empty<Rect>();
        Assert.Equal(new Point(10000, 10000), ws.ParkingOrigin);
        ws.ParkingOriginOverride = new Point(5, 5);
        Assert.Equal(new Point(5, 5), ws.ParkingOrigin);
        ws.Pointer = new Point(3, 4);
        ws.MouseDown = true;
        Assert.Equal(new Point(3, 4), ws.Pointer);
        Assert.True(ws.MouseDown);
        Assert.Null(ws.TabSibling(W1));
    }

    [Fact]
    public void E4_EmptyRectVisibleFractionIsZero()
    {
        Assert.Equal(0.0, Scene().VisibleFraction(new Rect(10, 10, 0, 0)), 12);
    }

    [Fact]
    public void AC22_Focus()
    {
        var ws = Scene();
        ws.AddWindow(W2, new Rect(0, 0, 1, 1));
        var before = ws.FrontToBack().ToArray();
        ws.Focus(W1);
        ws.Focus(W9);
        Assert.Equal(new[] { W1, W9 }, ws.FocusRequests);
        Assert.Equal(before, ws.FrontToBack());
        Assert.Equal(0, ws.PendingCount);
    }

    [Fact]
    public void AC23_NullChecks()
    {
        var ws = Scene();
        Assert.Throws<ArgumentNullException>(() => ws.SetFrame(W1, new Rect(0, 0, 1, 1), null!));
        Assert.Throws<ArgumentNullException>(() => ws.ReadFrame(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => ws.ReadFrame(W1, null!));
        Assert.Throws<ArgumentNullException>(() => ws.SetFrame(null!, new Rect(0, 0, 1, 1), _ => { }));
        Assert.Throws<ArgumentNullException>(() => ws.SetFrames(null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => ws.SetFrames(Array.Empty<FrameWrite>(), null!));
        Assert.Throws<ArgumentNullException>(() => ws.Focus(null!));
        Assert.Empty(ws.Writes);
        Assert.Empty(ws.Reads);
        Assert.Empty(ws.FocusRequests);
        Assert.Equal(0, ws.PendingCount);
    }

    [Fact]
    public void B23_ReadsRecorded_AndE3_ThrowingCompletionPropagates()
    {
        var ws = Scene();
        ws.ReadFrame(W1, _ => throw new InvalidOperationException("boom"));
        Assert.Equal(new[] { W1 }, ws.Reads);
        Assert.Throws<InvalidOperationException>(() => ws.Pump());
        Assert.Equal(0, ws.PendingCount);
    }

    [Fact]
    public void AC24_Capture()
    {
        var cap = new FakeCapture();
        cap.Start(W1);
        Assert.Null(cap.Latest(W1));
        cap.SetRate(W1, 10);
        Assert.Equal(0.1, cap.Interval(W1)!.Value, 12);
        cap.SetRate(W1, 0); cap.SetRate(W1, -1); cap.SetRate(W1, double.NaN);
        Assert.Equal(0.1, cap.Interval(W1)!.Value, 12);
        cap.SetRate(W2, 5);
        Assert.Null(cap.Interval(W2));
        Assert.Equal(1, cap.Deliver(W1, new Size(300, 200))!.Value.Serial);
        cap.Start(W2);
        Assert.Equal(2, cap.Deliver(W2, new Size(1, 1))!.Value.Serial);
        Assert.Equal(3, cap.Deliver(W1, new Size(300, 200))!.Value.Serial);
        Assert.Equal(3, cap.Latest(W1)!.Value.Serial);

        int before = cap.RecaptureRequests(W1);
        cap.Hold(W1, true);
        cap.Hold(W1, true);
        Assert.True(cap.IsHeld(W1));
        Assert.Equal(before + 1, cap.RecaptureRequests(W1));

        cap.Stop(W1);
        Assert.Null(cap.Latest(W1));
        Assert.Null(cap.Interval(W1));
        Assert.False(cap.IsHeld(W1));
        Assert.False(cap.IsStarted(W1));
        Assert.Null(cap.Deliver(W1, new Size(1, 1)));
        Assert.Null(cap.Latest(W1));
    }

    [Fact]
    public void AC24_StartIdempotentAndHoldUnstarted()
    {
        var cap = new FakeCapture();
        cap.Hold(W1, true);
        Assert.False(cap.IsHeld(W1));
        cap.Start(W1);
        Assert.Equal(1, cap.RecaptureRequests(W1));
        cap.Start(W1);
        Assert.Equal(1, cap.RecaptureRequests(W1));
        cap.SetRate(W1, 4);
        Assert.Equal(2, cap.RecaptureRequests(W1));
        cap.SetRate(W1, 4);
        Assert.Equal(2, cap.RecaptureRequests(W1));
        cap.Stop(W9); // unknown: no throw
    }

    [Fact]
    public void AC26_FakesAssignable()
    {
        IWindowSystem a = new FakeWindowSystem();
        ICapture b = new FakeCapture();
        IClock c = new ManualClock();
        IRenderer d = new RecordingRenderer();
        ILimitStore e = new InMemoryLimitStore();
        IDisplayStore f = new InMemoryDisplayStore();
        Assert.NotNull(a); Assert.NotNull(b); Assert.NotNull(c); Assert.NotNull(d); Assert.NotNull(e); Assert.NotNull(f);
    }
}
