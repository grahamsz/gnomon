using System.Drawing;

namespace Gnomon.Agent;

public sealed class MainWindow : System.Windows.Forms.Form
{
    private readonly AgentStatus _status;
    private readonly System.Windows.Forms.Label _now = new();
    private readonly System.Windows.Forms.Label _connection = new();
    private readonly System.Windows.Forms.ListBox _usage = new();
    private readonly System.Windows.Forms.ListBox _unknown = new();

    public MainWindow(AgentStatus status)
    {
        _status = status;
        Text = "Gnomon";
        ClientSize = new Size(620, 410);
        MinimumSize = new Size(540, 360);
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Program.ExecutablePath);

        var root = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            Padding = new System.Windows.Forms.Padding(20),
            ColumnCount = 2,
            RowCount = 5,
        };
        root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50));
        root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle());
        root.RowStyles.Add(new System.Windows.Forms.RowStyle());
        root.RowStyles.Add(new System.Windows.Forms.RowStyle());
        root.RowStyles.Add(new System.Windows.Forms.RowStyle());
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));

        var title = new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = "Gnomon",
            Font = new Font("Segoe UI Semibold", 20F),
        };
        root.Controls.Add(title, 0, 0);
        root.SetColumnSpan(title, 2);
        var subtitle = new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = "Your screen time, plainly visible",
            ForeColor = Color.DimGray,
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 18),
        };
        root.Controls.Add(subtitle, 0, 1);
        root.SetColumnSpan(subtitle, 2);
        root.Controls.Add(Section("Now", _now), 0, 2);
        root.Controls.Add(Section("Status", _connection), 1, 2);
        root.Controls.Add(Section("Local reported minutes", _usage), 0, 4);
        root.Controls.Add(Section("Currently unclassified", _unknown), 1, 4);
        Controls.Add(root);

        _status.Changed += StatusChanged;
        RefreshStatus();
    }

    private static System.Windows.Forms.Control Section(string title, System.Windows.Forms.Control content)
    {
        content.Dock = System.Windows.Forms.DockStyle.Fill;
        var panel = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new System.Windows.Forms.Padding(0, 0, 14, 14),
        };
        panel.RowStyles.Add(new System.Windows.Forms.RowStyle());
        panel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));
        panel.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = title,
            Font = new Font("Segoe UI Semibold", 9F),
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 4),
        });
        panel.Controls.Add(content);
        return panel;
    }

    private void StatusChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)RefreshStatus);
    }

    private void RefreshStatus()
    {
        var value = _status.Snapshot;
        _now.Text = value.ForegroundApp + Environment.NewLine + value.Category + " · " +
                    (value.Counting ? "counting" : "not counting");
        _connection.Text = "Home Assistant: " + (value.HaConnected ? "connected" : "offline") +
                           Environment.NewLine + "Browser extension: " +
                           (value.ExtensionReachable ? "connected" : "stale") +
                           Environment.NewLine + "Rules: v" + value.RulesVersion;
        _usage.Items.Clear();
        _usage.Items.AddRange(_status.CategoryTotals.Select(x =>
            x.Key + ": " + x.Value.Used + "/" + x.Value.Limit + " min · " +
            Math.Max(0, x.Value.Limit - x.Value.Used) + " remaining").Cast<object>().ToArray());
        _unknown.Items.Clear();
        _unknown.Items.AddRange(value.UnknownItems.Cast<object>().ToArray());
    }

    protected override void OnFormClosing(System.Windows.Forms.FormClosingEventArgs e)
    {
        if (e.CloseReason == System.Windows.Forms.CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _status.Changed -= StatusChanged;
        base.Dispose(disposing);
    }
}
