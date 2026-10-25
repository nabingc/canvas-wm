namespace CanvasWm.Engine.Persistence;

/// <summary>Loads settings.json with first-run and corrupt-file handling (B28).</summary>
public static class SettingsFile
{
    /// <summary>Reads settings.json; writes defaults on first run or after a corrupt file.</summary>
    /// <param name="files">The data folder.</param>
    /// <param name="log">One-line diagnostics; may be null.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentNullException">files is null.</exception>
    public static HostSettings LoadOrCreate(ITextFileStore files, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(files);
        TextFileRead r = files.Read(DataFiles.SettingsFileName);
        switch (r.Status)
        {
            case TextFileStatus.Ok when r.Content is not null:
                if (SettingsJson.TryDecode(r.Content, out var values, out string? error))
                {
                    return HostSettings.FromValues(values);
                }

                log?.Invoke($"settings: invalid ({error}); moved aside, defaults written");
                files.Move(DataFiles.SettingsFileName, DataFiles.SettingsFileName + DataFiles.CorruptSuffix);
                WriteDefaults(files, log);
                return HostSettings.Default;
            case TextFileStatus.Missing:
                WriteDefaults(files, log);
                return HostSettings.Default;
            default:
                log?.Invoke("settings: could not be read; defaults used");
                return HostSettings.Default;
        }
    }

    private static void WriteDefaults(ITextFileStore files, Action<string>? log)
    {
        if (!files.Write(DataFiles.SettingsFileName, SettingsJson.Encode(HostSettings.Default.ToValues())))
        {
            log?.Invoke("settings: write failed");
        }
    }
}
