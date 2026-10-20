using CanvasWm.Core.Shortcuts;
using static CanvasWm.Core.Tests.ShortcutsSpec.T;
using SA = CanvasWm.Core.Shortcuts.ShortcutAction;

namespace CanvasWm.Core.Tests.ShortcutsSpec;

public sealed class EditorTests
{
    private static readonly ShortcutBindings B = WinAlt;

    [Fact]
    public void AC19_Cancel_and_clear()
    {
        foreach (var m in new[] { Modifiers.None, WA })
        {
            var r = ShortcutEditor.RecordForAction(B, SA.Maximize, Key.Escape, m);
            Assert.Equal(EditOutcome.Cancelled, r.Outcome);
            Assert.Equal(B, r.Bindings);
        }
        foreach (var k in new[] { Key.Backspace, Key.Delete })
        {
            var r = ShortcutEditor.RecordForAction(B, SA.Maximize, k, Modifiers.None);
            Assert.Equal(EditOutcome.Cleared, r.Outcome);
            Assert.Null(r.Bindings[SA.Maximize]);
            AssertSameExcept(B, r.Bindings, SA.Maximize);
        }
        var s = ShortcutEditor.RecordForAction(B, SA.Maximize, Key.Backspace, WA);
        Assert.Equal(EditOutcome.Stored, s.Outcome);
        Assert.Equal(K(Key.Backspace, WA), s.Bindings[SA.Maximize]);
        var v = ShortcutEditor.RecordForViews(B, ViewSet.Save, Key.Delete, Modifiers.None);
        Assert.Equal(EditOutcome.Cleared, v.Outcome);
        Assert.Null(v.Bindings.SaveViewModifiers);
        Assert.DoesNotContain(ShortcutDispatch.Build(v.Bindings), e => e.Command.Kind == CommandKind.SaveView);
        Assert.Equal(EditOutcome.Cancelled, ShortcutEditor.RecordForViews(B, ViewSet.Recall, Key.Escape, Modifiers.None).Outcome);
    }

    [Fact]
    public void AC20_Required_modifier()
    {
        foreach (var m in new[] { Modifiers.Shift, Modifiers.None })
        {
            var r = ShortcutEditor.RecordForAction(B, SA.Split, Key.K, m);
            Assert.Equal(EditOutcome.NeedsModifier, r.Outcome);
            Assert.Equal(B, r.Bindings);
        }
        foreach (var m in new[] { Modifiers.Ctrl, Modifiers.Alt, Modifiers.Win })
            Assert.Equal(EditOutcome.Stored, ShortcutEditor.RecordForAction(B, SA.Split, Key.K, m).Outcome);
        Assert.Equal(EditOutcome.NotBindable, ShortcutEditor.SetAction(B, SA.Split, K(Key.Escape, WA)).Outcome);
        Assert.Equal(EditOutcome.NeedsModifier, ShortcutEditor.SetAction(B, SA.Split, K(Key.K, Modifiers.Shift)).Outcome);
        Assert.Equal(EditOutcome.NeedsModifier, ShortcutEditor.RecordForViews(B, ViewSet.Recall, Key.D4, Modifiers.Shift).Outcome);
        Assert.Equal(EditOutcome.NeedsModifier, ShortcutEditor.SetViewModifiers(B, ViewSet.Recall, Modifiers.Shift).Outcome);
    }

    [Fact]
    public void AC21_Move()
    {
        var r = ShortcutEditor.SetAction(B, SA.Back, K(Key.O, WA));
        Assert.Equal(EditOutcome.Moved, r.Outcome);
        Assert.Equal(SA.Overview, r.OtherAction);
        Assert.Null(r.Bindings[SA.Overview]);
        Assert.Equal(K(Key.O, WA), r.Bindings[SA.Back]);
        AssertSameExcept(B, r.Bindings, SA.Overview, SA.Back);
        var same = ShortcutEditor.SetAction(B, SA.Back, K(Key.LeftBracket, WA));
        Assert.Equal(EditOutcome.Stored, same.Outcome);
        Assert.Equal(B, same.Bindings);
    }

    [Fact]
    public void AC22_View_conflicts_for_actions()
    {
        var r = ShortcutEditor.SetAction(B, SA.Desktop, K(Key.D5, WAS));
        Assert.Equal(EditOutcome.ConflictsWithViews, r.Outcome);
        Assert.Equal(ViewSet.Recall, r.OtherViews);
        Assert.Equal(K(Key.W, WA), r.Bindings[SA.Desktop]);
        var s = ShortcutEditor.SetAction(B, SA.Desktop, K(Key.D5, WCA));
        Assert.Equal(EditOutcome.ConflictsWithViews, s.Outcome);
        Assert.Equal(ViewSet.Save, s.OtherViews);
        Assert.Equal(EditOutcome.Stored, ShortcutEditor.SetAction(B, SA.Desktop, K(Key.D0, WAS)).Outcome);
        Assert.Equal(EditOutcome.Stored, ShortcutEditor.SetAction(B, SA.Desktop, K(Key.D5, CA)).Outcome);
        Assert.Equal(EditOutcome.Stored, ShortcutEditor.SetAction(B, SA.Desktop, K(Key.D5, WA)).Outcome);
        var cleared = ShortcutEditor.ClearViews(B, ViewSet.Recall).Bindings;
        Assert.Equal(EditOutcome.Stored, ShortcutEditor.SetAction(cleared, SA.Desktop, K(Key.D5, WAS)).Outcome);
        Assert.Equal(EditOutcome.Stored, ShortcutEditor.SetAction(B, SA.Up, K(Key.Up, WAS)).Outcome);
    }

    [Fact]
    public void AC22_E7_Digit_action_when_set_unbound_then_set_refused()
    {
        var b = ShortcutEditor.ClearViews(B, ViewSet.Recall).Bindings;
        var r = ShortcutEditor.SetAction(b, SA.Desktop, K(Key.D5, WAS));
        Assert.Equal(EditOutcome.Stored, r.Outcome);
        var again = ShortcutEditor.SetViewModifiers(r.Bindings, ViewSet.Recall, WAS);
        Assert.Equal(EditOutcome.ConflictsWithAction, again.Outcome);
        Assert.Equal(SA.Desktop, again.OtherAction);
    }

    [Fact]
    public void AC23_View_sets()
    {
        var r = ShortcutEditor.SetViewModifiers(B, ViewSet.Recall, WCA);
        Assert.Equal(EditOutcome.RecallEqualsSave, r.Outcome);
        Assert.Equal(ViewSet.Save, r.OtherViews);
        Assert.Equal(B, r.Bindings);
        Assert.Equal(EditOutcome.Stored, ShortcutEditor.SetViewModifiers(B, ViewSet.Recall, WAS).Outcome);
        Assert.Equal(EditOutcome.NotADigit, ShortcutEditor.RecordForViews(B, ViewSet.Recall, Key.K, CA).Outcome);
        Assert.Equal(EditOutcome.NotADigit, ShortcutEditor.RecordForViews(B, ViewSet.Recall, Key.D0, CA).Outcome);
        var d7 = ShortcutEditor.RecordForViews(B, ViewSet.Recall, Key.D7, CA);
        Assert.Equal(EditOutcome.Stored, d7.Outcome);
        Assert.Equal(CA, d7.Bindings.RecallViewModifiers);
        Assert.Contains(ShortcutDispatch.Build(d7.Bindings), e => e.Equals(new DispatchEntry(K(Key.D7, CA), ShortcutCommand.GoToView(7))));

        var s = ShortcutEditor.SetAction(B, SA.Split, K(Key.D7, CA));
        Assert.Equal(EditOutcome.Stored, s.Outcome);
        var c = ShortcutEditor.SetViewModifiers(s.Bindings, ViewSet.Recall, CA);
        Assert.Equal(EditOutcome.ConflictsWithAction, c.Outcome);
        Assert.Equal(SA.Split, c.OtherAction);
        Assert.Equal(K(Key.D7, CA), c.Bindings[SA.Split]);
        Assert.Equal(WAS, c.Bindings.RecallViewModifiers);

        var bp = ShortcutEditor.ClearViews(B, ViewSet.Save).Bindings;
        var e = ShortcutEditor.SetViewModifiers(bp, ViewSet.Save, WAS);
        Assert.Equal(EditOutcome.RecallEqualsSave, e.Outcome);
        Assert.Equal(ViewSet.Recall, e.OtherViews);
        Assert.Equal(EditOutcome.Stored, ShortcutEditor.SetViewModifiers(bp, ViewSet.Save, CA).Outcome);
    }

    [Fact]
    public void AC24_Clear_and_Accepted()
    {
        var u = ShortcutBindings.Unbound;
        var a = ShortcutEditor.ClearAction(u, SA.Split);
        Assert.Equal(EditOutcome.Cleared, a.Outcome);
        Assert.Equal(u, a.Bindings);
        foreach (var s in Enum.GetValues<ViewSet>())
        {
            var v = ShortcutEditor.ClearViews(u, s);
            Assert.Equal(EditOutcome.Cleared, v.Outcome);
            Assert.Equal(u, v.Bindings);
        }
        var withSplit = ShortcutEditor.SetAction(B, SA.Split, K(Key.D7, CA)).Bindings;
        var results = new[]
        {
            ShortcutEditor.SetAction(B, SA.Back, K(Key.LeftBracket, WA)),      // Stored
            ShortcutEditor.SetAction(B, SA.Back, K(Key.O, WA)),                // Moved
            ShortcutEditor.ClearAction(B, SA.Back),                            // Cleared
            ShortcutEditor.RecordForAction(B, SA.Back, Key.Escape, WA),        // Cancelled
            ShortcutEditor.RecordForAction(B, SA.Back, Key.K, Modifiers.None), // NeedsModifier
            ShortcutEditor.SetAction(B, SA.Back, K(Key.Escape, WA)),           // NotBindable
            ShortcutEditor.RecordForViews(B, ViewSet.Recall, Key.K, CA),       // NotADigit
            ShortcutEditor.SetAction(B, SA.Back, K(Key.D5, WAS)),              // ConflictsWithViews
            ShortcutEditor.SetViewModifiers(withSplit, ViewSet.Recall, CA), // ConflictsWithAction
            ShortcutEditor.SetViewModifiers(B, ViewSet.Recall, WCA),           // RecallEqualsSave
        };
        var seen = results.Select(r => r.Outcome).ToHashSet();
        Assert.Equal(10, seen.Count);
        foreach (var r in results)
        {
            bool acc = r.Outcome is EditOutcome.Stored or EditOutcome.Moved or EditOutcome.Cleared;
            Assert.Equal(acc, r.Accepted);
            if (r.Outcome >= EditOutcome.Cancelled)
                Assert.Equal(r.Outcome == EditOutcome.ConflictsWithAction ? withSplit : B, r.Bindings);
        }
    }

    [Fact]
    public void Editor_null_and_range_checks()
    {
        Assert.Throws<ArgumentNullException>(() => ShortcutEditor.ClearAction(null!, SA.Back));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutEditor.ClearAction(B, (SA)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutEditor.ClearViews(B, (ViewSet)9));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutEditor.RecordForAction(B, SA.Back, (Key)0, WA));
    }
}
