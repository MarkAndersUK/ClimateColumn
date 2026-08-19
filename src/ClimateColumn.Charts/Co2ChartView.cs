using System.Drawing;
using System.Windows.Forms;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// The chart as a control: repaints on resize, and tracks the nearest swept concentration
/// under the pointer so the readout follows the mouse.
/// </summary>
public sealed class Co2ChartView : Control
{
    private IReadOnlyList<Co2Sweep> _sweeps = Array.Empty<Co2Sweep>();
    private int? _hoverIndex;

    public Co2ChartView()
    {
        // Double buffering matters here: the chart redraws wholesale on every mouse move.
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Theme = ChartTheme.Light;
    }

    public ChartTheme Theme { get; set; }

    private Co2ChartQuantity _quantity = Co2ChartQuantity.Forcing;

    /// <summary>
    /// Which quantity is plotted. Forcing by default: it is the comparison that borrows nothing
    /// from the model, since 5.35 ln(C/C0) is itself a statement about forcing.
    /// </summary>
    public Co2ChartQuantity Quantity
    {
        get => _quantity;
        set
        {
            if (ReferenceEquals(_quantity, value)) return;
            _quantity = value;
            Invalidate();
        }
    }

    /// <summary>Fired when the pointer moves to a different concentration, or leaves.</summary>
    public event Action<int?>? HoverChanged;

    /// <summary>
    /// Fired when a concentration is clicked. Hovering previews a concentration; clicking pins
    /// it, so the profile beside the chart can be studied with the pointer somewhere else.
    /// </summary>
    public event Action<int>? Picked;

    public void SetSweeps(IReadOnlyList<Co2Sweep> sweeps)
    {
        _sweeps = sweeps;
        _hoverIndex = null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_sweeps.Count == 0)
        {
            using var back = new SolidBrush(Theme.Surface);
            using var ink = new SolidBrush(Theme.InkSecondary);
            using var centre = new StringFormat
            {
                Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center
            };
            e.Graphics.FillRectangle(back, ClientRectangle);
            e.Graphics.DrawString("Running the column to equilibrium at each concentration…",
                Font, ink, ClientRectangle, centre);
            return;
        }

        Co2ChartPainter.Paint(e.Graphics, ClientRectangle, _sweeps, Theme, _hoverIndex, _quantity);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_sweeps.Count == 0) return;

        int? nearest = NearestIndex(e.X);
        if (nearest == _hoverIndex) return;

        _hoverIndex = nearest;
        HoverChanged?.Invoke(_hoverIndex);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_sweeps.Count == 0 || e.Button != MouseButtons.Left) return;

        if (NearestIndex(e.X) is int index) Picked?.Invoke(index);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverIndex is null) return;

        _hoverIndex = null;
        HoverChanged?.Invoke(null);
        Invalidate();
    }

    /// <summary>
    /// Nearest swept concentration to the pointer. The whole plot column is the hit target
    /// rather than each 9px dot, so the readout is easy to drive.
    /// </summary>
    private int? NearestIndex(int mouseX)
    {
        // Mirrors the painter's plot rect so the mapping cannot drift between the two.
        const int marginLeft = 74, marginRight = 150;
        int width = Math.Max(80, ClientRectangle.Width - marginLeft - marginRight);
        int left = ClientRectangle.Left + marginLeft;

        if (mouseX < left - 12 || mouseX > left + width + 12) return null;

        double[] ppm = Co2Sweep.Concentrations;
        double at = ppm[0] + (mouseX - left) / (double)width * (ppm[^1] - ppm[0]);

        int best = 0;
        for (int i = 1; i < ppm.Length; i++)
        {
            if (Math.Abs(ppm[i] - at) < Math.Abs(ppm[best] - at)) best = i;
        }
        return best;
    }
}
