using System.Reflection;
using CanvasWm.Engine.Persistence;
using CanvasWm.Win32.Hosting;

namespace CanvasWm.App;

internal static class Program
{
    private const string FaultSwitch = "--diagnostic-fault=";

    [STAThread]
    private static int Main(string[] args)
    {
        // 1. One instance per user session; a second launch signals the first and exits.
        using var instance = new SingleInstance();
        if (!instance.TryAcquire())
        {
            Win32HostWindow.SignalExisting();
            return 0;
        }

        // 2. Process setup.
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), DataFiles.DataFolderName);
        using var log = new FileLog(Path.Combine(root, DataFiles.LogsFolderName));
        var hooks = new CrashHooks(log);
        hooks.Install();

        // 3. Start line.
        Version? version = Assembly.GetExecutingAssembly().GetName().Version;
        log.Write($"start: version {version}, pid {Environment.ProcessId}, command line {Environment.CommandLine}");
        DiagnosticFault diagnostic = ParseArguments(args, log);

        // 4. Compose and run.
        int code;
        HostContext? context = HostContext.Create(log, hooks, diagnostic);
        if (context is null)
        {
            code = 2;
        }
        else
        {
            try
            {
                context.Begin();
                Application.Run(context);
                code = context.ExitCode;
            }
            finally
            {
                context.Dispose();
            }
        }

        // 5. Exit.
        log.Write($"exit: code {code}");
        return code;
    }

    private static DiagnosticFault ParseArguments(string[] args, FileLog log)
    {
        DiagnosticFault result = DiagnosticFault.None;
        foreach (string arg in args)
        {
            if (arg.StartsWith(FaultSwitch, StringComparison.Ordinal))
            {
                string value = arg[FaultSwitch.Length..];
                if (value == "ui")
                {
                    result = DiagnosticFault.Ui;
                    continue;
                }

                if (value == "background")
                {
                    result = DiagnosticFault.Background;
                    continue;
                }
            }

            log.Write("ignored argument: " + arg);
        }

        return result;
    }
}
