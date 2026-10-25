namespace CanvasWm.Engine.Hosting;

/// <summary>Tick pacing constants (B37). Immutable; every value finite and &gt; 0, the limit
/// &gt;= 1, Min &lt;= Fallback &lt;= Max, else ArgumentOutOfRangeException from the init accessor.</summary>
public sealed record TickPacingOptions
{
    private readonly double _fastFlushSeconds = 0.002;
    private readonly int _fastFlushLimit = 5;
    private readonly double _fallbackRefreshHz = 60;
    private readonly double _minRefreshHz = 20;
    private readonly double _maxRefreshHz = 1000;
    private readonly double _lateFrameFactor = 1.5;

    /// <summary>The default options.</summary>
    public static TickPacingOptions Default { get; } = new();

    /// <summary>A composition wait shorter than this is suspect, s. Default 0.002.</summary>
    public double FastFlushSeconds
    {
        get => _fastFlushSeconds;
        init => _fastFlushSeconds = Positive(value, nameof(FastFlushSeconds));
    }

    /// <summary>Consecutive suspect waits that switch to the timer. Default 5.</summary>
    public int FastFlushLimit
    {
        get => _fastFlushLimit;
        init => _fastFlushLimit = value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(FastFlushLimit), value, "Must be at least 1.");
    }

    /// <summary>Refresh rate used when none is known, Hz. Default 60.</summary>
    public double FallbackRefreshHz
    {
        get => _fallbackRefreshHz;
        init
        {
            Positive(value, nameof(FallbackRefreshHz));
            if (value < _minRefreshHz || value > _maxRefreshHz)
                throw new ArgumentOutOfRangeException(nameof(FallbackRefreshHz), value, "Must lie between Min and Max.");
            _fallbackRefreshHz = value;
        }
    }

    /// <summary>Lowest plausible refresh rate, Hz. Default 20.</summary>
    public double MinRefreshHz
    {
        get => _minRefreshHz;
        init
        {
            Positive(value, nameof(MinRefreshHz));
            if (value > _fallbackRefreshHz)
                throw new ArgumentOutOfRangeException(nameof(MinRefreshHz), value, "Must not exceed the fallback.");
            _minRefreshHz = value;
        }
    }

    /// <summary>Highest plausible refresh rate, Hz. Default 1000.</summary>
    public double MaxRefreshHz
    {
        get => _maxRefreshHz;
        init
        {
            Positive(value, nameof(MaxRefreshHz));
            if (value < _fallbackRefreshHz)
                throw new ArgumentOutOfRangeException(nameof(MaxRefreshHz), value, "Must not be below the fallback.");
            _maxRefreshHz = value;
        }
    }

    /// <summary>A frame later than this many nominal periods counts as late in the tick log
    /// (B67; late frames are only logged). Finite and at least 1. Default 1.5.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Value is not finite or is below 1.</exception>
    // SPEC-ASSUMPTION A10: the late-frame factor lives with the other tick constants (spec B67 states 1.5).
    public double LateFrameFactor
    {
        get => _lateFrameFactor;
        init => _lateFrameFactor = double.IsFinite(value) && value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(LateFrameFactor), value, "Must be finite and at least 1.");
    }

    private static double Positive(double v, string name) =>
        double.IsFinite(v) && v > 0
            ? v
            : throw new ArgumentOutOfRangeException(name, v, "Must be finite and greater than 0.");
}

/// <summary>What paces the tick thread.</summary>
public enum TickSource
{
    /// <summary>The desktop compositor's frame clock.</summary>
    Composition = 0,
    /// <summary>A high-resolution timer at the refresh period.</summary>
    Timer = 1,
}

/// <summary>Chooses between composition-paced and timer-paced ticks (B37). Single-threaded
/// (the tick thread).</summary>
public sealed class TickPacer
{
    /// <summary>Creates a pacer in Composition mode.</summary>
    /// <param name="options">null = TickPacingOptions.Default.</param>
    public TickPacer(TickPacingOptions? options = null)
    {
        Options = options ?? TickPacingOptions.Default;
    }

    /// <summary>The constants.</summary>
    public TickPacingOptions Options { get; }

    /// <summary>The current pacing source.</summary>
    public TickSource Source { get; private set; }

    /// <summary>Consecutive suspect composition waits.</summary>
    public int SuspectCount { get; private set; }

    /// <summary>Back to Composition with a zero count (each frames-on period).</summary>
    public void Reset()
    {
        Source = TickSource.Composition;
        SuspectCount = 0;
    }

    /// <summary>Records one composition wait.</summary>
    /// <param name="succeeded">Whether the wait call reported success.</param>
    /// <param name="elapsedSeconds">How long it blocked.</param>
    public void ReportFlush(bool succeeded, double elapsedSeconds)
    {
        if (Source == TickSource.Timer) return;
        if (!succeeded || !double.IsFinite(elapsedSeconds) || elapsedSeconds < Options.FastFlushSeconds)
        {
            SuspectCount++;
            if (SuspectCount >= Options.FastFlushLimit) Source = TickSource.Timer;
        }
        else
        {
            SuspectCount = 0;
        }
    }

    /// <summary>Timer period for refreshHz (B37).</summary>
    /// <param name="refreshHz">The refresh rate, if known.</param>
    /// <returns>The period in seconds.</returns>
    public double TimerPeriodSeconds(double? refreshHz)
    {
        if (refreshHz is double hz && double.IsFinite(hz) && hz >= Options.MinRefreshHz && hz <= Options.MaxRefreshHz)
        {
            return 1.0 / hz;
        }

        return 1.0 / Options.FallbackRefreshHz;
    }
}

/// <summary>Allows at most one outstanding tick post (B38). Thread-safe.</summary>
public sealed class TickCoalescer
{
    private int _outstanding;

    /// <summary>True when the caller should post a tick now (none outstanding).</summary>
    public bool TryBeginPost() => Interlocked.CompareExchange(ref _outstanding, 1, 0) == 0;

    /// <summary>The posted tick is being handled; the next signal may post again.</summary>
    public void Delivered() => Volatile.Write(ref _outstanding, 0);

    /// <summary>True while a posted tick has not been delivered.</summary>
    public bool IsOutstanding => Volatile.Read(ref _outstanding) != 0;
}
