namespace ClimateColumn.Core;

/// <summary>One entry in a grid refinement study.</summary>
public readonly record struct GridLevel(int SegmentCount, double SurfaceTemperature, double OutgoingLongwave);

/// <summary>Result of a grid refinement study.</summary>
public sealed class GridConvergenceResult
{
    public required IReadOnlyList<GridLevel> Levels { get; init; }

    /// <summary>Successive differences T(2N) - T(N), K.</summary>
    public required IReadOnlyList<double> Differences { get; init; }

    /// <summary>
    /// Observed order of convergence from the last three levels,
    /// p = log2(dT_coarse / dT_fine). Close to 1 for this scheme.
    /// </summary>
    public required double ObservedOrder { get; init; }

    /// <summary>Richardson extrapolation of the surface temperature to dz -> 0, K.</summary>
    public required double ExtrapolatedSurfaceTemperature { get; init; }

    /// <summary>How far the finest grid still sits from the extrapolated limit, K.</summary>
    public double FinestGridError =>
        Math.Abs(Levels[^1].SurfaceTemperature - ExtrapolatedSurfaceTemperature);
}

/// <summary>
/// Refines the vertical grid by successive factors of two and reports how the equilibrium
/// surface temperature converges. Holding temperature constant across a segment - the form
/// in which the Koenigsberger equation is applied here - makes the scheme first order in dz.
/// </summary>
public static class GridConvergence
{
    public static GridConvergenceResult Study(ModelOptions baseOptions, int levels = 4)
    {
        if (levels < 3) throw new ArgumentException("At least three levels are needed to infer an order.");

        var results = new List<GridLevel>();
        int n = baseOptions.SegmentCount;

        for (int k = 0; k < levels; k++, n *= 2)
        {
            var o = baseOptions.Clone();
            o.SegmentCount = n;
            var r = ColumnModel.RunToEquilibrium(o);
            results.Add(new GridLevel(n, r.SurfaceTemperature, r.Radiation.OutgoingLongwave));
        }

        var diffs = new List<double>();
        for (int i = 1; i < results.Count; i++)
            diffs.Add(results[i].SurfaceTemperature - results[i - 1].SurfaceTemperature);

        double coarse = diffs[^2];
        double fine = diffs[^1];
        double order = Math.Abs(fine) > 1e-12 ? Math.Log2(Math.Abs(coarse / fine)) : double.NaN;

        // Richardson: T_exact ~ T_fine + (T_fine - T_coarse) / (2^p - 1).
        double p = double.IsNaN(order) ? 1.0 : order;
        double extrapolated = results[^1].SurfaceTemperature + fine / (Math.Pow(2, p) - 1.0);

        return new GridConvergenceResult
        {
            Levels = results,
            Differences = diffs,
            ObservedOrder = order,
            ExtrapolatedSurfaceTemperature = extrapolated
        };
    }
}
