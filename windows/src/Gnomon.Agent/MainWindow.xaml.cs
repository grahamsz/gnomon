using System.Windows;

namespace Gnomon.Agent;

public partial class MainWindow : Window
{
    private readonly AgentStatus _status;
    public MainWindow(AgentStatus status)
    {
        InitializeComponent(); _status = status;
        _status.Changed += StatusChanged; Closed += (_, _) => Hide(); Refresh();
    }

    private void StatusChanged(object? sender, EventArgs e) => Dispatcher.Invoke(Refresh);

    private void Refresh()
    {
        var value = _status.Snapshot;
        NowText.Text = $"{value.ForegroundApp}\n{value.Category} · {(value.Counting ? "counting" : "not counting")}";
        StatusText.Text = $"Home Assistant: {(value.HaConnected ? "connected" : "offline")}\n" +
                          $"Browser extension: {(value.ExtensionReachable ? "connected" : "stale")}\nRules: v{value.RulesVersion}";
        UsageList.ItemsSource = _status.CategoryTotals.Select(x =>
            $"{x.Key}: {x.Value.Used}/{x.Value.Limit} min · {Math.Max(0, x.Value.Limit - x.Value.Used)} remaining").ToArray();
        UnknownList.ItemsSource = value.UnknownItems.ToArray();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true; Hide();
    }
}
