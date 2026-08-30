using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Covers the configuration built from observed abundances rather than a fitted absorber scale.
/// </summary>
/// <remarks>
/// These tests pin a <em>measurement</em>, not a target. The point of the configuration is that
/// nothing in it was chosen to make the surface come out anywhere, so the bounds here are wide
/// and exist to catch a unit error or a broken derivation - not to hold the answer in place.
/// </remarks>
[TestClass]
public class EarthlikeConfigurationTests
{
    /// <summary>
    /// The unit chain, checked against a figure from outside the model: 285 ppm of CO2 over a
    /// standard atmosphere is about 6.1e21 molecules per cm^2.
    /// </summary>
    /// <remarks>
    /// This is the test that matters most. Everything else in the configuration follows from
    /// column densities being right, and a units slip would produce a plausible-looking model
    /// that was quietly wrong by orders of magnitude.
    /// </remarks>
    [TestMethod]
    public void ColumnDensitiesMatchTheStandardFigures()
    {
        double co2 = EarthlikeConfiguration.WellMixedColumn(285e-6);
        Assert.AreEqual(6.1e21, co2, 0.2e21,
            $"285 ppm of CO2 should be about 6.1e21 per cm^2 (got {co2:E3})");

        // The whole air column, which the above is a fraction of.
        double air = EarthlikeConfiguration.AirColumnDensity();
        Assert.AreEqual(2.15e25, air, 0.1e25, $"air column (got {air:E3})");

        // Ozone: 300 DU by definition of the Dobson unit.
        Assert.AreEqual(300.0 * 2.6867e16, EarthlikeConfiguration.OzoneColumnDensity(), 1e14);
    }

    /// <summary>
    /// It reaches an equilibrium, and one in the right region - without any absorber scale to
    /// put it there.
    /// </summary>
    [TestMethod]
    public void ReachesAnEarthlikeSurfaceWithNothingFitted()
    {
        var configure = EarthlikeConfiguration.Build();
        if (configure is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        var result = ColumnModel.RunToEquilibrium(configure(EarthlikeConfiguration.Co2Ppm));

        Assert.IsTrue(result.Converged, "the unfitted column should still reach equilibrium");

        // Wide on purpose. Measured at 289.9 K; anything in this range is the same result, and
        // anything outside it means the unit chain or the derivation has broken rather than that
        // the physics has moved.
        Assert.AreEqual(290.0, result.SurfaceTemperature, 8.0,
            $"unfitted surface temperature (got {result.SurfaceTemperature:F3} K)");

        // At equilibrium the outgoing longwave equals the absorbed solar by construction, so
        // this is a check on the solver rather than on the spectroscopy.
        Assert.AreEqual(result.Column.Options.AbsorbedSolarFlux,
            result.Radiation.OutgoingLongwave, 0.01);
    }

    /// <summary>
    /// No absorber scale appears anywhere in it - which is the entire point, and the thing most
    /// likely to be undone by someone reaching for a familiar knob.
    /// </summary>
    [TestMethod]
    public void CarriesNoFittedAbsorberScale()
    {
        var configure = EarthlikeConfiguration.Build();
        if (configure is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        var options = configure(EarthlikeConfiguration.Co2Ppm);

        Assert.AreEqual(1.0, options.OpticalDepthScale, 0.0,
            "the physical configuration must not scale its absorbers");

        // And no single-band water vapour on top of the vapour already in the bands, which
        // would count the same gas twice.
        Assert.AreEqual(0.0, options.WaterVapourOpticalDepth, 0.0,
            "water vapour is in the bands at its observed column; adding the grey absorber " +
            "would double it");
    }

    /// <summary>
    /// Water vapour dominates the column, as it does on Earth - and by a much larger margin
    /// than the fitted recipe's relative amounts implied.
    /// </summary>
    [TestMethod]
    public void WaterVapourDominatesTheOpticalDepth()
    {
        var inventory = EarthlikeConfiguration.Inventory();
        if (inventory is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        double total = inventory.Sum(r => r.OpticalDepth);
        double water = inventory.Where(r => r.Gas.Contains("h2o")).Sum(r => r.OpticalDepth);

        Assert.IsTrue(water / total > 0.9,
            $"water vapour should carry most of the opacity (got {water / total:P1})");

        // CO2 is the next largest, and the rest are minor.
        var co2 = inventory.Single(r => r.Gas.Contains("co2")).OpticalDepth;
        foreach (var row in inventory.Where(r => !r.Gas.Contains("h2o") && !r.Gas.Contains("co2")))
        {
            Assert.IsTrue(row.OpticalDepth < co2,
                $"{row.Gas} should be smaller than CO2 ({row.OpticalDepth:F4} against {co2:F4})");
        }
    }
}
