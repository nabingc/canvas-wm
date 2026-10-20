using System.Collections.Immutable;
using CanvasWm.Core.Geometry;

namespace CanvasWm.Core.Windows;

/// <summary>Policy inputs of the classifier. Immutable; change with a with-expression.</summary>
public sealed record ClassifierPolicy
{
    private Size _minimumSize = new(120, 80);
    private IImmutableSet<string> _excludedApps = ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    /// <summary>Minimum 120 x 80, no exclusions. The same instance every time.</summary>
    public static ClassifierPolicy Default { get; } = new();

    /// <summary>Smallest managed frame, DIPs, inclusive on both axes. Default 120 x 80.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A component is negative or not finite.</exception>
    public Size MinimumSize
    {
        get => _minimumSize;
        init
        {
            if (!double.IsFinite(value.Width) || !double.IsFinite(value.Height)
                || value.Width < 0 || value.Height < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MinimumSize), value, "Components must be finite and non-negative.");
            }

            _minimumSize = value;
        }
    }

    /// <summary>App ids whose windows are never managed. Ordinal, case-sensitive. Default empty.
    /// The init accessor copies the input into an ordinal set.</summary>
    /// <exception cref="ArgumentNullException">Set to null.</exception>
    /// <exception cref="ArgumentException">An element is null or empty.</exception>
    public IImmutableSet<string> ExcludedApps
    {
        get => _excludedApps;
        init
        {
            ArgumentNullException.ThrowIfNull(value, nameof(ExcludedApps));
            var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (string? s in value)
            {
                if (string.IsNullOrEmpty(s))
                {
                    throw new ArgumentException("Elements must be non-empty.", nameof(ExcludedApps));
                }

                builder.Add(s);
            }

            _excludedApps = builder.ToImmutable();
        }
    }

    /// <summary>Convenience: a copy of this policy with the given exclusions.</summary>
    /// <param name="appIds">The excluded app ids.</param>
    /// <returns>The new policy.</returns>
    public ClassifierPolicy WithExcludedApps(IEnumerable<string> appIds)
    {
        ArgumentNullException.ThrowIfNull(appIds);
        return this with { ExcludedApps = appIds.ToImmutableHashSet(StringComparer.Ordinal) };
    }

    /// <summary>Value equality: same minimum size and the same set of excluded apps.</summary>
    /// <param name="other">The other policy.</param>
    /// <returns>True if equal.</returns>
    public bool Equals(ClassifierPolicy? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _minimumSize == other._minimumSize && _excludedApps.SetEquals(other._excludedApps);
    }

    /// <summary>Order-insensitive hash.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        int sum = 0;
        foreach (string s in _excludedApps)
        {
            sum += StringComparer.Ordinal.GetHashCode(s);
        }

        return HashCode.Combine(_minimumSize, sum, _excludedApps.Count);
    }
}
