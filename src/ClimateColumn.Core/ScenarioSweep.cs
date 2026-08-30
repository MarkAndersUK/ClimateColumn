namespace ClimateColumn.Core;

/// <summary>One point of a coupled CO2 and methane scenario.</summary>
public readonly record struct ScenarioPoint(
    double Ppm, double Ppb, double WarmingBoth, double WarmingCo2Only);

/// <summary>
/// CO2 and methane raised together, with methane following a stated trend.
/// </summary>
/// <remarks>
/// <strong>This is the only calculation in the model that depends on an assumption about the
/// future, and the assumption does more work than the physics.</strong> Everything else here
/// answers "what happens if this much gas is present"; this one also answers "how much gas will
/// be present", which the model has no way to know. The coupling is therefore a constant anyone
/// can disagree with, not a hidden default.
///
/// The trend has two halves because one would be wrong. Below present day the observed path is
/// used - 285 ppm / 700 ppb to 421 / 1920, a historical ratio near 9 ppb per ppm. Above it,
/// today's rates give 3.3. Extrapolating today's ratio backwards would contradict what actually
/// happened.
///
/// Beyond present-day CO2 every point is an extrapolation of a current rate over centuries.
/// Real methane has a decade-scale atmospheric lifetime and a sink that responds to its own
/// concentration, and neither is modelled - the shape is meaningful, the year it might
/// correspond to is not.
/// </remarks>
public static class ScenarioSweep
{
    /// <summary>Pre-industrial CO2, ppm - where the sweep starts.</summary>
    public const double PreIndustrialCo2 = 285.0;

    /// <summary>Pre-industrial methane, ppb.</summary>
    public const double PreIndustrialMethane = 700.0;

    /// <summary>Present-day CO2, ppm, roughly 2023.</summary>
    public const double PresentCo2 = 421.0;

    /// <summary>Present-day methane, ppb, roughly 2023.</summary>
    public const double PresentMethane = 1920.0;

    /// <summary>Current CO2 growth, ppm per year.</summary>
    public const double Co2Trend = 2.4;

    /// <summary>Current methane growth, ppb per year.</summary>
    public const double MethaneTrend = 8.0;

    /// <summary>A one-line statement of the coupling, for a figure to carry.</summary>
    public static string CouplingNote =>
        $"Coupling: observed path to {PresentCo2:F0} ppm / {PresentMethane:F0} ppb, then the " +
        $"current trend (CO₂ {Co2Trend:F1} ppm/yr, CH₄ {MethaneTrend:F0} ppb/yr) " +
        "— an extrapolation, not a projection";

    /// <summary>The methane concentration this scenario pairs with a given CO2 level, ppb.</summary>
    public static double MethaneFor(double ppm) =>
        ppm <= PresentCo2
            ? PreIndustrialMethane + (PresentMethane - PreIndustrialMethane)
                * (ppm - PreIndustrialCo2) / (PresentCo2 - PreIndustrialCo2)
            : PresentMethane + MethaneTrend / Co2Trend * (ppm - PresentCo2);

    /// <summary>
    /// Runs the scenario, or returns nothing when the HITRAN line lists have not been fetched.
    /// </summary>
    /// <remarks>
    /// Two equilibria per concentration - both gases, and CO2 alone over the same range - so a
    /// figure can show methane's contribution as the difference rather than asserting it. That
    /// is twenty marches plus a band derivation each, so it is slow and is not run until asked
    /// for.
    /// </remarks>
    public static IReadOnlyList<ScenarioPoint> Run()
    {
        ModelOptions At(double ppm, double ppb) =>
            Co2Sweep.SpectralConfiguration(methaneRatio: ppb / PreIndustrialMethane)!(ppm);

        if (Co2Sweep.SpectralConfiguration() is null) return Array.Empty<ScenarioPoint>();

        double baseline = ColumnModel
            .RunToEquilibrium(At(PreIndustrialCo2, PreIndustrialMethane))
            .SurfaceTemperature;

        var concentrations = Co2Sweep.Concentrations;
        var points = new ScenarioPoint[concentrations.Length];

        Parallel.For(0, concentrations.Length, i =>
        {
            double ppm = concentrations[i];
            double ppb = MethaneFor(ppm);

            double both = ColumnModel.RunToEquilibrium(At(ppm, ppb)).SurfaceTemperature - baseline;
            double alone = ColumnModel.RunToEquilibrium(At(ppm, PreIndustrialMethane))
                .SurfaceTemperature - baseline;

            points[i] = new ScenarioPoint(ppm, ppb, both, alone);
        });

        return points;
    }
}
