using CanvasWm.Core.Shortcuts;
using CanvasWm.Engine.Routing;

namespace CanvasWm.Engine.Tests.RoutingSpec;

public sealed class HotkeyTriggersTests
{
    private static KeyCombo Of(ShortcutBindings b, ShortcutAction a) => b[a]!.Value;

    private static IReadOnlyList<KeyCombo> Full(HotkeyTriggers h) => HotkeyPlan.Desired(h.Table, false);

    [Fact]
    [Trait("Assumption", "A13")]
    public void AC27_FullLifecycle()
    {
        var h = new HotkeyTriggers(ShortcutProfiles.WinAlt);
        var reqs = new List<HotkeyApplyRequest>();
        h.ApplyRequested += reqs.Add;

        // before Start: nothing raised
        Assert.False(h.IsStarted);
        h.SetBindings(ShortcutProfiles.WinAlt);
        h.SettingsOpened();
        h.Retry();
        Assert.Empty(reqs);
        Assert.Null(h.Pressed(Of(ShortcutProfiles.WinAlt, ShortcutAction.Right)));

        h.Start();
        var single = Assert.Single(reqs);
        Assert.Equal(HotkeyApplyReason.Start, single.Reason);
        Assert.Equal(Full(h), single.Desired);
        Assert.True(h.IsStarted);
        h.Start();
        Assert.Single(reqs);

        reqs.Clear();
        h.BeginRecording();
        Assert.True(h.Recording);
        var rec = Assert.Single(reqs);
        Assert.Equal(HotkeyApplyReason.RecordingStarted, rec.Reason);
        Assert.Empty(rec.Desired);
        Assert.Empty(h.Desired);
        h.BeginRecording();
        Assert.Single(reqs);
        Assert.Null(h.Pressed(Of(ShortcutProfiles.WinAlt, ShortcutAction.Right)));

        reqs.Clear();
        h.EndRecording();
        Assert.False(h.Recording);
        var stop = Assert.Single(reqs);
        Assert.Equal(HotkeyApplyReason.RecordingStopped, stop.Reason);
        Assert.Equal(Full(h), stop.Desired);
        Assert.NotEmpty(stop.Desired);
        h.EndRecording();
        Assert.Single(reqs);

        reqs.Clear();
        h.SetBindings(ShortcutProfiles.CtrlAlt);
        var bc = Assert.Single(reqs);
        Assert.Equal(HotkeyApplyReason.BindingsChanged, bc.Reason);
        Assert.Equal(HotkeyPlan.Desired(ShortcutDispatch.Build(ShortcutProfiles.CtrlAlt), false), bc.Desired);
        Assert.Equal(ShortcutDispatch.Build(ShortcutProfiles.CtrlAlt), h.Table);
        Assert.Same(ShortcutProfiles.CtrlAlt, h.Bindings);

        reqs.Clear();
        h.SettingsOpened();
        h.Retry();
        Assert.Equal(new[] { HotkeyApplyReason.SettingsOpened, HotkeyApplyReason.Retry }, reqs.Select(r => r.Reason).ToArray());
        Assert.All(reqs, r => Assert.Equal(h.Desired, r.Desired));

        Assert.Null(h.Pressed(Of(ShortcutProfiles.WinAlt, ShortcutAction.Right)));
        Assert.Equal(ShortcutCommand.ForAction(ShortcutAction.Right),
                     h.Pressed(Of(ShortcutProfiles.CtrlAlt, ShortcutAction.Right)));

        reqs.Clear();
        h.Stop();
        var s = Assert.Single(reqs);
        Assert.Equal(HotkeyApplyReason.Stop, s.Reason);
        Assert.Empty(s.Desired);
        Assert.True(h.IsStopped);
        Assert.Empty(h.Desired);
        reqs.Clear();
        h.Start();
        h.SetBindings(ShortcutProfiles.WinAlt);
        h.BeginRecording();
        h.EndRecording();
        h.SettingsOpened();
        h.Retry();
        h.Stop();
        Assert.Empty(reqs);
        Assert.Null(h.Pressed(Of(ShortcutProfiles.WinAlt, ShortcutAction.Right)));
    }

    [Fact]
    public void AC27_StateUpdatedBeforeStart()
    {
        var h = new HotkeyTriggers(ShortcutProfiles.WinAlt);
        var reqs = new List<HotkeyApplyRequest>();
        h.ApplyRequested += reqs.Add;
        h.SetBindings(ShortcutProfiles.CtrlAlt);
        h.BeginRecording();
        h.EndRecording();
        Assert.Empty(reqs);
        Assert.Same(ShortcutProfiles.CtrlAlt, h.Bindings);
        Assert.Equal(ShortcutDispatch.Build(ShortcutProfiles.CtrlAlt), h.Table);
        h.Start();
        Assert.Equal(HotkeyPlan.Desired(ShortcutDispatch.Build(ShortcutProfiles.CtrlAlt), false), Assert.Single(reqs).Desired);
    }

    [Fact]
    public void AC27_StopBeforeStartRaisesNothing()
    {
        var h = new HotkeyTriggers(ShortcutProfiles.WinAlt);
        var reqs = new List<HotkeyApplyRequest>();
        h.ApplyRequested += reqs.Add;
        h.Stop();
        Assert.Empty(reqs);
        Assert.True(h.IsStopped);
        Assert.Empty(h.Desired);
        h.Start();
        Assert.Empty(reqs);
        Assert.False(h.IsStarted);
    }

    [Fact]
    public void AC27_NullBindingsThrowAndKeepState()
    {
        Assert.Throws<ArgumentNullException>(() => new HotkeyTriggers(null!));
        var h = new HotkeyTriggers(ShortcutProfiles.WinAlt);
        Assert.Throws<ArgumentNullException>(() => h.SetBindings(null!));
        Assert.Same(ShortcutProfiles.WinAlt, h.Bindings);
    }

    [Fact]
    public void AC27_InitialState()
    {
        var h = new HotkeyTriggers(ShortcutProfiles.WinAlt);
        Assert.Same(ShortcutProfiles.WinAlt, h.Bindings);
        Assert.Equal(ShortcutDispatch.Build(ShortcutProfiles.WinAlt), h.Table);
        Assert.False(h.Recording);
        Assert.False(h.IsStarted);
        Assert.False(h.IsStopped);
        Assert.Equal(HotkeyPlan.Desired(h.Table, false), h.Desired);
    }
}
