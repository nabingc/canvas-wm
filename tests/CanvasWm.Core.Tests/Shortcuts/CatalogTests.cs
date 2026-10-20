using CanvasWm.Core.Layout;
using CanvasWm.Core.Shortcuts;
using static CanvasWm.Core.Tests.ShortcutsSpec.T;

namespace CanvasWm.Core.Tests.ShortcutsSpec;

public sealed class CatalogTests
{
    private static readonly (ShortcutAction A, string Id, string Desc, CommandKind Kind, Edge? Toward, TargetEngine Target)[] Table =
    {
        (ShortcutAction.Overview, "overview", "Show or hide the overview of all windows", CommandKind.ToggleOverview, null, TargetEngine.Active),
        (ShortcutAction.StepOut, "step-out", "Zoom out one level", CommandKind.ZoomOut, null, TargetEngine.Active),
        (ShortcutAction.StepIn, "step-in", "Zoom in one level", CommandKind.ZoomIn, null, TargetEngine.PointerThenActive),
        (ShortcutAction.Left, "left", "Go to the tile on the left", CommandKind.Move, Edge.Left, TargetEngine.Active),
        (ShortcutAction.Right, "right", "Go to the tile on the right", CommandKind.Move, Edge.Right, TargetEngine.Active),
        (ShortcutAction.Up, "up", "Go to the tile above", CommandKind.Move, Edge.Top, TargetEngine.Active),
        (ShortcutAction.Down, "down", "Go to the tile below", CommandKind.Move, Edge.Bottom, TargetEngine.Active),
        (ShortcutAction.Back, "back", "Go back to the previous view", CommandKind.Back, null, TargetEngine.Active),
        (ShortcutAction.Forward, "forward", "Go forward to the next view", CommandKind.Forward, null, TargetEngine.Active),
        (ShortcutAction.Maximize, "maximize", "Show the focused window alone, or undo", CommandKind.ToggleMaximize, null, TargetEngine.Active),
        (ShortcutAction.Desktop, "desktop", "Show or hide the empty desktop area", CommandKind.ToggleDesktopHidden, null, TargetEngine.Active),
        (ShortcutAction.Split, "split", "Open a new Explorer window beside the current tile", CommandKind.SplitForNewWindow, null, TargetEngine.Pointer),
    };

    [Fact]
    public void AC1_Catalogue_matches_table()
    {
        Assert.Equal(12, ShortcutCatalog.Actions.Count);
        for (int i = 0; i < 12; i++)
        {
            var e = ShortcutCatalog.Actions[i];
            var t = Table[i];
            Assert.Equal(i, (int)e.Action);
            Assert.Equal(t.A, e.Action);
            Assert.Equal(t.Id, e.Id);
            Assert.Equal(t.Desc, e.Description);
            Assert.Equal(t.Kind, e.Command.Kind);
            Assert.Equal(t.Toward, e.Command.Toward);
            Assert.Equal(t.Target, e.Target);
            Assert.False(e.RepeatsOnHold);
            Assert.Equal(e, ShortcutCatalog.Info(e.Action));
            Assert.Equal(ShortcutCommand.ForAction(e.Action), e.Command);
            Assert.Equal(e.Action, e.Command.Action);
            Assert.Null(e.Command.Slot);
            Assert.Equal(t.Toward is not null, e.Command.Toward is not null);
        }
        Assert.Equal(4, ShortcutCatalog.Actions.Count(a => a.Command.Toward is not null));
    }

    [Fact]
    public void AC2_TryParseId_exact()
    {
        foreach (var t in Table)
        {
            Assert.True(ShortcutCatalog.TryParseId(t.Id, out var a));
            Assert.Equal(t.A, a);
        }
        foreach (var bad in new[] { "Overview", "overview ", "", "everything" })
            Assert.False(ShortcutCatalog.TryParseId(bad, out _));
        Assert.Throws<ArgumentNullException>(() => ShortcutCatalog.TryParseId(null!, out _));
    }

    [Fact]
    public void AC2_Unique_and_clean_text()
    {
        Assert.Equal(12, ShortcutCatalog.Actions.Select(a => a.Id).Distinct().Count());
        Assert.Equal(12, ShortcutCatalog.Actions.Select(a => a.Description).Distinct().Count());
        var texts = ShortcutCatalog.Actions.SelectMany(a => new[] { a.Id, a.Description })
            .Concat(ReservedCombos.All.Select(r => r.Owner));
        for (int n = 1; n <= 9; n++)
            texts = texts.Append(ShortcutCatalog.DescribeView(ViewSet.Recall, n)).Append(ShortcutCatalog.DescribeView(ViewSet.Save, n));
        foreach (var s in texts)
        {
            Assert.DoesNotContain("trellis", s, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("licens", s, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AC3_View_commands()
    {
        var g = ShortcutCommand.GoToView(3);
        Assert.Equal(CommandKind.GoToView, g.Kind);
        Assert.Equal(3, g.Slot);
        Assert.Null(g.Action);
        Assert.Null(g.Toward);
        var s = ShortcutCommand.SaveView(9);
        Assert.Equal(CommandKind.SaveView, s.Kind);
        Assert.Equal(9, s.Slot);
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutCommand.GoToView(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutCommand.GoToView(10));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutCommand.SaveView(-1));
        Assert.Equal("Go to saved view 3", ShortcutCatalog.DescribeView(ViewSet.Recall, 3));
        Assert.Equal("Save the current view as view 3", ShortcutCatalog.DescribeView(ViewSet.Save, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutCatalog.DescribeView(ViewSet.Recall, 0));
        Assert.Equal(9, ShortcutCatalog.ViewSlots);
    }

    [Fact]
    public void AC4_Enums()
    {
        var keys = Enum.GetValues<Key>();
        Assert.Equal(86, keys.Length);
        Assert.DoesNotContain(keys, k => (int)k == 0);
        Assert.Equal(1, (int)Key.A); Assert.Equal(26, (int)Key.Z);
        Assert.Equal(30, (int)Key.D0); Assert.Equal(39, (int)Key.D9);
        Assert.Equal(40, (int)Key.F1); Assert.Equal(63, (int)Key.F24);
        Assert.Equal(70, (int)Key.Left); Assert.Equal(83, (int)Key.Backspace);
        Assert.Equal(84, (int)Key.Delete); Assert.Equal(85, (int)Key.Escape);
        Assert.Equal(110, (int)Key.Backquote);
        Assert.Equal(1, (int)Modifiers.Ctrl); Assert.Equal(2, (int)Modifiers.Alt);
        Assert.Equal(4, (int)Modifiers.Shift); Assert.Equal(8, (int)Modifiers.Win);
        Assert.Equal(15, (int)Modifiers.All);
        Assert.Equal(0, (int)ShortcutAction.Overview); Assert.Equal(11, (int)ShortcutAction.Split);
        Assert.Equal(7, (int)ShortcutAction.Back); Assert.Equal(10, (int)ShortcutAction.Desktop);
        Assert.Equal(0, (int)CommandKind.ToggleOverview); Assert.Equal(3, (int)CommandKind.Move);
        Assert.Equal(8, (int)CommandKind.SplitForNewWindow); Assert.Equal(9, (int)CommandKind.GoToView);
        Assert.Equal(10, (int)CommandKind.SaveView);
        Assert.Equal(0, (int)TargetEngine.Active); Assert.Equal(1, (int)TargetEngine.PointerThenActive);
        Assert.Equal(2, (int)TargetEngine.Pointer);
        Assert.Equal(0, (int)ViewSet.Recall); Assert.Equal(1, (int)ViewSet.Save);
        var eo = new[] { EditOutcome.Stored, EditOutcome.Moved, EditOutcome.Cleared, EditOutcome.Cancelled,
            EditOutcome.NeedsModifier, EditOutcome.NotBindable, EditOutcome.NotADigit,
            EditOutcome.ConflictsWithViews, EditOutcome.ConflictsWithAction, EditOutcome.RecallEqualsSave };
        for (int i = 0; i < eo.Length; i++) Assert.Equal(i, (int)eo[i]);
        Assert.Equal(0, (int)ReservedKind.Blocked); Assert.Equal(1, (int)ReservedKind.Conditional);
        Assert.Equal(0, (int)KeyVerdict.PassThrough); Assert.Equal(1, (int)KeyVerdict.Consume);
        Assert.Equal(0, (int)ShortcutProfile.WinAlt); Assert.Equal(1, (int)ShortcutProfile.CtrlAlt);
    }

    [Fact]
    public void AC5_KeyCombo_and_rules()
    {
        Assert.Equal(K(Key.O, WA), K(Key.O, WA));
        Assert.NotEqual(K(Key.O, WA), K(Key.O, WAS));
        Assert.NotEqual(K(Key.O, WA), K(Key.P, WA));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyCombo((Key)0, WA));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyCombo((Key)999, WA));
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyCombo(Key.O, (Modifiers)16));
        var d = default(KeyCombo);
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutRules.IsBindable(d));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutRules.MayClashWithAltGr(d));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReservedCombos.Find(d));
        Assert.Throws<ArgumentOutOfRangeException>(() => WinAlt.ActionBoundTo(d));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutEditor.SetAction(WinAlt, ShortcutAction.Back, d));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutDispatch.Match(ShortcutDispatch.Build(WinAlt), d));

        Assert.True(ShortcutRules.HasRequiredModifier(Modifiers.Ctrl));
        Assert.True(ShortcutRules.HasRequiredModifier(Modifiers.Alt));
        Assert.True(ShortcutRules.HasRequiredModifier(Modifiers.Win));
        Assert.True(ShortcutRules.HasRequiredModifier(Modifiers.Win | Modifiers.Shift));
        Assert.True(ShortcutRules.HasRequiredModifier(Modifiers.All));
        Assert.False(ShortcutRules.HasRequiredModifier(Modifiers.None));
        Assert.False(ShortcutRules.HasRequiredModifier(Modifiers.Shift));
        Assert.Equal(Modifiers.Ctrl | Modifiers.Alt | Modifiers.Win, ShortcutRules.Required);

        Assert.False(ShortcutRules.IsBindable(K(Key.Escape, WA)));
        Assert.True(ShortcutRules.IsBindable(K(Key.Backspace, WA)));
        for (int n = 1; n <= 9; n++)
        {
            Assert.Equal(n, ShortcutRules.SlotOf(D(n)));
            Assert.Equal((Key)((int)Key.D0 + n), D(n));
        }
        Assert.Null(ShortcutRules.SlotOf(Key.D0));
        Assert.Null(ShortcutRules.SlotOf(Key.A));
        Assert.Equal(Key.D5, ShortcutRules.SlotKey(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutRules.SlotKey(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutRules.SlotKey(10));

        Assert.True(ShortcutRules.MayClashWithAltGr(K(Key.D5, CA)));
        Assert.True(ShortcutRules.MayClashWithAltGr(K(Key.LeftBracket, CAS)));
        Assert.True(ShortcutRules.MayClashWithAltGr(K(Key.Q, CA)));
        Assert.True(ShortcutRules.MayClashWithAltGr(K(Key.Space, CA)));
        Assert.False(ShortcutRules.MayClashWithAltGr(K(Key.D5, WA)));
        Assert.False(ShortcutRules.MayClashWithAltGr(K(Key.D5, WCA)));
        Assert.False(ShortcutRules.MayClashWithAltGr(K(Key.Left, CA)));
        Assert.False(ShortcutRules.MayClashWithAltGr(K(Key.F1, CA)));
    }

    [Fact]
    public void AC6_Bindings()
    {
        var u = ShortcutBindings.Unbound;
        foreach (var a in AllActions) Assert.Null(u[a]);
        Assert.Null(u.RecallViewModifiers);
        Assert.Null(u.SaveViewModifiers);

        var b = WinAlt;
        var b2 = b.WithAction(ShortcutAction.Back, K(Key.B, WA));
        Assert.Equal(K(Key.LeftBracket, WA), b[ShortcutAction.Back]);
        Assert.Equal(K(Key.B, WA), b2[ShortcutAction.Back]);
        AssertSameExcept(b, b2, ShortcutAction.Back);
        Assert.NotEqual(b, b2);

        var x = ShortcutBindings.Unbound.WithAction(ShortcutAction.Left, K(Key.A, CA)) with { RecallViewModifiers = CA };
        var y = ShortcutBindings.Unbound.WithAction(ShortcutAction.Left, K(Key.A, CA)) with { RecallViewModifiers = CA };
        Assert.Equal(x, y);
        Assert.True(x.Equals(y));
        Assert.Equal(x.GetHashCode(), y.GetHashCode());
        Assert.Equal(ShortcutProfiles.WinAlt.GetHashCode(), ShortcutProfiles.Get(ShortcutProfile.WinAlt).GetHashCode());

        Assert.Throws<ArgumentException>(() => b.WithAction(ShortcutAction.Back, K(Key.K, Modifiers.Shift)));
        Assert.Throws<ArgumentException>(() => b.WithAction(ShortcutAction.Back, K(Key.Escape, Modifiers.Win)));
        Assert.Throws<ArgumentException>(() => u with { RecallViewModifiers = Modifiers.Shift });
        Assert.Throws<ArgumentException>(() => u with { SaveViewModifiers = Modifiers.None });
        Assert.Throws<ArgumentException>(() => u.WithViewModifiers(ViewSet.Save, Modifiers.Shift));

        var dup = u.WithAction(ShortcutAction.Forward, K(Key.B, WA)).WithAction(ShortcutAction.Back, K(Key.B, WA));
        Assert.Equal(ShortcutAction.Back, dup.ActionBoundTo(K(Key.B, WA)));
        Assert.Null(dup.ActionBoundTo(K(Key.C, WA)));

        Assert.Throws<ArgumentOutOfRangeException>(() => u[(ShortcutAction)99]);
        Assert.Throws<ArgumentOutOfRangeException>(() => u.WithAction((ShortcutAction)99, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutCatalog.Info((ShortcutAction)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutCommand.ForAction((ShortcutAction)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => u.ViewModifiers((ViewSet)5));
        Assert.Throws<ArgumentOutOfRangeException>(() => u.WithViewModifiers((ViewSet)5, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutCatalog.DescribeView((ViewSet)5, 1));
    }
}
