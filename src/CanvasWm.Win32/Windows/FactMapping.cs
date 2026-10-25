using CanvasWm.Core.Windows;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Interop;

namespace CanvasWm.Win32.Windows;

/// <summary>Decoded window style bits.</summary>
internal readonly record struct StyleFacts(bool IsChild, bool HasCaption, bool IsResizable,
    bool HasMaximizeButton, bool IsAppWindow, bool IsToolWindow, bool IsNoActivate);

/// <summary>Pure mappings from raw OS values to fact values.</summary>
internal static class FactMapping
{
    /// <summary>B6: decodes style and extended style bits.</summary>
    internal static StyleFacts DecodeStyles(long style, long exStyle) => new(
        IsChild: (style & NativeConstants.WS_CHILD) != 0,
        HasCaption: (style & NativeConstants.WS_CAPTION) == NativeConstants.WS_CAPTION,
        IsResizable: (style & NativeConstants.WS_THICKFRAME) != 0,
        HasMaximizeButton: (style & NativeConstants.WS_MAXIMIZEBOX) != 0,
        IsAppWindow: (exStyle & NativeConstants.WS_EX_APPWINDOW) != 0,
        IsToolWindow: (exStyle & NativeConstants.WS_EX_TOOLWINDOW) != 0,
        IsNoActivate: (exStyle & NativeConstants.WS_EX_NOACTIVATE) != 0);

    /// <summary>B7: cloak bits to kind. Shell wins over App, App over Inherited.</summary>
    internal static CloakKind ToCloakKind(bool readOk, uint cloaked)
    {
        if (!readOk || cloaked == 0)
        {
            return CloakKind.None;
        }

        if ((cloaked & NativeConstants.DWM_CLOAKED_SHELL) != 0)
        {
            return CloakKind.Shell;
        }

        if ((cloaked & NativeConstants.DWM_CLOAKED_APP) != 0)
        {
            return CloakKind.App;
        }

        if ((cloaked & NativeConstants.DWM_CLOAKED_INHERITED) != 0)
        {
            return CloakKind.Inherited;
        }

        // SPEC-ASSUMPTION A10: unknown non-zero bits map to App (temporary removal in the tracker).
        return CloakKind.App;
    }

    /// <summary>B8: maximize button state to tri-state.</summary>
    internal static bool? MaximizeButtonEnabled(bool readOk, uint maxButtonState)
    {
        if (!readOk || (maxButtonState & NativeConstants.STATE_SYSTEM_INVISIBLE) != 0)
        {
            return null;
        }

        return (maxButtonState & NativeConstants.STATE_SYSTEM_UNAVAILABLE) == 0;
    }

    /// <summary>B9: the window covers its monitor and has no caption.</summary>
    internal static bool IsFullScreen(PixelRect window, PixelRect monitor, bool hasCaption)
    {
        // SPEC-ASSUMPTION A18: full screen means "covers its monitor" with no caption; the zoomed
        // state is ignored because browsers keep WS_MAXIMIZE in F11 entered from a maximised window.
        return !hasCaption
            && window.Left <= monitor.Left && window.Top <= monitor.Top
            && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }

    /// <summary>B6: true iff the class is one of the four shell class names (ordinal).</summary>
    internal static bool IsShellClass(string className) =>
        className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    /// <summary>B11: lower-cases image paths and AUMIDs alike.</summary>
    internal static string NormalizeAppId(string raw)
    {
        // SPEC-ASSUMPTION A3: AUMIDs are lower-cased too.
        return raw.ToLowerInvariant();
    }

    /// <summary>B12: UIPI accessibility by integrity-level comparison.</summary>
    internal static bool IsAccessible(bool isOwnProcess, int? targetIntegrityRid, int ownIntegrityRid)
    {
        // SPEC-ASSUMPTION A7: unreadable level is inaccessible; accessible iff target RID <= own RID.
        if (isOwnProcess)
        {
            return true;
        }

        return targetIntegrityRid is { } rid && rid <= ownIntegrityRid;
    }

    /// <summary>B11: file description if non-blank (trimmed), else file name without extension.</summary>
    internal static string? AppNameFrom(string? fileDescription, string? imagePath)
    {
        if (!string.IsNullOrWhiteSpace(fileDescription))
        {
            return fileDescription.Trim();
        }

        if (!string.IsNullOrEmpty(imagePath))
        {
            string name = Path.GetFileNameWithoutExtension(imagePath);
            if (name.Length > 0)
            {
                return name;
            }
        }

        return null;
    }
}
