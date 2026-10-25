namespace CanvasWm.Engine.Persistence;

/// <summary>File-name rules shared by every ITextFileStore (B10).</summary>
public static class TextFileNames
{
    private const int MaxLength = 200;
    private const string ForbiddenChars = "<>:\"/\\|?*";

    /// <summary>Throws when name is not a plain, portable file name.</summary>
    /// <param name="name">The name.</param>
    /// <exception cref="ArgumentNullException">name is null.</exception>
    /// <exception cref="ArgumentException">name is invalid (B10).</exception>
    public static void Validate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 || name == "." || name == ".." || name.Length > MaxLength)
        {
            throw new ArgumentException("Invalid file name.", nameof(name));
        }

        foreach (char c in name)
        {
            if (c < 0x20 || ForbiddenChars.Contains(c))
            {
                throw new ArgumentException("Invalid file name.", nameof(name));
            }
        }

        char last = name[^1];
        if (last == '.' || last == ' ')
        {
            throw new ArgumentException("Invalid file name.", nameof(name));
        }
    }
}
