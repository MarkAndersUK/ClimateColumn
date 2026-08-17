using System.Collections.Concurrent;
using ClimateColumn.Core;

namespace ClimateColumn.Tests;

/// <summary>
/// Shared helpers for the test suite: memoised equilibrium runs, and the numerical
/// reference functions the physics assertions are checked against.
/// </summary>
internal static class TestSupport
{
    private static readonly ConcurrentDictionary<string, ModelResult> Cache = new();

    /// <summary>
    /// Runs a configuration to equilibrium, memoised under <paramref name="key"/>. Marching
    /// a column to equilibrium is by far the most expensive thing the suite does and several
    /// tests need the same handful of configurations, so they are computed once and shared.
    /// The result is treated as read-only by every caller; anything that mutates a column
    /// must build its own.
    /// </summary>
    public static ModelResult Equilibrium(string key, Func<ModelOptions> options) =>
        Cache.GetOrAdd(key, _ => ColumnModel.RunToEquilibrium(options()));

    /// <summary>The default configuration at equilibrium - the model's headline result.</summary>
    public static ModelResult Default => Equilibrium("default", () => new ModelOptions());

    /// <summary>
    /// Third-order exponential integral E3, by the midpoint rule on E3 = int_1^inf
    /// e^{-tau t}/t^3 dt. Substituting t = 1/u maps the integral to int_0^1 u e^{-tau/u} du,
    /// which is smooth and bounded on the unit interval. This exists to check the D = 2
    /// closure against the true angular integral, so it must not share any code with the
    /// model.
    /// </summary>
    public static double ExponentialIntegral3(double tau)
    {
        const int n = 200_000;
        double sum = 0.0;
        for (int i = 1; i <= n; i++)
        {
            double u = (i - 0.5) / n;
            sum += u * Math.Exp(-tau / u);
        }
        return sum / n;
    }

    /// <summary>Mean lapse rate from the surface up to <paramref name="depth"/>, K m^-1.</summary>
    public static double LapseRate(Column column, double depth)
    {
        Segment? top = null;
        foreach (var s in column.Segments)
        {
            if (s.MidAltitude <= depth) top = s;
        }
        if (top is null || top.MidAltitude < 1.0) return double.NaN;
        return (column.SurfaceTemperature - top.Temperature) / top.MidAltitude;
    }

    /// <summary>Temperature of the segment whose mid-altitude is nearest <paramref name="altitude"/>.</summary>
    public static double TemperatureAt(Column column, double altitude)
    {
        Segment? best = null;
        foreach (var s in column.Segments)
        {
            if (best is null ||
                Math.Abs(s.MidAltitude - altitude) < Math.Abs(best.MidAltitude - altitude))
            {
                best = s;
            }
        }
        return best!.Temperature;
    }

    /// <summary>Column hemispheric optical depth accumulated below <paramref name="altitude"/>.</summary>
    public static double OpticalDepthBelow(Column column, double altitude)
    {
        double sum = 0.0;
        foreach (var s in column.Segments)
        {
            if (s.MidAltitude < altitude)
                sum += s.OpticalThickness(PhysicalConstants.KoenigsbergerDiffusivity);
        }
        return sum;
    }

    /// <summary>
    /// Instantaneous longwave forcing, W m^-2: the drop in outgoing longwave when the
    /// absorber is changed from <paramref name="baseline"/> to <paramref name="perturbed"/>
    /// at fixed temperature. Both columns are initialised from the same standard atmosphere,
    /// so the temperatures agree and the difference is the pure absorber effect.
    /// </summary>
    public static double InstantaneousForcing(ModelOptions baseline, ModelOptions perturbed) =>
        RadiationSolver.Solve(Column.Build(baseline)).OutgoingLongwave -
        RadiationSolver.Solve(Column.Build(perturbed)).OutgoingLongwave;

    public static int CountOccurrences(string haystack, string needle)
    {
        int count = 0, index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index++;
        }
        return count;
    }
}
