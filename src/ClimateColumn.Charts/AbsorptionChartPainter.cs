using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Draws the infrared absorption bands as small multiples - one panel per gas, plus all of them
/// together.
/// </summary>
/// <remarks>
/// Small multiples rather than one overlay, because six filled traces on shared axes would hide
/// exactly what the figure exists to show. Water vapour saturates across most of the range and
/// would bury the isolated ozone band, which matters out of all proportion to its share of the
/// total optical depth precisely because it sits <em>inside</em> the window.
///
/// Each panel is labelled, so colour carries no identity here and one hue is used throughout
/// rather than a categorical palette. The combined panel is the exception and takes a neutral,
/// because it is a different kind of quantity from the gases above it.
/// </remarks>
public static class AbsorptionChartPainter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const int MarginTop = 10, MarginRight = 16, MarginBottom = 54, MarginLeft = 104;
    private const int PanelGap = 8;

    private static readonly double[] Ticks = { 200, 400, 667, 800, 1000, 1250, 1500, 2000 };

    public static void Paint(Graphics g, Rectangle bounds, IReadOnlyList<AbsorptionTrace> traces,
        double wingCutoff, ChartTheme theme)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var surface = new SolidBrush(theme.Surface);
        g.FillRectangle(surface, bounds);
        if (traces.Count == 0) return;

        int plotLeft = bounds.Left + MarginLeft;
        int plotRight = bounds.Right - MarginRight;
        if (plotRight - plotLeft < 80) return;

        int available = bounds.Height - MarginTop - MarginBottom - PanelGap * (traces.Count - 1);
        int panelHeight = available / traces.Count;
        if (panelHeight < 16) return;

        float X(double nu) => plotLeft + (float)(
            (nu - AbsorptionSpectrum.FromWavenumber) /
            (AbsorptionSpectrum.ToWavenumber - AbsorptionSpectrum.FromWavenumber) *
            (plotRight - plotLeft));

        using var labelFont = new Font(SystemFonts.DefaultFont.FontFamily, 8.75f, FontStyle.Regular);
        using var tickFont = new Font("Consolas", 8f, FontStyle.Regular, GraphicsUnit.Point);
        using var inkBrush = new SolidBrush(theme.Ink);
        using var mutedBrush = new SolidBrush(theme.Muted);
        using var captionBrush = new SolidBrush(theme.InkSecondary);
        using var axisPen = new Pen(theme.Axis, 1f);
        using var windowBrush = new SolidBrush(Color.FromArgb(20, theme.Reference));
        using var right = new StringFormat
        { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        using var centre = new StringFormat { Alignment = StringAlignment.Center };

        float windowLeft = X(AbsorptionSpectrum.WindowFrom);
        float windowWidth = X(AbsorptionSpectrum.WindowTo) - windowLeft;

        int top = bounds.Top + MarginTop;
        foreach (var trace in traces)
        {
            bool combined = trace.Gas == "All gases";
            float baseline = top + panelHeight;

            // The window is drawn behind every panel so it can be read straight down the figure.
            g.FillRectangle(windowBrush, windowLeft, top, windowWidth, panelHeight);
            g.DrawLine(axisPen, plotLeft, baseline, plotRight, baseline);

            var nu = AbsorptionSpectrum.Wavenumbers(trace.Absorptivity.Count);
            var points = new List<PointF>(nu.Length + 2) { new(X(nu[0]), baseline) };
            for (int b = 0; b < nu.Length; b++)
            {
                points.Add(new PointF(X(nu[b]),
                    baseline - (float)(trace.Absorptivity[b] * panelHeight)));
            }
            points.Add(new PointF(X(nu[^1]), baseline));

            using var fill = new SolidBrush(Color.FromArgb(
                combined ? 150 : 140, combined ? theme.Reference : theme.Series[0]));
            g.FillPolygon(fill, points.ToArray());

            g.DrawString(trace.Gas, labelFont, inkBrush,
                new RectangleF(bounds.Left + 4, top + panelHeight / 2f - 13, MarginLeft - 14, 14),
                right);
            g.DrawString(
                string.Format(Inv, "{0:P0} mean", AbsorptionSpectrum.MeanBetween(
                    trace, AbsorptionSpectrum.FromWavenumber, AbsorptionSpectrum.ToWavenumber)),
                tickFont, mutedBrush,
                new RectangleF(bounds.Left + 4, top + panelHeight / 2f + 1, MarginLeft - 14, 14),
                right);

            top += panelHeight + PanelGap;
        }

        float axisY = top - PanelGap;
        foreach (double t in Ticks)
        {
            g.DrawLine(axisPen, X(t), axisY, X(t), axisY + 4);
            g.DrawString(t.ToString("F0", Inv), tickFont, mutedBrush,
                new RectangleF(X(t) - 30, axisY + 6, 60, 14), centre);
            g.DrawString((1e4 / t).ToString("F1", Inv), tickFont, mutedBrush,
                new RectangleF(X(t) - 30, axisY + 19, 60, 14), centre);
        }

        g.DrawString("cm⁻¹", tickFont, mutedBrush,
            new RectangleF(bounds.Left + 4, axisY + 6, MarginLeft - 14, 14), right);
        g.DrawString("µm", tickFont, mutedBrush,
            new RectangleF(bounds.Left + 4, axisY + 19, MarginLeft - 14, 14), right);

        g.DrawString(string.Format(Inv,
            "Band-averaged absorptivity at observed Earth columns, wings integrated to {0:F0} cm⁻¹ " +
            "— shaded is the 800–1250 cm⁻¹ window, which carries no continuum here", wingCutoff),
            labelFont, captionBrush, bounds.Left + 8, bounds.Bottom - 18);
    }
}
