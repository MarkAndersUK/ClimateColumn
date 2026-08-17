namespace ClimateColumn.Core;

/// <summary>
/// One segment (layer) of the vertical column. All extensive quantities are
/// per unit horizontal area, so a "volume" element dV reduces to a thickness dz.
/// </summary>
public sealed class Segment
{
    public int Index { get; init; }

    /// <summary>Altitude of the lower interface, m.</summary>
    public double BottomAltitude { get; init; }

    /// <summary>Altitude of the upper interface, m.</summary>
    public double TopAltitude { get; init; }

    /// <summary>Geometric thickness dz, m.</summary>
    public double Thickness => TopAltitude - BottomAltitude;

    /// <summary>Mid-layer altitude, m.</summary>
    public double MidAltitude => 0.5 * (BottomAltitude + TopAltitude);

    /// <summary>Pressure at the lower interface, Pa.</summary>
    public double BottomPressure { get; init; }

    /// <summary>Pressure at the upper interface, Pa.</summary>
    public double TopPressure { get; init; }

    /// <summary>Mid-layer pressure, Pa.</summary>
    public double MidPressure => 0.5 * (BottomPressure + TopPressure);

    /// <summary>Air mass per unit horizontal area, kg m^-2 (= dp / g).</summary>
    public double MassPerArea { get; init; }

    /// <summary>Layer-mean air density, kg m^-3.</summary>
    public double Density => MassPerArea / Thickness;

    /// <summary>Heat capacity per unit horizontal area, J m^-2 K^-1.</summary>
    public double HeatCapacity => PhysicalConstants.DryAirSpecificHeat * MassPerArea;

    /// <summary>
    /// Volumetric emission coefficient eps' in the Koenigsberger equation
    /// dq = 4 eps' sigma T^4 dV. Units m^-1.
    /// </summary>
    public double EmissionCoefficient { get; set; }

    /// <summary>Segment temperature, K.</summary>
    public double Temperature { get; set; }

    /// <summary>Shortwave (solar) flux absorbed by this segment, W m^-2.</summary>
    public double ShortwaveAbsorbed { get; set; }

    /// <summary>
    /// Hemispheric optical thickness of the segment, D * eps' * dz, dimensionless.
    /// </summary>
    public double OpticalThickness(double diffusivity) =>
        diffusivity * EmissionCoefficient * Thickness;

    /// <summary>Blackbody emissive power sigma T^4 (Stefan-Boltzmann), W m^-2.</summary>
    public double BlackbodyEmissivePower =>
        PhysicalConstants.StefanBoltzmann * Math.Pow(Temperature, 4);

    /// <summary>
    /// Total emission from the segment given directly by the Koenigsberger equation,
    /// integrated over the segment volume: dq = 4 eps' sigma T^4 dz, W m^-2.
    /// Valid in the optically thin limit; the solver uses the exponential form that
    /// reduces to this as dz -> 0.
    /// </summary>
    public double KoenigsbergerEmission =>
        4.0 * EmissionCoefficient * PhysicalConstants.StefanBoltzmann *
        Math.Pow(Temperature, 4) * Thickness;
}
