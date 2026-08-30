using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Draws the surface warming of a coupled CO2 and methane scenario, with methane's share of it
/// shown as the gap between two curves.
/// </summary>
/// <remarks>
/// The decomposition is the reason this is a separate figure rather than another quantity on the
/// CO2 chart. Two curves - the pair rising together, and CO2 alone over the same range - make
/// methane's contribution the area between them, which is legible at a glance in a way that a
/// third line or a stacked bar is not.
///
/// <strong>The coupling is an assumption, not a result, and the figure says so.</strong> The
/// methane value paired with each CO2 level comes from a stated trend, and the subtitle carries
/// it, because a reader who takes this for a projection would be reading it wrong. Every point
/// beyond present-day CO2 is an extrapolation of a current rate over centuries.
/// </remarks>
public static class ScenarioChartPainter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const int MarginTop = 28, MarginRight = 158, MarginBottom = 74, MarginLeft = 74;
    private const int LegendHeight = 50;

    public static void Paint(Graphics g, Rectangle bounds, IReadOnlyList<ScenarioPoint> points,
        string couplingNote, ChartTheme theme)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var surface = new SolidBrush(theme.Surface);
        g.FillRectangle(surface, bounds);
        if (points.Count < 2) return;

        var plot = new Rectangle(
            bounds.Left + MarginLeft,
            bounds.Top + MarginTop + LegendHeight,
            Math.Max(80, bounds.Width - MarginLeft - MarginRight),
            Math.Max(80, bounds.Height - MarginTop - MarginBottom - LegendHeight));

        double xMin = points[0].Ppm, xMax = points[^1].Ppm;
        double hi = points.Max(p => p.WarmingBoth);
        double step = Co2ChartQuantity.NiceStep(hi / 5.0);
        double yMax = Math.Ceiling((hi + 0.35 * step) / step) * step;

        float X(double c) => plot.Left + (float)((c - xMin) / (xMax - xMin) * plot.Width);
        float Y(double t) => plot.Bottom - (float)(t / yMax * plot.Height);

        using var tickFont = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        using var labelFont = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular);
        using var boldFont = new Font("Consolas", 8.75f, FontStyle.Bold, GraphicsUnit.Point);

        using var mutedBrush = new SolidBrush(theme.Muted);
        using var inkBrush = new SolidBrush(theme.Ink);
        using var secondaryBrush = new SolidBrush(theme.InkSecondary);

        DrawGrid(g, plot, yMax, step, points, X, Y, theme, tickFont, mutedBrush);
        DrawMethaneBand(g, points, X, Y, theme);
        DrawCurves(g, points, X, Y, theme);
        DrawLegend(g, bounds, couplingNote, theme, labelFont, secondaryBrush, mutedBrush);
        DrawEndLabels(g, plot, points, Y, theme, boldFont, tickFont, inkBrush, mutedBrush);
        DrawAxisTitles(g, bounds, plot, labelFont, secondaryBrush);
    }

    private static void DrawGrid(Graphics g, Rectangle plot, double yMax, double step,
        IReadOnlyList<ScenarioPoint> points, Func<double, float> X, Func<double, float> Y,
        ChartTheme theme, Font tickFont, Brush mutedBrush)
    {
        using var gridPen = new Pen(theme.Grid, 1f);
        using var axisPen = new Pen(theme.Axis, 1f);
        using var right = new StringFormat
        { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        for (double t = 0; t <= yMax + 1e-9; t += step)
        {
            float y = Y(t);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString(t.ToString("F1", Inv), tickFont, mutedBrush,
                new RectangleF(plot.Left - 60, y - 8, 52, 16), right);
        }

        // Both axes labelled on the same tick: the scenario pairs them, so a reader should never
        // have to work out which methane value a CO2 level came with.
        float lastX = float.MinValue;
        foreach (var p in points)
        {
            float x = X(p.Ppm);
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);

            if (x - lastX < 54f) continue;
            lastX = x;

            g.DrawString(p.Ppm.ToString("F0", Inv), tickFont, mutedBrush,
                new RectangleF(x - 30, plot.Bottom + 7, 60, 16), centre);
            g.DrawString(p.Ppb.ToString("F0", Inv), tickFont, mutedBrush,
                new RectangleF(x - 30, plot.Bottom + 22, 60, 16), centre);
        }

        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
    }

    /// <summary>Methane's contribution: the area between the coupled curve and CO2 alone.</summary>
    private static void DrawMethaneBand(Graphics g, IReadOnlyList<ScenarioPoint> points,
        Func<double, float> X, Func<double, float> Y, ChartTheme theme)
    {
        var path = new PointF[points.Count * 2];
        for (int i = 0; i < points.Count; i++)
            path[i] = new PointF(X(points[i].Ppm), Y(points[i].WarmingBoth));
        for (int i = 0; i < points.Count; i++)
            path[points.Count + i] = new PointF(
                X(points[^(i + 1)].Ppm), Y(points[^(i + 1)].WarmingCo2Only));

        using var fill = new SolidBrush(Color.FromArgb(46, theme.Series[1]));
        g.FillPolygon(fill, path);
    }

    private static void DrawCurves(Graphics g, IReadOnlyList<ScenarioPoint> points,
        Func<double, float> X, Func<double, float> Y, ChartTheme theme)
    {
        void Line(Func<ScenarioPoint, double> value, Color colour, float[]? dash)
        {
            var pts = points.Select(p => new PointF(X(p.Ppm), Y(value(p)))).ToArray();
            using var pen = new Pen(colour, 2f)
            { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (dash is not null) pen.DashPattern = dash;
            g.DrawLines(pen, pts);

            using var fillDot = new SolidBrush(colour);
            using var ring = new Pen(theme.Surface, 2f);
            foreach (var p in pts)
            {
                g.FillEllipse(fillDot, p.X - 4f, p.Y - 4f, 8f, 8f);
                g.DrawEllipse(ring, p.X - 4f, p.Y - 4f, 8f, 8f);
            }
        }

        Line(p => p.WarmingCo2Only, theme.Series[1], new[] { 5f, 3f });
        Line(p => p.WarmingBoth, theme.Series[0], null);
    }

    private static void DrawLegend(Graphics g, Rectangle bounds, string couplingNote,
        ChartTheme theme, Font font, Brush brush, Brush mutedBrush)
    {
        float x = bounds.Left + MarginLeft;
        float y = bounds.Top + MarginTop - 8;

        foreach (var (text, colour, dash) in new (string, Color, float[]?)[]
        {
            ("CO₂ and methane rising together", theme.Series[0], null),
            ("CO₂ alone — the gap is methane's share", theme.Series[1], new[] { 5f, 3f })
        })
        {
            var size = g.MeasureString(text, font);
            using var pen = new Pen(colour, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (dash is not null) pen.DashPattern = dash;

            g.DrawLine(pen, x, y + size.Height / 2, x + 20, y + size.Height / 2);
            g.DrawString(text, font, brush, x + 26, y);
            x += 26 + size.Width + 22;
        }

        // The coupling assumption travels with the figure, not in a caption someone can lose.
        g.DrawString(couplingNote, font, mutedBrush, bounds.Left + MarginLeft, y + 20);
    }

    private static void DrawEndLabels(Graphics g, Rectangle plot,
        IReadOnlyList<ScenarioPoint> points, Func<double, float> Y, ChartTheme theme,
        Font boldFont, Font subFont, Brush inkBrush, Brush mutedBrush)
    {
        var last = points[^1];

        var entries = new (double Value, string Note, Color Colour)[]
        {
            (last.WarmingBoth, "both", theme.Series[0]),
            (last.WarmingCo2Only, "CO₂ alone", theme.Series[1])
        };

        var sorted = entries.OrderBy(e => Y(e.Value)).ToArray();
        var placed = new float[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
            placed[i] = i == 0 ? Y(sorted[i].Value) : Math.Max(Y(sorted[i].Value), placed[i - 1] + 28f);

        for (int i = 0; i < sorted.Length; i++)
        {
            g.DrawString("+" + sorted[i].Value.ToString("F2", Inv) + " K", boldFont, inkBrush,
                plot.Right + 14, placed[i] - 9);
            g.DrawString(sorted[i].Note, subFont, mutedBrush, plot.Right + 14, placed[i] + 3);
        }

        // Two short lines rather than one long one: at this margin the single-line form ran
        // off the right edge and lost its own percentage.
        double share = last.WarmingBoth - last.WarmingCo2Only;
        g.DrawString(string.Format(Inv, "methane", share), subFont, mutedBrush,
            plot.Right + 14, placed[^1] + 24);
        g.DrawString(string.Format(Inv, "+{0:F2} K  {1:P0}",
                share, last.WarmingBoth > 0 ? share / last.WarmingBoth : 0.0),
            subFont, mutedBrush, plot.Right + 14, placed[^1] + 36);
    }

    private static void DrawAxisTitles(Graphics g, Rectangle bounds, Rectangle plot,
        Font font, Brush brush)
    {
        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawString("CO₂ (ppm)  ·  methane (ppb)", font, brush,
            new RectangleF(plot.Left, bounds.Bottom - 24, plot.Width, 20), centre);

        var state = g.Save();
        g.TranslateTransform(bounds.Left + 18, plot.Top + plot.Height / 2f);
        g.RotateTransform(-90);
        g.DrawString("Surface warming from the base state (K)", font, brush,
            new RectangleF(-plot.Height / 2f, -10, plot.Height, 20), centre);
        g.Restore(state);
    }
}
