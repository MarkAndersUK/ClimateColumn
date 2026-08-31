using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// End-to-end behaviour of the marched model: that equilibrium closes every budget, that
/// the qualitative responses go the right way, and that the answer does not depend on the
/// grid.
/// </summary>
[TestClass]
public class FullModelTests
{
    [TestMethod]
    public void DefaultRunConvergesWithBothBudgetsClosed()
    {
        var result = TestSupport.Default;

        Assert.IsTrue(result.Converged, "the default configuration must reach equilibrium");
        Assert.IsTrue(Math.Abs(result.TopOfAtmosphereImbalance) < 1e-4,
            $"TOA imbalance must be negligible ({result.TopOfAtmosphereImbalance:E2} W/m2)");
        Assert.IsTrue(Math.Abs(result.SurfaceImbalance) < 1e-4,
            $"surface imbalance must be negligible ({result.SurfaceImbalance:E2} W/m2)");
        Assert.AreEqual(result.Column.Options.AbsorbedSolarFlux,
            result.Radiation.OutgoingLongwave, 1e-4,
            "the column must export exactly what it absorbs");
    }

    [TestMethod]
    public void SegmentsAboveTheConvectingLayerAreInRadiativeBalance()
    {
        var result = TestSupport.Default;
        var netRates = result.NetHeatingRatesPerDay();

        for (int i = 0; i < result.Column.Count; i++)
        {
            if (result.Column.Segments[i].MidAltitude > 25_000)
            {
                Assert.IsTrue(Math.Abs(netRates[i]) < 1e-6,
                    $"segment {i} above the convecting layer is not in radiative balance");
            }
        }
    }

    /// <summary>
    /// At equilibrium the surface budget reads SW + eps F_down - eps sigma Ts^4 =
    /// h_c (Ts - T_air), so the sol-air temperature must collapse onto the surface
    /// temperature. It is a check on the budget rather than a prediction.
    /// </summary>
    [TestMethod]
    public void SolAirTemperatureCollapsesOntoTheSurfaceTemperature()
    {
        var result = TestSupport.Default;

        Assert.AreEqual(result.SurfaceTemperature, result.SolAirTemperature, 1e-6,
            "sol-air must equal T_surface at equilibrium");
    }

    [TestMethod]
    public void ColumnLongwaveDivergenceBalancesAtmosphericSolarAndSensibleHeat()
    {
        var result = TestSupport.Default;

        double columnLw = result.Radiation.SurfaceUpwardFlux - result.Radiation.SurfaceDownwardFlux;
        double atmosphericSolar = result.Column.Options.AbsorbedSolarFlux -
                                  result.Column.SurfaceShortwaveAbsorbed;

        Assert.AreEqual(atmosphericSolar + result.SensibleHeatFlux,
            result.Radiation.OutgoingLongwave - columnLw, 1e-4,
            "everything the air gains non-radiatively must leave as longwave");
    }

    private static ModelResult Radiative => TestSupport.Equilibrium("convection-none",
        () => new ModelOptions { Convection = ConvectionMode.None });

    private static ModelResult SurfaceOnly => TestSupport.Equilibrium("convection-surface",
        () => new ModelOptions { Convection = ConvectionMode.SurfaceOnly });

    [TestMethod]
    public void ConvectionCoolsTheSurface()
    {
        var full = TestSupport.Default;

        Assert.IsTrue(Radiative.SurfaceTemperature > full.SurfaceTemperature,
            $"pure radiative equilibrium must be hotter ({Radiative.SurfaceTemperature:F2} vs " +
            $"{full.SurfaceTemperature:F2} K)");
        Assert.IsTrue(SurfaceOnly.SurfaceTemperature - full.SurfaceTemperature > 0.5,
            $"the lapse-rate adjustment must cool further than h_c alone " +
            $"({SurfaceOnly.SurfaceTemperature:F2} vs {full.SurfaceTemperature:F2} K)");
    }

    [TestMethod]
    public void EveryGreenhouseCaseIsWarmerThanTheEmissionTemperature()
    {
        Assert.IsTrue(Radiative.GreenhouseWarming > 0, "radiative-only must show warming");
        Assert.IsTrue(TestSupport.Default.GreenhouseWarming > 0, "the full model must show warming");
    }

    [TestMethod]
    public void ConvectingCaseHasAPlausibleSurfaceTemperature()
    {
        double ts = TestSupport.Default.SurfaceTemperature;

        Assert.IsTrue(ts is > 270 and < 305, $"surface temperature out of range ({ts:F2} K)");
    }

    [TestMethod]
    public void ConvectingLayerFormsOnlyWhenTheAdjustmentIsEnabled()
    {
        Assert.IsTrue(TestSupport.Default.ConvectiveTopAltitude > 1000.0,
            $"a convecting layer must form ({TestSupport.Default.ConvectiveTopAltitude / 1000.0:F2} km)");
        Assert.AreEqual(0.0, SurfaceOnly.ConvectiveTopAltitude, 0.0,
            "no layer may be reported when only the surface flux is active");
        Assert.AreEqual(0.0, Radiative.ConvectiveTopAltitude, 0.0,
            "no layer may be reported in pure radiative equilibrium");
    }

    [TestMethod]
    public void MoreOpaqueColumnsConvectDeeper()
    {
        var thick = TestSupport.Equilibrium("tau-5",
            () => new ModelOptions { TotalOpticalDepth = 5.0 });

        Assert.IsTrue(thick.ConvectiveTopAltitude > TestSupport.Default.ConvectiveTopAltitude,
            $"radiative equilibrium is more unstable at higher opacity " +
            $"({thick.ConvectiveTopAltitude / 1000.0:F2} vs " +
            $"{TestSupport.Default.ConvectiveTopAltitude / 1000.0:F2} km)");
    }

    /// <summary>
    /// The convective top must not be quantised to the segment grid.
    /// </summary>
    /// <remarks>
    /// It used to be. The getter returned the mid-altitude of the last segment sitting on the
    /// critical lapse rate, which is biased half a segment low and can only take one of
    /// SegmentCount values - so across a CO2 sweep the reported top did not move at all, and that
    /// was written up as the physics having stopped responding. It had not; the diagnostic could
    /// not see it.
    ///
    /// This asserts the property that was violated rather than a value: two columns whose
    /// convecting layers genuinely differ must report different tops, even when the difference is
    /// smaller than a segment. A return to grid snapping makes them equal and fails here.
    /// </remarks>
    [TestMethod]
    public void ConvectiveTopIsNotSnappedToTheSegmentGrid()
    {
        var tops = new List<double>();
        foreach (double co2 in new[] { 285.0, 1000.0, 4000.0 })
        {
            var options = new ModelOptions();
            options.Co2Concentration = co2;
            tops.Add(ColumnModel.RunToEquilibrium(options).ConvectiveTopAltitude);
        }

        Console.WriteLine(string.Join("  ", tops.Select(t => (t / 1000.0).ToString("F3") + " km")));

        Assert.IsTrue(tops.Distinct().Count() == tops.Count,
            "the convective top repeated across concentrations, which is what grid snapping " +
            $"looks like: {string.Join(", ", tops.Select(t => (t / 1000.0).ToString("F3")))} km");

        Assert.IsTrue(tops[2] > tops[0],
            $"more CO2 should not lower the convective top ({tops[0]:F0} m to {tops[2]:F0} m)");

        // A grid mid-altitude would land on an exact multiple of the segment thickness. The
        // interpolated value should not, except by coincidence at one concentration.
        var result = ColumnModel.RunToEquilibrium(new ModelOptions());
        double thickness = result.Column.Segments[0].Thickness;
        double offGrid = result.ConvectiveTopAltitude / thickness;

        Assert.IsTrue(Math.Abs(offGrid - Math.Round(offGrid)) > 1e-9,
            $"the top {result.ConvectiveTopAltitude:F1} m is an exact multiple of the " +
            $"{thickness:F1} m segment thickness, which suggests it is still snapped");
    }

    /// <summary>
    /// Solar absorption in the air is switched off so that tau = 0 remains a legitimate
    /// configuration with a genuine equilibrium.
    /// </summary>
    [TestMethod]
    public void SurfaceTemperatureIncreasesMonotonicallyWithOpticalDepth()
    {
        double previous = double.NegativeInfinity;

        foreach (double tau in new[] { 0.0, 0.5, 1.0, 2.0, 4.0 })
        {
            var result = TestSupport.Equilibrium($"sweep-{tau}", () => new ModelOptions
            {
                TotalOpticalDepth = tau,
                AtmosphericShortwaveFraction = 0.0
            });

            Assert.IsTrue(result.Converged, $"tau = {tau} must reach equilibrium");
            Assert.IsTrue(result.SurfaceTemperature > previous,
                $"tau = {tau} did not warm the surface further ({result.SurfaceTemperature:F3} K)");

            previous = result.SurfaceTemperature;
        }
    }

    [TestMethod]
    public void DoublingOpacityWarmsTheSurfaceSubstantially()
    {
        var thin = TestSupport.Default;
        var thick = TestSupport.Equilibrium("tau-3.6",
            () => new ModelOptions { TotalOpticalDepth = 3.6 });

        Assert.IsTrue(thick.SurfaceTemperature - thin.SurfaceTemperature > 1.0,
            $"doubling opacity must warm the surface " +
            $"({thick.SurfaceTemperature - thin.SurfaceTemperature:F2} K)");
    }

    [TestMethod]
    public void TransparentAirThatAbsorbsSunlightIsRejected()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            ColumnModel.RunToEquilibrium(new ModelOptions
            {
                TotalOpticalDepth = 0.0,
                AtmosphericShortwaveFraction = 0.22
            }),
            "a configuration with no equilibrium must be rejected, not silently run away");
    }

    [TestMethod]
    public void TransparentAirWithNoSolarAbsorptionIsAccepted()
    {
        var result = TestSupport.Equilibrium("transparent-no-sw", () => new ModelOptions
        {
            TotalOpticalDepth = 0.0,
            AtmosphericShortwaveFraction = 0.0,
            Convection = ConvectionMode.None
        });

        Assert.IsTrue(result.Converged, "the same column without solar absorption is legitimate");
    }

    private static ModelResult GreyBody => TestSupport.Equilibrium("eps-0.9",
        () => new ModelOptions { SurfaceEmissivity = 0.9 });

    [TestMethod]
    public void SurfaceEmissionFollowsStefanBoltzmann()
    {
        Assert.AreEqual(
            0.9 * PhysicalConstants.StefanBoltzmann * Math.Pow(GreyBody.SurfaceTemperature, 4),
            GreyBody.SurfaceEmission, 1e-9,
            "surface emission = eps_s sigma Ts^4");
    }

    [TestMethod]
    public void UpwardFluxAtTheSurfaceIncludesTheReflectedBackRadiation()
    {
        Assert.AreEqual(
            GreyBody.SurfaceEmission + 0.1 * GreyBody.Radiation.SurfaceDownwardFlux,
            GreyBody.Radiation.SurfaceUpwardFlux, 1e-9,
            "a non-black surface reflects (1 - eps_s) of the incident longwave");
    }

    /// <summary>
    /// The greenhouse flux must use the surface's own emission, not the upward flux at the
    /// surface, which also carries the reflected share of the back radiation.
    /// </summary>
    [TestMethod]
    public void GreenhouseFluxExcludesTheReflectedBackRadiation()
    {
        Assert.AreEqual(GreyBody.SurfaceEmission - GreyBody.Radiation.OutgoingLongwave,
            GreyBody.GreenhouseFlux, 1e-12,
            "greenhouse flux = surface emission - OLR");

        Assert.IsTrue(
            Math.Abs(GreyBody.GreenhouseFlux -
                     (GreyBody.Radiation.SurfaceUpwardFlux - GreyBody.Radiation.OutgoingLongwave)) > 1.0,
            "the reflected term must make a material difference, or this proves nothing");
    }

    /// <summary>
    /// With the near-surface extrapolation in place the remaining discretisation error is
    /// second order and already negligible at the coarsest grid tested, so these assert on
    /// the spread rather than on an inferred order, which is pure noise at this level.
    /// </summary>
    [TestMethod]
    public void ResultIsGridConverged()
    {
        var study = GridConvergence.Study(new ModelOptions { SegmentCount = 20 }, 5);

        double min = double.PositiveInfinity, max = double.NegativeInfinity;
        foreach (var level in study.Levels)
        {
            min = Math.Min(min, level.SurfaceTemperature);
            max = Math.Max(max, level.SurfaceTemperature);
        }

        Assert.IsTrue(max - min < 0.1, $"20 to 320 segments must span less than 0.1 K ({max - min:F4} K)");
        Assert.IsTrue(Math.Abs(study.Differences[^1]) < 0.005,
            $"the two finest grids must agree to 0.005 K ({Math.Abs(study.Differences[^1]):F5} K)");
        Assert.IsTrue(
            Math.Abs(TestSupport.Default.SurfaceTemperature - study.Levels[^1].SurfaceTemperature) < 0.02,
            "the default resolution must be within 0.02 K of the finest grid");
    }

    [TestMethod]
    public void ThickColumnStillConvergesUnderRefinement()
    {
        // At large optical depth the per-segment temperature gradient is steeper, so the
        // error is bigger and the refinement has visible work to do.
        var study = GridConvergence.Study(
            new ModelOptions { SegmentCount = 20, TotalOpticalDepth = 8.0 }, 4);

        Assert.IsTrue(Math.Abs(study.Differences[^1]) < Math.Abs(study.Differences[0]),
            $"refinement must reduce the error ({Math.Abs(study.Differences[0]):F3} -> " +
            $"{Math.Abs(study.Differences[^1]):F3} K)");
    }

    [TestMethod]
    public void ElsasserDiffusivityRunsAndStaysPhysical()
    {
        var result = TestSupport.Equilibrium("D=1.66", () => new ModelOptions
        {
            Diffusivity = PhysicalConstants.ElsasserDiffusivity
        });

        Assert.IsTrue(result.Converged, "D = 1.66 must still converge");
        Assert.IsTrue(result.SurfaceTemperature is > 200 and < 400,
            $"D = 1.66 must stay physical ({result.SurfaceTemperature:F2} K)");
    }
}
