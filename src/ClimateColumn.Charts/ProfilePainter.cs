using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Draws the vertical temperature profile: temperature across, altitude up. One entry point
/// serves the on-screen control and the PNG export, as the response chart does.
/// </summary>
/// <remarks>
/// Mark specs follow <see cref="Co2ChartPainter"/> so the two figures read as one set - 2px
/// lines with round joins, hairline gridlines, markers ringed in the surface colour, direct
/// labels at the line ends only.
///
/// One code differs, deliberately. On the response chart a dashed line in the axis colour is
/// the accepted law: something the model did not produce. Here the faded curve is the same
/// model at the reference concentration, so it keeps its configuration's own hue and is
/// separated by weight instead. Hue means configuration on both figures; using the response
/// chart's dashed-grey for a baseline would have said "not the model" about a model result.
///
/// The profile is drawn down to z = 0 using the extrapolated near-surface air temperature, and
/// the ground is marked separately. Those two are not the same number - their difference is
/// what drives the sensible heat flux - and a figure that plotted only one of them would hide
/// the mechanism that couples the surface to the air above it.
/// </remarks>
public static class ProfilePainter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const int MarginTop = 28, MarginRight = 118, MarginBottom = 62, MarginLeft = 62;
    private const int LegendHeight = 34;

    /// <summary>
    /// One curve on the figure: a profile, the slot its colour comes from, and whether it is
    /// the selected state or the reference it is being compared against.
    /// </summary>
    private sealed record Curve(ColumnProfile Profile, int Slot, bool IsBaseline);

    /// <param name="selected">
    /// The profile to draw for each sweep, by concentration index. Out-of-range indices draw
    /// nothing rather than throwing - the export is public and the index can come from an
    /// argument.
    /// </param>
    public static void Paint(Graphics g, Rectangle bounds, IReadOnlyList<Co2Sweep> sweeps,
        ChartTheme theme, int selected, int? hoverLevel = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var surface = new SolidBrush(theme.Surface);
        g.FillRectangle(surface, bounds);

        var curves = Curves(sweeps, selected);
        if (curves.Count == 0) return;

        var plot = new Rectangle(
            bounds.Left + MarginLeft,
            bounds.Top + MarginTop + LegendHeight,
            Math.Max(80, bounds.Width - MarginLeft - MarginRight),
            Math.Max(80, bounds.Height - MarginTop - MarginBottom - LegendHeight));

        var (tMin, tMax, tStep) = TemperatureRange(curves);
        double zMax = curves.Max(c => c.Profile.ColumnTopAltitude);
        double zStep = Co2ChartQuantity.NiceStep(zMax / 6.0);

        float X(double t) => plot.Left + (float)((t - tMin) / (tMax - tMin) * plot.Width);
        float Y(double z) => plot.Bottom - (float)(z / zMax * plot.Height);

        using var tickFont = new Font("Consolas", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        using var labelFont = new Font(SystemFonts.DefaultFont.FontFamily, 9f, FontStyle.Regular);
        using var boldFont = new Font("Consolas", 8.75f, FontStyle.Bold, GraphicsUnit.Point);

        using var mutedBrush = new SolidBrush(theme.Muted);
        using var inkBrush = new SolidBrush(theme.Ink);
        using var secondaryBrush = new SolidBrush(theme.InkSecondary);

        DrawConvectingLayer(g, plot, curves, X, Y, theme, tickFont, mutedBrush);
        DrawGrid(g, plot, tMin, tMax, tStep, zMax, zStep, X, Y, theme, tickFont, mutedBrush);
        DrawEmissionRule(g, plot, curves, X, theme, tickFont, mutedBrush);
        DrawAxisTitles(g, bounds, plot, labelFont, secondaryBrush);
        DrawLegend(g, bounds, sweeps, curves, selected, theme, labelFont, secondaryBrush);
        DrawCurves(g, curves, X, Y, theme);

        // After the curves, not before. The crossings sit on the lines by definition, so drawing
        // them first let the lines paint over their own markers and clip the labels' first
        // characters. The rule itself stays underneath, where a reference line belongs.
        DrawEmissionCrossings(g, plot, curves, X, Y, theme, tickFont, mutedBrush);
        DrawSurfaceMarkers(g, plot, curves, X, Y, theme, tickFont, boldFont, inkBrush, mutedBrush);

        if (hoverLevel is int level)
        {
            DrawHover(g, plot, curves, level, X, Y, theme, labelFont, boldFont);
        }
    }

    /// <summary>
    /// The curves to draw: each sweep at the selected concentration, plus the first sweep's
    /// reference profile when the selection has moved off it.
    /// </summary>
    /// <remarks>
    /// A profile identical to one already on the figure is dropped, exactly as on the response
    /// chart. Two cases produce one: the frozen-vapour configuration matches the feedback one
    /// at the reference concentration, and the reference profile itself is redundant when the
    /// reference is what is selected.
    /// </remarks>
    private static List<Curve> Curves(IReadOnlyList<Co2Sweep> sweeps, int selected)
    {
        var curves = new List<Curve>();

        for (int s = 0; s < sweeps.Count; s++)
        {
            var profiles = sweeps[s].Profiles;
            if (selected < 0 || selected >= profiles.Count) continue;

            var profile = profiles[selected];
            if (profile.Levels.Count == 0) continue;
            if (curves.Any(c => c.Profile.Matches(profile))) continue;

            curves.Add(new Curve(profile, s, IsBaseline: false));
        }

        if (curves.Count > 0 && sweeps[0].Profiles.Count > 0)
        {
            var baseline = sweeps[0].Profiles[0];
            if (baseline.Levels.Count > 0 && !curves.Any(c => c.Profile.Matches(baseline)))
            {
                curves.Add(new Curve(baseline, 0, IsBaseline: true));
            }
        }

        return curves;
    }

    /// <summary>
    /// Temperature range covering every curve, padded to a readable step. The surface
    /// temperatures are included even though they are drawn as markers rather than as part of
    /// a line - they are the warmest points on the figure, and an axis that clipped them would
    /// put the headline number outside the plot.
    /// </summary>
    private static (double Min, double Max, double Step) TemperatureRange(List<Curve> curves)
    {
        double lo = double.MaxValue, hi = double.MinValue;

        foreach (var curve in curves)
        {
            foreach (var level in curve.Profile.Levels)
            {
                lo = Math.Min(lo, level.Temperature);
                hi = Math.Max(hi, level.Temperature);
            }
            lo = Math.Min(lo, Math.Min(curve.Profile.SurfaceTemperature,
                                       curve.Profile.NearSurfaceAirTemperature));
            hi = Math.Max(hi, Math.Max(curve.Profile.SurfaceTemperature,
                                       curve.Profile.NearSurfaceAirTemperature));
        }

        if (lo > hi) return (200.0, 300.0, 20.0);

        // Padded by a fraction of the span rather than snapped out to whole gridline steps.
        // Snapping wasted a quarter of the plot: a 170-290 K profile has a 25 K step, and
        // rounding both ends out to a multiple of that opened the axis to 144-306 K.
        // The gridlines still land on round numbers - that is the drawing loop's job, not
        // the range's.
        double span = Math.Max(1.0, hi - lo);
        return (lo - 0.06 * span, hi + 0.06 * span,
                Co2ChartQuantity.NiceStep(span / 5.0));
    }

    /// <summary>
    /// The convecting layer as a tint, and a rule at each distinct convective top.
    /// </summary>
    /// <remarks>
    /// The tint stops at the <em>lowest</em> top on the figure, because that is the only region
    /// every curve agrees is convecting. Where the tops differ - and they do, since warming
    /// lifts the convective top - each gets its own rule in its own colour. A single band drawn
    /// to the highest top would claim agreement that is not there.
    /// </remarks>
    private static void DrawConvectingLayer(Graphics g, Rectangle plot, List<Curve> curves,
        Func<double, float> X, Func<double, float> Y, ChartTheme theme, Font font, Brush mutedBrush)
    {
        var tops = curves.Where(c => c.Profile.ConvectiveTopAltitude > 0.0).ToList();
        if (tops.Count == 0) return;

        double lowest = tops.Min(c => c.Profile.ConvectiveTopAltitude);

        using (var tint = new SolidBrush(Color.FromArgb(38, theme.Grid)))
        {
            float top = Y(lowest);
            g.FillRectangle(tint, plot.Left, top, plot.Width, plot.Bottom - top);
        }

        // Rules only where a top differs from the tinted edge; drawing one on top of the tint
        // boundary would just double the same line.
        foreach (var curve in tops)
        {
            double z = curve.Profile.ConvectiveTopAltitude;
            if (Math.Abs(z - lowest) < 1e-6) continue;

            using var pen = new Pen(Color.FromArgb(110, theme.Series[curve.Slot % theme.Series.Length]), 1f)
            {
                DashPattern = new[] { 4f, 3f }
            };
            g.DrawLine(pen, plot.Left, Y(z), plot.Right, Y(z));
        }

        double gamma = tops[0].Profile.CriticalLapseRate * 1000.0;
        g.DrawString(
            string.Format(Inv, "convecting · {0:F1} K/km", gamma),
            font, mutedBrush, plot.Left + 6, Y(lowest) - 16);
    }

    private static void DrawGrid(Graphics g, Rectangle plot, double tMin, double tMax, double tStep,
        double zMax, double zStep, Func<double, float> X, Func<double, float> Y, ChartTheme theme,
        Font tickFont, Brush mutedBrush)
    {
        using var gridPen = new Pen(theme.Grid, 1f);
        using var axisPen = new Pen(theme.Axis, 1f);
        using var right = new StringFormat
        {
            Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center
        };
        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        for (double z = 0.0; z <= zMax + 1e-6; z += zStep)
        {
            float y = Y(z);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString((z / 1000.0).ToString("F0", Inv), tickFont, mutedBrush,
                new RectangleF(plot.Left - 52, y - 8, 44, 16), right);
        }

        for (double t = Math.Ceiling(tMin / tStep) * tStep; t <= tMax + 1e-9; t += tStep)
        {
            float x = X(t);
            g.DrawLine(gridPen, x, plot.Top, x, plot.Bottom);
            g.DrawString(t.ToString("F0", Inv), tickFont, mutedBrush,
                new RectangleF(x - 30, plot.Bottom + 7, 60, 16), centre);
        }

        // The ground is a boundary of the model, not a gridline, so it carries the axis weight.
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
    }

    /// <summary>
    /// A rule at the planetary emission temperature, with the altitude each curve reaches it at.
    /// </summary>
    /// <remarks>
    /// This is the greenhouse argument in one picture: the planet must be seen from space to be
    /// as cold as its emission temperature, the column reaches that temperature some kilometres
    /// up, and the surface is warmer by the lapse rate times that height. The crossings move up
    /// as CO2 is added, which is the mechanism rather than a restatement of the result.
    ///
    /// It is a diagnostic and not a physical level - the real emission is spectral, from many
    /// different heights at once - so it is drawn in the axis colour rather than as a series,
    /// and labelled as a temperature rather than as "the emission level".
    /// </remarks>
    private static void DrawEmissionRule(Graphics g, Rectangle plot, List<Curve> curves,
        Func<double, float> X, ChartTheme theme, Font font, Brush mutedBrush)
    {
        double te = curves[0].Profile.EmissionTemperature;
        if (te <= 0.0) return;

        float x = X(te);
        if (x < plot.Left || x > plot.Right) return;

        using (var pen = new Pen(theme.Axis, 1f) { DashPattern = new[] { 3f, 3f } })
        {
            g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
        }

        using var centre = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(string.Format(Inv, "Tₑ = {0:F1} K", te), font, mutedBrush,
            new RectangleF(x - 45, plot.Top + 2, 90, 16), centre);
    }

    /// <summary>
    /// Where each curve crosses the emission temperature, with the altitudes stacked clear of
    /// each other.
    /// </summary>
    /// <remarks>
    /// The labels go to the <em>left</em> of the rule. To its right is where the curves are at
    /// these heights - the crossings are on the lines - so labels placed there sat on top of
    /// the very curves they annotate. Left of the rule at a few kilometres up, nothing is that
    /// cold, so the space is free.
    /// </remarks>
    private static void DrawEmissionCrossings(Graphics g, Rectangle plot, List<Curve> curves,
        Func<double, float> X, Func<double, float> Y, ChartTheme theme, Font font, Brush mutedBrush)
    {
        double te = curves[0].Profile.EmissionTemperature;
        if (te <= 0.0) return;

        float x = X(te);
        if (x < plot.Left || x > plot.Right) return;

        var crossings = new List<(float Y, string Text)>();

        foreach (var curve in curves)
        {
            double z = curve.Profile.EmissionAltitude;
            if (double.IsNaN(z)) continue;

            var colour = theme.Series[curve.Slot % theme.Series.Length];
            if (curve.IsBaseline) colour = Color.FromArgb(150, colour);

            using var fill = new SolidBrush(colour);
            using var ring = new Pen(theme.Surface, 2f);
            g.FillEllipse(fill, x - 4f, Y(z) - 4f, 8f, 8f);
            g.DrawEllipse(ring, x - 4f, Y(z) - 4f, 8f, 8f);

            crossings.Add((Y(z), string.Format(Inv, "{0:F2} km", z / 1000.0)));
        }

        if (crossings.Count == 0) return;

        crossings.Sort((a, b) => a.Y.CompareTo(b.Y));

        var placed = new float[crossings.Count];
        for (int i = 0; i < crossings.Count; i++)
        {
            placed[i] = i == 0 ? crossings[i].Y : Math.Max(crossings[i].Y, placed[i - 1] + 15f);
        }

        using var right = new StringFormat { Alignment = StringAlignment.Far };
        for (int i = 0; i < crossings.Count; i++)
        {
            g.DrawString(crossings[i].Text, font, mutedBrush,
                new RectangleF(x - 82, placed[i] - 7, 74, 15), right);
        }
    }

    private static void DrawAxisTitles(Graphics g, Rectangle bounds, Rectangle plot,
        Font font, Brush brush)
    {
        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawString("Temperature (K)", font, brush,
            new RectangleF(plot.Left, bounds.Bottom - 26, plot.Width, 20), centre);

        var state = g.Save();
        g.TranslateTransform(bounds.Left + 16, plot.Top + plot.Height / 2f);
        g.RotateTransform(-90);
        g.DrawString("Altitude (km)", font, brush,
            new RectangleF(-plot.Height / 2f, -10, plot.Height, 20), centre);
        g.Restore(state);
    }

    private static void DrawLegend(Graphics g, Rectangle bounds, IReadOnlyList<Co2Sweep> sweeps,
        List<Curve> curves, int selected, ChartTheme theme, Font font, Brush brush)
    {
        float x = bounds.Left + MarginLeft;
        float y = bounds.Top + MarginTop - 6;

        // Every configuration gets a key, drawn or not, so a reader is told why a colour they
        // saw on the response chart is missing here rather than left to wonder.
        var keys = new List<(string Text, int Slot, bool Faded)>();
        for (int s = 0; s < sweeps.Count; s++)
        {
            bool drawn = curves.Any(c => c.Slot == s && !c.IsBaseline);
            keys.Add((sweeps[s].Label + (drawn ? "" : " — identical here, not drawn"), s, !drawn));
        }

        if (curves.Any(c => c.IsBaseline))
        {
            keys.Add((string.Format(Inv, "{0:F0} ppm baseline", curves.First(c => c.IsBaseline).Profile.Ppm),
                0, true));
        }

        foreach (var (text, slot, faded) in keys)
        {
            var size = g.MeasureString(text, font);
            if (x + 26 + size.Width > bounds.Right - 12)
            {
                x = bounds.Left + MarginLeft;
                y += 16;
            }

            var colour = theme.Series[slot % theme.Series.Length];
            using var pen = new Pen(faded ? Color.FromArgb(150, colour) : colour, faded ? 1.5f : 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            g.DrawLine(pen, x, y + size.Height / 2, x + 20, y + size.Height / 2);
            g.DrawString(text, font, brush, x + 26, y);

            x += 26 + size.Width + 20;
        }
    }

    private static void DrawCurves(Graphics g, List<Curve> curves,
        Func<double, float> X, Func<double, float> Y, ChartTheme theme)
    {
        // Baselines first, so the selected state is never hidden behind its own comparison.
        foreach (var curve in curves.OrderByDescending(c => c.IsBaseline))
        {
            var colour = theme.Series[curve.Slot % theme.Series.Length];
            if (curve.IsBaseline) colour = Color.FromArgb(150, colour);

            var points = CurvePoints(curve.Profile, X, Y);

            using var pen = new Pen(colour, curve.IsBaseline ? 1.5f : 2f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLines(pen, points);
        }
    }

    /// <summary>
    /// The curve in screen space, from the ground up. The first point is the air temperature
    /// extrapolated to z = 0 rather than the lowest layer midpoint, which is where the model's
    /// own surface flux is evaluated - so the line meets the ground at the temperature the
    /// physics uses there.
    /// </summary>
    private static PointF[] CurvePoints(ColumnProfile profile,
        Func<double, float> X, Func<double, float> Y)
    {
        var points = new PointF[profile.Levels.Count + 1];
        points[0] = new PointF(X(profile.NearSurfaceAirTemperature), Y(0.0));

        for (int i = 0; i < profile.Levels.Count; i++)
        {
            points[i + 1] = new PointF(X(profile.Levels[i].Temperature), Y(profile.Levels[i].Altitude));
        }
        return points;
    }

    /// <summary>
    /// The ground itself: a square at the surface temperature, a ring where the air meets it,
    /// and a link between the two.
    /// </summary>
    /// <remarks>
    /// Those are different temperatures, and the gap between them is what the sensible heat
    /// flux is proportional to. Marking only one would draw an atmosphere in contact with a
    /// surface at its own temperature, which is not what the model solves.
    ///
    /// The values are labelled in the right margin with leaders back to their markers, rather
    /// than beside the markers themselves. Two configurations put their surfaces within a
    /// couple of kelvin of each other - close enough that labels drawn in place overlapped, and
    /// close to the right edge, where they also ran off the figure. The margin has room and
    /// already collision-checks.
    /// </remarks>
    private static void DrawSurfaceMarkers(Graphics g, Rectangle plot, List<Curve> curves,
        Func<double, float> X, Func<double, float> Y, ChartTheme theme, Font font, Font boldFont,
        Brush inkBrush, Brush mutedBrush)
    {
        float ground = Y(0.0);
        var drawn = curves.Where(c => !c.IsBaseline).ToList();
        if (drawn.Count == 0) return;

        foreach (var curve in drawn)
        {
            var colour = theme.Series[curve.Slot % theme.Series.Length];
            float sx = X(curve.Profile.SurfaceTemperature);
            float ax = X(curve.Profile.NearSurfaceAirTemperature);

            using (var link = new Pen(Color.FromArgb(90, colour), 1f))
            {
                g.DrawLine(link, ax, ground, sx, ground);
            }

            using var fill = new SolidBrush(colour);
            using var ring = new Pen(theme.Surface, 2f);
            using var hollow = new Pen(colour, 1.5f);

            g.FillRectangle(fill, sx - 4f, ground - 4f, 8f, 8f);
            g.DrawRectangle(ring, sx - 4f, ground - 4f, 8f, 8f);
            g.DrawEllipse(hollow, ax - 3f, ground - 3f, 6f, 6f);
        }

        // Stacked upward from the ground so the block sits in the bottom-right corner, which is
        // empty on a profile: nothing is both that warm and that high.
        var entries = drawn
            .OrderByDescending(c => c.Profile.SurfaceTemperature)
            .ToList();

        for (int i = 0; i < entries.Count; i++)
        {
            var curve = entries[i];
            var colour = theme.Series[curve.Slot % theme.Series.Length];
            float labelY = ground - 12 - (entries.Count - 1 - i) * 26f;
            float sx = X(curve.Profile.SurfaceTemperature);

            // Straight to the label rather than along the ground and up. Both leaders would
            // otherwise trace the same horizontal path, drawing what looks like an extra rule
            // at z = 0.
            using (var leader = new Pen(Color.FromArgb(128, colour), 1f))
            {
                g.DrawLines(leader, new[]
                {
                    new PointF(sx + 6, ground),
                    new PointF(plot.Right + 10, labelY),
                    new PointF(plot.Right + 14, labelY)
                });
            }

            g.DrawString(string.Format(Inv, "{0:F2} K", curve.Profile.SurfaceTemperature),
                boldFont, inkBrush, plot.Right + 17, labelY - 9);
            g.DrawString("surface", font, mutedBrush, plot.Right + 17, labelY + 2);
        }
    }

    /// <summary>
    /// A horizontal crosshair at one model level, with every curve read out at that height.
    /// Pressure appears here rather than as a second axis: it is the same position expressed
    /// differently, and the place for that is the readout, not a competing scale.
    /// </summary>
    private static void DrawHover(Graphics g, Rectangle plot, List<Curve> curves, int level,
        Func<double, float> X, Func<double, float> Y, ChartTheme theme, Font font, Font boldFont)
    {
        var reference = curves[0].Profile;
        if (level < 0 || level >= reference.Levels.Count) return;

        double z = reference.Levels[level].Altitude;
        float py = Y(z);

        using (var crosshair = new Pen(theme.Axis, 1f))
        {
            g.DrawLine(crosshair, plot.Left, py, plot.Right, py);
        }

        var rows = new List<(string Text, Color Colour, bool Faded, string Value)>();
        foreach (var curve in curves)
        {
            if (level >= curve.Profile.Levels.Count) continue;

            var colour = theme.Series[curve.Slot % theme.Series.Length];
            double t = curve.Profile.Levels[level].Temperature;

            string label = curve.IsBaseline
                ? string.Format(Inv, "{0:F0} ppm baseline", curve.Profile.Ppm)
                : curve.Profile.Label;

            rows.Add((label, curve.IsBaseline ? Color.FromArgb(150, colour) : colour,
                curve.IsBaseline, t.ToString("F3", Inv) + " K"));

            using var fill = new SolidBrush(curve.IsBaseline ? Color.FromArgb(150, colour) : colour);
            using var ring = new Pen(theme.Surface, 2f);
            g.FillEllipse(fill, X(t) - 4.5f, py - 4.5f, 9f, 9f);
            g.DrawEllipse(ring, X(t) - 4.5f, py - 4.5f, 9f, 9f);
        }

        if (rows.Count == 0) return;

        const int pad = 10, rowHeight = 17;
        string heading = string.Format(Inv, "{0:F2} km · {1:F1} hPa",
            z / 1000.0, reference.Levels[level].Pressure / 100.0);

        float labelWidth = rows.Max(r => g.MeasureString(r.Text, font).Width);
        float valueWidth = rows.Max(r => g.MeasureString(r.Value, boldFont).Width);
        float boxWidth = Math.Max(pad * 2 + 24 + labelWidth + 14 + valueWidth,
                                  pad * 2 + g.MeasureString(heading, boldFont).Width);
        float boxHeight = pad * 2 + rowHeight * (rows.Count + 1);

        // Below the crosshair unless that would overflow, and pinned to the left of the plot
        // where the curves are coldest and the box is least likely to sit on one.
        float bx = Math.Min(plot.Left + 8, plot.Right - boxWidth - 4);
        float by = py + 12;
        if (by + boxHeight > plot.Bottom - 4) by = py - 12 - boxHeight;
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

        g.DrawString(heading, boldFont, inkBrush, box.X + pad, box.Y + pad - 2);

        float ry = box.Y + pad + rowHeight;
        foreach (var row in rows)
        {
            using var pen = new Pen(row.Colour, row.Faded ? 1.5f : 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawLine(pen, box.X + pad, ry + 8, box.X + pad + 18, ry + 8);

            g.DrawString(row.Text, font, secondary, box.X + pad + 24, ry);
            g.DrawString(row.Value, boldFont, inkBrush, box.Right - pad - valueWidth, ry + 1);

            ry += rowHeight;
        }
    }
}
