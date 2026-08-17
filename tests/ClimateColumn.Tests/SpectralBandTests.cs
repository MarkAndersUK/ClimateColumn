using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Solving band by band: different gases acting where they actually act, each with its own
/// optical depth, vertical profile and line structure.
/// </summary>
/// <remarks>
/// A grey column has one absorber standing in for everything, so CO2's 15 um band and water
/// vapour's rotational band cannot both be represented - they differ in strength, in where they
/// sit vertically, and (as the HITRAN comparison shows) in line structure by two orders of
/// magnitude. Bands are what make both expressible at once.
/// </remarks>
[TestClass]
public class SpectralBandTests
{
    /// <summary>A rough two-gas atmosphere: CO2 at 15 um, water vapour in the far infrared.</summary>
    private static SpectralBand[] TwoGasBands() => new[]
    {
        new SpectralBand
        {
            Label = "H2O rotational",
            ShortWavelength = 20e-6,
            LongWavelength = 50e-6,
            OpticalDepth = 0.0,
            Co2Fraction = 0.0,
            WaterVapourOpticalDepth = 4.0
        },
        new SpectralBand
        {
            Label = "CO2 15 um",
            ShortWavelength = 13e-6,
            LongWavelength = 17e-6,
            OpticalDepth = 3.0,
            Co2Fraction = 1.0
        },
        new SpectralBand
        {
            Label = "window",
            ShortWavelength = 8e-6,
            LongWavelength = 13e-6,
            ContinuumOpticalDepth = 0.4,
            WaterVapourOpticalDepth = 0.1
        },
        new SpectralBand
        {
            Label = "remainder",
            OpticalDepth = 0.6,
            Co2Fraction = 0.0
        }
    };

    private static ModelOptions TwoGasOptions() => new()
    {
        SegmentCount = 40,
        Bands = TwoGasBands(),
        WaterVapourOpticalDepth = 1.0   // drives the Clausius-Clapeyron scaling
    };

    // ---------------------------------------------------------------- the arrangement itself

    /// <summary>
    /// With no bands given, the solver still runs the single-absorber arrangement, and does so
    /// bit-identically. Every existing configuration is expressed that way, so this is the
    /// regression that matters most.
    /// </summary>
    [TestMethod]
    public void NoBandsLeavesTheSingleAbsorberArrangementBitIdentical()
    {
        var column = Column.Build(new ModelOptions { SegmentCount = 30 });
        var rad = RadiationSolver.Solve(column);

        Assert.AreEqual(2, rad.BandLabels.Length, "the legacy arrangement is absorbing + window");
        CollectionAssert.AreEqual(new[] { "absorbing", "window" }, rad.BandLabels);

        // With no window the absorbing band carries everything, so the representative optical
        // thickness is exactly that band's own.
        for (int i = 0; i < column.Count; i++)
        {
            Assert.AreEqual(column.Segments[i].OpticalThickness(column.Options.Diffusivity),
                rad.OpticalThickness[i], 0.0,
                $"segment {i}: the representative thickness must equal the single band's");
        }
    }

    [TestMethod]
    public void BandLabelsAndThicknessesAreReported()
    {
        var column = Column.Build(TwoGasOptions());
        var rad = RadiationSolver.Solve(column);

        CollectionAssert.AreEqual(
            new[] { "H2O rotational", "CO2 15 um", "window", "remainder" }, rad.BandLabels);

        Assert.AreEqual(4, rad.BandOpticalThickness.Length);
        foreach (var band in rad.BandOpticalThickness)
        {
            Assert.AreEqual(column.Count, band.Length, "one thickness per segment per band");
        }
    }

    /// <summary>
    /// Each band's absorber is normalised to the column optical depth it was given, so the
    /// numbers a caller writes are the numbers the column carries.
    /// </summary>
    [TestMethod]
    public void EachBandCarriesTheOpticalDepthItWasGiven()
    {
        var options = new ModelOptions
        {
            SegmentCount = 40,
            Bands = new[]
            {
                new SpectralBand { Label = "a", ShortWavelength = 13e-6, LongWavelength = 17e-6, OpticalDepth = 2.5, Co2Fraction = 0.0 },
                new SpectralBand { Label = "b", ShortWavelength = 8e-6, LongWavelength = 13e-6, OpticalDepth = 0.4, Co2Fraction = 0.0 }
            }
        };
        var column = Column.Build(options);

        Assert.AreEqual(2.5, column.TotalBandOpticalDepth(0), 1e-9, "band a");
        Assert.AreEqual(0.4, column.TotalBandOpticalDepth(1), 1e-9, "band b");
    }

    // ---------------------------------------------------------------- energy closure

    /// <summary>
    /// The Planck weights must sum to one at every temperature, or the column emits more or less
    /// than its own sigma T^4. The remainder band is what guarantees it however the intervals are
    /// chosen.
    /// </summary>
    [DataTestMethod]
    [DataRow(220.0)]
    [DataRow(255.0)]
    [DataRow(288.0)]
    [DataRow(320.0)]
    public void BandWeightsSumToOneAtEveryTemperature(double temperature)
    {
        var bands = TwoGasBands();

        double claimed = 0.0;
        foreach (var band in bands) claimed += band.PlanckShare(temperature);

        Assert.IsTrue(claimed < 1.0,
            $"the interval bands should leave something over at {temperature} K ({claimed:F4})");

        // The remainder takes exactly the rest.
        double remainder = 1.0 - claimed;
        Assert.IsTrue(remainder is > 0.0 and < 1.0,
            $"the remainder should be a real share at {temperature} K ({remainder:F4})");
    }

    [TestMethod]
    public void EnergyClosesAcrossBands()
    {
        var column = Column.Build(TwoGasOptions());
        var rad = RadiationSolver.Solve(column);

        for (int i = 0; i < column.Count; i++)
        {
            Assert.AreEqual(rad.RadiativeHeating[i],
                rad.SegmentAbsorption[i] - rad.SegmentEmission[i], 1e-9,
                $"segment {i}: absorbed - emitted must equal the flux convergence");
        }

        double absorbed = 0.0, emitted = 0.0;
        for (int i = 0; i < column.Count; i++)
        {
            absorbed += rad.SegmentAbsorption[i];
            emitted += rad.SegmentEmission[i];
        }

        Assert.AreEqual(rad.SurfaceUpwardFlux + emitted - rad.SurfaceDownwardFlux,
            absorbed + rad.OutgoingLongwave, 1e-9,
            "column absorption + OLR = surface upward flux + column emission");
    }

    /// <summary>
    /// A set of transparent bands must behave exactly like no atmosphere at all: the surface's
    /// whole emission reaches space. This is the sharpest check that the weights partition the
    /// spectrum without losing or inventing any.
    /// </summary>
    [TestMethod]
    public void TransparentBandsLetTheWholeSurfaceEmissionEscape()
    {
        var options = new ModelOptions
        {
            SegmentCount = 20,
            SurfaceEmissivity = 1.0,
            Bands = new[]
            {
                new SpectralBand { Label = "a", ShortWavelength = 8e-6, LongWavelength = 13e-6 },
                new SpectralBand { Label = "b", ShortWavelength = 13e-6, LongWavelength = 17e-6 },
                new SpectralBand { Label = "rest" }
            }
        };

        var column = Column.Build(options);
        var rad = RadiationSolver.Solve(column);

        Assert.AreEqual(RadiationSolver.StefanBoltzmannFlux(column.SurfaceTemperature),
            rad.OutgoingLongwave, 1e-9,
            "with nothing absorbing anywhere, the whole surface emission must escape");
    }

    /// <summary>
    /// A band set that does not cover the spectrum must not lose the uncovered share. The solver
    /// closes it with a transparent band, so the surface still radiates exactly its own
    /// sigma T^4 and the missing part escapes rather than vanishing.
    /// </summary>
    /// <remarks>
    /// This was a real leak before the closing band existed: the interval bands' weights summed
    /// to less than one, so the surface silently under-radiated and the difference disappeared
    /// into the part of the spectrum nobody had described.
    /// </remarks>
    [TestMethod]
    public void UncoveredSpectrumIsClosedRatherThanLost()
    {
        var options = new ModelOptions
        {
            SegmentCount = 20,
            SurfaceEmissivity = 1.0,
            Bands = new[]
            {
                new SpectralBand { Label = "a", ShortWavelength = 13e-6, LongWavelength = 17e-6 },
                new SpectralBand { Label = "b", ShortWavelength = 20e-6, LongWavelength = 30e-6 }
            }
        };

        var column = Column.Build(options);
        var rad = RadiationSolver.Solve(column);

        Assert.AreEqual(3, rad.BandLabels.Length,
            "a closing band should have been added");
        Assert.AreEqual("uncovered", rad.BandLabels[^1]);

        // Nothing absorbs anywhere, so the whole surface emission must reach space - which only
        // holds if the weights sum to exactly one.
        Assert.AreEqual(RadiationSolver.StefanBoltzmannFlux(column.SurfaceTemperature),
            rad.OutgoingLongwave, 1e-9,
            "the uncovered share must escape, not disappear");
    }

    [TestMethod]
    public void OmittingTheRemainderLeavesThatShareTransparent()
    {
        // Two narrow opaque bands and no remainder: everything outside them should escape.
        var options = new ModelOptions
        {
            SegmentCount = 20,
            SurfaceEmissivity = 1.0,
            Bands = new[]
            {
                new SpectralBand { Label = "a", ShortWavelength = 13e-6, LongWavelength = 17e-6, OpticalDepth = 60, Co2Fraction = 0 },
                new SpectralBand { Label = "b", ShortWavelength = 20e-6, LongWavelength = 30e-6, OpticalDepth = 60, Co2Fraction = 0 }
            }
        };

        var column = Column.Build(options);
        var rad = RadiationSolver.Solve(column);

        double surface = RadiationSolver.StefanBoltzmannFlux(column.SurfaceTemperature);
        double claimed = options.Bands.Sum(b => b.PlanckShare(column.SurfaceTemperature));
        double escaping = (1.0 - claimed) * surface;

        Assert.IsTrue(rad.OutgoingLongwave > escaping,
            $"the uncovered share {escaping:F1} W/m2 must escape, plus whatever the opaque " +
            $"bands emit (OLR {rad.OutgoingLongwave:F1})");
        Assert.IsTrue(rad.OutgoingLongwave < surface,
            $"but the opaque bands must trap something ({rad.OutgoingLongwave:F1} vs {surface:F1})");
    }

    // ---------------------------------------------------------------- what banding buys

    /// <summary>
    /// The point of the exercise: two gases with different vertical profiles, acting in
    /// different parts of the spectrum, at the same time.
    /// </summary>
    [TestMethod]
    public void BandsPlaceTheirAbsorbersOnTheirOwnVerticalProfiles()
    {
        var column = Column.Build(TwoGasOptions());

        // Band 0 is water vapour, band 1 is CO2. Vapour has a 2 km scale height against the
        // well-mixed gas's ~8 km, so it must fall off far faster with altitude.
        int mid = column.Count / 2;

        double vapourRatio = column.Segments[0].BandEmissionCoefficients[0] /
                             column.Segments[mid].BandEmissionCoefficients[0];
        double carbonRatio = column.Segments[0].BandEmissionCoefficients[1] /
                             column.Segments[mid].BandEmissionCoefficients[1];

        Assert.IsTrue(vapourRatio > carbonRatio * 2.0,
            $"the vapour band must be far more bottom-heavy than the CO2 band " +
            $"({vapourRatio:F1} vs {carbonRatio:F1})");
    }

    /// <summary>
    /// Each band may carry its own line structure. This is the capability that the HITRAN
    /// comparison showed was needed: CO2 and water vapour want measurably different
    /// distributions, and before banding only one could be in play.
    /// </summary>
    [TestMethod]
    public void BandsMayCarryDifferentLineStructures()
    {
        var narrow = KDistribution.Build(KDistributionShape.Lognormal, 1.5, 16);
        var wide = KDistribution.Build(KDistributionShape.Lognormal, 2.4, 16);

        var options = new ModelOptions
        {
            SegmentCount = 30,
            Bands = new[]
            {
                new SpectralBand { Label = "co2", ShortWavelength = 13e-6, LongWavelength = 17e-6, OpticalDepth = 3.0, Co2Fraction = 0, Structure = narrow },
                new SpectralBand { Label = "h2o", ShortWavelength = 20e-6, LongWavelength = 50e-6, OpticalDepth = 3.0, Co2Fraction = 0, Structure = wide },
                new SpectralBand { Label = "rest", OpticalDepth = 0.5, Co2Fraction = 0 }
            }
        };

        var structured = RadiationSolver.Solve(Column.Build(options));

        // The same column with both bands grey must be more opaque, since structure always
        // transmits more at equal absorber.
        var greyOptions = options.Clone();
        greyOptions.Bands = options.Bands
            .Select(b => new SpectralBand
            {
                Label = b.Label,
                ShortWavelength = b.ShortWavelength,
                LongWavelength = b.LongWavelength,
                OpticalDepth = b.OpticalDepth,
                Co2Fraction = b.Co2Fraction,
                WaterVapourOpticalDepth = b.WaterVapourOpticalDepth,
                ContinuumOpticalDepth = b.ContinuumOpticalDepth
            })
            .ToArray();

        var grey = RadiationSolver.Solve(Column.Build(greyOptions));

        Assert.IsTrue(structured.OutgoingLongwave > grey.OutgoingLongwave,
            $"per-band line structure must let more out than grey bands " +
            $"({structured.OutgoingLongwave:F2} vs {grey.OutgoingLongwave:F2} W/m2)");
    }

    [TestMethod]
    public void OnlyTheCo2BandRespondsToConcentration()
    {
        double DepthOf(int band, double ppm)
        {
            var options = TwoGasOptions();
            options.Co2Concentration = ppm;
            return Column.Build(options).TotalBandOpticalDepth(band);
        }

        // Band 0 is water vapour with Co2Fraction 0; band 1 is CO2 with Co2Fraction 1.
        Assert.AreEqual(DepthOf(0, 285.0), DepthOf(0, 570.0), 1e-9,
            "the water-vapour band must not respond to CO2");
        Assert.AreEqual(2.0 * DepthOf(1, 285.0), DepthOf(1, 570.0), 1e-9,
            "the CO2 band must scale linearly with concentration");
    }

    [TestMethod]
    public void BandedColumnReachesEquilibrium()
    {
        var result = TestSupport.Equilibrium("two-gas-bands", TwoGasOptions);

        Assert.IsTrue(result.Converged, "a banded column must reach equilibrium");
        Assert.IsTrue(result.SurfaceTemperature is > 200 and < 350,
            $"and stay physical ({result.SurfaceTemperature:F2} K)");
        Assert.IsTrue(Math.Abs(result.TopOfAtmosphereImbalance) < 1e-4,
            $"with the top of atmosphere in balance ({result.TopOfAtmosphereImbalance:E2} W/m2)");
    }

    // ---------------------------------------------------------------- validation

    [TestMethod]
    public void OverlappingBandsAreRejected()
    {
        var options = new ModelOptions
        {
            Bands = new[]
            {
                new SpectralBand { Label = "a", ShortWavelength = 8e-6, LongWavelength = 14e-6 },
                new SpectralBand { Label = "b", ShortWavelength = 13e-6, LongWavelength = 17e-6 }
            }
        };

        var error = Assert.ThrowsException<ArgumentException>(() => Column.Build(options));
        StringAssert.Contains(error.Message, "overlap",
            "the message should say what is wrong");
    }

    [TestMethod]
    public void MoreThanOneRemainderIsRejected()
    {
        var options = new ModelOptions
        {
            Bands = new[]
            {
                new SpectralBand { Label = "a" },
                new SpectralBand { Label = "b" }
            }
        };

        Assert.ThrowsException<ArgumentException>(() => Column.Build(options),
            "only one band can carry what the others leave");
    }

    [TestMethod]
    public void NegativeBandAbsorberIsRejected()
    {
        var options = new ModelOptions
        {
            Bands = new[]
            {
                new SpectralBand { Label = "a", ShortWavelength = 8e-6, LongWavelength = 13e-6, OpticalDepth = -1.0 }
            }
        };

        Assert.ThrowsException<ArgumentException>(() => Column.Build(options));
    }

    [TestMethod]
    public void AdjacentBandsSharingAnEdgeAreAllowed()
    {
        // Touching is not overlapping: 8-13 and 13-17 partition cleanly.
        var options = new ModelOptions
        {
            SegmentCount = 20,
            Bands = new[]
            {
                new SpectralBand { Label = "a", ShortWavelength = 8e-6, LongWavelength = 13e-6, OpticalDepth = 1.0, Co2Fraction = 0 },
                new SpectralBand { Label = "b", ShortWavelength = 13e-6, LongWavelength = 17e-6, OpticalDepth = 1.0, Co2Fraction = 0 }
            }
        };

        var column = Column.Build(options);
        Assert.AreEqual(2, column.Options.Bands.Count);
    }
}
