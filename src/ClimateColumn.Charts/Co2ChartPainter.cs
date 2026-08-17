using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Draws a CO2 concentration sweep with GDI+. One entry point serves both the on-screen
/// control and the PNG export, so what you save is exactly what you saw.
/// </summary>
/// <remarks>
/// Mark specs match the HTML renderer: 2px lines with round joins, dashing for the
/// logarithmic expectation, solid hairline gridlines, 9px end markers ringed in the surface
/// colour, and direct labels on the four line ends only. Hue carries the configuration and
/// dashing carries model-versus-expectation, so two categorical colours cover four lines.
/// </remarks>
public static class Co2ChartPainter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const int MarginTop = 28, MarginRight = 150, MarginBottom = 62, MarginLeft = 74;
    private const int LegendHeight = 34;

    public static void Paint(Graphics g, Rectangle bounds, IReadOnlyList<Co2Sweep> sweeps,
        ChartTheme theme, int? hoverIndex)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var surface = new SolidBrush(theme.Surface);
        g.FillRectangle(surface, bounds);

        if (sweeps.Count == 0) return;

        var plot = new Rectangle(
            bounds.Left + MarginLeft,
            bounds.Top + MarginTop + LegendHeight,
            Math.Max(80, bounds.Width - MarginLeft - MarginRight),
            Math.Max(80, bounds.Height - MarginTop - MarginBottom - LegendHeight));

        double[] ppm = Co2Sweep.Concentrations;
        double xMin = ppm[0], xMax = ppm[^1];

        // Round the range outward to whole kelvin so ticks land on clean numbers.
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var s in sweeps)
        {
            for (int i = 0; i < s.Points.Count; i++)
            {
                lo = Math.Min(lo, Math.Min(s.Points[i].SurfaceTemperature, s.Expected(i)));
                hi = Math.Max(hi, Math.Max(s.Points[i].SurfaceTemperature, s.Expected(i)));
            }
        }
        double yMin = Math.Floor(lo) - 1, yMax = Math.Ceiling(hi) + 1;

        float X(double c) => plot.Left + (float)((c - xMin) / (xMax - xMin) * plot.Width);
        float Y(double t) => plot.Top + (float)((yMax - t) / (yMax - yMin) * plot.Height);

        using var tickFont = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        using var labelFont = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular);
        using var boldFont = new Font("Consolas", 8.75f, FontStyle.Bold, GraphicsUnit.Point);
        using var titleFont = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular);

        using var mutedBrush = new SolidBrush(theme.Muted);
        using var inkBrush = new SolidBrush(theme.Ink);
        using var secondaryBrush = new SolidBrush(theme.InkSecondary);

        DrawGrid(g, plot, yMin, yMax, Y, theme, tickFont, mutedBrush);
        DrawXAxis(g, plot, ppm, X, theme, tickFont, mutedBrush);
        DrawAxisTitles(g, bounds, plot, titleFont, secondaryBrush);
        DrawLegend(g, bounds, sweeps, theme, labelFont, secondaryBrush);
        DrawSeries(g, sweeps, X, Y, theme);
        DrawEndLabels(g, plot, sweeps, Y, theme, boldFont, tickFont, inkBrush, mutedBrush);

        if (hoverIndex is int idx && idx >= 0 && idx < ppm.Length)
        {
            DrawHover(g, plot, sweeps, idx, X, Y, theme, labelFont, boldFont);
        }
    }

    private static void DrawGrid(Graphics g, Rectangle plot, double yMin, double yMax,
        Func<double, float> Y, ChartTheme theme, Font tickFont, Brush mutedBrush)
    {
        using var gridPen = new Pen(theme.Grid, 1f);
        int step = (int)Math.Max(1, Math.Round((yMax - yMin) / 6.0));

        using var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        for (double t = yMin; t <= yMax + 1e-9; t += step)
        {
            float y = Y(t);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString(t.ToString("F0", Inv), tickFont, mutedBrush,
                new RectangleF(plot.Left - 60, y - 8, 52, 16), right);
        }
    }

    private static void DrawXAxis(Graphics g, Rectangle plot, double[] ppm,
        Func<double, float> X, ChartTheme theme, Font tickFont, Brush mutedBrush)
    {
        using var axisPen = new Pen(theme.Axis, 1f);
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        // Label every swept concentration where there is room, else every other one.
        int stride = plot.Width / Math.Max(1, ppm.Length) < 46 ? 2 : 1;
        for (int i = 0; i < ppm.Length; i += stride)
        {
            g.DrawString(ppm[i].ToString("F0", Inv), tickFont, mutedBrush,
                new RectangleF(X(ppm[i]) - 30, plot.Bottom + 7, 60, 16), centre);
        }
    }

    private static void DrawAxisTitles(Graphics g, Rectangle bounds, Rectangle plot,
        Font font, Brush brush)
    {
        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawString("CO₂ concentration (ppm)", font, brush,
            new RectangleF(plot.Left, bounds.Bottom - 26, plot.Width, 20), centre);

        var state = g.Save();
        g.TranslateTransform(bounds.Left + 18, plot.Top + plot.Height / 2f);
        g.RotateTransform(-90);
        g.DrawString("Surface temperature (K)", font, brush,
            new RectangleF(-plot.Height / 2f, -10, plot.Height, 20), centre);
        g.Restore(state);
    }

    /// <summary>
    /// A legend is always present for two or more series - identity must never rest on colour
    /// alone. Each key repeats the line's own dash pattern.
    /// </summary>
    private static void DrawLegend(Graphics g, Rectangle bounds, IReadOnlyList<Co2Sweep> sweeps,
        ChartTheme theme, Font font, Brush brush)
    {
        float x = bounds.Left + MarginLeft;
        float y = bounds.Top + MarginTop - 6;

        for (int s = 0; s < sweeps.Count; s++)
        {
            foreach (bool dashed in new[] { false, true })
            {
                // The dashed curve is a reference built from the accepted forcing law, not
                // something the model produced, so it must not read as though it were.
                string text = dashed
                    ? $"{sweeps[s].Label} (logarithmic reference)"
                    : $"{sweeps[s].Label} (model)";
                var size = g.MeasureString(text, font);

                // Wrap to a second row rather than run off the figure.
                if (x + 26 + size.Width > bounds.Right - 12)
                {
                    x = bounds.Left + MarginLeft;
                    y += 16;
                }

                using var pen = new Pen(theme.Series[s % theme.Series.Length], 2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round
                };
                if (dashed) pen.DashPattern = new[] { 3f, 2f };

                g.DrawLine(pen, x, y + size.Height / 2, x + 20, y + size.Height / 2);
                g.DrawString(text, font, brush, x + 26, y);

                x += 26 + size.Width + 20;
            }
        }
    }

    private static void DrawSeries(Graphics g, IReadOnlyList<Co2Sweep> sweeps,
        Func<double, float> X, Func<double, float> Y, ChartTheme theme)
    {
        for (int s = 0; s < sweeps.Count; s++)
        {
            var colour = theme.Series[s % theme.Series.Length];

            DrawLine(g, sweeps[s], i => sweeps[s].Points[i].SurfaceTemperature, X, Y, colour, dashed: false);
            DrawLine(g, sweeps[s], i => sweeps[s].Expected(i), X, Y, colour, dashed: true);

            // End marker on the model curve, ringed in the surface colour.
            int last = sweeps[s].Points.Count - 1;
            float mx = X(sweeps[s].Points[last].Ppm);
            float my = Y(sweeps[s].Points[last].SurfaceTemperature);

            using var fill = new SolidBrush(colour);
            using var ring = new Pen(theme.Surface, 2f);
            g.FillEllipse(fill, mx - 4.5f, my - 4.5f, 9f, 9f);
            g.DrawEllipse(ring, mx - 4.5f, my - 4.5f, 9f, 9f);
        }
    }

    private static void DrawLine(Graphics g, Co2Sweep sweep, Func<int, double> value,
        Func<double, float> X, Func<double, float> Y, Color colour, bool dashed)
    {
        var points = new PointF[sweep.Points.Count];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = new PointF(X(sweep.Points[i].Ppm), Y(value(i)));
        }

        using var pen = new Pen(colour, 2f)
        {
            LineJoin = LineJoin.Round,
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        if (dashed) pen.DashPattern = new[] { 3.5f, 2.5f };

        g.DrawLines(pen, points);
    }

    /// <summary>
    /// Direct labels on the line ends only. Where two lines finish close together the labels
    /// are pushed apart and joined back to their own end by a leader, rather than stacked.
    /// </summary>
    private static void DrawEndLabels(Graphics g, Rectangle plot, IReadOnlyList<Co2Sweep> sweeps,
        Func<double, float> Y, ChartTheme theme, Font boldFont, Font subFont,
        Brush inkBrush, Brush mutedBrush)
    {
        var entries = new List<(double Value, string Note, int Slot, float Anchor)>();
        for (int s = 0; s < sweeps.Count; s++)
        {
            int last = sweeps[s].Points.Count - 1;
            entries.Add((sweeps[s].Points[last].SurfaceTemperature, "model", s, Y(sweeps[s].Points[last].SurfaceTemperature)));
            entries.Add((sweeps[s].Expected(last), "log reference", s, Y(sweeps[s].Expected(last))));
        }

        entries.Sort((a, b) => a.Anchor.CompareTo(b.Anchor));

        const float spacing = 30f;
        var placed = new float[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            placed[i] = i == 0 ? entries[i].Anchor : Math.Max(entries[i].Anchor, placed[i - 1] + spacing);
        }

        float overflow = placed[^1] - plot.Bottom;
        if (overflow > 0)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                placed[i] -= overflow;
                if (i > 0 && placed[i] - placed[i - 1] >= spacing) break;
            }
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            float labelY = placed[i];

            if (Math.Abs(labelY - e.Anchor) > 2f)
            {
                using var leader = new Pen(Color.FromArgb(128, theme.Series[e.Slot % theme.Series.Length]), 1f);
                g.DrawLines(leader, new[]
                {
                    new PointF(plot.Right + 2, e.Anchor),
                    new PointF(plot.Right + 7, e.Anchor),
                    new PointF(plot.Right + 11, labelY),
                    new PointF(plot.Right + 15, labelY)
                });
            }

            g.DrawString($"{e.Value.ToString("F1", Inv)} K", boldFont, inkBrush, plot.Right + 18, labelY - 8);
            g.DrawString(e.Note, subFont, mutedBrush, plot.Right + 18, labelY + 4);
        }
    }

    /// <summary>
    /// Crosshair, per-series dots and a readout box. Every value is also on an end label or
    /// in the values grid, so this enhances rather than gates.
    /// </summary>
    private static void DrawHover(Graphics g, Rectangle plot, IReadOnlyList<Co2Sweep> sweeps,
        int idx, Func<double, float> X, Func<double, float> Y, ChartTheme theme,
        Font font, Font boldFont)
    {
        float px = X(Co2Sweep.Concentrations[idx]);

        using var crosshair = new Pen(theme.Axis, 1f);
        g.DrawLine(crosshair, px, plot.Top, px, plot.Bottom);

        var rows = new List<(string Text, Color Colour, bool Dashed, string Value)>();
        for (int s = 0; s < sweeps.Count; s++)
        {
            var colour = theme.Series[s % theme.Series.Length];
            rows.Add((sweeps[s].Label, colour, false,
                sweeps[s].Points[idx].SurfaceTemperature.ToString("F2", Inv) + " K"));
            rows.Add((sweeps[s].Label + " (logarithmic reference)", colour, true,
                sweeps[s].Expected(idx).ToString("F2", Inv) + " K"));

            foreach (var (value, dashed) in new[]
                     { (sweeps[s].Points[idx].SurfaceTemperature, false), (sweeps[s].Expected(idx), true) })
            {
                float y = Y(value);
                using var fill = new SolidBrush(colour);
                using var ring = new Pen(theme.Surface, 2f);
                g.FillEllipse(fill, px - 4.5f, y - 4.5f, 9f, 9f);
                g.DrawEllipse(ring, px - 4.5f, y - 4.5f, 9f, 9f);
                _ = dashed;
            }
        }

        // Readout box, flipped to the other side of the crosshair when it would overflow.
        const int pad = 10, rowHeight = 17;
        float labelWidth = rows.Max(r => g.MeasureString(r.Text, font).Width);
        float valueWidth = rows.Max(r => g.MeasureString(r.Value, boldFont).Width);
        float boxWidth = pad * 2 + 24 + labelWidth + 14 + valueWidth;
        float boxHeight = pad * 2 + rowHeight * (rows.Count + 1);

        float bx = px + 14;
        if (bx + boxWidth > plot.Right) bx = px - 14 - boxWidth;
        bx = Math.Max(plot.Left, bx);

        // Place the box clear of the lines. The span that matters is the one the box itself
        // covers, not the hovered column: at 700 ppm the mean sits low, but the box extends
        // right to 1000 ppm where the curves have climbed into the top of the plot.
        float highest = plot.Bottom, lowest = plot.Top;
        for (int j = 0; j < Co2Sweep.Concentrations.Length; j++)
        {
            float jx = X(Co2Sweep.Concentrations[j]);
            if (jx < bx - 4 || jx > bx + boxWidth + 4) continue;

            for (int s = 0; s < sweeps.Count; s++)
            {
                foreach (double t in new[] { sweeps[s].Points[j].SurfaceTemperature, sweeps[s].Expected(j) })
                {
                    highest = Math.Min(highest, Y(t));
                    lowest = Math.Max(lowest, Y(t));
                }
            }
        }

        const float clearance = 12f;
        float by;
        if (plot.Bottom - lowest >= boxHeight + clearance) by = plot.Bottom - boxHeight - 8;
        else if (highest - plot.Top >= boxHeight + clearance) by = plot.Top + 8;
        else by = plot.Top + 8;   // nowhere is clear; the top is the least obstructive

        by = Math.Clamp(by, plot.Top + 4, Math.Max(plot.Top + 4, plot.Bottom - boxHeight - 4));

        var box = new RectangleF(bx, by, boxWidth, boxHeight);
        using (var back = new SolidBrush(theme.Surface))
        using (var edge = new Pen(theme.Axis, 1f))
        {
            g.FillRectangle(back, box);
            g.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
        }

        using var inkBrush = new SolidBrush(theme.Ink);
        using var secondary = new SolidBrush(theme.InkSecondary);

        g.DrawString($"{Co2Sweep.Concentrations[idx].ToString("N0", Inv)} ppm",
            boldFont, inkBrush, box.X + pad, box.Y + pad - 2);

        float ry = box.Y + pad + rowHeight;
        foreach (var row in rows)
        {
            using var pen = new Pen(row.Colour, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (row.Dashed) pen.DashPattern = new[] { 3f, 2f };
            g.DrawLine(pen, box.X + pad, ry + 8, box.X + pad + 18, ry + 8);

            g.DrawString(row.Text, font, secondary, box.X + pad + 24, ry);
            g.DrawString(row.Value, boldFont, inkBrush, box.Right - pad - valueWidth, ry + 1);

            ry += rowHeight;
        }
    }
}
