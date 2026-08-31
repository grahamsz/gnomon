namespace Gnomon.Core;

public sealed class UnknownReportCache
{
    private readonly HashSet<(ClassificationKind Kind, string Id, int Version)> _reported = [];

    public bool ShouldReport(Classification classification) =>
        classification.IsUnknown && _reported.Add((classification.Kind, classification.AppId, classification.RulesVersion));

    public void RetainVersion(int version) => _reported.RemoveWhere(x => x.Version != version);
}
