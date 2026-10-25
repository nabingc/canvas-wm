using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Persistence;

/// <summary>IDisplayStore over an ITextFileStore (B22-B24). Engine-thread only.</summary>
public sealed class FileDisplayStore : IDisplayStore
{
    private readonly ITextFileStore _files;
    private readonly Action<string>? _log;
    private readonly Dictionary<string, string> _lastWritten = new(StringComparer.Ordinal);

    /// <summary>Creates the store.</summary>
    /// <param name="files">The layouts folder.</param>
    /// <param name="log">One-line diagnostics; may be null.</param>
    /// <exception cref="ArgumentNullException">files is null.</exception>
    public FileDisplayStore(ITextFileStore files, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
        _log = log;
    }

    /// <inheritdoc />
    public SavedDisplay? Load(EngineKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        string name = DataFiles.LayoutFileName(key);
        TextFileRead r = _files.Read(name);
        if (r.Status == TextFileStatus.Missing) return null;
        if (r.Status == TextFileStatus.Failed || r.Content is null)
        {
            _log?.Invoke($"layout {name}: could not be read");
            return null;
        }

        if (!SavedDisplayJson.TryDecode(r.Content, out EngineKey? stored, out SavedDisplay? display, out string? error))
        {
            _log?.Invoke($"layout {name}: invalid ({error}); moved aside");
            _files.Move(name, name + DataFiles.CorruptSuffix);
            return null;
        }

        if (!stored.Equals(key))
        {
            _log?.Invoke($"layout {name}: belongs to another engine key; ignored");
            return null;
        }

        return display;
    }

    /// <inheritdoc />
    public void Save(EngineKey key, SavedDisplay display)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(display);
        string name = DataFiles.LayoutFileName(key);
        string text;
        try
        {
            text = SavedDisplayJson.Encode(key, display);
        }
        catch (NotSupportedException e)
        {
            _log?.Invoke($"layout {name}: not saved ({e.Message})");
            return;
        }

        // SPEC-ASSUMPTION A20: unchanged text is not written again.
        if (_lastWritten.TryGetValue(name, out string? last) && string.Equals(last, text, StringComparison.Ordinal))
        {
            return;
        }

        if (_files.Write(name, text)) _lastWritten[name] = text;
        else
        {
            _lastWritten.Remove(name);
            _log?.Invoke($"layout {name}: write failed");
        }
    }
}
