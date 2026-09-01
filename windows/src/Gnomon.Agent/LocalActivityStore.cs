using System.Text.Json;
using Gnomon.Core;

namespace Gnomon.Agent;

internal sealed record LocalActivityEntry(
    string Kind, string Id, string Label, int Minutes, DateTimeOffset LastSeen);

/// <summary>Per-Windows-user activity index. Raw app/domain names never leave this machine.</summary>
internal sealed class LocalActivityStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public LocalActivityStore(string path) => _path = path;

    public IReadOnlyList<LocalActivityEntry> Read()
    {
        lock (_gate) return Load().Values.OrderByDescending(x => x.Minutes).ThenBy(x => x.Label).ToList();
    }

    public void Observe(Classification classification, string label, int minutes = 0)
    {
        lock (_gate)
        {
            var values = Load();
            var kind = classification.Kind == ClassificationKind.Domain ? "domain" : "process";
            var key = kind + ":" + classification.AppId;
            values.TryGetValue(key, out var existing);
            values[key] = new LocalActivityEntry(
                kind, classification.AppId,
                string.IsNullOrWhiteSpace(label) ? classification.AppId : label.Trim(),
                Math.Max(0, (existing?.Minutes ?? 0) + minutes), DateTimeOffset.UtcNow);
            Save(values);
        }
    }

    private Dictionary<string, LocalActivityEntry> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new(StringComparer.OrdinalIgnoreCase);
            var items = JsonSerializer.Deserialize<List<LocalActivityEntry>>(
                File.ReadAllText(_path), ProtocolCodec.JsonOptions) ?? [];
            return items.ToDictionary(x => x.Kind + ":" + x.Id, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save(Dictionary<string, LocalActivityEntry> values)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(values.Values, ProtocolCodec.JsonOptions));
        File.Copy(temporary, _path, true);
        File.Delete(temporary);
    }
}
