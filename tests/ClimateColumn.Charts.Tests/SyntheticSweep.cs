using ClimateColumn.Core;

namespace ClimateColumn.Charts.Tests;

/// <summary>
/// Builds a <see cref="Co2Sweep"/> from made-up numbers instead of running the model.
/// </summary>
/// <remarks>
/// The real sweep marches a column to equilibrium at every concentration and takes several
/// seconds; the physics is already covered by ClimateColumn.Tests. What these tests are about
/// is the export - whether a file is written, at the right size, honouring the theme - and
/// that has no interest in whether the temperatures are physical. Synthetic data keeps them
/// instant and independent of the solver.
/// </remarks>
internal static class SyntheticSweep
{
    public static Co2Sweep Build(string label = "Synthetic", double baseTemperature = 287.0,
        double slope = 0.004, double forcingSlope = 0.006)
    {
        var points = new List<Co2Point>();
        var forcings = new List<double>();

        foreach (double ppm in Co2Sweep.Concentrations)
        {
            double above = ppm - Co2Sweep.Concentrations[0];
            points.Add(new Co2Point(
                Ppm: ppm,
                DryOpticalDepth: 1.8 + above * 0.0004,
                SurfaceTemperature: baseTemperature + above * slope,
                Converged: true));

            // Non-zero at the calibration point so Sensitivity is finite, and varying with
            // forcingSlope so that two synthetic configurations differ in forcing as well as in
            // temperature. They previously did not: Build ignored its arguments here, so Pair()
            // produced two identical forcing curves, which the chart now correctly refuses to
            // draw twice - and that turned this fixture into a degenerate case.
            forcings.Add(0.5 + above * forcingSlope);
        }

        return new Co2Sweep
        {
            Label = label,
            Command = "--synthetic",
            Points = points,
            Forcings = forcings
        };
    }

    /// <summary>The two-configuration pair the chart is normally drawn from.</summary>
    public static Co2Sweep[] Pair() => new[]
    {
        Build("No vapour feedback", 286.8, 0.0032, 0.0055),
        Build("With water vapour feedback", 287.0, 0.0046, 0.0065)
    };
}
