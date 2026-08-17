using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The optional physics layered on top of the grey column: a spectral window, pressure
/// broadening, an ozone-like solar heating layer, and the water vapour feedback.
/// </summary>
[TestClass]
public class ExtendedPhysicsTests
{
    [TestMethod]
    public void WindowScalesTheForcingByExactlyOneMinusF()
    {
        double Forcing(double window)
        {
            var baseline = new ModelOptions { WindowFraction = window };
            var doubled = baseline.Clone();
            doubled.OpticalDepthScale = 2.0;
            return TestSupport.InstantaneousForcing(baseline, doubled);
        }

        double grey = Forcing(0.0);

        Assert.IsTrue(grey > 1.0, $"the grey doubling forcing must be positive ({grey:F2} W/m2)");
        Assert.AreEqual(0.6, Forcing(0.4) / grey, 1e-9,
            "at fixed temperature the window is an exact multiplicative factor on the forcing");
    }

    [TestMethod]
    public void WindowEmissionEscapesAnOpaqueBand()
    {
        var column = Column.Build(new ModelOptions
        {
            TotalOpticalDepth = 50.0, WindowFraction = 0.35, SegmentCount = 30
        });
        var rad = RadiationSolver.Solve(column);

        double windowFlux = 0.35 * column.Options.SurfaceEmissivity *
                            RadiationSolver.StefanBoltzmannFlux(column.SurfaceTemperature);

        Assert.IsTrue(rad.OutgoingLongwave >= windowFlux - 1e-9,
            $"the window share must escape however opaque the band is " +
            $"(OLR {rad.OutgoingLongwave:F1} vs window {windowFlux:F1} W/m2)");
    }

    [TestMethod]
    public void FullyWindowedAirThatAbsorbsSunlightIsRejected()
    {
        // A transparent atmosphere that still absorbs sunlight has no way to shed that
        // energy, so the integration would run away rather than find an equilibrium.
        Assert.ThrowsException<ArgumentException>(() =>
            ColumnModel.RunToEquilibrium(new ModelOptions
            {
                WindowFraction = 1.0,
                AtmosphericShortwaveFraction = 0.22
            }),
            "a window covering the whole spectrum must be rejected, not silently run away");
    }

    [TestMethod]
    public void WaterVapourAloneIsAValidAbsorber()
    {
        // Dry tau = 0 no longer implies transparency now that vapour is carried separately,
        // so validation must not fire. One step is enough to prove acceptance.
        ColumnModel.RunToEquilibrium(new ModelOptions
        {
            TotalOpticalDepth = 0.0, WaterVapourOpticalDepth = 1.0, MaxSteps = 1
        });
    }

    /// <summary>
    /// A vapour-only column has a quasi-transparent tail (eps' ~ e^(-z/2km)) whose radiative
    /// relaxation is glacial, so the default 1e-6 W/m2 tolerance is not reachable in a
    /// bounded number of steps. What matters physically is that the column settles instead
    /// of running away - the Clausius-Clapeyron feedback collapses the vapour onto its
    /// stable cold branch - so the tolerances here are matched to that tail.
    /// </summary>
    [TestMethod]
    public void WaterVapourOnlyColumnSettlesOntoItsStableColdBranch()
    {
        var result = TestSupport.Equilibrium("vapour-only", () => new ModelOptions
        {
            TotalOpticalDepth = 0.0,
            WaterVapourOpticalDepth = 1.0,
            TopAltitude = 15_000,
            AtmosphericShortwaveFraction = 0.0,
            FluxTolerance = 1e-3,
            TemperatureTolerance = 1e-4
        });

        Assert.IsTrue(result.Converged, "a vapour-only tropospheric column must reach equilibrium");
        Assert.IsTrue(result.Column.CurrentWaterVapourOpticalDepth() < 1.0,
            $"the feedback must collapse the vapour below its reference loading " +
            $"(tau {result.Column.CurrentWaterVapourOpticalDepth():F3})");
    }

    [TestMethod]
    public void PressureBroadeningPreservesTheColumnOpticalDepth()
    {
        var wellMixed = Column.Build(new ModelOptions { SegmentCount = 80 });
        var broadened = Column.Build(new ModelOptions
        {
            SegmentCount = 80, PressureBroadeningExponent = 1.0
        });

        Assert.AreEqual(wellMixed.TotalOpticalDepth(), broadened.TotalOpticalDepth(), 1e-9,
            "broadening redistributes the absorber, it does not add any");
    }

    [DataTestMethod]
    [DataRow(1.0)]
    [DataRow(2.0)]
    public void PressureBroadeningConcentratesTheAbsorberDownward(double exponent)
    {
        var wellMixed = Column.Build(new ModelOptions { SegmentCount = 80 });
        var broadened = Column.Build(new ModelOptions
        {
            SegmentCount = 80, PressureBroadeningExponent = exponent
        });

        double low = TestSupport.OpticalDepthBelow(broadened, 5_000);
        double reference = TestSupport.OpticalDepthBelow(wellMixed, 5_000);

        Assert.IsTrue(low > reference * 1.15,
            $"n = {exponent} must concentrate the absorber below 5 km ({low:F3} vs {reference:F3})");
    }

    [TestMethod]
    public void LargerBroadeningExponentConcentratesTheAbsorberFurther()
    {
        double Below5Km(double exponent) => TestSupport.OpticalDepthBelow(
            Column.Build(new ModelOptions
            {
                SegmentCount = 80, PressureBroadeningExponent = exponent
            }), 5_000);

        Assert.IsTrue(Below5Km(2.0) > Below5Km(1.0), "the exponent must act monotonically");
    }

    [TestMethod]
    public void OzoneHeatingWarmsTheStratosphere()
    {
        var without = TestSupport.Default;
        var with = TestSupport.Equilibrium("ozone", () => new ModelOptions { OzoneFraction = 0.3 });

        Assert.IsTrue(with.Converged, "the ozone-heated column must converge");
        Assert.IsTrue(
            TestSupport.TemperatureAt(with.Column, 25_000) >
            TestSupport.TemperatureAt(without.Column, 25_000) + 5.0,
            $"the layer must warm 25 km ({TestSupport.TemperatureAt(with.Column, 25_000):F1} vs " +
            $"{TestSupport.TemperatureAt(without.Column, 25_000):F1} K)");
    }

    [TestMethod]
    public void OzoneHeatingCreatesATemperatureInversion()
    {
        var result = TestSupport.Equilibrium("ozone", () => new ModelOptions { OzoneFraction = 0.3 });

        bool inversion = false;
        for (int i = 0; i < result.Column.Count - 1; i++)
        {
            var s = result.Column.Segments[i];
            if (s.MidAltitude is > 12_000 and < 30_000 &&
                result.Column.Segments[i + 1].Temperature > s.Temperature + 0.1)
            {
                inversion = true;
            }
        }

        Assert.IsTrue(inversion,
            "temperature must increase with altitude below the layer peak");
    }

    [TestMethod]
    public void OzoneRedistributionConservesTheAbsorbedSolarFlux()
    {
        var result = TestSupport.Equilibrium("ozone", () => new ModelOptions { OzoneFraction = 0.3 });

        double atmospheric = 0.0;
        foreach (var s in result.Column.Segments) atmospheric += s.ShortwaveAbsorbed;

        Assert.AreEqual(result.Column.Options.AbsorbedSolarFlux,
            atmospheric + result.Column.SurfaceShortwaveAbsorbed, 1e-9,
            "moving solar absorption around must not create or destroy any");
    }

    [TestMethod]
    public void WaterVapourLoadingFollowsClausiusClapeyron()
    {
        var column = Column.Build(new ModelOptions
        {
            SegmentCount = 40, TotalOpticalDepth = 1.0, WaterVapourOpticalDepth = 0.8
        });

        double expected = 0.8 * Math.Exp(PhysicalConstants.ClausiusClapeyronScale *
            (1.0 / 288.15 - 1.0 / ConvectionSolver.NearSurfaceAirTemperature(column)));

        Assert.AreEqual(1.0 + expected, column.TotalOpticalDepth(), 1e-6,
            "column tau = dry + Clausius-Clapeyron-scaled water vapour");
    }

    [TestMethod]
    public void WarmingMultipliesTheVapourLoadingByTheClausiusClapeyronFactor()
    {
        var column = Column.Build(new ModelOptions
        {
            SegmentCount = 40, TotalOpticalDepth = 1.0, WaterVapourOpticalDepth = 0.8
        });

        double before = column.TotalOpticalDepth() - 1.0;
        foreach (var s in column.Segments) s.Temperature += 5.0;
        column.DistributeOpticalDepth();
        double after = column.TotalOpticalDepth() - 1.0;

        double warmAir = ConvectionSolver.NearSurfaceAirTemperature(column);
        Assert.AreEqual(
            Math.Exp(PhysicalConstants.ClausiusClapeyronScale * (1.0 / (warmAir - 5.0) - 1.0 / warmAir)),
            after / before, 1e-6,
            "a 5 K warming must scale the loading by exp(L/Rv (1/T - 1/T'))");

        // About +6.5 %/K near 288 K compounds to roughly 1.38x over 5 K.
        Assert.IsTrue(after > before * 1.3,
            $"warming must materially increase the vapour loading ({before:F3} -> {after:F3})");
    }

    [TestMethod]
    public void WaterVapourMakesTheAbsorberProfileBottomHeavy()
    {
        // Its 2 km scale height is far shorter than the ~8 km density scale height, so the
        // combined profile must decay faster with altitude than the dry absorber alone.
        double Ratio(Column c) =>
            c.Segments[0].EmissionCoefficient / c.Segments[c.Count / 2].EmissionCoefficient;

        var dryOnly = Column.Build(new ModelOptions { SegmentCount = 40, TotalOpticalDepth = 1.0 });
        var withVapour = Column.Build(new ModelOptions
        {
            SegmentCount = 40, TotalOpticalDepth = 1.0, WaterVapourOpticalDepth = 0.8
        });

        Assert.IsTrue(Ratio(withVapour) > Ratio(dryOnly) * 2.0,
            "the vapour component must concentrate the absorber near the ground");
    }

    /// <summary>
    /// Compares climate sensitivity in K per W/m2, which normalises away the different
    /// instantaneous forcings of the two configurations. Both double the dry absorber; only
    /// the first has vapour free to respond.
    /// </summary>
    [TestMethod]
    public void WaterVapourFeedbackRaisesTheClimateSensitivity()
    {
        var withFeedback = FeedbackCase(dryTau: 1.0, wvTau: 0.8);
        var withoutFeedback = FeedbackCase(dryTau: 1.8, wvTau: 0.0);

        Assert.IsTrue(withFeedback.VapourAfter > withFeedback.VapourBefore * 1.05,
            $"warming must raise the equilibrium vapour loading " +
            $"({withFeedback.VapourBefore:F3} -> {withFeedback.VapourAfter:F3})");

        Assert.IsTrue(withFeedback.Sensitivity > withoutFeedback.Sensitivity * 1.1,
            $"the feedback must amplify the response ({withFeedback.Sensitivity:F3} vs " +
            $"{withoutFeedback.Sensitivity:F3} K per W/m2)");
    }

    private static (double Sensitivity, double VapourBefore, double VapourAfter) FeedbackCase(
        double dryTau, double wvTau)
    {
        var options = new ModelOptions
        {
            TotalOpticalDepth = dryTau,
            WaterVapourOpticalDepth = wvTau
        };
        var baseline = TestSupport.Equilibrium($"feedback-{dryTau}-{wvTau}", () => options);

        var perturbed = options.Clone();
        perturbed.OpticalDepthScale = 2.0;

        // Instantaneous forcing: baseline temperatures held, dry absorber doubled, vapour
        // re-evaluated at those held temperatures so it stays out of the forcing.
        var held = Column.Build(perturbed);
        for (int i = 0; i < held.Count; i++)
            held.Segments[i].Temperature = baseline.Column.Segments[i].Temperature;
        held.SurfaceTemperature = baseline.SurfaceTemperature;
        held.DistributeOpticalDepth();

        double forcing = baseline.Radiation.OutgoingLongwave -
                         RadiationSolver.Solve(held).OutgoingLongwave;

        var warmed = TestSupport.Equilibrium($"feedback-{dryTau}-{wvTau}-2x", () => perturbed);

        return ((warmed.SurfaceTemperature - baseline.SurfaceTemperature) / forcing,
                baseline.Column.CurrentWaterVapourOpticalDepth(),
                warmed.Column.CurrentWaterVapourOpticalDepth());
    }
}
