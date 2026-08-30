using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Covers the sub-Lorentzian far-wing correction: the shape itself, that it reaches the line
/// profile, and that it does what it was added to do.
/// </summary>
[TestClass]
public class ChiFactorTests
{
    private static readonly ChiFactor Chi = ChiFactor.CarbonDioxideNu2;

    [TestMethod]
    public void IsUnityInsideTheImpactRegion()
    {
        Assert.AreEqual(1.0, Chi.At(0.0), 0.0);
        Assert.AreEqual(1.0, Chi.At(Chi.FirstBreak), 0.0);
        Assert.AreEqual(1.0, Chi.At(-Chi.FirstBreak), 0.0, "the correction is symmetric in detuning");
    }

    /// <summary>
    /// A discontinuity at a breakpoint would put a step into the absorption spectrum, which
    /// would then propagate into the k-distribution as a spurious feature.
    /// </summary>
    [TestMethod]
    public void IsContinuousAcrossEveryBreakpoint()
    {
        const double h = 1e-7;

        foreach (double breakpoint in new[] { Chi.FirstBreak, Chi.SecondBreak, Chi.ThirdBreak })
        {
            Assert.AreEqual(Chi.At(breakpoint - h), Chi.At(breakpoint + h), 1e-6,
                $"chi should not step at the {breakpoint:F0} cm^-1 breakpoint");
        }
    }

    /// <summary>
    /// Pins the published coefficients, because the previous set was wrong and nothing caught it.
    /// </summary>
    /// <remarks>
    /// Chaverot et al. (2025), A&amp;A 702, A137, Tables 2 and 3, CO2-N2, nu_2 band. The B values
    /// are temperature dependent - B_i(T) = alpha + beta exp(-gamma T) - so they are checked at
    /// the 296 K this model works at, and separately for varying with temperature at all, which
    /// the coefficients they replaced did not.
    /// </remarks>
    [TestMethod]
    public void CarriesThePublishedCarbonDioxideNitrogenCoefficients()
    {
        var chi = ChiFactor.CarbonDioxideNu2InNitrogen(296.0);

        Assert.AreEqual(3.0, chi.FirstBreak, 0.0);
        Assert.AreEqual(50.0, chi.SecondBreak, 0.0);
        Assert.AreEqual(180.0, chi.ThirdBreak, 0.0);

        Assert.AreEqual(0.065 + 0.038 * Math.Exp(-0.003 * 296.0), chi.FirstDecay, 1e-12);
        Assert.AreEqual(0.018 + 0.055 * Math.Exp(-0.020 * 296.0), chi.SecondDecay, 1e-12);
        Assert.AreEqual(0.0085, chi.ThirdDecay, 0.0);

        // Colder air broadens less far, so the wings are cut back harder.
        Assert.IsTrue(ChiFactor.CarbonDioxideNu2InNitrogen(220.0).FirstDecay > chi.FirstDecay,
            "B1 should fall with temperature");
    }

    /// <summary>
    /// The same band with a different collision partner is a different correction. Asserting it
    /// keeps the pure-CO2 set from being quietly treated as interchangeable with the air one.
    /// </summary>
    [TestMethod]
    public void DistinguishesTheCollisionPartner()
    {
        var air = ChiFactor.CarbonDioxideNu2InNitrogen();
        var pure = ChiFactor.CarbonDioxideNu2InCarbonDioxide();

        Assert.AreNotEqual(air.SecondBreak, pure.SecondBreak);
        Assert.AreNotEqual(air.ThirdBreak, pure.ThirdBreak);
        Assert.AreEqual(air, ChiFactor.CarbonDioxideNu2,
            "an Earth-like column is CO2 in air, so that is the set it should default to");
    }

    [TestMethod]
    public void FallsMonotonicallyAndStaysBelowOne()
    {
        double previous = 1.0;
        for (double s = 0.0; s <= 500.0; s += 0.5)
        {
            double chi = Chi.At(s);

            Assert.IsTrue(chi <= 1.0 + 1e-12, $"chi({s:F1}) = {chi:F6} exceeds one");
            Assert.IsTrue(chi > 0.0, $"chi({s:F1}) reached zero");
            Assert.IsTrue(chi <= previous + 1e-12, $"chi rose between {s - 0.5:F1} and {s:F1}");

            previous = chi;
        }
    }

    /// <summary>
    /// The correction has to bite where the forcing was coming from, or it would not have
    /// changed anything. At the shipped 400 cm^-1 cutoff chi is about 3e-4.
    ///
    /// It does not follow that the cutoff is converged. The published far wing decays with an
    /// e-folding length of 118 cm^-1 against the 62 of the coefficients it replaced, so it
    /// reaches further: opening the cutoff from 400 to 800 cm^-1 still moves the coefficient by
    /// about 4 %, and only past 800 does it settle.
    /// </summary>
    [TestMethod]
    public void SuppressesTheFarWingsByOrdersOfMagnitude()
    {
        Assert.IsTrue(Chi.At(100.0) < 0.02, $"chi(100) = {Chi.At(100.0):E2}");
        Assert.IsTrue(Chi.At(400.0) < 1e-3, $"chi(400) = {Chi.At(400.0):E2}");
    }

    [TestMethod]
    public void NoneLeavesTheProfileAlone()
    {
        foreach (double s in new[] { 0.0, 1.0, 50.0, 500.0, 5_000.0 })
        {
            Assert.AreEqual(1.0, ChiFactor.None.At(s), 0.0,
                $"the null correction should not touch a detuning of {s:F0} cm^-1");
        }
    }

    /// <summary>
    /// The correction must actually reach the line shape - a chi factor that were computed and
    /// then dropped would leave every test above passing and the spectrum unchanged.
    /// </summary>
    [TestMethod]
    public void ReachesTheLineProfileAndOnlyTouchesTheWings()
    {
        var lines = new[] { new SpectralLine(700.0, 1.0, 0.07) };

        var lorentz = LineByLineBand.FromLines(lines, 500.0, 900.0, 4_000, wingCutoff: 400.0);
        var corrected = LineByLineBand.FromLines(lines, 500.0, 900.0, 4_000, wingCutoff: 400.0,
            chi: Chi);

        var a = lorentz.AbsorptionCoefficients();
        var b = corrected.AbsorptionCoefficients();

        // Both are normalised to a band mean of one, so compare shape: the corrected spectrum
        // must put relatively more of its absorption near the centre and less in the wings.
        int centre = a.Length / 2;
        Assert.IsTrue(b[centre] > a[centre],
            $"after normalising, the corrected core should carry more of the band " +
            $"({b[centre]:E3} against {a[centre]:E3})");

        Assert.IsTrue(b[10] < a[10],
            $"the far wing should be suppressed ({b[10]:E3} against {a[10]:E3})");
    }

    /// <summary>
    /// The headline result. With pure Lorentz wings the model's CO2 forcing coefficient came out
    /// near 6.95 W m^-2 per ln, about 1.30x the accepted 5.35; correcting the far wings brings it
    /// to roughly 0.9x. This asserts the direction and the rough size rather than a precise value,
    /// because the exact figure depends on the wing cutoff and band resolution as well.
    /// </summary>
    [TestMethod]
    public void CorrectingTheWingsBringsTheForcingDownTowardsTheAcceptedLaw()
    {
        var withChi = Co2Sweep.SpectralConfiguration(subLorentzianWings: true);
        var without = Co2Sweep.SpectralConfiguration(subLorentzianWings: false);

        if (withChi is null || without is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        int last = Co2Sweep.Concentrations.Length - 1;
        double span = Math.Log(Co2Sweep.Concentrations[last] / Co2Sweep.Concentrations[0]);

        double a = Co2Sweep.ForcingCurve(withChi)[last] / span;
        double b = Co2Sweep.ForcingCurve(without)[last] / span;

        Assert.IsTrue(b > a,
            $"the correction should reduce the forcing, not raise it ({b:F3} to {a:F3})");

        Assert.IsTrue(a < b * 0.85,
            $"the correction should be a large effect, not a tweak ({b:F3} to {a:F3} W/m2 per ln)");

        Assert.AreEqual(1.0, a / Co2Sweep.AcceptedForcingCoefficient, 0.20,
            $"corrected, the coefficient should land near the accepted 5.35 " +
            $"(got {a:F3}, was {b:F3})");
    }
}
