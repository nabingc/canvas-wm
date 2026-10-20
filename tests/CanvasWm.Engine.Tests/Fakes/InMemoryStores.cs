using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Rendering;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Tests.Fakes;

/// <summary>In-memory learned-minimum store.</summary>
public sealed class InMemoryLimitStore : ILimitStore
{
    private readonly Dictionary<AppId, Size> _entries = new();

    /// <summary>Stored entries.</summary>
    public IReadOnlyDictionary<AppId, Size> Entries => _entries;

    /// <summary>Number of calls that wrote an entry.</summary>
    public int PersistCount { get; private set; }

    /// <inheritdoc />
    public Size? Minimum(AppId? app) => app is not null && _entries.TryGetValue(app, out var s) ? s : null;

    /// <inheritdoc />
    public void Learn(AppId? app, Size minimum)
    {
        if (app is null) return;
        if (!double.IsFinite(minimum.Width) || !double.IsFinite(minimum.Height)) return;
        Size? current = _entries.TryGetValue(app, out var c) ? c : null;
        _entries[app] = LimitRules.Learn(current, minimum);
        PersistCount++;
    }

    /// <inheritdoc />
    public void Observe(AppId? app, Size observed)
    {
        if (app is null || !_entries.TryGetValue(app, out var current)) return;
        var next = LimitRules.Observe(current, observed);
        if (next is { } n && n != current)
        {
            _entries[app] = n;
            PersistCount++;
        }
    }
}

/// <summary>In-memory display store.</summary>
public sealed class InMemoryDisplayStore : IDisplayStore
{
    private readonly Dictionary<EngineKey, SavedDisplay> _records = new();
    private readonly List<(EngineKey Key, SavedDisplay Display)> _log = new();

    /// <summary>Every save in order.</summary>
    public IReadOnlyList<(EngineKey Key, SavedDisplay Display)> SaveLog => _log;

    /// <inheritdoc />
    public SavedDisplay? Load(EngineKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _records.TryGetValue(key, out var d) ? d : null;
    }

    /// <inheritdoc />
    public void Save(EngineKey key, SavedDisplay display)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(display);
        _records[key] = display;
        _log.Add((key, display));
    }
}

/// <summary>Renderer that records every plan.</summary>
public sealed class RecordingRenderer : IRenderer
{
    private readonly List<RenderPlan> _plans = new();

    /// <summary>All plans in order.</summary>
    public IReadOnlyList<RenderPlan> Plans => _plans;

    /// <summary>The most recent plan, or null.</summary>
    public RenderPlan? Last => _plans.Count == 0 ? null : _plans[^1];

    /// <inheritdoc />
    public void Render(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _plans.Add(plan);
    }
}
