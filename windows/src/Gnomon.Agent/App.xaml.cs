using System.IO;
using System.Text.Json;
using System.Windows;
using Gnomon.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Gnomon.Agent;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private TrayController? _tray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var paths = AgentPaths.Create();
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        Program.ConfigureLogging(paths);

        var config = await LoadConfigAsync(paths.ConfigFile);
        if (e.Args.Contains("--configure", StringComparer.OrdinalIgnoreCase))
        {
            var setup = new ConfigurationWindow(paths, config);
            MainWindow = setup;
            setup.Closed += (_, _) => Shutdown();
            setup.Show();
            return;
        }
        if (!string.Equals(Environment.UserName, config.WindowsUser, StringComparison.OrdinalIgnoreCase))
        {
            Log.Information("Session user {User} does not match configured user {Configured}; exiting", Environment.UserName, config.WindowsUser);
            Shutdown();
            return;
        }

        _host = Host.CreateDefaultBuilder(e.Args)
            .UseSerilog()
            .ConfigureServices(services =>
            {
                services.AddSingleton(paths); services.AddSingleton(config);
                services.AddSingleton<AgentStatus>(); services.AddSingleton<Classifier>();
                services.AddSingleton<UnknownReportCache>(); services.AddSingleton<DeltaQuantizer>();
                services.AddSingleton<ForegroundWatcher>(); services.AddSingleton<ActivityProbes>();
                services.AddSingleton<HaWebSocketClient>(); services.AddSingleton<ExtensionServer>();
                services.AddHostedService<TrackingWorker>();
            }).Build();
        await _host.StartAsync();
        _tray = new TrayController(_host.Services.GetRequiredService<AgentStatus>());
        _tray.ExitRequested += async (_, _) =>
        {
            if (_host is not null) await _host.StopAsync(TimeSpan.FromSeconds(5));
            Shutdown();
        };
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_host is not null) { await _host.StopAsync(TimeSpan.FromSeconds(5)); _host.Dispose(); }
        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    protected override async void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (_host is not null) await _host.StopAsync(TimeSpan.FromSeconds(5));
        base.OnSessionEnding(e);
    }

    private static async Task<AgentConfig> LoadConfigAsync(string path)
    {
        if (!File.Exists(path))
        {
            Log.Error("Configuration missing at {Path}", path);
            return AgentConfig.Empty;
        }
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<AgentConfig>(stream, ProtocolCodec.JsonOptions) ?? AgentConfig.Empty;
        }
        catch (JsonException exception)
        {
            Log.Error(exception, "Configuration at {Path} is invalid", path);
            return AgentConfig.Empty;
        }
    }
}

public sealed record AgentPaths(string DataDirectory, string LogDirectory, string ConfigFile, string RulesCacheFile)
{
    public static AgentPaths Create()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Gnomon");
        return new(root, Path.Combine(root, "logs"), Path.Combine(root, "config.json"), Path.Combine(root, "rules-cache.json"));
    }
}
