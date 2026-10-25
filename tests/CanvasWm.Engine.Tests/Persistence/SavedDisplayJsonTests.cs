using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Persistence;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.PersistenceSpec;

internal sealed record TestRestore : RestoreTarget;

public sealed class SavedDisplayJsonTests
{
    internal static readonly EngineKey KA = new(new DisplayKey("A"), "default");

    private static WindowKey W(string n) => new(n);

    private static SavedDisplay Rich(EngineKey? owner = null)
    {
        var panelSingle = PanelNode.ForWindow("w1");
        var tabbed = new PanelNode("p2", new[] { "w2", "w3" }, null, true);
        var emptyDesk = new DesktopNode(null, "d9");
        var split = new SplitNode("s1", Axis.Y, new[] { 1.0, 2.5, 1.0 }, new LayoutNode[] { panelSingle, tabbed, emptyDesk });
        var root = new DesktopNode(split);
        var f1 = new FloatingPanel(PanelNode.ForWindow("w4"), new Rect(10, 20, 300, 200), 1, new Size(300, 200));
        var f2 = new FloatingPanel(PanelNode.ForWindow("w5"), new Rect(0.1, 0.2, 0.3, 0.4), 2);
        var hd = new HiddenDesktop("g", new[] { f1, f2 }, null, new[] { "n1", "n2" }, 0.3);
        var doc = new LayoutDocument(root, new[] { f1, f2 }, null, hd);
        var framings = new Dictionary<string, IReadOnlyList<string>>
        {
            ["1"] = new[] { "w1" },
            ["2"] = new[] { "p2", "w1" },
        };
        var owners = new Dictionary<WindowKey, string> { [W("w1")] = (owner ?? KA).Value, [W("w2")] = "B|default" };
        return new SavedDisplay(doc, "p2", framings, owners,
            new Dictionary<WindowKey, DockSpot>(), new Dictionary<WindowKey, Size> { [W("w4")] = new Size(300, 200) });
    }

    private static SavedDisplay RoundTrip(EngineKey k, SavedDisplay r, out string text)
    {
        text = SavedDisplayJson.Encode(k, r);
        Assert.True(SavedDisplayJson.TryDecode(text, out var k2, out var r2, out var err), err);
        Assert.Equal(k, k2);
        return r2!;
    }

    [Fact]
    public void AC2_ValidateAccepts()
    {
        foreach (var n in new[] { "limits.json", "arrangement-ab.json", "a b.json.corrupt" })
            TextFileNames.Validate(n);
    }

    [Fact]
    public void AC2_ValidateRejects()
    {
        Assert.Throws<ArgumentNullException>(() => TextFileNames.Validate(null!));
        foreach (var n in new[] { "", ".", "..", "a/b", "a\\b", "a:b", "a|b", "a?", "a*", "a<", "a>", "a\"", "a\u0001", "name.", "name ", new string('x', 201) })
            Assert.Throws<ArgumentException>(() => TextFileNames.Validate(n));
        TextFileNames.Validate(new string('x', 200));
    }

    [Fact]
    public void AC2_LayoutFileNameAndConstants()
    {
        string expected = "arrangement-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("A|default"))) + ".json";
        Assert.Equal(expected, DataFiles.LayoutFileName(KA));
        Assert.Equal(81, expected.Length);
        TextFileNames.Validate(expected);
        Assert.NotEqual(expected, DataFiles.LayoutFileName(new EngineKey(new DisplayKey("A"), "d2")));
        var dev = new EngineKey(new DisplayKey(@"\\?\DISPLAY#GSM5B7F#5&1a2b&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"), "default");
        string n2 = DataFiles.LayoutFileName(dev);
        Assert.Equal(81, n2.Length);
        TextFileNames.Validate(n2);
        Assert.Equal("CanvasWM", DataFiles.DataFolderName);
        Assert.Equal("layouts", DataFiles.LayoutsFolderName);
        Assert.Equal("logs", DataFiles.LogsFolderName);
        Assert.Equal("limits.json", DataFiles.LimitsFileName);
        Assert.Equal("settings.json", DataFiles.SettingsFileName);
        Assert.Equal("canvaswm.log", DataFiles.LogFileName);
        Assert.Equal(".corrupt", DataFiles.CorruptSuffix);
    }

    [Fact]
    public void AC3_RichRoundTrip()
    {
        var r = Rich();
        var back = RoundTrip(KA, r, out _);
        Assert.Equal(r, back);
        Assert.NotNull(back.DockSpots);
        Assert.Empty(back.DockSpots!);
        Assert.NotNull(back.FloatSizes);
    }

    [Fact]
    public void AC3_NullDockSpotsAndFloatSizes()
    {
        var r = new SavedDisplay(new LayoutDocument(new DesktopNode(PanelNode.ForWindow("w1"))));
        var back = RoundTrip(KA, r, out string text);
        Assert.Equal(r, back);
        Assert.Null(back.DockSpots);
        Assert.Null(back.FloatSizes);
        Assert.DoesNotContain("dockSpots", text);
        Assert.DoesNotContain("floatSizes", text);
    }

    [Fact]
    public void AC3_NonFiniteNumbers()
    {
        var split = new SplitNode("s", Axis.X, new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity },
            new LayoutNode[] { PanelNode.ForWindow("a"), PanelNode.ForWindow("b"), PanelNode.ForWindow("c") });
        var fl = new FloatingPanel(PanelNode.ForWindow("f"), new Rect(double.NegativeInfinity, 1, 2, 3), 0);
        var r = new SavedDisplay(new LayoutDocument(new DesktopNode(split), new[] { fl }));
        var back = RoundTrip(KA, r, out string text);
        Assert.Equal(r, back);
        Assert.Contains("\"NaN\"", text);
        Assert.Contains("\"Infinity\"", text);
        Assert.Contains("\"-Infinity\"", text);
    }

    [Fact]
    public void AC3_AwkwardKeys()
    {
        var k = new EngineKey(new DisplayKey("\\\\?\\DISPLAY#x{y}|\"q\"\u00e9\u4e2d"), "d\u00e9fault");
        var r = new SavedDisplay(new LayoutDocument(null), null, null,
            new Dictionary<WindowKey, string> { [W("w\u00e9\u4e2d")] = k.Value },
            null, new Dictionary<WindowKey, Size> { [W("\u00fc\"|")] = new Size(1, 2) });
        var back = RoundTrip(k, r, out _);
        Assert.Equal(r, back);
    }

    [Fact]
    public void AC4_WireShape()
    {
        var split = new SplitNode("s1", Axis.X, new[] { 1.0, 2.0 }, new LayoutNode[]
        {
            PanelNode.ForWindow("w1"),
            new PanelNode("p2", new[] { "w2", "w3" }, "w3", true),
        });
        var fl = new FloatingPanel(PanelNode.ForWindow("w4"), new Rect(10, 20, 300, 200), 1, new Size(300, 200));
        var r = new SavedDisplay(new LayoutDocument(new DesktopNode(split), new[] { fl }), "p2",
            new Dictionary<string, IReadOnlyList<string>> { ["1"] = new[] { "w1" }, ["2"] = new[] { "p2", "w1" } },
            new Dictionary<WindowKey, string> { [W("w1")] = "A|default" },
            null, new Dictionary<WindowKey, Size> { [W("w4")] = new Size(300, 200) });
        const string expected = """
        {
          "version": 1,
          "engineKey": "A|default",
          "doc": {
            "root": {
              "kind": "desktop", "id": "desktop",
              "child": {
                "kind": "split", "id": "s1", "axis": "x", "weights": [1, 2],
                "children": [
                  { "kind": "panel", "id": "w1", "views": ["w1"], "selected": "w1", "tabbed": false },
                  { "kind": "panel", "id": "p2", "views": ["w2", "w3"], "selected": "w3", "tabbed": true }
                ]
              }
            },
            "floating": [
              { "panel": { "kind": "panel", "id": "w4", "views": ["w4"], "selected": "w4", "tabbed": false },
                "rect": { "x": 10, "y": 20, "w": 300, "h": 200 }, "z": 1, "size": { "w": 300, "h": 200 } }
            ],
            "hidden": [],
            "hiddenDesktop": null
          },
          "framed": "p2",
          "framings": { "1": ["w1"], "2": ["p2", "w1"] },
          "owners": { "w1": "A|default" },
          "floatSizes": { "w4": { "w": 300, "h": 200 } }
        }
        """;
        string text = SavedDisplayJson.Encode(KA, r);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(text), JsonNode.Parse(expected)), text);

        var names = JsonNode.Parse(text)!.AsObject().Select(p => p.Key).ToArray();
        Assert.Equal(new[] { "version", "engineKey", "doc", "framed", "framings", "owners", "floatSizes" }, names);
        Assert.DoesNotContain('\r', text);
        Assert.False(text.StartsWith('\uFEFF'));
        Assert.EndsWith("\n", text);
        Assert.False(text.EndsWith("\n\n"));
    }

    [Fact]
    public void AC4_InsertionOrderIndependent()
    {
        SavedDisplay Make(bool rev)
        {
            var o = new List<KeyValuePair<WindowKey, string>> { new(W("a"), "x"), new(W("b"), "y"), new(W("c"), "z") };
            if (rev) o.Reverse();
            var f = new List<KeyValuePair<string, IReadOnlyList<string>>>
            {
                new("1", new[] { "a" }), new("2", new[] { "b" }),
            };
            if (rev) f.Reverse();
            var d = new Dictionary<WindowKey, string>(o);
            return new SavedDisplay(new LayoutDocument(null), null, new Dictionary<string, IReadOnlyList<string>>(f), d);
        }

        Assert.Equal(SavedDisplayJson.Encode(KA, Make(false)), SavedDisplayJson.Encode(KA, Make(true)));
    }

    [Fact]
    public void AC4_NaNWeightIsString()
    {
        var split = new SplitNode("s", Axis.X, new[] { double.NaN }, new LayoutNode[] { PanelNode.ForWindow("a") });
        string text = SavedDisplayJson.Encode(KA, new SavedDisplay(new LayoutDocument(new DesktopNode(split))));
        var w = JsonNode.Parse(text)!["doc"]!["root"]!["child"]!["weights"]![0]!;
        Assert.Equal("NaN", (string)w!);
    }

    private static SavedDisplay Decode(string json)
    {
        Assert.True(SavedDisplayJson.TryDecode(json, out var k, out var d, out var e), e);
        Assert.Equal(KA, k);
        return d!;
    }

    [Fact]
    public void AC5_Defaults()
    {
        var d = Decode("""{"version":1,"engineKey":"A|default","doc":{}}""");
        Assert.Equal(LayoutDocument.Empty, d.Document);
        Assert.Null(d.FramedNodeId);
        Assert.Empty(d.Framings);
        Assert.Empty(d.Owners);
        Assert.Null(d.DockSpots);
        Assert.Null(d.FloatSizes);
    }

    [Fact]
    public void AC5_PanelAndNodeDefaults()
    {
        var d = Decode("""{"version":1,"engineKey":"A|default","doc":{"root":{"kind":"desktop","child":{"kind":"split","id":"s","axis":"y","children":[{"kind":"panel","id":"p"},{"kind":"panel","id":"q","selected":null}]}},"floating":null}}""");
        var desk = Assert.IsType<DesktopNode>(d.Document.Root);
        Assert.Equal("desktop", desk.Id);
        var s = Assert.IsType<SplitNode>(desk.Child);
        Assert.Equal(Axis.Y, s.Axis);
        Assert.Empty(s.Weights);
        Assert.Equal(2, s.Children.Length);
        foreach (var c in s.Children)
        {
            var p = Assert.IsType<PanelNode>(c);
            Assert.Null(p.Selected);
            Assert.False(p.Tabbed);
            Assert.Empty(p.Views);
        }

        Assert.Empty(d.Document.Floating);
        var s2 = Decode("""{"version":1,"engineKey":"A|default","doc":{"root":{"kind":"split","id":"s","axis":"x"}}}""");
        var sp = Assert.IsType<SplitNode>(s2.Document.Root);
        Assert.Empty(sp.Weights);
        Assert.Empty(sp.Children);
    }

    [Fact]
    public void AC5_UnknownKeysIgnoredAndLastDuplicateWins()
    {
        var d = Decode("""{"version":1,"zzz":[1],"engineKey":"A|default","doc":{"extra":{"a":1},"root":{"kind":"panel","id":"p","novel":true}},"owners":{"w1":"a","w1":"b"}}""");
        Assert.Equal("b", d.Owners[W("w1")]);
        Assert.IsType<PanelNode>(d.Document.Root);
    }

    public static IEnumerable<object[]> Bad()
    {
        const string H = "\"version\":1,\"engineKey\":\"A|default\"";
        string Ok = "{" + H + ",\"doc\":{}}";
        string Doc(string d) => "{" + H + ",\"doc\":" + d + "}";
        string Node(string n) => Doc("{\"root\":" + n + "}");
        yield return new object[] { "" };
        yield return new object[] { "nope" };
        yield return new object[] { "[]" };
        yield return new object[] { Ok + "x" };
        yield return new object[] { "{/*c*/" + H + ",\"doc\":{}}" };
        yield return new object[] { "{" + H + ",\"doc\":{},}" };
        yield return new object[] { "{\"engineKey\":\"A|default\",\"doc\":{}}" };
        yield return new object[] { "{\"version\":2,\"engineKey\":\"A|default\",\"doc\":{}}" };
        yield return new object[] { "{\"version\":\"1\",\"engineKey\":\"A|default\",\"doc\":{}}" };
        yield return new object[] { "{\"version\":1,\"doc\":{}}" };
        yield return new object[] { "{\"version\":1,\"engineKey\":\"nopipe\",\"doc\":{}}" };
        yield return new object[] { "{\"version\":1,\"engineKey\":\"A|\",\"doc\":{}}" };
        yield return new object[] { "{\"version\":1,\"engineKey\":5,\"doc\":{}}" };
        yield return new object[] { "{" + H + "}" };
        yield return new object[] { "{" + H + ",\"doc\":null}" };
        yield return new object[] { "{" + H + ",\"doc\":[]}" };
        yield return new object[] { Node("{\"kind\":\"tree\",\"id\":\"x\"}") };
        yield return new object[] { Node("{\"id\":\"x\"}") };
        yield return new object[] { Node("{\"kind\":\"split\",\"id\":\"s\",\"axis\":\"z\"}") };
        yield return new object[] { Node("{\"kind\":\"split\",\"id\":\"s\"}") };
        yield return new object[] { Node("{\"kind\":\"split\",\"axis\":\"x\"}") };
        yield return new object[] { Node("{\"kind\":\"panel\"}") };
        yield return new object[] { Node("{\"kind\":\"split\",\"id\":\"s\",\"axis\":\"x\",\"children\":[null]}") };
        yield return new object[] { Node("{\"kind\":\"split\",\"id\":\"s\",\"axis\":\"x\",\"weights\":[\"abc\"]}") };
        yield return new object[] { Node("{\"kind\":\"split\",\"id\":\"s\",\"axis\":\"x\",\"weights\":[\"nan\"]}") };
        yield return new object[] { Node("{\"kind\":\"panel\",\"id\":\"p\",\"tabbed\":\"yes\"}") };
        yield return new object[] { Node("{\"kind\":\"panel\",\"id\":\"p\",\"views\":[null]}") };
        yield return new object[] { Doc("{\"floating\":[{\"panel\":{\"kind\":\"panel\",\"id\":\"p\"},\"rect\":{\"x\":1,\"y\":1,\"w\":1,\"h\":1},\"z\":1.5}]}") };
        yield return new object[] { Doc("{\"floating\":[{\"panel\":{\"kind\":\"desktop\",\"id\":\"d\"},\"rect\":{\"x\":1,\"y\":1,\"w\":1,\"h\":1},\"z\":1}]}") };
        yield return new object[] { Doc("{\"hiddenDesktop\":{\"restore\":{}}}") };
        yield return new object[] { Doc("{\"hidden\":[{}]}") };
        yield return new object[] { "{" + H + ",\"doc\":{},\"dockSpots\":{\"w1\":{}}}" };
        yield return new object[] { "{" + H + ",\"doc\":{},\"owners\":{\"\":\"x\"}}" };
        yield return new object[] { Node(string.Concat(Enumerable.Repeat("{\"kind\":\"desktop\",\"child\":", 300)) + "null" + new string('}', 300)) };
    }

    [Theory]
    [MemberData(nameof(Bad))]
    public void AC6_Rejects(string json)
    {
        bool ok = SavedDisplayJson.TryDecode(json, out var k, out var d, out var err);
        Assert.False(ok);
        Assert.Null(k);
        Assert.Null(d);
        Assert.False(string.IsNullOrEmpty(err));
        Assert.DoesNotContain('\n', err);
    }

    [Fact]
    public void AC6_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => SavedDisplayJson.TryDecode(null!, out _, out _, out _));
    }

    [Fact]
    public void AC7_EncodeUnsupported()
    {
        foreach (var r in Unsupported())
            Assert.Throws<NotSupportedException>(() => SavedDisplayJson.Encode(KA, r));
    }

    private static IEnumerable<SavedDisplay> Unsupported()
    {
        yield return new SavedDisplay(new LayoutDocument(null, null, new[] { new HiddenPanel(PanelNode.ForWindow("h"), new TestRestore()) }));
        yield return new SavedDisplay(new LayoutDocument(null, null, null, new HiddenDesktop(null, Array.Empty<FloatingPanel>(), new TestRestore(), Array.Empty<string>(), null)));
        yield return new SavedDisplay(new LayoutDocument(null), null, null, null,
            new Dictionary<WindowKey, DockSpot> { [W("w")] = new DockSpot(new TestRestore(), Array.Empty<string>()) });
    }

    [Fact]
    public void AC7_StoreSkipsUnsupported()
    {
        foreach (var r in Unsupported())
        {
            var files = new InMemoryTextFileStore();
            var log = new List<string>();
            var store = new FileDisplayStore(files, log.Add);
            store.Save(KA, r);
            Assert.Empty(files.Writes);
            Assert.Single(log);
        }
    }
}
