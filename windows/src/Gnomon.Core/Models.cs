using System.Text.Json.Serialization;

namespace Gnomon.Core;

public sealed record AgentConfig(
    string HaUrl,
    string HaToken,
    string Kid,
    string Device,
    string WindowsUser,
    int ExtensionPort = 45981)
{
    public static AgentConfig Empty { get; } = new("", "", "", "", "", 45981);
}

public sealed record CategoryRule(
    string Id,
    string Name,
    [property: JsonPropertyName("idle_timeout_min")] int IdleTimeoutMinutes = 3,
    [property: JsonPropertyName("media_counts_as_active")] bool MediaCountsAsActive = false);

public sealed record RuleOverrides(
    IReadOnlyDictionary<string, string> Processes,
    IReadOnlyDictionary<string, string> Domains);

public sealed record RulesMap(
    int Version,
    IReadOnlyList<CategoryRule> Categories,
    IReadOnlyDictionary<string, string> Processes,
    IReadOnlyDictionary<string, string> Domains,
    IReadOnlyDictionary<string, RuleOverrides> Overrides)
{
    public static RulesMap Empty { get; } = new(
        0,
        [new("unclassified", "Unclassified")],
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, RuleOverrides>(StringComparer.OrdinalIgnoreCase));
}

public enum ClassificationKind { Process, Domain }

public sealed record Classification(
    string Category,
    string AppId,
    ClassificationKind Kind,
    bool IsUnknown,
    int RulesVersion);

public sealed record UsageDelta(
    string Kid,
    string Device,
    string Category,
    int Minutes,
    string AppId,
    ClassificationKind Kind = ClassificationKind.Process,
    string AppLabel = "");

public sealed record ClassificationCategory(string Id, string Name);

public sealed record ClassificationItem(
    string Kind,
    string Id,
    string Label,
    string Category,
    int Minutes,
    IReadOnlyList<string> Devices,
    [property: JsonPropertyName("last_seen")] string LastSeen,
    bool Unclassified);

public sealed record ClassificationCatalog(
    int Version,
    IReadOnlyList<ClassificationCategory> Categories,
    IReadOnlyList<ClassificationItem> Items);

public sealed record AggregateAllowance(int Used, int Limit);
public sealed record DeviceAllowance(string Id, int Used, int Limit);
public sealed record CategoryAllowance(string Id, string Name, int Used, int Limit);
public sealed record AggregateStatus(
    IReadOnlyList<CategoryAllowance> Categories,
    AggregateAllowance Child,
    DeviceAllowance Device);

public sealed record ActivitySnapshot(
    bool ForegroundMapped,
    bool SessionActive,
    bool InputIdle,
    bool MediaPlaying,
    bool MediaCountsAsActive);

public sealed record StatusSnapshot(
    string ForegroundApp,
    string Category,
    bool Counting,
    bool ExtensionReachable,
    bool HaConnected,
    int RulesVersion);
