using System.Drawing;
using System.Windows.Forms;

namespace Gnomon.Agent;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon? _appIcon;
    private readonly AgentStatus _status;
    private MainWindow? _window;
    public event EventHandler? ExitRequested;

    public TrayController(AgentStatus status)
    {
        _status = status;
        _appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath);
        _icon = new NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Information, Visible = true, Text = "Gnomon · starting"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Open());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _icon.ContextMenuStrip = menu; _icon.DoubleClick += (_, _) => Open();
        _status.Changed += Changed; Changed(this, EventArgs.Empty);
    }

    private void Open()
    {
        _window ??= new MainWindow(_status);
        _window.Show(); _window.Activate();
    }

    private void Changed(object? sender, EventArgs e)
    {
        var summary = string.Join(" · ", _status.CategoryTotals.Take(3).Select(x => $"{x.Key} {x.Value.Used}/{x.Value.Limit} min"));
        var tooltip = string.IsNullOrEmpty(summary) ? "Gnomon · no usage yet" : summary;
        _icon.Text = tooltip[..Math.Min(63, tooltip.Length)];
    }

    public void Dispose() { _status.Changed -= Changed; _icon.Visible = false; _icon.Dispose(); _appIcon?.Dispose(); }
}
