using System.Text;
using CanvasWm.Core.Windows;
using CanvasWm.Win32.Windows;

namespace CanvasWm.Win32.Tests.WindowEnumSpec;

/// <summary>AC33: manual probe tools used by windows-verification/win32-window-enum.md.
/// With CANVASWM_MANUAL unset both tests return immediately and write nothing.</summary>
public sealed class ManualProbe
{
    private static bool Enabled => Environment.GetEnvironmentVariable("CANVASWM_MANUAL") == "1";

    private static readonly string[] FactColumns =
    [
        "Handle", "ProcessId", "ThreadId", "AppId", "AppName", "Title", "FrameX", "FrameY", "FrameWidth", "FrameHeight",
        "IsOwnProcess", "IsShellWindow", "OwnsDesktop", "IsTopLevel", "IsChild", "IsOwned", "IsAppWindow", "IsToolWindow",
        "IsNoActivate", "HasCaption", "IsResizable", "HasMaximizeButton", "MaximizeEnabled", "IsVisible", "Cloak",
        "IsMinimized", "IsFullScreen", "IsHung", "IsAccessible",
    ];

    private static string Clean(string? s) => (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    private static string Row(OsWindowFacts f)
    {
        string[] v =
        [
            f.Handle.ToString(), f.ProcessId.ToString(), f.ThreadId.ToString(), Clean(f.AppId), Clean(f.AppName), Clean(f.Title),
            f.Frame is { } r ? r.X.ToString("R") : "", f.Frame is { } r2 ? r2.Y.ToString("R") : "",
            f.Frame is { } r3 ? r3.Width.ToString("R") : "", f.Frame is { } r4 ? r4.Height.ToString("R") : "",
            f.IsOwnProcess.ToString(), f.IsShellWindow.ToString(), f.OwnsDesktop.ToString(), f.IsTopLevel.ToString(),
            f.IsChild.ToString(), f.IsOwned.ToString(), f.IsAppWindow.ToString(), f.IsToolWindow.ToString(),
            f.IsNoActivate.ToString(), f.HasCaption.ToString(), f.IsResizable.ToString(), f.HasMaximizeButton.ToString(),
            f.MaximizeEnabled?.ToString() ?? "", f.IsVisible.ToString(), f.Cloak.ToString(), f.IsMinimized.ToString(),
            f.IsFullScreen.ToString(), f.IsHung.ToString(), f.IsAccessible.ToString(),
        ];
        return string.Join('\t', v);
    }

    [Fact]
    public void DumpFacts()
    {
        if (!Enabled) { return; }
        using var rig = new Rig();
        var sb = new StringBuilder().AppendLine(string.Join('\t', FactColumns));
        foreach (OsWindowFacts f in rig.Enumerate()) { sb.AppendLine(Row(f)); }
        string path = Path.Combine(Path.GetTempPath(), "canvaswm-facts.tsv");
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine("wrote " + path);
    }

    [Fact]
    public void RecordEvents()
    {
        if (!Enabled) { return; }
        int seconds = int.TryParse(Environment.GetEnvironmentVariable("CANVASWM_PROBE_SECONDS"), out int s) && s > 0 ? s : 30;
        using var rig = new Rig();
        var sb = new StringBuilder().AppendLine("Time\tKind\tHandle\tTitle");
        rig.Loop.Invoke(() => rig.Source.Subscribe(e =>
        {
            string title = rig.Source.Describe(e.Handle)?.Title ?? "";
            lock (sb) { sb.AppendLine($"{DateTime.Now:HH:mm:ss.fff}\t{e.Kind}\t{e.Handle}\t{Clean(title)}"); }
        }));
        Console.WriteLine($"recording for {seconds} s; interact with windows now");
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        string path = Path.Combine(Path.GetTempPath(), "canvaswm-events.tsv");
        lock (sb) { File.WriteAllText(path, sb.ToString()); }
        Console.WriteLine("wrote " + path);
    }
}
