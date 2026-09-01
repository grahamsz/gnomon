using System.IO;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Gnomon.Core;
using MediaColor = System.Windows.Media.Color;

namespace Gnomon.Agent;

public partial class ConfigurationWindow : Window
{
    private readonly AgentPaths _paths;

    public ConfigurationWindow(AgentPaths paths, AgentConfig config)
    {
        InitializeComponent();
        _paths = paths;

        var placeholder = AgentConfiguration.IsPlaceholder(config);
        HaAddressText.Text = placeholder ? AgentConfiguration.DefaultHomeAssistantAddress : config.HaUrl;
        TokenText.Password = placeholder ? "" : config.HaToken;
        KidText.Text = placeholder && config.Kid.Equals("alex", StringComparison.OrdinalIgnoreCase) ? "" : config.Kid;
        DeviceText.Text = string.IsNullOrWhiteSpace(config.Device) || placeholder
            ? Environment.MachineName.ToLowerInvariant()
            : config.Device;
        WindowsUserText.Text = string.IsNullOrWhiteSpace(config.WindowsUser) ||
                               (placeholder && config.WindowsUser.Equals("Alex", StringComparison.OrdinalIgnoreCase))
            ? Environment.UserName
            : config.WindowsUser;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!AgentConfiguration.TryNormalizeHomeAssistantUrl(HaAddressText.Text, out var haUrl))
        {
            ShowError("Enter a valid Home Assistant hostname or URL.");
            return;
        }
        if (string.IsNullOrWhiteSpace(TokenText.Password))
        {
            ShowError("Enter a Home Assistant long-lived access token.");
            return;
        }
        if (string.IsNullOrWhiteSpace(KidText.Text) || string.IsNullOrWhiteSpace(DeviceText.Text) ||
            string.IsNullOrWhiteSpace(WindowsUserText.Text))
        {
            ShowError("Kid ID, device ID, and Windows account are required.");
            return;
        }

        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        StatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(33, 74, 117));
        StatusText.Text = "Saving configuration and starting Gnomon…";

        try
        {
            var config = new AgentConfig(
                haUrl,
                TokenText.Password.Trim(),
                KidText.Text.Trim(),
                DeviceText.Text.Trim(),
                WindowsUserText.Text.Trim(),
                45981);
            Directory.CreateDirectory(_paths.DataDirectory);
            Directory.CreateDirectory(_paths.LogDirectory);
            var temporaryFile = $"{_paths.ConfigFile}.tmp";
            try
            {
                var options = new JsonSerializerOptions(ProtocolCodec.JsonOptions) { WriteIndented = true };
                await File.WriteAllTextAsync(temporaryFile, JsonSerializer.Serialize(config, options));
                File.Move(temporaryFile, _paths.ConfigFile, true);
            }
            finally
            {
                if (File.Exists(temporaryFile)) File.Delete(temporaryFile);
            }

            await Task.Run(RestartService);
            StatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(25, 112, 59));
            StatusText.Text = "Gnomon is configured. The tray agent should appear within a few seconds.";
            SaveButton.Content = "Finish";
            SaveButton.IsEnabled = true;
            SaveButton.Click -= Save_Click;
            SaveButton.Click += Finish_Click;
        }
        catch (Exception exception)
        {
            ShowError($"Could not save the configuration: {exception.Message}");
            SaveButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private static void RestartService()
    {
        using var service = new ServiceController("GnomonAgent");
        service.Refresh();
        if (service.Status is not ServiceControllerStatus.Stopped and not ServiceControllerStatus.StopPending)
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
        StatusText.Foreground = new SolidColorBrush(MediaColor.FromRgb(178, 34, 34));
        StatusText.Text = message;
    }

    private void Finish_Click(object sender, RoutedEventArgs e) => Close();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
