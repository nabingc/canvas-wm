using CanvasWm.Core.Shortcuts;
using CanvasWm.Win32.Hotkeys;
using static CanvasWm.Win32.Tests.HotkeysSpec.HotkeyRig;

namespace CanvasWm.Win32.Tests.HotkeysSpec;

/// <summary>AC18-AC26 of specs/win32-hotkeys.md: real RegisterHotKey calls on message-only windows.
/// Needs an interactive Windows session; SendInput is used by AC24 only.</summary>
public sealed class RealRegistrationTests
{
    private static HashSet<KeyCombo> Set(IEnumerable<KeyCombo> c) => [.. c];

    private static int CountClassWindows()
    {
        int n = 0;
        nint h = 0;
        while ((h = HotkeyNative.FindWindowExW(HotkeyNative.HWND_MESSAGE, h, "CanvasWm.HotkeyWindow", null)) != 0) { n++; }
        return n;
    }

    [Fact]
    public void AC18_Construction()
    {
        using var rig = new HotkeyRig();
        nint hwnd = rig.Source.WindowHandle;
        Assert.NotEqual(0, hwnd);
        Assert.True(HotkeyNative.IsWindow(hwnd));
        bool found = false;
        nint h = 0;
        while ((h = HotkeyNative.FindWindowExW(HotkeyNative.HWND_MESSAGE, h, "CanvasWm.HotkeyWindow", null)) != 0)
        {
            found |= h == hwnd;
        }

        Assert.True(found, "own window not found under HWND_MESSAGE");
        Assert.Equal("CanvasWm.HotkeyWindow", Win32HotkeySourceNames.WindowClass);

        var second = rig.L.Invoke(() => new Win32HotkeySource(rig.L.Post));
        Assert.NotEqual(0, second.WindowHandle);
        Assert.NotEqual(hwnd, second.WindowHandle);
        rig.L.Invoke(() => second.Dispose());

        int before = CountClassWindows();
        Assert.Throws<ArgumentNullException>(() => new Win32HotkeySource(null!));
        Assert.Equal(before, CountClassWindows());
    }

    [Fact]
    public void AC19_Register_and_unregister()
    {
        using var rig = new HotkeyRig();
        var r = rig.Apply(T1, T2);
        Assert.Equal([new HotkeyReport(T1, HotkeyStatus.Registered, 0), new HotkeyReport(T2, HotkeyStatus.Registered, 0)], r);
        Assert.True(rig.IsHeld(T1));
        Assert.True(rig.IsHeld(T2));

        r = rig.Apply(T2);
        Assert.Equal([new HotkeyReport(T2, HotkeyStatus.Registered, 0)], r);
        Assert.True(rig.IsFree(T1));
        Assert.True(rig.IsHeld(T2));
        Assert.Equal(Set([T2]), Set(rig.Source.RegisteredCombos));

        Assert.Empty(rig.Apply());
        Assert.True(rig.IsFree(T1));
        Assert.True(rig.IsFree(T2));
    }

    [Fact]
    public void AC20_Taken_and_retry()
    {
        using var rig = new HotkeyRig();
        rig.HLoop.Invoke(() => rig.H.Apply([T1]));
        var r = rig.Apply(T1, T2);
        Assert.Equal([new HotkeyReport(T1, HotkeyStatus.Taken, 1409), new HotkeyReport(T2, HotkeyStatus.Registered, 0)], r);
        Assert.Equal(Set([T2]), Set(rig.Source.RegisteredCombos));

        rig.HLoop.Invoke(() => rig.H.Apply([]));
        r = rig.Apply(T1, T2);
        Assert.Equal([new HotkeyReport(T1, HotkeyStatus.Registered, 0), new HotkeyReport(T2, HotkeyStatus.Registered, 0)], r);
        Assert.True(rig.IsHeld(T1));
    }

    [Fact]
    public void AC21_Apply_errors_leave_state_unchanged()
    {
        using var rig = new HotkeyRig();
        rig.Apply(T2);

        void Unchanged()
        {
            Assert.Equal(Set([T2]), Set(rig.Source.RegisteredCombos));
            Assert.True(rig.IsFree(T1));
        }

        Assert.Throws<InvalidOperationException>(() => rig.Source.Apply([T1])); // test thread
        Unchanged();
        Assert.Throws<ArgumentNullException>(() => rig.L.Invoke(() => rig.Source.Apply(null!)));
        Unchanged();
        Assert.Throws<ArgumentOutOfRangeException>(() => rig.L.Invoke(() => rig.Source.Apply([T1, default])));
        Unchanged();
        Assert.Throws<ArgumentException>(() => rig.L.Invoke(() => rig.Source.Apply([T1, new KeyCombo(Key.F24, Modifiers.Shift)])));
        Unchanged();

        var esc = new KeyCombo(Key.Escape, Modifiers.Ctrl | Modifiers.Shift | Modifiers.Alt);
        var accepted = rig.Apply(esc);
        Assert.Single(accepted);
        Assert.Equal(esc, accepted[0].Combo);
        Assert.Empty(rig.Apply());

        rig.L.Invoke(() => rig.Source.Dispose());
        Assert.Throws<ObjectDisposedException>(() => rig.L.Invoke(() => rig.Source.Apply([T1])));
    }

    [Fact]
    public void AC22_Duplicates_and_order()
    {
        using var rig = new HotkeyRig();
        var r = rig.Apply(T2, T1, T2);
        Assert.Equal([new HotkeyReport(T2, HotkeyStatus.Registered, 0), new HotkeyReport(T1, HotkeyStatus.Registered, 0)], r);
        Assert.Equal(Set([T1, T2]), Set(rig.Source.RegisteredCombos));

        r = rig.Apply(T1, T2);
        Assert.Equal([new HotkeyReport(T1, HotkeyStatus.Registered, 0), new HotkeyReport(T2, HotkeyStatus.Registered, 0)], r);
        Assert.True(rig.IsHeld(T1));
        Assert.True(rig.IsHeld(T2));
    }

    private static void PostHotkey(nint hwnd, int id, int mods, int vk)
        => Assert.True(HotkeyNative.PostMessageW(hwnd, HotkeyNative.WM_HOTKEY, unchecked((nuint)(nint)id), (nint)((vk << 16) | mods)));

    [Fact]
    public void AC23_Posted_WM_HOTKEY()
    {
        var mp = new ManualPost();
        using var rig = new HotkeyRig(manual: mp);
        var got = new List<(KeyCombo C, int Thread)>();
        rig.L.Invoke(() => rig.Source.Pressed += c => { lock (got) { got.Add((c, Environment.CurrentManagedThreadId)); } });
        rig.Apply(T1);
        nint hwnd = rig.Source.WindowHandle;
        int t1Id = (int)T1.Key * 16 + (int)T1.Modifiers;

        PostHotkey(hwnd, t1Id, 0xF, 0x87);
        Thread.Sleep(200);
        lock (got) { Assert.Empty(got); }
        Assert.Equal(1, mp.Count);
        rig.L.Invoke(() => mp[0]());
        lock (got)
        {
            Assert.Single(got);
            Assert.Equal(T1, got[0].C);
            Assert.Equal(rig.L.ManagedThreadId, got[0].Thread);
        }

        int before = mp.Count;
        PostHotkey(hwnd, t1Id, 0x7, 0x87);
        PostHotkey(hwnd, -1, 0, 0);
        PostHotkey(hwnd, (int)T2.Key * 16 + (int)T2.Modifiers, 0x7, 0x85);
        Thread.Sleep(200);
        Assert.Equal(before, mp.Count);

        rig.Apply();
        PostHotkey(hwnd, t1Id, 0xF, 0x87);
        Thread.Sleep(200);
        Assert.Equal(before, mp.Count);
    }

    private static void Combo(ushort[] mods, ushort vk, int repeats)
    {
        foreach (ushort m in mods) { HotkeyNative.Key(m, false); }
        HotkeyNative.Key(vk, false);
        for (int i = 0; i < repeats; i++)
        {
            Thread.Sleep(30);
            HotkeyNative.Key(vk, false);
        }

        HotkeyNative.Key(vk, true);
        foreach (ushort m in mods.Reverse()) { HotkeyNative.Key(m, true); }
    }

    [Fact]
    public void AC24_Real_input_fires_once_despite_repeat()
    {
        using var rig = new HotkeyRig();
        var got = new List<KeyCombo>();
        rig.L.Invoke(() => rig.Source.Pressed += c => { lock (got) { got.Add(c); } });
        rig.Apply(T2);
        try
        {
            try
            {
                Combo([HotkeyNative.VK_CONTROL, HotkeyNative.VK_MENU, HotkeyNative.VK_SHIFT], 0x85, 10);
            }
            finally
            {
                // Make sure nothing stays logically down if SendInput failed half way.
                foreach (ushort k in new ushort[] { 0x85, HotkeyNative.VK_SHIFT, HotkeyNative.VK_MENU, HotkeyNative.VK_CONTROL })
                {
                    try { HotkeyNative.Key(k, true); } catch { /* ignore */ }
                }
            }

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline)
            {
                lock (got) { if (got.Count > 0) { break; } }
                Thread.Sleep(20);
            }

            Thread.Sleep(400); // room for any (wrong) repeat to arrive
            lock (got) { Assert.Equal([T2], got); }
        }
        finally
        {
            rig.Apply();
        }
    }

    [Fact]
    public void AC25a_Dispose_on_owner_thread()
    {
        using var rig = new HotkeyRig();
        rig.Apply(T1, T2);
        nint old = rig.Source.WindowHandle;
        rig.L.Invoke(() => rig.Source.Dispose());
        Assert.True(rig.IsFree(T1));
        Assert.True(rig.IsFree(T2));
        Assert.False(HotkeyNative.IsWindow(old));
        Assert.Equal(0, rig.Source.WindowHandle);
        rig.L.Invoke(() => rig.Source.Dispose());
        Assert.Equal(0, rig.Source.WindowHandle);
    }

    [Fact]
    public void AC25b_Dispose_off_thread_posts_cleanup()
    {
        var mp = new ManualPost();
        using var rig = new HotkeyRig(manual: mp);
        var got = new List<KeyCombo>();
        rig.L.Invoke(() => rig.Source.Pressed += c => { lock (got) { got.Add(c); } });
        rig.Apply(T1);
        nint old = rig.Source.WindowHandle;
        PostHotkey(old, (int)T1.Key * 16 + (int)T1.Modifiers, 0xF, 0x87);
        Thread.Sleep(200);
        Assert.Equal(1, mp.Count); // the drain

        rig.Source.Dispose(); // test thread, not the owner
        Assert.Equal(2, mp.Count); // plus the cleanup
        Assert.True(rig.IsHeld(T1));
        Assert.True(HotkeyNative.IsWindow(old));

        rig.L.Invoke(() => mp[0]()); // earlier drain: nothing may be raised
        lock (got) { Assert.Empty(got); }
        rig.L.Invoke(() => mp[1]());
        Assert.True(rig.IsFree(T1));
        Assert.False(HotkeyNative.IsWindow(old));
        Assert.Equal(0, rig.Source.WindowHandle);
    }

    [Fact]
    public void AC26_Labels()
    {
        foreach (Key k in Enum.GetValues<Key>())
        {
            Assert.False(string.IsNullOrEmpty(Win32KeyInput.KeyLabel(k)), k.ToString());
        }

        int n = HotkeyNative.GetKeyboardLayoutList(0, null);
        var list = new nint[Math.Max(n, 0)];
        if (n > 0) { HotkeyNative.GetKeyboardLayoutList(n, list); }
        nint us = list.FirstOrDefault(h => ((long)h & 0xFFFF) == 0x0409 && (((long)h >> 16) & 0xFFFF) == 0x0409);
        if (us == 0)
        {
            Console.WriteLine("US layout not installed");
            return;
        }

        foreach (Key k in Enum.GetValues<Key>())
        {
            Assert.Equal(KeyMap.DefaultLabel(k), Win32KeyInput.KeyLabel(k, us));
            Assert.True(Win32KeyInput.IsOnLayout(k, us), k.ToString());
        }
    }
}

internal static class Win32HotkeySourceNames
{
    internal const string WindowClass = Win32HotkeySource.WindowClassName;
}
