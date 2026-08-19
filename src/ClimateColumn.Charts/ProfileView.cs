using System.Drawing;
using System.Windows.Forms;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// The vertical profile as a control: repaints on resize, and tracks the nearest model level
/// under the pointer so the readout follows the mouse up and down the column.
/// </summary>
public sealed class ProfileView : Control
{
    private IReadOnlyList<Co2Sweep> _sweeps = Array.Empty<Co2Sweep>();
    private int? _hoverLevel;
    private int _selected;

    public ProfileView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Theme = ChartTheme.Light;
    }

    public ChartTheme Theme { get; set; }

    /// <summary>Which concentration's profile is drawn, as an index into the sweep.</summary>
    public int Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }

    /// <summary>Fired when the pointer moves to a different model level, or leaves.</summary>
    public event Action<int?>? HoverChanged;

    public void SetSweeps(IReadOnlyList<Co2Sweep> sweeps)
    {
        _sweeps = sweeps;
        _hoverLevel = null;
        Invalidate();
    }

    /// <summary>The profile currently drawn for the first sweep, or null when there is none.</summary>
    public ColumnProfile? Current =>
        _sweeps.Count > 0 && _selected >= 0 && _selected < _sweeps[0].Profiles.Count
            ? _sweeps[0].Profiles[_selected]
            : null;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Current is null)
        {
            using var back = new SolidBrush(Theme.Surface);
            using var ink = new SolidBrush(Theme.InkSecondary);
            using var centre = new StringFormat
            {
                Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center
            };
            e.Graphics.FillRectangle(back, ClientRectangle);
            e.Graphics.DrawString("No profile yet — the column is still running.",
                Font, ink, ClientRectangle, centre);
            return;
        }

        ProfilePainter.Paint(e.Graphics, ClientRectangle, _sweeps, Theme, _selected, _hoverLevel);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int? nearest = NearestLevel(e.Y);
        if (nearest == _hoverLevel) return;

        _hoverLevel = nearest;
        HoverChanged?.Invoke(_hoverLevel);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoverLevel is null) return;

        _hoverLevel = null;
        HoverChanged?.Invoke(null);
        Invalidate();
    }

    /// <summary>
    /// Nearest model level to the pointer's height. The whole plot row is the hit target, so
    /// the readout is driven by moving up and down rather than by finding a 9px dot.
    /// </summary>
    private int? NearestLevel(int mouseY)
    {
        var profile = Current;
        if (profile is null || profile.Levels.Count == 0) return null;

        // Mirrors the painter's plot rect so the mapping cannot drift between the two.
        const int marginTop = 28, marginBottom = 62, legendHeight = 34;
        int top = ClientRectangle.Top + marginTop + legendHeight;
        int height = Math.Max(80, ClientRectangle.Height - marginTop - marginBottom - legendHeight);

        if (mouseY < top - 12 || mouseY > top + height + 12) return null;

        double zMax = profile.ColumnTopAltitude;
        if (zMax <= 0.0) return null;

        double at = (top + height - mouseY) / (double)height * zMax;

        int best = 0;
        for (int i = 1; i < profile.Levels.Count; i++)
        {
            if (Math.Abs(profile.Levels[i].Altitude - at) <
                Math.Abs(profile.Levels[best].Altitude - at)) best = i;
        }
        return best;
    }
}
