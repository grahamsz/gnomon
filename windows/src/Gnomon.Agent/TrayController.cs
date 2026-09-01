using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

namespace Gnomon.Agent;

public sealed class TrayController : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon? _appIcon;
    private readonly AgentStatus _status;
    private MainWindow? _window;
    private BrowserSetupWindow? _browserSetup;
    public event EventHandler? ExitRequested;

    public TrayController(AgentStatus status)
    {
        _status = status;
        _appIcon = Icon.ExtractAssociatedIcon(Program.ExecutablePath);
        _icon = new NotifyIcon
        {
            Icon = _appIcon ?? SystemIcons.Information, Visible = true, Text = "Gnomon · starting"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Open());
        menu.Items.Add("Configure…", null, (_, _) => Configure());
        menu.Items.Add("Set up Chrome companion…", null, (_, _) => OpenBrowserSetup());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _icon.ContextMenuStrip = menu; _icon.DoubleClick += (_, _) => Open();
        _status.Changed += Changed; Changed(this, EventArgs.Empty);
    }

    private void Open()
    {
        if (_window is null || _window.IsDisposed) _window = new MainWindow(_status);
        _window.Show(); _window.Activate();
    }

    private static void Configure()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Program.ExecutablePath,
                Arguments = "--configure",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // The user cancelled the elevation prompt.
        }
    }

    private void OpenBrowserSetup()
    {
        if (_browserSetup is null || _browserSetup.IsDisposed) _browserSetup = new BrowserSetupWindow();
        _browserSetup.Show();
        _browserSetup.Activate();
    }

    private void Changed(object? sender, EventArgs e)
    {
        var status = _status.Snapshot;
        var constrained = new[] { _status.ChildOverall, _status.DeviceOverall }
            .Where(value => value.Limit > 0)
            .Select(value => Math.Max(0, value.Limit - value.Used)).ToList();
        var summary = constrained.Count > 0
            ? $"Gnomon · {constrained.Min()} min left"
            : "Gnomon · no overall limit";
        var tooltip = !status.HaConnected
            ? "Gnomon · Home Assistant offline"
            : summary;
        _icon.Text = tooltip.Substring(0, Math.Min(63, tooltip.Length));
    }

    public void Dispose()
    {
        _status.Changed -= Changed;
        _window?.Dispose();
        _browserSetup?.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon?.Dispose();
    }
}
