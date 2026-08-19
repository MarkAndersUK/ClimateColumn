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

    /// <summary>
    /// Ratio of this shell's volume to the slab of the same thickness standing on the planet's
    /// surface: the volume mean of (r/r_0)^2 across the layer. Exactly 1 in plane-parallel
    /// geometry, and about 1.016 at 50 km on Earth.
    /// </summary>
    /// <remarks>
    /// Computed as the exact shell volume rather than (r_mid/r_0)^2, so that the segment holds
    /// exactly the mass a spherical shell holds and emits exactly the power one emits:
    /// <c>(r_t^3 - r_b^3) / (3 r_0^2 dz)</c>.
    ///
    /// <see cref="MassPerArea"/> is deliberately <em>not</em> scaled by this. That quantity is
    /// the radial column density, and optical depth is a path integral along a radial ray -
    /// a shell being wider does not make it more opaque from below. Keeping the two separate is
    /// what stops sphericity leaking into the absorption coefficients.
    /// </remarks>
    public double ShellVolumeFactor { get; init; } = 1.0;

    /// <summary>
    /// Heat capacity per unit <em>surface</em> area, J m^-2 K^-1 - the whole shell's heat
    /// capacity divided by the area of the planet beneath it, so that every flux in the model
    /// remains power per unit surface area.
    /// </summary>
    public double HeatCapacity =>
        PhysicalConstants.DryAirSpecificHeat * MassPerArea * ShellVolumeFactor;

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
    /// Volumetric extinction coefficient inside the spectral window, m^-1. Zero unless a
    /// water-vapour continuum has been configured; the window is otherwise transparent.
    /// </summary>
    public double WindowEmissionCoefficient { get; set; }

    /// <summary>
    /// Hemispheric optical thickness of the segment, D * eps' * dz, dimensionless.
    /// </summary>
    public double OpticalThickness(double diffusivity) =>
        diffusivity * EmissionCoefficient * Thickness;

    /// <summary>Hemispheric optical thickness inside the window, dimensionless.</summary>
    public double WindowOpticalThickness(double diffusivity) =>
        diffusivity * WindowEmissionCoefficient * Thickness;

    /// <summary>
    /// Volumetric longwave extinction from cloud droplets, m^-1. Zero outside the cloud deck,
    /// and everywhere when there is no cloud.
    /// </summary>
    /// <remarks>
    /// Kept apart from the gas coefficients because it behaves differently in two ways. It is
    /// grey - droplets absorb across a band rather than in lines, so this is not scaled by any
    /// k-distribution - and it applies only to the cloudy fraction of the sky, so it enters one
    /// of the two solves the independent column approximation makes and not the other.
    /// </remarks>
    public double CloudExtinction { get; set; }

    /// <summary>
    /// Volumetric extinction coefficient in each explicit spectral band, m^-1. Empty unless
    /// <see cref="ModelOptions.Bands"/> is in use.
    /// </summary>
    /// <remarks>
    /// Kept per band rather than as one number because the bands' absorbers have genuinely
    /// different vertical profiles: a well-mixed gas follows air density, water vapour falls off
    /// with its own scale height, and the continuum falls off faster still.
    /// </remarks>
    public double[] BandEmissionCoefficients { get; set; } = Array.Empty<double>();

    /// <summary>Hemispheric optical thickness in band <paramref name="band"/>.</summary>
    public double BandOpticalThickness(int band, double diffusivity) =>
        diffusivity * BandEmissionCoefficients[band] * Thickness;

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
        Math.Pow(Temperature, 4) * Thickness * ShellVolumeFactor;
}
