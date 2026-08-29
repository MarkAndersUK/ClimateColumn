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
    /// The result this was built to find. Nothing in the model imposes a forcing law - the band
    /// structure comes from line data and the absorber is exactly linear in concentration - so
    /// which law the response follows is the spectroscopy's answer, not the model's assumption.
    /// </summary>
    /// <remarks>
    /// CO2's band is saturated at its core, so extra gas acts only in the wings and the forcing
    /// grows as ln(C). Methane's 7.7 um band is weak and largely unsaturated, so extra gas acts
    /// across the whole band and the forcing grows as sqrt(M). Getting the second law out of the
    /// same machinery that produced the first is a sharper test than either alone.
    /// </remarks>
    [TestMethod]
    public void TheResponseIsCloserToASquareRootThanToALogarithm()
    {
        var sweep = MethaneSweep.Run(equilibrate: false);
        if (sweep is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        Assert.AreEqual(0.0, sweep.Forcings[0], 1e-9,
            "the reference concentration must force by exactly nothing");

        for (int i = 1; i < sweep.Points.Count; i++)
        {
            Assert.IsTrue(sweep.Forcings[i] > sweep.Forcings[i - 1],
                $"forcing should rise with methane ({MethaneSweep.Concentrations[i]:F0} ppb)");
        }

        var (root, log) = sweep.FitResiduals();

        Assert.IsTrue(root < log,
            $"the response should fit a square root better than a logarithm " +
            $"(residuals {root:E3} against {log:E3})");
    }
}
