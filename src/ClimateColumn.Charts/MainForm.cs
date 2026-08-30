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
    private readonly ScenarioView _scenario = new() { Dock = DockStyle.Fill };
    private readonly TabControl _charts = new() { Dock = DockStyle.Fill };
    private readonly ProfileView _profile = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new();
    private readonly ToolStrip _toolbar = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripButton _saveChartButton = new();
    private readonly ToolStripButton _saveProfileButton = new();
    private readonly ToolStripButton _themeButton = new();
    private readonly ToolStripButton _quantityButton = new();
    private readonly ToolStripButton _gridButton = new();
    private readonly ToolStripButton _cloudButton = new();
    private readonly ToolStripComboBox _concentration = new();
    private readonly SplitContainer _outer = new();
    private readonly SplitContainer _figures = new();

    private Co2Sweep[] _sweeps = Array.Empty<Co2Sweep>();
    private bool _dark;
    private bool _clouds;

    /// <summary>Set while a march is running, so a second one cannot start on top of it.</summary>
    private bool _busy;

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
        //
        // The minimum sizes are deliberately NOT set here - see LayoutPanels.
        _figures.Dock = DockStyle.Fill;
        _figures.Orientation = Orientation.Vertical;
        var responseTab = new TabPage("CO₂ response");
        responseTab.Controls.Add(_chart);
        var scenarioTab = new TabPage("Both gases");
        scenarioTab.Controls.Add(_scenario);
        _charts.TabPages.Add(responseTab);
        _charts.TabPages.Add(scenarioTab);

        // The scenario costs twenty equilibrium marches, so it is not run until it is looked at.
        _charts.SelectedIndexChanged += async (_, _) =>
        {
            if (_charts.SelectedIndex == 1 && !_scenario.HasData && !_busy) await RunScenarioAsync();
        };

        _figures.Panel1.Controls.Add(_charts);
        _figures.Panel2.Controls.Add(_profile);
        _figures.FixedPanel = FixedPanel.Panel2;

        _outer.Dock = DockStyle.Fill;
        _outer.Orientation = Orientation.Horizontal;
        _outer.Panel1.Controls.Add(_figures);
        _outer.Panel2.Controls.Add(BuildGrid());

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

        Shown += (_, _) => LayoutPanels();
        Load += async (_, _) => await RunSweepsAsync();
    }

    /// <summary>Panel minimums and splitter positions, once the form has its real size.</summary>
    private void LayoutPanels()
    {
        SetSplit(_outer, panel1Min: 340, panel2Min: 90, fraction: 0.70);
        SetSplit(_figures, panel1Min: 380, panel2Min: 320, fraction: 0.58);
    }

    /// <summary>
    /// Gives a splitter its panel minimums and puts it at a fraction of its container.
    /// </summary>
    /// <remarks>
    /// Both parts have to happen here rather than in the constructor, and the reason is not
    /// obvious. Assigning <see cref="SplitContainer.Panel2MinSize"/> makes the control move its
    /// splitter to satisfy the new minimum - and a container that has not been laid out yet is
    /// still 150 px wide, where no position can satisfy a 380 px and a 320 px minimum at once.
    /// The setter throws from inside itself, naming a property nobody assigned, so the window
    /// simply never opened.
    ///
    /// Waiting for Shown means the sizes are real. The guard covers the case where they are
    /// nonetheless too small to divide, which the form's MinimumSize should prevent but which
    /// must not be a crash if it ever stops doing so.
    /// </remarks>
    private static void SetSplit(SplitContainer split, int panel1Min, int panel2Min, double fraction)
    {
        int extent = split.Orientation == Orientation.Horizontal ? split.Height : split.Width;
        if (extent < panel1Min + panel2Min + split.SplitterWidth) return;

        split.Panel1MinSize = panel1Min;
        split.Panel2MinSize = panel2Min;
        split.SplitterDistance = Math.Clamp(
            (int)(extent * fraction), panel1Min, extent - panel2Min - split.SplitterWidth);
    }

    private ToolStrip BuildToolbar()
    {
        _saveChartButton.Text = "Save chart…";
        _saveChartButton.Enabled = false;
        _saveChartButton.Click += (_, _) =>
        {
            if (_charts.SelectedIndex == 1) SaveScenarioPng(); else SaveChartPng();
        };

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
        // forcings borrows nothing from the model. Neither temperature view carries a reference
        // curve, for the same reason - drawing one would need the model's own sensitivity.
        //
        // The button names the view it will move to rather than the one showing, so a reader
        // never has to work out which of three states they are in.
        _quantityButton.Text = "Show " + _chart.Quantity.Next.Name.ToLowerInvariant();
        _quantityButton.Click += (_, _) =>
        {
            _chart.Quantity = _chart.Quantity.Next;
            _quantityButton.Text = "Show " + _chart.Quantity.Next.Name.ToLowerInvariant();
        };

        // Switching clouds re-runs the whole sweep, because a cloud deck changes the atmosphere
        // rather than the drawing. Both configurations are calibrated to the same 286.796 K base
        // state, so what moves between them is the deck's doing and not a 15 K jump in where the
        // column started.
        _cloudButton.Text = "Clouds: off";
        _cloudButton.Click += async (_, _) =>
        {
            _clouds = !_clouds;
            _cloudButton.Text = _clouds ? "Clouds: on" : "Clouds: off";
            await RunSweepsAsync();
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

        var toolbar = _toolbar;
        toolbar.Dock = DockStyle.Top;
        toolbar.GripStyle = ToolStripGripStyle.Hidden;
        toolbar.Items.Add(_saveChartButton);
        toolbar.Items.Add(_saveProfileButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_quantityButton);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(new ToolStripLabel("Profile at"));
        toolbar.Items.Add(_concentration);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(_cloudButton);
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
        bool clouds = _clouds;

        // A sweep takes about a minute, and the cloud toggle can start another one while the
        // first is still going. Disabling the controls that would do so is simpler than trying
        // to cancel a march mid-flight.
        _busy = true;
        _cloudButton.Enabled = false;
        _saveChartButton.Enabled = false;
        _saveProfileButton.Enabled = false;
        _statusLabel.Text = clouds
            ? "Running the column to equilibrium at each concentration, under cloud…"
            : "Running the column to equilibrium at each concentration…";

        // A minute of nothing moving is indistinguishable from a hang. UseWaitCursor rather
        // than Cursor.Current because it propagates to every child control - the chart, the
        // profile and the grid each carry their own cursor otherwise, so setting only the
        // form's would leave the pointer normal over exactly the area being looked at.
        UseWaitCursor = true;

        Co2Sweep[] sweeps;
        try
        {
            sweeps = await Task.Run(() => Co2Sweep.ForChart(clouds));
        }
        finally
        {
            // In a finally so a failed sweep cannot strand the window with a busy pointer and
            // a dead toggle.
            UseWaitCursor = false;
            _busy = false;
            _cloudButton.Enabled = true;
        }

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

    /// <summary>
    /// Runs the coupled scenario, once, the first time its tab is looked at.
    /// </summary>
    /// <remarks>
    /// Lazily rather than alongside the response sweep because it costs twice as much - two
    /// equilibria at every concentration, one with both gases rising and one with CO2 alone -
    /// and a reader who never opens the tab should not pay for it. The result never changes, so
    /// once it is in hand the tab is free thereafter.
    /// </remarks>
    private async Task RunScenarioAsync()
    {
        _busy = true;
        _cloudButton.Enabled = false;
        _saveChartButton.Enabled = false;
        _scenario.SetPoints(Array.Empty<ScenarioPoint>(), "Running both gases together…");
        _statusLabel.Text = "Running the column at each concentration, with methane rising too…";
        UseWaitCursor = true;

        IReadOnlyList<ScenarioPoint> points;
        try
        {
            points = await Task.Run(ScenarioSweep.Run);
        }
        finally
        {
            UseWaitCursor = false;
            _busy = false;
            _cloudButton.Enabled = true;
        }

        _scenario.SetPoints(points,
            "No HITRAN data — run scripts/fetch-hitran.ps1 -Molecule all.");
        _saveChartButton.Enabled = _sweeps.Length > 0 || points.Count > 0;
        _statusLabel.Text = points.Count > 0 ? ScenarioSummary(points) : Summary();
    }

    /// <summary>What the scenario says at its far end, and how much of it methane is.</summary>
    private static string ScenarioSummary(IReadOnlyList<ScenarioPoint> points)
    {
        var last = points[^1];
        double share = last.WarmingBoth - last.WarmingCo2Only;

        return string.Format(CultureInfo.InvariantCulture,
            "{0:N0} ppm with {1:N0} ppb — +{2:F2} K, of which methane is {3:F2} K ({4:P0})   ·   {5}",
            last.Ppm, last.Ppb, last.WarmingBoth, share,
            last.WarmingBoth > 0 ? share / last.WarmingBoth : 0.0, ScenarioSweep.CouplingNote);
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
        _scenario.Theme = theme;
        _profile.Theme = theme;
        _charts.BackColor = theme.Plane;
        _charts.ForeColor = theme.Ink;
        _scenario.Invalidate();
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

        // A ToolStrip paints through a renderer carrying its own colour table, so it does not
        // follow the form. Setting ForeColor on the form DOES reach its items, which is what
        // made dark mode unreadable rather than merely unthemed: white labels on the renderer's
        // system-light strip.
        _toolbar.Renderer = new ChartToolStripRenderer(theme);
        _status.Renderer = new ChartToolStripRenderer(theme);

        _toolbar.BackColor = theme.Plane;
        _toolbar.ForeColor = theme.Ink;
        _status.BackColor = theme.Plane;
        _statusLabel.ForeColor = theme.InkSecondary;

        // The combo box is a hosted Win32 control, not a rendered ToolStrip item, so the
        // renderer never touches it and it needs its colours set directly.
        _concentration.BackColor = theme.Surface;
        _concentration.ForeColor = theme.Ink;
        _concentration.FlatStyle = FlatStyle.Flat;

        foreach (var item in _toolbar.Items.OfType<ToolStripItem>())
        {
            item.ForeColor = theme.Ink;
        }
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

    private void SaveScenarioPng()
    {
        if (!_scenario.HasData) return;

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = "co2-and-methane.png",
            Title = "Save the coupled scenario"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        ScenarioChartExport.SavePng(dialog.FileName, _scenario.Points,
            _dark ? ChartTheme.Dark : ChartTheme.Light, _scenario.Width, _scenario.Height);

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
