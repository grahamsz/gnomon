using Gnomon.Core;

namespace Gnomon.Agent;

internal sealed class AgentApplicationContext : System.Windows.Forms.ApplicationContext
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ForegroundWatcher _foreground;
    private readonly ActivityProbes _probes;
    private readonly Task _workerTask;
    private readonly TrayController _tray;

    public AgentApplicationContext(AgentConfig config, AgentPaths paths)
    {
        var status = new AgentStatus();
        _foreground = new ForegroundWatcher();
        _probes = new ActivityProbes();
        var extension = new ExtensionServer(config, status);
        var homeAssistant = new HaWebSocketClient(config, paths, status);
        var worker = new TrackingWorker(
            config, _foreground, _probes, new Classifier(), new DeltaQuantizer(),
            new UnknownReportCache(), extension, homeAssistant, status);
        _workerTask = Task.Run(() => worker.RunAsync(_cancellation.Token));

        _tray = new TrayController(status);
        _tray.ExitRequested += (_, _) => ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _cancellation.Cancel();
        try { _workerTask.Wait(TimeSpan.FromSeconds(10)); }
        catch (AggregateException exception) when (exception.InnerExceptions.All(x => x is OperationCanceledException)) { }
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _foreground.Dispose();
            _probes.Dispose();
            _cancellation.Dispose();
        }
        base.Dispose(disposing);
    }
}
