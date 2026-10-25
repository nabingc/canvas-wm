using System.Text;
using CanvasWm.Engine.Persistence;

namespace CanvasWm.Win32.Storage;

/// <summary>ITextFileStore over one directory with System.IO (B58).</summary>
public sealed class DirectoryTextStore : ITextFileStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Creates the store. The directory need not exist yet.</summary>
    /// <param name="directory">A fully qualified path.</param>
    /// <exception cref="ArgumentNullException">directory is null.</exception>
    /// <exception cref="ArgumentException">directory is not a fully qualified path.</exception>
    public DirectoryTextStore(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new ArgumentException("The directory must be a fully qualified path.", nameof(directory));
        }

        Directory = directory;
    }

    /// <summary>The folder.</summary>
    public string Directory { get; }

    /// <inheritdoc />
    public TextFileRead Read(string name)
    {
        TextFileNames.Validate(name);
        string path = Path.Combine(Directory, name);
        try
        {
            if (!File.Exists(path))
            {
                return new TextFileRead(TextFileStatus.Missing, null);
            }

            return new TextFileRead(TextFileStatus.Ok, File.ReadAllText(path, Utf8NoBom));
        }
        catch (FileNotFoundException)
        {
            return new TextFileRead(TextFileStatus.Missing, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new TextFileRead(TextFileStatus.Missing, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new TextFileRead(TextFileStatus.Failed, null);
        }
    }

    /// <inheritdoc />
    public bool Write(string name, string content)
    {
        TextFileNames.Validate(name);
        ArgumentNullException.ThrowIfNull(content);
        string path = Path.Combine(Directory, name);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = Utf8NoBom.GetBytes(content);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tmp, path, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            try
            {
                File.Delete(tmp);
            }
            catch (Exception)
            {
                // Best effort.
            }

            return false;
        }
    }

    /// <inheritdoc />
    public bool Move(string name, string newName)
    {
        TextFileNames.Validate(name);
        TextFileNames.Validate(newName);
        try
        {
            File.Move(Path.Combine(Directory, name), Path.Combine(Directory, newName), overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
