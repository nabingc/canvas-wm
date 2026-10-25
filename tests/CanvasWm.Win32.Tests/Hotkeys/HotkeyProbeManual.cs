using System.Diagnostics;
using System.Text;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Win32.Hotkeys;
using static CanvasWm.Win32.Tests.HotkeysSpec.HotkeyRig;

namespace CanvasWm.Win32.Tests.HotkeysSpec;

/// <summary>AC27-AC34 of specs/win32-hotkeys.md: manual probe tools. With CANVASWM_MANUAL unset every
/// test here returns at once, writes nothing and registers nothing. Output goes to
/// %TEMP%\canvaswm-hotkeys-&lt;CANVASWM_LABEL or "run"&gt;.tsv.</summary>
public sealed class HotkeyProbeManual
{
    private const Modifiers W = Modifiers.Win;
    private const Modifiers C = Modifiers.Ctrl;
    private const Modifiers WS = Modifiers.Win | Modifiers.Shift;
    private const Modifiers WCS = Modifiers.Win | Modifiers.Ctrl | Modifiers.Shift;
    private const Modifiers WCAS = Modifiers.All;
    private const int PressWindowMs = 6000;

    private static bool Enabled => Environment.GetEnvironmentVariable("CANVASWM_MANUAL") == "1";

    private static string Label
    {
        get
        {
            string? l = Environment.GetEnvironmentVariable("CANVASWM_LABEL");
            return string.IsNullOrWhiteSpace(l) ? "run" : l.Trim();
        }
    }

    private static string TsvPath => Path.Combine(Path.GetTempPath(), $"canvaswm-hotkeys-{Label}.tsv");

    private static readonly string Header =
        "Test\tGroup\tCombo\tRegisterResult\tStatus\tFired\tMs\tForegroundBefore\tForegroundAfter\tNote";

    private static string Clean(string? s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static void Write(string test, string group, string combo, string reg, string status, string fired,
        long ms, string fgBefore, string fgAfter, string note)
    {
        string path = TsvPath;
        var sb = new StringBuilder();
        if (!File.Exists(path)) { sb.AppendLine(Header); }
        sb.AppendLine(string.Join('\t', new[]
        {
            Clean(test), Clean(group), Clean(combo), Clean(reg), Clean(status), Clean(fired), ms.ToString(),
            Clean(fgBefore), Clean(fgAfter), Clean(note),
        }));
        File.AppendAllText(path, sb.ToString());
    }

    internal static string Format(KeyCombo c)
    {
        var parts = new List<string>();
        if ((c.Modifiers & Modifiers.Win) != 0) { parts.Add("Win"); }
        if ((c.Modifiers & Modifiers.Ctrl) != 0) { parts.Add("Ctrl"); }
        if ((c.Modifiers & Modifiers.Alt) != 0) { parts.Add("Alt"); }
        if ((c.Modifiers & Modifiers.Shift) != 0) { parts.Add("Shift"); }
        parts.Add(c.Key.ToString());
        return string.Join('+', parts);
    }

    private static string RegisterResult(HotkeyReport r) => r.Status switch
    {
        HotkeyStatus.Registered or HotkeyStatus.NotOnLayout => "ok",
        HotkeyStatus.Taken => "taken",
        _ => "error:" + r.ErrorCode,
    };

    private static KeyCombo K(Key k, Modifiers m) => new(k, m);

    private static List<KeyCombo> Group(string name)
    {
        switch (name)
        {
            case "nc":
                return ReservedCombos.All.Where(r => r.NeedsConfirmation && ShortcutRules.HasRequiredModifier(r.Combo.Modifiers))
                    .Select(r => r.Combo).Distinct().ToList();
            case "defaults":
                return HotkeyPlan.Desired(ShortcutDispatch.Build(ShortcutProfiles.WinAlt), false).ToList();
            case "controls":
                return [K(Key.L, W), K(Key.D, W), K(Key.Left, W), K(Key.Escape, C | Modifiers.Shift)];
            case "candidates":
                var list = new List<KeyCombo>();
                foreach (Key k in new[] { Key.W, Key.T, Key.Y, Key.O, Key.P, Key.D, Key.L, Key.X, Key.N, Key.Space })
                {
                    list.Add(K(k, WCAS));
                }

                list.Add(K(Key.F23, WS));
                list.Add(K(Key.S, WS));
                list.Add(K(Key.B, WCS));
                list.Add(K(Key.Enter, W | C));
                list.Add(K(Key.O, W | C));
                list.Add(K(Key.C, W | C));
                return list;
            default:
                throw new ArgumentException("unknown group " + name);
        }
    }

    private static readonly string[] AllGroups = ["nc", "defaults", "controls", "candidates"];

    private static string[] SelectedGroups()
    {
        string? g = Environment.GetEnvironmentVariable("CANVASWM_HOTKEY_GROUPS");
        if (string.IsNullOrWhiteSpace(g)) { return AllGroups; }
        return g.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>AC28 step 1: register everything once, write one row per combo, dispose.</summary>
    [Fact]
    public void RegisterAll()
    {
        if (!Enabled) { return; }
        var origin = new Dictionary<KeyCombo, string>();
        var all = new List<KeyCombo>();
        foreach (string g in AllGroups)
        {
            foreach (KeyCombo c in Group(g))
            {
                if (origin.TryAdd(c, g)) { all.Add(c); }
            }
        }

        using var loop = new WindowEnumSpec.MessageLoopThread();
        var src = loop.Invoke(() => new Win32HotkeySource(loop.Post));
        try
        {
            var reports = loop.Invoke(() => src.Apply(all));
            string fg = HotkeyNative.ForegroundClass();
            foreach (HotkeyReport r in reports)
            {
                Write(nameof(RegisterAll), origin[r.Combo], Format(r.Combo), RegisterResult(r), r.Status.ToString(), "skipped",
                    0, fg, fg, r.ErrorCode == 0 ? "" : "error=" + r.ErrorCode);
            }

            Console.WriteLine($"RegisterAll: {reports.Count} combos -> {TsvPath}");
        }
        finally
        {
            loop.Invoke(() => src.Dispose());
        }
    }

    /// <summary>AC28 step 2 (also AC29-AC33): prompt for each registered combo and record whether it fired.</summary>
    [Fact]
    public void PressEach()
    {
        if (!Enabled) { return; }
        foreach (string g in SelectedGroups())
        {
            List<KeyCombo> combos = Group(g);
            Console.WriteLine($"== group {g}: {combos.Count} combos ==");
            using var loop = new WindowEnumSpec.MessageLoopThread();
            var src = loop.Invoke(() => new Win32HotkeySource(loop.Post));
            var presses = new List<(KeyCombo C, long Ms)>();
            var clock = Stopwatch.StartNew();
            loop.Invoke(() => src.Pressed += c => { lock (presses) { presses.Add((c, clock.ElapsedMilliseconds)); } });
            try
            {
                var reports = loop.Invoke(() => src.Apply(combos));
                foreach (HotkeyReport r in reports)
                {
                    string reg = RegisterResult(r);
                    string fgBefore = HotkeyNative.ForegroundClass();
                    if (reg != "ok")
                    {
                        Write(nameof(PressEach), g, Format(r.Combo), reg, r.Status.ToString(), "skipped", 0, fgBefore, fgBefore,
                            "not registered; error=" + r.ErrorCode);
                        continue;
                    }

                    if (r.Combo == K(Key.L, W))
                    {
                        Write(nameof(PressEach), g, Format(r.Combo), reg, r.Status.ToString(), "skipped", 0, fgBefore, fgBefore,
                            "register only (would lock the session)");
                        continue;
                    }

                    lock (presses) { presses.Clear(); }
                    Console.WriteLine($"Press {Format(r.Combo)} now ({PressWindowMs / 1000} s; do nothing to skip)");
                    long start = clock.ElapsedMilliseconds;
                    Thread.Sleep(PressWindowMs);
                    Thread.Sleep(1000);
                    string fgAfter = HotkeyNative.ForegroundClass();
                    List<(KeyCombo C, long Ms)> mine;
                    lock (presses) { mine = presses.Where(p => p.C == r.Combo).ToList(); }
                    bool fired = mine.Count > 0;
                    string note = $"presses={mine.Count}" + (presses.Count > mine.Count ? "; other combos also fired" : "");
                    Write(nameof(PressEach), g, Format(r.Combo), reg, r.Status.ToString(), fired ? "yes" : "no",
                        fired ? mine[0].Ms - start : 0, fgBefore, fgAfter, note);
                }
            }
            finally
            {
                loop.Invoke(() => src.Dispose());
            }
        }

        Console.WriteLine("PressEach done -> " + TsvPath);
    }

    /// <summary>AC32: current layout labels and IsOnLayout for the punctuation keys and D1.</summary>
    [Fact]
    public void LayoutLabels()
    {
        if (!Enabled) { return; }
        using var loop = new WindowEnumSpec.MessageLoopThread();
        loop.Invoke(() =>
        {
            nint hkl = Interop.Hkl();
            string fg = HotkeyNative.ForegroundClass();
            foreach (Key k in new[]
            {
                Key.D1, Key.Minus, Key.Equals, Key.LeftBracket, Key.RightBracket, Key.Backslash, Key.Semicolon,
                Key.Quote, Key.Comma, Key.Period, Key.Slash, Key.Backquote,
            })
            {
                bool on = Win32KeyInput.IsOnLayout(k, hkl);
                Write(nameof(LayoutLabels), "0x" + ((long)hkl & 0xFFFFFFFF).ToString("X8"), k.ToString(), "", on ? "OnLayout" : "NotOnLayout",
                    "skipped", 0, fg, fg, "label=" + Win32KeyInput.KeyLabel(k, hkl));
            }
        });
        Console.WriteLine("LayoutLabels -> " + TsvPath);
    }

    private static class Interop
    {
        // The label thread's layout equals the process layout unless per-app input is on.
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
        internal static nint Hkl() => GetKeyboardLayout(0);
    }

    private static readonly object RecorderGate = new();
    private static readonly ProbeNative.WndProcDelegate RecorderProc = RecorderWndProc;

    private static nint RecorderWndProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case HotkeyNative.WM_KEYDOWN:
            case HotkeyNative.WM_SYSKEYDOWN:
                KeyCombo? c = Win32KeyInput.ReadKeyMessage(msg, wParam, lParam);
                string fg = HotkeyNative.ForegroundClass();
                Write("RecorderProbe", "recorder", c is KeyCombo k ? Format(k) : "(null)", "", "", "yes", 0, fg, fg,
                    $"msg=0x{msg:X4} vk=0x{(long)wParam:X2} lParam=0x{(long)lParam:X8}");
                Console.WriteLine($"recorded {(c is KeyCombo kk ? Format(kk) : "(null)")}  (vk=0x{(long)wParam:X2})");
                return 0;
            case 0x0106: // WM_SYSCHAR: swallowed so Alt combos do not open menu mode
                return 0;
            case 0x0002: // WM_DESTROY
                ProbeNative.PostQuitMessage(0);
                return 0;
            default:
                return ProbeNative.DefWindowProcW(hwnd, msg, wParam, lParam);
        }
    }

    /// <summary>AC34: a visible window that logs what ReadKeyMessage makes of each key press.</summary>
    [Fact]
    public void RecorderProbe()
    {
        if (!Enabled) { return; }
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("CANVASWM_PROBE_SECONDS"), out int s) && s > 0 ? s : 120;
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try { RunRecorder(seconds); }
            catch (Exception ex) { failure = ex; }
        }) { Name = "CanvasWm.RecorderProbe" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(seconds + 30)), "recorder window did not end");
        if (failure is not null) { throw new InvalidOperationException("recorder probe failed", failure); }
        Console.WriteLine("RecorderProbe -> " + TsvPath);
    }

    private static void RunRecorder(int seconds)
    {
        const string ClassName = "CanvasWm.RecorderProbe";
        nint inst = ProbeNative.GetModuleHandleW(null);
        lock (RecorderGate)
        {
            var wc = new ProbeNative.WNDCLASSEXW
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<ProbeNative.WNDCLASSEXW>(),
                lpfnWndProc = RecorderProc,
                hInstance = inst,
                hbrBackground = 6, // COLOR_WINDOW + 1
                lpszClassName = ClassName,
            };
            if (ProbeNative.RegisterClassExW(ref wc) == 0 && System.Runtime.InteropServices.Marshal.GetLastWin32Error() != 1410)
            {
                throw new System.ComponentModel.Win32Exception();
            }
        }

        nint hwnd = ProbeNative.CreateWindowExW(0, ClassName, "CanvasWM recorder probe - press keys, close the window to end",
            0x00CF0000 /* WS_OVERLAPPEDWINDOW */, 200, 200, 640, 200, 0, 0, inst, 0);
        if (hwnd == 0) { throw new System.ComponentModel.Win32Exception(); }
        ProbeNative.ShowWindow(hwnd, 5);
        ProbeNative.SetForegroundWindow(hwnd);
        Console.WriteLine($"Recorder window open for up to {seconds} s. Press the keys listed in the checklist, then close it.");
        var timeout = new Timer(_ => HotkeyNative.PostMessageW(hwnd, 0x0010 /* WM_CLOSE */, 0, 0), null, seconds * 1000, Timeout.Infinite);
        try
        {
            while (ProbeNative.GetMessageW(out ProbeNative.MSG m, 0, 0, 0) > 0)
            {
                ProbeNative.TranslateMessage(ref m);
                ProbeNative.DispatchMessageW(ref m);
            }
        }
        finally
        {
            timeout.Dispose();
        }
    }
}
