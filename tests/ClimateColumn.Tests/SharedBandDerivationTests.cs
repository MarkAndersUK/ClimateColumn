using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Deriving one band set from several molecules at once, on a shared wavenumber grid.
/// </summary>
/// <remarks>
/// Deriving each gas separately produces overlapping sets - N2O's band sits inside methane's, and
/// water vapour is everywhere - and overlapping bands are rejected, rightly, since each would claim
/// its own share of every emitter's Planck function. A shared grid partitions the range once and
/// records how much each gas contributes to each band.
///
/// Needs all six line lists. Fetch them with <c>scripts/fetch-hitran.ps1 -Molecule all</c>; these
/// skip rather than fail without them.
/// </remarks>
[TestClass]
public class SharedBandDerivationTests
{
    private static readonly (string File, AbsorberKind Kind, double Depth, bool Co2, string Label)[] Recipe =
    {
        (HitranLineList.WaterVapourRotational, AbsorberKind.WaterVapour, 6.0, false, "H2O rot"),
        (HitranLineList.WaterVapourBending,    AbsorberKind.WaterVapour, 2.0, false, "H2O bend"),
        (HitranLineList.Co2FifteenMicron,      AbsorberKind.WellMixed,   2.0, true,  "CO2"),
        (HitranLineList.OzoneNineSixMicron,    AbsorberKind.Ozone,       0.5, false, "O3"),
        (HitranLineList.MethaneSevenSevenMicron, AbsorberKind.WellMixed, 0.2, false, "CH4"),
        (HitranLineList.NitrousOxideSevenEightMicron, AbsorberKind.WellMixed, 0.1, false, "N2O")
    };

    private static IReadOnlyList<SpectralBand>? _bands;

    /// <summary>
    /// The derived set, built once. Twelve bands over 100-2000 cm^-1 from about 100,000 lines takes
    /// well under a second thanks to the wing cutoff.
    /// </summary>
    private static IReadOnlyList<SpectralBand> Bands
    {
        get
        {
            if (_bands is not null) return _bands;

            var molecules = new List<BandDerivation.Molecule>();
            foreach (var (file, kind, depth, co2, label) in Recipe)
            {
                string? path = HitranLineList.DefaultPath(file);
                if (path is null)
                {
                    Assert.Inconclusive(
                        $"No HITRAN data at data/{file}. Run " +
                        "scripts/fetch-hitran.ps1 -Molecule all to fetch every longwave band.");
                }

                molecules.Add(new BandDerivation.Molecule(
                    HitranLineList.Load(path!, minimumIntensity: 1e-26), kind, depth, co2, label));
            }

            _bands = BandDerivation.DeriveShared(
                molecules, fromWavenumber: 100, toWavenumber: 2000, bandCount: 12,
                samples: 100_000, gPoints: 12, wingCutoff: 15.0);

            return _bands;
        }
    }

    private static SpectralBand BandNear(double micron) =>
        Bands.Single(b => micron * 1e-6 >= b.ShortWavelength && micron * 1e-6 <= b.LongWavelength);

    // ---------------------------------------------------------------- the point of the exercise

    /// <summary>
    /// The remainder was most of the spectrum when only two gases were derived. Covering
    /// 100-2000 cm^-1 with six leaves barely any of the emission to a free parameter.
    /// </summary>
    [TestMethod]
    public void DerivedBandsCoverNearlyAllTheEmission()
    {
        double covered = Bands.Sum(b => b.PlanckShare(260.0));

        Assert.IsTrue(covered > 0.97,
            $"the derived bands should carry almost all of a 260 K Planck function " +
            $"(got {covered:P2}, leaving {1 - covered:P2} to the remainder)");
        Assert.IsTrue(covered <= 1.0, $"and never more than all of it ({covered:P4})");
    }

    [TestMethod]
    public void SharedBandsPartitionTheRangeWithoutOverlapping()
    {
        var sorted = Bands.OrderBy(b => b.ShortWavelength).ToList();

        for (int i = 1; i < sorted.Count; i++)
        {
            Assert.AreEqual(sorted[i - 1].LongWavelength, sorted[i].ShortWavelength, 1e-12,
                $"bands {i - 1} and {i} should meet exactly, neither gapping nor overlapping");
        }

        // And the solver agrees, which is the check that actually matters.
        var column = Column.Build(new ModelOptions
        {
            SegmentCount = 20, Bands = Bands.ToArray(), WaterVapourOpticalDepth = 1.0
        });

        Assert.AreEqual(Bands.Count, column.Options.Bands.Count);
    }

    // ---------------------------------------------------------------- does it find real features?

    /// <summary>
    /// The atmospheric window should emerge as a band where essentially nothing absorbs. Nothing
    /// tells the derivation the window exists - it is simply where none of these gases has lines.
    /// </summary>
    [TestMethod]
    public void DerivationFindsTheAtmosphericWindow()
    {
        var window = BandNear(11.8);

        double opacity = window.OpticalDepth + window.WaterVapourOpticalDepth +
                         window.OzoneOpticalDepth;

        Assert.IsTrue(opacity < 0.05,
            $"the 11-12 um band should be nearly empty, which is what makes it a window " +
            $"(total tau {opacity:F4})");

        // And it should be the emptiest band in the set.
        double weakest = Bands.Min(b =>
            b.OpticalDepth + b.WaterVapourOpticalDepth + b.OzoneOpticalDepth);
        Assert.AreEqual(weakest, opacity, 1e-12, "and the emptiest band overall");
    }

    /// <summary>
    /// Ozone's 9.6 um band should land inside the window, which is exactly why it matters
    /// climatically: it absorbs where nothing else does.
    /// </summary>
    [TestMethod]
    public void OzoneLandsInTheWindowOnItsOwnProfile()
    {
        var ozoneBand = Bands.OrderByDescending(b => b.OzoneOpticalDepth).First();
        double centre = 0.5 * (ozoneBand.ShortWavelength + ozoneBand.LongWavelength) * 1e6;

        Assert.IsTrue(centre is > 8.0 and < 11.5,
            $"the strongest ozone band should sit near 9.6 um (got {centre:F2} um)");
        Assert.IsTrue(ozoneBand.OzoneOpticalDepth > 1.0,
            $"and carry real opacity ({ozoneBand.OzoneOpticalDepth:F3})");

        // Ozone opacity must go on the Chapman profile, so it should peak in the stratosphere
        // rather than at the ground like a well-mixed or vapour absorber.
        int band = Bands.ToList().IndexOf(ozoneBand);
        var column = Column.Build(new ModelOptions
        {
            SegmentCount = 60, TopAltitude = 50_000,
            Bands = Bands.ToArray(), WaterVapourOpticalDepth = 1.0
        });

        int peak = 0;
        for (int i = 1; i < column.Count; i++)
        {
            if (column.Segments[i].BandEmissionCoefficients[band] >
                column.Segments[peak].BandEmissionCoefficients[band]) peak = i;
        }

        double altitude = column.Segments[peak].MidAltitude / 1000.0;
        Assert.IsTrue(altitude is > 15.0 and < 40.0,
            $"ozone absorption should peak in the stratosphere, not at the ground " +
            $"(peaks at {altitude:F1} km)");
    }

    [TestMethod]
    public void CarbonDioxideDominatesItsOwnBandAndNothingElse()
    {
        var strongest = Bands.OrderByDescending(b => b.OpticalDepth).First();
        double centre = 0.5 * (strongest.ShortWavelength + strongest.LongWavelength) * 1e6;

        Assert.IsTrue(centre is > 13.5 and < 16.5,
            $"the most opaque well-mixed band should be CO2's nu2 centre near 15 um " +
            $"(got {centre:F2} um)");
        Assert.AreEqual(1.0, strongest.Co2Fraction, 0.01,
            "and CO2 should account for essentially all of its well-mixed opacity");

        // The methane and nitrous oxide region should be dominated by something other than CO2.
        var methaneRegion = BandNear(7.0);
        Assert.IsTrue(methaneRegion.Co2Fraction < 0.5,
            $"the 7 um region should not be attributed to CO2 ({methaneRegion.Co2Fraction:F3})");
    }

    [TestMethod]
    public void WaterVapourDominatesTheFarInfrared()
    {
        var farInfrared = Bands.OrderByDescending(b => b.LongWavelength).First();

        Assert.IsTrue(farInfrared.WaterVapourOpticalDepth > 10.0,
            $"the far infrared should be very opaque with water vapour " +
            $"({farInfrared.WaterVapourOpticalDepth:F2})");
        Assert.AreEqual(0.0, farInfrared.OpticalDepth, 1e-9,
            "and carry no well-mixed opacity there");
    }

    // ---------------------------------------------------------------- in the column

    [TestMethod]
    public void SharedBandColumnReachesEquilibrium()
    {
        var result = TestSupport.Equilibrium("shared-bands", () => new ModelOptions
        {
            SegmentCount = 40, Bands = Bands.ToArray(), WaterVapourOpticalDepth = 1.0
        });

        Assert.IsTrue(result.Converged, "a six-molecule banded column must reach equilibrium");
        Assert.IsTrue(Math.Abs(result.TopOfAtmosphereImbalance) < 1e-4,
            $"with the top of atmosphere in balance ({result.TopOfAtmosphereImbalance:E2} W/m2)");
        Assert.IsTrue(result.SurfaceTemperature is > 200 and < 350,
            $"and a physical surface temperature ({result.SurfaceTemperature:F2} K)");
    }

    /// <summary>
    /// Only the bands CO2 dominates respond to concentration. This is what a single broadband
    /// absorber could never express, and here the split comes from the line strengths rather than
    /// from a choice.
    /// </summary>
    [TestMethod]
    public void OnlyCarbonDioxideBandsRespondToConcentration()
    {
        var options = new ModelOptions
        {
            SegmentCount = 30, Bands = Bands.ToArray(), WaterVapourOpticalDepth = 1.0
        };
        var doubled = options.Clone();
        doubled.Co2Concentration = 570.0;

        var before = Column.Build(options);
        var after = Column.Build(doubled);

        int responded = 0;
        for (int b = 0; b < Bands.Count; b++)
        {
            double change = after.TotalBandOpticalDepth(b) - before.TotalBandOpticalDepth(b);

            if (Bands[b].Co2Fraction > 0.01 && Bands[b].OpticalDepth > 0.01)
            {
                Assert.IsTrue(change > 0, $"{Bands[b].Label} is CO2-bearing and should respond");
                responded++;
            }
            else
            {
                Assert.AreEqual(0.0, change, 1e-9,
                    $"{Bands[b].Label} carries no CO2 and must not respond");
            }
        }

        Assert.IsTrue(responded > 0, "at least one band should have responded");

        var warmer = ColumnModel.RunToEquilibrium(doubled);
        var reference = ColumnModel.RunToEquilibrium(options);
        Assert.IsTrue(warmer.SurfaceTemperature > reference.SurfaceTemperature,
            $"doubling CO2 must warm the column " +
            $"({warmer.SurfaceTemperature:F2} vs {reference.SurfaceTemperature:F2} K)");
    }
}
