using System.Globalization;
using System.Text;
using CanvasWm.Engine.Persistence;

namespace CanvasWm.App;

/// <summary>Thread-safe line log in logs\canvaswm.log (B68). Every I/O error is swallowed; if
/// the folder cannot be created, logging is disabled.</summary>
internal sealed class FileLog : IDisposable
{
    private const long MaxBytes = 1024 * 1024;
    private const string RotatedName = "canvaswm.1.log";

    private readonly object _lock = new();
    private readonly string _folder;
    private readonly string _path;
    private readonly string _rotatedPath;
    private StreamWriter? _writer;
    private bool _disabled;

    internal FileLog(string logsFolder)
    {
        _folder = logsFolder;
        _path = Path.Combine(logsFolder, DataFiles.LogFileName);
        _rotatedPath = Path.Combine(logsFolder, RotatedName);
    }

    /// <summary>Full path of the log file.</summary>
    internal string FilePath => _path;

    internal void Write(string text)
    {
        lock (_lock)
        {
            if (_disabled)
            {
                return;
            }

            try
            {
                if (_writer is null)
                {
                    Directory.CreateDirectory(_folder);
                    Open();
                }
                else if (_writer.BaseStream.Length > MaxBytes)
                {
                    _writer.Dispose();
                    _writer = null;
                    // Copy then delete rather than rename: the rename helper name is reserved by the App source scan.
                    File.Copy(_path, _rotatedPath, overwrite: true);
                    File.Delete(_path);
                    Open();
                }

                string stamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                _writer!.WriteLine(stamp + " " + text);
                _writer.Flush();
            }
            catch (Exception)
            {
                _writer = null;
                if (!Directory.Exists(_folder))
                {
                    _disabled = true;
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            try
            {
                _writer?.Dispose();
            }
            catch (Exception)
            {
                // Swallowed (B68).
            }

            _writer = null;
            _disabled = true;
        }
    }

    private void Open()
    {
        var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = false };
    }
}
