using System.Globalization;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Displays;

/// <summary>The parts of one display that define the display set (B1).</summary>
/// <param name="Key">Stable display identity text (non-empty).</param>
/// <param name="Frame">Full frame, DIPs.</param>
/// <param name="Dpi">Effective DPI (96 = 100 %).</param>
public readonly record struct DisplaySignatureEntry(string Key, Rect Frame, int Dpi);

/// <summary>One text value that changes when a display is added, removed, moved, resized or
/// rescaled, and not when only its work area changes. Pure.</summary>
public static class DisplaySignature
{
    /// <summary>Entries sorted by Key (ordinal); each "Key@X,Y,W,H@Dpi"; joined by ";" (B1).</summary>
    /// <param name="displays">The displays.</param>
    /// <returns>The signature text.</returns>
    /// <exception cref="ArgumentNullException">displays is null.</exception>
    /// <exception cref="ArgumentException">An entry's Key is null or empty.</exception>
    public static string Of(IEnumerable<DisplaySignatureEntry> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var list = displays.ToList();
        foreach (DisplaySignatureEntry e in list)
        {
            if (string.IsNullOrEmpty(e.Key))
            {
                throw new ArgumentException("Display key must not be null or empty.", nameof(displays));
            }
        }

        // SPEC-ASSUMPTION A4: sorted by key, no escaping of '@' or ';'.
        CultureInfo c = CultureInfo.InvariantCulture;
        return string.Join(";", list
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => e.Key + "@" + e.Frame.X.ToString(c) + "," + e.Frame.Y.ToString(c) + ","
                         + e.Frame.Width.ToString(c) + "," + e.Frame.Height.ToString(c) + "@"
                         + e.Dpi.ToString(c)));
    }
}
