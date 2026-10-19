namespace CanvasWm.Core.Layout;

/// <summary>Mints node ids.</summary>
public interface IIdGenerator
{
    /// <summary>Returns "{prefix}-{R}{C}": a random component followed by a wrapping counter.</summary>
    /// <param name="prefix">Id prefix.</param>
    string Make(string prefix);
}

/// <summary>Id prefixes used by layout edits.</summary>
public static class IdPrefixes
{
    /// <summary>"split" - the only prefix used by layout edits.</summary>
    public const string Split = "split";
}

/// <summary>Thread-safe id generator: random component + wrapping counter.</summary>
public sealed class UidGenerator : IIdGenerator
{
    /// <summary>Counter modulus: 1,000,000.</summary>
    public const int CounterModulus = 1_000_000;

    private readonly Func<uint> _random;
    private readonly object _gate = new();
    private int _counter;

    /// <summary>Process-wide instance using Random.Shared, counter starting at 0.</summary>
    // SPEC-ASSUMPTION A12: process-wide shared generator mirrors the studied global counter.
    public static UidGenerator Shared { get; } =
        new(() => (uint)Random.Shared.NextInt64(0, 1L << 32));

    /// <summary>Creates a generator.</summary>
    /// <param name="random">Source of the random 32-bit component (called once per Make).</param>
    /// <param name="initialCounter">Counter value before the first call, in [0, 999999].</param>
    /// <exception cref="ArgumentNullException">random is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">initialCounter outside [0, 999999].</exception>
    public UidGenerator(Func<uint> random, int initialCounter = 0)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (initialCounter < 0 || initialCounter >= CounterModulus)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCounter));
        }
        _random = random;
        _counter = initialCounter;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">prefix is null.</exception>
    public string Make(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        int c;
        string r;
        lock (_gate)
        {
            _counter = (_counter + 1) % CounterModulus;
            c = _counter;
            r = ToBase36(_random());
        }
        if (r.Length > 5)
        {
            r = r.Substring(0, 5);
        }
        return prefix + "-" + r + ToBase36((ulong)c);
    }

    /// <summary>Lowercase base-36 rendering (digits 0-9 then a-z) of a non-negative value; 0 gives "0".</summary>
    /// <param name="value">The value.</param>
    public static string ToBase36(ulong value)
    {
        if (value == 0)
        {
            return "0";
        }
        Span<char> buf = stackalloc char[13];
        int pos = buf.Length;
        while (value != 0)
        {
            int d = (int)(value % 36);
            buf[--pos] = (char)(d < 10 ? '0' + d : 'a' + (d - 10));
            value /= 36;
        }
        return new string(buf.Slice(pos));
    }
}
