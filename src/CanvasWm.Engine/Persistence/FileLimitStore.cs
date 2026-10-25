using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Persistence;

/// <summary>ILimitStore over one file, DataFiles.LimitsFileName (B25, B26).</summary>
public sealed class FileLimitStore : ILimitStore
{
    private readonly ITextFileStore _files;
    private readonly Action<string>? _log;
    private readonly Dictionary<AppId, Size> _entries = new();

    /// <summary>Reads the file once, now (B25).</summary>
    /// <param name="files">The data folder.</param>
    /// <param name="log">One-line diagnostics; may be null.</param>
    /// <exception cref="ArgumentNullException">files is null.</exception>
    public FileLimitStore(ITextFileStore files, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
        _log = log;
        TextFileRead r = files.Read(DataFiles.LimitsFileName);
        if (r.Status == TextFileStatus.Failed)
        {
            log?.Invoke("limits: could not be read");
        }
        else if (r.Status == TextFileStatus.Ok && r.Content is not null)
        {
            if (LimitsJson.TryDecode(r.Content, out var map, out string? error))
            {
                foreach (var kv in map) _entries[kv.Key] = kv.Value;
            }
            else
            {
                log?.Invoke($"limits: invalid ({error}); moved aside");
                files.Move(DataFiles.LimitsFileName, DataFiles.LimitsFileName + DataFiles.CorruptSuffix);
            }
        }
    }

    /// <summary>Snapshot of the entries (copy).</summary>
    public IReadOnlyDictionary<AppId, Size> Entries => new Dictionary<AppId, Size>(_entries);

    /// <inheritdoc />
    public Size? Minimum(AppId? app) => app is not null && _entries.TryGetValue(app, out Size s) ? s : null;

    /// <inheritdoc />
    public void Learn(AppId? app, Size minimum)
    {
        if (app is null) return;
        if (!double.IsFinite(minimum.Width) || !double.IsFinite(minimum.Height)) return;
        Size? current = Minimum(app);
        Size next = LimitRules.Learn(current, minimum);
        if (current is Size c && c.Equals(next)) return;
        _entries[app] = next;
        Persist();
    }

    /// <inheritdoc />
    public void Observe(AppId? app, Size observed)
    {
        if (app is null) return;
        Size? current = Minimum(app);
        if (current is null) return;
        Size? next = LimitRules.Observe(current, observed);
        if (next is not Size n || n.Equals(current.Value)) return;
        _entries[app] = n;
        Persist();
    }

    private void Persist()
    {
        if (!_files.Write(DataFiles.LimitsFileName, LimitsJson.Encode(_entries)))
        {
            _log?.Invoke("limits: write failed");
        }
    }
}
