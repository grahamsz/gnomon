using System.ComponentModel;
using System.Runtime.CompilerServices;
using Gnomon.Core;

namespace Gnomon.Agent;

public sealed class AgentStatus : INotifyPropertyChanged
{
    private StatusSnapshot _snapshot = new("None", "unclassified", false, false, false, 0, []);
    private readonly Dictionary<string, int> _localUsage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Used, int Limit)> _categoryTotals = new(StringComparer.OrdinalIgnoreCase);
    public event PropertyChangedEventHandler? PropertyChanged;

    public StatusSnapshot Snapshot { get { lock (this) return _snapshot; } }
    public IReadOnlyDictionary<string, int> LocalUsage { get { lock (this) return new Dictionary<string, int>(_localUsage); } }
    public IReadOnlyDictionary<string, (int Used, int Limit)> CategoryTotals { get { lock (this) return new Dictionary<string, (int, int)>(_categoryTotals); } }
    public event EventHandler? Changed;

    public void Update(Func<StatusSnapshot, StatusSnapshot> update)
    {
        lock (this) _snapshot = update(_snapshot);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Snapshot)));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void AddUsage(string category, int minutes)
    {
        lock (this)
        {
            _localUsage[category] = _localUsage.GetValueOrDefault(category) + minutes;
            var total = _categoryTotals.GetValueOrDefault(category);
            _categoryTotals[category] = (total.Used + minutes, total.Limit);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCategoryState(string category, int? used = null, int? limit = null)
    {
        lock (this)
        {
            var value = _categoryTotals.GetValueOrDefault(category);
            _categoryTotals[category] = (used ?? value.Used, limit ?? value.Limit);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
