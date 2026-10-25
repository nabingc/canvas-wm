using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CanvasWm.Engine.Persistence;

/// <summary>JSON codec of the flat settings map (B21). Pure.</summary>
public static class SettingsJson
{
    /// <summary>The format version written and accepted: 1.</summary>
    public const int Version = 1;

    /// <summary>Encodes the map; entries are sorted ordinally.</summary>
    /// <param name="values">The map.</param>
    /// <returns>The file text.</returns>
    /// <exception cref="ArgumentNullException">values is null.</exception>
    public static string Encode(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var buffer = new ArrayBufferWriter<byte>();
        using var w = JsonSupport.NewWriter(buffer);
        w.WriteStartObject();
        w.WriteNumber("version", Version);
        w.WriteStartObject("values");
        foreach (var kv in values.OrderBy(k => k.Key, StringComparer.Ordinal))
            w.WriteString(kv.Key, kv.Value);
        w.WriteEndObject();
        w.WriteEndObject();
        return JsonSupport.Finish(w, buffer);
    }

    /// <summary>Decodes file text (B21). On false, values is null and error is a non-empty reason.</summary>
    /// <param name="json">The file text.</param>
    /// <param name="values">The map on success.</param>
    /// <param name="error">The reason on failure.</param>
    /// <returns>True on success.</returns>
    /// <exception cref="ArgumentNullException">json is null.</exception>
    public static bool TryDecode(string json,
                                 [NotNullWhen(true)] out IReadOnlyDictionary<string, string>? values,
                                 out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        values = null;
        error = null;
        try
        {
            using JsonDocument doc = JsonSupport.Parse(json);
            var top = JsonSupport.Props(doc.RootElement, "top level");
            if (JsonSupport.Int(JsonSupport.Req(top, "version"), "version") != Version)
                throw JsonSupport.Reject("unsupported version");
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in JsonSupport.Props(JsonSupport.Req(top, "values"), "values"))
                map[kv.Key] = JsonSupport.Str(kv.Value, "value");
            values = map;
            return true;
        }
        catch (Exception e)
        {
            values = null;
            string msg = e.Message.ReplaceLineEndings(" ").Trim();
            error = msg.Length == 0 ? e.GetType().Name : msg;
            return false;
        }
    }
}
