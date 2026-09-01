using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Gnomon.Core;

public static class ProtocolCodec
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Auth(string token) => JsonSerializer.Serialize(new
    {
        type = "auth", access_token = token
    }, JsonOptions);

    public static string CallService(int id, string service, object data, bool returnResponse = false) =>
        JsonSerializer.Serialize(new
        {
            id, type = "call_service", domain = "gnomon", service,
            service_data = data,
            return_response = returnResponse ? true : (bool?)null
        }, JsonOptions);

    public static string ReportUsage(int id, UsageDelta delta) => CallService(id, "report_usage", new
    {
        kid = delta.Kid, device = delta.Device, category = delta.Category,
        minutes = delta.Minutes
    });

    public static string Heartbeat(int id, string kid, string device, string version) =>
        CallService(id, "heartbeat", new { kid, device, agent_version = version });

    public static string GetRules(int id) => CallService(id, "get_rules", new { }, true);

    public static string GetStatus(int id, string kid, string device) =>
        CallService(id, "get_status", new { kid, device }, true);

    public static string GetClassifications(int id, string kid) =>
        CallService(id, "get_classifications", new { kid }, true);

    public static string SetClassification(
        int id, string kid, string kind, string itemId, string category) =>
        CallService(id, "set_classification", new { kid, kind, id = itemId, category }, true);

    public static string GetStates(int id) => JsonSerializer.Serialize(new { id, type = "get_states" }, JsonOptions);

    public static string SubscribeChanges(int id) => JsonSerializer.Serialize(new
    {
        id, type = "subscribe_events", event_type = "gnomon_changed"
    }, JsonOptions);

    public static bool IsRulesVersionEvent(JsonNode message)
    {
        return message["event"]?["data"]?["kind"]?.GetValue<string>() == "rules";
    }

    public static bool IsStatusEvent(JsonNode message, string kid) =>
        message["event"]?["data"]?["kind"]?.GetValue<string>() == "status" &&
        message["event"]?["data"]?["kid"]?.GetValue<string>() == kid;
}
