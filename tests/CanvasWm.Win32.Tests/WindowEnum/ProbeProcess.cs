using System.Diagnostics;
using System.Text;

namespace CanvasWm.Win32.Tests.WindowEnumSpec;

/// <summary>An out-of-process WinForms window hosted by Windows PowerShell (A20). Commands are
/// passed through a command directory polled every 50 ms by the probe's UI thread (not stdin:
/// a UI thread cannot block on stdin, and PowerShell has no cheap background reader).
/// Commands: "title X", "fixed", "child", "focus", "hang".
/// "focus" clears the active control first: the text box already has focus at start-up, so
/// re-focusing it alone raises no focus event. "hang" busy-waits: Start-Sleep on the STA UI
/// thread pumps messages, so Windows never reports the window as hung. "fixed" drops the maximize
/// box before the sizing border, because AC26 starts asserting as soon as it sees !IsResizable.</summary>
internal sealed class ProbeProcess : IDisposable
{
    private const string Script = """
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$script:f = New-Object System.Windows.Forms.Form
$script:f.Text = $env:CWM_TITLE
$script:f.StartPosition = 'Manual'
$script:f.Location = New-Object System.Drawing.Point([int]$env:CWM_X, [int]$env:CWM_Y)
$script:f.Size = New-Object System.Drawing.Size([int]$env:CWM_W, [int]$env:CWM_H)
$script:tb = New-Object System.Windows.Forms.TextBox
$script:tb.Location = New-Object System.Drawing.Point(10, 10)
$script:f.Controls.Add($script:tb)
$script:dir = $env:CWM_CMDDIR
$script:f.Add_Shown({
  [Console]::Out.WriteLine('HWND ' + $script:f.Handle.ToInt64())
  [Console]::Out.Flush()
})
$script:t = New-Object System.Windows.Forms.Timer
$script:t.Interval = 50
$script:t.Add_Tick({
  foreach ($file in (Get-ChildItem -Path $script:dir -Filter '*.cmd' | Sort-Object Name)) {
    $line = (Get-Content -Path $file.FullName -Raw).Trim()
    Remove-Item -Path $file.FullName -Force
    if ($line.StartsWith('title ')) { $script:f.Text = $line.Substring(6) }
    elseif ($line -eq 'fixed') { $script:f.MaximizeBox = $false; $script:f.FormBorderStyle = 'FixedDialog' }
    elseif ($line -eq 'child') {
      $c = New-Object System.Windows.Forms.Label
      $c.Text = 'child'
      $c.Location = New-Object System.Drawing.Point(10, 60)
      $script:f.Controls.Add($c)
      $null = $c.Handle
      [Console]::Out.WriteLine('CHILD ' + $c.Handle.ToInt64())
      [Console]::Out.Flush()
      $c.Left = $c.Left + 20
      $c.Top = $c.Top + 20
    }
    elseif ($line -eq 'focus') { $script:f.Activate(); $script:f.ActiveControl = $null; $null = $script:tb.Focus() }
    elseif ($line -eq 'hang') { $e = [DateTime]::UtcNow.AddSeconds(15); while ([DateTime]::UtcNow -lt $e) { } }
  }
})
$script:t.Start()
[System.Windows.Forms.Application]::Run($script:f)
""";

    private readonly Process _proc;
    private readonly string _cmdDir;
    private int _seq;
    private readonly List<string> _lines = [];
    private readonly object _gate = new();

    private ProbeProcess(Process proc, string cmdDir, string title)
    {
        _proc = proc;
        _cmdDir = cmdDir;
        Title = title;
    }

    internal string Title { get; }

    internal ulong Handle { get; private set; }

    internal int ProcessId => _proc.Id;

    internal string ImagePath => _proc.MainModule!.FileName;

    internal static ProbeProcess Start(int x = 120, int y = 120, int w = 640, int h = 480)
    {
        string title = "CanvasWmProbe-" + Guid.NewGuid().ToString("N");
        string cmdDir = Path.Combine(Path.GetTempPath(), "canvaswm-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cmdDir);
        string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        var psi = new ProcessStartInfo(ps)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-WindowStyle");
        psi.ArgumentList.Add("Hidden");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(Script)));
        psi.Environment["CWM_TITLE"] = title;
        psi.Environment["CWM_X"] = x.ToString();
        psi.Environment["CWM_Y"] = y.ToString();
        psi.Environment["CWM_W"] = w.ToString();
        psi.Environment["CWM_H"] = h.ToString();
        psi.Environment["CWM_CMDDIR"] = cmdDir;

        Process proc = Process.Start(psi)!;
        var probe = new ProbeProcess(proc, cmdDir, title);
        var thread = new Thread(() =>
        {
            string? line;
            while ((line = proc.StandardOutput.ReadLine()) is not null)
            {
                lock (probe._gate) { probe._lines.Add(line); }
            }
        }) { IsBackground = true };
        thread.Start();

        string hwnd = probe.WaitLine("HWND ", TimeSpan.FromSeconds(30));
        probe.Handle = unchecked((ulong)long.Parse(hwnd));
        return probe;
    }

    internal ulong WaitChildHandle() => unchecked((ulong)long.Parse(WaitLine("CHILD ", TimeSpan.FromSeconds(10))));

    /// <summary>Queues a command for the probe's UI thread.</summary>
    internal void Send(string command)
    {
        string name = $"{Interlocked.Increment(ref _seq):D6}";
        string tmp = Path.Combine(_cmdDir, name + ".tmp");
        File.WriteAllText(tmp, command);
        File.Move(tmp, Path.Combine(_cmdDir, name + ".cmd"));
    }

    private string WaitLine(string prefix, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            lock (_gate)
            {
                string? hit = _lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
                if (hit is not null)
                {
                    _lines.Remove(hit);
                    return hit[prefix.Length..].Trim();
                }
            }

            if (_proc.HasExited)
            {
                throw new InvalidOperationException("probe exited early: " + prefix);
            }

            Thread.Sleep(25);
        }

        throw new TimeoutException("probe did not print " + prefix);
    }

    public void Dispose()
    {
        try { if (!_proc.HasExited) { _proc.Kill(true); } } catch (InvalidOperationException) { }
        _proc.Dispose();
        try { Directory.Delete(_cmdDir, true); } catch (IOException) { }
    }
}
