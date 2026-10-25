using System.Reflection;
using CanvasWm.Core.Shortcuts;
using static CanvasWm.Core.Tests.ShortcutsSpec.T;
using SA = CanvasWm.Core.Shortcuts.ShortcutAction;

namespace CanvasWm.Core.Tests.ShortcutsSpec;

public sealed class HotkeyPlanTests
{
    private static readonly KeyCombo T1 = K(Key.F24, Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift | Modifiers.Win);
    private static readonly KeyCombo T2 = K(Key.F22, Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift);
    private static readonly ShortcutCommand X = ShortcutCommand.ForAction(SA.Overview);
    private static readonly ShortcutCommand Y = ShortcutCommand.ForAction(SA.Split);

    [Fact]
    public void AC1a_Desired_WinAlt_equals_table_combos()
    {
        var table = ShortcutDispatch.Build(WinAlt);
        var d = HotkeyPlan.Desired(table, false);
        Assert.Equal(30, d.Count);
        Assert.True(table.Select(e => e.Combo).SequenceEqual(d));
        Assert.Equal(K(Key.O, WA), d[0]);
        Assert.Equal(K(Key.D1, WAS), d[12]);
        Assert.Equal(K(Key.D1, WCA), d[13]);
    }

    [Fact]
    public void AC1b_Desired_recording_is_empty()
        => Assert.Empty(HotkeyPlan.Desired(ShortcutDispatch.Build(WinAlt), true));

    [Fact]
    public void AC1c_Desired_dedups_first_occurrence()
    {
        var b = WinAlt.WithAction(SA.Back, K(Key.B, WA)).WithAction(SA.Forward, K(Key.B, WA));
        var table = ShortcutDispatch.Build(b);
        var d = HotkeyPlan.Desired(table, false);
        Assert.Equal(29, d.Count);
        Assert.Single(d, K(Key.B, WA));
        int backPos = table.ToList().FindIndex(e => e.Combo == K(Key.B, WA));
        Assert.Equal(K(Key.B, WA), d[backPos]);
        Assert.Equal(ShortcutCommand.ForAction(SA.Back), table[backPos].Command);
    }

    [Fact]
    public void AC1d_Desired_skips_unbindable_and_duplicates()
    {
        var table = new[]
        {
            new DispatchEntry(K(Key.K, Modifiers.Shift), X),
            new DispatchEntry(K(Key.Escape, WA), X),
            new DispatchEntry(K(Key.O, WA), X),
            new DispatchEntry(K(Key.O, WA), Y),
        };
        Assert.Equal(new[] { K(Key.O, WA) }, HotkeyPlan.Desired(table, false));
    }

    [Fact]
    public void AC1e_Desired_empty_cases()
    {
        Assert.Empty(HotkeyPlan.Desired(Array.Empty<DispatchEntry>(), false));
        Assert.Empty(HotkeyPlan.Desired(ShortcutDispatch.Build(ShortcutBindings.Unbound), false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AC1f_Desired_argument_errors(bool recording)
    {
        Assert.Throws<ArgumentNullException>(() => HotkeyPlan.Desired(null!, recording));
        var bad = new[] { new DispatchEntry(K(Key.O, WA), X), new DispatchEntry(default, X) };
        Assert.Throws<ArgumentOutOfRangeException>(() => HotkeyPlan.Desired(bad, recording));
    }

    [Fact]
    public void AC1g_Every_desired_combo_matches()
    {
        var table = ShortcutDispatch.Build(WinAlt);
        foreach (var c in HotkeyPlan.Desired(table, false)) Assert.NotNull(ShortcutDispatch.Match(table, c));
    }

    [Fact]
    public void AC2a_Diff_basic()
    {
        var r = new[] { K(Key.C, WA), K(Key.A, WA), K(Key.B, WA) };
        var d = new[] { K(Key.C, WA), K(Key.D, WA), K(Key.A, WA), K(Key.D, WA) };
        var diff = HotkeyPlan.Diff(r, d);
        Assert.Equal(new[] { K(Key.B, WA) }, diff.ToUnregister);
        Assert.Equal(new[] { K(Key.D, WA) }, diff.ToRegister);
    }

    [Fact]
    public void AC2b_Diff_unregister_sorted()
    {
        var r = new[] { K(Key.O, WA), K(Key.A, CA), K(Key.A, WA), K(Key.A, Modifiers.Ctrl) };
        var diff = HotkeyPlan.Diff(r, Array.Empty<KeyCombo>());
        Assert.Equal(new[] { K(Key.A, Modifiers.Ctrl), K(Key.A, CA), K(Key.A, WA), K(Key.O, WA) }, diff.ToUnregister);
        Assert.Empty(diff.ToRegister);
    }

    [Fact]
    public void AC2c_Diff_register_order_distinct()
    {
        var diff = HotkeyPlan.Diff(Array.Empty<KeyCombo>(), new[] { T2, T1, T2 });
        Assert.Empty(diff.ToUnregister);
        Assert.Equal(new[] { T2, T1 }, diff.ToRegister);
    }

    [Fact]
    public void AC2d_Diff_registered_duplicates()
    {
        var diff = HotkeyPlan.Diff(new[] { T1, T1 }, new[] { T1 });
        Assert.Empty(diff.ToUnregister);
        Assert.Empty(diff.ToRegister);
    }

    [Fact]
    public void AC2e_Diff_equal_sets_different_order()
    {
        var diff = HotkeyPlan.Diff(new[] { T1, T2 }, new[] { T2, T1 });
        Assert.Empty(diff.ToUnregister);
        Assert.Empty(diff.ToRegister);
    }

    [Fact]
    public void AC2f_Diff_inputs_unchanged_results_readonly()
    {
        var r = new List<KeyCombo> { T1, K(Key.A, WA) };
        var d = new List<KeyCombo> { T2, T2 };
        var diff = HotkeyPlan.Diff(r, d);
        Assert.Equal(new[] { T1, K(Key.A, WA) }, r);
        Assert.Equal(new[] { T2, T2 }, d);
        Assert.Throws<NotSupportedException>(() => ((IList<KeyCombo>)diff.ToUnregister).Add(T1));
        Assert.Throws<NotSupportedException>(() => ((IList<KeyCombo>)diff.ToRegister).Add(T1));
        var dd = HotkeyPlan.Desired(ShortcutDispatch.Build(WinAlt), false);
        Assert.Throws<NotSupportedException>(() => ((IList<KeyCombo>)dd).Add(T1));
    }

    [Fact]
    public void AC2g_Diff_argument_errors()
    {
        Assert.Throws<ArgumentNullException>(() => HotkeyPlan.Diff(null!, new[] { T1 }));
        Assert.Throws<ArgumentNullException>(() => HotkeyPlan.Diff(new[] { T1 }, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => HotkeyPlan.Diff(new[] { default(KeyCombo) }, new[] { T1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => HotkeyPlan.Diff(new[] { T1 }, new[] { default(KeyCombo) }));
    }

    [Fact]
    public void AC3_Recording_round_trip_and_rebind()
    {
        var table = ShortcutDispatch.Build(WinAlt);
        var on = HotkeyPlan.Desired(table, false);
        var d1 = HotkeyPlan.Diff(Array.Empty<KeyCombo>(), on);
        Assert.Equal(30, d1.ToRegister.Count);
        Assert.Empty(d1.ToUnregister);

        var reg = d1.ToRegister.ToList();
        var off = HotkeyPlan.Diff(reg, HotkeyPlan.Desired(table, true));
        Assert.Equal(30, off.ToUnregister.Count);
        Assert.Empty(off.ToRegister);
        var sorted = reg.OrderBy(c => (int)c.Key).ThenBy(c => (int)c.Modifiers).ToList();
        Assert.True(sorted.SequenceEqual(off.ToUnregister));

        var d3 = HotkeyPlan.Diff(Array.Empty<KeyCombo>(), HotkeyPlan.Desired(table, false));
        Assert.True(d1.ToRegister.SequenceEqual(d3.ToRegister));
        Assert.True(d1.ToUnregister.SequenceEqual(d3.ToUnregister));

        var rebound = ShortcutDispatch.Build(WinAlt.WithAction(SA.Back, K(Key.B, WA)));
        var d4 = HotkeyPlan.Diff(reg, HotkeyPlan.Desired(rebound, false));
        Assert.Equal(new[] { K(Key.LeftBracket, WA) }, d4.ToUnregister);
        Assert.Equal(new[] { K(Key.B, WA) }, d4.ToRegister);
    }

    [Fact]
    public void AC4_Types()
    {
        Assert.Equal(0, (int)HotkeyStatus.Registered);
        Assert.Equal(1, (int)HotkeyStatus.NotOnLayout);
        Assert.Equal(2, (int)HotkeyStatus.Taken);
        Assert.Equal(3, (int)HotkeyStatus.Failed);
        Assert.True(new HotkeyReport(T1, HotkeyStatus.Registered, 0).IsActive);
        Assert.True(new HotkeyReport(T1, HotkeyStatus.NotOnLayout, 0).IsActive);
        Assert.False(new HotkeyReport(T1, HotkeyStatus.Taken, 1409).IsActive);
        Assert.False(new HotkeyReport(T1, HotkeyStatus.Failed, 0).IsActive);
        Assert.Equal(new HotkeyReport(T1, HotkeyStatus.Taken, 1409), new HotkeyReport(T1, HotkeyStatus.Taken, 1409));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CanvasWm.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void AC5_Key_doc_fix()
    {
        string root = RepoRoot();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "src", "CanvasWm.Core"), "*.cs", SearchOption.AllDirectories))
            Assert.DoesNotContain("US-layout punctuation position", File.ReadAllText(f));
        string key = File.ReadAllText(Path.Combine(root, "src", "CanvasWm.Core", "Shortcuts", "Key.cs"));
        const string phrase = "on the US layout. It is identified by the key code";
        int count = 0, i = 0;
        while ((i = key.IndexOf(phrase, i, StringComparison.Ordinal)) >= 0) { count++; i += phrase.Length; }
        Assert.Equal(11, count);
        Assert.Equal(86, Enum.GetValues<Key>().Length);
    }

    [Fact]
    public void AC6_Types_public_in_core_namespace()
    {
        var asm = typeof(KeyCombo).Assembly;
        foreach (var n in new[] { "HotkeyStatus", "HotkeyReport", "HotkeyDiff", "HotkeyPlan" })
        {
            var t = asm.GetType("CanvasWm.Core.Shortcuts." + n);
            Assert.True(t is not null && t.IsPublic, n);
        }
        Assert.DoesNotContain(asm.GetReferencedAssemblies().Select(a => a.Name ?? ""),
            n => n.StartsWith("CanvasWm.", StringComparison.Ordinal));
        // public surface of the new types uses only System / Core types
        foreach (var n in new[] { "HotkeyStatus", "HotkeyReport", "HotkeyDiff", "HotkeyPlan" })
        {
            var t = asm.GetType("CanvasWm.Core.Shortcuts." + n)!;
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var types = m switch
                {
                    MethodInfo mi => mi.GetParameters().Select(p => p.ParameterType).Append(mi.ReturnType),
                    PropertyInfo pi => new[] { pi.PropertyType },
                    _ => Array.Empty<Type>(),
                };
                foreach (var ty in types.SelectMany(Flatten))
                    Assert.True(ty.Namespace is not null && (ty.Namespace.StartsWith("System", StringComparison.Ordinal)
                        || ty.Namespace.StartsWith("CanvasWm.Core", StringComparison.Ordinal)), $"{n}.{m.Name}: {ty}");
            }
        }
    }

    private static IEnumerable<Type> Flatten(Type t)
    {
        if (t.IsByRef || t.IsArray) return Flatten(t.GetElementType()!);
        if (t.IsGenericType) return new[] { t.GetGenericTypeDefinition() }.Concat(t.GetGenericArguments().SelectMany(Flatten));
        return new[] { t };
    }
}
