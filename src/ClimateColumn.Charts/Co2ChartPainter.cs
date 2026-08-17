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
/// Mark specs match the HTML renderer: 2px lines with round joins, dashing for the reference
/// law, solid hairline gridlines, 9px end markers ringed in the surface colour, and direct
/// labels on the line ends only. Hue carries the configuration and dashing carries
/// model-versus-reference.
///
/// What is plotted comes from <see cref="Co2ChartQuantity"/>, which also decides whether a
/// reference curve is drawn at all: forcing has one, because 5.35 ln(C/C0) is a statement about
/// forcing and can be compared directly; temperature does not, because converting that law into
/// a temperature would need the model's own sensitivity.
/// </remarks>
public static class Co2ChartPainter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const int MarginTop = 28, MarginRight = 150, MarginBottom = 62, MarginLeft = 74;
    private const int LegendHeight = 34;

    public static void Paint(Graphics g, Rectangle bounds, IReadOnlyList<Co2Sweep> sweeps,
        ChartTheme theme, int? hoverIndex, Co2ChartQuantity quantity)
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

        // The axis range and gridline spacing come from the quantity, shared with the HTML
        // renderer so the two figures cannot drift apart.
        var (yMin, yMax, yStep) = quantity.Range(sweeps);

        float X(double c) => plot.Left + (float)((c - xMin) / (xMax - xMin) * plot.Width);
        float Y(double t) => plot.Top + (float)((yMax - t) / (yMax - yMin) * plot.Height);

        using var tickFont = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        using var labelFont = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular);
        using var boldFont = new Font("Consolas", 8.75f, FontStyle.Bold, GraphicsUnit.Point);
        using var titleFont = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular);

        using var mutedBrush = new SolidBrush(theme.Muted);
        using var inkBrush = new SolidBrush(theme.Ink);
        using var secondaryBrush = new SolidBrush(theme.InkSecondary);

        DrawGrid(g, plot, yMin, yMax, yStep, Y, quantity, theme, tickFont, mutedBrush);
        DrawXAxis(g, plot, ppm, X, theme, tickFont, mutedBrush);
        DrawAxisTitles(g, bounds, plot, quantity, titleFont, secondaryBrush);
        DrawLegend(g, bounds, sweeps, quantity, theme, labelFont, secondaryBrush);
        DrawSeries(g, sweeps, X, Y, quantity, theme);
        DrawEndLabels(g, plot, sweeps, Y, quantity, theme, boldFont, tickFont, inkBrush, mutedBrush);

        if (hoverIndex is int idx && idx >= 0 && idx < ppm.Length)
        {
            DrawHover(g, plot, sweeps, idx, X, Y, quantity, theme, labelFont, boldFont);
        }
    }

    private static void DrawGrid(Graphics g, Rectangle plot, double yMin, double yMax, double step,
        Func<double, float> Y, Co2ChartQuantity quantity, ChartTheme theme, Font tickFont,
        Brush mutedBrush)
    {
        using var gridPen = new Pen(theme.Grid, 1f);
        using var zeroPen = new Pen(theme.Axis, 1f);

        using var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        for (double t = yMin; t <= yMax + 1e-9; t += step)
        {
            float y = Y(t);

            // Zero is a real datum on the forcing axis - the reference concentration - so it
            // gets the axis weight rather than a gridline's.
            bool isZero = Math.Abs(t) < 1e-9 && yMin < -1e-9;
            g.DrawLine(isZero ? zeroPen : gridPen, plot.Left, y, plot.Right, y);

            g.DrawString(t.ToString(quantity.TickFormat, Inv), tickFont, mutedBrush,
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
        Co2ChartQuantity quantity, Font font, Brush brush)
    {
        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawString("CO₂ concentration (ppm)", font, brush,
            new RectangleF(plot.Left, bounds.Bottom - 26, plot.Width, 20), centre);

        var state = g.Save();
        g.TranslateTransform(bounds.Left + 18, plot.Top + plot.Height / 2f);
        g.RotateTransform(-90);
        g.DrawString(quantity.AxisTitle, font, brush,
            new RectangleF(-plot.Height / 2f, -10, plot.Height, 20), centre);
        g.Restore(state);
    }

    /// <summary>
    /// A legend is always present for two or more series - identity must never rest on colour
    /// alone. Each key repeats the line's own dash pattern.
    /// </summary>
    private static void DrawLegend(Graphics g, Rectangle bounds, IReadOnlyList<Co2Sweep> sweeps,
        Co2ChartQuantity quantity, ChartTheme theme, Font font, Brush brush)
    {
        float x = bounds.Left + MarginLeft;
        float y = bounds.Top + MarginTop - 6;

        var patterns = quantity.HasReference ? new[] { false, true } : new[] { false };

        for (int s = 0; s < sweeps.Count; s++)
        {
            foreach (bool dashed in patterns)
            {
                // The dashed curve is the accepted law, not something the model produced, so it
                // must not read as though it were.
                string text = dashed
                    ? $"{quantity.ReferenceLabel} (accepted law)"
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
        Func<double, float> X, Func<double, float> Y, Co2ChartQuantity quantity, ChartTheme theme)
    {
        for (int s = 0; s < sweeps.Count; s++)
        {
            var sweep = sweeps[s];
            var colour = theme.Series[s % theme.Series.Length];

            DrawLine(g, sweep, i => quantity.Model(sweep, i), X, Y, colour, dashed: false);
            if (quantity.Reference is { } reference)
            {
                DrawLine(g, sweep, i => reference(sweep, i), X, Y, colour, dashed: true);
            }

            // End marker on the model curve, ringed in the surface colour.
            int last = sweeps[s].Points.Count - 1;
            float mx = X(sweeps[s].Points[last].Ppm);
            float my = Y(quantity.Model(sweep, last));

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
        Func<double, float> Y, Co2ChartQuantity quantity, ChartTheme theme, Font boldFont,
        Font subFont, Brush inkBrush, Brush mutedBrush)
    {
        var entries = new List<(double Value, string Note, int Slot, float Anchor)>();
        for (int s = 0; s < sweeps.Count; s++)
        {
            var sweep = sweeps[s];
            int last = sweep.Points.Count - 1;

            double model = quantity.Model(sweep, last);
            entries.Add((model, "model", s, Y(model)));

            if (quantity.Reference is { } reference)
            {
                double value = reference(sweep, last);
                entries.Add((value, "accepted law", s, Y(value)));
            }
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

            g.DrawString($"{e.Value.ToString(quantity.EndLabelFormat, Inv)} {quantity.Unit}",
                boldFont, inkBrush, plot.Right + 18, labelY - 8);
            g.DrawString(e.Note, subFont, mutedBrush, plot.Right + 18, labelY + 4);
        }
    }

    /// <summary>
    /// Crosshair, per-series dots and a readout box. Every value is also on an end label or
    /// in the values grid, so this enhances rather than gates.
    /// </summary>
    private static void DrawHover(Graphics g, Rectangle plot, IReadOnlyList<Co2Sweep> sweeps,
        int idx, Func<double, float> X, Func<double, float> Y, Co2ChartQuantity quantity,
        ChartTheme theme, Font font, Font boldFont)
    {
        float px = X(Co2Sweep.Concentrations[idx]);

        using var crosshair = new Pen(theme.Axis, 1f);
        g.DrawLine(crosshair, px, plot.Top, px, plot.Bottom);

        var rows = new List<(string Text, Color Colour, bool Dashed, string Value)>();
        for (int s = 0; s < sweeps.Count; s++)
        {
            var sweep = sweeps[s];
            var colour = theme.Series[s % theme.Series.Length];

            foreach (var (value, dashed, label) in HoverEntries(sweep, idx, quantity))
            {
                rows.Add((label, colour, dashed,
                    value.ToString(quantity.ValueFormat, Inv) + " " + quantity.Unit));

                float y = Y(value);
                using var fill = new SolidBrush(colour);
                using var ring = new Pen(theme.Surface, 2f);
                g.FillEllipse(fill, px - 4.5f, y - 4.5f, 9f, 9f);
                g.DrawEllipse(ring, px - 4.5f, y - 4.5f, 9f, 9f);
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
                foreach (var (value, _, _) in HoverEntries(sweeps[s], j, quantity))
                {
                    highest = Math.Min(highest, Y(value));
                    lowest = Math.Max(lowest, Y(value));
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

    /// <summary>
    /// The series a hover readout lists at one concentration: the model always, plus the
    /// reference law where the quantity has one.
    /// </summary>
    private static IEnumerable<(double Value, bool Dashed, string Label)> HoverEntries(
        Co2Sweep sweep, int index, Co2ChartQuantity quantity)
    {
        yield return (quantity.Model(sweep, index), false, sweep.Label);

        if (quantity.Reference is { } reference)
        {
            yield return (reference(sweep, index), true, quantity.ReferenceLabel!);
        }
    }
}
