using CanvasWm.Core.Shortcuts;
using static CanvasWm.Core.Tests.ShortcutsSpec.T;
using SA = CanvasWm.Core.Shortcuts.ShortcutAction;

namespace CanvasWm.Core.Tests.ShortcutsSpec;

public sealed class StoreTests
{
    private static Dictionary<string, string> D(params (string, string)[] kv) =>
        kv.ToDictionary(x => x.Item1, x => x.Item2);

    [Fact]
    public void AC25_Format()
    {
        var f = ShortcutStore.Format(WinAlt);
        Assert.Equal(15, f.Count);
        Assert.Equal("1", f["hotkeys.version"]);
        Assert.Equal("O,10", f["hotkeys.overview"]);
        Assert.Equal("Up,14", f["hotkeys.up"]);
        Assert.Equal("LeftBracket,10", f["hotkeys.back"]);
        Assert.Equal("Minus,10", f["hotkeys.step-out"]);
        Assert.Equal("14", f["hotkeys.views.recall"]);
        Assert.Equal("11", f["hotkeys.views.save"]);
        Assert.Equal("D,3", ShortcutStore.Format(CtrlAlt)["hotkeys.desktop"]);
        var u = ShortcutStore.Format(ShortcutBindings.Unbound);
        Assert.Equal(15, u.Count);
        foreach (var kv in u.Where(x => x.Key != "hotkeys.version")) Assert.Equal("none", kv.Value);
        Assert.Equal("hotkeys.step-in", ShortcutStore.ActionKey(SA.StepIn));
    }

    [Fact]
    public void AC26_Round_trip_profiles()
    {
        foreach (var x in new[] { WinAlt, CtrlAlt, ShortcutBindings.Unbound })
        {
            Assert.Equal(x, ShortcutStore.Parse(ShortcutStore.Format(x), ShortcutBindings.Unbound));
            Assert.Equal(x, ShortcutStore.Parse(ShortcutStore.Format(x), WinAlt));
        }
    }

    [Fact]
    public void AC26_Round_trip_random()
    {
        var rng = new Random(20240607);
        var keys = Enum.GetValues<Key>().Where(k => k != Key.Escape).ToArray();
        Modifiers RandSet()
        {
            while (true)
            {
                var m = (Modifiers)rng.Next(0, 16);
                if (ShortcutRules.HasRequiredModifier(m)) return m;
            }
        }
        for (int i = 0; i < 200; i++)
        {
            var b = ShortcutBindings.Unbound;
            foreach (var a in AllActions)
                if (rng.Next(3) != 0) b = b.WithAction(a, K(keys[rng.Next(keys.Length)], RandSet()));
            if (rng.Next(3) != 0) b = b with { RecallViewModifiers = RandSet() };
            if (rng.Next(3) != 0) b = b with { SaveViewModifiers = RandSet() };
            Assert.Equal(b, ShortcutStore.Parse(ShortcutStore.Format(b), WinAlt));
        }
    }

    [Fact]
    public void AC27_Missing_and_none()
    {
        Assert.Equal(WinAlt, ShortcutStore.Parse(new Dictionary<string, string>(), WinAlt));
        var p = ShortcutStore.Parse(D(("hotkeys.overview", "none")), WinAlt);
        Assert.Null(p[SA.Overview]);
        AssertSameExcept(WinAlt, p, SA.Overview);
        var r = ShortcutStore.Parse(D(("hotkeys.views.recall", "none")), WinAlt);
        Assert.Null(r.RecallViewModifiers);
        Assert.Equal(WCA, r.SaveViewModifiers);
        Assert.Equal(21, ShortcutDispatch.Build(r).Count);
        Assert.Equal(WinAlt, ShortcutStore.Parse(D(("hotkeys.everything", "x"), ("x", "y")), WinAlt));
        Assert.Throws<ArgumentNullException>(() => ShortcutStore.Parse(null!, WinAlt));
        Assert.Throws<ArgumentNullException>(() => ShortcutStore.Parse(new Dictionary<string, string>(), null!));
        Assert.Throws<ArgumentNullException>(() => ShortcutStore.ParseCombo(null!));
        Assert.Throws<ArgumentNullException>(() => ShortcutStore.ParseViewModifiers(null!));
    }

    [Theory]
    [InlineData("abc")] [InlineData("O")] [InlineData("O,10,1")] [InlineData("o,10")] [InlineData("15,10")]
    [InlineData("O,16")] [InlineData("O,-1")] [InlineData("O,+2")] [InlineData(" O,10")] [InlineData("O, 10")]
    [InlineData("O,")] [InlineData(",10")] [InlineData("O,4")] [InlineData("O,0")] [InlineData("Escape,10")]
    [InlineData("None")] [InlineData("")] [InlineData("none")]
    public void AC28_Malformed_combo_is_null(string value)
    {
        Assert.Null(ShortcutStore.ParseCombo(value));
        var p = ShortcutStore.Parse(D(("hotkeys.overview", value)), WinAlt);
        Assert.Null(p[SA.Overview]);
    }

    [Fact]
    public void AC28_Null_dictionary_value_is_unbound()
    {
        var d = new Dictionary<string, string> { ["hotkeys.overview"] = null!, ["hotkeys.views.save"] = null! };
        var p = ShortcutStore.Parse(d, WinAlt);
        Assert.Null(p[SA.Overview]);
        Assert.Null(p.SaveViewModifiers);
        Assert.Equal(WAS, p.RecallViewModifiers);
    }

    [Fact]
    public void AC28_Valid_combos()
    {
        Assert.Equal(K(Key.O, WA), ShortcutStore.ParseCombo("O,010"));
        Assert.Equal(K(Key.F24, Modifiers.Ctrl), ShortcutStore.ParseCombo("F24,1"));
        Assert.Equal("none", ShortcutStore.FormatCombo(null));
        Assert.Equal("O,10", ShortcutStore.FormatCombo(K(Key.O, WA)));
    }

    [Theory]
    [InlineData("abc")] [InlineData("16")] [InlineData("4")] [InlineData("0")] [InlineData("")]
    [InlineData("-2")] [InlineData("None")] [InlineData("10 ")] [InlineData("none")]
    public void AC28_Malformed_view_modifiers_null(string value)
    {
        Assert.Null(ShortcutStore.ParseViewModifiers(value));
        var p = ShortcutStore.Parse(D(("hotkeys.views.recall", value)), WinAlt);
        Assert.Null(p.RecallViewModifiers);
    }

    [Fact]
    public void AC28_Valid_view_modifiers()
    {
        Assert.Equal(CA, ShortcutStore.ParseViewModifiers("3"));
        Assert.Equal("14", ShortcutStore.FormatViewModifiers(WAS));
        Assert.Equal("none", ShortcutStore.FormatViewModifiers(null));
    }

    [Fact]
    [Trait("Assumption", "A10")]
    public void AC29_Version()
    {
        Assert.Equal(WinAlt, ShortcutStore.Parse(D(("hotkeys.version", "2"), ("hotkeys.overview", "none")), WinAlt));
        Assert.Null(ShortcutStore.Parse(D(("hotkeys.version", "1"), ("hotkeys.overview", "none")), WinAlt)[SA.Overview]);
        Assert.Null(ShortcutStore.Parse(D(("hotkeys.overview", "none")), WinAlt)[SA.Overview]);
    }

    [Fact]
    [Trait("Assumption", "A11")]
    public void A11_Bare_or_escape_parses_unbound()
    {
        Assert.Null(ShortcutStore.ParseCombo("O,0"));
        Assert.Null(ShortcutStore.ParseCombo("O,4"));
        Assert.Null(ShortcutStore.ParseCombo("Escape,2"));
        Assert.Null(ShortcutStore.ParseViewModifiers("4"));
    }

    [Fact]
    [Trait("Assumption", "A12")]
    public void A12_Parse_keeps_conflicts()
    {
        var p = ShortcutStore.Parse(D(("hotkeys.back", "B,10"), ("hotkeys.forward", "B,10"),
            ("hotkeys.views.recall", "11"), ("hotkeys.views.save", "11"), ("hotkeys.split", "D2,11")), WinAlt);
        Assert.Equal(K(Key.B, WA), p[SA.Back]);
        Assert.Equal(K(Key.B, WA), p[SA.Forward]);
        Assert.Equal(p.RecallViewModifiers, p.SaveViewModifiers);
        Assert.Equal(K(Key.D2, WA | Modifiers.Ctrl), p[SA.Split]);
    }
}
