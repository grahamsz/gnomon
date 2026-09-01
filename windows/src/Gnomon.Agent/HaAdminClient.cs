using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gnomon.Core;

namespace Gnomon.Agent;

internal sealed class HaAdminClient
{
    public Task<RulesMap> GetRulesAsync(
        AgentConfig config, CancellationToken token) =>
        CallAsync(config, ProtocolCodec.GetRules, token);

    public Task<RulesMap> SetClassificationAsync(
        AgentConfig config, ClassificationItem item, string category, CancellationToken token) =>
        CallAsync(
            config,
            id => ProtocolCodec.SetClassification(id, config.Kid, item.Kind, item.Id, category),
            token);

    private static async Task<RulesMap> CallAsync(
        AgentConfig config, Func<int, string> request, CancellationToken token)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(config.HaUrl), token);
        var required = await ReceiveAsync(socket, token);
        if (required?["type"]?.GetValue<string>() != "auth_required")
            throw new InvalidDataException("Home Assistant did not request authentication.");
        await SendAsync(socket, ProtocolCodec.Auth(config.HaToken), token);
        var auth = await ReceiveAsync(socket, token);
        if (auth?["type"]?.GetValue<string>() == "auth_invalid")
            throw new AuthenticationException("Home Assistant rejected the token.");
        if (auth?["type"]?.GetValue<string>() != "auth_ok")
            throw new InvalidDataException("Home Assistant authentication did not complete.");

        const int id = 1;
        await SendAsync(socket, request(id), token);
        while (true)
        {
            var message = await ReceiveAsync(socket, token)
                          ?? throw new WebSocketException("Home Assistant closed the connection.");
            if (message["id"]?.GetValue<int>() != id) continue;
            if (message["success"]?.GetValue<bool>() != true)
            {
                var error = message["error"]?["message"]?.GetValue<string>()
                            ?? "Home Assistant rejected the classification request.";
                throw new InvalidOperationException(error);
            }
            var payload = message["result"]?["response"] ?? message["result"];
            return payload?.Deserialize<RulesMap>(ProtocolCodec.JsonOptions)
                   ?? throw new InvalidDataException("Home Assistant returned an empty rule document.");
        }
    }

    private static async Task SendAsync(
        ClientWebSocket socket, string message, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    private static async Task<JsonNode?> ReceiveAsync(
        ClientWebSocket socket, CancellationToken token)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[16 * 1024];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);
        return JsonNode.Parse(stream.ToArray());
    }
}
