using System.Drawing;
using Gnomon.Core;

namespace Gnomon.Agent;

internal sealed class ClassificationWindow : System.Windows.Forms.Form
{
    private readonly AgentConfig _config;
    private readonly LocalActivityStore _activity;
    private readonly HaAdminClient _client = new();
    private readonly System.Windows.Forms.TextBox _search = new();
    private readonly System.Windows.Forms.ComboBox _kind = new();
    private readonly System.Windows.Forms.DataGridView _grid = new();
    private readonly System.Windows.Forms.Label _summary = new();
    private readonly System.Windows.Forms.Label _status = new();
    private readonly System.Windows.Forms.Button _refresh = new();
    private ClassificationCatalog _catalog = new(0, [], []);
    private RulesMap _rules = RulesMap.Empty;
    private bool _loading;

    public ClassificationWindow(AgentConfig config, LocalActivityStore activity)
    {
        _config = config;
        _activity = activity;
        Text = "Gnomon classifications";
        ClientSize = new Size(860, 590);
        MinimumSize = SizeFromClientSize(new Size(720, 500));
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Program.ExecutablePath);

        var root = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Fill,
            Padding = new System.Windows.Forms.Padding(24, 20, 24, 18),
            ColumnCount = 1,
            RowCount = 6,
        };
        root.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100));
        root.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
        root.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true, Text = "Classify activity",
            Font = new Font("Segoe UI Semibold", 22F),
        });
        root.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true,
            Text = "Classify only activity seen on this PC. Home Assistant syncs the resulting rules—not your browsing list.",
            ForeColor = Color.DimGray,
            Margin = new System.Windows.Forms.Padding(0, 2, 0, 14),
        });

        var toolbar = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top, AutoSize = true, ColumnCount = 4,
            Margin = new System.Windows.Forms.Padding(0, 0, 0, 10),
        };
        toolbar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 130));
        toolbar.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
        _search.AccessibleName = "Search apps and websites";
        _search.Dock = System.Windows.Forms.DockStyle.Fill;
        _search.TextChanged += (_, _) => RebuildRows();
        _kind.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
        _kind.Items.AddRange(["All activity", "Apps", "Websites"]);
        _kind.SelectedIndex = 0;
        _kind.Dock = System.Windows.Forms.DockStyle.Fill;
        _kind.SelectedIndexChanged += (_, _) => RebuildRows();
        _refresh.Text = "Refresh";
        _refresh.AutoSize = true;
        _refresh.Click += async (_, _) => await RefreshAsync();
        toolbar.Controls.Add(new System.Windows.Forms.Label
        {
            AutoSize = true, Text = "Search", Anchor = System.Windows.Forms.AnchorStyles.Left,
            Margin = new System.Windows.Forms.Padding(0, 6, 8, 0),
        }, 0, 0);
        toolbar.Controls.Add(_search, 1, 0);
        toolbar.Controls.Add(_kind, 2, 0);
        toolbar.Controls.Add(_refresh, 3, 0);
        root.Controls.Add(toolbar);

        _summary.AutoSize = true;
        _summary.Font = new Font("Segoe UI Semibold", 9F);
        _summary.Margin = new System.Windows.Forms.Padding(0, 0, 0, 8);
        root.Controls.Add(_summary);

        ConfigureGrid();
        root.Controls.Add(_grid);

        var footer = new System.Windows.Forms.TableLayoutPanel
        {
            Dock = System.Windows.Forms.DockStyle.Top, AutoSize = true, ColumnCount = 2,
            Margin = new System.Windows.Forms.Padding(0, 10, 0, 0),
        };
        footer.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100));
        footer.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
        _status.AutoSize = true;
        _status.ForeColor = Color.DimGray;
        _status.Text = "Assignments affect future minutes; today's recorded totals stay auditable.";
        var close = new System.Windows.Forms.Button { Text = "Close", AutoSize = true };
        close.Click += (_, _) => Close();
        footer.Controls.Add(_status, 0, 0);
        footer.Controls.Add(close, 1, 0);
        root.Controls.Add(footer);
        Controls.Add(root);
        Shown += async (_, _) => await RefreshAsync();
    }

    private void ConfigureGrid()
    {
        _grid.Dock = System.Windows.Forms.DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoGenerateColumns = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = System.Windows.Forms.DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn
            { Name = "Label", HeaderText = "App or website", FillWeight = 24, AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn
            { Name = "Identifier", HeaderText = "Identifier", FillWeight = 34, AutoSizeMode = System.Windows.Forms.DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn
            { Name = "Type", HeaderText = "Type", Width = 76 });
        _grid.Columns.Add(new System.Windows.Forms.DataGridViewTextBoxColumn
            { Name = "Minutes", HeaderText = "Minutes", Width = 72 });
        _grid.Columns.Add(new System.Windows.Forms.DataGridViewComboBoxColumn
            { Name = "Category", HeaderText = "Bucket", Width = 150, FlatStyle = System.Windows.Forms.FlatStyle.Flat });
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty) _grid.CommitEdit(System.Windows.Forms.DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += async (_, eventArgs) => await CategoryChangedAsync(eventArgs);
        _grid.DataError += (_, _) => { };
    }

    private async Task RefreshAsync()
    {
        SetBusy(true, "Loading activity from Home Assistant…");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _rules = await _client.GetRulesAsync(_config, timeout.Token);
            _catalog = BuildLocalCatalog();
            ConfigureCategories();
            RebuildRows();
            _status.ForeColor = Color.DimGray;
            _status.Text = "Assignments affect future minutes; today's recorded totals stay auditable.";
        }
        catch (Exception exception)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = "Could not load classifications: " + exception.Message;
        }
        finally { SetBusy(false); }
    }

    private void ConfigureCategories()
    {
        var column = (System.Windows.Forms.DataGridViewComboBoxColumn)_grid.Columns["Category"];
        column.DataSource = _catalog.Categories.ToList();
        column.DisplayMember = nameof(ClassificationCategory.Name);
        column.ValueMember = nameof(ClassificationCategory.Id);
    }

    private void RebuildRows()
    {
        _loading = true;
        try
        {
            var query = _search.Text.Trim();
            var kind = _kind.SelectedIndex switch { 1 => "process", 2 => "domain", _ => "" };
            var items = _catalog.Items.Where(item =>
                (kind.Length == 0 || item.Kind == kind) &&
                (query.Length == 0 || item.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                 item.Id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            _grid.Rows.Clear();
            foreach (var item in items)
            {
                var index = _grid.Rows.Add(
                    item.Label, item.Id, item.Kind == "domain" ? "Website" : "App",
                    item.Minutes, item.Category);
                var row = _grid.Rows[index];
                row.Tag = item;
                if (item.Unclassified) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 248, 230);
            }
            _summary.Text = $"{items.Count} items · {items.Sum(item => item.Minutes)} minutes shown · rules v{_catalog.Version}";
        }
        finally { _loading = false; }
    }

    private async Task CategoryChangedAsync(System.Windows.Forms.DataGridViewCellEventArgs eventArgs)
    {
        if (_loading || eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != _grid.Columns["Category"].Index) return;
        var row = _grid.Rows[eventArgs.RowIndex];
        if (row.Tag is not ClassificationItem item || row.Cells[eventArgs.ColumnIndex].Value is not string category ||
            category == item.Category) return;
        SetBusy(true, $"Moving {item.Label} to {category}…");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            _rules = await _client.SetClassificationAsync(_config, item, category, timeout.Token);
            _catalog = BuildLocalCatalog();
            ConfigureCategories();
            RebuildRows();
            _status.ForeColor = Color.FromArgb(25, 112, 59);
            _status.Text = $"{item.Label} now uses {category}. The new rule is syncing to agents.";
        }
        catch (Exception exception)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = "Could not change the bucket: " + exception.Message;
            RebuildRows();
        }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _refresh.Enabled = !busy;
        _grid.Enabled = !busy;
        if (message is not null)
        {
            _status.ForeColor = Color.FromArgb(33, 74, 117);
            _status.Text = message;
        }
    }

    private ClassificationCatalog BuildLocalCatalog()
    {
        var classifier = new Classifier();
        var now = DateTimeOffset.UtcNow;
        var items = _activity.Read().Select(value =>
        {
            var classification = value.Kind == "domain"
                ? classifier.Classify("chrome.exe", _config.Kid, value.Id, now, now, _rules)
                : classifier.Classify(value.Id, _config.Kid, null, null, now, _rules);
            return new ClassificationItem(
                value.Kind, value.Id, value.Label, classification.Category, value.Minutes,
                [_config.Device], value.LastSeen.ToString("O"), classification.IsUnknown);
        }).OrderByDescending(value => value.Minutes).ThenBy(value => value.Label).ToList();
        return new ClassificationCatalog(
            _rules.Version,
            _rules.Categories.Select(value => new ClassificationCategory(value.Id, value.Name)).ToList(),
            items);
    }
}
