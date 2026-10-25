namespace CanvasWm.Win32.Interop;

/// <summary>Win32 constants used by the adapter.</summary>
internal static class NativeConstants
{
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;

    internal const long WS_CHILD = 0x40000000;
    internal const long WS_CAPTION = 0x00C00000;
    internal const long WS_THICKFRAME = 0x00040000;
    internal const long WS_MAXIMIZEBOX = 0x00010000;
    internal const long WS_EX_TOOLWINDOW = 0x00000080;
    internal const long WS_EX_APPWINDOW = 0x00040000;
    internal const long WS_EX_NOACTIVATE = 0x08000000;

    internal const uint GW_OWNER = 4;
    internal const uint GA_ROOT = 2;
    internal const uint MONITOR_DEFAULTTONEAREST = 2;
    internal const int MDT_EFFECTIVE_DPI = 0;

    internal const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const uint DWMWA_CLOAKED = 14;
    internal const uint DWM_CLOAKED_APP = 1;
    internal const uint DWM_CLOAKED_SHELL = 2;
    internal const uint DWM_CLOAKED_INHERITED = 4;

    internal const uint STATE_SYSTEM_UNAVAILABLE = 0x1;
    internal const uint STATE_SYSTEM_INVISIBLE = 0x8000;

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const int TokenIntegrityLevel = 25;
    internal const nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    internal const int OBJID_WINDOW = 0;
    internal const int CHILDID_SELF = 0;

    internal const int ERROR_SUCCESS = 0;
    internal const int ERROR_INSUFFICIENT_BUFFER = 122;
    internal const int APPLICATION_USER_MODEL_ID_MAX_LENGTH = 130;
    internal const int MEDIUM_INTEGRITY_RID = 0x2000;

    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    internal const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    internal const uint EVENT_OBJECT_CREATE = 0x8000;
    internal const uint EVENT_OBJECT_DESTROY = 0x8001;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_HIDE = 0x8003;
    internal const uint EVENT_OBJECT_FOCUS = 0x8005;
    internal const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    internal const uint EVENT_OBJECT_CLOAKED = 0x8017;
    internal const uint EVENT_OBJECT_UNCLOAKED = 0x8018;

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_NOOWNERZORDER = 0x0200;
    internal const int SW_SHOWNOACTIVATE = 4;
    internal const int SW_RESTORE = 9;
    internal const int SM_SWAPBUTTON = 23;
    internal const int VK_LBUTTON = 0x01;
    internal const int VK_RBUTTON = 0x02;

    // Hotkeys (#12).
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;
    internal const uint WM_HOTKEY = 0x0312;
    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_SYSKEYDOWN = 0x0104;
    internal static readonly nint HWND_MESSAGE = -3;
    internal const int IDHOT_SNAPWINDOW = -1;
    internal const int IDHOT_SNAPDESKTOP = -2;
    internal const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    internal const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    internal const uint MAPVK_VK_TO_CHAR = 2;
    internal const uint MAPVK_VK_TO_VSC_EX = 4;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12;
    internal const int VK_LWIN = 0x5B;
    internal const int VK_RWIN = 0x5C;
}
