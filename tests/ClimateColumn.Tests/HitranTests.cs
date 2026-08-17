using System.Collections.Concurrent;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The band approximations checked against <em>real</em> spectral data: HITRAN's CO2 15 um band
/// and H2O pure rotational band - between them, most of what absorbs longwave in the atmosphere.
/// </summary>
/// <remarks>
/// These are the only tests in the project that compare against something measured rather than
/// derived. Everything else - closed forms, budgets, even the synthetic line-by-line reference -
/// checks that the model is self-consistent or that a method is implemented correctly. None of
/// it can say whether an approximation resembles a real gas.
///
/// The data is not committed, so these skip rather than fail when it is absent. Fetch it with
/// <c>scripts/fetch-hitran.ps1</c> and <c>-Molecule h2o-rotational</c>. Keeping it out of the
/// repository is what lets the suite still run with no network at all.
/// </remarks>
[TestClass]
public class HitranTests
{
    /// <summary>A band worth resolving: which file, and over what interval.</summary>
    private sealed record Target(string File, string Name, double From, double To);

    private static readonly Target CarbonDioxide =
        new(HitranLineList.Co2FifteenMicron, "CO2 15 um", 640, 700);

    private static readonly Target WaterVapour =
        new(HitranLineList.WaterVapourRotational, "H2O rotational", 200, 400);

    private static readonly ConcurrentDictionary<string, LineByLineBand> Bands = new();

    private static readonly double[] OpticalDepths = { 0.1, 0.3, 1.0, 3.0, 10.0, 30.0 };

    private static Target Resolve(string file) =>
        file == CarbonDioxide.File ? CarbonDioxide : WaterVapour;

    /// <summary>
    /// The path to a downloaded list, or a skip. Every test goes through here, so a missing
    /// download is always reported as "not run" rather than as a failure.
    /// </summary>
    private static string RequirePath(string file)
    {
        string? path = HitranLineList.DefaultPath(file);
        if (path is null)
        {
            Assert.Inconclusive(
                $"No HITRAN data at data/{file}. Run scripts/fetch-hitran.ps1 (add " +
                "-Molecule h2o-rotational for water vapour); these tests compare the band " +
                "approximations against real lines.");
        }
        return path!;
    }

    /// <summary>
    /// A resolved band, built once per file. Weak lines are dropped: below 1e-27 they are a long
    /// tail that costs time and changes nothing.
    /// </summary>
    private static LineByLineBand Band(Target target)
    {
        string path = RequirePath(target.File);

        return Bands.GetOrAdd(target.File, _ =>
        {
            var lines = HitranLineList.Load(path, minimumIntensity: 1e-27);
            return LineByLineBand.FromLines(lines, target.From, target.To, 60_000, wingCutoff: 25.0);
        });
    }

    /// <summary>Best-fitting lognormal width for a band at one optical depth.</summary>
    private static (double Width, double Error) BestWidth(LineByLineBand band, double tau)
    {
        double reference = band.Transmission(tau);
        double bestWidth = 0.0, bestError = double.MaxValue;

        for (double width = 0.25; width <= 8.0; width += 0.05)
        {
            double error = Math.Abs(
                KDistribution.Build(KDistributionShape.Lognormal, width, 64).Transmission(tau) -
                reference);

            if (error < bestError) { bestError = error; bestWidth = width; }
        }

        return (bestWidth, bestError);
    }

    // ---------------------------------------------------------------- both molecules

    [DataTestMethod]
    [DataRow(HitranLineList.Co2FifteenMicron)]
    [DataRow(HitranLineList.WaterVapourRotational)]
    public void RealBandSpansOrdersOfMagnitude(string file)
    {
        var target = Resolve(file);
        var k = Band(target).AbsorptionCoefficients();

        double mean = 0.0, min = double.MaxValue, max = double.MinValue;
        foreach (double value in k)
        {
            mean += value / k.Length;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        Assert.AreEqual(1.0, mean, 1e-9, $"{target.Name}: the band mean should be normalised to one");
        Assert.IsTrue(max / min > 100.0,
            $"{target.Name}: a real band should span orders of magnitude " +
            $"({min:E2} to {max:E2}, ratio {max / min:E2})");
    }

    [DataTestMethod]
    [DataRow(HitranLineList.Co2FifteenMicron, 1.0)]
    [DataRow(HitranLineList.Co2FifteenMicron, 3.0)]
    [DataRow(HitranLineList.WaterVapourRotational, 1.0)]
    [DataRow(HitranLineList.WaterVapourRotational, 3.0)]
    public void GreyBandIsBadlyWrongAgainstRealGases(string file, double opticalDepth)
    {
        var target = Resolve(file);
        double reference = Band(target).Transmission(opticalDepth);
        double grey = Math.Exp(-opticalDepth);

        Assert.IsTrue(grey < reference,
            $"{target.Name} at tau = {opticalDepth}: grey must under-transmit " +
            $"({grey:F5} vs {reference:F5})");
        Assert.IsTrue(reference - grey > 0.05,
            $"{target.Name} at tau = {opticalDepth}: the grey error should be substantial " +
            $"(line-by-line {reference:F5}, grey {grey:F5})");
    }

    [DataTestMethod]
    [DataRow(HitranLineList.Co2FifteenMicron, 16, 0.012)]
    [DataRow(HitranLineList.Co2FifteenMicron, 32, 0.005)]
    [DataRow(HitranLineList.WaterVapourRotational, 16, 0.015)]
    [DataRow(HitranLineList.WaterVapourRotational, 32, 0.006)]
    public void MeasuredKDistributionReproducesRealGases(string file, int points, double tolerance)
    {
        var target = Resolve(file);
        var band = Band(target);
        var quadrature = band.ToKDistribution(points);

        foreach (double tau in OpticalDepths)
        {
            Assert.AreEqual(band.Transmission(tau), quadrature.Transmission(tau), tolerance,
                $"{target.Name}, {points} g-points at tau = {tau}");
        }
    }

    [TestMethod]
    public void IntensityCutoffKeepsTheLinesThatMatter()
    {
        string path = RequirePath(CarbonDioxide.File);

        var all = HitranLineList.Load(path);
        var strong = HitranLineList.Load(path, minimumIntensity: 1e-27);

        Assert.IsTrue(strong.Count < all.Count, "the cutoff should actually drop something");
        Assert.IsTrue(strong.Count > 1000,
            $"but should leave a properly resolved band ({strong.Count} lines)");
    }

    // ---------------------------------------------------------------- what differs between them

    /// <summary>
    /// Water vapour's rotational band is far more irregular than CO2's, and the consequence for
    /// a grey model is severe.
    /// </summary>
    /// <remarks>
    /// CO2's 15 um band is a regular vibration-rotation progression; H2O is an asymmetric rotor
    /// whose lines are scattered irregularly, so its absorption spans a far wider range - about
    /// 8e4 against 9e2 for the same measure. At tau = 10 that means a grey band transmits under
    /// 0.01 % where the real H2O band transmits half. It is the most direct statement available
    /// of what the grey assumption costs, and it is worse for the gas that does most of the
    /// absorbing.
    /// </remarks>
    [TestMethod]
    public void WaterVapourIsFarMoreNonGreyThanCarbonDioxide()
    {
        double Range(Target target)
        {
            var k = Band(target).AbsorptionCoefficients();
            double min = double.MaxValue, max = double.MinValue;
            foreach (double value in k) { min = Math.Min(min, value); max = Math.Max(max, value); }
            return max / min;
        }

        double water = Range(WaterVapour);
        double carbon = Range(CarbonDioxide);

        Assert.IsTrue(water > 10.0 * carbon,
            $"H2O should span a far wider range of absorption than CO2 " +
            $"({water:E2} vs {carbon:E2})");

        // And the practical consequence, at an optical depth where grey has given up entirely.
        double reference = Band(WaterVapour).Transmission(10.0);
        Assert.IsTrue(reference > 0.3,
            $"the real H2O band should still transmit substantially at tau = 10 ({reference:F4}) " +
            $"where a grey band transmits {Math.Exp(-10.0):E2}");
    }

    /// <summary>
    /// The two molecules need materially different parametric widths, so no single
    /// <c>--k-width</c> can serve an atmosphere containing both.
    /// </summary>
    /// <remarks>
    /// This is the strongest statement available against the parametric approach. Even setting
    /// aside that CO2's best width drifts with optical depth, the widths the two gases want
    /// differ by more than half again - and a real column contains both at once. Given line data,
    /// <see cref="ModelOptions.MeasuredKDistribution"/> is the answer; the width knob is a
    /// convenience, not a physical parameter.
    /// </remarks>
    [TestMethod]
    public void DifferentMoleculesNeedDifferentParametricWidths()
    {
        double water = BestWidth(Band(WaterVapour), 1.0).Width;
        double carbon = BestWidth(Band(CarbonDioxide), 1.0).Width;

        Assert.IsTrue(water > carbon * 1.3,
            $"H2O should want a materially wider spread than CO2 " +
            $"(H2O {water:F2}, CO2 {carbon:F2}); if they agreed, a single width would be defensible");
    }

    /// <summary>
    /// CO2's best-fit width drifts with optical depth, because a real band's k-distribution is
    /// not lognormal. Water vapour's, interestingly, is far more stable - many irregular lines
    /// land closer to lognormal than one regular progression does - so the drift is asserted only
    /// where it occurs, rather than assumed universal.
    /// </summary>
    [TestMethod]
    public void CarbonDioxideWidthDriftsWithOpticalDepthMoreThanWaterVapourDoes()
    {
        double Drift(Target target) =>
            Math.Abs(BestWidth(Band(target), 0.1).Width - BestWidth(Band(target), 30.0).Width);

        double carbon = Drift(CarbonDioxide);
        double water = Drift(WaterVapour);

        Assert.IsTrue(carbon > 0.2,
            $"CO2's best-fit width should drift across optical depth ({carbon:F2})");
        Assert.IsTrue(water < carbon,
            $"H2O's should drift less, being closer to lognormal ({water:F2} vs {carbon:F2})");
    }

    [DataTestMethod]
    [DataRow(HitranLineList.Co2FifteenMicron)]
    [DataRow(HitranLineList.WaterVapourRotational)]
    public void MeasuredDistributionBeatsTheBestParametricFit(string file)
    {
        var target = Resolve(file);
        var band = Band(target);
        var references = OpticalDepths.Select(t => band.Transmission(t)).ToArray();

        double Rms(KDistribution candidate)
        {
            double sum = 0.0;
            for (int i = 0; i < OpticalDepths.Length; i++)
            {
                double d = candidate.Transmission(OpticalDepths[i]) - references[i];
                sum += d * d;
            }
            return Math.Sqrt(sum / OpticalDepths.Length);
        }

        double bestParametric = double.MaxValue;
        for (double width = 0.25; width <= 8.0; width += 0.05)
        {
            bestParametric = Math.Min(bestParametric,
                Rms(KDistribution.Build(KDistributionShape.Lognormal, width, 64)));
        }

        double measured = Rms(band.ToKDistribution(32));

        Assert.IsTrue(measured < 0.5 * bestParametric,
            $"{target.Name}: a measured distribution should beat the best parametric fit " +
            $"(measured {measured:F5}, best lognormal {bestParametric:F5})");
    }

    // ---------------------------------------------------------------- in the column

    /// <summary>
    /// The loop closed: a distribution measured from real lines drives the column model.
    /// </summary>
    [DataTestMethod]
    [DataRow(HitranLineList.Co2FifteenMicron)]
    [DataRow(HitranLineList.WaterVapourRotational)]
    public void ColumnRunsOnAKDistributionMeasuredFromRealLines(string file)
    {
        var target = Resolve(file);
        var measured = Band(target).ToKDistribution(16);

        var result = ColumnModel.RunToEquilibrium(new ModelOptions
        {
            MeasuredKDistribution = measured
        });

        Assert.IsTrue(result.Converged,
            $"{target.Name}: the column must reach equilibrium on real line data");
        Assert.IsTrue(result.SurfaceTemperature is > 200 and < 350,
            $"{target.Name}: and stay physical ({result.SurfaceTemperature:F2} K)");

        // Real line structure lets more radiation out than a grey band of the same absorber, so
        // the surface settles cooler.
        Assert.IsTrue(result.SurfaceTemperature < TestSupport.Default.SurfaceTemperature,
            $"{target.Name}: real line structure should cool the surface relative to grey " +
            $"({result.SurfaceTemperature:F2} vs {TestSupport.Default.SurfaceTemperature:F2} K)");
    }

    /// <summary>
    /// The whole arc, closed: CO2 and water vapour in their own bands, each carrying the line
    /// structure measured from its own HITRAN spectrum, solved together in one column.
    /// </summary>
    /// <remarks>
    /// This was impossible before banding. A single broadband absorber can hold one
    /// k-distribution, and the two gases demonstrably want different ones - CO2's best-fit
    /// lognormal width is about 1.5 against water vapour's 2.4, and their measured distributions
    /// differ far more than that comparison suggests. Now each band carries its own.
    /// </remarks>
    [TestMethod]
    public void BandedColumnCarriesBothGasesWithTheirOwnMeasuredStructures()
    {
        var carbon = Band(CarbonDioxide).ToKDistribution(16);
        var water = Band(WaterVapour).ToKDistribution(16);

        var options = new ModelOptions
        {
            SegmentCount = 40,
            WaterVapourOpticalDepth = 1.0,
            Bands = new[]
            {
                new SpectralBand
                {
                    Label = "H2O rotational",
                    ShortWavelength = 20e-6,
                    LongWavelength = 50e-6,
                    WaterVapourOpticalDepth = 4.0,
                    Co2Fraction = 0.0,
                    Structure = water
                },
                new SpectralBand
                {
                    Label = "CO2 15 um",
                    ShortWavelength = 13e-6,
                    LongWavelength = 17e-6,
                    OpticalDepth = 3.0,
                    Structure = carbon
                },
                new SpectralBand { Label = "remainder", OpticalDepth = 0.5, Co2Fraction = 0.0 }
            }
        };

        var result = ColumnModel.RunToEquilibrium(options);

        Assert.IsTrue(result.Converged, "the two-gas banded column must reach equilibrium");
        Assert.IsTrue(result.SurfaceTemperature is > 200 and < 350,
            $"and stay physical ({result.SurfaceTemperature:F2} K)");

        // The two bands really are carrying different distributions.
        Assert.AreNotEqual(carbon.Multipliers[0], water.Multipliers[0],
            "the two gases should not have produced identical structures");

        // And doubling CO2 warms it, with only the CO2 band responding.
        var doubled = options.Clone();
        doubled.Co2Concentration = 570.0;
        var warmer = ColumnModel.RunToEquilibrium(doubled);

        Assert.IsTrue(warmer.SurfaceTemperature > result.SurfaceTemperature,
            $"doubling CO2 must warm the banded column " +
            $"({warmer.SurfaceTemperature:F2} vs {result.SurfaceTemperature:F2} K)");
    }

    [TestMethod]
    public void MeasuredDistributionTakesPrecedenceOverTheParametricShape()
    {
        var measured = Band(CarbonDioxide).ToKDistribution(16);

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
