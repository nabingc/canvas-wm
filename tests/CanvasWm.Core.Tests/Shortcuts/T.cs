using CanvasWm.Core.Shortcuts;

namespace CanvasWm.Core.Tests.ShortcutsSpec;

internal static class T
{
    public const Modifiers WA = Modifiers.Win | Modifiers.Alt;
    public const Modifiers CA = Modifiers.Ctrl | Modifiers.Alt;
    public const Modifiers WCA = Modifiers.Win | Modifiers.Ctrl | Modifiers.Alt;
    public const Modifiers WAS = WA | Modifiers.Shift;
    public const Modifiers CAS = CA | Modifiers.Shift;

    public static KeyCombo K(Key key, Modifiers m) => new(key, m);
    public static ShortcutBindings WinAlt => ShortcutProfiles.WinAlt;
    public static ShortcutBindings CtrlAlt => ShortcutProfiles.CtrlAlt;
    public static IEnumerable<ShortcutAction> AllActions => Enum.GetValues<ShortcutAction>();
    public static Key D(int n) => ShortcutRules.SlotKey(n);

    public static void AssertSameExcept(ShortcutBindings a, ShortcutBindings b, params ShortcutAction[] except)
    {
        foreach (var act in AllActions)
        {
            if (except.Contains(act)) continue;
            Assert.Equal(a[act], b[act]);
        }
        Assert.Equal(a.RecallViewModifiers, b.RecallViewModifiers);
        Assert.Equal(a.SaveViewModifiers, b.SaveViewModifiers);
    }
}
