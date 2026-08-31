namespace Gnomon.Core;

public sealed class Classifier
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedge.exe", "chrome.exe", "firefox.exe", "brave.exe", "vivaldi.exe"
    };

    public Classification Classify(
        string processName,
        string kid,
        string? browserDomain,
        DateTimeOffset? extensionLastSeen,
        DateTimeOffset now,
        RulesMap rules)
    {
        processName = Path.GetFileName(processName).ToLowerInvariant();
        var processOverrides = Override(rules, kid).Processes;
        var domainOverrides = Override(rules, kid).Domains;

        if (Browsers.Contains(processName))
        {
            var extensionFresh = extensionLastSeen is not null && now - extensionLastSeen <= TimeSpan.FromSeconds(60);
            if (!extensionFresh || string.IsNullOrWhiteSpace(browserDomain))
                return Unknown(processName, ClassificationKind.Process, rules.Version);

            var hostname = NormalizeDomain(browserDomain);
            var category = LongestDomainMatch(hostname, domainOverrides)
                           ?? LongestDomainMatch(hostname, rules.Domains);
            return category is null
                ? Unknown(hostname, ClassificationKind.Domain, rules.Version)
                : new(category, hostname, ClassificationKind.Domain, false, rules.Version);
        }

        if (processOverrides.TryGetValue(processName, out var overrideCategory)
            || rules.Processes.TryGetValue(processName, out overrideCategory))
            return new(overrideCategory, processName, ClassificationKind.Process, false, rules.Version);

        return Unknown(processName, ClassificationKind.Process, rules.Version);
    }

    private static RuleOverrides Override(RulesMap rules, string kid) =>
        rules.Overrides.TryGetValue(kid, out var value)
            ? value
            : new RuleOverrides(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static string NormalizeDomain(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            value = uri.Host;
        return value.Trim().TrimEnd('.').ToLowerInvariant();
    }

    private static string? LongestDomainMatch(string hostname, IReadOnlyDictionary<string, string> rules) =>
        rules.Where(x => hostname.Equals(x.Key, StringComparison.OrdinalIgnoreCase)
                         || hostname.EndsWith('.' + x.Key, StringComparison.OrdinalIgnoreCase))
             .OrderByDescending(x => x.Key.Length)
             .Select(x => x.Value)
             .FirstOrDefault();

    private static Classification Unknown(string id, ClassificationKind kind, int version) =>
        new("unclassified", id, kind, true, version);
}
