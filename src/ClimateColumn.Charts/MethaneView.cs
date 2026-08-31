using System.Drawing;
using System.Windows.Forms;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// The methane sweep and the law it follows, as a control.
/// </summary>
/// <remarks>
/// Like <see cref="ScenarioView"/> this has no hover readout: the figure names the winning law
/// and carries its own end labels, so there is nothing a pointer would reveal that is not
/// already on the page.
///
/// The sweep is not run until the view is looked at. It re-derives the bands at every methane
/// concentration - the approximation that made the response come out linear when it was scaled
/// instead - so it costs a band derivation per point and is far too slow to run on startup.
/// </remarks>
public sealed class MethaneView : Control
{
    private MethaneSweep? _sweep;
    private string _message = "Select this chart to run the methane sweep.";

    public MethaneView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Theme = ChartTheme.Light;
    }

    public ChartTheme Theme { get; set; }

    /// <summary>Whether the sweep has been run, so the form knows not to run it twice.</summary>
    public bool HasData => _sweep is not null;

    /// <summary>The sweep on show, so the form can export exactly what is drawn.</summary>
    public MethaneSweep? Sweep => _sweep;

    public void SetSweep(MethaneSweep? sweep, string message)
    {
        _sweep = sweep;
        _message = message;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_sweep is null)
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

        MethaneChartPainter.Paint(e.Graphics, ClientRectangle, _sweep, Theme);
    }
}
