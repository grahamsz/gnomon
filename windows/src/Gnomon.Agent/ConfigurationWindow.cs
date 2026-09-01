using System.Drawing;
using System.ServiceProcess;
using System.Text.Json;
using Gnomon.Core;

namespace Gnomon.Agent;

public sealed class ConfigurationWindow : System.Windows.Forms.Form
{
    private readonly AgentPaths _paths;
    private readonly System.Windows.Forms.TextBox _haAddress = new();
    private readonly System.Windows.Forms.TextBox _token = new();
    private readonly System.Windows.Forms.TextBox _kid = new();
    private readonly System.Windows.Forms.TextBox _device = new();
    private readonly System.Windows.Forms.TextBox _windowsUser = new();
    private readonly System.Windows.Forms.Label _status = new();
    private readonly System.Windows.Forms.Button _save = new();
    private readonly System.Windows.Forms.Button _cancel = new();
    private readonly System.Windows.Forms.Button _browserSetup = new();
    private readonly System.Windows.Forms.Button _classifications = new();

    public ConfigurationWindow(AgentPaths paths, AgentConfig config)
    {
        _paths = paths;
        Text = "Set up Gnomon";
        ClientSize = new Size(560, 650);
        MinimumSize = SizeFromClientSize(new Size(520, 650));
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Program.ExecutablePath);

        var placeholder = AgentConfiguration.IsPlaceholder(config);
        _haAddress.Text = placeholder ? AgentConfiguration.DefaultHomeAssistantAddress : config.HaUrl;
        _token.Text = placeholder ? "" : config.HaToken;
        _token.UseSystemPasswordChar = true;
        _kid.Text = placeholder && config.Kid.Equals("alex", StringComparison.OrdinalIgnoreCase) ? "" : config.Kid;
        _device.Text = string.IsNullOrWhiteSpace(config.Device) || placeholder
            ? Environment.MachineName.ToLowerInvariant()
            : config.Device;
        _windowsUser.Text = string.IsNullOrWhiteSpace(config.WindowsUser) ||
                            (placeholder && config.WindowsUser.Equals("Alex", StringComparison.OrdinalIgnoreCase))
            ? Environment.UserName
            : config.WindowsUser;

        var root = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            Padding = new System.Windows.Forms.Padding(28, 22, 28, 18),
            ColumnCount = 1,
            RowCount = 9,
        };
        root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));
        for (var row = 0; row < 7; row++)
        {
            root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        }
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));

        var title = new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = "Connect Gnomon",
            Font = new Font("Segoe UI Semibold", 22F),
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 4),
        };
        var subtitle = Help("Tell this Windows agent where to report screen time.");
        subtitle.Margin = new System.Windows.Forms.Padding(0, 0, 0, 14);
        root.Controls.Add(title);
        root.Controls.Add(subtitle);
        root.Controls.Add(Field("Home Assistant address", _haAddress,
            "Use the same HTTP or HTTPS address that opens Home Assistant in your browser."));
        root.Controls.Add(Field("Long-lived access token", _token,
            "Create one in your Home Assistant profile under Security → Long-lived access tokens."));

        var identifiers = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 10),
        };
        identifiers.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50));
        identifiers.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50));
        var kidField = Field("Kid ID", _kid, "Must exactly match the Kid ID in the Gnomon integration.");
        kidField.Margin = new System.Windows.Forms.Padding(0, 0, 9, 0);
        var deviceField = Field("Device ID", _device, "A short name for this computer.");
        deviceField.Margin = new System.Windows.Forms.Padding(9, 0, 0, 0);
        identifiers.Controls.Add(kidField, 0, 0);
        identifiers.Controls.Add(deviceField, 1, 0);
        root.Controls.Add(identifiers);
        root.Controls.Add(Field("Windows account to track", _windowsUser,
            "Enter the Windows sign-in name used by the person this device belongs to."));

        _status.AutoSize = false;
        _status.Height = 54;
        _status.Dock = System.Windows.Forms.DockStyle.Top;
        _status.Padding = new System.Windows.Forms.Padding(12, 10, 12, 10);
        _status.BackColor = Color.FromArgb(238, 245, 255);
        _status.ForeColor = Color.FromArgb(33, 74, 117);
        _status.Text = "Configuration is stored on this PC and used only to connect directly to Home Assistant.";
        root.Controls.Add(_status);

        var spacer = new System.Windows.Forms.Panel { Dock = System.Windows.Forms.DockStyle.Fill };
        root.Controls.Add(spacer);

        var buttons = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
            FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new System.Windows.Forms.Padding(0, 12, 0, 0),
        };
        _save.Text = "Save and start";
        _save.AutoSize = true;
        _save.Padding = new System.Windows.Forms.Padding(10, 5, 10, 5);
        _save.Click += SaveClick;
        _cancel.Text = "Cancel";
        _cancel.AutoSize = true;
        _cancel.Padding = new System.Windows.Forms.Padding(10, 5, 10, 5);
        _cancel.Click += (_, _) => Close();
        _browserSetup.Text = "Chrome…";
        _browserSetup.AutoSize = true;
        _browserSetup.Padding = new System.Windows.Forms.Padding(10, 5, 10, 5);
        _browserSetup.Click += (_, _) => new BrowserSetupWindow().ShowDialog(this);
        _classifications.Text = "Classifications…";
        _classifications.AutoSize = true;
        _classifications.Padding = new System.Windows.Forms.Padding(10, 5, 10, 5);
        _classifications.Click += OpenClassifications;
        buttons.Controls.Add(_save);
        buttons.Controls.Add(_classifications);
        buttons.Controls.Add(_browserSetup);
        buttons.Controls.Add(_cancel);
        root.Controls.Add(buttons);

        AcceptButton = _save;
        CancelButton = _cancel;
        Controls.Add(root);
    }

    private void OpenClassifications(object? sender, EventArgs e)
    {
        if (!AgentConfiguration.TryNormalizeHomeAssistantUrl(_haAddress.Text, out var haUrl) ||
            string.IsNullOrWhiteSpace(_token.Text) || string.IsNullOrWhiteSpace(_kid.Text))
        {
            ShowError("Home Assistant address, token, and Kid ID are required to manage classifications.");
            return;
        }
        var config = new AgentConfig(
            haUrl, _token.Text.Trim(), _kid.Text.Trim(), _device.Text.Trim(),
            _windowsUser.Text.Trim(), 45981);
        new ClassificationWindow(config, new LocalActivityStore(_paths.ActivityFile)).ShowDialog(this);
    }

    private static System.Windows.Forms.Control Field(
        string title, System.Windows.Forms.TextBox input, string help)
    {
        input.Dock = System.Windows.Forms.DockStyle.Top;
        input.Margin = new System.Windows.Forms.Padding(0, 5, 0, 4);
        var panel = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 10),
        };
        panel.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = title,
            Font = new Font("Segoe UI Semibold", 9F),
        });
        panel.Controls.Add(input);
        panel.Controls.Add(Help(help));
        return panel;
    }

    private static System.Windows.Forms.Label Help(string text) => new()
    {
        AutoSize = true,
        MaximumSize = new Size(490, 0),
        ForeColor = Color.DimGray,
        Text = text,
    };

    private async void SaveClick(object? sender, EventArgs e)
    {
        if (!AgentConfiguration.TryNormalizeHomeAssistantUrl(_haAddress.Text, out var haUrl))
        {
            ShowError("Enter a valid Home Assistant hostname or URL.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_token.Text))
        {
            ShowError("Enter a Home Assistant long-lived access token.");
            return;
        }
        if (string.IsNullOrWhiteSpace(_kid.Text) || string.IsNullOrWhiteSpace(_device.Text) ||
            string.IsNullOrWhiteSpace(_windowsUser.Text))
        {
            ShowError("Kid ID, device ID, and Windows account are required.");
            return;
        }

        _save.Enabled = false;
        _cancel.Enabled = false;
        _status.ForeColor = Color.FromArgb(33, 74, 117);
        _status.Text = "Saving configuration and starting Gnomon…";

        try
        {
            var config = new AgentConfig(haUrl, _token.Text.Trim(), _kid.Text.Trim(), _device.Text.Trim(),
                _windowsUser.Text.Trim(), 45981);
            Directory.CreateDirectory(_paths.DataDirectory);
            Directory.CreateDirectory(_paths.LogDirectory);
            var temporaryFile = _paths.ConfigFile + ".tmp";
            try
            {
                var options = new JsonSerializerOptions(ProtocolCodec.JsonOptions) { WriteIndented = true };
                File.WriteAllText(temporaryFile, JsonSerializer.Serialize(config, options));
                if (File.Exists(_paths.ConfigFile)) File.Replace(temporaryFile, _paths.ConfigFile, null);
                else File.Move(temporaryFile, _paths.ConfigFile);
            }
            finally
            {
                if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
            }

            await Task.Run(RestartService);
            _status.ForeColor = Color.FromArgb(25, 112, 59);
            _status.Text = "Gnomon is configured. The tray agent should appear within a few seconds.";
            _save.Text = "Finish";
            _save.Enabled = true;
            _save.Click -= SaveClick;
            _save.Click += (_, _) => Close();
        }
        catch (Exception exception)
        {
            ShowError("Could not save the configuration: " + exception.Message);
            _save.Enabled = true;
            _cancel.Enabled = true;
        }
    }

    private static void RestartService()
    {
        using var service = new ServiceController("GnomonAgent");
        service.Refresh();
        if (service.Status != ServiceControllerStatus.Stopped && service.Status != ServiceControllerStatus.StopPending)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        else if (service.Status == ServiceControllerStatus.StopPending)
        {
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
    }

    private void ShowError(string message)
    {
        _status.ForeColor = Color.FromArgb(178, 34, 34);
        _status.Text = message;
    }
}
