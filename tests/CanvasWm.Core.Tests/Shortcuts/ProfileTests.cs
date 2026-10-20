using CanvasWm.Core.Shortcuts;
using static CanvasWm.Core.Tests.ShortcutsSpec.T;
using SA = CanvasWm.Core.Shortcuts.ShortcutAction;

namespace CanvasWm.Core.Tests.ShortcutsSpec;

public sealed class ProfileTests
{
    private static IEnumerable<KeyCombo> ViewCombos(ShortcutBindings p)
    {
        for (int n = 1; n <= 9; n++)
        {
            yield return K(D(n), p.RecallViewModifiers!.Value);
            yield return K(D(n), p.SaveViewModifiers!.Value);
        }
    }

    [Fact]
    public void AC7_WinAlt_table()
    {
        var p = WinAlt;
        Assert.Equal(K(Key.O, WA), p[SA.Overview]);
        Assert.Equal(K(Key.Minus, WA), p[SA.StepOut]);
        Assert.Equal(K(Key.Equals, WA), p[SA.StepIn]);
        Assert.Equal(K(Key.Left, WA), p[SA.Left]);
        Assert.Equal(K(Key.Right, WA), p[SA.Right]);
        Assert.Equal(K(Key.Up, WAS), p[SA.Up]);
        Assert.Equal(K(Key.Down, WAS), p[SA.Down]);
        Assert.Equal(K(Key.LeftBracket, WA), p[SA.Back]);
        Assert.Equal(K(Key.RightBracket, WA), p[SA.Forward]);
        Assert.Equal(K(Key.F, WA), p[SA.Maximize]);
        Assert.Equal(K(Key.W, WA), p[SA.Desktop]);
        Assert.Equal(K(Key.N, WA), p[SA.Split]);
        Assert.Equal(WAS, p.RecallViewModifiers);
        Assert.Equal(WCA, p.SaveViewModifiers);
    }

    [Fact]
    public void AC8_CtrlAlt_table()
    {
        var p = CtrlAlt;
        Assert.Equal(K(Key.Space, CA), p[SA.Overview]);
        Assert.Equal(K(Key.Up, CA), p[SA.StepOut]);
        Assert.Equal(K(Key.Down, CA), p[SA.StepIn]);
        Assert.Equal(K(Key.Left, CA), p[SA.Left]);
        Assert.Equal(K(Key.Right, CA), p[SA.Right]);
        Assert.Equal(K(Key.Up, CAS), p[SA.Up]);
        Assert.Equal(K(Key.Down, CAS), p[SA.Down]);
        Assert.Equal(K(Key.LeftBracket, CA), p[SA.Back]);
        Assert.Equal(K(Key.RightBracket, CA), p[SA.Forward]);
        Assert.Equal(K(Key.Enter, CA), p[SA.Maximize]);
        Assert.Equal(K(Key.D, CA), p[SA.Desktop]);
        Assert.Equal(K(Key.N, CA), p[SA.Split]);
        Assert.Equal(CA, p.RecallViewModifiers);
        Assert.Equal(CAS, p.SaveViewModifiers);
    }

    [Theory]
    [InlineData(ShortcutProfile.WinAlt)]
    [InlineData(ShortcutProfile.CtrlAlt)]
    public void AC9_Profile_invariants(ShortcutProfile profile)
    {
        var p = ShortcutProfiles.Get(profile);
        var combos = AllActions.Select(a => p[a]!.Value).ToList();
        Assert.Equal(12, combos.Distinct().Count());
        var views = ViewCombos(p).ToList();
        Assert.Equal(18, views.Count);
        foreach (var c in combos) Assert.DoesNotContain(c, views);
        Assert.NotEqual(p.RecallViewModifiers, p.SaveViewModifiers);
        foreach (var c in combos.Concat(views)) Assert.True(ShortcutRules.IsBindable(c));
        foreach (var a in AllActions)
        {
            var r = ShortcutEditor.SetAction(p, a, p[a]!.Value);
            Assert.Equal(EditOutcome.Stored, r.Outcome);
            Assert.Equal(p, r.Bindings);
        }
        foreach (var s in Enum.GetValues<ViewSet>())
            Assert.Equal(EditOutcome.Stored, ShortcutEditor.SetViewModifiers(p, s, p.ViewModifiers(s)!.Value).Outcome);
        Assert.True(ShortcutProfiles.AreDefaults(p, profile));
        Assert.False(ShortcutProfiles.AreDefaults(p.WithAction(SA.Split, null), profile));
    }

    [Fact]
    public void AC9_Default_is_WinAlt()
    {
        Assert.Equal(WinAlt, ShortcutProfiles.Default);
        Assert.Equal(WinAlt, ShortcutProfiles.Get(ShortcutProfile.WinAlt));
        Assert.Equal(CtrlAlt, ShortcutProfiles.Get(ShortcutProfile.CtrlAlt));
        Assert.True(ShortcutProfiles.AreDefaults(WinAlt, ShortcutProfile.WinAlt));
        Assert.False(ShortcutProfiles.AreDefaults(WinAlt, ShortcutProfile.CtrlAlt));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutProfiles.Get((ShortcutProfile)9));
        Assert.Throws<ArgumentNullException>(() => ShortcutProfiles.AreDefaults(null!, ShortcutProfile.WinAlt));
    }

    [Fact]
    public void AC10_Reserved_list()
    {
        Assert.Equal(84, ReservedCombos.All.Count);
        Assert.Equal(84, ReservedCombos.All.Select(r => r.Combo).Distinct().Count());
        foreach (var c in new[] { K(Key.L, Modifiers.Win), K(Key.Delete, CA), K(Key.R, WA), K(Key.G, WA), K(Key.B, WA),
                     K(Key.D, WA), K(Key.K, WA), K(Key.Up, WA), K(Key.Down, WA), K(Key.D5, WA) })
            Assert.Equal(ReservedKind.Blocked, ReservedCombos.Find(c)!.Kind);
        Assert.Equal(ReservedKind.Conditional, ReservedCombos.Find(K(Key.H, WA))!.Kind);
        Assert.Equal(ReservedKind.Conditional, ReservedCombos.Find(K(Key.Space, CA))!.Kind);
        Assert.Null(ReservedCombos.Find(K(Key.O, WA)));
        Assert.Null(ReservedCombos.Find(K(Key.D5, WAS)));
        Assert.Null(ReservedCombos.Find(K(Key.D5, WCA)));
    }

    [Fact]
    public void AC10_Reserved_spot_checks_against_table()
    {
        Assert.Equal(ReservedKind.Conditional, ReservedCombos.Find(K(Key.Enter, WA))!.Kind);
        foreach (var k in new[] { Key.D, Key.F, Key.I, Key.L, Key.M, Key.R })
            Assert.Equal(ReservedKind.Conditional, ReservedCombos.Find(K(k, CA))!.Kind);
        foreach (var k in new[] { Key.Left, Key.Right, Key.Up, Key.Down })
        {
            Assert.Equal(ReservedKind.Conditional, ReservedCombos.Find(K(k, CA))!.Kind);
            Assert.Equal(ReservedKind.Blocked, ReservedCombos.Find(K(k, Modifiers.Win))!.Kind);
            Assert.Equal(ReservedKind.Blocked, ReservedCombos.Find(K(k, Modifiers.Win | Modifiers.Shift))!.Kind);
        }
        for (int i = 0; i <= 9; i++)
            foreach (var m in new[] { Modifiers.Win, Modifiers.Win | Modifiers.Shift, Modifiers.Win | Modifiers.Ctrl, WA })
                Assert.Equal(ReservedKind.Blocked, ReservedCombos.Find(K(Key.D0 + i, m))!.Kind);
        Assert.False(ReservedCombos.Find(K(Key.L, Modifiers.Win))!.NeedsConfirmation);
        Assert.True(ReservedCombos.Find(K(Key.D5, WA))!.NeedsConfirmation);
        Assert.Equal(ReservedKind.Blocked, ReservedCombos.Find(K(Key.Tab, Modifiers.Alt))!.Kind);
        Assert.Equal(ReservedKind.Blocked, ReservedCombos.Find(K(Key.F4, Modifiers.Alt))!.Kind);
        Assert.Equal(ReservedKind.Blocked, ReservedCombos.Find(K(Key.Escape, Modifiers.Ctrl))!.Kind);
        Assert.Equal(ReservedKind.Blocked, ReservedCombos.Find(K(Key.Escape, Modifiers.Ctrl | Modifiers.Shift))!.Kind);
    }

    [Fact]
    public void AC11_No_reserved_combo_in_defaults()
    {
        foreach (var p in new[] { WinAlt, CtrlAlt })
            foreach (var a in AllActions)
            {
                var r = ReservedCombos.Find(p[a]!.Value);
                Assert.True(r is null || r.Kind != ReservedKind.Blocked, $"{a}");
            }
        foreach (var a in AllActions) Assert.Null(ReservedCombos.Find(WinAlt[a]!.Value));
        foreach (var c in ViewCombos(CtrlAlt))
        {
            var r = ReservedCombos.Find(c);
            Assert.True(r is null || r.Kind != ReservedKind.Blocked);
        }
        foreach (var c in ViewCombos(WinAlt)) Assert.Null(ReservedCombos.Find(c));
        for (int n = 1; n <= 9; n++)
            Assert.Equal(ReservedKind.Blocked, ReservedCombos.Find(K(D(n), WA))!.Kind);
    }
}
