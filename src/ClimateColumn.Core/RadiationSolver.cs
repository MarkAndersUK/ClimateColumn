namespace ClimateColumn.Core;

/// <summary>Longwave flux profile produced by <see cref="RadiationSolver"/>.</summary>
public sealed class RadiationResult
{
    /// <summary>Upward longwave flux at each interface (0 = surface, N = top), W m^-2.</summary>
    public required double[] UpwardFlux { get; init; }

    /// <summary>Downward longwave flux at each interface, W m^-2.</summary>
    public required double[] DownwardFlux { get; init; }

    /// <summary>Net upward longwave flux at each interface, W m^-2.</summary>
    public required double[] NetUpwardFlux { get; init; }

    /// <summary>Longwave flux convergence into each segment, W m^-2 (positive = warming).</summary>
    public required double[] RadiativeHeating { get; init; }

    /// <summary>Hemispheric optical thickness of each segment.</summary>
    public required double[] OpticalThickness { get; init; }

    /// <summary>Emission actually used by the solver, up + down, per segment, W m^-2.</summary>
    public required double[] SegmentEmission { get; init; }

    /// <summary>Emission from the raw Koenigsberger form 4 eps' sigma T^4 dz, W m^-2.</summary>
    public required double[] KoenigsbergerEmission { get; init; }

    /// <summary>Longwave absorbed by each segment, W m^-2.</summary>
    public required double[] SegmentAbsorption { get; init; }

    /// <summary>Outgoing longwave radiation at the top of the column, W m^-2.</summary>
    public double OutgoingLongwave => UpwardFlux[^1];

    /// <summary>Downward longwave at the surface ("back radiation"), W m^-2.</summary>
    public double SurfaceDownwardFlux => DownwardFlux[0];

    /// <summary>Upward longwave leaving the surface, W m^-2.</summary>
    public double SurfaceUpwardFlux => UpwardFlux[0];
}

/// <summary>
/// Grey two-stream longwave solver.
///
/// Emission is the Koenigsberger equation, dq = 4 eps' sigma T^4 dV. For a plane
/// parallel slab of thickness dz this is 4 eps' sigma T^4 dz per unit horizontal area,
/// shared equally between the upward and downward hemispheres. Kirchhoff's law makes the
/// hemispheric absorptivity equal the hemispheric emissivity, which fixes the flux-space
/// extinction coefficient at D * eps' with D = 2 (see PhysicalConstants).
///
/// Integrating the resulting Schwarzschild equation
///     dF+/dz = -D eps' (F+ - sigma T^4),   dF-/dz = +D eps' (F- - sigma T^4)
/// across a segment of constant temperature gives the exponential recurrence used below,
/// which is stable at arbitrary optical thickness and reduces exactly to the differential
/// Koenigsberger form as dz -> 0.
///
/// The surface is a Stefan-Boltzmann emitter with emissivity eps_s, and reflects the
/// remaining (1 - eps_s) of the incident longwave.
///
/// An optional spectral window (Options.WindowFraction) splits the spectrum in two: a
/// fraction f where the absorber is fully transparent, and the remaining (1 - f) which
/// stays grey. Every Planck source term in the recurrence is scaled by (1 - f), and the
/// window share of the surface emission, f eps_s sigma Ts^4, escapes to space unattenuated
/// and altitude-independent - the atmosphere neither absorbs nor emits there, so the window
/// contributes nothing to any flux divergence.
/// </summary>
public static class RadiationSolver
{
    public static RadiationResult Solve(Column column)
    {
        int n = column.Count;
        double d = column.Options.Diffusivity;
        double band = 1.0 - column.Options.WindowFraction;
        var segments = column.Segments;

        var tau = new double[n];
        var transmittance = new double[n];
        var absorptivity = new double[n];
        var blackbody = new double[n];

        for (int i = 0; i < n; i++)
        {
            tau[i] = segments[i].OpticalThickness(d);
            transmittance[i] = Math.Exp(-tau[i]);
            absorptivity[i] = 1.0 - transmittance[i];
            blackbody[i] = segments[i].BlackbodyEmissivePower;
        }

        var down = new double[n + 1];
        var up = new double[n + 1];

        // Downward stream: no longwave enters the top of the column. Only the grey band
        // carries a downward flux - the atmosphere cannot emit into the window.
        down[n] = 0.0;
        for (int i = n - 1; i >= 0; i--)
        {
            down[i] = down[i + 1] * transmittance[i] + absorptivity[i] * band * blackbody[i];
        }

        // Surface: Stefan-Boltzmann emission plus specular reflection of the back radiation.
        // The up recurrence carries the grey band only; the window share of the surface
        // emission passes through untouched and is added onto the reported fluxes below.
        double epsS = column.Options.SurfaceEmissivity;
        double surfaceBlackbody = PhysicalConstants.StefanBoltzmann *
                                  Math.Pow(column.SurfaceTemperature, 4);
        double windowFlux = epsS * (1.0 - band) * surfaceBlackbody;
        up[0] = epsS * band * surfaceBlackbody + (1.0 - epsS) * down[0];

        for (int i = 0; i < n; i++)
        {
            up[i + 1] = up[i] * transmittance[i] + absorptivity[i] * band * blackbody[i];
        }

        var heating = new double[n];
        var emission = new double[n];
        var koenigsberger = new double[n];
        var absorbed = new double[n];

        for (int i = 0; i < n; i++)
        {
            // Absorption sees only the in-band incident fluxes, so it is computed before
            // the window flux is folded into the upward stream.
            absorbed[i] = absorptivity[i] * (up[i] + down[i + 1]);

            // Emission is shared equally between the two hemispheres.
            emission[i] = 2.0 * absorptivity[i] * band * blackbody[i];
            koenigsberger[i] = segments[i].KoenigsbergerEmission;
        }

        for (int i = 0; i <= n; i++) up[i] += windowFlux;

        var net = new double[n + 1];
        for (int i = 0; i <= n; i++) net[i] = up[i] - down[i];

        for (int i = 0; i < n; i++)
        {
            // Flux convergence: what enters the segment from below and above minus what
            // leaves. The window flux is the same at every interface and cancels here.
            heating[i] = net[i] - net[i + 1];
        }

        return new RadiationResult
        {
            UpwardFlux = up,
            DownwardFlux = down,
            NetUpwardFlux = net,
            RadiativeHeating = heating,
            OpticalThickness = tau,
            SegmentEmission = emission,
            KoenigsbergerEmission = koenigsberger,
            SegmentAbsorption = absorbed
        };
    }

    /// <summary>
    /// Stefan-Boltzmann emissive power, W m^-2, for emissivity <paramref name="emissivity"/>
    /// at temperature <paramref name="temperature"/> (K).
    /// </summary>
    public static double StefanBoltzmannFlux(double temperature, double emissivity = 1.0) =>
        emissivity * PhysicalConstants.StefanBoltzmann * Math.Pow(temperature, 4);

    /// <summary>
    /// The Koenigsberger equation in its raw differential form: total emission per unit
    /// volume, W m^-3, for volumetric emission coefficient <paramref name="emissionCoefficient"/>
    /// (m^-1) at temperature <paramref name="temperature"/> (K).
    /// </summary>
    public static double KoenigsbergerVolumetricEmission(double temperature, double emissionCoefficient) =>
        4.0 * emissionCoefficient * PhysicalConstants.StefanBoltzmann * Math.Pow(temperature, 4);
}
