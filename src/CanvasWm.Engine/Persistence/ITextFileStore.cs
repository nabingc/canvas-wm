namespace CanvasWm.Engine.Persistence;

/// <summary>Outcome of reading a named text file.</summary>
public enum TextFileStatus
{
    /// <summary>The file was read.</summary>
    Ok = 0,
    /// <summary>The file does not exist.</summary>
    Missing = 1,
    /// <summary>The file exists but cannot be read.</summary>
    Failed = 2,
}

/// <summary>Read result. Content is non-null exactly when Status is Ok.</summary>
/// <param name="Status">Outcome.</param>
/// <param name="Content">The text, when Status is Ok.</param>
public readonly record struct TextFileRead(TextFileStatus Status, string? Content);

/// <summary>A flat folder of small UTF-8 text files, addressed by plain file name. The only
/// file I/O seam of the stores. Implementations never throw for storage failures; they
/// report them through the return values. Called on the engine thread.</summary>
public interface ITextFileStore
{
    /// <summary>Reads the whole file. Missing when it does not exist; Failed when it exists
    /// but cannot be read.</summary>
    /// <param name="name">Plain file name.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException">name is not valid (TextFileNames.Validate).</exception>
    TextFileRead Read(string name);

    /// <summary>Atomically replaces (or creates) the file with content: a reader sees either
    /// the old or the new content, never a mix. True on success.</summary>
    /// <param name="name">Plain file name.</param>
    /// <param name="content">The text.</param>
    /// <returns>True on success.</returns>
    /// <exception cref="ArgumentException">name is not valid.</exception>
    bool Write(string name, string content);

    /// <summary>Renames name to newName, replacing newName if it exists. False when name is
    /// missing or the rename failed.</summary>
    /// <param name="name">Existing file name.</param>
    /// <param name="newName">New file name.</param>
    /// <returns>True on success.</returns>
    /// <exception cref="ArgumentException">a name is not valid.</exception>
    bool Move(string name, string newName);
}
