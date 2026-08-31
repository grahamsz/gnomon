using Gnomon.Core;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Gnomon.Agent;

public sealed class TrackingWorker : BackgroundService
{
    private readonly AgentConfig _config;
    private readonly ForegroundWatcher _foreground;
    private readonly ActivityProbes _probes;
    private readonly Classifier _classifier;
    private readonly DeltaQuantizer _quantizer;
    private readonly UnknownReportCache _unknownCache;
    private readonly ExtensionServer _extension;
    private readonly HaWebSocketClient _ha;
    private readonly AgentStatus _status;
    private readonly HashSet<string> _localUnknowns = new(StringComparer.OrdinalIgnoreCase);
    private string _lastApp = "";
    private Classification? _lastClassification;
    private DateTimeOffset _lastTick = DateTimeOffset.UtcNow;
    private DateTimeOffset _lastMediaProbe = DateTimeOffset.MinValue;
    private bool _mediaPlaying;

    public TrackingWorker(
        AgentConfig config, ForegroundWatcher foreground, ActivityProbes probes,
        Classifier classifier, DeltaQuantizer quantizer, UnknownReportCache unknownCache,
        ExtensionServer extension, HaWebSocketClient ha, AgentStatus status)
    {
        _config = config; _foreground = foreground; _probes = probes; _classifier = classifier;
        _quantizer = quantizer; _unknownCache = unknownCache; _extension = extension; _ha = ha; _status = status;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = _extension.StartAsync(stoppingToken);
        var haTask = _ha.RunAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken)) await TickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            Flush(_lastClassification);
            await _extension.DisposeAsync();
            try { await haTask; } catch (OperationCanceledException) { }
        }
    }

    private async Task TickAsync(CancellationToken token)
    {
        var now = DateTimeOffset.UtcNow;
        var foreground = _foreground.Current;
        var classification = _classifier.Classify(
            foreground.ProcessName, _config.Kid, _extension.CurrentDomain, _extension.LastSeen, now, _ha.Rules);

        if (_lastApp.Length > 0 && !string.Equals(_lastApp, classification.AppId, StringComparison.OrdinalIgnoreCase))
            Flush(_lastClassification);
        _lastApp = classification.AppId;
        _lastClassification = classification;

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
        if (counting) _quantizer.Accumulate(classification.Category, now - _lastTick);
        _lastTick = now;

        if (_quantizer.RemainderSeconds(classification.Category) >= 60) Flush(classification);
        if (classification.IsUnknown)
        {
            _localUnknowns.Add(classification.AppId);
            if (_ha.Connected && _unknownCache.ShouldReport(classification))
                await _ha.ReportUnknownAsync(classification, foreground.Hint, token);
        }
        _unknownCache.RetainVersion(_ha.Rules.Version);

        var extensionFresh = _extension.LastSeen is not null && now - _extension.LastSeen <= TimeSpan.FromSeconds(60);
        _status.Update(x => x with
        {
            ForegroundApp = foreground.ProcessName, Category = classification.Category,
            Counting = counting, ExtensionReachable = extensionFresh, HaConnected = _ha.Connected,
            RulesVersion = _ha.Rules.Version, UnknownItems = _localUnknowns.ToArray()
        });
    }

    private void Flush(Classification? classification)
    {
        if (classification is null) return;
        var minutes = _quantizer.FlushWholeMinutes(classification.Category);
        if (minutes <= 0) return;
        foreach (var chunk in Chunk(minutes, 30))
            _ha.Queue(new(_config.Kid, _config.Device, classification.Category, chunk, classification.AppId));
        _status.AddUsage(classification.Category, minutes);
        Log.Information("Usage delta: {Category} {Minutes} min ({App})", classification.Category, minutes, classification.AppId);
    }

    private static IEnumerable<int> Chunk(int total, int maximum)
    {
        while (total > 0) { var value = Math.Min(total, maximum); yield return value; total -= value; }
    }
}
