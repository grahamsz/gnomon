using System.Diagnostics;
using System.Drawing;

namespace Gnomon.Agent;

public sealed class BrowserSetupWindow : System.Windows.Forms.Form
{
    private readonly string _extensionDirectory = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Browser Companion");

    public BrowserSetupWindow()
    {
        Text = "Set up the Gnomon browser companion";
        ClientSize = new Size(600, 390);
        MinimumSize = SizeFromClientSize(new Size(540, 390));
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Program.ExecutablePath);

        var root = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            Padding = new System.Windows.Forms.Padding(28, 22, 28, 18),
            ColumnCount = 1,
            RowCount = 5,
        };
        root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));

        root.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = "Connect Chrome",
            Font = new Font("Segoe UI Semibold", 22F),
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 6),
        });
        root.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true,
            MaximumSize = new Size(530, 0),
            Text = "Chrome requires you to approve locally installed extensions. This takes about a minute and only needs to be done once.",
            ForeColor = Color.DimGray,
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 18),
        });

        var instructions = new System.Windows.Forms.Label
        {
            AutoSize = true,
            MaximumSize = new Size(530, 0),
            Text = "1. Open Chrome extensions.\r\n2. Turn on Developer mode.\r\n3. Choose Load unpacked.\r\n4. Select the Browser Companion folder below.",
            Font = new Font("Segoe UI", 10F),
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 14),
        };
        root.Controls.Add(instructions);

        var folderPanel = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
        };
        folderPanel.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = "Browser Companion folder",
            Font = new Font("Segoe UI Semibold", 9F),
        });
        folderPanel.Controls.Add(new System.Windows.Forms.TextBox
        {
            Dock = System.Windows.Forms.DockStyle.Top,
            ReadOnly = true,
            Text = _extensionDirectory,
            Margin = new System.Windows.Forms.Padding(0, 5, 0, 0),
        });
        root.Controls.Add(folderPanel);

        var buttons = new System.Windows.Forms.FlowLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink,
            FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new System.Windows.Forms.Padding(0, 12, 0, 0),
        };
        var close = Button("Close", (_, _) => Close());
        var copy = Button("Copy folder path", (_, _) => System.Windows.Forms.Clipboard.SetText(_extensionDirectory));
        var openFolder = Button("Open folder", (_, _) => OpenFolder());
        var openChrome = Button("Open Chrome extensions", (_, _) => OpenChromeExtensions());
        buttons.Controls.Add(close);
        buttons.Controls.Add(copy);
        buttons.Controls.Add(openFolder);
        buttons.Controls.Add(openChrome);
        root.Controls.Add(buttons);

        AcceptButton = openChrome;
        CancelButton = close;
        Controls.Add(root);
    }

    private static System.Windows.Forms.Button Button(string text, EventHandler click)
    {
        var button = new System.Windows.Forms.Button
        {
            AutoSize = true,
            Text = text,
            Padding = new System.Windows.Forms.Padding(8, 4, 8, 4),
        };
        button.Click += click;
        return button;
    }

    private void OpenFolder()
    {
        if (!EnsureExtensionInstalled()) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{_extensionDirectory}\"",
            UseShellExecute = true,
        });
    }

    private void OpenChromeExtensions()
    {
        if (!EnsureExtensionInstalled()) return;
        var chrome = ChromeExecutable();
        if (chrome is null)
        {
            System.Windows.Forms.Clipboard.SetText("chrome://extensions");
            System.Windows.Forms.MessageBox.Show(this,
                "Chrome was not found. Open Chrome, paste chrome://extensions into the address bar, then choose Load unpacked.",
                "Open Chrome extensions", System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = chrome,
            Arguments = "--new-tab chrome://extensions",
            UseShellExecute = true,
        });
        OpenFolder();
    }

    private bool EnsureExtensionInstalled()
    {
        if (File.Exists(Path.Combine(_extensionDirectory, "manifest.json"))) return true;
        System.Windows.Forms.MessageBox.Show(this,
            "The Browser Companion files are missing. Install the latest Gnomon dev MSI, then try again.",
            "Browser Companion not installed", System.Windows.Forms.MessageBoxButtons.OK,
            System.Windows.Forms.MessageBoxIcon.Warning);
        return false;
    }

    private static string? ChromeExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Google", "Chrome", "Application", "chrome.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
