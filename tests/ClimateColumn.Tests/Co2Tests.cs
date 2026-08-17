using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The CO2 concentration front end, and an honest account of what the grey model does and
/// does not get right about the response to it.
/// </summary>
[TestClass]
public class Co2Tests
{
    [TestMethod]
    public void ReferenceConcentrationLeavesTheAbsorberUnchanged()
    {
        var options = new ModelOptions { TotalOpticalDepth = 2.0, Co2AbsorberFraction = 1.0 };

        Assert.AreEqual(2.0, options.EffectiveDryOpticalDepth, 1e-12,
            "at C = C_ref the concentration scaling must be the identity");
    }

    /// <summary>
    /// Optical depth is linear in absorber amount, so doubling the ppm doubles the CO2 share
    /// and leaves the rest of the dry absorber alone.
    /// </summary>
    [DataTestMethod]
    [DataRow(1.0, 4.0)]
    [DataRow(0.25, 2.5)]
    [DataRow(0.0, 2.0)]
    public void ConcentrationScalesOnlyTheCo2ShareOfTheAbsorber(
        double co2Fraction, double expected)
    {
        var options = new ModelOptions
        {
            TotalOpticalDepth = 2.0,
            Co2AbsorberFraction = co2Fraction,
            Co2Concentration = 570.0,
            Co2ReferenceConcentration = 285.0
        };

        Assert.AreEqual(expected, options.EffectiveDryOpticalDepth, 1e-12,
            $"share {co2Fraction}: tau = 2.0 x ((1 - f) + f x 2)");
    }

    /// <summary>
    /// Co2AbsorberFraction is a fixed input describing the reference state, not a state
    /// variable: raising the concentration must not change it. The fraction the column
    /// actually ends up with does rise, because the CO2 component grows while the other
    /// well-mixed gases do not - and that is the whole mechanism, so both halves are pinned
    /// down here.
    /// </summary>
    [TestMethod]
    public void Co2AbsorberFractionIsFixedButTheRealisedShareGrows()
    {
        var options = new ModelOptions { TotalOpticalDepth = 1.8, Co2AbsorberFraction = 0.06 };

        double nonCo2 = 1.8 * 0.94;   // held fixed by construction
        double RealisedShare()
        {
            double total = options.EffectiveDryOpticalDepth;
            return (total - nonCo2) / total;
        }

        Assert.AreEqual(0.06, options.Co2AbsorberFraction, 1e-12,
            "the parameter itself must not move");
        Assert.AreEqual(0.06, RealisedShare(), 1e-9,
            "at the reference concentration the realised share equals the parameter");

        options.Co2Concentration = 425.0;

        Assert.AreEqual(0.06, options.Co2AbsorberFraction, 1e-12,
            "raising the concentration must not rewrite the input");

        // f r / ((1 - f) + f r) with f = 0.06, r = 425/285.
        double r = 425.0 / 285.0;
        Assert.AreEqual(0.06 * r / (0.94 + 0.06 * r), RealisedShare(), 1e-9,
            "CO2 must take a larger share of the absorber as it accumulates");
        Assert.IsTrue(RealisedShare() > 0.06, "the realised share must grow, not stay put");
    }

    [TestMethod]
    public void BuiltColumnCarriesTheConcentrationScaledOpticalDepth()
    {
        var column = Column.Build(new ModelOptions
        {
            TotalOpticalDepth = 1.8, Co2Concentration = 425.0, Co2ReferenceConcentration = 285.0
        });

        Assert.AreEqual(1.8 * 425.0 / 285.0, column.TotalOpticalDepth(), 1e-9,
            "the scaling must reach the column that is actually solved");
    }

    [TestMethod]
    public void MoreCo2WarmsTheSurface()
    {
        var preIndustrial = TestSupport.Equilibrium("co2-285",
            () => new ModelOptions { Co2AbsorberFraction = 0.06 });
        var present = TestSupport.Equilibrium("co2-425",
            () => new ModelOptions { Co2AbsorberFraction = 0.06, Co2Concentration = 425.0 });

        Assert.IsTrue(present.SurfaceTemperature > preIndustrial.SurfaceTemperature,
            $"285 -> 425 ppm must warm the surface " +
            $"({present.SurfaceTemperature - preIndustrial.SurfaceTemperature:F3} K)");
    }

    /// <summary>
    /// Forcing between two concentrations, optionally under a transparent window given as a
    /// wavelength interval in microns. A zero-width interval means no window.
    /// </summary>
    private static double ForcingBetween(double from, double to,
        double windowFromMicrons = 0.0, double windowToMicrons = 0.0)
    {
        var baseline = new ModelOptions
        {
            WindowShortWavelength = windowFromMicrons * 1e-6,
            WindowLongWavelength = windowToMicrons * 1e-6,
            Co2Concentration = from
        };
        var perturbed = baseline.Clone();
        perturbed.Co2Concentration = to;
        return TestSupport.InstantaneousForcing(baseline, perturbed);
    }

    /// <summary>
    /// Real CO2 forcing is very nearly logarithmic, so successive doublings should force
    /// about equally. The grey column does saturate - its emission level rises into thinner
    /// air - but oversaturates, each doubling buying roughly half the last.
    /// </summary>
    [TestMethod]
    public void SuccessiveDoublingsGiveDiminishingForcing()
    {
        double ratio = ForcingBetween(570, 1140) / ForcingBetween(285, 570);

        Assert.IsTrue(ratio < 0.9, $"the grey column must saturate with concentration (ratio {ratio:F3})");
    }

    /// <summary>
    /// This is what makes the window a legitimate calibration knob rather than a distortion:
    /// it lowers the magnitude of the forcing without materially reshaping its concentration
    /// dependence.
    /// </summary>
    /// <remarks>
    /// Under a flat window fraction the saturation ratio was preserved to machine precision,
    /// because f factored out of every source term and cancelled in the ratio. With f
    /// following each emitter's temperature the cancellation is no longer exact - the two
    /// doublings shift the emission level, and the window share differs slightly between the
    /// levels they emit from. The shape is still very nearly preserved, which is the property
    /// that matters, but the honest tolerance is a small percentage rather than 1e-9.
    /// </remarks>
    [TestMethod]
    public void WindowNearlyPreservesTheSaturationRatio()
    {
        double grey = ForcingBetween(570, 1140) / ForcingBetween(285, 570);
        double windowed = ForcingBetween(570, 1140, 8.0, 13.0) / ForcingBetween(285, 570, 8.0, 13.0);

        Assert.AreEqual(grey, windowed, 0.03 * grey,
            $"the window should not materially reshape the concentration dependence " +
            $"(grey {grey:F4} vs windowed {windowed:F4})");
    }

    /// <summary>
    /// The window suppresses the CO2 forcing, and the suppression is stronger than the
    /// surface's own window share alone would give.
    /// </summary>
    /// <remarks>
    /// The forcing is linear in the per-segment band weights but with mixed signs - the
    /// surface term raises it, atmospheric emission compensates and lowers it - so the
    /// suppression is not a convex combination of the (1 - f) values and is not bracketed by
    /// them. See the corresponding note in ExtendedPhysicsTests.
    /// </remarks>
    [TestMethod]
    public void WindowSuppressesTheCo2Forcing()
    {
        double grey = ForcingBetween(285, 425);
        double windowed = ForcingBetween(285, 425, 8.0, 13.0);
        double suppression = windowed / grey;

        var options = new ModelOptions
        {
            WindowShortWavelength = 8e-6, WindowLongWavelength = 13e-6
        };
        double surfaceOnly = 1.0 - options.WindowShare(286.8);

        Assert.IsTrue(suppression < 1.0,
            $"a window must suppress the forcing (factor {suppression:F4})");
        Assert.IsTrue(suppression < surfaceOnly,
            $"the colder emitters should deepen the cut beyond the surface-only prediction " +
            $"({suppression:F4} vs {surfaceOnly:F4})");
        Assert.IsTrue(suppression is > 0.4 and < 0.9,
            $"suppression {suppression:F4} is outside the plausible range for an 8-13 um window");
    }

    /// <summary>
    /// Magnitude, not shape, is the grey model's real failure: one doubling should force
    /// about 3.7 W/m2 and instead forces an order of magnitude more. This is why the
    /// concentration runs in the README have to be calibrated against a known forcing rather
    /// than read off directly.
    /// </summary>
    [TestMethod]
    public void GreyDoublingForcingIsFarTooLarge()
    {
        double forcing = ForcingBetween(285, 570, 0.0);

        Assert.IsTrue(forcing > 10.0 * 3.7,
            $"the documented failure must still be present ({forcing:F1} vs ~3.7 W/m2)");
    }

    /// <summary>
    /// A small Co2AbsorberFraction fixes the magnitude of the forcing at one concentration
    /// but not its shape: the CO2 component then grows linearly against a large fixed
    /// remainder, so each doubling adds twice the absolute optical depth the last one did
    /// and buys MORE forcing, where the real gas buys the same every time. This is why the
    /// calibration must not be extrapolated, and it is the opposite behaviour to the
    /// undiluted f = 1 case, so both are pinned here to keep them from being conflated.
    /// </summary>
    [TestMethod]
    public void DilutedCo2LosesTheLogarithmicShape()
    {
        var reference = new ModelOptions { Co2AbsorberFraction = 0.06 };

        double ForcingFromReference(double ppm)
        {
            var perturbed = reference.Clone();
            perturbed.Co2Concentration = ppm;
            return TestSupport.InstantaneousForcing(reference, perturbed);
        }

        double first = ForcingFromReference(570);
        double second = ForcingFromReference(1140) - first;
        double third = ForcingFromReference(2280) - ForcingFromReference(1140);

        Assert.IsTrue(second > first * 1.5,
            $"a diluted absorber must buy MORE per doubling ({first:F2} then {second:F2} W/m2)");
        Assert.IsTrue(third > second * 1.5,
            $"and more again ({second:F2} then {third:F2} W/m2)");

        // Undiluted, the same model saturates instead - the contrast is the whole point.
        double undilutedFirst = ForcingBetween(285, 570, 0.0);
        double undilutedSecond = ForcingBetween(570, 1140, 0.0);

        Assert.IsTrue(undilutedSecond < undilutedFirst,
            $"at f = 1 the model must still saturate ({undilutedFirst:F2} then " +
            $"{undilutedSecond:F2} W/m2)");
    }
}
