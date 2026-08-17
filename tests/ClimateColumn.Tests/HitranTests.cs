using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The band approximations checked against <em>real</em> spectral data: HITRAN's CO2 15 um band,
/// the transition that does the actual greenhouse work.
/// </summary>
/// <remarks>
/// These are the only tests in the project that compare against something measured rather than
/// derived. Everything else - closed forms, budgets, even the synthetic line-by-line reference -
/// checks that the model is self-consistent or that a method is implemented correctly. None of
/// it can say whether the approximation resembles a real gas.
///
/// The data is not committed, so these skip rather than fail when it is absent. Fetch it with
/// <c>scripts/fetch-hitran.ps1</c>. Keeping it out of the repository is what lets the suite
/// still run with no network at all.
/// </remarks>
[TestClass]
public class HitranTests
{
    private static LineByLineBand? _band;

    /// <summary>
    /// The path to the downloaded list, or a skip. Every test goes through here, so that a
    /// missing download is always reported as "not run" rather than as a failure - reaching for
    /// the path directly is how one of these accidentally became a hard failure offline.
    /// </summary>
    private static string RequirePath()
    {
        string? path = HitranLineList.DefaultPath();
        if (path is null)
        {
            Assert.Inconclusive(
                "No HITRAN data. Run scripts/fetch-hitran.ps1 to download the CO2 15 um band; " +
                "these tests compare the band approximations against real lines.");
        }
        return path!;
    }

    /// <summary>
    /// The CO2 band core, resolved. Built once: 10,000 lines against 60,000 samples is the
    /// expensive part, and it is deterministic.
    /// </summary>
    private static LineByLineBand Band
    {
        get
        {
            if (_band is not null) return _band;

            // Weak lines are dropped: below 1e-27 they are a long tail that costs time and
            // changes nothing. The 640-700 cm^-1 core is where the band actually absorbs.
            var lines = HitranLineList.Load(RequirePath(), minimumIntensity: 1e-27);
            _band = LineByLineBand.FromLines(lines, 640, 700, 60_000, wingCutoff: 25.0);
            return _band;
        }
    }

    private static readonly double[] OpticalDepths = { 0.1, 0.3, 1.0, 3.0, 10.0, 30.0 };

    [TestMethod]
    public void RealLineListLoadsAndSpansOrdersOfMagnitude()
    {
        var k = Band.AbsorptionCoefficients();

        double mean = 0.0, min = double.MaxValue, max = double.MinValue;
        foreach (double value in k)
        {
            mean += value / k.Length;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        Assert.AreEqual(1.0, mean, 1e-9, "the band mean should be normalised to one");
        Assert.IsTrue(max / min > 100.0,
            $"a real band should span orders of magnitude ({min:E2} to {max:E2}, ratio {max / min:E2})");
    }

    [TestMethod]
    public void IntensityCutoffKeepsTheLinesThatMatter()
    {
        string path = RequirePath();
        var all = HitranLineList.Load(path);
        var strong = HitranLineList.Load(path, minimumIntensity: 1e-27);

        Assert.IsTrue(strong.Count < all.Count,
            "the cutoff should actually drop something");
        Assert.IsTrue(strong.Count > 1000,
            $"but should leave a properly resolved band ({strong.Count} lines)");
    }

    /// <summary>
    /// A grey band against real CO2. This is the number that justifies everything downstream of
    /// it, and it is much worse than "somewhat too opaque".
    /// </summary>
    [DataTestMethod]
    [DataRow(0.3)]
    [DataRow(1.0)]
    [DataRow(3.0)]
    [DataRow(10.0)]
    public void GreyBandIsBadlyWrongAgainstRealCo2(double opticalDepth)
    {
        double reference = Band.Transmission(opticalDepth);
        double grey = Math.Exp(-opticalDepth);

        Assert.IsTrue(grey < reference,
            $"tau = {opticalDepth}: grey must under-transmit ({grey:F5} vs {reference:F5})");
        Assert.IsTrue(reference - grey > 0.02,
            $"tau = {opticalDepth}: the grey error against real CO2 should be substantial " +
            $"(line-by-line {reference:F5}, grey {grey:F5})");
    }

    /// <summary>
    /// The band's own measured k-distribution against real lines. This is the approach that
    /// works, and it is what <see cref="ModelOptions.MeasuredKDistribution"/> exists to carry.
    /// </summary>
    [DataTestMethod]
    [DataRow(8, 0.03)]
    [DataRow(16, 0.012)]
    [DataRow(32, 0.005)]
    public void MeasuredKDistributionReproducesRealCo2(int points, double tolerance)
    {
        var quadrature = Band.ToKDistribution(points);

        foreach (double tau in OpticalDepths)
        {
            Assert.AreEqual(Band.Transmission(tau), quadrature.Transmission(tau), tolerance,
                $"{points} g-points at tau = {tau}");
        }
    }

    /// <summary>
    /// The awkward result, and the reason MeasuredKDistribution was added: the parametric
    /// families cannot represent a real band across a range of optical depths.
    /// </summary>
    /// <remarks>
    /// Fitting a lognormal width to real CO2 gives a different answer at every optical depth -
    /// about 1.7 where the band is thin, about 1.25 where it is thick - because a real band's
    /// k-distribution simply is not lognormal. Any single choice of --k-width is therefore a
    /// compromise, and the test pins the drift so the limitation cannot quietly be forgotten.
    /// </remarks>
    [TestMethod]
    public void NoSingleParametricWidthFitsRealCo2()
    {
        double BestWidth(double tau)
        {
            double reference = Band.Transmission(tau);
            double bestWidth = 0.0, bestError = double.MaxValue;

            for (double width = 0.25; width <= 8.0; width += 0.05)
            {
                double error = Math.Abs(
                    KDistribution.Build(KDistributionShape.Lognormal, width, 64).Transmission(tau) -
                    reference);

                if (error < bestError) { bestError = error; bestWidth = width; }
            }

            return bestWidth;
        }

        double thin = BestWidth(0.1);
        double thick = BestWidth(30.0);

        Assert.IsTrue(thin > thick + 0.2,
            $"the best-fit width should drift with optical depth ({thin:F2} thin, {thick:F2} thick); " +
            "if it did not, a single parametric width would be defensible");

        // And the best single width across the range is markedly worse than the measured
        // distribution, which is the whole argument for using real data when you have it.
        var references = OpticalDepths.Select(t => Band.Transmission(t)).ToArray();

        double bestRms = double.MaxValue;
        for (double width = 0.25; width <= 8.0; width += 0.05)
        {
            var candidate = KDistribution.Build(KDistributionShape.Lognormal, width, 64);
            double sum = 0.0;
            for (int i = 0; i < OpticalDepths.Length; i++)
            {
                double d = candidate.Transmission(OpticalDepths[i]) - references[i];
                sum += d * d;
            }
            bestRms = Math.Min(bestRms, Math.Sqrt(sum / OpticalDepths.Length));
        }

        var measured = Band.ToKDistribution(32);
        double measuredRms = Math.Sqrt(
            OpticalDepths.Select((t, i) => Math.Pow(measured.Transmission(t) - references[i], 2)).Sum() /
            OpticalDepths.Length);

        Assert.IsTrue(measuredRms < 0.25 * bestRms,
            $"a measured distribution should beat the best parametric fit by a wide margin " +
            $"(measured {measuredRms:F5}, best lognormal {bestRms:F5})");
    }

    /// <summary>
    /// The loop closed: a distribution measured from real CO2 lines drives the column model, and
    /// the column reaches equilibrium with it.
    /// </summary>
    [TestMethod]
    public void ColumnRunsOnAKDistributionMeasuredFromRealCo2()
    {
        var measured = Band.ToKDistribution(16);

        var options = new ModelOptions { MeasuredKDistribution = measured };
        var result = ColumnModel.RunToEquilibrium(options);

        Assert.IsTrue(result.Converged, "the column must reach equilibrium on real line data");
        Assert.IsTrue(result.SurfaceTemperature is > 200 and < 350,
            $"and stay physical ({result.SurfaceTemperature:F2} K)");

        // Real line structure lets more radiation out than a grey band of the same absorber, so
        // the surface settles cooler - the same conclusion the synthetic band reached, now from
        // measured lines.
        Assert.IsTrue(result.SurfaceTemperature < TestSupport.Default.SurfaceTemperature,
            $"real line structure should cool the surface relative to grey " +
            $"({result.SurfaceTemperature:F2} vs {TestSupport.Default.SurfaceTemperature:F2} K)");
    }

    [TestMethod]
    public void MeasuredDistributionTakesPrecedenceOverTheParametricShape()
    {
        var measured = Band.ToKDistribution(16);

        var options = new ModelOptions
        {
            KDistributionShape = KDistributionShape.Lognormal,
            KDistributionWidth = 2.0,
            KDistributionPoints = 8,
            MeasuredKDistribution = measured
        };

        var built = options.BuildKDistribution();

        Assert.AreEqual(measured.Points, built.Points,
            "the measured distribution should win over the parametric settings");
        for (int j = 0; j < measured.Points; j++)
        {
            Assert.AreEqual(measured.Multipliers[j], built.Multipliers[j], 0.0,
                $"sub-band {j} should come from the measured distribution");
        }
    }
}
