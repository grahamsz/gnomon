namespace Gnomon.Core;

public static class AgentConfiguration
{
    public const string DefaultHomeAssistantAddress = "homeassistant.local";

    public static AgentConfig CreateDefault(string windowsUser, string device) => new(
        "ws://homeassistant.local:8123/api/websocket",
        "",
        "",
        device,
        windowsUser,
        45981);

    public static bool IsPlaceholder(AgentConfig config) =>
        string.IsNullOrWhiteSpace(config.HaToken) ||
        config.HaToken.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase);

    public static bool TryNormalizeHomeAssistantUrl(string value, out string websocketUrl)
    {
        websocketUrl = "";
        var input = value.Trim();
        if (input.Length == 0) return false;

        var suppliedScheme = input.Contains("://", StringComparison.Ordinal);
        var candidate = suppliedScheme ? input : $"http://{input}";
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            return false;

        var scheme = uri.Scheme.ToLowerInvariant() switch
        {
            "http" => "ws",
            "https" => "wss",
            "ws" => "ws",
            "wss" => "wss",
            _ => "",
        };
        if (scheme.Length == 0) return false;

        var builder = new UriBuilder(uri)
        {
            Scheme = scheme,
            Path = "/api/websocket",
            Query = "",
            Fragment = "",
        };
        if (!suppliedScheme && uri.IsDefaultPort) builder.Port = 8123;
        else if (suppliedScheme && uri.IsDefaultPort) builder.Port = -1;

        websocketUrl = builder.Uri.AbsoluteUri.TrimEnd('/');
        return true;
    }
}
