using System.ComponentModel;
using System.Runtime.CompilerServices;
using Gnomon.Core;

namespace Gnomon.Agent;

public sealed class AgentStatus : INotifyPropertyChanged
{
    private StatusSnapshot _snapshot = new("None", "unclassified", false, false, false, 0);
    private readonly Dictionary<string, int> _localUsage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Name, int Used, int Limit)> _categoryTotals = new(StringComparer.OrdinalIgnoreCase);
    private (int Used, int Limit) _childOverall;
    private (int Used, int Limit) _deviceOverall;
    public event PropertyChangedEventHandler? PropertyChanged;

    public StatusSnapshot Snapshot { get { lock (this) return _snapshot; } }
    public IReadOnlyDictionary<string, int> LocalUsage { get { lock (this) return new Dictionary<string, int>(_localUsage); } }
    public IReadOnlyDictionary<string, (string Name, int Used, int Limit)> CategoryTotals
    {
        get { lock (this) return new Dictionary<string, (string, int, int)>(_categoryTotals); }
    }
    public (int Used, int Limit) ChildOverall { get { lock (this) return _childOverall; } }
    public (int Used, int Limit) DeviceOverall { get { lock (this) return _deviceOverall; } }
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
            _localUsage.TryGetValue(category, out var local);
            _localUsage[category] = local + minutes;
            _categoryTotals.TryGetValue(category, out var total);
            _categoryTotals[category] = (
                string.IsNullOrWhiteSpace(total.Name) ? category : total.Name,
                total.Used + minutes, total.Limit);
            _childOverall = (_childOverall.Used + minutes, _childOverall.Limit);
            _deviceOverall = (_deviceOverall.Used + minutes, _deviceOverall.Limit);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetCategoryState(string category, int? used = null, int? limit = null)
    {
        lock (this)
        {
            _categoryTotals.TryGetValue(category, out var value);
            _categoryTotals[category] = (
                string.IsNullOrWhiteSpace(value.Name) ? category : value.Name,
                used ?? value.Used, limit ?? value.Limit);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetOverallState(bool device, int? used = null, int? limit = null)
    {
        lock (this)
        {
            var value = device ? _deviceOverall : _childOverall;
            value = (used ?? value.Used, limit ?? value.Limit);
            if (device) _deviceOverall = value; else _childOverall = value;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Apply(AggregateStatus status)
    {
        lock (this)
        {
            _categoryTotals.Clear();
            foreach (var category in status.Categories)
                _categoryTotals[category.Id] = (category.Name, category.Used, category.Limit);
            _childOverall = (status.Child.Used, status.Child.Limit);
            _deviceOverall = (status.Device.Used, status.Device.Limit);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
