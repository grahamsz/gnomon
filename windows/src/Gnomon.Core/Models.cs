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

public sealed record UsageDelta(string Kid, string Device, string Category, int Minutes, string AppId);

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
    int RulesVersion,
    IReadOnlyCollection<string> UnknownItems);
