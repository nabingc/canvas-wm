using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CanvasWm.Engine.Persistence;

/// <summary>Raised inside a decoder for a rejected file; always caught by TryDecode.</summary>
internal sealed class JsonRejectException : Exception
{
    internal JsonRejectException(string message) : base(message)
    {
    }
}

/// <summary>Shared writer and reader helpers of the codecs (B15, B16).</summary>
internal static class JsonSupport
{
    internal const int MaxDepth = 256;

    internal static Utf8JsonWriter NewWriter(ArrayBufferWriter<byte> buffer) => new(buffer, new JsonWriterOptions
    {
        Indented = true,
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    internal static string Finish(Utf8JsonWriter writer, ArrayBufferWriter<byte> buffer)
    {
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    internal static void WriteDouble(Utf8JsonWriter w, double v)
    {
        if (double.IsNaN(v)) w.WriteStringValue("NaN");
        else if (double.IsPositiveInfinity(v)) w.WriteStringValue("Infinity");
        else if (double.IsNegativeInfinity(v)) w.WriteStringValue("-Infinity");
        else w.WriteNumberValue(v);
    }

    internal static void WriteDouble(Utf8JsonWriter w, string name, double v)
    {
        w.WritePropertyName(name);
        WriteDouble(w, v);
    }

    internal static JsonDocument Parse(string json)
    {
        return JsonDocument.Parse(json, new JsonDocumentOptions
        {
            MaxDepth = MaxDepth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
        });
    }

    internal static JsonRejectException Reject(string message) => new(message);

    /// <summary>Properties of an object; the last occurrence of a duplicated name wins.</summary>
    internal static Dictionary<string, JsonElement> Props(JsonElement obj, string what)
    {
        if (obj.ValueKind != JsonValueKind.Object) throw Reject(what + " is not an object");
        var d = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonProperty p in obj.EnumerateObject()) d[p.Name] = p.Value;
        return d;
    }

    /// <summary>The value, or null when missing or JSON null.</summary>
    internal static JsonElement? Opt(Dictionary<string, JsonElement> props, string name) =>
        props.TryGetValue(name, out JsonElement e) && e.ValueKind != JsonValueKind.Null ? e : null;

    internal static JsonElement Req(Dictionary<string, JsonElement> props, string name)
    {
        if (Opt(props, name) is JsonElement e) return e;
        throw Reject("missing " + name);
    }

    internal static string Str(JsonElement e, string what)
    {
        if (e.ValueKind != JsonValueKind.String) throw Reject(what + " is not a string");
        return e.GetString()!;
    }

    internal static bool Bool(JsonElement e, string what)
    {
        if (e.ValueKind == JsonValueKind.True) return true;
        if (e.ValueKind == JsonValueKind.False) return false;
        throw Reject(what + " is not a boolean");
    }

    internal static int Int(JsonElement e, string what)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int v)) return v;
        throw Reject(what + " is not an integer");
    }

    /// <summary>A number, or exactly one of the strings "NaN", "Infinity", "-Infinity" (B15).</summary>
    internal static double Dbl(JsonElement e, string what)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double v)) return v;
        if (e.ValueKind == JsonValueKind.String)
        {
            switch (e.GetString())
            {
                case "NaN": return double.NaN;
                case "Infinity": return double.PositiveInfinity;
                case "-Infinity": return double.NegativeInfinity;
            }
        }

        throw Reject(what + " is not a number");
    }

    /// <summary>A finite JSON number only.</summary>
    internal static double FiniteNumber(JsonElement e, string what)
    {
        if (e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out double v) && double.IsFinite(v)) return v;
        throw Reject(what + " is not a finite number");
    }

    internal static JsonElement Arr(JsonElement e, string what)
    {
        if (e.ValueKind != JsonValueKind.Array) throw Reject(what + " is not an array");
        return e;
    }

    internal static List<string> StrList(JsonElement? e, string what)
    {
        var list = new List<string>();
        if (e is not JsonElement arr) return list;
        foreach (JsonElement item in Arr(arr, what).EnumerateArray()) list.Add(Str(item, what + " element"));
        return list;
    }
}
