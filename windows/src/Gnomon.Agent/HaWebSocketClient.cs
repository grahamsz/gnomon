using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gnomon.Core;
using Serilog;

namespace Gnomon.Agent;

public sealed class HaWebSocketClient
{
    private readonly AgentConfig _config;
    private readonly AgentPaths _paths;
    private readonly AgentStatus _status;
    private readonly Queue<UsageDelta> _pending = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private int _id;
    private int _pendingMinutes;
    private (int Id, UsageDelta Delta)? _usageInFlight;
    public RulesMap Rules { get; private set; } = RulesMap.Empty;
    public bool Connected => _socket?.State == WebSocketState.Open;
    public event EventHandler? RulesChanged;

    public HaWebSocketClient(AgentConfig config, AgentPaths paths, AgentStatus status)
    { _config = config; _paths = paths; _status = status; }

    public async Task RunAsync(CancellationToken token)
    {
        await LoadCacheAsync(token);
        var delay = TimeSpan.FromSeconds(5);
        while (!token.IsCancellationRequested)
        {
            try
            {
                await ConnectAndReceiveAsync(token);
                delay = TimeSpan.FromSeconds(5);
            }
            catch (AuthenticationException ex)
            {
                Log.Error(ex, "Home Assistant rejected the token");
                _status.Update(x => x with { HaConnected = false });
                await Task.Delay(TimeSpan.FromHours(1), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                Log.Warning(ex, "Home Assistant connection lost; retrying");
                _status.Update(x => x with { HaConnected = false });
                var jitter = Random.Shared.NextDouble() * 0.3 + 0.85;
                await Task.Delay(TimeSpan.FromMilliseconds(delay.TotalMilliseconds * jitter), token);
                delay = TimeSpan.FromSeconds(Math.Min(300, delay.TotalSeconds * 2));
            }
        }
    }

    public void Queue(UsageDelta delta)
    {
        lock (_pending)
        {
            while (_pendingMinutes + delta.Minutes > 720 && _pending.Count > 0)
                _pendingMinutes -= _pending.Dequeue().Minutes;
            if (delta.Minutes <= 720)
            {
                _pending.Enqueue(delta); _pendingMinutes += delta.Minutes;
            }
        }
    }

    public async Task ReportUnknownAsync(Classification item, string hint, CancellationToken token)
    {
        if (!Connected) return;
        await SendAsync(ProtocolCodec.ReportUnknown(NextId(), _config.Kid, _config.Device, item, hint), token);
    }

    private async Task ConnectAndReceiveAsync(CancellationToken token)
    {
        _usageInFlight = null;
        _socket?.Dispose(); _socket = new ClientWebSocket();
        await _socket.ConnectAsync(new Uri(_config.HaUrl), token);
        var required = await ReceiveAsync(token);
        if (required?["type"]?.GetValue<string>() != "auth_required") throw new InvalidDataException("Expected auth_required");
        await SendAsync(ProtocolCodec.Auth(_config.HaToken), token);
        var auth = await ReceiveAsync(token);
        if (auth?["type"]?.GetValue<string>() == "auth_invalid") throw new AuthenticationException("auth_invalid");
        if (auth?["type"]?.GetValue<string>() != "auth_ok") throw new InvalidDataException("Expected auth_ok");
        _status.Update(x => x with { HaConnected = true });

        await RefreshRulesIfStaleAsync(token);
        await SendAsync(ProtocolCodec.SubscribeStateChanges(NextId()), token);
        await SendAsync(ProtocolCodec.Heartbeat(NextId(), _config.Kid, _config.Device, ThisAssembly.Version), token);
        await FlushAsync(token);
        using var heartbeat = new PeriodicTimer(TimeSpan.FromMinutes(5));
        var heartbeatTask = Task.Run(async () =>
        {
            while (await heartbeat.WaitForNextTickAsync(token))
                await SendAsync(ProtocolCodec.Heartbeat(NextId(), _config.Kid, _config.Device, ThisAssembly.Version), token);
        }, token);

        while (_socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var message = await ReceiveAsync(token) ?? throw new WebSocketException("Socket closed");
            HandleUsageResult(message);
            HandleStateEvent(message);
            if (ProtocolCodec.IsRulesVersionEvent(message)) await RefreshRulesAsync(token);
            await FlushAsync(token);
        }
        await heartbeatTask;
    }

    private async Task RefreshRulesIfStaleAsync(CancellationToken token)
    {
        var commandId = NextId();
        await SendAsync(ProtocolCodec.GetStates(commandId), token);
        var message = await AwaitResultAsync(commandId, token);
        var states = message["result"]?.AsArray();
        var versionState = states?
            .FirstOrDefault(x => x?["entity_id"]?.GetValue<string>() == "sensor.gnomon_rules_version");
        if (states is not null)
            foreach (var state in states) ApplyCategoryState(state?["entity_id"]?.GetValue<string>(), state?["state"]?.GetValue<string>());
        var remoteVersion = versionState?["state"]?.GetValue<string>();
        if (!int.TryParse(remoteVersion, out var version) || version != Rules.Version)
            await RefreshRulesAsync(token);
    }

    private async Task RefreshRulesAsync(CancellationToken token)
    {
        var commandId = NextId();
        await SendAsync(ProtocolCodec.GetRules(commandId), token);
        var message = await AwaitResultAsync(commandId, token);
        var response = message["result"]?["response"] ?? message["result"];
        if (response is null) return;
        Rules = response.Deserialize<RulesMap>(ProtocolCodec.JsonOptions) ?? Rules;
        await File.WriteAllTextAsync(_paths.RulesCacheFile, JsonSerializer.Serialize(Rules, ProtocolCodec.JsonOptions), token);
        _status.Update(x => x with { RulesVersion = Rules.Version });
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<JsonNode> AwaitResultAsync(int commandId, CancellationToken token)
    {
        while (true)
        {
            var message = await ReceiveAsync(token) ?? throw new WebSocketException();
            HandleUsageResult(message);
            HandleStateEvent(message);
            if (message["id"]?.GetValue<int>() == commandId) return message;
        }
    }

    private void HandleStateEvent(JsonNode message)
    {
        var data = message["event"]?["data"];
        ApplyCategoryState(data?["entity_id"]?.GetValue<string>(), data?["new_state"]?["state"]?.GetValue<string>());
    }

    private void ApplyCategoryState(string? entityId, string? state)
    {
        if (!int.TryParse(state, out var value) || entityId is null) return;
        var usedPrefix = $"sensor.gnomon_used_{_config.Kid}_";
        var limitPrefix = $"number.gnomon_limit_{_config.Kid}_";
        if (entityId.StartsWith(usedPrefix, StringComparison.Ordinal))
        {
            var category = entityId[usedPrefix.Length..];
            if (!category.Contains('_') || Rules.Categories.Any(x => x.Id == category))
                _status.SetCategoryState(category, used: value);
        }
        else if (entityId.StartsWith(limitPrefix, StringComparison.Ordinal))
            _status.SetCategoryState(entityId[limitPrefix.Length..], limit: value);
    }

    private async Task FlushAsync(CancellationToken token)
    {
        if (!Connected || _usageInFlight is not null) return;
        UsageDelta? delta;
        lock (_pending) delta = _pending.Count > 0 ? _pending.Peek() : null;
        if (delta is null) return;
        var id = NextId();
        _usageInFlight = (id, delta);
        try { await SendAsync(ProtocolCodec.ReportUsage(id, delta), token); }
        catch { _usageInFlight = null; throw; }
    }

    private void HandleUsageResult(JsonNode message)
    {
        if (_usageInFlight is not { } pending || message["id"]?.GetValue<int>() != pending.Id) return;
        if (message["success"]?.GetValue<bool>() == true)
        {
            lock (_pending)
            {
                if (_pending.Count > 0 && _pending.Peek() == pending.Delta)
                { _pending.Dequeue(); _pendingMinutes -= pending.Delta.Minutes; }
            }
        }
        else Log.Warning("Home Assistant rejected usage delta {Id}; it remains queued", pending.Id);
        _usageInFlight = null;
    }

    private async Task LoadCacheAsync(CancellationToken token)
    {
        try
        {
            if (File.Exists(_paths.RulesCacheFile))
                Rules = JsonSerializer.Deserialize<RulesMap>(await File.ReadAllTextAsync(_paths.RulesCacheFile, token), ProtocolCodec.JsonOptions) ?? Rules;
            _status.Update(x => x with { RulesVersion = Rules.Version });
        }
        catch (Exception ex) { Log.Warning(ex, "Rules cache could not be loaded"); }
    }

    private int NextId() => Interlocked.Increment(ref _id);

    private async Task SendAsync(string message, CancellationToken token)
    {
        if (_socket is null) throw new WebSocketException();
        await _sendLock.WaitAsync(token);
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
        }
        finally { _sendLock.Release(); }
    }

    private async Task<JsonNode?> ReceiveAsync(CancellationToken token)
    {
        if (_socket is null) return null;
        using var stream = new MemoryStream();
        var buffer = new byte[16 * 1024];
        ValueWebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(buffer.AsMemory(), token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonNode.Parse(stream.ToArray());
    }
}

internal static class ThisAssembly
{
    public static string Version => typeof(ThisAssembly).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
