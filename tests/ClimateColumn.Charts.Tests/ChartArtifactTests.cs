using System.Drawing;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Charts.Tests;

/// <summary>
/// Produces the WinForms chart as a durable artifact, from live model output.
/// </summary>
/// <remarks>
/// The HTML chart has been generated and checked by a test since it existed, which is what stops
/// its numbers drifting from the model. The PNG had no equivalent: <see cref="SavePngTests"/>
/// exercises the same painter but writes throwaway files into a temp directory and uses synthetic
/// sweeps, so it proved the export worked without ever producing the real figure. This closes that
/// gap - same sweep, same painter, written to <c>artifacts/</c> beside the HTML one.
///
/// It runs the real sweep rather than synthetic numbers, so it costs a few seconds. That is the
/// price of the artifact being the real thing; the fast synthetic tests next door still cover the
/// export's edge cases.
/// </remarks>
[TestClass]
public class ChartArtifactTests
{
    public TestContext TestContext { get; set; } = null!;

    private static Co2Sweep[]? _sweeps;

    /// <summary>
    /// The two calibrated configurations at equilibrium. Computed once: this is 2 x 9 marches to
    /// equilibrium and by far the slowest thing here.
    /// </summary>
    private static Co2Sweep[] Sweeps
    {
        get
        {
            if (_sweeps is not null) return _sweeps;

            // Only the spectrally derived configuration is charted. A calibrated grey curve beside
            // it invited the figure to be read as a comparison of two models rather than as one
            // model against the forcing law it ought to follow.
            var spectral = Co2Sweep.SpectralBands();
            if (spectral is null)
            {
                Assert.Inconclusive(
                    "No HITRAN data, so there is no spectral sweep to chart. Run " +
                    "scripts/fetch-hitran.ps1 -Molecule all.");
            }

            _sweeps = new[] { spectral! };
            return _sweeps;
        }
    }

    /// <summary>
    /// Resolves <c>artifacts/</c> beside the solution, so the PNG lands next to the HTML chart
    /// rather than deep in a bin directory.
    /// </summary>
    private static string ArtifactsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "ClimateColumn.sln")))
        {
            directory = directory.Parent;
        }

        string root = directory?.FullName ?? AppContext.BaseDirectory;
        string artifacts = Path.Combine(root, "artifacts");
        Directory.CreateDirectory(artifacts);
        return artifacts;
    }

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [DataTestMethod]
    [DataRow("co2-response.png", false)]
    [DataRow("co2-response-dark.png", true)]
    public void RendersTheChartToTheArtifactsFolder(string fileName, bool dark)
    {
        string path = Path.Combine(ArtifactsDirectory(), fileName);

        Co2ChartExport.SavePng(path, Sweeps,
            dark ? ChartTheme.Dark : ChartTheme.Light, 1150, 720);

        Assert.IsTrue(File.Exists(path), $"nothing was written to {path}");

        var header = new byte[8];
        using (var stream = File.OpenRead(path)) stream.ReadExactly(header);
        CollectionAssert.AreEqual(PngSignature, header, "the file should be a PNG");

        using var image = new Bitmap(path);
        Assert.AreEqual(1150, image.Width);
        Assert.AreEqual(720, image.Height);

        TestContext.WriteLine($"chart written to {path}");
    }

    /// <summary>
    /// The artifact has to be the real chart, not a plausible-looking one. These are the same
    /// checks the HTML chart's test makes, so the two figures cannot silently diverge.
    /// </summary>
    [TestMethod]
    public void ArtifactCarriesTheModelsOwnNumbers()
    {
        int last = Co2Sweep.Concentrations.Length - 1;

        foreach (var sweep in Sweeps)
        {
            Assert.AreEqual(Co2Sweep.Concentrations.Length, sweep.Points.Count,
                $"{sweep.Label}: one point per swept concentration");

            foreach (var point in sweep.Points)
            {
                Assert.IsTrue(point.Converged,
                    $"{sweep.Label}: {point.Ppm:F0} ppm must be an equilibrium");
            }

            // The chart's headline claim, and it is not the one it used to be. This asserted
            // that the model overshoots the logarithmic expectation by more than a kelvin,
            // which it did while the far wings were pure Lorentzian and the forcing ran 1.33x
            // the accepted law. With the sub-Lorentzian correction the model tracks that law
            // closely and finishes slightly under it, so the assertion now pins the agreement
            // rather than the departure - and stays two-sided, because a model that drifted far
            // either way would be worth knowing about.
            Assert.IsTrue(Math.Abs(sweep.Overshoot(last)) < 1.5,
                $"{sweep.Label}: the model should track the logarithmic expectation to about a " +
                $"kelvin across the sweep (off by {sweep.Overshoot(last):F3} K at " +
                $"{Co2Sweep.Concentrations[last]:F0} ppm)");

            Assert.IsTrue(sweep.Warming(last) > 0,
                $"{sweep.Label}: warming should be positive ({sweep.Warming(last):F3} K)");
        }
    }

    // Whether the two themes actually differ in the pixels is covered by
    // SavePngTests.ThemeReachesThePixels, which uses synthetic sweeps and runs in milliseconds.
    // Repeating it here against the real sweep would add nothing, and writing the artifact files
    // twice at different sizes made the tests order-dependent - and deadlocked on GDI+, which
    // keeps a file open for the lifetime of any Bitmap read from it.
}
