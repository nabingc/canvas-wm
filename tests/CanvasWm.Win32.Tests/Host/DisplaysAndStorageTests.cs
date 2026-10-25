using System.Text;
using CanvasWm.Core.Layout;
using CanvasWm.Engine.Persistence;
using CanvasWm.Engine.Services;
using CanvasWm.Win32.Coordinates;
using CanvasWm.Win32.Displays;
using CanvasWm.Win32.Storage;
using Xunit.Abstractions;

namespace CanvasWm.Win32.Tests.HostSpec;

/// <summary>AC25 and AC26 of specs/app-host.md.</summary>
public sealed class DisplaysAndStorageTests
{
    private readonly ITestOutputHelper _out;

    public DisplaysAndStorageTests(ITestOutputHelper output) => _out = output;

    // ---- AC25 ----

    [Fact]
    public void AC25_SnapshotSanity()
    {
        var snap = Win32Displays.Snapshot();
        Assert.NotEmpty(snap);
        Assert.Single(snap, d => d.IsMain);
        var main = snap.Single(d => d.IsMain);
        Assert.Equal(0, main.Frame.X);
        Assert.Equal(0, main.Frame.Y);
        Assert.All(snap, d => Assert.False(string.IsNullOrEmpty(d.Key.Value)));
        Assert.Equal(snap.Count, snap.Select(d => d.Key.Value).Distinct(StringComparer.Ordinal).Count());
        const double eps = 1e-6;
        foreach (var d in snap)
        {
            Assert.True(d.WorkArea.X >= d.Frame.X - eps && d.WorkArea.Y >= d.Frame.Y - eps
                        && d.WorkArea.X + d.WorkArea.Width <= d.Frame.X + d.Frame.Width + eps
                        && d.WorkArea.Y + d.WorkArea.Height <= d.Frame.Y + d.Frame.Height + eps,
                        $"work area of {d.Key} outside frame");
            Assert.True(d.Dpi >= 96);
            if (!d.Key.Value.StartsWith(@"\\?\", StringComparison.Ordinal))
                _out.WriteLine("display key is not a device path (A9): " + d.Key.Value);
        }

        var monitors = HostNative.ReadMonitors();
        Assert.Equal(monitors.Count, snap.Count);
        for (int i = 0; i < monitors.Count; i++)
        {
            var (b, _, dpi, primary) = monitors[i];
            var px = new PixelRect(b.Left, b.Top, b.Right, b.Bottom);
            Assert.Equal(DipSpace.ToDip(px, px, dpi), snap[i].Frame);
            Assert.Equal((int)dpi, snap[i].Dpi);
            Assert.Equal(primary, snap[i].IsMain);
        }

        Assert.Equal(DisplayInfo.SignatureOf(snap), DisplayInfo.SignatureOf(Win32Displays.Snapshot()));
    }

    // ---- AC26 ----

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "CanvasWmTest-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Path, true); } catch { /* best effort */ }
        }
    }

    private static string[] Tmps(string dir) =>
        System.IO.Directory.Exists(dir) ? System.IO.Directory.GetFiles(dir, "*.tmp") : [];

    [Fact]
    public void AC26_ReadWriteBasics()
    {
        using var t = new TempDir();
        var s = new DirectoryTextStore(t.Path);
        Assert.Equal(t.Path, s.Directory);
        Assert.Equal(new TextFileRead(TextFileStatus.Missing, null), s.Read("a.json"));
        Assert.True(s.Write("a.json", "h\u00e9llo\n"));
        Assert.True(System.IO.Directory.Exists(t.Path));
        byte[] bytes = File.ReadAllBytes(Path.Combine(t.Path, "a.json"));
        Assert.Equal(new UTF8Encoding(false).GetBytes("h\u00e9llo\n"), bytes);
        Assert.Equal(new TextFileRead(TextFileStatus.Ok, "h\u00e9llo\n"), s.Read("a.json"));
        Assert.True(s.Write("a.json", "second"));
        Assert.Equal("second", s.Read("a.json").Content);
        Assert.Empty(Tmps(t.Path));
    }

    [Fact]
    public void AC26_BomIsStrippedOnRead()
    {
        using var t = new TempDir();
        var s = new DirectoryTextStore(t.Path);
        System.IO.Directory.CreateDirectory(t.Path);
        File.WriteAllText(Path.Combine(t.Path, "b.json"), "bom text", new UTF8Encoding(true));
        Assert.Equal("bom text", s.Read("b.json").Content);
    }

    [Fact]
    public void AC26_Move()
    {
        using var t = new TempDir();
        var s = new DirectoryTextStore(t.Path);
        s.Write("a", "A");
        s.Write("b", "B");
        Assert.True(s.Move("a", "b"));
        Assert.Equal("A", s.Read("b").Content);
        Assert.Equal(TextFileStatus.Missing, s.Read("a").Status);
        Assert.False(s.Move("nothing", "x"));
    }

    [Fact]
    public void AC26_LockedDestinationKeepsOldContentAndLeavesNoTemp()
    {
        using var t = new TempDir();
        var s = new DirectoryTextStore(t.Path);
        Assert.True(s.Write("a.json", "old"));
        using (new FileStream(Path.Combine(t.Path, "a.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(s.Write("a.json", "new"));
            Assert.Equal(TextFileStatus.Failed, s.Read("a.json").Status);
        }

        Assert.Equal("old", s.Read("a.json").Content);
        Assert.Empty(Tmps(t.Path));
    }

    [Fact]
    public void AC26_InvalidNamesAndRelativeDirectory()
    {
        using var t = new TempDir();
        var s = new DirectoryTextStore(t.Path);
        Assert.Throws<ArgumentException>(() => s.Read("a/b"));
        Assert.Throws<ArgumentException>(() => s.Write("a:b", "x"));
        Assert.Throws<ArgumentException>(() => s.Move("ok", "bad|name"));
        Assert.Throws<ArgumentException>(() => new DirectoryTextStore("relative\\dir"));
    }

    [Fact]
    public void AC26_StoresRoundTripThroughDisk()
    {
        using var t = new TempDir();
        var layouts = new DirectoryTextStore(Path.Combine(t.Path, "layouts"));
        var root = new DirectoryTextStore(t.Path);
        var key = new EngineKey(new DisplayKey(@"\\?\DISPLAY#GSM5B7F#5&1a2b&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}"), "default");
        var rec = new SavedDisplay(new LayoutDocument(new DesktopNode(PanelNode.ForWindow("w1"))), "w1");

        var ds = new FileDisplayStore(layouts);
        ds.Save(key, rec);
        Assert.Equal(rec, new FileDisplayStore(layouts).Load(key));
        string name = DataFiles.LayoutFileName(key);
        layouts.Write(name, "{\"version\":1");
        Assert.Null(new FileDisplayStore(layouts).Load(key));
        Assert.Equal(TextFileStatus.Ok, layouts.Read(name + ".corrupt").Status);
        Assert.Equal(TextFileStatus.Missing, layouts.Read(name).Status);

        var first = SettingsFile.LoadOrCreate(root);
        Assert.Equal(HostSettings.Default, first);
        Assert.Equal(TextFileStatus.Ok, root.Read("settings.json").Status);
        Assert.Equal(first, SettingsFile.LoadOrCreate(root));

        var lim = new FileLimitStore(root);
        var app = new AppId("com.x");
        lim.Learn(app, new CanvasWm.Core.Geometry.Size(500, 300));
        var again = new FileLimitStore(root);
        Assert.Equal(new CanvasWm.Core.Geometry.Size(500, 300), again.Minimum(app));
    }
}
