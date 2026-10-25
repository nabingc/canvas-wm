using CanvasWm.Core.Displays;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Engine.Services;

/// <summary>One display as the platform reports it. Data only; all rects in DIPs.</summary>
public sealed record DisplayInfo
{
    private readonly DisplayKey _key = null!;
    private readonly Rect _frame;
    private readonly Rect _workArea;
    private readonly int _dpi;

    /// <summary>Creates a display snapshot entry.</summary>
    /// <param name="Key">Stable display key.</param>
    /// <param name="Frame">Full frame.</param>
    /// <param name="WorkArea">Usable area (frame minus taskbars).</param>
    /// <param name="Dpi">Effective DPI, greater than 0 (96 = 100 %). Used only in the signature.</param>
    /// <param name="IsMain">True for the primary display.</param>
    /// <exception cref="ArgumentNullException">Key is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Frame or WorkArea has a non-finite component
    /// or a negative size; Dpi is 0 or negative.</exception>
    public DisplayInfo(DisplayKey Key, Rect Frame, Rect WorkArea, int Dpi, bool IsMain)
    {
        this.Key = Key;
        this.Frame = Frame;
        this.WorkArea = WorkArea;
        this.Dpi = Dpi;
        this.IsMain = IsMain;
    }

    /// <summary>True for the primary display.</summary>
    public bool IsMain { get; init; }

    /// <summary>Stable display key.</summary>
    /// <exception cref="ArgumentNullException">Value is null.</exception>
    public DisplayKey Key
    {
        get => _key;
        init => _key = value ?? throw new ArgumentNullException(nameof(Key));
    }

    /// <summary>Full frame.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Non-finite component or negative size.</exception>
    public Rect Frame
    {
        get => _frame;
        init => _frame = CheckRect(value, nameof(Frame));
    }

    /// <summary>Usable area.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Non-finite component or negative size.</exception>
    public Rect WorkArea
    {
        get => _workArea;
        init => _workArea = CheckRect(value, nameof(WorkArea));
    }

    /// <summary>Effective DPI.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is 0 or negative.</exception>
    public int Dpi
    {
        get => _dpi;
        init => _dpi = value > 0 ? value : throw new ArgumentOutOfRangeException(nameof(Dpi), value, "Dpi must be positive.");
    }

    /// <summary>DisplaySignature.Of over (Key.Value, Frame, Dpi) of each display (B2).</summary>
    /// <param name="displays">The displays.</param>
    /// <returns>The signature text.</returns>
    /// <exception cref="ArgumentNullException">displays or an element is null.</exception>
    public static string SignatureOf(IEnumerable<DisplayInfo> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        var entries = new List<DisplaySignatureEntry>();
        foreach (DisplayInfo d in displays)
        {
            ArgumentNullException.ThrowIfNull(d, nameof(displays));
            entries.Add(new DisplaySignatureEntry(d.Key.Value, d.Frame, d.Dpi));
        }

        return DisplaySignature.Of(entries);
    }

    private static Rect CheckRect(Rect r, string name)
    {
        if (!double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.Width)
            || !double.IsFinite(r.Height) || r.Width < 0 || r.Height < 0)
        {
            throw new ArgumentOutOfRangeException(name, r, "Rect must be finite with non-negative size.");
        }

        return r;
    }
}
