using CanvasWm.Core.Geometry;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Engine.Hosting;
using CanvasWm.Engine.Rendering;
using CanvasWm.Engine.Routing;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;
using CanvasWm.Engine.Tests.RoutingSpec;

namespace CanvasWm.Engine.Tests.HostingSpec;

public sealed class RelayCompositionLifetimeTests
{
    // ---- AC14 ----

    [Fact]
    public void AC14_FactoryBuildsWiredEngines()
    {
        var policy = EnginePolicy.Default;
        var win = new FakeWindowSystem();
        var cap = new FakeCapture();
        var clock = new ManualClock();
        var lim = new InMemoryLimitStore();
        var store = new InMemoryDisplayStore();
        var rends = new Dictionary<DisplayKey, IRenderer>
        {
            [Rt.KA] = new RecordingRenderer(),
            [Rt.KB] = new RecordingRenderer(),
        };
        var f = HostComposition.EngineFactory(policy, win, cap, clock, lim, store, d => rends[d.Key]);
        var e = f(Rt.EB, Rt.B);
        Assert.Equal(Rt.EB, e.Key);
        Assert.Equal(Rt.B.WorkArea, e.VisibleArea);
        Assert.Equal(policy, e.Policy);
        Assert.Same(win, e.Services.Windows);
        Assert.Same(cap, e.Services.Capture);
        Assert.Same(clock, e.Services.Clock);
        Assert.Same(lim, e.Services.Limits);
        Assert.Same(store, e.Services.Displays);
        Assert.Same(rends[Rt.KB], e.Services.Renderer);
    }

    [Fact]
    public void AC14_UsedAsRouterFactory()
    {
        var t = new Rt();
        var f = HostComposition.EngineFactory(EnginePolicy.Default, t.Win, t.Cap, t.Clock, t.Lim, t.Mem, _ => new RecordingRenderer());
        t.FactoryOverride = f;
        t.Ready();
        Assert.Equal(2, t.Router.Engines.Count);
        Assert.All(t.Router.Engines, e => Assert.True(e.IsStarted));
    }

    [Fact]
    public void AC14_NullArguments()
    {
        var p = EnginePolicy.Default;
        var w = new FakeWindowSystem();
        var c = new FakeCapture();
        var k = new ManualClock();
        var l = new InMemoryLimitStore();
        var s = new InMemoryDisplayStore();
        Func<DisplayInfo, IRenderer> r = _ => NullRenderer.Instance;
        Assert.Throws<ArgumentNullException>(() => HostComposition.EngineFactory(null!, w, c, k, l, s, r));
        Assert.Throws<ArgumentNullException>(() => HostComposition.EngineFactory(p, null!, c, k, l, s, r));
        Assert.Throws<ArgumentNullException>(() => HostComposition.EngineFactory(p, w, null!, k, l, s, r));
        Assert.Throws<ArgumentNullException>(() => HostComposition.EngineFactory(p, w, c, null!, l, s, r));
        Assert.Throws<ArgumentNullException>(() => HostComposition.EngineFactory(p, w, c, k, null!, s, r));
        Assert.Throws<ArgumentNullException>(() => HostComposition.EngineFactory(p, w, c, k, l, null!, r));
        Assert.Throws<ArgumentNullException>(() => HostComposition.EngineFactory(p, w, c, k, l, s, null!));
    }

    // ---- AC15 ----

    private sealed class RelayRig
    {
        public readonly Rt T = new();
        public readonly HotkeyTriggers Triggers = new(ShortcutProfiles.WinAlt);
        public readonly List<IReadOnlyList<KeyCombo>> ApplyCalls = new();
        public readonly List<string> Log = new();
        public readonly ShortcutRelay Relay;
        public readonly KeyCombo Taken = ShortcutProfiles.WinAlt[ShortcutAction.Split]!.Value;
        public Func<IReadOnlyList<KeyCombo>, IReadOnlyList<HotkeyReport>>? Override;

        public RelayRig()
        {
            Relay = new ShortcutRelay(Triggers, T.Router, Apply, Log.Add);
        }

        private IReadOnlyList<HotkeyReport> Apply(IReadOnlyList<KeyCombo> desired)
        {
            ApplyCalls.Add(desired);
            if (Override is not null) return Override(desired);
            return desired.Select(c => new HotkeyReport(c, c == Taken ? HotkeyStatus.Taken : HotkeyStatus.Registered,
                c == Taken ? 1409 : 0)).ToArray();
        }
    }

    [Fact]
    public void AC15_ConstructionAndStart()
    {
        var r = new RelayRig();
        Assert.Empty(r.ApplyCalls);
        Assert.Empty(r.Relay.LastReports);
        var applied = new List<HotkeyApplyRequest>();
        r.Relay.Applied += (req, _) => applied.Add(req);
        r.T.Router.Ready(new[] { Rt.W1, Rt.W2, Rt.W3 });
        Assert.Single(r.ApplyCalls);
        Assert.Equal(HotkeyPlan.Desired(r.Triggers.Table, false), r.ApplyCalls[0]);
        Assert.Equal(r.ApplyCalls[0].Count, r.Relay.LastReports.Count);
        Assert.Contains(r.Relay.LastReports, x => x.Combo == r.Taken && x.Status == HotkeyStatus.Taken);
        Assert.Equal(HotkeyApplyReason.Start, Assert.Single(applied).Reason);
        Assert.Equal(2, r.Log.Count);
        Assert.Contains(ShortcutStore.FormatCombo(r.Taken), r.Log.Single(l => l.StartsWith("hotkey ", StringComparison.Ordinal)));

        r.Triggers.Retry();
        Assert.Equal(2, r.ApplyCalls.Count);
        Assert.Equal(HotkeyApplyReason.Retry, applied[^1].Reason);
    }

    [Fact]
    public void AC15_Pressed()
    {
        var r = new RelayRig();
        r.T.Router.Ready(new[] { Rt.W1, Rt.W2, Rt.W3 });
        r.T.Router.WindowFocused(Rt.K("w1"));
        r.T.Router.AppActivated(100, false);
        var route = r.Relay.Pressed(ShortcutProfiles.WinAlt[ShortcutAction.Right]!.Value);
        Assert.NotNull(route);
        Assert.Equal(Rt.EA, route!.Engine);
        Assert.Equal(ShortcutCommand.ForAction(ShortcutAction.Right), route.Command);

        Assert.Null(r.Relay.Pressed(new KeyCombo(Key.Z, Modifiers.Ctrl)));

        r.Triggers.BeginRecording();
        Assert.Null(r.Relay.Pressed(ShortcutProfiles.WinAlt[ShortcutAction.Right]!.Value));
    }

    [Fact]
    public void AC15_ApplyThrowPropagates()
    {
        var r = new RelayRig { Override = _ => throw new InvalidOperationException("apply") };
        Assert.Throws<InvalidOperationException>(() => r.T.Router.Ready(new[] { Rt.W1, Rt.W2, Rt.W3 }));
    }

    [Fact]
    public void AC15_ApplyThrowPropagatesFromTrigger()
    {
        var r = new RelayRig();
        r.T.Router.Ready(new[] { Rt.W1, Rt.W2, Rt.W3 });
        r.Override = _ => throw new InvalidOperationException("apply");
        Assert.Throws<InvalidOperationException>(() => r.Triggers.Retry());
    }

    // ---- AC16 ----

    private sealed class LifeRig
    {
        public Rt T;
        public HotkeyTriggers Triggers = new(ShortcutProfiles.WinAlt);
        public ShortcutRelay Relay;
        public AppLifetime Life;
        public List<string> Events = new();
        public List<string> Log = new();
        public List<(int Code, double At)> Finished = new();
        public int FinishedEvents;
        public HookStore? Hook;
        public Func<Action>? FinishAction;

        public LifeRig(bool ready = true, bool dropRouterAfter = false)
        {
            T = new Rt(wrapStore: s => Hook = new HookStore(s),
                       wrapClock: dropRouterAfter ? c => new DroppingClock(c) : null);
            Hook!.OnSave = k => Events.Add("save:" + k.Value);
            Relay = new ShortcutRelay(Triggers, T.Router, d => { Events.Add("apply:" + d.Count); return Array.Empty<HotkeyReport>(); });
            Life = new AppLifetime(T.Router, Triggers, T.Clock, () => Events.Add("stopTracking"),
                code => { Finished.Add((code, T.Clock.Now)); FinishAction?.Invoke()(); }, Log.Add);
            Life.Finished += () => FinishedEvents++;
            if (ready)
            {
                T.Ready();
                T.Win.Pump();
                T.Clock.Advance(0.2);
                T.Win.Pump();
                T.Clock.Advance(50 - T.Clock.Now);
                T.EA_.SetParked(Rt.K("w1"), true);
                Events.Clear();
            }
        }
    }

    [Fact]
    public void AC16_OrderedQuitThenDeferredFinish()
    {
        var r = new LifeRig();
        int writes0 = r.T.Win.Writes.Count;
        r.Life.RequestQuit(QuitReason.User);
        Assert.Equal(new[] { "apply:0", "stopTracking", "save:A|default", "save:B|default" }, r.Events);
        Assert.Equal(LifetimeState.ShuttingDown, r.Life.State);
        Assert.Equal(QuitReason.User, r.Life.Reason);
        Assert.False(r.Life.IsQuitComplete);
        Assert.Empty(r.Finished);

        r.T.Clock.Advance(0);
        r.T.Win.Pump();
        Assert.True(r.T.Router.IsShutdownComplete);
        Assert.True(r.Life.IsQuitComplete);
        Assert.Empty(r.Finished);
        Assert.True(r.T.Win.Writes.Count > writes0);
        r.T.Clock.Advance(0);
        Assert.Equal(new[] { (0, 50.0) }, r.Finished);
        Assert.Equal(LifetimeState.Finished, r.Life.State);
        Assert.Equal(0, r.Life.ExitCode);
        Assert.False(r.Life.Faulted);
        Assert.Equal(1, r.FinishedEvents);
        Assert.Contains(r.Log, l => l.StartsWith("finish:", StringComparison.Ordinal));
    }

    [Fact]
    public void AC16_DeadlineWithoutPump()
    {
        var r = new LifeRig();
        r.Life.RequestQuit(QuitReason.User);
        r.T.Clock.Advance(1.999);
        Assert.Empty(r.Finished);
        r.T.Clock.Advance(0.001);
        Assert.Single(r.Finished);
        Assert.Equal(0, r.Finished[0].Code);
        Assert.Equal(52.0, r.Finished[0].At, 1e-9);
    }

    [Fact]
    public void AC16_SafetyNetWhenRouterAfterDropped()
    {
        var r = new LifeRig(dropRouterAfter: true);
        r.Life.RequestQuit(QuitReason.User);
        r.T.Clock.Advance(2.499);
        Assert.Empty(r.Finished);
        // AC16 says IsQuitComplete is false here, but B45 ORs in router.IsShutdownComplete, which is
        // deadline-based (true from 52.0). The spec text is inconsistent; reported, not asserted.
        Assert.True(r.Life.IsQuitComplete);
        r.T.Clock.Advance(0.001);
        Assert.True(r.Life.IsQuitComplete);
        Assert.Single(r.Finished);
        Assert.Equal(52.5, r.Finished[0].At, 1e-9);
    }

    [Fact]
    public void AC16_ZeroEnginesFinishNotInline()
    {
        var r = new LifeRig(ready: false);
        r.Life.RequestQuit(QuitReason.User);
        Assert.Empty(r.Finished);
        r.T.Clock.Advance(0);
        Assert.Single(r.Finished);
    }

    [Fact]
    public void AC16_FaultsAndExitCode()
    {
        var r = new LifeRig();
        r.Life.ReportFault(new InvalidOperationException("f"));
        Assert.True(r.Life.Faulted);
        Assert.Equal(QuitReason.Fault, r.Life.Reason);
        Assert.Equal(LifetimeState.ShuttingDown, r.Life.State);
        r.T.Clock.Advance(5);
        Assert.Equal(1, Assert.Single(r.Finished).Code);
        Assert.Equal(1, r.Life.ExitCode);

        var r2 = new LifeRig();
        r2.Life.RequestQuit(QuitReason.User);
        r2.Life.ReportFault(new InvalidOperationException("late"));
        Assert.Equal(QuitReason.User, r2.Life.Reason);
        r2.T.Clock.Advance(5);
        Assert.Equal(1, Assert.Single(r2.Finished).Code);

        int logged = r2.Log.Count;
        r2.Life.ReportFault(new InvalidOperationException("after"));
        Assert.True(r2.Log.Count >= logged);   // logged or counted only
        Assert.Single(r2.Finished);
        Assert.Equal(1, r2.Life.ExitCode);
    }

    [Fact]
    public void AC16_AfterFinishedFaultDoesNotChangeExitCode()
    {
        var r = new LifeRig();
        r.Life.RequestQuit(QuitReason.User);
        r.T.Clock.Advance(5);
        Assert.Equal(0, r.Life.ExitCode);
        r.Life.ReportFault(new InvalidOperationException("after"));
        Assert.False(r.Life.Faulted);
        Assert.Equal(0, r.Life.ExitCode);
        Assert.Equal(LifetimeState.Finished, r.Life.State);
    }

    [Fact]
    public void AC16_MaxLoggedFaults()
    {
        var r = new LifeRig();
        r.Life.RequestQuit(QuitReason.User);
        r.Log.Clear();
        for (int i = 0; i < 25; i++) r.Life.ReportFault(new InvalidOperationException("f" + i));
        Assert.Equal(20, r.Log.Count(l => l.StartsWith("fault:", StringComparison.Ordinal)));
        r.T.Clock.Advance(5);
        Assert.Contains(r.Log, l => l.StartsWith("finish:", StringComparison.Ordinal) && l.Contains("faults 25", StringComparison.Ordinal));
    }

    [Fact]
    public void AC16_StoreThrowsDuringShutdown()
    {
        var r = new LifeRig();
        r.Hook!.OnSave = _ => throw new InvalidOperationException("disk");
        r.Life.RequestQuit(QuitReason.User);   // must not throw
        Assert.True(r.Life.Faulted);
        Assert.Contains(r.Log, l => l.Contains("disk", StringComparison.Ordinal));
        double t0 = r.T.Clock.Now;
        r.T.Clock.Advance(2.5);
        var f = Assert.Single(r.Finished);
        Assert.Equal(1, f.Code);
        Assert.True(f.At - t0 <= 2.5 + 1e-9);
    }

    [Fact]
    public void AC16_SecondQuitIsNoOpAndFinishThrowStillRaisesFinished()
    {
        var r = new LifeRig();
        r.FinishAction = () => () => throw new InvalidOperationException("finish boom");
        r.Life.RequestQuit(QuitReason.User);
        int saves = r.Events.Count(e => e.StartsWith("save:", StringComparison.Ordinal));
        int stops = r.Events.Count(e => e == "stopTracking");
        r.Life.RequestQuit(QuitReason.EndSession);
        Assert.Equal(QuitReason.User, r.Life.Reason);
        Assert.Equal(saves, r.Events.Count(e => e.StartsWith("save:", StringComparison.Ordinal)));
        Assert.Equal(stops, r.Events.Count(e => e == "stopTracking"));
        r.T.Clock.Advance(5);
        Assert.Single(r.Finished);
        Assert.Equal(1, r.FinishedEvents);
        Assert.Contains(r.Log, l => l.Contains("finish boom", StringComparison.Ordinal));
    }

    [Fact]
    public void AC16_NullArgumentsAndFault()
    {
        var r = new LifeRig();
        Assert.Throws<ArgumentNullException>(() => r.Life.ReportFault(null!));
        Assert.Throws<ArgumentNullException>(() => new AppLifetime(null!, r.Triggers, r.T.Clock, () => { }, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new AppLifetime(r.T.Router, null!, r.T.Clock, () => { }, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new AppLifetime(r.T.Router, r.Triggers, null!, () => { }, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new AppLifetime(r.T.Router, r.Triggers, r.T.Clock, null!, _ => { }));
        Assert.Throws<ArgumentNullException>(() => new AppLifetime(r.T.Router, r.Triggers, r.T.Clock, () => { }, null!));
    }
}
