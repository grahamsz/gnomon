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
    private static readonly Random Jitter = new();
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
        LoadCache();
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
                double jitter;
                lock (Jitter) jitter = Jitter.NextDouble() * 0.3 + 0.85;
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
        Log.Information("Connected to Home Assistant for kid {Kid} on device {Device}", _config.Kid, _config.Device);

        await RefreshRulesAsync(token);
        await RefreshAggregateStatusAsync(token);
        await SendAsync(ProtocolCodec.SubscribeChanges(NextId()), token);
        await SendAsync(ProtocolCodec.Heartbeat(NextId(), _config.Kid, _config.Device, ThisAssembly.Version), token);
        await FlushAsync(token);
        using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var heartbeatTask = Task.Run(async () =>
        {
            while (!heartbeatCancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(5), heartbeatCancellation.Token);
                await SendAsync(ProtocolCodec.Heartbeat(NextId(), _config.Kid, _config.Device, ThisAssembly.Version),
                    heartbeatCancellation.Token);
            }
        }, heartbeatCancellation.Token);

        try
        {
            while (_socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var message = await ReceiveAsync(token) ?? throw new WebSocketException("Socket closed");
                var usageAccepted = HandleUsageResult(message);
                var statusChanged = ProtocolCodec.IsStatusEvent(message, _config.Kid);
                if (ProtocolCodec.IsRulesVersionEvent(message)) await RefreshRulesAsync(token);
                if (usageAccepted || statusChanged) await RefreshAggregateStatusAsync(token);
                await FlushAsync(token);
            }
        }
        finally
        {
            heartbeatCancellation.Cancel();
            try { await heartbeatTask; }
            catch (OperationCanceledException) { }
        }
    }

    private async Task RefreshRulesAsync(CancellationToken token)
    {
        var commandId = NextId();
        await SendAsync(ProtocolCodec.GetRules(commandId), token);
        var message = await AwaitResultAsync(commandId, token);
        var response = message["result"]?["response"] ?? message["result"];
        if (response is null) return;
        Rules = response.Deserialize<RulesMap>(ProtocolCodec.JsonOptions) ?? Rules;
        File.WriteAllText(_paths.RulesCacheFile, JsonSerializer.Serialize(Rules, ProtocolCodec.JsonOptions));
        _status.Update(x => x with { RulesVersion = Rules.Version });
        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task<JsonNode> AwaitResultAsync(int commandId, CancellationToken token)
    {
        while (true)
        {
            var message = await ReceiveAsync(token) ?? throw new WebSocketException();
            HandleUsageResult(message);
            if (message["id"]?.GetValue<int>() == commandId) return message;
        }
    }

    private async Task RefreshAggregateStatusAsync(CancellationToken token)
    {
        var commandId = NextId();
        await SendAsync(ProtocolCodec.GetStatus(commandId, _config.Kid, _config.Device), token);
        var message = await AwaitResultAsync(commandId, token);
        var response = message["result"]?["response"] ?? message["result"];
        var status = response?.Deserialize<AggregateStatus>(ProtocolCodec.JsonOptions);
        if (status is not null) _status.Apply(status);
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

    private bool HandleUsageResult(JsonNode message)
    {
        if (_usageInFlight is not { } pending || message["id"]?.GetValue<int>() != pending.Id) return false;
        var accepted = message["success"]?.GetValue<bool>() == true;
        if (accepted)
        {
            lock (_pending)
            {
                if (_pending.Count > 0 && _pending.Peek() == pending.Delta)
                { _pending.Dequeue(); _pendingMinutes -= pending.Delta.Minutes; }
            }
        }
        else Log.Warning("Home Assistant rejected usage delta {Id}; it remains queued", pending.Id);
        _usageInFlight = null;
        return accepted;
    }

    private void LoadCache()
    {
        try
        {
            if (File.Exists(_paths.RulesCacheFile))
                Rules = JsonSerializer.Deserialize<RulesMap>(File.ReadAllText(_paths.RulesCacheFile), ProtocolCodec.JsonOptions) ?? Rules;
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
            await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }
        finally { _sendLock.Release(); }
    }

    private async Task<JsonNode?> ReceiveAsync(CancellationToken token)
    {
        if (_socket is null) return null;
        using var stream = new MemoryStream();
        var buffer = new byte[16 * 1024];
        WebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonNode.Parse(stream.ToArray());
    }
}

internal static class ThisAssembly
{
    public static string Version => typeof(ThisAssembly).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
}
