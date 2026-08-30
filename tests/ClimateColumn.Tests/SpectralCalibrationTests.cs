using System.Diagnostics;
using System.Globalization;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Calibrates the spectral configuration's absorber amount at a given resolution, and records
/// what a full sweep costs there.
/// </summary>
/// <remarks>
/// The absorber scale exists to put the base state at an Earth-like surface temperature, so it is
/// resolution dependent: the value that did that at 8 bands with a 15 cm^-1 wing cutoff leaves the
/// surface 2.5 K too cold once the bands are split finer and the wings are kept. Changing
/// resolution without re-calibrating therefore changes two things at once and makes the CO2 result
/// impossible to attribute.
/// </remarks>
[TestClass]
public class SpectralCalibrationTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The base state the absorber scale is calibrated to reproduce, K.</summary>
    private const double TargetBaseTemperature = 286.797;

    private static double BaseTemperature(int bands, int g, double cut, double scale)
    {
        var configure = Co2Sweep.SpectralConfiguration(
            bandCount: bands, gPoints: g, wingCutoff: cut, absorberScale: scale);

        if (configure is null)
        {
            Assert.Inconclusive("No HITRAN data. Run scripts/fetch-hitran.ps1 -Molecule all.");
        }

        return ColumnModel.RunToEquilibrium(configure!(Co2Sweep.Concentrations[0]))
            .SurfaceTemperature;
    }

    /// <summary>
    /// Bisects the absorber scale until the base state matches the configuration the rest of the
    /// project is documented against, then reports the converged CO2 response there.
    /// </summary>
    [TestMethod]
    public void CalibrateTheConvergedConfiguration()
    {
        const int bands = 16, gPoints = 16;
        const double cut = Co2Sweep.DefaultWingCutoff;

        var log = new List<string>();

        // More absorber is monotonically warmer, so plain bisection is enough. The bracket starts
        // above the old scale of 13: at this resolution 13 leaves the surface 2.4 K short, because
        // finer bands with their wings kept absorb differently from the coarse truncated ones the
        // old value was fitted against.
        double lo = 12.0, hi = 22.0, scale = 0;
        for (int i = 0; i < 9; i++)
        {
            scale = 0.5 * (lo + hi);
            double t = BaseTemperature(bands, gPoints, cut, scale);
            log.Add(string.Format(Inv, "  scale {0,7:F4}  ->  Ts {1,8:F3} K", scale, t));
            Console.WriteLine(log[^1]);

            if (Math.Abs(t - TargetBaseTemperature) < 0.02) break;
            if (t < TargetBaseTemperature) lo = scale; else hi = scale;
        }

        var sw = Stopwatch.StartNew();
        var sweep = Co2Sweep.SpectralBands(
            bandCount: bands, gPoints: gPoints, wingCutoff: cut, absorberScale: scale)!;
        sw.Stop();

        int last = Co2Sweep.Concentrations.Length - 1;
        double c0 = Co2Sweep.Concentrations[0];

        double loA = double.MaxValue, hiA = double.MinValue;
        for (int i = 1; i <= last; i++)
        {
            double a = sweep.Forcings[i] / Math.Log(Co2Sweep.Concentrations[i] / c0);
            loA = Math.Min(loA, a);
            hiA = Math.Max(hiA, a);
        }

        log.Add("");
        log.Add(string.Format(Inv,
            "converged configuration: {0} bands x {1} g-points, wing cutoff {2:F0} cm^-1, absorber scale {3:F4}",
            bands, gPoints, cut, scale));
        log.Add(string.Format(Inv, "  base state          : {0:F3} K", sweep.BaseTemperature));
        log.Add(string.Format(Inv, "  warming to {0:F0} ppm : +{1:F2} K", Co2Sweep.Concentrations[last], sweep.Warming(last)));
        log.Add(string.Format(Inv, "  F({0:F0})            : {1:F3} W/m2 against {2:F3} accepted (ratio {3:F3})",
            Co2Sweep.Concentrations[last], sweep.Forcings[last], sweep.AcceptedForcing(last),
            sweep.Forcings[last] / sweep.AcceptedForcing(last)));
        log.Add(string.Format(Inv, "  coefficient A       : {0:F3} to {1:F3} W/m2 per ln (drift {2:F2}%)",
            loA, hiA, 100 * (hiA - loA) / hiA));
        log.Add(string.Format(Inv, "  sensitivity         : {0:F3} K per W/m2", sweep.Sensitivity));
        log.Add(string.Format(Inv, "  full sweep cost     : {0:F0} s", sw.Elapsed.TotalSeconds));

        foreach (string line in log.Skip(log.Count - 8)) Console.WriteLine(line);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ClimateColumn.sln")))
        {
            dir = dir.Parent;
        }
        File.WriteAllLines(
            Path.Combine(dir!.FullName, "artifacts", "spectral-calibration.txt"), log);

        Assert.AreEqual(TargetBaseTemperature, sweep.BaseTemperature, 0.05,
            $"the bisection did not land on the target base state; scale {scale:F4} gives " +
            $"{sweep.BaseTemperature:F3} K");
    }
}
