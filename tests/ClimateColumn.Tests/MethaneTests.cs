using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Covers dialling methane: the per-band share that makes it possible, and the response that
/// comes out when it is raised above its base value.
/// </summary>
[TestClass]
public class MethaneTests
{
    private static SpectralBand Band(double co2, double methane, double tau = 2.0) => new()
    {
        ShortWavelength = 6e-6,
        LongWavelength = 9e-6,
        Label = "test",
        OpticalDepth = tau,
        Co2Fraction = co2,
        MethaneFraction = methane
    };

    [TestMethod]
    public void MethaneScalesOnlyItsOwnShareOfABand()
    {
        var band = Band(co2: 0.25, methane: 0.25, tau: 4.0);

        // Doubling methane raises only its quarter: 4 * (0.5 + 0.25*1 + 0.25*2) = 5.
        Assert.AreEqual(5.0, band.EffectiveOpticalDepth(1.0, 2.0), 1e-12);

        // Doubling CO2 instead does the same to the other quarter.
        Assert.AreEqual(5.0, band.EffectiveOpticalDepth(2.0, 1.0), 1e-12);

        // Both at once move both shares.
        Assert.AreEqual(6.0, band.EffectiveOpticalDepth(2.0, 2.0), 1e-12);
    }

    /// <summary>
    /// A band with no methane in it must behave exactly as it did before methane existed,
    /// whatever the methane concentration is set to.
    /// </summary>
    [TestMethod]
    public void ABandWithoutMethaneIsUntouchedByIt()
    {
        var band = Band(co2: 1.0, methane: 0.0, tau: 3.0);

        foreach (double ratio in new[] { 0.0, 0.5, 1.0, 5.0, 100.0 })
        {
            Assert.AreEqual(band.EffectiveOpticalDepth(1.7), band.EffectiveOpticalDepth(1.7, ratio),
                1e-12, $"methane at {ratio}x should not touch a band with none in it");
        }
    }

    /// <summary>The single-argument overload is the old behaviour exactly.</summary>
    [TestMethod]
    public void TheOldOverloadStillMeansMethaneAtItsReference()
    {
        var band = Band(co2: 0.4, methane: 0.3, tau: 5.0);

        Assert.AreEqual(band.EffectiveOpticalDepth(2.5, 1.0), band.EffectiveOpticalDepth(2.5), 1e-12);
    }

    /// <summary>
    /// Both gases live inside one OpticalDepth, so shares summing past one would manufacture
    /// opacity the moment either concentration moved.
    /// </summary>
    [TestMethod]
    public void RejectsSharesThatOversubscribeABand()
    {
        var options = new ModelOptions { Bands = new[] { Band(co2: 0.7, methane: 0.5) } };

        Assert.ThrowsException<ArgumentException>(() => options.Validate());
    }

    [DataTestMethod]
    [DataRow(-0.1)]
    [DataRow(1.1)]
    public void RejectsAnImpossibleMethaneShare(double share)
    {
        var options = new ModelOptions { Bands = new[] { Band(co2: 0.0, methane: share) } };

        Assert.ThrowsException<ArgumentException>(() => options.Validate());
    }

    [TestMethod]
    public void RejectsANegativeConcentration()
    {
        Assert.ThrowsException<ArgumentException>(
            () => new ModelOptions { MethaneConcentration = -1.0 }.Validate());
        Assert.ThrowsException<ArgumentException>(
            () => new ModelOptions { MethaneReferenceConcentration = 0.0 }.Validate());
    }

    /// <summary>
    /// The regression that matters most. Methane defaults to its reference, so the ratio is one
    /// and every existing configuration must be exactly what it was.
    /// </summary>
    [TestMethod]
    public void DefaultsToItsReferenceAndChangesNothing()
    {
        var options = new ModelOptions();

        Assert.AreEqual(1.0, options.MethaneConcentrationRatio, 0.0);

        var result = ColumnModel.RunToEquilibrium(options);
        Assert.AreEqual(286.797, result.SurfaceTemperature, 5e-4);
        Assert.AreEqual(238.175, result.Radiation.OutgoingLongwave, 5e-4);
    }

    /// <summary>
    /// The derivation records methane's share per band, and it lands where methane actually
    /// absorbs - the 7.7 um band, around 1300 cm^-1.
    /// </summary>
    [TestMethod]
    public void TheDerivationRecordsMethaneWhereItAbsorbs()
    {
        var configure = Co2Sweep.SpectralConfiguration();
        if (configure is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        var bands = configure(Co2Sweep.Concentrations[0]).Bands!;

        Assert.IsTrue(bands.Any(b => b.MethaneFraction > 0.1),
            "some band should carry a material methane share");

        foreach (var band in bands)
        {
            Assert.IsTrue(band.Co2Fraction + band.MethaneFraction <= 1.0 + 1e-9,
                $"{band.Label}: shares sum to {band.Co2Fraction + band.MethaneFraction:F3}");
        }

        // Methane's band is at 7.7 um; CO2's is at 15. The band carrying the most methane must
        // be the shorter-wavelength one, or the shares have been attached to the wrong gas.
        var richest = bands.OrderByDescending(b => b.MethaneFraction).First();
        var co2Richest = bands.Where(b => !b.IsRemainder)
            .OrderByDescending(b => b.OpticalDepth * b.Co2Fraction).First();

        Assert.IsTrue(richest.LongWavelength < co2Richest.LongWavelength,
            $"methane's richest band ({richest.Label}) should sit shortward of CO2's " +
            $"({co2Richest.Label})");
    }

    /// <summary>
    /// Methane's present-day forcing is calibrated to observation, so this pins the calibration
    /// rather than testing a prediction.
    /// </summary>
    /// <remarks>
    /// Worth a test precisely because it is a calibration: it is a constant that will drift
    /// silently the next time the absorber recipe or the wing treatment changes, and nothing
    /// else would notice. <see cref="Co2Sweep.CalibratedMethaneShare"/> and the absorber scale
    /// were solved together for exactly this.
    /// </remarks>
    [TestMethod]
    public void PresentDayForcingMatchesObservation()
    {
        var sweep = MethaneSweep.Run(equilibrate: false);
        if (sweep is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        int now = MethaneSweep.PresentDayIndex;

        Assert.AreEqual(0.0, sweep.Forcings[0], 1e-9,
            "the reference concentration must force by exactly nothing");

        Assert.AreEqual(sweep.AcceptedForcing(now), sweep.Forcings[now], 0.02,
            $"700 to {MethaneSweep.PresentDayPpb:F0} ppb should give the accepted forcing, " +
            "since the methane share was calibrated to make it so");

        for (int i = 1; i < sweep.Points.Count; i++)
        {
            Assert.IsTrue(sweep.Forcings[i] > sweep.Forcings[i - 1],
                $"forcing should rise with methane ({MethaneSweep.Concentrations[i]:F0} ppb)");
        }
    }

    /// <summary>
    /// The shape, which is the part that is <em>not</em> calibrated - and it comes out wrong in a
    /// way worth recording.
    /// </summary>
    /// <remarks>
    /// A band's forcing law follows its saturation: optically thick gives ln(M), partly saturated
    /// gives sqrt(M), genuinely thin gives M. The real atmosphere shows sqrt(M), so real methane
    /// is partly saturated. This model comes out nearly linear - a weaker curvature - and so
    /// over-predicts away from the point it was calibrated at: 1.00x accepted at 1900 ppb by
    /// construction, but 1.15x at 3500.
    ///
    /// The obvious explanation is wrong and the test records that too, because it cost time. The
    /// bands are not thin: methane's hemispheric optical depth is 1.07 and 2.39 in the two bands
    /// it occupies, which is squarely the partly-saturated regime that should give sqrt(M).
    /// Whatever is flattening the response is not a lack of methane opacity.
    /// </remarks>
    [TestMethod]
    public void TheResponseIsFlatterThanTheObservedSquareRoot()
    {
        var sweep = MethaneSweep.Run(equilibrate: false);
        if (sweep is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        var fits = sweep.LawFits().OrderBy(f => f.Residual).ToArray();

        Assert.AreEqual("linear in M", fits[0].Name,
            "the model's methane response is nearly linear: " +
            string.Join(", ", fits.Select(f => $"{f.Name} {f.Residual:F4}")));

        // Still better than a logarithm, which is the one thing it clearly is not.
        var log = sweep.LawFits().Single(f => f.Name == "ln M");
        var root = sweep.LawFits().Single(f => f.Name == "√M");
        Assert.IsTrue(root.Residual < log.Residual,
            $"a square root should still beat a logarithm ({root.Residual:F4} against {log.Residual:F4})");

        // The consequence of the flatter shape: it over-predicts beyond the calibration point.
        int last = sweep.Points.Count - 1;
        double ratio = sweep.Forcings[last] / sweep.AcceptedForcing(last);
        Assert.IsTrue(ratio > 1.05,
            $"a flatter-than-sqrt response should over-predict at the top of the sweep " +
            $"(got {ratio:F3}x accepted at {MethaneSweep.Concentrations[last]:F0} ppb)");
    }

    /// <summary>
    /// What the flat response actually was: an artefact of scaling a band mean while its
    /// k-distribution stayed at the reference.
    /// </summary>
    /// <remarks>
    /// Dialling methane through each band's recorded share stretches the band mean, but the
    /// distribution inside it goes on describing an atmosphere with the reference amount in it -
    /// so the saturation that ought to develop in methane's strong lines never appears, and the
    /// response comes out nearly linear.
    ///
    /// Re-deriving the bands at each concentration removes that, and the law changes: sqrt(M)
    /// goes from second place to first and beats a straight line by a factor of nearly thirty.
    /// The observed law is sqrt(M), so the physics was there all along and the approximation was
    /// hiding it.
    ///
    /// Re-derivation is not the default because its magnitude is uncalibrated - it forces 1.51x
    /// the accepted value at present-day methane, since the share was calibrated in the scaled
    /// mode. Making it default needs its own joint calibration of share and absorber scale.
    /// </remarks>
    [TestMethod]
    public void RederivingTheBandsRecoversTheSquareRootLaw()
    {
        var scaled = MethaneSweep.Run(equilibrate: false, rederive: false);
        var derived = MethaneSweep.Run(equilibrate: false, rederive: true);

        if (scaled is null || derived is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        Assert.AreEqual("linear in M", scaled.BestFit().Name,
            "scaling the band share should give the flat, nearly linear response");

        Assert.AreEqual("√M", derived.BestFit().Name,
            "re-deriving should recover the observed square-root law: " +
            string.Join(", ", derived.LawFits().Select(f => $"{f.Name} {f.Residual:F4}")));

        var root = derived.LawFits().Single(f => f.Name == "√M");
        var linear = derived.LawFits().Single(f => f.Name == "linear in M");

        Assert.IsTrue(linear.Residual > 10.0 * root.Residual,
            $"re-derived, a square root should beat a straight line decisively " +
            $"({root.Residual:F4} against {linear.Residual:F4})");
    }

    /// <summary>
    /// The measurement that ruled out the easy explanation, kept so the next person does not
    /// have to make it again.
    /// </summary>
    [TestMethod]
    public void MethanesBandsAreNotOpticallyThin()
    {
        var configure = Co2Sweep.SpectralConfiguration();
        if (configure is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        var options = configure(Co2Sweep.Concentrations[0]);
        double d = options.Diffusivity;

        double thickest = options.Bands!
            .Where(b => b.MethaneFraction > 0.01)
            .Max(b => d * b.OpticalDepth * b.MethaneFraction);

        Assert.IsTrue(thickest > 1.0,
            $"methane's richest band should be optically thick enough to curve the response " +
            $"(hemispheric tau {thickest:F3}) - so a nearly linear response is not explained by " +
            "the band being thin");
    }
}
