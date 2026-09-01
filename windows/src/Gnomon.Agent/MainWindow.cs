using System.Drawing;

namespace Gnomon.Agent;

public sealed class MainWindow : System.Windows.Forms.Form
{
    private readonly AgentStatus _status;
    private readonly System.Windows.Forms.Label _remaining = new();
    private readonly System.Windows.Forms.Label _allowances = new();
    private readonly System.Windows.Forms.Label _now = new();
    private readonly System.Windows.Forms.Label _connection = new();
    private readonly System.Windows.Forms.TableLayoutPanel _categories = new();

    public MainWindow(AgentStatus status)
    {
        _status = status;
        Text = "Gnomon";
        ClientSize = new Size(600, 560);
        MinimumSize = SizeFromClientSize(new Size(520, 480));
        StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Program.ExecutablePath);

        var root = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            Padding = new System.Windows.Forms.Padding(26, 22, 26, 20),
            ColumnCount = 1, RowCount = 7,
        };
        root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));

        root.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true, Text = "Screen time left",
            Font = new Font("Segoe UI Semibold", 11F), ForeColor = Color.DimGray,
        });
        _remaining.AutoSize = true;
        _remaining.Font = new Font("Segoe UI Semibold", 30F);
        _remaining.Margin = new System.Windows.Forms.Padding(0, 2, 0, 2);
        root.Controls.Add(_remaining);
        _allowances.AutoSize = true;
        _allowances.ForeColor = Color.DimGray;
        _allowances.Margin = new System.Windows.Forms.Padding(0, 0, 0, 18);
        root.Controls.Add(_allowances);
        root.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true, Text = "Categories", Font = new Font("Segoe UI Semibold", 13F),
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 8),
        });
        _categories.Dock = System.Windows.Forms.DockStyle.Fill;
        _categories.AutoScroll = true;
        _categories.ColumnCount = 1;
        root.Controls.Add(_categories);

        var footer = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top, AutoSize = true, ColumnCount = 2,
            Margin = new System.Windows.Forms.Padding(0, 16, 0, 0),
        };
        footer.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 65));
        footer.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 35));
        _now.AutoSize = true;
        _connection.AutoSize = true;
        _connection.TextAlign = ContentAlignment.TopRight;
        _connection.Dock = System.Windows.Forms.DockStyle.Fill;
        footer.Controls.Add(_now, 0, 0);
        footer.Controls.Add(_connection, 1, 0);
        root.Controls.Add(footer);
        Controls.Add(root);

        _status.Changed += StatusChanged;
        RefreshStatus();
    }

    private void StatusChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed && IsHandleCreated) BeginInvoke((Action)RefreshStatus);
    }

    private void RefreshStatus()
    {
        var value = _status.Snapshot;
        var child = _status.ChildOverall;
        var device = _status.DeviceOverall;
        var constrained = new[] { Remaining(child), Remaining(device) }
            .Where(x => x.HasValue).Select(x => x!.Value).ToList();
        if (constrained.Count == 0)
        {
            _remaining.Text = "No overall limit";
            _remaining.ForeColor = Color.FromArgb(32, 72, 55);
        }
        else
        {
            var left = Math.Max(0, constrained.Min());
            _remaining.Text = left == 1 ? "1 minute" : $"{left} minutes";
            _remaining.ForeColor = left == 0 ? Color.Firebrick : Color.FromArgb(32, 72, 55);
        }
        _allowances.Text = $"Child: {AllowanceText(child)}    •    This PC: {AllowanceText(device)}";

        _categories.SuspendLayout();
        _categories.Controls.Clear();
        _categories.RowStyles.Clear();
        _categories.RowCount = 0;
        foreach (var category in _status.CategoryTotals.OrderBy(x => x.Value.Name))
            AddCategory(category.Value.Name, (category.Value.Used, category.Value.Limit));
        if (_status.CategoryTotals.Count == 0)
            _categories.Controls.Add(new System.Windows.Forms.Label
            {
                AutoSize = true, Text = "Waiting for Home Assistant totals…", ForeColor = Color.DimGray,
            });
        _categories.ResumeLayout();

        _now.Text = $"Now: {value.ForegroundApp}\r\n{value.Category} · {(value.Counting ? "counting" : "not counting")}";
        _connection.Text = value.HaConnected ? $"Connected · rules v{value.RulesVersion}" : "Home Assistant offline";
        _connection.ForeColor = value.HaConnected ? Color.FromArgb(25, 112, 59) : Color.Firebrick;
    }

    private void AddCategory(string name, (int Used, int Limit) value)
    {
        var panel = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 2,
            Padding = new System.Windows.Forms.Padding(12, 10, 12, 10),
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 8),
            BackColor = Color.FromArgb(246, 247, 249),
        };
        panel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 65));
        panel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 35));
        panel.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true, Text = name, Font = new Font("Segoe UI Semibold", 10F),
        }, 0, 0);
        var remaining = Remaining(value);
        panel.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true, Dock = System.Windows.Forms.DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Text = remaining.HasValue ? $"{Math.Max(0, remaining.Value)} min left" : $"{value.Used} min used",
            ForeColor = remaining.HasValue && remaining.Value <= 0 ? Color.Firebrick : Color.FromArgb(32, 72, 55),
            Font = new Font("Segoe UI Semibold", 10F),
        }, 1, 0);
        var detail = new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = value.Limit > 0 ? $"{value.Used} of {value.Limit} minutes" : "No category limit",
            ForeColor = Color.DimGray,
        };
        panel.Controls.Add(detail, 0, 1);
        panel.SetColumnSpan(detail, 2);
        _categories.RowCount++;
        _categories.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        _categories.Controls.Add(panel, 0, _categories.RowCount - 1);
    }

    private static int? Remaining((int Used, int Limit) value) =>
        value.Limit > 0 ? value.Limit - value.Used : null;

    private static string AllowanceText((int Used, int Limit) value) =>
        value.Limit > 0 ? $"{Math.Max(0, value.Limit - value.Used)} min left" : "unlimited";

    protected override void OnFormClosing(System.Windows.Forms.FormClosingEventArgs e)
    {
        if (e.CloseReason == System.Windows.Forms.CloseReason.UserClosing)
        {
            e.Cancel = true; Hide(); return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _status.Changed -= StatusChanged;
        base.Dispose(disposing);
    }
}
