using System.Drawing;
using System.Windows.Forms;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// The infrared absorption bands as a control.
/// </summary>
/// <remarks>
/// No hover readout: every panel is labelled and carries its own mean, and the axis is doubled
/// in wavenumber and wavelength, so a pointer would add nothing that is not already drawn.
/// </remarks>
public sealed class AbsorptionView : Control
{
    private IReadOnlyList<AbsorptionTrace> _traces = Array.Empty<AbsorptionTrace>();
    private double _wingCutoff = Co2Sweep.DefaultWingCutoff;
    private string _message = "Select this chart to compute the absorption bands.";

    public AbsorptionView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Theme = ChartTheme.Light;
    }

    public ChartTheme Theme { get; set; }

    /// <summary>Whether the spectra have been computed, so the form knows not to repeat it.</summary>
    public bool HasData => _traces.Count > 0;

    /// <summary>The traces on show, so the form can export exactly what is drawn.</summary>
    public IReadOnlyList<AbsorptionTrace> Traces => _traces;

    /// <summary>The cutoff the traces on show were computed at.</summary>
    public double WingCutoff => _wingCutoff;

    public void SetTraces(IReadOnlyList<AbsorptionTrace> traces, double wingCutoff, string message)
    {
        _traces = traces;
        _wingCutoff = wingCutoff;
        _message = message;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_traces.Count == 0)
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

        AbsorptionChartPainter.Paint(e.Graphics, ClientRectangle, _traces, _wingCutoff, Theme);
    }
}
