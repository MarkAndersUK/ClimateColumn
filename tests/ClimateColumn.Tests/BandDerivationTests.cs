using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Deriving a spectral band set from line data rather than specifying it by hand.
/// </summary>
/// <remarks>
/// Banding by hand means three guesses per band: where it sits, how opaque it is relative to its
/// neighbours, and what its line structure looks like. Given a line list all three follow from the
/// data - boundaries from the Planck function, relative opacity from the mean absorption measured
/// inside each band, structure from the measured distribution of that absorption. Only the total
/// amount of gas stays a free parameter, which is right: that is concentration, not spectroscopy.
///
/// The mechanics are tested against the synthetic band, so they always run. Whether the derivation
/// recovers real band shapes needs HITRAN, so those tests skip when it is absent.
/// </remarks>
[TestClass]
public class BandDerivationTests
{
    private static LineByLineBand? _synthetic;
    private static LineByLineBand Synthetic => _synthetic ??= LineByLineBand.Synthetic();

    private static LineByLineBand? _carbon;
    private static LineByLineBand? _water;

    private static LineByLineBand Real(string file, double from, double to)
    {
        string? path = HitranLineList.DefaultPath(file);
        if (path is null)
        {
            Assert.Inconclusive(
                $"No HITRAN data at data/{file}. Run scripts/fetch-hitran.ps1 (add " +
                "-Molecule h2o-rotational for water vapour).");
        }

        var lines = HitranLineList.Load(path!, minimumIntensity: 1e-27);
        return LineByLineBand.FromLines(lines, from, to, 60_000, wingCutoff: 25.0);
    }

    private static LineByLineBand Carbon =>
        _carbon ??= Real(HitranLineList.Co2FifteenMicron, 600, 750);

    private static LineByLineBand Water =>
        _water ??= Real(HitranLineList.WaterVapourRotational, 150, 450);

    // ---------------------------------------------------------------- mechanics

    [DataTestMethod]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(8)]
    public void DerivationProducesContiguousNonOverlappingBands(int count)
    {
        var bands = BandDerivation.Derive(
            Synthetic, count, opticalDepth: 2.0, AbsorberKind.WellMixed, "test");

        Assert.AreEqual(count, bands.Count);

        // Sorted by short wavelength, each band's long edge should meet the next band's short
        // edge - a partition of the covered range with no gaps and no overlap.
        var sorted = bands.OrderBy(b => b.ShortWavelength).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            Assert.AreEqual(sorted[i - 1].LongWavelength, sorted[i].ShortWavelength, 1e-12,
                $"bands {i - 1} and {i} should share an edge exactly");
        }

        foreach (var band in bands)
        {
            Assert.IsTrue(band.LongWavelength > band.ShortWavelength,
                $"{band.Label} should have positive width");
            Assert.IsFalse(band.IsRemainder, "a derived band is never the remainder");
        }
    }

    /// <summary>
    /// A derived set must pass the solver's own validation - the derivation is the most likely
    /// place for an off-by-one in the wavenumber-to-wavelength flip to produce overlap.
    /// </summary>
    [TestMethod]
    public void DerivedBandsAreAcceptedByTheSolver()
    {
        var options = new ModelOptions
        {
            SegmentCount = 20,
            Bands = BandDerivation.Combine(0.3,
                BandDerivation.Derive(Synthetic, 4, 1.5, AbsorberKind.WellMixed, "x")).ToArray()
        };

        var column = Column.Build(options);
        var rad = RadiationSolver.Solve(column);

        Assert.AreEqual(5, rad.BandLabels.Length, "four derived bands plus the remainder");
    }

    /// <summary>
    /// The one free parameter behaves as documented: the Planck-weighted mean optical depth across
    /// the derived bands is what was asked for, with the relative pattern coming from the data.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.5)]
    [DataRow(2.0)]
    [DataRow(8.0)]
    public void OpticalDepthIsNormalisedToThePlanckWeightedMean(double requested)
    {
        const double reference = 260.0;
        var bands = BandDerivation.Derive(
            Synthetic, 6, requested, AbsorberKind.WellMixed, "x", referenceTemperature: reference);

        double weighted = 0.0, shares = 0.0;
        foreach (var band in bands)
        {
            double share = band.PlanckShare(reference);
            weighted += share * band.OpticalDepth;
            shares += share;
        }

        Assert.IsTrue(shares > 0, "the bands should carry some of the Planck function");
        Assert.AreEqual(requested, weighted / shares, requested * 1e-6,
            $"the Planck-weighted mean optical depth should be {requested}");
    }

    [TestMethod]
    public void ScalingIsLinearInTheRequestedDepth()
    {
        var thin = BandDerivation.Derive(Synthetic, 5, 1.0, AbsorberKind.WellMixed, "x");
        var thick = BandDerivation.Derive(Synthetic, 5, 3.0, AbsorberKind.WellMixed, "x");

        for (int b = 0; b < thin.Count; b++)
        {
            Assert.AreEqual(3.0 * thin[b].OpticalDepth, thick[b].OpticalDepth, 1e-9,
                $"band {b} should scale linearly with the requested depth");
        }
    }

    [DataTestMethod]
    [DataRow(AbsorberKind.WellMixed)]
    [DataRow(AbsorberKind.WaterVapour)]
    public void AbsorberKindDecidesWhichVerticalProfileTheBandUses(AbsorberKind kind)
    {
        var bands = BandDerivation.Derive(Synthetic, 3, 2.0, kind, "x");

        foreach (var band in bands)
        {
            if (kind == AbsorberKind.WellMixed)
            {
                Assert.IsTrue(band.OpticalDepth > 0, "a well-mixed band carries dry opacity");
                Assert.AreEqual(0.0, band.WaterVapourOpticalDepth, 0.0);
            }
            else
            {
                Assert.IsTrue(band.WaterVapourOpticalDepth > 0, "a vapour band carries vapour opacity");
                Assert.AreEqual(0.0, band.OpticalDepth, 0.0);
                Assert.AreEqual(0.0, band.Co2Fraction, 0.0,
                    "a vapour band must not respond to CO2 concentration");
            }
        }
    }

    [TestMethod]
    public void EachDerivedBandCarriesItsOwnMeasuredStructure()
    {
        var bands = BandDerivation.Derive(Synthetic, 4, 2.0, AbsorberKind.WellMixed, "x", gPoints: 12);

        foreach (var band in bands)
        {
            Assert.IsNotNull(band.Structure, $"{band.Label} should carry a k-distribution");
            Assert.AreEqual(12, band.Structure!.Points);

            double weights = 0.0, mean = 0.0;
            for (int j = 0; j < band.Structure.Points; j++)
            {
                weights += band.Structure.Weights[j];
                mean += band.Structure.Weights[j] * band.Structure.Multipliers[j];
            }

            Assert.AreEqual(1.0, weights, 1e-12, $"{band.Label}: weights should sum to one");
            Assert.AreEqual(1.0, mean, 1e-9,
                $"{band.Label}: the distribution should be relative to the band's own mean");
        }
    }

    [TestMethod]
    public void CombineSortsAndAppendsARemainder()
    {
        var combined = BandDerivation.Combine(0.7,
            BandDerivation.Derive(Synthetic, 3, 1.0, AbsorberKind.WellMixed, "a"));

        Assert.AreEqual(4, combined.Count, "three bands plus a remainder");
        Assert.IsTrue(combined[^1].IsRemainder, "the remainder should come last");
        Assert.AreEqual(0.7, combined[^1].OpticalDepth, 1e-12);

        for (int i = 1; i < combined.Count - 1; i++)
        {
            Assert.IsTrue(combined[i].ShortWavelength >= combined[i - 1].ShortWavelength,
                "bands should come out sorted by wavelength");
        }
    }

    // ---------------------------------------------------------------- does it recover real bands?

    /// <summary>
    /// The substantive test: derived from HITRAN, the most opaque band should be the one containing
    /// the CO2 nu2 centre at 15 um, with the wings far weaker. Nothing tells the derivation where
    /// the band centre is - it comes out of the line strengths.
    /// </summary>
    [TestMethod]
    public void DerivationRecoversTheCarbonDioxideBandCentre()
    {
        var bands = BandDerivation.Derive(Carbon, 5, 2.0, AbsorberKind.WellMixed, "CO2");

        var strongest = bands.OrderByDescending(b => b.OpticalDepth).First();
        double centre = 0.5 * (strongest.ShortWavelength + strongest.LongWavelength) * 1e6;

        Assert.IsTrue(centre is > 14.0 and < 16.0,
            $"the most opaque band should straddle 15 um, the nu2 centre (got {centre:F2} um)");

        double weakest = bands.Min(b => b.OpticalDepth);
        Assert.IsTrue(strongest.OpticalDepth > 20.0 * weakest,
            $"the centre should be far more opaque than the wings " +
            $"({strongest.OpticalDepth:F2} vs {weakest:F2})");
    }

    /// <summary>
    /// Water vapour's pure rotational band strengthens toward longer wavelengths, and the
    /// derivation should show that without being told.
    /// </summary>
    [TestMethod]
    public void DerivationRecoversTheWaterVapourRotationalSlope()
    {
        var bands = BandDerivation.Derive(Water, 5, 4.0, AbsorberKind.WaterVapour, "H2O")
            .OrderBy(b => b.ShortWavelength)
            .ToList();

        Assert.IsTrue(bands[^1].WaterVapourOpticalDepth > 5.0 * bands[0].WaterVapourOpticalDepth,
            $"the far-infrared end should be far more opaque than the short-wavelength end " +
            $"({bands[^1].WaterVapourOpticalDepth:F2} vs {bands[0].WaterVapourOpticalDepth:F2})");

        // And monotonically so, which is what a rotational progression looks like.
        for (int i = 1; i < bands.Count; i++)
        {
            Assert.IsTrue(bands[i].WaterVapourOpticalDepth > bands[i - 1].WaterVapourOpticalDepth,
                $"band {i} should be stronger than band {i - 1} " +
                $"({bands[i].WaterVapourOpticalDepth:F3} vs {bands[i - 1].WaterVapourOpticalDepth:F3})");
        }
    }

    /// <summary>
    /// The band-strength pattern should come from the spectrum, not from where the boundaries
    /// happen to fall, so the two boundary strategies should broadly agree.
    /// </summary>
    [TestMethod]
    public void BothBoundaryStrategiesFindTheSameBandStructure()
    {
        double PeakDepth(LineByLineBand.SubdivisionStrategy strategy) =>
            BandDerivation.Derive(Carbon, 5, 2.0, AbsorberKind.WellMixed, "CO2", strategy: strategy)
                .Max(b => b.OpticalDepth);

        double planck = PeakDepth(LineByLineBand.SubdivisionStrategy.EqualPlanckEnergy);
        double uniform = PeakDepth(LineByLineBand.SubdivisionStrategy.UniformWavenumber);

        Assert.AreEqual(planck, uniform, 0.25 * planck,
            $"the peak opacity should not depend much on how the edges are placed " +
            $"({planck:F2} equal-Planck, {uniform:F2} uniform)");
    }

    /// <summary>
    /// The end of the road: a band set derived entirely from two gases' line data, solved to
    /// equilibrium, responding to CO2.
    /// </summary>
    [TestMethod]
    public void DerivedTwoGasColumnSolvesAndRespondsToCarbonDioxide()
    {
        var bands = BandDerivation.Combine(0.4,
            BandDerivation.Derive(Water, 4, 4.0, AbsorberKind.WaterVapour, "H2O"),
            BandDerivation.Derive(Carbon, 4, 2.0, AbsorberKind.WellMixed, "CO2"));

        var options = new ModelOptions
        {
            SegmentCount = 40,
            Bands = bands.ToArray(),
            WaterVapourOpticalDepth = 1.0
        };

        var result = ColumnModel.RunToEquilibrium(options);

        Assert.IsTrue(result.Converged, "a fully derived band set must reach equilibrium");
        Assert.IsTrue(Math.Abs(result.TopOfAtmosphereImbalance) < 1e-4,
            $"with the top of atmosphere in balance ({result.TopOfAtmosphereImbalance:E2} W/m2)");
        Assert.IsTrue(result.SurfaceTemperature is > 200 and < 350,
            $"and a physical surface temperature ({result.SurfaceTemperature:F2} K)");

        var doubled = options.Clone();
        doubled.Co2Concentration = 570.0;
        var warmer = ColumnModel.RunToEquilibrium(doubled);

        Assert.IsTrue(warmer.SurfaceTemperature > result.SurfaceTemperature,
            $"doubling CO2 must warm a derived column " +
            $"({warmer.SurfaceTemperature:F2} vs {result.SurfaceTemperature:F2} K)");

        // Only the CO2 bands respond; the water bands are unchanged.
        var before = Column.Build(options);
        var after = Column.Build(doubled);
        for (int b = 0; b < bands.Count; b++)
        {
            bool isCarbon = bands[b].Co2Fraction > 0;
            double change = Math.Abs(after.TotalBandOpticalDepth(b) - before.TotalBandOpticalDepth(b));

            if (isCarbon)
                Assert.IsTrue(change > 1e-9, $"{bands[b].Label} should respond to CO2");
            else
                Assert.AreEqual(0.0, change, 1e-9, $"{bands[b].Label} should not respond to CO2");
        }
    }
}
