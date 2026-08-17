using System.Globalization;
using System.Text;
using ClimateColumn.Core;

namespace ClimateColumn.Tests;

/// <summary>
/// Renders a concentration sweep as a self-contained HTML page: an SVG line chart plus the
/// table of every plotted value. No external assets, no scripts - the geometry is computed
/// here so the figure is exactly what the model produced.
/// </summary>
/// <remarks>
/// Hue carries the configuration and dashing carries model-versus-expectation, so two
/// categorical colours cover four lines. Both are slots from a palette validated for
/// colour-vision deficiency in light and dark mode. Every value also appears in the table,
/// so nothing is reachable only by reading a line position.
/// </remarks>
internal static class Co2ChartRenderer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Categorical slots 1 and 2, light / dark steps.
    private static readonly (string Light, string Dark)[] SeriesColors =
        { ("#2a78d6", "#3987e5"), ("#eb6834", "#d95926") };

    private const int Width = 900, Height = 470;
    private const int MarginTop = 22, MarginRight = 138, MarginBottom = 54, MarginLeft = 62;
    private const int PlotWidth = Width - MarginLeft - MarginRight;
    private const int PlotHeight = Height - MarginTop - MarginBottom;

    private static readonly double[] XTicks = { 285, 400, 500, 600, 700, 800, 900, 1000 };

    public static string Render(Co2Sweep noFeedback, Co2Sweep withFeedback)
    {
        var sweeps = new[] { noFeedback, withFeedback };

        double xMin = Co2Sweep.Concentrations[0];
        double xMax = Co2Sweep.Concentrations[^1];

        // Round the y range outward to whole kelvin so the ticks land on clean numbers.
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

        double X(double ppm) => MarginLeft + (ppm - xMin) / (xMax - xMin) * PlotWidth;
        double Y(double t) => MarginTop + (yMax - t) / (yMax - yMin) * PlotHeight;

        var svg = new StringBuilder();

        // Gridlines and y ticks - solid hairlines, one step off the surface.
        int tickStep = (int)Math.Max(1, Math.Round((yMax - yMin) / 6.0));
        for (double t = yMin; t <= yMax + 1e-9; t += tickStep)
        {
            svg.AppendLine(Fmt(
                "  <line x1=\"{0}\" x2=\"{1}\" y1=\"{2:F1}\" y2=\"{2:F1}\" stroke=\"var(--grid)\" stroke-width=\"1\"/>",
                MarginLeft, MarginLeft + PlotWidth, Y(t)));
            svg.AppendLine(Fmt(
                "  <text x=\"{0}\" y=\"{1:F1}\" text-anchor=\"end\" class=\"tick\">{2:F0}</text>",
                MarginLeft - 12, Y(t) + 4, t));
        }

        // Axis rule and x ticks.
        svg.AppendLine(Fmt(
            "  <line x1=\"{0}\" x2=\"{1}\" y1=\"{2}\" y2=\"{2}\" stroke=\"var(--axis)\" stroke-width=\"1\"/>",
            MarginLeft, MarginLeft + PlotWidth, MarginTop + PlotHeight));

        foreach (double c in XTicks)
        {
            svg.AppendLine(Fmt(
                "  <text x=\"{0:F1}\" y=\"{1}\" text-anchor=\"middle\" class=\"tick\">{2:F0}</text>",
                X(c), MarginTop + PlotHeight + 22, c));
        }

        svg.AppendLine(Fmt(
            "  <text x=\"{0:F1}\" y=\"{1}\" text-anchor=\"middle\" class=\"axis-title\">CO₂ concentration (ppm)</text>",
            MarginLeft + PlotWidth / 2.0, Height - 10));
        svg.AppendLine(Fmt(
            "  <text x=\"{0:F1}\" y=\"16\" transform=\"rotate(-90)\" text-anchor=\"middle\" class=\"axis-title\">Surface temperature (K)</text>",
            -(MarginTop + PlotHeight / 2.0)));

        // Lines: 2px, round join and cap. Dashed marks the logarithmic expectation.
        for (int s = 0; s < sweeps.Length; s++)
        {
            string color = Fmt("var(--series-{0})", s + 1);

            svg.AppendLine(Fmt(
                "  <path class=\"series-line\" d=\"{0}\" fill=\"none\" stroke=\"{1}\" stroke-width=\"2\" " +
                "stroke-linejoin=\"round\" stroke-linecap=\"round\"/>",
                Path(sweeps[s], i => sweeps[s].Points[i].SurfaceTemperature, X, Y), color));

            svg.AppendLine(Fmt(
                "  <path class=\"series-line\" d=\"{0}\" fill=\"none\" stroke=\"{1}\" stroke-width=\"2\" " +
                "stroke-linejoin=\"round\" stroke-linecap=\"round\" stroke-dasharray=\"7 5\" opacity=\"0.85\"/>",
                Path(sweeps[s], i => sweeps[s].Expected(i), X, Y), color));
        }

        // End markers on the model curves, ringed in the surface colour so they stay legible.
        for (int s = 0; s < sweeps.Length; s++)
        {
            int last = sweeps[s].Points.Count - 1;
            svg.AppendLine(Fmt(
                "  <circle cx=\"{0:F1}\" cy=\"{1:F1}\" r=\"4.5\" fill=\"var(--series-{2})\" stroke=\"var(--surface)\" stroke-width=\"2\"/>",
                X(sweeps[s].Points[last].Ppm), Y(sweeps[s].Points[last].SurfaceTemperature), s + 1));
        }

        // Direct labels on the four line ends only - never a value on every point. Where the
        // lines converge the labels are pushed apart and joined to their own line end by a
        // leader, rather than stacked (which detaches them and reads as noise) or overlapped.
        foreach (var label in PlaceEndLabels(EndLabels(sweeps), Y))
        {
            if (label.NeedsLeader)
            {
                svg.AppendLine(Fmt(
                    "  <path class=\"leader\" d=\"M{0},{1:F1} L{2},{1:F1} L{3},{4:F1} L{5},{4:F1}\" fill=\"none\" " +
                    "stroke=\"var(--series-{6})\" stroke-width=\"1\" opacity=\"0.5\"/>",
                    MarginLeft + PlotWidth + 2, label.AnchorY,
                    MarginLeft + PlotWidth + 6,
                    MarginLeft + PlotWidth + 9, label.LabelY,
                    MarginLeft + PlotWidth + 12, label.Slot));
            }

            svg.AppendLine(Fmt(
                "  <text x=\"{0}\" y=\"{1:F1}\" class=\"end-label\">{2:F1} K</text>",
                MarginLeft + PlotWidth + 15, label.LabelY + 1, label.Value));
            svg.AppendLine(Fmt(
                "  <text x=\"{0}\" y=\"{1:F1}\" class=\"end-label-sub\">{2}</text>",
                MarginLeft + PlotWidth + 15, label.LabelY + 15, label.Note));
        }

        // Hover layer: a crosshair, one dot per series, and a transparent hit rect over the
        // whole plot so the target is the full column rather than each 9px dot.
        svg.AppendLine(Fmt(
            "  <line id=\"crosshair\" y1=\"{0}\" y2=\"{1}\" stroke=\"var(--axis)\" stroke-width=\"1\" opacity=\"0\"/>",
            MarginTop, MarginTop + PlotHeight));

        for (int s = 0; s < sweeps.Length; s++)
        {
            foreach (string kind in new[] { "model", "expected" })
            {
                svg.AppendLine(Fmt(
                    "  <circle class=\"hover-dot\" data-series=\"{0}-{1}\" r=\"4.5\" fill=\"var(--series-{0})\" " +
                    "stroke=\"var(--surface)\" stroke-width=\"2\" opacity=\"0\"/>", s + 1, kind));
            }
        }

        svg.AppendLine(Fmt(
            "  <rect id=\"hit\" x=\"{0}\" y=\"{1}\" width=\"{2}\" height=\"{3}\" fill=\"transparent\" style=\"cursor:crosshair\"/>",
            MarginLeft, MarginTop, PlotWidth, PlotHeight));

        var scale = new ChartScale(MarginLeft, MarginTop, PlotWidth, PlotHeight,
            Width, xMin, xMax, yMin, yMax);

        return Page(sweeps, svg.ToString(), scale);
    }

    /// <summary>
    /// The plot geometry, handed to the hover script so it recomputes coordinates with the
    /// same formulas the SVG was drawn with rather than a second, drifting copy.
    /// </summary>
    private sealed record ChartScale(
        int Left, int Top, int PlotWidth, int PlotHeight, int ViewBoxWidth,
        double XMin, double XMax, double YMin, double YMax);

    private static IEnumerable<(double Value, string Note, int Slot)> EndLabels(Co2Sweep[] sweeps)
    {
        for (int s = 0; s < sweeps.Length; s++)
        {
            int last = sweeps[s].Points.Count - 1;
            yield return (sweeps[s].Points[last].SurfaceTemperature, "model", s + 1);
            yield return (sweeps[s].Expected(last), "expected", s + 1);
        }
    }

    private sealed record PlacedLabel(
        double Value, string Note, int Slot, double AnchorY, double LabelY, bool NeedsLeader);

    /// <summary>
    /// Spreads end labels vertically so a converging pair cannot overlap, and reports which
    /// ones moved far enough to need a leader line back to their own line end.
    /// </summary>
    /// <remarks>
    /// Each label is two lines of type, so it needs about 30px. The pass sorts by anchor,
    /// pushes downward to enforce that spacing, then pushes back up if the last label ran
    /// past the plot - which keeps the whole set inside the figure however the sweep range
    /// changes. Without this, two lines that finish within ~0.1 K of each other render their
    /// labels on top of one another.
    /// </remarks>
    private static List<PlacedLabel> PlaceEndLabels(
        IEnumerable<(double Value, string Note, int Slot)> labels, Func<double, double> y)
    {
        const double spacing = 30.0;

        var ordered = labels
            .Select(l => (l.Value, l.Note, l.Slot, Anchor: y(l.Value)))
            .OrderBy(l => l.Anchor)
            .ToList();

        var placed = new double[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            placed[i] = i == 0
                ? ordered[i].Anchor
                : Math.Max(ordered[i].Anchor, placed[i - 1] + spacing);
        }

        // If the stack overflowed the bottom of the plot, slide the whole run up.
        double overflow = placed[^1] - (MarginTop + PlotHeight);
        if (overflow > 0)
        {
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                placed[i] -= overflow;
                if (i > 0 && placed[i] - placed[i - 1] >= spacing) break;
            }
        }

        return ordered
            .Select((l, i) => new PlacedLabel(
                l.Value, l.Note, l.Slot, l.Anchor, placed[i],
                Math.Abs(placed[i] - l.Anchor) > 2.0))
            .ToList();
    }

    private static string Path(Co2Sweep sweep, Func<int, double> value,
        Func<double, double> x, Func<double, double> y)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < sweep.Points.Count; i++)
        {
            sb.Append(Fmt("{0}{1:F1},{2:F1} ",
                i == 0 ? "M" : "L", x(sweep.Points[i].Ppm), y(value(i))));
        }
        return sb.ToString().TrimEnd();
    }

    private static string Page(Co2Sweep[] sweeps, string svg, ChartScale scale)
    {
        int last = Co2Sweep.Concentrations.Length - 1;
        var sb = new StringBuilder();

        sb.AppendLine("<title>ClimateColumn CO₂ Response</title>");
        sb.AppendLine();
        sb.AppendLine(Style());
        sb.AppendLine("<div class=\"wrap\">");

        sb.AppendLine("  <header>");
        sb.AppendLine("    <span class=\"eyebrow\">ClimateColumn · generated by the test suite</span>");
        sb.AppendLine(Fmt("    <h1>Surface temperature against CO₂, {0:F0} → {1:F0} ppm</h1>",
            Co2Sweep.Concentrations[0], Co2Sweep.Concentrations[last]));
        sb.AppendLine("    <p class=\"lede\">Two calibrated configurations, each against the " +
                      "logarithmic response it would follow if its forcing obeyed " +
                      "5.35 ln(C/C₀). Solid is the model; dashed is that expectation.</p>");
        sb.AppendLine("  </header>");

        // Legend - identity never rests on colour alone.
        sb.AppendLine("  <section class=\"card\">");
        sb.AppendLine("    <div class=\"legend\">");
        for (int s = 0; s < sweeps.Length; s++)
        {
            foreach (var (dash, suffix) in new[] { ("", ""), (" stroke-dasharray=\"6 4\"", ", expected") })
            {
                sb.AppendLine(Fmt(
                    "      <span class=\"legend-item\"><svg class=\"legend-key\" width=\"22\" height=\"10\" aria-hidden=\"true\">" +
                    "<line x1=\"1\" y1=\"5\" x2=\"21\" y2=\"5\" stroke=\"var(--series-{0})\" stroke-width=\"2\" " +
                    "stroke-linecap=\"round\"{1}/></svg><span>{2}{3}</span></span>",
                    s + 1, dash, Escape(sweeps[s].Label), suffix));
            }
        }
        sb.AppendLine("    </div>");

        sb.AppendLine("    <div class=\"chart-host\">");
        sb.AppendLine("      <div class=\"chart-scroll\">");
        sb.AppendLine(Fmt("        <svg id=\"chart\" viewBox=\"0 0 {0} {1}\" role=\"img\" aria-label=\"{2}\">",
            Width, Height,
            Escape("Line chart of surface temperature against CO2 concentration. Both model " +
                   "configurations run nearly straight while their logarithmic expectations flatten.")));
        sb.Append(svg);
        sb.AppendLine("        </svg>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"tip\" id=\"tip\" aria-hidden=\"true\"></div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </section>");

        // The finding, stated from the numbers rather than asserted in prose.
        sb.AppendLine("  <section>");
        sb.AppendLine("    <h2>The curves bend the wrong way</h2>");
        sb.AppendLine("    <p>A logarithmic response flattens on a linear axis. These run almost " +
                      "straight, because <code>--co2-fraction</code> leaves a small CO₂ slice " +
                      "growing linearly against a large fixed remainder — so each doubling adds " +
                      "twice the absolute optical depth the last did, and forcing per doubling grows " +
                      "instead of holding constant.</p>");
        sb.AppendLine("    <div class=\"callouts\">");
        foreach (var s in sweeps)
        {
            sb.AppendLine("      <div class=\"callout\">");
            sb.AppendLine(Fmt("        <span class=\"callout-label\">{0}</span>", Escape(s.Label)));
            sb.AppendLine(Fmt("        <span class=\"callout-value\">+{0:F1} K</span>", s.Warming(last)));
            sb.AppendLine(Fmt("        <span class=\"callout-note\">against +{0:F1} K expected — {1:F1}× over</span>",
                s.Expected(last) - s.BaseTemperature,
                s.Warming(last) / (s.Expected(last) - s.BaseTemperature)));
            sb.AppendLine("      </div>");
        }
        sb.AppendLine("      <div class=\"callout\">");
        sb.AppendLine(Fmt("        <span class=\"callout-label\">Forcing to {0:F0} ppm</span>",
            Co2Sweep.Concentrations[last]));
        sb.AppendLine(Fmt("        <span class=\"callout-value\">{0:F1} W/m²</span>",
            sweeps[0].Forcings[last]));
        sb.AppendLine(Fmt("        <span class=\"callout-note\">against {0:F1} W/m² accepted — {1:F1}× over</span>",
            sweeps[0].AcceptedForcing(last),
            sweeps[0].Forcings[last] / sweeps[0].AcceptedForcing(last)));
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine(Fmt("    <p>Agreement is {0:F3} K at the {1:F0} ppm calibration point and " +
                      "{2:F2} K by {3:F0} ppm. Treat anything much past 600 ppm as unsound " +
                      "unless <code>--co2-fraction</code> is recalibrated where you need it.</p>",
            Math.Abs(sweeps[0].Overshoot(Co2Sweep.CalibrationIndex)),
            Co2Sweep.Concentrations[Co2Sweep.CalibrationIndex],
            sweeps[0].Overshoot(last), Co2Sweep.Concentrations[last]));
        sb.AppendLine("  </section>");

        // Table view - the WCAG-clean twin of the chart.
        sb.AppendLine("  <section class=\"card\">");
        sb.AppendLine("    <div class=\"table-scroll\">");
        sb.AppendLine("      <table>");
        sb.AppendLine("        <caption>Every plotted value, straight from the model. Temperatures in K.</caption>");
        sb.AppendLine("        <thead><tr><th scope=\"col\">CO₂ (ppm)</th>");
        foreach (var s in sweeps)
        {
            sb.AppendLine(Fmt("          <th scope=\"col\">dry τ</th><th scope=\"col\">{0}</th><th scope=\"col\">Expected</th>",
                Escape(s.Label)));
        }
        sb.AppendLine("        </tr></thead>");
        sb.AppendLine("        <tbody>");
        for (int i = 0; i < Co2Sweep.Concentrations.Length; i++)
        {
            sb.Append(Fmt("          <tr><td>{0:N0}</td>", Co2Sweep.Concentrations[i]));
            foreach (var s in sweeps)
            {
                sb.Append(Fmt("<td>{0:F3}</td><td>{1:F3}</td><td>{2:F3}</td>",
                    s.Points[i].DryOpticalDepth, s.Points[i].SurfaceTemperature, s.Expected(i)));
            }
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("        </tbody>");
        sb.AppendLine("      </table>");
        sb.AppendLine("    </div>");
        sb.AppendLine("  </section>");

        sb.AppendLine("  <section>");
        sb.AppendLine("    <h2>Which &ldquo;forcing&rdquo; this is</h2>");
        sb.AppendLine("    <p>Three different quantities go by that name in this model, and mixing " +
                      "them up is easy. The figures above are <strong>instantaneous, measured " +
                      "against the reference equilibrium</strong> &mdash; the only definition " +
                      "comparable to 5.35&nbsp;ln(C/C&#8320;).</p>");
        sb.AppendLine("    <p>The stepwise values <code>--co2-scenario</code> prints are each taken " +
                      "against the <em>previous</em> equilibrium, which is warmer every step, so they " +
                      "must not be summed and held against that law. And the saturation quoted in the " +
                      "README &mdash; 54.3 then 28.8&nbsp;W/m&sup2; &mdash; is a third measure, at " +
                      "fixed standard-atmosphere temperatures with the whole absorber scaling. That " +
                      "is why the undiluted model saturates there while the diluted one over-forces " +
                      "here.</p>");
        sb.AppendLine("  </section>");

        sb.AppendLine("  <section>");
        sb.AppendLine("    <h2>Reproducing this</h2>");
        foreach (var s in sweeps)
        {
            sb.AppendLine(Fmt("    <h3>{0}</h3>", Escape(s.Label)));
            sb.AppendLine(Fmt("    <pre><code>dotnet run --project src/ClimateColumn.Cli -- \\\n  {0} \\\n  --co2-scenario {1}</code></pre>",
                Escape(s.Command),
                string.Join(",", Co2Sweep.Concentrations.Select(c => c.ToString("F0", Inv)))));
        }
        sb.AppendLine("  </section>");

        sb.AppendLine("  <footer>");
        sb.AppendLine(Fmt("    Forcings are instantaneous and measured against the {0:F0} ppm equilibrium, " +
                      "the only definition comparable to 5.35 ln(C/C₀). Each expectation curve uses " +
                      "its own configuration's d<em>T</em>/d<em>F</em> — {1} — measured at the " +
                      "calibration point.",
            Co2Sweep.Concentrations[0],
            string.Join(" and ", sweeps.Select(s => s.Sensitivity.ToString("F3", Inv))) +
            " K per W/m²"));
        sb.AppendLine("  </footer>");

        sb.AppendLine(HoverData(sweeps, scale));
        sb.AppendLine("</div>");
        sb.AppendLine(HoverScript());

        return sb.ToString();
    }

    /// <summary>
    /// The series values and plot geometry, as JSON in a script tag. Kept out of a JS string
    /// literal so nothing has to be escaped twice.
    /// </summary>
    private static string HoverData(Co2Sweep[] sweeps, ChartScale scale)
    {
        var series = new List<string>();
        for (int s = 0; s < sweeps.Length; s++)
        {
            series.Add(Fmt(
                "{{\"id\":\"{0}-model\",\"label\":\"{1}\",\"slot\":{0},\"dash\":false,\"values\":[{2}]}}",
                s + 1, Escape(sweeps[s].Label),
                string.Join(",", sweeps[s].Points.Select(p => p.SurfaceTemperature.ToString("F4", Inv)))));

            series.Add(Fmt(
                "{{\"id\":\"{0}-expected\",\"label\":\"{1}, expected\",\"slot\":{0},\"dash\":true,\"values\":[{2}]}}",
                s + 1, Escape(sweeps[s].Label),
                string.Join(",", Enumerable.Range(0, sweeps[s].Points.Count)
                    .Select(i => sweeps[s].Expected(i).ToString("F4", Inv)))));
        }

        return Fmt(
            "  <script id=\"chart-data\" type=\"application/json\">" +
            "{{\"ppm\":[{0}],\"scale\":{{\"left\":{1},\"top\":{2},\"w\":{3},\"h\":{4},\"vbw\":{5}," +
            "\"xMin\":{6:F1},\"xMax\":{7:F1},\"yMin\":{8:F1},\"yMax\":{9:F1}}},\"series\":[{10}]}}" +
            "</script>",
            string.Join(",", Co2Sweep.Concentrations.Select(c => c.ToString("F0", Inv))),
            scale.Left, scale.Top, scale.PlotWidth, scale.PlotHeight, scale.ViewBoxWidth,
            scale.XMin, scale.XMax, scale.YMin, scale.YMax,
            string.Join(",", series));
    }

    /// <summary>
    /// Crosshair and tooltip. Every value is also on an end label or in the table, so this
    /// enhances rather than gates - nothing is reachable only by hovering.
    /// </summary>
    private static string HoverScript() => """
        <script>
          (() => {
            const data = JSON.parse(document.getElementById('chart-data').textContent);
            const { ppm, scale, series } = data;
            const svg = document.getElementById('chart');
            const crosshair = document.getElementById('crosshair');
            const hit = document.getElementById('hit');
            const tip = document.getElementById('tip');
            const host = document.querySelector('.chart-host');
            const dots = new Map([...document.querySelectorAll('.hover-dot')]
              .map(d => [d.dataset.series, d]));

            const x = c => scale.left + (c - scale.xMin) / (scale.xMax - scale.xMin) * scale.w;
            const y = t => scale.top + (scale.yMax - t) / (scale.yMax - scale.yMin) * scale.h;

            const show = (clientX, clientY) => {
              const box = svg.getBoundingClientRect();
              const at = scale.xMin +
                ((clientX - box.left) / box.width * scale.vbw - scale.left) / scale.w *
                (scale.xMax - scale.xMin);

              let idx = 0;
              ppm.forEach((c, i) => {
                if (Math.abs(c - at) < Math.abs(ppm[idx] - at)) idx = i;
              });

              const px = x(ppm[idx]);
              crosshair.setAttribute('x1', px);
              crosshair.setAttribute('x2', px);
              crosshair.setAttribute('opacity', 1);

              series.forEach(s => {
                const dot = dots.get(s.id);
                if (!dot) return;
                dot.setAttribute('cx', px);
                dot.setAttribute('cy', y(s.values[idx]));
                dot.setAttribute('opacity', 1);
              });

              tip.innerHTML =
                '<div class="tip-head">' + ppm[idx].toLocaleString() + ' ppm</div>' +
                series.map(s =>
                  '<div class="tip-row">' +
                    '<svg width="14" height="8" aria-hidden="true"><line x1="0" y1="4" x2="14" y2="4" ' +
                      'stroke="var(--series-' + s.slot + ')" stroke-width="2" stroke-linecap="round"' +
                      (s.dash ? ' stroke-dasharray="5 3"' : '') + '/></svg>' +
                    '<span>' + s.label + '</span>' +
                    '<span class="tip-val">' + s.values[idx].toFixed(2) + ' K</span>' +
                  '</div>').join('');

              tip.classList.add('on');
              const hostBox = host.getBoundingClientRect();
              let left = clientX - hostBox.left + 16;
              if (left + tip.offsetWidth > hostBox.width) left = clientX - hostBox.left - tip.offsetWidth - 16;
              tip.style.left = Math.max(0, left) + 'px';
              tip.style.top = Math.max(0, clientY - hostBox.top - 20) + 'px';
            };

            const hide = () => {
              crosshair.setAttribute('opacity', 0);
              dots.forEach(d => d.setAttribute('opacity', 0));
              tip.classList.remove('on');
            };

            hit.addEventListener('mousemove', e => show(e.clientX, e.clientY));
            hit.addEventListener('mouseleave', hide);
            hit.addEventListener('touchmove', e => {
              const t = e.touches[0];
              if (t) show(t.clientX, t.clientY);
            }, { passive: true });
            hit.addEventListener('touchend', hide);
          })();
        </script>
        """;

    /// <summary>
    /// Theme tokens for all three viewer states: bare :root carries the complete light
    /// palette, the media query covers an unstamped document under an OS dark setting, and
    /// the [data-theme] scope lets an explicit toggle win in both directions.
    /// </summary>
    private static string Style() => """
        <style>
          :root {
            color-scheme: light;
            --surface: #fcfcfb; --plane: #f9f9f7;
            --ink: #0b0b0b; --ink-2: #52514e; --muted: #898781;
            --grid: #e1e0d9; --axis: #c3c2b7;
            --hairline: rgba(11,11,11,0.10);
            --series-1: #2a78d6; --series-2: #eb6834;
            --warn-wash: rgba(235,104,52,0.08); --warn-edge: rgba(235,104,52,0.32);
          }
          @media (prefers-color-scheme: dark) {
            :root:where(:not([data-theme="light"])) {
              color-scheme: dark;
              --surface: #1a1a19; --plane: #0d0d0d;
              --ink: #ffffff; --ink-2: #c3c2b7; --muted: #898781;
              --grid: #2c2c2a; --axis: #383835;
              --hairline: rgba(255,255,255,0.10);
              --series-1: #3987e5; --series-2: #d95926;
              --warn-wash: rgba(217,89,38,0.12); --warn-edge: rgba(217,89,38,0.40);
            }
          }
          :root[data-theme="dark"] {
            color-scheme: dark;
            --surface: #1a1a19; --plane: #0d0d0d;
            --ink: #ffffff; --ink-2: #c3c2b7; --muted: #898781;
            --grid: #2c2c2a; --axis: #383835;
            --hairline: rgba(255,255,255,0.10);
            --series-1: #3987e5; --series-2: #d95926;
            --warn-wash: rgba(217,89,38,0.12); --warn-edge: rgba(217,89,38,0.40);
          }
          * { box-sizing: border-box; }
          body {
            background: var(--plane); color: var(--ink);
            font-family: system-ui, -apple-system, "Segoe UI", sans-serif;
            line-height: 1.6; margin: 0; padding: 40px 24px 72px;
          }
          .wrap { max-width: 940px; margin: 0 auto; display: flex; flex-direction: column; gap: 32px; }
          header { display: flex; flex-direction: column; gap: 8px; }
          .eyebrow {
            font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
            font-size: 12px; letter-spacing: 0.08em; text-transform: uppercase; color: var(--muted);
          }
          h1 { font-size: clamp(26px, 4vw, 34px); line-height: 1.2; font-weight: 600; margin: 0; text-wrap: balance; }
          .lede { color: var(--ink-2); font-size: 17px; margin: 0; max-width: 65ch; }
          .card { background: var(--surface); border: 1px solid var(--hairline); border-radius: 10px; padding: 24px; }
          .legend { display: flex; flex-wrap: wrap; gap: 8px 24px; margin-bottom: 20px; }
          .legend-item { display: flex; align-items: center; gap: 8px; font-size: 13px; color: var(--ink-2); }
          .chart-host { position: relative; }
          .chart-scroll { overflow-x: auto; }
          /* Scoped to the chart: a bare `svg` rule would also stretch the legend and
             tooltip keys to full width. */
          #chart { display: block; width: 100%; height: auto; min-width: 620px; }
          .legend-key, .tip-row svg { display: block; flex: none; }
          .tip {
            position: absolute; pointer-events: none; z-index: 5; min-width: 210px;
            background: var(--surface); border: 1px solid var(--hairline); border-radius: 8px;
            padding: 10px 12px; font-size: 12px; box-shadow: 0 4px 16px rgba(0,0,0,0.12);
            opacity: 0; transition: opacity 0.12s;
          }
          .tip.on { opacity: 1; }
          .tip-head {
            font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
            font-weight: 600; color: var(--ink); margin-bottom: 6px;
          }
          .tip-row { display: flex; align-items: center; gap: 8px; color: var(--ink-2); line-height: 1.7; }
          .tip-val {
            margin-left: auto; color: var(--ink);
            font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
            font-variant-numeric: tabular-nums;
          }
          @media (prefers-reduced-motion: reduce) { .tip { transition: none; } }
          .tick {
            font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
            font-size: 11px; fill: var(--muted); font-variant-numeric: tabular-nums;
          }
          .axis-title { font-size: 12px; fill: var(--ink-2); letter-spacing: 0.02em; }
          .end-label {
            font-size: 12px; font-weight: 600; fill: var(--ink);
            font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
          }
          .end-label-sub { font-size: 11px; font-weight: 400; fill: var(--muted); }
          h2 { font-size: 20px; font-weight: 600; margin: 0 0 12px; text-wrap: balance; }
          h3 { font-size: 15px; font-weight: 600; margin: 0 0 8px; }
          p { margin: 0 0 14px; max-width: 65ch; color: var(--ink-2); }
          p:last-child { margin-bottom: 0; }
          .wrap :is(h2, h3) + p { margin-top: 0; }
          .callouts {
            display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
            gap: 16px; margin: 20px 0;
          }
          .callout {
            background: var(--warn-wash); border: 1px solid var(--warn-edge); border-radius: 8px;
            padding: 14px 16px; display: flex; flex-direction: column; gap: 2px;
          }
          .callout-label { font-size: 12px; color: var(--ink-2); }
          .callout-value { font-size: 26px; font-weight: 600; color: var(--ink); line-height: 1.15; }
          .callout-note { font-size: 12px; color: var(--muted); }
          table { width: 100%; border-collapse: collapse; font-size: 13px; font-variant-numeric: tabular-nums; }
          caption { text-align: left; color: var(--muted); font-size: 12px; padding-bottom: 10px; }
          th, td { padding: 7px 10px; text-align: right; border-bottom: 1px solid var(--grid); }
          th { color: var(--ink-2); font-weight: 600; font-size: 12px; border-bottom-color: var(--axis); }
          th:first-child, td:first-child { text-align: left; }
          td { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; color: var(--ink); }
          tbody tr:last-child td { border-bottom: none; }
          .table-scroll { overflow-x: auto; }
          pre {
            background: var(--plane); border: 1px solid var(--hairline); border-radius: 8px;
            padding: 14px 16px; overflow-x: auto; margin: 0 0 12px; font-size: 12.5px; line-height: 1.65;
          }
          code { font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; color: var(--ink); }
          p code {
            background: var(--plane); border: 1px solid var(--hairline);
            border-radius: 4px; padding: 1px 5px; font-size: 0.9em;
          }
          footer {
            color: var(--muted); font-size: 13px;
            border-top: 1px solid var(--hairline); padding-top: 20px;
          }
        </style>
        """;

    private static string Fmt(string format, params object[] args) =>
        string.Format(Inv, format, args);

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
