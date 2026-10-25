using System.Reflection;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Win32.Hotkeys;
using CanvasWm.Win32.Tests.WindowEnumSpec;

namespace CanvasWm.Win32.Tests.HotkeysSpec;

/// <summary>AC10-AC17 of specs/win32-hotkeys.md: pure helpers, no window needed.</summary>
public sealed class PureHelperTests
{
    private const Modifiers WA = Modifiers.Win | Modifiers.Alt;
    private const Modifiers CA = Modifiers.Ctrl | Modifiers.Alt;
    private const Modifiers WCA = Modifiers.Win | Modifiers.Ctrl | Modifiers.Alt;
    private const Modifiers All = Modifiers.All;

    private static readonly KeyCombo T1 = new(Key.F24, All);
    private static readonly KeyCombo T2 = new(Key.F22, Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift);

    private static KeyCombo K(Key k, Modifiers m) => new(k, m);
    private static nint LP(int mods, int vk) => (nint)((vk << 16) | mods);

    // Independent restatement of table B20.
    private static int ExpectedVk(Key k)
    {
        string n = k.ToString();
        if (n.Length == 1) { return n[0]; }
        if (n.Length == 2 && n[0] == 'D') { return 0x30 + (n[1] - '0'); }
        if (n[0] == 'F' && int.TryParse(n.AsSpan(1), out int f)) { return 0x70 + f - 1; }
        return k switch
        {
            Key.Left => 0x25, Key.Up => 0x26, Key.Right => 0x27, Key.Down => 0x28,
            Key.Space => 0x20, Key.Enter => 0x0D, Key.Tab => 0x09, Key.Backspace => 0x08, Key.Delete => 0x2E, Key.Escape => 0x1B,
            Key.Insert => 0x2D, Key.Home => 0x24, Key.End => 0x23, Key.PageUp => 0x21, Key.PageDown => 0x22,
            Key.Minus => 0xBD, Key.Equals => 0xBB, Key.LeftBracket => 0xDB, Key.RightBracket => 0xDD, Key.Backslash => 0xDC,
            Key.Semicolon => 0xBA, Key.Quote => 0xDE, Key.Comma => 0xBC, Key.Period => 0xBE, Key.Slash => 0xBF, Key.Backquote => 0xC0,
            _ => throw new InvalidOperationException("no expectation for " + k),
        };
    }

    private static readonly Key[] Punct =
    [
        Key.Minus, Key.Equals, Key.LeftBracket, Key.RightBracket, Key.Backslash, Key.Semicolon,
        Key.Quote, Key.Comma, Key.Period, Key.Slash, Key.Backquote,
    ];

    [Fact]
    public void AC10_KeyMap()
    {
        Key[] keys = Enum.GetValues<Key>();
        Assert.Equal(86, keys.Length);
        Assert.Equal(86, KeyMap.Count);
        foreach (Key k in keys)
        {
            Assert.Equal(ExpectedVk(k), KeyMap.ToVk(k));
            Assert.True(KeyMap.TryFromVk(KeyMap.ToVk(k), out Key back));
            Assert.Equal(k, back);
            Assert.Equal(Punct.Contains(k), KeyMap.IsLayoutAssigned(k));
        }

        Assert.Equal(86, keys.Select(KeyMap.ToVk).Distinct().Count());
        foreach (int vk in new[] { 0x60, 0x69, 0x0C, 0xE2, 0x2C, 0x10, 0x11, 0x12, 0xA0, 0xA5, 0x5B, 0x5C, 0xE5, 0xE7, 0x00, 0xFF, -1, 256 })
        {
            Assert.False(KeyMap.TryFromVk(vk, out _), "vk " + vk);
        }

        Assert.Equal("A", KeyMap.DefaultLabel(Key.A));
        Assert.Equal("7", KeyMap.DefaultLabel(Key.D7));
        Assert.Equal("F13", KeyMap.DefaultLabel(Key.F13));
        Assert.Equal("PageDown", KeyMap.DefaultLabel(Key.PageDown));
        Assert.Equal("Space", KeyMap.DefaultLabel(Key.Space));
        Assert.Equal("\\", KeyMap.DefaultLabel(Key.Backslash));
        Assert.Equal("'", KeyMap.DefaultLabel(Key.Quote));
        Assert.Equal("`", KeyMap.DefaultLabel(Key.Backquote));
        Assert.Throws<ArgumentOutOfRangeException>(() => KeyMap.ToVk((Key)0));
    }

    [Fact]
    public void AC11_ModMap()
    {
        Assert.Equal(0x2u, ModMap.ToModFlags(Modifiers.Ctrl));
        Assert.Equal(0x1u, ModMap.ToModFlags(Modifiers.Alt));
        Assert.Equal(0x4u, ModMap.ToModFlags(Modifiers.Shift));
        Assert.Equal(0x8u, ModMap.ToModFlags(Modifiers.Win));
        Assert.Equal(0xFu, ModMap.ToModFlags(All));
        Assert.Equal(0u, ModMap.ToModFlags(Modifiers.None));
        for (int i = 0; i < 16; i++)
        {
            var m = (Modifiers)i;
            Assert.Equal(m, ModMap.FromModFlags(ModMap.ToModFlags(m)));
        }

        Assert.Equal(0x4003u, ModMap.RegisterFlags(CA));
        Assert.Equal(0x4009u, ModMap.RegisterFlags(WA));
        Assert.Equal(CA, ModMap.FromModFlags(0x4003));
        Assert.Equal(Modifiers.None, ModMap.FromModFlags(0xFFF0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ModMap.ToModFlags((Modifiers)16));
    }

    [Fact]
    public void AC12_HotkeyIds()
    {
        Assert.Equal(67, HotkeyIds.For(K(Key.D, CA)));
        Assert.Equal(16, HotkeyIds.For(K(Key.A, Modifiers.None)));
        Assert.Equal(1775, HotkeyIds.For(K(Key.Backquote, All)));
        var ids = new HashSet<int>();
        foreach (Key k in Enum.GetValues<Key>())
        {
            for (int m = 0; m < 16; m++)
            {
                var c = K(k, (Modifiers)m);
                int id = HotkeyIds.For(c);
                Assert.True(ids.Add(id));
                Assert.InRange(id, 16, 1775);
                Assert.True(id <= HotkeyIds.MaxApplicationId);
                Assert.True(HotkeyIds.TryDecode(id, out KeyCombo back));
                Assert.Equal(c, back);
            }
        }

        Assert.Equal(86 * 16, ids.Count);
        foreach (int bad in new[] { -1, 0, 15, (27 * 16) + 3, 1776 })
        {
            Assert.False(HotkeyIds.TryDecode(bad, out _), "id " + bad);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => HotkeyIds.For(default));
    }

    [Theory]
    [InlineData(true, 0, true, HotkeyStatus.Registered, 0)]
    [InlineData(true, 5, true, HotkeyStatus.Registered, 0)]
    [InlineData(true, 1409, false, HotkeyStatus.NotOnLayout, 0)]
    [InlineData(true, 5, false, HotkeyStatus.NotOnLayout, 0)]
    [InlineData(false, 1409, true, HotkeyStatus.Taken, 1409)]
    [InlineData(false, 1409, false, HotkeyStatus.Taken, 1409)]
    [InlineData(false, 5, true, HotkeyStatus.Failed, 5)]
    [InlineData(false, 5, false, HotkeyStatus.Failed, 5)]
    [InlineData(false, 0, true, HotkeyStatus.Failed, 0)]
    [InlineData(false, 0, false, HotkeyStatus.Failed, 0)]
    public void AC13_ReportFor(bool registered, int err, bool onLayout, HotkeyStatus status, int code)
    {
        Assert.Equal(new HotkeyReport(T1, status, code), HotkeyMessages.ReportFor(T1, registered, err, onLayout));
    }

    [Fact]
    public void AC14_DecodeHotkey()
    {
        var map = new Dictionary<int, KeyCombo> { [HotkeyIds.For(T1)] = T1 };
        nint id = HotkeyIds.For(T1);
        Assert.Equal(T1, HotkeyMessages.DecodeHotkey(id, LP(0xF, 0x87), map));
        Assert.Equal(T1, HotkeyMessages.DecodeHotkey(id, LP(0x400F, 0x87), map));
        Assert.Null(HotkeyMessages.DecodeHotkey(id, LP(0x7, 0x87), map));
        Assert.Null(HotkeyMessages.DecodeHotkey(id, LP(0xF, 0x86), map));
        Assert.Null(HotkeyMessages.DecodeHotkey(-1, LP(0xF, 0x87), map));
        Assert.Null(HotkeyMessages.DecodeHotkey(-2, LP(0xF, 0x87), map));
        Assert.Null(HotkeyMessages.DecodeHotkey(HotkeyIds.For(T2), LP(0x7, 0x85), map));
        Assert.Null(HotkeyMessages.DecodeHotkey(0xC000, LP(0xF, 0x87), map));
    }

    [Fact]
    public void AC15a_TranslateKeyMessage()
    {
        const uint KD = 0x0100, SKD = 0x0104;
        Assert.Equal(K(Key.O, WA), HotkeyMessages.TranslateKeyMessage(SKD, 'O', 0, false, true, false, true));
        Assert.Equal(K(Key.LeftBracket, CA), HotkeyMessages.TranslateKeyMessage(KD, 0xDB, 0, true, true, false, false));
        Assert.Equal(K(Key.Escape, Modifiers.None), HotkeyMessages.TranslateKeyMessage(KD, 0x1B, 0, false, false, false, false));
        Assert.Equal(K(Key.Backspace, Modifiers.None), HotkeyMessages.TranslateKeyMessage(KD, 0x08, 0, false, false, false, false));
        Assert.Equal(K(Key.F10, Modifiers.None), HotkeyMessages.TranslateKeyMessage(SKD, 0x79, 0, false, false, false, false));
        Assert.Equal(K(Key.Enter, Modifiers.None), HotkeyMessages.TranslateKeyMessage(KD, 0x0D, 1 << 24, false, false, false, false));
        Assert.Null(HotkeyMessages.TranslateKeyMessage(KD, 'O', 1 << 30, true, false, false, false));
        Assert.Null(HotkeyMessages.TranslateKeyMessage(0x0101, 'O', 0, true, false, false, false));
        Assert.Null(HotkeyMessages.TranslateKeyMessage(0x0102, 'O', 0, true, false, false, false));
        Assert.Null(HotkeyMessages.TranslateKeyMessage(KD, 0x12, 0, false, true, false, false));
        Assert.Null(HotkeyMessages.TranslateKeyMessage(KD, 0x61, 0, false, false, false, false));
        Assert.Null(HotkeyMessages.TranslateKeyMessage(KD, unchecked((nint)0x1_0000_0041), 0, false, false, false, false));
    }

    [Fact]
    public void AC15b_ReadKeyMessage_UsesKeyState()
    {
        using var loop = new MessageLoopThread();
        loop.Invoke(() =>
        {
            var saved = new byte[256];
            Assert.True(HotkeyNative.GetKeyboardState(saved));
            try
            {
                var s = new byte[256];
                s[HotkeyNative.VK_CONTROL] = 0x80;
                s[HotkeyNative.VK_MENU] = 0x80;
                s[HotkeyNative.VK_LWIN] = 0x80;
                Assert.True(HotkeyNative.SetKeyboardState(s));
                Assert.Equal(K(Key.D, WCA), Win32KeyInput.ReadKeyMessage(HotkeyNative.WM_SYSKEYDOWN, 'D', 0));

                s = new byte[256];
                s[HotkeyNative.VK_RWIN] = 0x80;
                s[HotkeyNative.VK_SHIFT] = 0x80;
                Assert.True(HotkeyNative.SetKeyboardState(s));
                Assert.Equal(K(Key.D, Modifiers.Win | Modifiers.Shift), Win32KeyInput.ReadKeyMessage(HotkeyNative.WM_SYSKEYDOWN, 'D', 0));
            }
            finally
            {
                HotkeyNative.SetKeyboardState(saved);
            }
        });
    }

    private sealed class ManualPost
    {
        internal readonly List<Action> P = [];
        internal void Post(Action a) => P.Add(a);
    }

    [Fact]
    public void AC16a_OnePostThenOrderedDelivery()
    {
        var mp = new ManualPost();
        var got = new List<KeyCombo>();
        var q = new PressQueue(mp.Post, got.Add);
        q.Enqueue(T1);
        q.Enqueue(T2);
        Assert.Single(mp.P);
        Assert.Empty(got);
        mp.P[0]();
        Assert.Equal([T1, T2], got);
    }

    [Fact]
    public void AC16b_EnqueueDuringDeliver_SameDrain()
    {
        var mp = new ManualPost();
        var got = new List<KeyCombo>();
        PressQueue? q = null;
        var t3 = K(Key.F21, Modifiers.Ctrl | Modifiers.Alt | Modifiers.Shift);
        q = new PressQueue(mp.Post, c =>
        {
            got.Add(c);
            if (c == T1) { q!.Enqueue(t3); }
        });
        q.Enqueue(T1);
        q.Enqueue(T2);
        mp.P[0]();
        Assert.Equal([T1, T2, t3], got);
        Assert.Single(mp.P);
    }

    [Fact]
    public void AC16c_NestedDrain_DeliversNothing()
    {
        var mp = new ManualPost();
        var got = new List<KeyCombo>();
        int depth = 0, maxDepth = 0;
        PressQueue? q = null;
        q = new PressQueue(mp.Post, c =>
        {
            depth++;
            maxDepth = Math.Max(maxDepth, depth);
            got.Add(c);
            if (c == T1)
            {
                q!.Enqueue(T2); // no new post: a drain is running
                Assert.Single(mp.P);
                mp.P[0](); // nested drain by running the posted action again
            }

            depth--;
        });
        q.Enqueue(T1);
        mp.P[0]();
        Assert.Equal([T1, T2], got);
        Assert.Equal(1, maxDepth);
    }

    [Fact]
    public void AC16d_InlinePost_Throws_PressStaysQueued()
    {
        var got = new List<KeyCombo>();
        PressQueue? q = null;
        q = new PressQueue(a => a(), got.Add);
        Assert.Throws<InvalidOperationException>(() => q.Enqueue(T1));
        Assert.Equal(1, q.QueuedCount);
        Assert.Empty(got);
    }

    [Fact]
    public void AC16e_DeliverThrows_ReDrainPosted()
    {
        var mp = new ManualPost();
        var got = new List<KeyCombo>();
        var q = new PressQueue(mp.Post, c =>
        {
            if (c == T1) { throw new InvalidOperationException("boom"); }
            got.Add(c);
        });
        q.Enqueue(T1);
        q.Enqueue(T2);
        Assert.Throws<InvalidOperationException>(() => mp.P[0]());
        Assert.Equal(2, mp.P.Count);
        Assert.Empty(got);
        mp.P[1]();
        Assert.Equal([T2], got);
    }

    [Fact]
    public void AC16f_Close_DropsAndStopsPosting()
    {
        var mp = new ManualPost();
        var got = new List<KeyCombo>();
        var q = new PressQueue(mp.Post, got.Add);
        q.Enqueue(T1);
        q.Close();
        mp.P[0]();
        Assert.Empty(got);
        q.Enqueue(T2);
        Assert.Single(mp.P);
        Assert.Equal(0, q.QueuedCount);
    }

    [Fact]
    public void AC17_PublicSurface()
    {
        Assembly asm = typeof(Win32HotkeySource).Assembly;
        string[] names = asm.GetExportedTypes().Select(t => t.FullName!).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(9, names.Length);
        Assert.Contains("CanvasWm.Win32.Hotkeys.Win32HotkeySource", names);
        Assert.Contains("CanvasWm.Win32.Hotkeys.Win32KeyInput", names);
        Assert.Equal(7, names.Count(n => !n.StartsWith("CanvasWm.Win32.Hotkeys.", StringComparison.Ordinal)));

        Type src = typeof(Win32HotkeySource);
        Assert.True(typeof(IDisposable).IsAssignableFrom(src));
        Assert.DoesNotContain(src.GetInterfaces(), i => i.Namespace?.StartsWith("CanvasWm.Engine", StringComparison.Ordinal) == true);
        const BindingFlags Pub = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        string[] members = src.GetMembers(Pub).Select(m => m.MemberType + ":" + m.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        string[] expected = ["Constructor:.ctor", "Event:Pressed", "Method:Apply", "Method:Dispose"];
        // add_/remove_ accessors are methods; filter them out.
        members = members.Where(m => !m.StartsWith("Method:add_", StringComparison.Ordinal)
            && !m.StartsWith("Method:remove_", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), members);

        string[] ki = typeof(Win32KeyInput).GetMembers(Pub).Select(m => m.MemberType + ":" + m.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(["Method:KeyLabel", "Method:ReadKeyMessage"], ki);
        Assert.Single(typeof(Win32KeyInput).GetMethods(Pub), m => m.Name == "KeyLabel");
    }
}
