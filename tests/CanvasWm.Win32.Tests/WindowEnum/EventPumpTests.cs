using CanvasWm.Engine.Services;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.WindowEnumSpec;

/// <summary>AC14 of specs/win32-window-enum.md: EventPump with a test post that records actions.</summary>
public sealed class EventPumpTests
{
    private sealed class BoomException : Exception;

    private static RawWindowEvent Ev(ulong h) => new(RawWindowEventKind.Created, h);

    [Fact]
    public void AC14_TwoEnqueues_OnePost_DeliveredInOrderToHandlersInAddOrder()
    {
        var p = new List<Action>();
        var pump = new EventPump(p.Add);
        var log = new List<string>();
        pump.Add(e => log.Add($"h1:{e.Handle}"));
        pump.Add(e => log.Add($"h2:{e.Handle}"));
        pump.Enqueue(Ev(1));
        pump.Enqueue(Ev(2));
        Assert.Single(p);
        Assert.Empty(log);
        Assert.Equal(2, pump.QueuedCount);
        Assert.Equal(2, pump.HandlerCount);

        p[0]();
        Assert.Equal(["h1:1", "h2:1", "h1:2", "h2:2"], log);
        Assert.Equal(0, pump.QueuedCount);
    }

    [Fact]
    public void AC14_EnqueueDuringDrain_DeliveredInSameDrain_PostDoesNotGrow()
    {
        var p = new List<Action>();
        var pump = new EventPump(p.Add);
        var seen = new List<ulong>();
        pump.Add(e =>
        {
            seen.Add(e.Handle);
            if (e.Handle == 1)
            {
                pump.Enqueue(Ev(3));
            }
        });
        pump.Enqueue(Ev(1));
        pump.Enqueue(Ev(2));
        p[0]();
        Assert.Equal([1UL, 2UL, 3UL], seen);
        Assert.Single(p);
    }

    [Fact]
    public void AC14_DisposedHandlerGetsNothing_EvenForQueuedEvents_DoubleDisposeOk()
    {
        var p = new List<Action>();
        var pump = new EventPump(p.Add);
        var got1 = new List<ulong>();
        var got2 = new List<ulong>();
        pump.Add(e => got1.Add(e.Handle));
        IDisposable reg2 = pump.Add(e => got2.Add(e.Handle));
        pump.Enqueue(Ev(1));
        pump.Enqueue(Ev(2));
        reg2.Dispose();
        reg2.Dispose();
        Assert.Equal(1, pump.HandlerCount);
        p[0]();
        Assert.Equal(2, got1.Count);
        Assert.Empty(got2);
    }

    [Fact]
    public void AC14_NestedDrain_DeliversNothingAndReturns()
    {
        var p = new List<Action>();
        var pump = new EventPump(p.Add);
        var seen = new List<ulong>();
        int nestedCalls = 0;
        pump.Add(e =>
        {
            seen.Add(e.Handle);
            if (e.Handle == 1)
            {
                int before = seen.Count;
                p[0]();
                nestedCalls++;
                Assert.Equal(before, seen.Count);
            }
        });
        pump.Enqueue(Ev(1));
        pump.Enqueue(Ev(2));
        p[0]();
        Assert.Equal(1, nestedCalls);
        Assert.Equal([1UL, 2UL], seen);
    }

    [Fact]
    public void AC14_InlinePost_EnqueueThrowsInvalidOperation_EventStaysQueued()
    {
        var pump = new EventPump(a => a());
        pump.Add(_ => { });
        Assert.Throws<InvalidOperationException>(() => pump.Enqueue(Ev(1)));
        Assert.Equal(1, pump.QueuedCount);
    }

    [Fact]
    public void AC14_HandlerThrows_PropagatesOnce_NewDrainPosted_RemainingDelivered()
    {
        var p = new List<Action>();
        var pump = new EventPump(p.Add);
        var seen = new List<ulong>();
        bool thrown = false;
        pump.Add(e =>
        {
            seen.Add(e.Handle);
            if (e.Handle == 1 && !thrown)
            {
                thrown = true;
                throw new BoomException();
            }
        });
        pump.Enqueue(Ev(1));
        pump.Enqueue(Ev(2));
        pump.Enqueue(Ev(3));
        Assert.Throws<BoomException>(() => p[0]());
        Assert.Equal(2, p.Count);
        Assert.Equal(2, pump.QueuedCount);
        p[1]();
        Assert.Equal([1UL, 2UL, 3UL], seen);
        Assert.Equal(0, pump.QueuedCount);
        Assert.Equal(2, p.Count);
    }
}
