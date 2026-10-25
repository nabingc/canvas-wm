using CanvasWm.Core.Geometry;
using CanvasWm.Core.Layout;
using CanvasWm.Core.Shortcuts;
using CanvasWm.Engine.Persistence;
using CanvasWm.Engine.Services;
using CanvasWm.Engine.Tests.Fakes;

namespace CanvasWm.Engine.Tests.PersistenceSpec;

public sealed class StoreAndSettingsTests
{
    private static readonly EngineKey KA = new(new DisplayKey("A"), "default");
    private static readonly EngineKey KB = new(new DisplayKey("B"), "default");

    private static SavedDisplay Rec(string w) =>
        new(new LayoutDocument(new DesktopNode(PanelNode.ForWindow(w))), w);

    // ---- AC8 FileDisplayStore ----

    [Fact]
    public void AC8_SaveLoadAndSkipUnchanged()
    {
        var f = new InMemoryTextFileStore();
        var s = new FileDisplayStore(f);
        var r = Rec("w1");
        s.Save(KA, r);
        var w = Assert.Single(f.Writes);
        Assert.Equal(DataFiles.LayoutFileName(KA), w.Name);
        Assert.Equal(r, s.Load(KA));
        s.Save(KA, r);
        Assert.Single(f.Writes);
        s.Save(KA, Rec("w2"));
        Assert.Equal(2, f.Writes.Count);
    }

    [Fact]
    public void AC8_LoadNeverSavedIsQuiet()
    {
        var f = new InMemoryTextFileStore();
        var log = new List<string>();
        Assert.Null(new FileDisplayStore(f, log.Add).Load(KA));
        Assert.Empty(f.Writes);
        Assert.Empty(f.Moves);
        Assert.Empty(log);
    }

    [Fact]
    public void AC8_CorruptMovedOnceThenMissing()
    {
        var f = new InMemoryTextFileStore();
        var log = new List<string>();
        string name = DataFiles.LayoutFileName(KA);
        f.Seed(name, "{\"version\":1");
        var s = new FileDisplayStore(f, log.Add);
        Assert.Null(s.Load(KA));
        Assert.Equal(new[] { (name, name + ".corrupt") }, f.Moves);
        Assert.Single(log);
        Assert.Null(s.Load(KA));
        Assert.Single(f.Moves);
        Assert.Single(log);
        Assert.Equal("{\"version\":1", f.Files[name + ".corrupt"]);
    }

    [Fact]
    public void AC8_OtherKeyIgnoredAndUntouched()
    {
        var f = new InMemoryTextFileStore();
        string text = SavedDisplayJson.Encode(KB, Rec("w1"));
        string name = DataFiles.LayoutFileName(KA);
        f.Seed(name, text);
        var s = new FileDisplayStore(f);
        Assert.Null(s.Load(KA));
        Assert.Empty(f.Moves);
        Assert.Equal(text, f.Files[name]);
    }

    [Fact]
    public void AC8_ReadFailure()
    {
        var f = new InMemoryTextFileStore();
        var log = new List<string>();
        string name = DataFiles.LayoutFileName(KA);
        f.Seed(name, SavedDisplayJson.Encode(KA, Rec("w1")));
        f.FailRead.Add(name);
        Assert.Null(new FileDisplayStore(f, log.Add).Load(KA));
        Assert.Empty(f.Moves);
        Assert.Single(log);
    }

    [Fact]
    public void AC8_WriteFailureNotRemembered()
    {
        var f = new InMemoryTextFileStore();
        var log = new List<string>();
        string name = DataFiles.LayoutFileName(KA);
        f.FailWrite.Add(name);
        var s = new FileDisplayStore(f, log.Add);
        s.Save(KA, Rec("w1"));
        Assert.Single(log);
        Assert.Single(f.Writes);
        s.Save(KA, Rec("w1"));
        Assert.Equal(2, f.Writes.Count);
        f.FailWrite.Clear();
        s.Save(KA, Rec("w1"));
        Assert.Equal(3, f.Writes.Count);
        s.Save(KA, Rec("w1"));
        Assert.Equal(3, f.Writes.Count);
    }

    [Fact]
    public void AC8_NullArguments()
    {
        var s = new FileDisplayStore(new InMemoryTextFileStore());
        Assert.Throws<ArgumentNullException>(() => s.Load(null!));
        Assert.Throws<ArgumentNullException>(() => s.Save(null!, Rec("w")));
        Assert.Throws<ArgumentNullException>(() => s.Save(KA, null!));
        Assert.Throws<ArgumentNullException>(() => new FileDisplayStore(null!));
    }

    // ---- AC9 FileLimitStore ----

    private static readonly AppId App = new("com.x");

    [Fact]
    public void AC9_FreshHasNoWrites()
    {
        var f = new InMemoryTextFileStore();
        var s = new FileLimitStore(f);
        Assert.Empty(s.Entries);
        Assert.Empty(f.Writes);
        Assert.Null(s.Minimum(App));
    }

    [Fact]
    public void AC9_MatchesInMemoryAndWritesOnChange()
    {
        var f = new InMemoryTextFileStore();
        var s = new FileLimitStore(f);
        var m = new InMemoryLimitStore();
        void Step(Action<ILimitStore> a, int expectWrites)
        {
            a(s);
            a(m);
            Assert.Equal(m.Minimum(App), s.Minimum(App));
            Assert.Equal(expectWrites, f.Writes.Count);
            if (f.Writes.Count > 0)
            {
                Assert.True(LimitsJson.TryDecode(f.Writes[^1].Content, out var dec, out _));
                Assert.Equal(s.Entries.OrderBy(k => k.Key.Value), dec!.OrderBy(k => k.Key.Value));
            }
        }

        Step(x => x.Learn(App, new Size(500, 300)), 1);
        Step(x => x.Learn(App, new Size(400, 350)), 2);
        Step(x => x.Observe(App, new Size(450, 360)), 3);
        Step(x => x.Observe(App, new Size(499, 349)), 3);
        Assert.Equal(m.Minimum(App), s.Minimum(App));

        var again = new FileLimitStore(f);
        Assert.Equal(s.Entries.OrderBy(k => k.Key.Value), again.Entries.OrderBy(k => k.Key.Value));
        Assert.Equal(3, f.Writes.Count);
    }

    [Fact]
    public void AC9_CorruptMovedAside()
    {
        var f = new InMemoryTextFileStore();
        var log = new List<string>();
        f.Seed("limits.json", "garbage");
        var s = new FileLimitStore(f, log.Add);
        Assert.Empty(s.Entries);
        Assert.Equal(new[] { ("limits.json", "limits.json.corrupt") }, f.Moves);
        Assert.Single(log);
        Assert.Empty(f.Writes);
    }

    [Fact]
    public void AC9_WriteFailureKeepsMemory()
    {
        var f = new InMemoryTextFileStore();
        var log = new List<string>();
        f.FailWrite.Add("limits.json");
        var s = new FileLimitStore(f, log.Add);
        s.Learn(App, new Size(500, 300));
        Assert.Equal(new Size(500, 300), s.Minimum(App));
        Assert.Single(log);
    }

    [Fact]
    public void AC9_IgnoredCallsDoNotWrite()
    {
        var f = new InMemoryTextFileStore();
        var s = new FileLimitStore(f);
        s.Learn(null, new Size(1, 1));
        s.Observe(null, new Size(1, 1));
        s.Learn(App, new Size(double.NaN, 1));
        Assert.Empty(f.Writes);
    }

    [Fact]
    public void AC9_LimitsJsonRejects()
    {
        foreach (var j in new[]
        {
            """{"version":1,"limits":{"a":{"w":-1,"h":2}}}""",
            """{"version":1,"limits":{"a":{"w":"NaN","h":2}}}""",
            """{"version":1,"limits":{"":{"w":1,"h":2}}}""",
            """{"version":2,"limits":{}}""",
            """{"version":1}""",
            """{"version":1,"limits":[]}""",
            "nope",
        })
        {
            Assert.False(LimitsJson.TryDecode(j, out var l, out var e), j);
            Assert.Null(l);
            Assert.False(string.IsNullOrEmpty(e));
        }

        Assert.True(LimitsJson.TryDecode("""{"version":1,"limits":{"a":{"w":0,"h":2}}}""", out _, out _));
    }

    // ---- AC10 settings ----

    [Fact]
    public void AC10_DefaultAndRoundTrip()
    {
        var d = HostSettings.Default;
        Assert.Equal(ShortcutProfile.WinAlt, d.Profile);
        Assert.Same(ShortcutProfiles.WinAlt, d.Shortcuts);
        var v = d.ToValues();
        Assert.Equal(16, v.Count);
        Assert.Equal("WinAlt", v["shortcuts.profile"]);
        Assert.True(v.ContainsKey(ShortcutStore.VersionKey));
        Assert.Equal(d, HostSettings.FromValues(v));

        var c = new HostSettings(ShortcutProfile.CtrlAlt, ShortcutProfiles.CtrlAlt);
        Assert.Equal(c, HostSettings.FromValues(c.ToValues()));
        var unb = new HostSettings(ShortcutProfile.WinAlt, ShortcutProfiles.WinAlt.WithAction(ShortcutAction.Right, null));
        var back = HostSettings.FromValues(unb.ToValues());
        Assert.Equal(unb, back);
        Assert.Null(back.Shortcuts[ShortcutAction.Right]);
    }

    [Fact]
    public void AC10_ProfileSelection()
    {
        var c = HostSettings.FromValues(new Dictionary<string, string> { ["shortcuts.profile"] = "CtrlAlt" });
        Assert.Equal(ShortcutProfile.CtrlAlt, c.Profile);
        Assert.Equal(ShortcutProfiles.CtrlAlt, c.Shortcuts);
        foreach (var bad in new[] { "Foo", "ctrlalt" })
            Assert.Equal(ShortcutProfile.WinAlt, HostSettings.FromValues(new Dictionary<string, string> { ["shortcuts.profile"] = bad }).Profile);
        Assert.Throws<ArgumentOutOfRangeException>(() => new HostSettings((ShortcutProfile)99, ShortcutProfiles.WinAlt));
        Assert.Throws<ArgumentNullException>(() => new HostSettings(ShortcutProfile.WinAlt, null!));
    }

    [Fact]
    public void AC10_InitAccessorsValidate()
    {
        // Spec: "HostSettings validates in its init accessors" (a `with` expression runs the accessor).
        Assert.Throws<ArgumentOutOfRangeException>(() => HostSettings.Default with { Profile = (ShortcutProfile)99 });
        Assert.Throws<ArgumentNullException>(() => HostSettings.Default with { Shortcuts = null! });
    }

    [Fact]
    public void AC10_LoadOrCreateMissing()
    {
        var f = new InMemoryTextFileStore();
        var s = SettingsFile.LoadOrCreate(f);
        Assert.Equal(HostSettings.Default, s);
        var w = Assert.Single(f.Writes);
        Assert.Equal("settings.json", w.Name);
        Assert.True(SettingsJson.TryDecode(w.Content, out var vals, out _));
        Assert.Equal(HostSettings.Default.ToValues().OrderBy(k => k.Key), vals!.OrderBy(k => k.Key));
    }

    [Fact]
    public void AC10_LoadOrCreateExistingNotRewritten()
    {
        var f = new InMemoryTextFileStore();
        var vals = new Dictionary<string, string>(HostSettings.Default.ToValues())
        {
            ["shortcuts.profile"] = "CtrlAlt",
            [ShortcutStore.ActionKey(ShortcutAction.Right)] = "none",
            ["x.y"] = "z",
        };
        f.Seed("settings.json", SettingsJson.Encode(vals));
        var s = SettingsFile.LoadOrCreate(f);
        Assert.Equal(ShortcutProfile.CtrlAlt, s.Profile);
        Assert.Null(s.Shortcuts[ShortcutAction.Right]);
        Assert.NotNull(s.Shortcuts[ShortcutAction.Left]);
        Assert.Empty(f.Writes);
        Assert.Empty(f.Moves);
    }

    [Fact]
    public void AC10_LoadOrCreateCorrupt()
    {
        var f = new InMemoryTextFileStore();
        var log = new List<string>();
        f.Seed("settings.json", "{\"version\":1");
        var s = SettingsFile.LoadOrCreate(f, log.Add);
        Assert.Equal(HostSettings.Default, s);
        Assert.Equal(new[] { ("settings.json", "settings.json.corrupt") }, f.Moves);
        Assert.Single(f.Writes);
        Assert.NotEmpty(log);
    }

    [Fact]
    public void AC10_LoadOrCreateReadFailure()
    {
        var f = new InMemoryTextFileStore();
        f.Seed("settings.json", "x");
        f.FailRead.Add("settings.json");
        Assert.Equal(HostSettings.Default, SettingsFile.LoadOrCreate(f));
        Assert.Empty(f.Writes);
        Assert.Empty(f.Moves);
    }

    [Fact]
    public void AC10_SettingsJson()
    {
        foreach (var j in new[]
        {
            """{"version":1,"values":{"a":1}}""", """{"version":1,"values":{"a":null}}""", """{"version":1,"values":{"a":{}}}""",
            """{"version":2,"values":{}}""", """{"version":1}""", "x",
        })
        {
            Assert.False(SettingsJson.TryDecode(j, out var v, out var e), j);
            Assert.Null(v);
            Assert.False(string.IsNullOrEmpty(e));
        }

        var d = new Dictionary<string, string> { ["ké"] = "v中" };
        Assert.True(SettingsJson.TryDecode(SettingsJson.Encode(d), out var back, out _));
        Assert.Equal(d, back);
    }
}
