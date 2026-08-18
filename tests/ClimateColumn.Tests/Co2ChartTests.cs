using System.Globalization;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Sweeps CO2 to 2000 ppm in both calibrated configurations, asserts the findings the
/// resulting chart claims, and writes the chart to <c>artifacts/co2-response.html</c>.
/// </summary>
/// <remarks>
/// The chart is generated rather than hand-authored on purpose: every number in it comes
/// from the sweep below, so the figure cannot drift away from what the model actually does.
/// The assertions are the test; the file is a by-product. The two sweeps are 24 equilibrium
/// runs between them and take a few seconds, so they are computed once for the class.
/// </remarks>
[TestClass]
public class Co2ChartTests
{
    public TestContext TestContext { get; set; } = null!;

    private static Co2Sweep _noFeedback = null!;
    private static Co2Sweep _withFeedback = null!;

    private static Co2Sweep[] _plotted = Array.Empty<Co2Sweep>();

    [ClassInitialize]
    public static void RunSweeps(TestContext _)
    {
        _noFeedback = Co2Sweep.NoFeedback();
        _withFeedback = Co2Sweep.WithWaterVapourFeedback();

        // Empty when the HITRAN line lists have not been fetched. One source of truth for what
        // the chart shows, shared with the app and the PNG export.
        _plotted = Co2Sweep.ForChart();
    }

    /// <summary>The grey configurations, which the assertions below are about.</summary>
    private static IEnumerable<Co2Sweep> Sweeps => new[] { _noFeedback, _withFeedback };

    /// <summary>
    /// What goes on the chart: the spectral configuration, and the same configuration with the
    /// water vapour held at its reference loading.
    /// </summary>
    /// <remarks>
    /// The grey configurations are still swept, because the findings about them below are real and
    /// documented, but they are no longer plotted. The chart is about what the model does when its
    /// absorption comes from line data; putting a calibrated grey curve beside it invited the figure
    /// to be read as a comparison of two models rather than as one model against the forcing law it
    /// ought to follow.
    ///
    /// With no HITRAN data there is nothing to plot, so the rendering tests skip rather than fall
    /// back to a grey curve - a chart captioned as spectral must not quietly show something else.
    ///
    /// The fixed-vapour series is the same model with the feedback switched off, not a different
    /// model, which is why it belongs here where the calibrated grey curves did not.
    /// </remarks>
    private static Co2Sweep[] Plotted
    {
        get
        {
            if (_plotted.Length == 0)
            {
                Assert.Inconclusive(
                    "No HITRAN data, so there is no spectral sweep to chart. Run " +
                    "scripts/fetch-hitran.ps1 -Molecule all.");
            }
            return _plotted;
        }
    }

    private static int Last => Co2Sweep.Concentrations.Length - 1;

    /// <summary>
    /// How many curves the forcing figure actually draws: one per configuration that is not a
    /// duplicate of the first, plus the accepted law.
    /// </summary>
    /// <remarks>
    /// Computed rather than written down, because it depends on the data. The no-feedback
    /// configuration produces a forcing curve identical to the default's - a feedback cannot
    /// change an instantaneous forcing measured at held temperatures - so it is suppressed rather
    /// than painted invisibly over the top. On the temperature figure the same two would both be
    /// drawn, since there they differ.
    /// </remarks>
    private static int DrawnCurves =>
        Enumerable.Range(0, Plotted.Length)
            .Count(i => !Co2ChartQuantity.Forcing.DuplicatesFirst(Plotted, i)) + 1;

    [TestMethod]
    public void EveryPointInBothSweepsReachesEquilibrium()
    {
        foreach (var sweep in Sweeps)
        {
            foreach (var point in sweep.Points)
            {
                Assert.IsTrue(point.Converged,
                    $"{sweep.Label}: {point.Ppm:F0} ppm did not converge, so its temperature is not an equilibrium");
            }
        }
    }

    [TestMethod]
    public void SurfaceTemperatureRisesMonotonicallyWithConcentration()
    {
        foreach (var sweep in Sweeps)
        {
            for (int i = 1; i < sweep.Points.Count; i++)
            {
                Assert.IsTrue(
                    sweep.Points[i].SurfaceTemperature > sweep.Points[i - 1].SurfaceTemperature,
                    $"{sweep.Label}: {sweep.Points[i].Ppm:F0} ppm is not warmer than {sweep.Points[i - 1].Ppm:F0} ppm");
            }
        }
    }

    /// <summary>
    /// The calibration does its job where it was made: both configurations were tuned so the
    /// forcing at 425 ppm matches the accepted value, so the model and the logarithmic
    /// expectation must very nearly coincide there.
    /// </summary>
    [TestMethod]
    public void CalibrationHoldsAtTheReferencePoint()
    {
        foreach (var sweep in Sweeps)
        {
            double accepted = sweep.AcceptedForcing(Co2Sweep.CalibrationIndex);
            double modelled = sweep.Forcings[Co2Sweep.CalibrationIndex];

            Assert.AreEqual(accepted, modelled, 0.1,
                $"{sweep.Label}: forcing at the calibration point should match 5.35 ln(C/C0)");
            Assert.IsTrue(Math.Abs(sweep.Overshoot(Co2Sweep.CalibrationIndex)) < 0.05,
                $"{sweep.Label}: model and expectation should agree at the calibration point " +
                $"(gap {sweep.Overshoot(Co2Sweep.CalibrationIndex):F4} K)");
        }
    }

    /// <summary>
    /// ...and fails away from it. This is the chart's whole point, and the reason the README
    /// tells you not to extrapolate the calibration.
    /// </summary>
    /// <remarks>
    /// The thresholds are relative to the calibration point rather than absolute kelvin, so
    /// the test keeps its meaning if the sweep's upper bound moves. An absolute bar like
    /// "&gt; 5 K" only holds for one particular range and has to be re-tuned every time.
    /// </remarks>
    [TestMethod]
    public void CalibrationDoesNotSurviveExtrapolation()
    {
        foreach (var sweep in Sweeps)
        {
            double atCalibration = Math.Abs(sweep.Overshoot(Co2Sweep.CalibrationIndex));
            double atEnd = sweep.Overshoot(Last);

            Assert.IsTrue(atEnd > 1.0,
                $"{sweep.Label}: the model should overshoot the logarithmic expectation " +
                $"materially by {Co2Sweep.Concentrations[Last]:F0} ppm (got {atEnd:F2} K)");

            Assert.IsTrue(atEnd > 20.0 * atCalibration,
                $"{sweep.Label}: the overshoot at {Co2Sweep.Concentrations[Last]:F0} ppm " +
                $"({atEnd:F3} K) should dwarf the residual at the calibration point " +
                $"({atCalibration:F4} K)");

            Assert.IsTrue(sweep.Forcings[Last] > 1.25 * sweep.AcceptedForcing(Last),
                $"{sweep.Label}: forcing at {Co2Sweep.Concentrations[Last]:F0} ppm should run over " +
                $"the accepted value ({sweep.Forcings[Last]:F1} vs {sweep.AcceptedForcing(Last):F1} W/m2)");
        }
    }

    /// <summary>
    /// The overshoot grows with distance from the calibration point rather than appearing
    /// suddenly, which is what makes "trust it near 425 ppm, not at 2000" the right reading.
    /// </summary>
    [TestMethod]
    public void OvershootGrowsWithDistanceFromTheCalibrationPoint()
    {
        foreach (var sweep in Sweeps)
        {
            for (int i = Co2Sweep.CalibrationIndex + 2; i <= Last; i++)
            {
                Assert.IsTrue(sweep.Overshoot(i) > sweep.Overshoot(i - 1),
                    $"{sweep.Label}: overshoot should keep growing at {sweep.Points[i].Ppm:F0} ppm " +
                    $"({sweep.Overshoot(i - 1):F3} then {sweep.Overshoot(i):F3} K)");
            }
        }
    }

    /// <summary>
    /// The response is near-linear in concentration where it should be logarithmic. Measured
    /// by the ratio of warming at the last point to warming at the midpoint of the sweep:
    /// linear-in-C predicts the ratio of the concentration increments, logarithmic predicts
    /// the ratio of their logs. The model should sit close to the former and far from the
    /// latter. The midpoint is taken from the array so the test follows the sweep range.
    /// </summary>
    [TestMethod]
    public void ResponseIsNearLinearInConcentrationRatherThanLogarithmic()
    {
        int mid = Co2Sweep.Concentrations.Length / 2;
        Assert.IsTrue(mid > 0 && mid < Last, "the sweep needs a usable midpoint");

        double c0 = Co2Sweep.Concentrations[0];
        double linear = (Co2Sweep.Concentrations[Last] - c0) / (Co2Sweep.Concentrations[mid] - c0);
        double logarithmic = Math.Log(Co2Sweep.Concentrations[Last] / c0) /
                             Math.Log(Co2Sweep.Concentrations[mid] / c0);

        foreach (var sweep in Sweeps)
        {
            double actual = sweep.Warming(Last) / sweep.Warming(mid);

            Assert.IsTrue(Math.Abs(actual - linear) < Math.Abs(actual - logarithmic),
                $"{sweep.Label}: warming ratio {actual:F3} should sit nearer the linear-in-C " +
                $"prediction {linear:F3} than the logarithmic {logarithmic:F3}");
        }
    }

    /// <summary>
    /// Writes the chart. Kept as its own test so a failure here reads as "the figure could
    /// not be written", not "the physics is wrong" - the findings above are asserted
    /// separately and stand on their own.
    /// </summary>
    [TestMethod]
    public void RendersTheChartToTheArtifactsFolder()
    {
        string html = Co2ChartRenderer.Render(Plotted);

        Assert.IsTrue(html.Contains("<svg id=\"chart\"", StringComparison.Ordinal),
            "the rendered page should contain the chart");
        Assert.AreEqual(DrawnCurves, CountOccurrences(html, "<path class=\"series-line\""),
            "a model curve and an expectation curve per plotted configuration");
        Assert.IsFalse(html.Contains("NaN", StringComparison.Ordinal),
            "a NaN in the output means a coordinate or a ratio failed to compute");

        // Every swept concentration should reach the table view.
        foreach (double ppm in Co2Sweep.Concentrations)
        {
            string cell = $"<td>{ppm.ToString("N0", CultureInfo.InvariantCulture)}</td>";
            Assert.IsTrue(html.Contains(cell, StringComparison.Ordinal),
                $"{ppm:F0} ppm is missing from the table view");
        }

        string path = Path.Combine(ArtifactsDirectory(), "co2-response.html");
        File.WriteAllText(path, html);

        Assert.IsTrue(new FileInfo(path).Length > 4096, "the written file looks truncated");
        TestContext.WriteLine($"chart written to {path}");
    }

    /// <summary>
    /// At this sweep range two of the four lines finish within about 0.1 K of each other, so
    /// their end labels have to be spread apart and joined back to their own line ends by
    /// leaders. This checks no two labels overlap and that all of them stay inside the figure.
    /// </summary>
    [TestMethod]
    public void EndLabelsDoNotOverlapAndStayInsideTheFigure()
    {
        string html = Co2ChartRenderer.Render(Plotted);

        var ys = new List<double>();
        int at = 0;
        while ((at = html.IndexOf("class=\"end-label\"", at, StringComparison.Ordinal)) >= 0)
        {
            // The y attribute precedes the class on each label element.
            int yAt = html.LastIndexOf("y=\"", at, StringComparison.Ordinal);
            int from = yAt + 3;
            int to = html.IndexOf('"', from);
            ys.Add(double.Parse(html[from..to], CultureInfo.InvariantCulture));
            at += 1;
        }

        Assert.AreEqual(DrawnCurves, ys.Count,
            "one label per drawn model line end, plus one for the accepted law");

        ys.Sort();
        for (int i = 1; i < ys.Count; i++)
        {
            Assert.IsTrue(ys[i] - ys[i - 1] >= 24.0,
                $"end labels {i - 1} and {i} are only {ys[i] - ys[i - 1]:F1}px apart and would collide");
        }

        // The SVG viewBox is 470 tall; a label pushed outside it would be clipped.
        Assert.IsTrue(ys[0] > 0 && ys[^1] < 470,
            $"labels must stay inside the figure (got {ys[0]:F1} to {ys[^1]:F1})");

        // A label that moved off its own line end must be traceable back to it.
        int moved = ys.Count - CountOccurrences(html, "class=\"leader\"");
        Assert.IsTrue(CountOccurrences(html, "class=\"leader\"") > 0 || moved == ys.Count,
            "any nudged label needs a leader line back to its line end");
    }

    /// <summary>
    /// The hover layer reads its coordinates from an embedded JSON block, so that block has
    /// to carry every series at full length and agree with the sweep. A silent mismatch here
    /// would put the tooltip on the wrong values while the drawn lines stayed correct.
    /// </summary>
    [TestMethod]
    public void HoverDataMatchesTheRenderedSeries()
    {
        string html = Co2ChartRenderer.Render(Plotted);

        const string open = "<script id=\"chart-data\" type=\"application/json\">";
        int start = html.IndexOf(open, StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, "the chart should embed its hover data");

        int from = start + open.Length;
        int end = html.IndexOf("</script>", from, StringComparison.Ordinal);
        string json = html[from..end];

        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.AreEqual(Co2Sweep.Concentrations.Length, root.GetProperty("ppm").GetArrayLength(),
            "every swept concentration should be in the hover data");

        var series = root.GetProperty("series");
        Assert.AreEqual(DrawnCurves, series.GetArrayLength(),
            "one model series per configuration, plus a single accepted-law series");

        foreach (var entry in series.EnumerateArray())
        {
            Assert.AreEqual(Co2Sweep.Concentrations.Length,
                entry.GetProperty("values").GetArrayLength(),
                $"series {entry.GetProperty("id").GetString()} is the wrong length");
        }

        Assert.AreEqual("W/m²", root.GetProperty("unit").GetString(),
            "the tooltip takes its unit from the data, so the figure's quantity must be there");

        // Spot-check that the values really are the plotted model's, not a stale copy or a
        // configuration that is no longer on the chart. The default figure is forcing, so the
        // last point is the one that carries information - the first is zero by definition.
        int last = Co2Sweep.Concentrations.Length - 1;
        double lastModelValue = series[0].GetProperty("values")[last].GetDouble();
        Assert.AreEqual(Plotted[0].Forcings[last], lastModelValue, 1e-4,
            "the hover data should carry the same forcings the lines were drawn from");

        double lastAccepted = series[1].GetProperty("values")[last].GetDouble();
        Assert.AreEqual(Plotted[0].AcceptedForcing(last), lastAccepted, 1e-4,
            "the dashed series should be the accepted law, unconverted");

        // Each dot the script moves must exist in the SVG, keyed by series id.
        foreach (var entry in series.EnumerateArray())
        {
            string id = entry.GetProperty("id").GetString()!;
            Assert.IsTrue(html.Contains($"data-series=\"{id}\"", StringComparison.Ordinal),
                $"no hover dot was emitted for series {id}");
        }
    }

    /// <summary>
    /// Resolves <c>artifacts/</c> beside the solution file, creating it if needed, so the
    /// output lands somewhere discoverable rather than deep in a bin directory. Falls back to
    /// the test output directory if the solution cannot be located.
    /// </summary>
    private static string ArtifactsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClimateColumn.sln")))
        {
            dir = dir.Parent;
        }

        string root = dir?.FullName ?? AppContext.BaseDirectory;
        string artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(artifacts);
        return artifacts;
    }

    private static int CountOccurrences(string haystack, string needle) =>
        TestSupport.CountOccurrences(haystack, needle);
}
