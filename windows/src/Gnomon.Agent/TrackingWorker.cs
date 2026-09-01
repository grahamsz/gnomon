using Gnomon.Core;
using Serilog;

namespace Gnomon.Agent;

internal sealed class TrackingWorker
{
    private readonly AgentConfig _config;
    private readonly ForegroundWatcher _foreground;
    private readonly ActivityProbes _probes;
    private readonly Classifier _classifier;
    private readonly DeltaQuantizer _quantizer;
    private readonly LocalActivityStore _activity;
    private readonly ExtensionServer _extension;
    private readonly HaWebSocketClient _ha;
    private readonly AgentStatus _status;
    private string _lastApp = "";
    private Classification? _lastClassification;
    private string _lastHint = "";
    private DateTimeOffset _lastTick = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastMediaProbe = DateTimeOffset.MinValue;
    private bool _mediaPlaying;

    public TrackingWorker(
        AgentConfig config, ForegroundWatcher foreground, ActivityProbes probes,
        Classifier classifier, DeltaQuantizer quantizer, LocalActivityStore activity,
        ExtensionServer extension, HaWebSocketClient ha, AgentStatus status)
    {
        _config = config; _foreground = foreground; _probes = probes; _classifier = classifier;
        _quantizer = quantizer; _activity = activity; _extension = extension; _ha = ha; _status = status;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        _ = _extension.StartAsync(stoppingToken);
        var haTask = _ha.RunAsync(stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                await TickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            Flush(_lastClassification, _lastHint);
            await _extension.StopAsync();
            try { await haTask; } catch (OperationCanceledException) { }
        }
    }

    private async Task TickAsync(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var foreground = _foreground.Current;
        var classification = _classifier.Classify(
            foreground.ProcessName, _config.Kid, _extension.CurrentDomain, _extension.LastSeen, now, _ha.Rules);

        var changed = _lastApp.Length == 0 ||
                      !string.Equals(_lastApp, classification.AppId, StringComparison.OrdinalIgnoreCase);
        if (_lastApp.Length > 0 && changed)
            Flush(_lastClassification, _lastHint);
        _lastApp = classification.AppId;
        _lastClassification = classification;
        _lastHint = classification.Kind == ClassificationKind.Domain ? classification.AppId : foreground.Hint;
        if (changed) _activity.Observe(classification, _lastHint);

        var rule = _ha.Rules.Categories.FirstOrDefault(x => x.Id.Equals(classification.Category, StringComparison.OrdinalIgnoreCase))
                   ?? new CategoryRule("unclassified", "Unclassified");
        if (now - _lastMediaProbe >= TimeSpan.FromSeconds(5))
        {
            _mediaPlaying = await _probes.IsMediaPlayingAsync(foreground.ProcessId);
            _lastMediaProbe = now;
        }
        var idle = _probes.IsInputIdle(TimeSpan.FromMinutes(rule.IdleTimeoutMinutes));
        var counting = ActivityStateMachine.IsCounting(new(
            true, _probes.SessionActive, idle, _mediaPlaying, rule.MediaCountsAsActive));
        var usageKey = $"{classification.Kind}:{classification.AppId}";
        if (counting) _quantizer.Accumulate(usageKey, now - _lastTick);
        _lastTick = now;

        if (_quantizer.RemainderSeconds(usageKey) >= 60) Flush(classification, _lastHint);
        var extensionFresh = _extension.LastSeen is not null && now - _extension.LastSeen <= TimeSpan.FromSeconds(60);
        _status.Update(x => x with
        {
            ForegroundApp = foreground.ProcessName, Category = classification.Category,
            Counting = counting, ExtensionReachable = extensionFresh, HaConnected = _ha.Connected,
            RulesVersion = _ha.Rules.Version
        });
    }

    private void Flush(Classification? classification, string hint)
    {
        if (classification is null) return;
        var usageKey = $"{classification.Kind}:{classification.AppId}";
        var minutes = _quantizer.FlushWholeMinutes(usageKey);
        if (minutes <= 0) return;
        _activity.Observe(classification, hint, minutes);
        foreach (var chunk in Chunk(minutes, 30))
            _ha.Queue(new(
                _config.Kid, _config.Device, classification.Category, chunk,
                classification.AppId, classification.Kind, hint));
        _status.AddUsage(classification.Category, minutes);
        Log.Information("Usage delta: {Category} {Minutes} min ({App})", classification.Category, minutes, classification.AppId);
    }

    private static IEnumerable<int> Chunk(int total, int maximum)
    {
        while (total > 0) { var value = Math.Min(total, maximum); yield return value; total -= value; }
    }
}
