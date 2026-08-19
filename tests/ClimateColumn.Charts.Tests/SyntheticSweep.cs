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
        var profiles = new List<ColumnProfile>();

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
            profiles.Add(Profile(label, ppm, baseTemperature + above * slope));
        }

        return new Co2Sweep
        {
            Label = label,
            Command = "--synthetic",
            Points = points,
            Forcings = forcings,
            Profiles = profiles
        };
    }

    /// <summary>
    /// A made-up vertical profile with the shape the real one has: a constant lapse rate up to
    /// a convective top, then isothermal above.
    /// </summary>
    /// <remarks>
    /// Shape rather than physics, deliberately. The profile figure has to place a convecting
    /// layer, find where the column crosses the emission temperature, and mark a ground that is
    /// warmer than the air on it - and all three of those are exercised by any profile with the
    /// right shape. What they are not exercised by is a run of the solver, which takes minutes.
    ///
    /// The isothermal cap is above the emission temperature crossing, so the crossing is always
    /// inside the lapse-rate section and moves with the surface temperature. That keeps the
    /// figure's annotation responding to the data rather than pinned to a fixed height.
    /// </remarks>
    private static ColumnProfile Profile(string label, double ppm, double surfaceTemperature)
    {
        const int count = 30;
        const double top = 50_000.0, lapse = 0.0065;

        double air = surfaceTemperature - 1.2;
        double convectiveTop = 4_000.0 + (surfaceTemperature - 287.0) * 400.0;

        var levels = new List<ProfileLevel>();
        for (int i = 0; i < count; i++)
        {
            double z = (i + 0.5) * top / count;
            double t = z <= 12_000.0
                ? air - lapse * z
                : air - lapse * 12_000.0;

            levels.Add(new ProfileLevel(z, 101_325.0 * Math.Exp(-z / 8_400.0), t));
        }

        return new ColumnProfile
        {
            Label = label,
            Ppm = ppm,
            Levels = levels,
            SurfaceTemperature = surfaceTemperature,
            NearSurfaceAirTemperature = air,
            ConvectiveTopAltitude = convectiveTop,
            CriticalLapseRate = lapse,
            EmissionTemperature = 254.6,
            ColumnTopAltitude = top,
            Converged = true
        };
    }

    /// <summary>The two-configuration pair the chart is normally drawn from.</summary>
    public static Co2Sweep[] Pair() => new[]
    {
        Build("No vapour feedback", 286.8, 0.0032, 0.0055),
        Build("With water vapour feedback", 287.0, 0.0046, 0.0065)
    };

    /// <summary>
    /// Two configurations that are identical, which is what the real pair is at the reference
    /// concentration. The figure must not paint one over the other.
    /// </summary>
    public static Co2Sweep[] IdenticalPair() => new[]
    {
        Build("First", 287.0, 0.004, 0.006),
        Build("Second", 287.0, 0.004, 0.006)
    };
}
