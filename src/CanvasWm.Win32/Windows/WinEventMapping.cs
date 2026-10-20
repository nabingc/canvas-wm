using CanvasWm.Engine.Services;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Windows;

/// <summary>Maps WinEvent ids to raw event kinds and applies the delivery filter.</summary>
internal static class WinEventMapping
{
    private static readonly (uint Min, uint Max)[] Ranges =
    [
        (NativeConstants.EVENT_SYSTEM_FOREGROUND, NativeConstants.EVENT_SYSTEM_FOREGROUND),
        (NativeConstants.EVENT_SYSTEM_MINIMIZESTART, NativeConstants.EVENT_SYSTEM_MINIMIZEEND),
        (NativeConstants.EVENT_OBJECT_CREATE, NativeConstants.EVENT_OBJECT_HIDE),
        (NativeConstants.EVENT_OBJECT_FOCUS, NativeConstants.EVENT_OBJECT_FOCUS),
        (NativeConstants.EVENT_OBJECT_LOCATIONCHANGE, NativeConstants.EVENT_OBJECT_NAMECHANGE),
        (NativeConstants.EVENT_OBJECT_CLOAKED, NativeConstants.EVENT_OBJECT_UNCLOAKED),
    ];

    /// <summary>The hooked event ranges, in installation order (B14).</summary>
    internal static IReadOnlyList<(uint Min, uint Max)> HookRanges => Ranges;

    /// <summary>Kind for a hooked event id, or null for any other id.</summary>
    internal static RawWindowEventKind? KindFor(uint eventId) => eventId switch
    {
        NativeConstants.EVENT_SYSTEM_FOREGROUND => RawWindowEventKind.Foreground,
        NativeConstants.EVENT_SYSTEM_MINIMIZESTART => RawWindowEventKind.Minimized,
        NativeConstants.EVENT_SYSTEM_MINIMIZEEND => RawWindowEventKind.Restored,
        NativeConstants.EVENT_OBJECT_CREATE => RawWindowEventKind.Created,
        NativeConstants.EVENT_OBJECT_DESTROY => RawWindowEventKind.Destroyed,
        NativeConstants.EVENT_OBJECT_SHOW => RawWindowEventKind.Shown,
        NativeConstants.EVENT_OBJECT_HIDE => RawWindowEventKind.Hidden,
        NativeConstants.EVENT_OBJECT_FOCUS => RawWindowEventKind.FocusChanged,
        NativeConstants.EVENT_OBJECT_LOCATIONCHANGE => RawWindowEventKind.LocationChanged,
        NativeConstants.EVENT_OBJECT_NAMECHANGE => RawWindowEventKind.TitleChanged,
        NativeConstants.EVENT_OBJECT_CLOAKED => RawWindowEventKind.Cloaked,
        NativeConstants.EVENT_OBJECT_UNCLOAKED => RawWindowEventKind.Uncloaked,
        _ => null,
    };

    /// <summary>B15 filter + handle choice. Returns null to drop the event.</summary>
    internal static RawWindowEvent? Translate(uint eventId, nint hwnd, int idObject, int idChild,
                                              Func<nint, bool> isWindow, Func<nint, nint> rootOf)
    {
        RawWindowEventKind? kind = KindFor(eventId);
        if (kind is not { } k)
        {
            return null;
        }

        if (k is RawWindowEventKind.Foreground or RawWindowEventKind.FocusChanged)
        {
            if (hwnd == 0)
            {
                return new RawWindowEvent(k, 0);
            }

            nint root = rootOf(hwnd);
            return new RawWindowEvent(k, HandleConversion.ToHandle(root != 0 ? root : hwnd));
        }

        if (idObject != NativeConstants.OBJID_WINDOW || idChild != NativeConstants.CHILDID_SELF || hwnd == 0)
        {
            return null;
        }

        // SPEC-ASSUMPTION A11: a dead window skips the top-level check so Destroyed is not lost.
        if (isWindow(hwnd) && rootOf(hwnd) != hwnd)
        {
            return null;
        }

        return new RawWindowEvent(k, HandleConversion.ToHandle(hwnd));
    }
}
