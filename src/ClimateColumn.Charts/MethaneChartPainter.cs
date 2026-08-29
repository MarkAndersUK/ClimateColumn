using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Draws the methane forcing sweep against both candidate laws.
/// </summary>
/// <remarks>
/// A separate figure rather than the CO2 chart re-pointed, because it makes a different claim.
/// The CO2 chart compares one model curve against one accepted law; this one asks <em>which of
/// two laws</em> the model follows, so it must draw both and let the reader see which the points
/// sit on.
///
/// That is the whole interest of methane here. Its 7.7 um band is weak and largely unsaturated,
/// so the forcing should grow as sqrt(M) where CO2's saturated band gives ln(C). Nothing in the
/// model imposes either, so the figure is showing a prediction rather than a fit - and both
/// curves are drawn through the model's own endpoint, so neither is flattered by a free scale.
///
/// The mark specs and palette are shared with <see cref="Co2ChartPainter"/> so the two read as
/// one set: 2px lines with round joins, hairline gridlines, markers ringed in the surface
/// colour, direct labels on the line ends.
/// </remarks>
public static class MethaneChartPainter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const int MarginTop = 28, MarginRight = 170, MarginBottom = 62, MarginLeft = 74;
    private const int LegendHeight = 34;

    public static void Paint(Graphics g, Rectangle bounds, MethaneSweep sweep, ChartTheme theme)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var surface = new SolidBrush(theme.Surface);
        g.FillRectangle(surface, bounds);

        var ppb = MethaneSweep.Concentrations;
        int n = ppb.Length;

        var plot = new Rectangle(
            bounds.Left + MarginLeft,
            bounds.Top + MarginTop + LegendHeight,
            Math.Max(80, bounds.Width - MarginLeft - MarginRight),
            Math.Max(80, bounds.Height - MarginTop - MarginBottom - LegendHeight));

        // Both laws are scaled to pass through the model's own last point, so the figure
        // compares shape and nothing else. A law fitted with a free scale would otherwise be
        // judged partly on a magnitude the model does not claim to get right.
        double last = sweep.Forcings[^1];
        double c0 = ppb[0];
        double rootSpan = Math.Sqrt(ppb[^1]) - Math.Sqrt(c0);
        double logSpan = Math.Log(ppb[^1] / c0);

        double Root(int i) => rootSpan > 0 ? last * (Math.Sqrt(ppb[i]) - Math.Sqrt(c0)) / rootSpan : 0.0;
        double Log(int i) => logSpan > 0 ? last * Math.Log(ppb[i] / c0) / logSpan : 0.0;

        double hi = 0.0;
        for (int i = 0; i < n; i++)
            hi = Math.Max(hi, Math.Max(sweep.Forcings[i], Math.Max(Root(i), Log(i))));

        double step = Co2ChartQuantity.NiceStep(hi / 5.0);
        double yMax = Math.Ceiling((hi + 0.35 * step) / step) * step;

        float X(double c) => plot.Left + (float)((c - ppb[0]) / (ppb[^1] - ppb[0]) * plot.Width);
        float Y(double f) => plot.Bottom - (float)(f / yMax * plot.Height);

        using var tickFont = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        using var labelFont = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular);
        using var boldFont = new Font("Consolas", 8.75f, FontStyle.Bold, GraphicsUnit.Point);

        using var mutedBrush = new SolidBrush(theme.Muted);
        using var inkBrush = new SolidBrush(theme.Ink);
        using var secondaryBrush = new SolidBrush(theme.InkSecondary);

        DrawGrid(g, plot, yMax, step, ppb, X, Y, theme, tickFont, mutedBrush);
        DrawAxisTitles(g, bounds, plot, labelFont, secondaryBrush);
        DrawLegend(g, bounds, sweep, theme, labelFont, secondaryBrush);

        // The two laws first, so the model's own points sit on top of whichever it follows.
        DrawLaw(g, n, i => Log(i), i => ppb[i], X, Y, theme.Reference, new[] { 2f, 4f });
        DrawLaw(g, n, i => Root(i), i => ppb[i], X, Y, theme.Series[2], new[] { 6f, 3f });

        DrawModel(g, sweep, X, Y, theme);
        DrawEndLabels(g, plot, sweep, n, Root, Log, Y, theme, boldFont, tickFont, inkBrush, mutedBrush);
        DrawVerdict(g, plot, sweep, labelFont, mutedBrush);
    }

    private static void DrawGrid(Graphics g, Rectangle plot, double yMax, double step,
        double[] ppb, Func<double, float> X, Func<double, float> Y, ChartTheme theme,
        Font tickFont, Brush mutedBrush)
    {
        using var gridPen = new Pen(theme.Grid, 1f);
        using var axisPen = new Pen(theme.Axis, 1f);
        using var right = new StringFormat
        {
            Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center
        };
        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        for (double f = 0; f <= yMax + 1e-9; f += step)
        {
            float y = Y(f);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString(f.ToString("F1", Inv), tickFont, mutedBrush,
                new RectangleF(plot.Left - 60, y - 8, 52, 16), right);
        }

        // Every swept concentration gets a gridline and a label; there are ten and they are
        // unevenly spaced, so labelling only some would leave the eye guessing at the rest.
        //
        // Except where the present-day rule's own label would sit on a neighbour's. The clash is
        // measured rather than guessed at a pixel threshold: 1700 and 1900 are 54 px apart here,
        // which a 46 px rule lets through, and "1900 - today" is 90 px wide.
        const string presentLabel = "1900 — today";
        float presentX = X(MethaneSweep.PresentDayPpb);
        float presentHalf = g.MeasureString(presentLabel, tickFont).Width / 2f;

        for (int i = 0; i < ppb.Length; i++)
        {
            float x = X(ppb[i]);
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);

            if (Math.Abs(ppb[i] - MethaneSweep.PresentDayPpb) < 1e-9) continue;

            string text = ppb[i].ToString("F0", Inv);
            float half = g.MeasureString(text, tickFont).Width / 2f;
            if (Math.Abs(x - presentX) < half + presentHalf + 4f) continue;

            g.DrawString(text, tickFont, mutedBrush,
                new RectangleF(x - 30, plot.Bottom + 7, 60, 16), centre);
        }

        // Present-day methane, marked as the CO2 chart marks its own reference concentration.
        float px = X(MethaneSweep.PresentDayPpb);
        using (var rule = new Pen(theme.Axis, 1f) { DashPattern = new[] { 3f, 3f } })
        {
            g.DrawLine(rule, px, plot.Top, px, plot.Bottom);
        }
        g.DrawString(presentLabel, tickFont, mutedBrush,
            new RectangleF(px - 45, plot.Bottom + 7, 90, 16), centre);

        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
    }

    private static void DrawAxisTitles(Graphics g, Rectangle bounds, Rectangle plot,
        Font font, Brush brush)
    {
        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawString("Methane concentration (ppb)", font, brush,
            new RectangleF(plot.Left, bounds.Bottom - 26, plot.Width, 20), centre);

        var state = g.Save();
        g.TranslateTransform(bounds.Left + 18, plot.Top + plot.Height / 2f);
        g.RotateTransform(-90);
        g.DrawString("Radiative forcing (W m⁻²)", font, brush,
            new RectangleF(-plot.Height / 2f, -10, plot.Height, 20), centre);
        g.Restore(state);
    }

    private static void DrawLegend(Graphics g, Rectangle bounds, MethaneSweep sweep,
        ChartTheme theme, Font font, Brush brush)
    {
        float x = bounds.Left + MarginLeft;
        float y = bounds.Top + MarginTop - 6;

        var keys = new (string Text, Color Colour, float[]? Dash)[]
        {
            (sweep.Label, theme.Series[0], null),
            ("√M — the weak-band law", theme.Series[2], new[] { 6f, 3f }),
            ("ln M — CO₂'s saturated-band law", theme.Reference, new[] { 2f, 4f })
        };

        foreach (var (text, colour, dash) in keys)
        {
            var size = g.MeasureString(text, font);
            if (x + 26 + size.Width > bounds.Right - 12)
            {
                x = bounds.Left + MarginLeft;
                y += 16;
            }

            using var pen = new Pen(colour, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (dash is not null) pen.DashPattern = dash;

            g.DrawLine(pen, x, y + size.Height / 2, x + 20, y + size.Height / 2);
            g.DrawString(text, font, brush, x + 26, y);

            x += 26 + size.Width + 20;
        }
    }

    private static void DrawLaw(Graphics g, int n, Func<int, double> value, Func<int, double> at,
        Func<double, float> X, Func<double, float> Y, Color colour, float[] dash)
    {
        var points = new PointF[n];
        for (int i = 0; i < n; i++) points[i] = new PointF(X(at(i)), Y(value(i)));

        using var pen = new Pen(colour, 2f)
        {
            LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round,
            DashPattern = dash
        };
        g.DrawLines(pen, points);
    }

    private static void DrawModel(Graphics g, MethaneSweep sweep,
        Func<double, float> X, Func<double, float> Y, ChartTheme theme)
    {
        var ppb = MethaneSweep.Concentrations;
        var colour = theme.Series[0];

        var points = new PointF[ppb.Length];
        for (int i = 0; i < ppb.Length; i++) points[i] = new PointF(X(ppb[i]), Y(sweep.Forcings[i]));

        using (var pen = new Pen(colour, 2f)
               { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLines(pen, points);
        }

        // Every swept point marked, because the claim is about which curve the points lie on
        // rather than about the line between them.
        using var fill = new SolidBrush(colour);
        using var ring = new Pen(theme.Surface, 2f);
        foreach (var p in points)
        {
            g.FillEllipse(fill, p.X - 4f, p.Y - 4f, 8f, 8f);
            g.DrawEllipse(ring, p.X - 4f, p.Y - 4f, 8f, 8f);
        }
    }

    private static void DrawEndLabels(Graphics g, Rectangle plot, MethaneSweep sweep, int n,
        Func<int, double> root, Func<int, double> log, Func<double, float> Y, ChartTheme theme,
        Font boldFont, Font subFont, Brush inkBrush, Brush mutedBrush)
    {
        // All three meet at the last point by construction, so only the divergence in the middle
        // is worth labelling - and that is where the two laws are furthest apart.
        int mid = n / 2;

        var entries = new (double Value, string Note, Color Colour)[]
        {
            (sweep.Forcings[mid], "model", theme.Series[0]),
            (root(mid), "√M", theme.Series[2]),
            (log(mid), "ln M", theme.Reference)
        };

        var sorted = entries.OrderBy(e => Y(e.Value)).ToArray();
        var placed = new float[sorted.Length];
        for (int i = 0; i < sorted.Length; i++)
        {
            placed[i] = i == 0
                ? Y(sorted[i].Value)
                : Math.Max(Y(sorted[i].Value), placed[i - 1] + 26f);
        }

        for (int i = 0; i < sorted.Length; i++)
        {
            g.DrawString(sorted[i].Value.ToString("F2", Inv) + " W/m²", boldFont, inkBrush,
                plot.Right + 14, placed[i] - 9);
            g.DrawString(sorted[i].Note, subFont, mutedBrush, plot.Right + 14, placed[i] + 3);
        }
    }

    /// <summary>
    /// States what the fit actually shows, with the residuals behind it, so the figure carries
    /// its own evidence rather than asking to be taken on trust.
    /// </summary>
    /// <remarks>
    /// Worded as "closer to" rather than "follows", because the curve is visibly between the two
    /// laws rather than on either. Claiming it follows the square root would be reading the
    /// residual and not the figure - and the figure is right there beside the claim.
    /// </remarks>
    private static void DrawVerdict(Graphics g, Rectangle plot, MethaneSweep sweep,
        Font font, Brush mutedBrush)
    {
        var (root, log) = sweep.FitResiduals();

        string text = string.Format(Inv,
            "Closer to √M than to ln M — residual {0:F3} against {1:F3} — but between them: " +
            "above √M through the middle, and below ln M",
            root, log);

        g.DrawString(text, font, mutedBrush, plot.Left + 8, plot.Top + 6);
    }
}
