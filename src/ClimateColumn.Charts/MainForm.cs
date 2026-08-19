using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Hosts the response chart, the vertical profile beside it, and a values grid. The sweep runs
/// on a background thread so the window paints immediately rather than appearing frozen for
/// several seconds.
/// </summary>
/// <remarks>
/// The two figures are shown together rather than on separate tabs, and they are linked: the
/// profile draws whichever concentration the pointer is over on the response chart. That is the
/// point of putting them side by side. The response chart says the surface warms by 6.37 K at
/// 1000 ppm; only the profile says where in the column that came from - the convective top
/// lifting, the emission level rising, the upper column cooling while the surface warms.
///
/// Hovering previews, clicking pins. Without pinning, the profile would revert the moment the
/// pointer left the chart, which makes it impossible to look at.
/// </remarks>
public sealed class MainForm : Form
{
    private readonly Co2ChartView _chart = new() { Dock = DockStyle.Fill };
    private readonly ProfileView _profile = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripButton _saveChartButton = new();
    private readonly ToolStripButton _saveProfileButton = new();
    private readonly ToolStripButton _themeButton = new();
    private readonly ToolStripButton _quantityButton = new();
    private readonly ToolStripButton _gridButton = new();
    private readonly ToolStripComboBox _concentration = new();
    private readonly SplitContainer _outer = new();
    private readonly SplitContainer _figures = new();

    private Co2Sweep[] _sweeps = Array.Empty<Co2Sweep>();
    private bool _dark;

    /// <summary>
    /// The concentration the profile shows when the pointer is not over the response chart.
    /// Starts at the highlighted concentration rather than the reference, because the reference
    /// profile is the one thing the figure already draws as a baseline - opening on it would
    /// show a single curve compared against itself.
    /// </summary>
    private int _pinned = Math.Max(0, Co2Sweep.HighlightIndex);

    public MainForm()
    {
        Text = "ClimateColumn — CO₂ response and vertical profile";
        MinimumSize = new Size(1040, 680);
        Size = new Size(1420, 900);
        StartPosition = FormStartPosition.CenterScreen;

        var toolbar = BuildToolbar();

        // Response chart left, profile right. The profile is portrait by nature and needs less
        // width, so it takes the smaller share and is the panel that gives way on a resize.
        _figures.Dock = DockStyle.Fill;
        _figures.Orientation = Orientation.Vertical;
        _figures.Panel1.Controls.Add(_chart);
        _figures.Panel2.Controls.Add(_profile);
        _figures.Panel1MinSize = 380;
        _figures.Panel2MinSize = 320;
        _figures.FixedPanel = FixedPanel.Panel2;

        _outer.Dock = DockStyle.Fill;
        _outer.Orientation = Orientation.Horizontal;
        _outer.Panel1.Controls.Add(_figures);
        _outer.Panel2.Controls.Add(BuildGrid());
        _outer.Panel1MinSize = 340;
        _outer.Panel2MinSize = 90;

        _statusLabel.Text = "Running the column to equilibrium at each concentration…";
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _status.Items.Add(_statusLabel);

        Controls.Add(_outer);
        Controls.Add(toolbar);
        Controls.Add(_status);

        _chart.HoverChanged += OnChartHover;
        _chart.Picked += Pin;
        _profile.HoverChanged += OnProfileHover;

        ApplyTheme();
        Load += async (_, _) =>
        {
            LayoutPanels();
            await RunSweepsAsync();
        };
    }

    /// <summary>
    /// Splitter positions, set once the form has its real size. Setting them in the constructor
    /// would size them against the design-time bounds.
    /// </summary>
    private void LayoutPanels()
    {
        _outer.SplitterDistance = Math.Max(_outer.Panel1MinSize, (int)(_outer.Height * 0.70));
        _figures.SplitterDistance = Math.Max(_figures.Panel1MinSize, (int)(_figures.Width * 0.58));
    }

    private ToolStrip BuildToolbar()
    {
        _saveChartButton.Text = "Save chart…";
        _saveChartButton.Enabled = false;
        _saveChartButton.Click += (_, _) => SaveChartPng();

        _saveProfileButton.Text = "Save profile…";
        _saveProfileButton.Enabled = false;
        _saveProfileButton.Click += (_, _) => SaveProfilePng();

        _themeButton.Text = "Dark";
        _themeButton.Click += (_, _) =>
        {
            _dark = !_dark;
            _themeButton.Text = _dark ? "Light" : "Dark";
            ApplyTheme();
        };

        // Forcing is the default view: 5.35 ln(C/C0) is a statement about forcing, so comparing
        // forcings borrows nothing from the model. The temperature view has no reference curve
        // for the same reason - drawing one would need the model's own sensitivity.
        _quantityButton.Text = "Show temperature";
        _quantityButton.Click += (_, _) =>
        {
            bool toForcing = ReferenceEquals(_chart.Quantity, Co2ChartQuantity.SurfaceTemperature);
            _chart.Quantity = toForcing
                ? Co2ChartQuantity.Forcing
                : Co2ChartQuantity.SurfaceTemperature;
            _quantityButton.Text = toForcing ? "Show temperature" : "Show forcing";
        };

        _gridButton.Text = "Hide values";
        _gridButton.Click += (_, _) =>
        {
            _outer.Panel2Collapsed = !_outer.Panel2Collapsed;
            _gridButton.Text = _outer.Panel2Collapsed ? "Show values" : "Hide values";
        };

        // The profile can also be driven without the chart, which matters when the window is
        // narrow enough that the chart is the panel that got squeezed.
        _concentration.DropDownStyle = ComboBoxStyle.DropDownList;
        _concentration.Width = 108;
        foreach (double ppm in Co2Sweep.Concentrations)
        {
            _concentration.Items.Add(ppm.ToString("N0", CultureInfo.InvariantCulture) + " ppm");
        }
        _concentration.SelectedIndex = _pinned;
        _concentration.SelectedIndexChanged += (_, _) =>
        {
            if (_concentration.SelectedIndex >= 0) Pin(_concentration.SelectedIndex);
        };

        var toolbar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden };
        toolbar.Items.Add(_saveChartButton);
        toolbar.Items.Add(_saveProfileButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_quantityButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripLabel("Profile at"));
        toolbar.Items.Add(_concentration);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_gridButton);
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

        // Clicking a row pins the profile at that concentration, so the grid drives the figure
        // as well as reporting it.
        _grid.CellClick += (_, e) => { if (e.RowIndex >= 0) Pin(e.RowIndex); };
        return _grid;
    }

    /// <summary>Fixes the profile at one concentration, from the chart, grid or picker.</summary>
    private void Pin(int index)
    {
        if (index < 0 || index >= Co2Sweep.Concentrations.Length) return;

        _pinned = index;
        _profile.Selected = index;

        if (_concentration.SelectedIndex != index) _concentration.SelectedIndex = index;
        if (index < _grid.Rows.Count && !_grid.Rows[index].Selected)
        {
            _grid.ClearSelection();
            _grid.Rows[index].Selected = true;
        }

        _statusLabel.Text = ProfileSummary(index);
    }

    private async Task RunSweepsAsync()
    {
        // Co2Sweep.ForChart marches the column to equilibrium at every concentration, which takes
        // several seconds - sixteen derived bands with sixteen g-points each - so keep it off the
        // UI thread. It returns nothing when the HITRAN line lists have not been fetched.
        var sweeps = await Task.Run(Co2Sweep.ForChart);

        _sweeps = sweeps;
        _chart.SetSweeps(sweeps);
        _profile.SetSweeps(sweeps);
        _profile.Selected = _pinned;
        FillGrid();

        if (sweeps.Length == 0)
        {
            _statusLabel.Text = "No HITRAN data — run scripts/fetch-hitran.ps1 -Molecule all.";
            return;
        }

        _saveChartButton.Enabled = true;
        _saveProfileButton.Enabled = sweeps[0].Profiles.Count > 0;
        _statusLabel.Text = Summary();
    }

    private string Summary()
    {
        int last = Co2Sweep.Concentrations.Length - 1;
        var parts = _sweeps.Select(s => string.Format(CultureInfo.InvariantCulture,
            "{0}: {1:F3} vs {2:F3} W/m² accepted  (+{3:F2} K)",
            s.Label, s.Forcings[last], s.AcceptedForcing(last), s.Warming(last)));

        return string.Format(CultureInfo.InvariantCulture, "{0:F0} → {1:F0} ppm — ",
            Co2Sweep.Concentrations[0], Co2Sweep.Concentrations[last]) + string.Join("   ·   ", parts);
    }

    /// <summary>
    /// What the profile at one concentration says, in the terms the figure draws: the surface,
    /// the depth of the convecting layer, and where the column reaches the emission temperature.
    /// </summary>
    private string ProfileSummary(int index)
    {
        if (_sweeps.Length == 0 || index >= _sweeps[0].Profiles.Count) return Summary();

        var profile = _sweeps[0].Profiles[index];
        var baseline = _sweeps[0].Profiles[0];

        string emission = double.IsNaN(profile.EmissionAltitude)
            ? "no crossing"
            : string.Format(CultureInfo.InvariantCulture, "{0:F2} km ({1:+0.00;-0.00} km)",
                profile.EmissionAltitude / 1000.0,
                (profile.EmissionAltitude - baseline.EmissionAltitude) / 1000.0);

        return string.Format(CultureInfo.InvariantCulture,
            "{0:N0} ppm — surface {1:F3} K ({2:+0.00;-0.00} K on {3:F0} ppm)   ·   " +
            "convecting to {4:F2} km   ·   Tₑ = {5:F1} K reached at {6}",
            profile.Ppm, profile.SurfaceTemperature,
            profile.SurfaceTemperature - baseline.SurfaceTemperature, baseline.Ppm,
            profile.ConvectiveTopAltitude / 1000.0, profile.EmissionTemperature, emission);
    }

    private void FillGrid()
    {
        _grid.Columns.Clear();
        _grid.Rows.Clear();

        _grid.Columns.Add("ppm", "CO₂ (ppm)");
        foreach (var sweep in _sweeps)
        {
            _grid.Columns.Add("tau" + sweep.Label, "dry τ");
            _grid.Columns.Add("forcing" + sweep.Label, "F model (W/m²)");
            _grid.Columns.Add("accepted" + sweep.Label, "5.35 ln(C/C₀)");
            _grid.Columns.Add("model" + sweep.Label, "T_s (K)");

            // Only where the sweep kept its columns, which a synthetic sweep does not.
            if (sweep.Profiles.Count > 0)
            {
                _grid.Columns.Add("convective" + sweep.Label, "conv. top (km)");
                _grid.Columns.Add("emission" + sweep.Label, "Tₑ level (km)");
            }
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
                cells.Add(sweep.Forcings[i].ToString("F3", CultureInfo.InvariantCulture));
                cells.Add(sweep.AcceptedForcing(i).ToString("F3", CultureInfo.InvariantCulture));
                cells.Add(sweep.Points[i].SurfaceTemperature.ToString("F3", CultureInfo.InvariantCulture));

                if (sweep.Profiles.Count > i)
                {
                    var profile = sweep.Profiles[i];
                    cells.Add((profile.ConvectiveTopAltitude / 1000.0).ToString("F2", CultureInfo.InvariantCulture));
                    cells.Add(double.IsNaN(profile.EmissionAltitude)
                        ? "—"
                        : (profile.EmissionAltitude / 1000.0).ToString("F2", CultureInfo.InvariantCulture));
                }
            }
            _grid.Rows.Add(cells.ToArray());
        }

        if (_pinned < _grid.Rows.Count) _grid.Rows[_pinned].Selected = true;
    }

    /// <summary>
    /// Hovering the response chart previews that concentration in the profile. Leaving it
    /// returns to whatever was pinned, rather than to nothing.
    /// </summary>
    private void OnChartHover(int? index)
    {
        if (_sweeps.Length == 0) return;

        _profile.Selected = index ?? _pinned;

        if (index is null)
        {
            _statusLabel.Text = ProfileSummary(_pinned);
            _grid.ClearSelection();
            if (_pinned < _grid.Rows.Count) _grid.Rows[_pinned].Selected = true;
            return;
        }

        int i = index.Value;
        var parts = _sweeps.Select(s => string.Format(CultureInfo.InvariantCulture,
            "{0}: F = {1:F3} W/m² vs {2:F3} accepted (ratio {3:F2})  ·  T_s = {4:F3} K",
            s.Label, s.Forcings[i], s.AcceptedForcing(i),
            Math.Abs(s.AcceptedForcing(i)) > 1e-9 ? s.Forcings[i] / s.AcceptedForcing(i) : double.NaN,
            s.Points[i].SurfaceTemperature));

        _statusLabel.Text = string.Format(CultureInfo.InvariantCulture, "{0:N0} ppm — ",
            Co2Sweep.Concentrations[i]) + string.Join("   ·   ", parts);

        if (i < _grid.Rows.Count)
        {
            _grid.ClearSelection();
            _grid.Rows[i].Selected = true;
            _grid.FirstDisplayedScrollingRowIndex = i;
        }
    }

    /// <summary>Reads out one model level of the profile, across every configuration.</summary>
    private void OnProfileHover(int? level)
    {
        if (_sweeps.Length == 0 || _sweeps[0].Profiles.Count == 0) return;

        if (level is null)
        {
            _statusLabel.Text = ProfileSummary(_profile.Selected);
            return;
        }

        int selected = _profile.Selected;
        var parts = new List<string>();

        foreach (var sweep in _sweeps)
        {
            if (selected >= sweep.Profiles.Count) continue;

            var profile = sweep.Profiles[selected];
            if (level.Value >= profile.Levels.Count) continue;

            parts.Add(string.Format(CultureInfo.InvariantCulture, "{0}: {1:F3} K",
                profile.Label, profile.Levels[level.Value].Temperature));
        }

        var reference = _sweeps[0].Profiles[selected];
        if (level.Value >= reference.Levels.Count) return;

        var at = reference.Levels[level.Value];
        _statusLabel.Text = string.Format(CultureInfo.InvariantCulture,
            "{0:N0} ppm at {1:F2} km ({2:F1} hPa) — ", reference.Ppm,
            at.Altitude / 1000.0, at.Pressure / 100.0) + string.Join("   ·   ", parts);
    }

    private void ApplyTheme()
    {
        var theme = _dark ? ChartTheme.Dark : ChartTheme.Light;
        _chart.Theme = theme;
        _profile.Theme = theme;
        _chart.Invalidate();
        _profile.Invalidate();

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

    private void SaveChartPng()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = "co2-response.png",
            Title = "Save the response chart"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        Co2ChartExport.SavePng(dialog.FileName, _sweeps,
            _dark ? ChartTheme.Dark : ChartTheme.Light, _chart.Width, _chart.Height);

        _statusLabel.Text = $"Saved {dialog.FileName}";
    }

    private void SaveProfilePng()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = string.Format(CultureInfo.InvariantCulture, "profile-{0:F0}ppm.png",
                Co2Sweep.Concentrations[_profile.Selected]),
            Title = "Save the vertical profile"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ProfileExport.SavePng(dialog.FileName, _sweeps,
            _dark ? ChartTheme.Dark : ChartTheme.Light, _profile.Width, _profile.Height,
            _profile.Selected);

        _statusLabel.Text = $"Saved {dialog.FileName}";
    }
}
