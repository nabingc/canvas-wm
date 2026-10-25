using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CanvasWm.Core.Geometry;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Persistence;

/// <summary>JSON codec of the learned-minimum map (B20). Pure.</summary>
public static class LimitsJson
{
    /// <summary>The format version written and accepted: 1.</summary>
    public const int Version = 1;

    /// <summary>Encodes the map; entries are sorted ordinally. Entries with a non-finite size are skipped.</summary>
    /// <param name="limits">The map.</param>
    /// <returns>The file text.</returns>
    /// <exception cref="ArgumentNullException">limits is null.</exception>
    public static string Encode(IReadOnlyDictionary<AppId, Size> limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        var buffer = new ArrayBufferWriter<byte>();
        using var w = JsonSupport.NewWriter(buffer);
        w.WriteStartObject();
        w.WriteNumber("version", Version);
        w.WriteStartObject("limits");
        foreach (var kv in limits.OrderBy(k => k.Key.Value, StringComparer.Ordinal))
        {
            if (!double.IsFinite(kv.Value.Width) || !double.IsFinite(kv.Value.Height)) continue;
            w.WriteStartObject(kv.Key.Value);
            w.WriteNumber("w", kv.Value.Width);
            w.WriteNumber("h", kv.Value.Height);
            w.WriteEndObject();
        }

        w.WriteEndObject();
        w.WriteEndObject();
        return JsonSupport.Finish(w, buffer);
    }

    /// <summary>Decodes file text (B20). On false, limits is null and error is a non-empty reason.</summary>
    /// <param name="json">The file text.</param>
    /// <param name="limits">The map on success.</param>
    /// <param name="error">The reason on failure.</param>
    /// <returns>True on success.</returns>
    /// <exception cref="ArgumentNullException">json is null.</exception>
    public static bool TryDecode(string json,
                                 [NotNullWhen(true)] out IReadOnlyDictionary<AppId, Size>? limits,
                                 out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        limits = null;
        error = null;
        try
        {
            using JsonDocument doc = JsonSupport.Parse(json);
            var top = JsonSupport.Props(doc.RootElement, "top level");
            if (JsonSupport.Int(JsonSupport.Req(top, "version"), "version") != Version)
                throw JsonSupport.Reject("unsupported version");
            var map = new Dictionary<AppId, Size>();
            foreach (var kv in JsonSupport.Props(JsonSupport.Req(top, "limits"), "limits"))
            {
                if (kv.Key.Length == 0) throw JsonSupport.Reject("empty app id");
                var sp = JsonSupport.Props(kv.Value, "limit");
                double wd = JsonSupport.FiniteNumber(JsonSupport.Req(sp, "w"), "w");
                double hd = JsonSupport.FiniteNumber(JsonSupport.Req(sp, "h"), "h");
                if (wd < 0 || hd < 0) throw JsonSupport.Reject("negative limit");
                map[new AppId(kv.Key)] = new Size(wd, hd);
            }

            limits = map;
            return true;
        }
        catch (Exception e)
        {
            limits = null;
            string msg = e.Message.ReplaceLineEndings(" ").Trim();
            error = msg.Length == 0 ? e.GetType().Name : msg;
            return false;
        }
    }
}
