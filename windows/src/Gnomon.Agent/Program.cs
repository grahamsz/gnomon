using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Gnomon.Agent;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--service", StringComparer.OrdinalIgnoreCase))
        {
            return RunServiceAsync(args).GetAwaiter().GetResult();
        }

        if (args.Contains("--configure", StringComparer.OrdinalIgnoreCase) && !IsAdministrator())
        {
            return RelaunchConfigurationAsAdministrator();
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
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
                FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable"),
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
            .WriteTo.File(Path.Combine(paths.LogDirectory, "gnomon-.log"), rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14, shared: true)
            .CreateLogger();
    }

    private static async Task<int> RunServiceAsync(string[] args)
    {
        var paths = AgentPaths.Create();
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        ConfigureLogging(paths);

        try
        {
            // The marker is for our entry-point dispatch, not host configuration.
            var hostArgs = args.Where(arg => !string.Equals(arg, "--service", StringComparison.OrdinalIgnoreCase)).ToArray();
            using var host = Host.CreateDefaultBuilder(hostArgs)
                .UseWindowsService(options => options.ServiceName = "GnomonAgent")
                .UseSerilog()
                .ConfigureServices(services => services.AddHostedService<WatchdogService>())
                .Build();
            await host.RunAsync();
            return 0;
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Gnomon service terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}
