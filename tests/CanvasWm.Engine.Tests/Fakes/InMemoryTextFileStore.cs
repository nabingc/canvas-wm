using CanvasWm.Engine.Persistence;

namespace CanvasWm.Engine.Tests.Fakes;

/// <summary>Dictionary-backed ITextFileStore that records calls and injects failures.</summary>
public sealed class InMemoryTextFileStore : ITextFileStore
{
    private readonly Dictionary<string, string> _files = new(StringComparer.Ordinal);

    /// <summary>Files by name.</summary>
    public IReadOnlyDictionary<string, string> Files => _files;

    /// <summary>Names read, in order.</summary>
    public List<string> Reads { get; } = new();

    /// <summary>Writes (name, content), in order, including failed ones.</summary>
    public List<(string Name, string Content)> Writes { get; } = new();

    /// <summary>Moves (from, to), in order.</summary>
    public List<(string From, string To)> Moves { get; } = new();

    /// <summary>Names whose Read reports Failed.</summary>
    public HashSet<string> FailRead { get; } = new(StringComparer.Ordinal);

    /// <summary>Names whose Write returns false.</summary>
    public HashSet<string> FailWrite { get; } = new(StringComparer.Ordinal);

    /// <summary>Names whose Move returns false.</summary>
    public HashSet<string> FailMove { get; } = new(StringComparer.Ordinal);

    /// <summary>Seeds a file without recording a write.</summary>
    public void Seed(string name, string content) => _files[name] = content;

    /// <inheritdoc />
    public TextFileRead Read(string name)
    {
        TextFileNames.Validate(name);
        Reads.Add(name);
        if (FailRead.Contains(name)) return new TextFileRead(TextFileStatus.Failed, null);
        return _files.TryGetValue(name, out var c)
            ? new TextFileRead(TextFileStatus.Ok, c)
            : new TextFileRead(TextFileStatus.Missing, null);
    }

    /// <inheritdoc />
    public bool Write(string name, string content)
    {
        TextFileNames.Validate(name);
        Writes.Add((name, content));
        if (FailWrite.Contains(name)) return false;
        _files[name] = content;
        return true;
    }

    /// <inheritdoc />
    public bool Move(string name, string newName)
    {
        TextFileNames.Validate(name);
        TextFileNames.Validate(newName);
        Moves.Add((name, newName));
        if (FailMove.Contains(name) || !_files.TryGetValue(name, out var c)) return false;
        _files.Remove(name);
        _files[newName] = c;
        return true;
    }
}
