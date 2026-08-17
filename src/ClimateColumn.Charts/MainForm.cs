using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Hosts the chart, a values grid, and a Save PNG action. The sweep runs on a background
/// thread so the window paints immediately rather than appearing frozen for several seconds.
/// </summary>
public sealed class MainForm : Form
{
    private readonly Co2ChartView _chart = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripButton _saveButton = new();
    private readonly ToolStripButton _themeButton = new();
    private readonly SplitContainer _split = new();

    private Co2Sweep[] _sweeps = Array.Empty<Co2Sweep>();
    private bool _dark;

    public MainForm()
    {
        Text = "ClimateColumn — CO₂ response";
        MinimumSize = new Size(900, 620);
        Size = new Size(1180, 820);
        StartPosition = FormStartPosition.CenterScreen;

        var toolbar = BuildToolbar();

        _split.Dock = DockStyle.Fill;
        _split.Orientation = Orientation.Horizontal;
        _split.Panel1.Controls.Add(_chart);
        _split.Panel2.Controls.Add(BuildGrid());
        _split.Panel1MinSize = 300;
        _split.Panel2MinSize = 120;

        _statusLabel.Text = "Running the column to equilibrium at each concentration…";
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _status.Items.Add(_statusLabel);

        Controls.Add(_split);
        Controls.Add(toolbar);
        Controls.Add(_status);

        _chart.HoverChanged += OnHoverChanged;

        ApplyTheme();
        Load += async (_, _) => await RunSweepsAsync();
    }

    private ToolStrip BuildToolbar()
    {
        _saveButton.Text = "Save PNG…";
        _saveButton.Enabled = false;
        _saveButton.Click += (_, _) => SavePng();

        _themeButton.Text = "Dark";
        _themeButton.Click += (_, _) =>
        {
            _dark = !_dark;
            _themeButton.Text = _dark ? "Light" : "Dark";
            ApplyTheme();
        };

        var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.Add(_saveButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_themeButton);
        return toolbar;
    }

    private DataGridView BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EditMode = DataGridViewEditMode.EditProgrammatically;
        return _grid;
    }

    private async Task RunSweepsAsync()
    {
        // Co2Sweep.Run marches the column to equilibrium at every concentration, which takes
        // a few seconds; keep it off the UI thread.
        var sweeps = await Task.Run(() => new[]
        {
            Co2Sweep.NoFeedback(),
            Co2Sweep.WithWaterVapourFeedback()
        });

        _sweeps = sweeps;
        _chart.SetSweeps(sweeps);
        FillGrid();

        _saveButton.Enabled = true;
        _statusLabel.Text = Summary();
    }

    private string Summary()
    {
        int last = Co2Sweep.Concentrations.Length - 1;
        var parts = _sweeps.Select(s => string.Format(CultureInfo.InvariantCulture,
            "{0}: +{1:F2} K (expected +{2:F2} K)",
            s.Label, s.Warming(last), s.Expected(last) - s.BaseTemperature));

        return string.Format(CultureInfo.InvariantCulture, "{0:F0} → {1:F0} ppm — ",
            Co2Sweep.Concentrations[0], Co2Sweep.Concentrations[last]) + string.Join("   ·   ", parts);
    }

    private void FillGrid()
    {
        _grid.Columns.Clear();
        _grid.Rows.Clear();

        _grid.Columns.Add("ppm", "CO₂ (ppm)");
        foreach (var sweep in _sweeps)
        {
            _grid.Columns.Add("tau" + sweep.Label, "dry τ");
            _grid.Columns.Add("model" + sweep.Label, sweep.Label);
            _grid.Columns.Add("expected" + sweep.Label, "Expected");
        }

        foreach (var column in _grid.Columns.Cast<DataGridViewColumn>())
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        for (int i = 0; i < Co2Sweep.Concentrations.Length; i++)
        {
            var cells = new List<object> { Co2Sweep.Concentrations[i].ToString("N0", CultureInfo.InvariantCulture) };
            foreach (var sweep in _sweeps)
            {
                cells.Add(sweep.Points[i].DryOpticalDepth.ToString("F3", CultureInfo.InvariantCulture));
                cells.Add(sweep.Points[i].SurfaceTemperature.ToString("F3", CultureInfo.InvariantCulture));
                cells.Add(sweep.Expected(i).ToString("F3", CultureInfo.InvariantCulture));
            }
            _grid.Rows.Add(cells.ToArray());
        }
    }

    private void OnHoverChanged(int? index)
    {
        if (_sweeps.Length == 0) return;

        if (index is null)
        {
            _statusLabel.Text = Summary();
            _grid.ClearSelection();
            return;
        }

        int i = index.Value;
        var parts = _sweeps.Select(s => string.Format(CultureInfo.InvariantCulture,
            "{0}: {1:F3} K (expected {2:F3} K, over by {3:F3})",
            s.Label, s.Points[i].SurfaceTemperature, s.Expected(i), s.Overshoot(i)));

        _statusLabel.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0} ppm — ",
            Co2Sweep.Concentrations[i]) + string.Join("   ·   ", parts);

        if (i < _grid.Rows.Count)
        {
            _grid.ClearSelection();
            _grid.Rows[i].Selected = true;
            _grid.FirstDisplayedScrollingRowIndex = i;
        }
    }

    private void ApplyTheme()
    {
        var theme = _dark ? ChartTheme.Dark : ChartTheme.Light;
        _chart.Theme = theme;
        _chart.Invalidate();

        BackColor = theme.Plane;
        ForeColor = theme.Ink;

        _grid.BackgroundColor = theme.Surface;
        _grid.ForeColor = theme.Ink;
        _grid.GridColor = theme.Grid;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = theme.Plane;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = theme.InkSecondary;
        _grid.DefaultCellStyle.BackColor = theme.Surface;
        _grid.DefaultCellStyle.ForeColor = theme.Ink;
        _grid.DefaultCellStyle.SelectionBackColor = theme.Grid;
        _grid.DefaultCellStyle.SelectionForeColor = theme.Ink;

        _status.BackColor = theme.Plane;
        _statusLabel.ForeColor = theme.InkSecondary;
    }

    private void SavePng()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = "co2-response.png",
            Title = "Save the chart"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        Co2ChartExport.SavePng(dialog.FileName, _sweeps,
            _dark ? ChartTheme.Dark : ChartTheme.Light, _chart.Width, _chart.Height);

        _statusLabel.Text = $"Saved {dialog.FileName}";
    }
}
