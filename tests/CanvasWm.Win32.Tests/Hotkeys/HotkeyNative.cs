using System.Runtime.InteropServices;

namespace CanvasWm.Win32.Tests.HotkeysSpec;

/// <summary>Test-only P/Invoke for the hotkey tests (SendInput is allowed here, never in src/).</summary>
internal static class HotkeyNative
{
    internal const uint WM_HOTKEY = 0x0312, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104;
    internal const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002;
    internal const ushort VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    internal static readonly nint HWND_MESSAGE = -3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public nint dwExtraInfo;
    }

    // INPUT on x64: type (4) + 4 padding + union (the MOUSEINPUT member is the largest, 32 bytes).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    internal struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint count, INPUT[] inputs, int size);

    [DllImport("user32.dll")] internal static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] internal static extern bool SetKeyboardState(byte[] state);
    [DllImport("user32.dll")] internal static extern int GetKeyboardLayoutList(int n, [Out] nint[]? list);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassNameW(nint hwnd, [Out] char[] buf, int max);
    [DllImport("user32.dll")] internal static extern bool PostMessageW(nint hwnd, uint msg, nuint w, nint l);

    internal static string ForegroundClass()
    {
        nint h = GetForegroundWindow();
        if (h == 0) { return ""; }
        var buf = new char[256];
        int n = GetClassNameW(h, buf, buf.Length);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    internal static void Key(ushort vk, bool up)
    {
        var inp = new INPUT[1];
        inp[0].type = INPUT_KEYBOARD;
        inp[0].ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 };
        uint sent = SendInput(1, inp, Marshal.SizeOf<INPUT>());
        if (sent != 1) { throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error()); }
    }
}

/// <summary>Extra declarations for the recorder probe window (manual tool only).</summary>
internal static class ProbeNative
{
    internal delegate nint WndProcDelegate(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public int ptX; public int ptY;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll")] internal static extern nint DefWindowProcW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] internal static extern int GetMessageW(out MSG m, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] internal static extern nint DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] internal static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint CreateWindowExW(uint exStyle, string cls, string? title, uint style, int x, int y, int w, int h,
        nint parent, nint menu, nint inst, nint param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandleW(string? name);
}
