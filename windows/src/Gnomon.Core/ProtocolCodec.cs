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
        minutes = delta.Minutes, app_id = delta.AppId
    });

    public static string ReportUnknown(
        int id, string kid, string device, Classification classification, string hint) =>
        CallService(id, "report_unknown", new
        {
            kid, device,
            kind = classification.Kind == ClassificationKind.Process ? "process" : "domain",
            id = classification.AppId, hint
        });

    public static string Heartbeat(int id, string kid, string device, string version) =>
        CallService(id, "heartbeat", new { kid, device, agent_version = version });

    public static string GetRules(int id) => CallService(id, "get_rules", new { }, true);

    public static string GetStates(int id) => JsonSerializer.Serialize(new { id, type = "get_states" }, JsonOptions);

    public static string SubscribeStateChanges(int id) => JsonSerializer.Serialize(new
    {
        id, type = "subscribe_events", event_type = "state_changed"
    }, JsonOptions);

    public static bool IsRulesVersionEvent(JsonNode message)
    {
        var entityId = message["event"]?["data"]?["entity_id"]?.GetValue<string>();
        return entityId == "sensor.gnomon_rules_version";
    }
}
