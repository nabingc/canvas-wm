using CanvasWm.Core.Shortcuts;
using static CanvasWm.Core.Tests.ShortcutsSpec.T;
using SA = CanvasWm.Core.Shortcuts.ShortcutAction;

namespace CanvasWm.Core.Tests.ShortcutsSpec;

public sealed class DispatchTests
{
    private static readonly IReadOnlyList<DispatchEntry> Table = ShortcutDispatch.Build(WinAlt);

    [Fact]
    public void AC12_Build()
    {
        Assert.Equal(30, Table.Count);
        for (int i = 0; i < 12; i++)
        {
            Assert.Equal(WinAlt[(SA)i]!.Value, Table[i].Combo);
            Assert.Equal(ShortcutCommand.ForAction((SA)i), Table[i].Command);
        }
        Assert.Equal(new DispatchEntry(K(Key.D1, WAS), ShortcutCommand.GoToView(1)), Table[12]);
        Assert.Equal(new DispatchEntry(K(Key.D1, WCA), ShortcutCommand.SaveView(1)), Table[13]);
        for (int n = 1; n <= 9; n++)
        {
            Assert.Equal(new DispatchEntry(K(D(n), WAS), ShortcutCommand.GoToView(n)), Table[10 + 2 * n]);
            Assert.Equal(new DispatchEntry(K(D(n), WCA), ShortcutCommand.SaveView(n)), Table[11 + 2 * n]);
        }
        Assert.Equal(29, ShortcutDispatch.Build(WinAlt.WithAction(SA.Split, null)).Count);
        var noRecall = ShortcutDispatch.Build(WinAlt with { RecallViewModifiers = null });
        Assert.Equal(21, noRecall.Count);
        Assert.DoesNotContain(noRecall, e => e.Command.Kind == CommandKind.GoToView);
        Assert.Empty(ShortcutDispatch.Build(ShortcutBindings.Unbound));
        Assert.Throws<ArgumentNullException>(() => ShortcutDispatch.Build(null!));
    }

    [Fact]
    public void AC13_Match_WinAlt()
    {
        Assert.Equal(ShortcutCommand.ForAction(SA.Overview), ShortcutDispatch.Match(Table, K(Key.O, WA)));
        Assert.Equal(ShortcutCommand.ForAction(SA.Up), ShortcutDispatch.Match(Table, K(Key.Up, WAS)));
        Assert.Null(ShortcutDispatch.Match(Table, K(Key.Right, WA | Modifiers.Ctrl)));
        Assert.Null(ShortcutDispatch.Match(Table, K(Key.Right, Modifiers.Win)));
        Assert.Equal(ShortcutCommand.GoToView(3), ShortcutDispatch.Match(Table, K(Key.D3, WAS)));
        Assert.Equal(ShortcutCommand.SaveView(3), ShortcutDispatch.Match(Table, K(Key.D3, WCA)));
        Assert.Null(ShortcutDispatch.Match(Table, K(Key.D3, WA)));
        Assert.Null(ShortcutDispatch.Match(Table, K(Key.D0, WAS)));
        Assert.Throws<ArgumentNullException>(() => ShortcutDispatch.Match(null!, K(Key.O, WA)));
    }

    [Fact]
    public void AC14_First_match_wins()
    {
        var b = ShortcutBindings.Unbound.WithAction(SA.Forward, K(Key.B, WA)).WithAction(SA.Back, K(Key.B, WA));
        Assert.Equal(ShortcutCommand.ForAction(SA.Back), ShortcutDispatch.Match(ShortcutDispatch.Build(b), K(Key.B, WA)));
        var e = ShortcutBindings.Unbound with { RecallViewModifiers = WAS, SaveViewModifiers = WAS };
        var t = ShortcutDispatch.Build(e);
        Assert.Equal(18, t.Count);
        Assert.Equal(ShortcutCommand.GoToView(2), ShortcutDispatch.Match(t, K(Key.D2, WAS)));
    }

    [Fact]
    public void AC15_Hold_sequence()
    {
        var s0 = DispatchState.Initial;
        var (s1, o1) = ShortcutDispatch.KeyDown(s0, Table, Key.Right, WA, false);
        Assert.Equal(KeyVerdict.Consume, o1.Verdict);
        Assert.Equal(ShortcutCommand.ForAction(SA.Right), o1.Command);
        Assert.Equal(new[] { Key.Right }, s1.Held.ToArray());
        Assert.Empty(s0.Held);
        int commands = 1;
        var s = s1;
        for (int i = 0; i < 10; i++)
        {
            var before = s;
            var (n, o) = ShortcutDispatch.KeyDown(s, Table, Key.Right, WA, true);
            Assert.Equal(KeyVerdict.Consume, o.Verdict);
            Assert.Null(o.Command);
            if (o.Command is not null) commands++;
            Assert.Equal(new[] { Key.Right }, before.Held.ToArray());
            s = n;
        }
        var (s2, v) = ShortcutDispatch.KeyUp(s, Key.Right);
        Assert.Equal(KeyVerdict.Consume, v);
        Assert.Empty(s2.Held);
        Assert.Equal(new[] { Key.Right }, s.Held.ToArray());
        Assert.Equal(1, commands);
    }

    [Fact]
    public void AC16_Pass_through_cases()
    {
        var (s, v) = ShortcutDispatch.KeyUp(DispatchState.Initial, Key.Q);
        Assert.Equal(KeyVerdict.PassThrough, v);
        Assert.Empty(s.Held);
        Assert.False(s.Recording);

        var (s2, o) = ShortcutDispatch.KeyDown(DispatchState.Initial, Table, Key.Q, WA, false);
        Assert.Equal(KeyVerdict.PassThrough, o.Verdict);
        Assert.Null(o.Command);
        Assert.Empty(s2.Held);

        var (held, _) = ShortcutDispatch.KeyDown(DispatchState.Initial, Table, Key.Right, WA, false);
        var (s3, o3) = ShortcutDispatch.KeyDown(held, Table, Key.Right, Modifiers.Win, true);
        Assert.Equal(KeyVerdict.PassThrough, o3.Verdict);
        Assert.Null(o3.Command);
        Assert.Equal(new[] { Key.Right }, s3.Held.ToArray());
        var (s4, v4) = ShortcutDispatch.KeyUp(s3, Key.Right);
        Assert.Equal(KeyVerdict.Consume, v4);
        Assert.Empty(s4.Held);

        var (_, o5) = ShortcutDispatch.KeyDown(DispatchState.Initial, Table, Key.O, WA, true);
        Assert.Equal(KeyVerdict.Consume, o5.Verdict);
        Assert.Null(o5.Command);
    }

    [Fact]
    [Trait("Assumption", "A6")]
    public void AC16_A6_Repeat_decided_only_by_flag()
    {
        // O is held from a lost key-down; a non-repeat key-down must still run the command.
        var (held, _) = ShortcutDispatch.KeyDown(DispatchState.Initial, Table, Key.O, WA, false);
        var (_, o) = ShortcutDispatch.KeyDown(held, Table, Key.O, WA, false);
        Assert.Equal(ShortcutCommand.ForAction(SA.Overview), o.Command);
    }

    [Fact]
    [Trait("Assumption", "A7")]
    public void AC17_Recording()
    {
        var (held, _) = ShortcutDispatch.KeyDown(DispatchState.Initial, Table, Key.Right, WA, false);
        var rec = held.WithRecording(true);
        Assert.True(rec.Recording);
        Assert.Equal(new[] { Key.Right }, rec.Held.ToArray());
        var (s1, o) = ShortcutDispatch.KeyDown(rec, Table, Key.O, WA, false);
        Assert.Equal(KeyVerdict.PassThrough, o.Verdict);
        Assert.Null(o.Command);
        Assert.Equal(new[] { Key.Right }, s1.Held.ToArray());
        var (s2, v2) = ShortcutDispatch.KeyUp(s1, Key.O);
        Assert.Equal(KeyVerdict.PassThrough, v2);
        var (s3, v3) = ShortcutDispatch.KeyUp(s2, Key.Right);
        Assert.Equal(KeyVerdict.PassThrough, v3);
        Assert.Empty(s3.Held);
        var off = s3.WithRecording(false);
        var (_, o4) = ShortcutDispatch.KeyDown(off, Table, Key.O, WA, false);
        Assert.Equal(KeyVerdict.Consume, o4.Verdict);
        Assert.Equal(ShortcutCommand.ForAction(SA.Overview), o4.Command);
        Assert.False(DispatchState.Initial.Recording);
        Assert.Empty(DispatchState.Initial.Held);
    }

    [Fact]
    public void AC18_Argument_checks()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutDispatch.KeyDown(DispatchState.Initial, Table, (Key)0, WA, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutDispatch.KeyDown(DispatchState.Initial, Table, Key.O, (Modifiers)32, false));
        Assert.Throws<ArgumentNullException>(() => ShortcutDispatch.KeyDown(null!, Table, Key.O, WA, false));
        Assert.Throws<ArgumentNullException>(() => ShortcutDispatch.KeyDown(DispatchState.Initial, null!, Key.O, WA, false));
        Assert.Throws<ArgumentNullException>(() => ShortcutDispatch.KeyUp(null!, Key.O));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShortcutDispatch.KeyUp(DispatchState.Initial, (Key)0));
    }
}
