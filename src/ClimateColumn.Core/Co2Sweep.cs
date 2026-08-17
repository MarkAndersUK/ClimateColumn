namespace ClimateColumn.Core;

/// <summary>One equilibrium point of a concentration sweep.</summary>
public sealed record Co2Point(double Ppm, double DryOpticalDepth, double SurfaceTemperature, bool Converged);

/// <summary>
/// A CO2 concentration sweep of one model configuration, together with the logarithmic
/// response it would have if its forcing followed the accepted law.
/// </summary>
/// <remarks>
/// Forcings here are measured against the <em>fixed</em> reference equilibrium, not against
/// the previous step. That is the only definition comparable to 5.35 ln(C/C0); the stepwise
/// forcings the CLI prints are each taken against the previous (warmer) equilibrium and must
/// not be summed and held against the logarithmic law.
/// </remarks>
public sealed class Co2Sweep
{
    /// <summary>Concentrations swept, ppm. The reference is first.</summary>
    public static readonly double[] Concentrations =
        { 285, 350, 425, 500, 600, 700, 800, 900, 1000 };

    /// <summary>The concentration both configurations are calibrated at, ppm.</summary>
    public const double CalibrationPpm = 425.0;

    /// <summary>
    /// Index of <see cref="CalibrationPpm"/> in the sweep. Looked up rather than hard-coded
    /// so the sweep range can change without silently pointing at the wrong point.
    /// </summary>
    public static readonly int CalibrationIndex = Array.IndexOf(Concentrations, CalibrationPpm);

    /// <summary>Accepted CO2 forcing coefficient, W m^-2 per ln(C/C0).</summary>
    public const double AcceptedForcingCoefficient = 5.35;

    public required string Label { get; init; }
    public required string Command { get; init; }
    public required IReadOnlyList<Co2Point> Points { get; init; }

    /// <summary>Instantaneous forcing at each concentration, against the reference state.</summary>
    public required IReadOnlyList<double> Forcings { get; init; }

    public double ReferencePpm => Concentrations[0];
    public double BaseTemperature => Points[0].SurfaceTemperature;

    /// <summary>
    /// The configuration's own climate sensitivity, K per W m^-2, measured at the
    /// calibration point. Taken from the model rather than assumed, so the expectation
    /// curve tracks the model if the configuration changes.
    /// </summary>
    public double Sensitivity =>
        (Points[CalibrationIndex].SurfaceTemperature - BaseTemperature) / Forcings[CalibrationIndex];

    /// <summary>Accepted forcing at index <paramref name="i"/>, W m^-2.</summary>
    public double AcceptedForcing(int i) =>
        AcceptedForcingCoefficient * Math.Log(Concentrations[i] / ReferencePpm);

    /// <summary>
    /// Surface temperature the configuration would reach if its forcing were the accepted
    /// logarithmic one, at its own sensitivity. This is the curve the model ought to follow.
    /// </summary>
    public double Expected(int i) => BaseTemperature + Sensitivity * AcceptedForcing(i);

    /// <summary>Warming from the reference to index <paramref name="i"/>, K.</summary>
    public double Warming(int i) => Points[i].SurfaceTemperature - BaseTemperature;

    /// <summary>Gap between the model and the logarithmic expectation at index i, K.</summary>
    public double Overshoot(int i) => Points[i].SurfaceTemperature - Expected(i);

    /// <summary>Runs a configuration across every concentration and measures its forcings.</summary>
    public static Co2Sweep Run(string label, string command, Func<ModelOptions> configure)
    {
        var reference = ColumnModel.RunToEquilibrium(WithConcentration(configure, Concentrations[0]));

        var points = new List<Co2Point>();
        var forcings = new List<double>();

        for (int i = 0; i < Concentrations.Length; i++)
        {
            var options = WithConcentration(configure, Concentrations[i]);
            var result = i == 0 ? reference : ColumnModel.RunToEquilibrium(options);

            points.Add(new Co2Point(
                Concentrations[i], options.EffectiveDryOpticalDepth,
                result.SurfaceTemperature, result.Converged));

            forcings.Add(ForcingFrom(reference, options));
        }

        return new Co2Sweep
        {
            Label = label, Command = command, Points = points, Forcings = forcings
        };
    }

    private static ModelOptions WithConcentration(Func<ModelOptions> configure, double ppm)
    {
        var options = configure();
        options.Co2Concentration = ppm;
        return options;
    }

    /// <summary>
    /// Instantaneous forcing, W m^-2: the drop in outgoing longwave when the absorber is
    /// changed to <paramref name="perturbed"/> while the temperatures stay at
    /// <paramref name="baseline"/>'s equilibrium. Redistributing after the temperatures are
    /// copied re-evaluates any water vapour at the baseline state, keeping the feedback out
    /// of the forcing.
    /// </summary>
    private static double ForcingFrom(ModelResult baseline, ModelOptions perturbed)
    {
        var held = Column.Build(perturbed);
        for (int i = 0; i < held.Count; i++)
            held.Segments[i].Temperature = baseline.Column.Segments[i].Temperature;
        held.SurfaceTemperature = baseline.SurfaceTemperature;
        held.DistributeOpticalDepth();

        return baseline.Radiation.OutgoingLongwave - RadiationSolver.Solve(held).OutgoingLongwave;
    }

    /// <summary>The configuration used for the README's no-feedback calibration.</summary>
    public static Co2Sweep NoFeedback() => Run(
        "No vapour feedback",
        "--co2-fraction 0.06",
        () => new ModelOptions { Co2AbsorberFraction = 0.06 });

    /// <summary>The configuration used for the README's water-vapour-feedback calibration.</summary>
    public static Co2Sweep WithWaterVapourFeedback() => Run(
        "With water vapour feedback",
        "--optical-depth 2.0 --wv-tau 1.8 --ozone-fraction 0.3 --pressure-broadening 1 --co2-fraction 0.11",
        () => new ModelOptions
        {
            TotalOpticalDepth = 2.0,
            WaterVapourOpticalDepth = 1.8,
            OzoneFraction = 0.3,
            PressureBroadeningExponent = 1.0,
            Co2AbsorberFraction = 0.11
        });
}
