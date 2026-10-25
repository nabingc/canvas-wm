using System.Security.Cryptography;
using System.Text;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Persistence;

/// <summary>Names of the data folder and its files (B11).</summary>
public static class DataFiles
{
    /// <summary>Name of the data folder below the local application data folder.</summary>
    public const string DataFolderName = "CanvasWM";
    /// <summary>Folder of the per-engine layout files.</summary>
    public const string LayoutsFolderName = "layouts";
    /// <summary>Folder of the log.</summary>
    public const string LogsFolderName = "logs";
    /// <summary>Learned-minimum file.</summary>
    public const string LimitsFileName = "limits.json";
    /// <summary>Settings file.</summary>
    public const string SettingsFileName = "settings.json";
    /// <summary>Log file.</summary>
    public const string LogFileName = "canvaswm.log";
    /// <summary>Suffix added to a file that could not be decoded.</summary>
    public const string CorruptSuffix = ".corrupt";

    /// <summary>"arrangement-" + lowercase hex SHA-256 of UTF-8(key.Value) + ".json" (B12).</summary>
    /// <param name="key">The engine key.</param>
    /// <returns>The file name.</returns>
    /// <exception cref="ArgumentNullException">key is null.</exception>
    public static string LayoutFileName(EngineKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return "arrangement-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key.Value))) + ".json";
    }
}
