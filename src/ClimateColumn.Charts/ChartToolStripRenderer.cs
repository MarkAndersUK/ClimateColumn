using System.Drawing;
using System.Windows.Forms;

namespace ClimateColumn.Charts;

/// <summary>
/// Paints a <see cref="ToolStrip"/> or <see cref="StatusStrip"/> in the chart's own palette.
/// </summary>
/// <remarks>
/// Needed because a ToolStrip does not take its colours from the form it sits on. It paints
/// itself through a renderer that carries its own colour table, and the default table is built
/// from the Windows system colours - which are light whatever the chart is doing.
///
/// The result was worse than an unthemed strip. Setting <c>ForeColor</c> on the form <em>does</em>
/// reach the strip's items, so in dark mode the labels turned white while the strip behind them
/// stayed light grey: white text on near-white, and the menus could not be read at all. Either
/// theming both or neither would have been legible; theming exactly one was the failure.
/// </remarks>
public sealed class ChartToolStripRenderer : ToolStripProfessionalRenderer
{
    private readonly ChartTheme _theme;

    public ChartToolStripRenderer(ChartTheme theme)
        : base(new ChartColorTable(theme))
    {
        _theme = theme;

        // The professional renderer draws a raised border and item borders that read as chrome
        // from another application when the surrounding page is flat.
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var back = new SolidBrush(_theme.Plane);
        e.Graphics.FillRectangle(back, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        // A single hairline against the figures below, in place of the default bevel.
        using var pen = new Pen(_theme.Grid, 1f);
        var b = e.AffectedBounds;

        if (e.ToolStrip is StatusStrip) e.Graphics.DrawLine(pen, b.Left, b.Top, b.Right, b.Top);
        else e.Graphics.DrawLine(pen, b.Left, b.Bottom - 1, b.Right, b.Bottom - 1);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Disabled items keep a visible but recessive colour rather than the system's grey,
        // which vanishes on a dark strip. Save is disabled until the sweep finishes, so this is
        // the state the window opens in.
        e.TextColor = e.Item.Enabled ? _theme.Ink : _theme.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_theme.Grid, 1f);
        var b = e.Item.Bounds;

        if (e.Vertical) e.Graphics.DrawLine(pen, b.Width / 2, 4, b.Width / 2, b.Height - 4);
        else e.Graphics.DrawLine(pen, 4, b.Height / 2, b.Width - 4, b.Height / 2);
    }

    /// <summary>The hover, pressed and border colours the professional renderer asks for.</summary>
    private sealed class ChartColorTable : ProfessionalColorTable
    {
        private readonly ChartTheme _theme;

        public ChartColorTable(ChartTheme theme)
        {
            _theme = theme;
            UseSystemColors = false;
        }

        public override Color ToolStripGradientBegin => _theme.Plane;
        public override Color ToolStripGradientMiddle => _theme.Plane;
        public override Color ToolStripGradientEnd => _theme.Plane;
        public override Color ToolStripBorder => _theme.Grid;
        public override Color ToolStripContentPanelGradientBegin => _theme.Plane;
        public override Color ToolStripContentPanelGradientEnd => _theme.Plane;
        public override Color ToolStripPanelGradientBegin => _theme.Plane;
        public override Color ToolStripPanelGradientEnd => _theme.Plane;

        public override Color StatusStripGradientBegin => _theme.Plane;
        public override Color StatusStripGradientEnd => _theme.Plane;

        // Hover and pressed states use the grid colour, which is the one step away from the
        // plane that both palettes already define - light enough to see on the dark strip and
        // dark enough to see on the light one.
        public override Color ButtonSelectedHighlight => _theme.Grid;
        public override Color ButtonSelectedGradientBegin => _theme.Grid;
        public override Color ButtonSelectedGradientMiddle => _theme.Grid;
        public override Color ButtonSelectedGradientEnd => _theme.Grid;
        public override Color ButtonSelectedBorder => _theme.Axis;

        public override Color ButtonPressedHighlight => _theme.Axis;
        public override Color ButtonPressedGradientBegin => _theme.Axis;
        public override Color ButtonPressedGradientMiddle => _theme.Axis;
        public override Color ButtonPressedGradientEnd => _theme.Axis;
        public override Color ButtonPressedBorder => _theme.Axis;

        public override Color ButtonCheckedGradientBegin => _theme.Grid;
        public override Color ButtonCheckedGradientMiddle => _theme.Grid;
        public override Color ButtonCheckedGradientEnd => _theme.Grid;

        public override Color SeparatorDark => _theme.Grid;
        public override Color SeparatorLight => _theme.Grid;

        public override Color MenuItemSelected => _theme.Grid;
        public override Color MenuItemBorder => _theme.Axis;
        public override Color MenuBorder => _theme.Axis;
        public override Color MenuStripGradientBegin => _theme.Plane;
        public override Color MenuStripGradientEnd => _theme.Plane;
        public override Color ImageMarginGradientBegin => _theme.Plane;
        public override Color ImageMarginGradientMiddle => _theme.Plane;
        public override Color ImageMarginGradientEnd => _theme.Plane;
    }
}
