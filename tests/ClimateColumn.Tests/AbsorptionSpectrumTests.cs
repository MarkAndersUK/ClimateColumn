using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Covers the infrared absorption spectra, and uses them as a check on the line data itself.
/// </summary>
/// <remarks>
/// The band centres are the useful assertion here. Where each gas absorbs is settled
/// spectroscopy that this project does not get to choose, so a band landing in the wrong place
/// would mean the line lists, the cross-section path or the binning is wrong - and none of the
/// existing tests would notice, because they all work with band-mean quantities that have had
/// the wavenumber axis integrated away.
/// </remarks>
[TestClass]
public class AbsorptionSpectrumTests
{
    private static IReadOnlyList<AbsorptionTrace>? Traces() =>
        AbsorptionSpectrum.Compute(bins: 120, samples: 24_000);

    /// <summary>The bin centre carrying a trace's largest absorptivity, cm^-1.</summary>
    private static double PeakWavenumber(AbsorptionTrace trace)
    {
        var nu = AbsorptionSpectrum.Wavenumbers(trace.Absorptivity.Count);
        int peak = 0;
        for (int b = 1; b < trace.Absorptivity.Count; b++)
        {
            if (trace.Absorptivity[b] > trace.Absorptivity[peak]) peak = b;
        }
        return nu[peak];
    }

    [TestMethod]
    public void EachGasAbsorbsWhereSpectroscopySaysItShould()
    {
        var traces = Traces();
        if (traces is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        // Wide windows, because the peak is a binned maximum and the point is that the band is
        // in the right place rather than that it peaks at one exact wavenumber.
        var expected = new Dictionary<string, (double Low, double High)>
        {
            ["Carbon dioxide"] = (600, 740),   // nu_2, 15 um
            ["Ozone"] = (980, 1120),           // 9.6 um, inside the window
            ["Methane"] = (1200, 1400),        // 7.7 um
            ["Nitrous oxide"] = (1200, 1400),  // 7.8 um
        };

        foreach (var trace in traces)
        {
            if (!expected.TryGetValue(trace.Gas, out var band)) continue;

            double peak = PeakWavenumber(trace);
            Assert.IsTrue(peak >= band.Low && peak <= band.High,
                $"{trace.Gas} peaks at {peak:F0} cm^-1, outside the expected " +
                $"{band.Low:F0}-{band.High:F0} band");
        }
    }

    [TestMethod]
    public void AbsorptivityStaysWithinItsPhysicalBounds()
    {
        var traces = Traces();
        if (traces is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        foreach (var trace in traces)
        {
            foreach (double a in trace.Absorptivity)
            {
                Assert.IsTrue(a is >= 0.0 and <= 1.0,
                    $"{trace.Gas} reports an absorptivity of {a:F4}, which is not a fraction");
            }
        }
    }

    /// <summary>
    /// The combined trace must absorb at least as much as any single gas everywhere.
    /// </summary>
    /// <remarks>
    /// Adding a gas cannot make a band more transparent, so this catches the combined trace
    /// being built from a mean rather than from summed optical depth - which would look
    /// plausible and be wrong.
    /// </remarks>
    [TestMethod]
    public void TheCombinedTraceAbsorbsAtLeastAsMuchAsAnyGas()
    {
        var traces = Traces();
        if (traces is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        var all = traces[^1];
        Assert.AreEqual("All gases", all.Gas, "the combined trace should come last");

        for (int i = 0; i < traces.Count - 1; i++)
        {
            for (int b = 0; b < all.Absorptivity.Count; b++)
            {
                Assert.IsTrue(all.Absorptivity[b] >= traces[i].Absorptivity[b] - 1e-9,
                    $"at bin {b} the combined trace absorbs {all.Absorptivity[b]:F4} against " +
                    $"{traces[i].Gas}'s {traces[i].Absorptivity[b]:F4}");
            }
        }
    }

    /// <summary>
    /// The window is the point of the figure, so it has to actually be a window.
    /// </summary>
    /// <remarks>
    /// This configuration carries no continuum, so the window here is more open than the real
    /// atmosphere's - which is exactly what the figure is for. Asserting that it is markedly
    /// more transparent than the surrounding band pins that.
    /// </remarks>
    [TestMethod]
    public void TheWindowIsMoreOpenThanTheBandsAroundIt()
    {
        var traces = Traces();
        if (traces is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        var all = traces[^1];
        double window = AbsorptionSpectrum.MeanBetween(
            all, AbsorptionSpectrum.WindowFrom, AbsorptionSpectrum.WindowTo);
        double below = AbsorptionSpectrum.MeanBetween(all, 200, 600);
        double above = AbsorptionSpectrum.MeanBetween(all, 1400, 1800);

        Assert.IsTrue(window < 0.6 * below,
            $"the window absorbs {window:P0} against {below:P0} below it, which is not a window");
        Assert.IsTrue(window < 0.6 * above,
            $"the window absorbs {window:P0} against {above:P0} above it");
    }

    /// <summary>
    /// A wider cutoff integrates more wing, so it cannot absorb less.
    /// </summary>
    [TestMethod]
    public void AWiderCutoffDoesNotAbsorbLess()
    {
        var narrow = AbsorptionSpectrum.Compute(bins: 60, samples: 12_000, wingCutoff: 100.0);
        if (narrow is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        var wide = AbsorptionSpectrum.Compute(bins: 60, samples: 12_000, wingCutoff: 800.0)!;

        double narrowMean = AbsorptionSpectrum.MeanBetween(narrow[^1],
            AbsorptionSpectrum.FromWavenumber, AbsorptionSpectrum.ToWavenumber);
        double wideMean = AbsorptionSpectrum.MeanBetween(wide[^1],
            AbsorptionSpectrum.FromWavenumber, AbsorptionSpectrum.ToWavenumber);

        Assert.IsTrue(wideMean >= narrowMean - 1e-9,
            $"opening the cutoff from 100 to 800 cm^-1 lowered mean absorptivity from " +
            $"{narrowMean:P2} to {wideMean:P2}");
    }
}
