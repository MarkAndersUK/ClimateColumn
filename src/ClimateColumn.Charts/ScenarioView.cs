using System.Drawing;
using System.Windows.Forms;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// The coupled CO2 and methane scenario as a control.
/// </summary>
/// <remarks>
/// No hover readout, unlike the other two views. The figure's own end labels carry the numbers
/// that matter and the axis labels both concentrations at every tick, so there is nothing a
/// pointer would reveal that is not already on the page.
/// </remarks>
public sealed class ScenarioView : Control
{
    private IReadOnlyList<ScenarioPoint> _points = Array.Empty<ScenarioPoint>();
    private string _message = "Select this tab to run the scenario.";

    public ScenarioView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Theme = ChartTheme.Light;
    }

    public ChartTheme Theme { get; set; }

    /// <summary>Whether the scenario has been run, so the form knows not to run it twice.</summary>
    public bool HasData => _points.Count > 0;

    /// <summary>The points on show, so the form can export exactly what is drawn.</summary>
    public IReadOnlyList<ScenarioPoint> Points => _points;

    public void SetPoints(IReadOnlyList<ScenarioPoint> points, string message)
    {
        _points = points;
        _message = message;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_points.Count == 0)
        {
            using var back = new SolidBrush(Theme.Surface);
            using var ink = new SolidBrush(Theme.InkSecondary);
            using var centre = new StringFormat
            {
                Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center
            };
            e.Graphics.FillRectangle(back, ClientRectangle);
            e.Graphics.DrawString(_message, Font, ink, ClientRectangle, centre);
            return;
        }

        ScenarioChartPainter.Paint(e.Graphics, ClientRectangle, _points,
            ScenarioSweep.CouplingNote, Theme);
    }
}
