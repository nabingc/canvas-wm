using System.Diagnostics.CodeAnalysis;

namespace CanvasWm.Engine.Services;

/// <summary>Opaque identity of a managed top-level window. Compared ordinally and case-sensitively; the Engine never parses it.</summary>
public sealed record WindowKey
{
    /// <summary>Creates a key.</summary>
    /// <param name="value">The opaque value.</param>
    /// <exception cref="ArgumentNullException">value is null.</exception>
    /// <exception cref="ArgumentException">value is empty.</exception>
    public WindowKey(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The opaque value, compared ordinally.</summary>
    public string Value { get; }

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The value.</returns>
    public override string ToString() => Value;
}

/// <summary>Opaque identity of a physical display (for example a monitor device path).</summary>
public sealed record DisplayKey
{
    /// <summary>Creates a key.</summary>
    /// <param name="value">The opaque value.</param>
    /// <exception cref="ArgumentNullException">value is null.</exception>
    /// <exception cref="ArgumentException">value is empty.</exception>
    public DisplayKey(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The opaque value, compared ordinally.</summary>
    public string Value { get; }

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The value.</returns>
    public override string ToString() => Value;
}

/// <summary>Identity of an application. "No identity" is a null reference.</summary>
public sealed record AppId
{
    /// <summary>Creates a key.</summary>
    /// <param name="value">The opaque value.</param>
    /// <exception cref="ArgumentNullException">value is null.</exception>
    /// <exception cref="ArgumentException">value is empty.</exception>
    public AppId(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The opaque value, compared ordinally.</summary>
    public string Value { get; }

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The value.</returns>
    public override string ToString() => Value;
}

/// <summary>Identity of one engine: a display plus an OS virtual-desktop id.</summary>
public sealed record EngineKey
{
    /// <summary>Desktop id to use when per-virtual-desktop layouts are not supported.</summary>
    public const string SingleDesktopId = "default";

    /// <summary>The separator between display and desktop.</summary>
    public const char Separator = '|';

    /// <summary>Creates an engine key.</summary>
    /// <param name="display">The display.</param>
    /// <param name="desktopId">The desktop id.</param>
    /// <exception cref="ArgumentNullException">display or desktopId is null.</exception>
    /// <exception cref="ArgumentException">desktopId is empty or contains the separator.</exception>
    public EngineKey(DisplayKey display, string desktopId)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentException.ThrowIfNullOrEmpty(desktopId);
        if (desktopId.Contains(Separator))
        {
            throw new ArgumentException("Desktop id must not contain the separator.", nameof(desktopId));
        }

        Display = display;
        DesktopId = desktopId;
    }

    /// <summary>The display.</summary>
    public DisplayKey Display { get; }

    /// <summary>The desktop id.</summary>
    public string DesktopId { get; }

    /// <summary>Display.Value + "|" + DesktopId.</summary>
    public string Value => Display.Value + Separator + DesktopId;

    /// <summary>Returns <see cref="Value"/>.</summary>
    /// <returns>The value.</returns>
    public override string ToString() => Value;

    /// <summary>Splits at the last separator. False for null, no separator, or an empty part.</summary>
    /// <param name="value">The wire text.</param>
    /// <param name="key">The parsed key on success.</param>
    /// <returns>True on success.</returns>
    public static bool TryParse(string? value, [NotNullWhen(true)] out EngineKey? key)
    {
        key = null;
        if (value is null) return false;
        int i = value.LastIndexOf(Separator);
        if (i <= 0 || i == value.Length - 1) return false;
        key = new EngineKey(new DisplayKey(value[..i]), value[(i + 1)..]);
        return true;
    }
}
