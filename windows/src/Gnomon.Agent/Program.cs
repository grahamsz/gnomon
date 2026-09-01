using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using Gnomon.Core;
using Serilog;

namespace Gnomon.Agent;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
        {
            if (Environment.UserInteractive) return RunInteractiveWatchdog();
            ServiceBase.Run(new GnomonWindowsService());
            return 0;
        }

        if (args.Contains("--configure", StringComparer.OrdinalIgnoreCase) && !IsAdministrator())
            return RelaunchConfigurationAsAdministrator();

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        var paths = AgentPaths.Create();
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        ConfigureLogging(paths);

        try
        {
            var config = LoadConfig(paths.ConfigFile);
            if (args.Contains("--configure", StringComparer.OrdinalIgnoreCase))
            {
                System.Windows.Forms.Application.Run(new ConfigurationWindow(paths, config));
                return 0;
            }

            if (!string.Equals(Environment.UserName, config.WindowsUser, StringComparison.OrdinalIgnoreCase))
            {
                Log.Information("Session user {User} does not match configured user {Configured}; exiting",
                    Environment.UserName, config.WindowsUser);
                return 0;
            }

            using var context = new AgentApplicationContext(config, paths);
            System.Windows.Forms.Application.Run(context);
            return 0;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Gnomon terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    internal static string ExecutablePath =>
        Process.GetCurrentProcess().MainModule?.FileName
        ?? throw new InvalidOperationException("Executable path unavailable");

    private static int RunInteractiveWatchdog()
    {
        var paths = AgentPaths.Create();
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        ConfigureLogging(paths);
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        try
        {
            new WatchdogService().RunAsync(cancellation.Token).GetAwaiter().GetResult();
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int RelaunchConfigurationAsAdministrator()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = ExecutablePath,
                Arguments = "--configure",
                UseShellExecute = true,
                Verb = "runas",
            });
            return 0;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return 1223;
        }
    }

    internal static void ConfigureLogging(AgentPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(Path.Combine(paths.LogDirectory, "gnomon-.log"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14, shared: true)
            .CreateLogger();
    }

    private static AgentConfig LoadConfig(string path)
    {
        if (!File.Exists(path))
        {
            Log.Error("Configuration missing at {Path}", path);
            return AgentConfig.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(path), ProtocolCodec.JsonOptions)
                   ?? AgentConfig.Empty;
        }
        catch (JsonException exception)
        {
            Log.Error(exception, "Configuration at {Path} is invalid", path);
            return AgentConfig.Empty;
        }
    }
}

internal sealed class GnomonWindowsService : ServiceBase
{
    private CancellationTokenSource? _cancellation;
    private Task? _watchdog;

    public GnomonWindowsService()
    {
        ServiceName = "GnomonAgent";
        CanStop = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args)
    {
        var paths = AgentPaths.Create();
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        Program.ConfigureLogging(paths);
        _cancellation = new CancellationTokenSource();
        _watchdog = Task.Run(() => new WatchdogService().RunAsync(_cancellation.Token));
        Log.Information("Gnomon service started");
    }

    protected override void OnStop()
    {
        _cancellation?.Cancel();
        try { _watchdog?.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException exception) when (exception.InnerExceptions.All(x => x is OperationCanceledException)) { }
        _cancellation?.Dispose();
        _cancellation = null;
        _watchdog = null;
        Log.Information("Gnomon service stopped");
        Log.CloseAndFlush();
    }
}
