using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Services;

namespace CanvasWm.Engine.Persistence;

/// <summary>JSON codec of one saved display record (B13-B19). Pure.</summary>
public static class SavedDisplayJson
{
    /// <summary>The format version written and accepted: 1.</summary>
    public const int Version = 1;

    /// <summary>Encodes key + display as the file text (B13-B16).</summary>
    /// <param name="key">The engine key.</param>
    /// <param name="display">The record.</param>
    /// <returns>The file text.</returns>
    /// <exception cref="NotSupportedException">display holds a value that has no wire form
    /// yet: a hidden panel, a hidden desktop with a restore target, or a non-empty dock-spot
    /// map (B17).</exception>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static string Encode(EngineKey key, SavedDisplay display)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(display);
        // SPEC-ASSUMPTION A5: restore targets and dock spots have no wire form yet.
        if (display.Document.Hidden.Length > 0)
            throw new NotSupportedException("Hidden panels have no wire form yet.");
        if (display.Document.HiddenDesktop?.Restore is not null)
            throw new NotSupportedException("A restore target has no wire form yet.");
        if (display.DockSpots is { Count: > 0 })
            throw new NotSupportedException("Dock spots have no wire form yet.");

        var buffer = new ArrayBufferWriter<byte>();
        using var w = JsonSupport.NewWriter(buffer);
        w.WriteStartObject();
        w.WriteNumber("version", Version);
        w.WriteString("engineKey", key.Value);
        w.WritePropertyName("doc");
        WriteDocument(w, display.Document);
        if (display.FramedNodeId is null) w.WriteNull("framed");
        else w.WriteString("framed", display.FramedNodeId);

        w.WriteStartObject("framings");
        foreach (var kv in display.Framings.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            w.WriteStartArray(kv.Key);
            foreach (string id in kv.Value) w.WriteStringValue(id);
            w.WriteEndArray();
        }

        w.WriteEndObject();
        w.WriteStartObject("owners");
        foreach (var kv in display.Owners.OrderBy(k => k.Key.Value, StringComparer.Ordinal))
            w.WriteString(kv.Key.Value, kv.Value);
        w.WriteEndObject();
        if (display.DockSpots is not null)
        {
            w.WriteStartObject("dockSpots");
            w.WriteEndObject();
        }

        if (display.FloatSizes is not null)
        {
            w.WriteStartObject("floatSizes");
            foreach (var kv in display.FloatSizes.OrderBy(k => k.Key.Value, StringComparer.Ordinal))
            {
                w.WritePropertyName(kv.Key.Value);
                WriteSize(w, kv.Value);
            }

            w.WriteEndObject();
        }

        w.WriteEndObject();
        return JsonSupport.Finish(w, buffer);
    }

    /// <summary>Decodes file text (B18, B19). On false, key and display are null and error
    /// is a non-empty one-line reason.</summary>
    /// <param name="json">The file text.</param>
    /// <param name="key">The engine key on success.</param>
    /// <param name="display">The record on success.</param>
    /// <param name="error">The reason on failure.</param>
    /// <returns>True on success.</returns>
    /// <exception cref="ArgumentNullException">json is null.</exception>
    public static bool TryDecode(string json,
                                 [NotNullWhen(true)] out EngineKey? key,
                                 [NotNullWhen(true)] out SavedDisplay? display,
                                 out string? error)
    {
        ArgumentNullException.ThrowIfNull(json);
        key = null;
        display = null;
        error = null;
        try
        {
            using JsonDocument doc = JsonSupport.Parse(json);
            var top = JsonSupport.Props(doc.RootElement, "top level");
            if (JsonSupport.Int(JsonSupport.Req(top, "version"), "version") != Version)
                throw JsonSupport.Reject("unsupported version");
            if (!EngineKey.TryParse(JsonSupport.Str(JsonSupport.Req(top, "engineKey"), "engineKey"), out EngineKey? k))
                throw JsonSupport.Reject("invalid engineKey");
            LayoutDocument layout = ReadDocument(JsonSupport.Req(top, "doc"));
            string? framed = JsonSupport.Opt(top, "framed") is JsonElement fe ? JsonSupport.Str(fe, "framed") : null;

            var framings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (JsonSupport.Opt(top, "framings") is JsonElement fr)
            {
                foreach (var kv in JsonSupport.Props(fr, "framings"))
                {
                    if (kv.Value.ValueKind == JsonValueKind.Null) throw JsonSupport.Reject("framings entry is null");
                    framings[kv.Key] = JsonSupport.StrList(kv.Value, "framings entry");
                }
            }

            var owners = new Dictionary<WindowKey, string>();
            if (JsonSupport.Opt(top, "owners") is JsonElement ow)
            {
                foreach (var kv in JsonSupport.Props(ow, "owners"))
                    owners[NewWindowKey(kv.Key)] = JsonSupport.Str(kv.Value, "owner");
            }

            Dictionary<WindowKey, DockSpot>? dockSpots = null;
            if (JsonSupport.Opt(top, "dockSpots") is JsonElement ds)
            {
                var props = JsonSupport.Props(ds, "dockSpots");
                if (props.Count > 0) throw JsonSupport.Reject("dockSpots must be empty");
                dockSpots = new Dictionary<WindowKey, DockSpot>();
            }

            Dictionary<WindowKey, Size>? floatSizes = null;
            if (JsonSupport.Opt(top, "floatSizes") is JsonElement fs)
            {
                floatSizes = new Dictionary<WindowKey, Size>();
                foreach (var kv in JsonSupport.Props(fs, "floatSizes"))
                    floatSizes[NewWindowKey(kv.Key)] = ReadSize(kv.Value);
            }

            display = new SavedDisplay(layout, framed, framings, owners, dockSpots, floatSizes);
            key = k;
            return true;
        }
        catch (Exception e)
        {
            key = null;
            display = null;
            string msg = e.Message.ReplaceLineEndings(" ").Trim();
            error = msg.Length == 0 ? e.GetType().Name : msg;
            return false;
        }
    }

    private static WindowKey NewWindowKey(string value)
    {
        if (value.Length == 0) throw JsonSupport.Reject("empty window key");
        return new WindowKey(value);
    }

    // ---- write ----

    private static void WriteDocument(Utf8JsonWriter w, LayoutDocument d)
    {
        w.WriteStartObject();
        w.WritePropertyName("root");
        if (d.Root is null) w.WriteNullValue();
        else WriteNode(w, d.Root);
        w.WriteStartArray("floating");
        foreach (FloatingPanel f in d.Floating) WriteFloating(w, f);
        w.WriteEndArray();
        w.WriteStartArray("hidden");
        w.WriteEndArray();
        w.WritePropertyName("hiddenDesktop");
        if (d.HiddenDesktop is null)
        {
            w.WriteNullValue();
        }
        else
        {
            HiddenDesktop h = d.HiddenDesktop;
            w.WriteStartObject();
            if (h.Group is null) w.WriteNull("group");
            else w.WriteString("group", h.Group);
            w.WriteStartArray("floats");
            foreach (FloatingPanel f in h.Floats) WriteFloating(w, f);
            w.WriteEndArray();
            w.WriteNull("restore");
            w.WriteStartArray("neighbourLeaves");
            foreach (string n in h.NeighbourLeaves) w.WriteStringValue(n);
            w.WriteEndArray();
            w.WritePropertyName("weight");
            if (h.Weight is double wt) JsonSupport.WriteDouble(w, wt);
            else w.WriteNullValue();
            w.WriteEndObject();
        }

        w.WriteEndObject();
    }

    private static void WriteNode(Utf8JsonWriter w, LayoutNode node)
    {
        switch (node)
        {
            case SplitNode s:
                w.WriteStartObject();
                w.WriteString("kind", "split");
                w.WriteString("id", s.Id);
                w.WriteString("axis", s.Axis.ToRaw());
                w.WriteStartArray("weights");
                foreach (double x in s.Weights) JsonSupport.WriteDouble(w, x);
                w.WriteEndArray();
                w.WriteStartArray("children");
                foreach (LayoutNode c in s.Children) WriteNode(w, c);
                w.WriteEndArray();
                w.WriteEndObject();
                break;
            case PanelNode p:
                WritePanel(w, p);
                break;
            case DesktopNode d:
                w.WriteStartObject();
                w.WriteString("kind", "desktop");
                w.WriteString("id", d.Id);
                w.WritePropertyName("child");
                if (d.Child is null) w.WriteNullValue();
                else WriteNode(w, d.Child);
                w.WriteEndObject();
                break;
            default:
                throw new NotSupportedException("Unknown node type.");
        }
    }

    private static void WritePanel(Utf8JsonWriter w, PanelNode p)
    {
        w.WriteStartObject();
        w.WriteString("kind", "panel");
        w.WriteString("id", p.Id);
        w.WriteStartArray("views");
        foreach (string v in p.Views) w.WriteStringValue(v);
        w.WriteEndArray();
        if (p.Selected is null) w.WriteNull("selected");
        else w.WriteString("selected", p.Selected);
        w.WriteBoolean("tabbed", p.Tabbed);
        w.WriteEndObject();
    }

    private static void WriteFloating(Utf8JsonWriter w, FloatingPanel f)
    {
        w.WriteStartObject();
        w.WritePropertyName("panel");
        WritePanel(w, f.Panel);
        w.WritePropertyName("rect");
        w.WriteStartObject();
        JsonSupport.WriteDouble(w, "x", f.Rect.X);
        JsonSupport.WriteDouble(w, "y", f.Rect.Y);
        JsonSupport.WriteDouble(w, "w", f.Rect.Width);
        JsonSupport.WriteDouble(w, "h", f.Rect.Height);
        w.WriteEndObject();
        w.WriteNumber("z", f.Z);
        w.WritePropertyName("size");
        if (f.Size is Size s) WriteSize(w, s);
        else w.WriteNullValue();
        w.WriteEndObject();
    }

    private static void WriteSize(Utf8JsonWriter w, Size s)
    {
        w.WriteStartObject();
        JsonSupport.WriteDouble(w, "w", s.Width);
        JsonSupport.WriteDouble(w, "h", s.Height);
        w.WriteEndObject();
    }

    // ---- read ----

    private static LayoutDocument ReadDocument(JsonElement e)
    {
        var p = JsonSupport.Props(e, "doc");
        LayoutNode? root = JsonSupport.Opt(p, "root") is JsonElement r ? ReadNode(r) : null;
        var floating = new List<FloatingPanel>();
        if (JsonSupport.Opt(p, "floating") is JsonElement fl) floating.AddRange(ReadFloatingList(fl, "floating"));
        if (JsonSupport.Opt(p, "hidden") is JsonElement h && JsonSupport.Arr(h, "hidden").GetArrayLength() > 0)
            throw JsonSupport.Reject("hidden must be empty");
        HiddenDesktop? hd = null;
        if (JsonSupport.Opt(p, "hiddenDesktop") is JsonElement hde)
        {
            var hp = JsonSupport.Props(hde, "hiddenDesktop");
            if (JsonSupport.Opt(hp, "restore") is not null) throw JsonSupport.Reject("restore must be null");
            string? group = JsonSupport.Opt(hp, "group") is JsonElement g ? JsonSupport.Str(g, "group") : null;
            var floats = JsonSupport.Opt(hp, "floats") is JsonElement fe
                ? ReadFloatingList(fe, "floats")
                : new List<FloatingPanel>();
            var neighbours = JsonSupport.StrList(JsonSupport.Opt(hp, "neighbourLeaves"), "neighbourLeaves");
            double? weight = JsonSupport.Opt(hp, "weight") is JsonElement we ? JsonSupport.Dbl(we, "weight") : null;
            hd = new HiddenDesktop(group, floats, null, neighbours, weight);
        }

        return new LayoutDocument(root, floating, null, hd);
    }

    private static List<FloatingPanel> ReadFloatingList(JsonElement e, string what)
    {
        var list = new List<FloatingPanel>();
        foreach (JsonElement item in JsonSupport.Arr(e, what).EnumerateArray())
        {
            var p = JsonSupport.Props(item, what + " element");
            if (ReadNode(JsonSupport.Req(p, "panel")) is not PanelNode panel)
                throw JsonSupport.Reject("floating panel is not a panel node");
            var rp = JsonSupport.Props(JsonSupport.Req(p, "rect"), "rect");
            var rect = new Rect(
                JsonSupport.Dbl(JsonSupport.Req(rp, "x"), "rect.x"),
                JsonSupport.Dbl(JsonSupport.Req(rp, "y"), "rect.y"),
                JsonSupport.Dbl(JsonSupport.Req(rp, "w"), "rect.w"),
                JsonSupport.Dbl(JsonSupport.Req(rp, "h"), "rect.h"));
            int z = JsonSupport.Opt(p, "z") is JsonElement ze ? JsonSupport.Int(ze, "z") : 0;
            Size? size = JsonSupport.Opt(p, "size") is JsonElement se ? ReadSize(se) : null;
            list.Add(new FloatingPanel(panel, rect, z, size));
        }

        return list;
    }

    private static Size ReadSize(JsonElement e)
    {
        var p = JsonSupport.Props(e, "size");
        return new Size(JsonSupport.Dbl(JsonSupport.Req(p, "w"), "size.w"), JsonSupport.Dbl(JsonSupport.Req(p, "h"), "size.h"));
    }

    private static LayoutNode ReadNode(JsonElement e)
    {
        var p = JsonSupport.Props(e, "node");
        string kind = JsonSupport.Str(JsonSupport.Req(p, "kind"), "kind");
        switch (kind)
        {
            case "split":
            {
                string id = JsonSupport.Str(JsonSupport.Req(p, "id"), "id");
                if (!AxisExtensions.TryParseRaw(JsonSupport.Str(JsonSupport.Req(p, "axis"), "axis"), out Axis axis))
                    throw JsonSupport.Reject("unknown axis");
                var weights = new List<double>();
                if (JsonSupport.Opt(p, "weights") is JsonElement we)
                {
                    foreach (JsonElement x in JsonSupport.Arr(we, "weights").EnumerateArray())
                        weights.Add(JsonSupport.Dbl(x, "weight"));
                }

                var children = new List<LayoutNode>();
                if (JsonSupport.Opt(p, "children") is JsonElement ce)
                {
                    foreach (JsonElement c in JsonSupport.Arr(ce, "children").EnumerateArray())
                        children.Add(ReadNode(c));
                }

                return new SplitNode(id, axis, weights, children);
            }

            case "panel":
            {
                string id = JsonSupport.Str(JsonSupport.Req(p, "id"), "id");
                var views = JsonSupport.StrList(JsonSupport.Opt(p, "views"), "views");
                string? selected = JsonSupport.Opt(p, "selected") is JsonElement se ? JsonSupport.Str(se, "selected") : null;
                bool tabbed = JsonSupport.Opt(p, "tabbed") is JsonElement te && JsonSupport.Bool(te, "tabbed");
                return new PanelNode(id, views, selected, tabbed);
            }

            case "desktop":
            {
                string id = JsonSupport.Opt(p, "id") is JsonElement ie ? JsonSupport.Str(ie, "id") : DesktopNode.DefaultId;
                LayoutNode? child = JsonSupport.Opt(p, "child") is JsonElement ch ? ReadNode(ch) : null;
                return new DesktopNode(child, id);
            }

            default:
                throw JsonSupport.Reject("unknown node kind");
        }
    }
}
