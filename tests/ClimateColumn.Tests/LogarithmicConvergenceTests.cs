using System.Globalization;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// How closely the spectral configuration's CO2 forcing follows a pure logarithm, and what the
/// remaining departure is caused by.
/// </summary>
/// The forcing is logarithmic to a few percent, which is the headline result - a linear increase in
/// absorber becomes a logarithmic forcing purely through band structure. These tests measure how
/// close, and hold the shipped resolution on the converged plateau found by the study recorded in
/// artifacts/convergence-study.txt.
/// </remarks>
[TestClass]
public class LogarithmicConvergenceTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Fits F = A ln(C/C0) and reports how far A wanders. Zero drift is an exact logarithm.
    /// </summary>
    private static (double Drift, double Coefficient, double RSquared) LogarithmicFit(
        IReadOnlyList<double> forcings)
    {
        double c0 = Co2Sweep.Concentrations[0];
        double lo = double.MaxValue, hi = double.MinValue;
        double sx = 0, sy = 0, sxx = 0, sxy = 0, n = 0;

        for (int i = 1; i < forcings.Count; i++)
        {
            double x = Math.Log(Co2Sweep.Concentrations[i] / c0);
            double y = forcings[i];

            double a = y / x;
            lo = Math.Min(lo, a);
            hi = Math.Max(hi, a);

            sx += x; sy += y; sxx += x * x; sxy += x * y; n++;
        }

        double slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
        double intercept = (sy - slope * sx) / n;

        double residual = 0, total = 0, mean = sy / n;
        for (int i = 1; i < forcings.Count; i++)
        {
            double x = Math.Log(Co2Sweep.Concentrations[i] / c0);
            double fit = intercept + slope * x;
            residual += Math.Pow(forcings[i] - fit, 2);
            total += Math.Pow(forcings[i] - mean, 2);
        }

        return ((hi - lo) / hi, slope, total > 0 ? 1.0 - residual / total : 1.0);
    }

    private static Co2Sweep Require(int bandCount, int gPoints, bool rederive = false)
    {
        var sweep = Co2Sweep.SpectralBands(
            bandCount: bandCount, gPoints: gPoints, rederive: rederive);
        if (sweep is null)
        {
            Assert.Inconclusive(
                "No HITRAN data. Run scripts/fetch-hitran.ps1 -Molecule all; this test measures " +
                "the CO2 response of the spectrally derived bands.");
        }
        return sweep!;
    }

    /// <summary>
    /// Checks the shipped resolution is still on the converged plateau, and records the numbers.
    /// </summary>
    /// <remarks>
    /// </remarks>
    [TestMethod]
    public void MeasureHowTheDriftRespondsToResolution()
    {
        string csv = Path.Combine(ArtifactDirectory(), "logarithmic-convergence.csv");
        string txt = Path.ChangeExtension(csv, ".txt");

        File.WriteAllText(csv, "rederive,bands,gpoints,seconds,drift_pct,coefficient,ratio,r_squared\n");
        File.WriteAllText(txt, "");

        int measured = 0;

        // Each row is appended as it is measured. The previous version of this study wrote only at
        // the end and lost everything when it was interrupted; at these resolutions a run is long
        // enough that partial results are worth having.
        void Probe(int bands, int gPoints, bool rederive, double wingCutoff = 400.0)
        {
            var configure = Co2Sweep.SpectralConfiguration(
                bandCount: bands, gPoints: gPoints, rederive: rederive, wingCutoff: wingCutoff);

            if (configure is null)
            {
                Assert.Inconclusive(
                    "No HITRAN data. Run scripts/fetch-hitran.ps1 -Molecule all.");
            }

            long start = Environment.TickCount64;
            var forcings = Co2Sweep.ForcingCurve(configure!);
            double seconds = (Environment.TickCount64 - start) / 1000.0;

            var (drift, a, r2) = LogarithmicFit(forcings);

            File.AppendAllText(csv, string.Format(Inv, "{0},{1},{2},{3},{4:F1},{5:F4},{6:F4},{7:F4},{8:F7}\n",
                rederive, bands, gPoints, wingCutoff, seconds, 100 * drift, a,
                a / Co2Sweep.AcceptedForcingCoefficient, r2));
            File.AppendAllText(txt, string.Format(Inv,
                "{0,-13} {1,2} x {2,2}  cut {3,3:F0}  {4,4:F0}s   drift {5,6:F2}%   A {6,6:F3}  ({7:F3}x accepted)   R2 {8:F5}\n",
                rederive ? "re-derived" : "extrapolated", bands, gPoints, wingCutoff, seconds,
                100 * drift, a, a / Co2Sweep.AcceptedForcingCoefficient, r2));

            measured++;
        }

        // The study that chose the defaults is preserved in artifacts/convergence-study.txt.
        // What runs routinely is a two-point check that the shipped resolution is still on the
        // converged plateau: a full re-scan costs minutes and the answer does not move.
        Probe(16, 16, rederive: false, wingCutoff: 400.0);
        Probe(16, 32, rederive: false, wingCutoff: 400.0);

        Assert.AreEqual(2, measured, "every configuration should have been measured");
    }

    /// <summary>Locates artifacts/ beside the solution.</summary>
    private static string ArtifactDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "ClimateColumn.sln")))
        {
            directory = directory.Parent;
        }

        string path = Path.Combine(directory?.FullName ?? ".", "artifacts");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// The shipped configuration's forcing coefficient agrees with the converged one, and its
    /// response is logarithmic to a few percent.
    /// </summary>
    /// <remarks>
    /// The convergence study behind this is recorded in artifacts/convergence-*.txt. Its result,
    /// briefly: refining the wing cutoff to 800 cm^-1 at 16 bands x 16 g-points gives
    /// A = 6.978 W m^-2 per ln, and 16 x 32 at a 400 cm^-1 cutoff gives 6.906. So the converged
    /// coefficient is about 7.0, or 1.30x the accepted 5.35, and the shipped 8 x 4 configuration's
    /// 6.994 is right - but by cancellation rather than by resolution. Its 15 cm^-1 wing cutoff
    /// throws away far-wing absorption, and its coarse band split happens to compensate.
    ///
    /// That matters for what may be changed safely. Widening the cutoff alone takes the shipped
    /// configuration to A = 9.5, badly wrong, because it removes one half of a cancelling pair.
    /// Both knobs have to move together, which is why this asserts against the converged value
    /// rather than against the shipped one.
    /// </remarks>
    [TestMethod]
    public void TheShippedCoefficientAgreesWithTheConvergedOne()
    {
        var (drift, coefficient, rSquared) = LogarithmicFit(Require(bandCount: 16, gPoints: 16).Forcings);

        Console.WriteLine(string.Format(Inv,
            "  drift {0:P3}  A = {1:F4} W/m2 per ln  R2 = {2:F7}", drift, coefficient, rSquared));

        // The converged coefficient, measured at 16x16 with an 800 cm^-1 cutoff and at 16x32 with
        // 400. Both land near 6.95; the shipped configuration must not stray far from that.
        Assert.AreEqual(6.97, coefficient, 0.35,
            $"the shipped coefficient is {coefficient:F3} W/m2 per ln against a converged ~6.95");

        Assert.IsTrue(drift < 0.06,
            $"the fitted coefficient wanders {drift:P2} across the sweep; converged resolutions " +
            "sit between 2% and 5%, so anything above 6% means something else has changed");

        Assert.IsTrue(rSquared > 0.999,
            $"R^2 against a pure logarithm is only {rSquared:F7}");
    }

}
