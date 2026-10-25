using System.Globalization;

namespace CanvasWm.Core.Displays;

/// <summary>Chooses one stable, unique key per monitor (B1). Pure.</summary>
public static class DisplayKeyChoice
{
    /// <summary>One key per entry of monitorSourceNames, in the same order.</summary>
    /// <param name="monitorSourceNames">Each monitor's GDI device name, in enumeration order
    /// (null or empty when unknown).</param>
    /// <param name="paths">Active display paths in OS order.</param>
    /// <returns>The keys.</returns>
    /// <exception cref="ArgumentNullException">A list is null.</exception>
    public static IReadOnlyList<string> Assign(IReadOnlyList<string?> monitorSourceNames,
                                               IReadOnlyList<DisplayPathName> paths)
    {
        ArgumentNullException.ThrowIfNull(monitorSourceNames);
        ArgumentNullException.ThrowIfNull(paths);
        var result = new List<string>(monitorSourceNames.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < monitorSourceNames.Count; i++)
        {
            string? g = monitorSourceNames[i];
            string? candidate = null;
            if (!string.IsNullOrEmpty(g))
            {
                foreach (DisplayPathName p in paths)
                {
                    if (!string.IsNullOrEmpty(p.SourceName)
                        && string.Equals(p.SourceName, g, StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(p.DevicePath))
                    {
                        candidate = p.DevicePath;
                        break;
                    }
                }
            }

            candidate ??= !string.IsNullOrEmpty(g) ? g : "display-" + i.ToString(CultureInfo.InvariantCulture);
            string key = candidate;
            for (int n = 2; !used.Add(key); n++)
            {
                key = candidate + "#" + n.ToString(CultureInfo.InvariantCulture);
            }

            result.Add(key);
        }

        return result.AsReadOnly();
    }
}
