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

    /// <summary>Hemispheric optical thickness of each segment in the absorbing band.</summary>
    public required double[] OpticalThickness { get; init; }

    /// <summary>
    /// Hemispheric optical thickness of each segment inside the spectral window. All zeros
    /// unless a water-vapour continuum has been configured.
    /// </summary>
    public required double[] WindowOpticalThickness { get; init; }

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
/// An optional spectral window splits the spectrum in two: a wavelength interval where the
/// absorber is fully transparent, and everything else, which stays grey. The window share of
/// the surface emission escapes to space unattenuated and altitude-independent - the
/// atmosphere neither absorbs nor emits there, so the window contributes nothing to any flux
/// divergence.
///
/// The share is evaluated from each emitter's own temperature rather than being a single
/// number for the whole column, because the fraction of a Planck function inside a fixed
/// wavelength interval depends strongly on how hot the emitter is. Every source term
/// therefore carries its own weight (1 - f(T)), and the surface splits its emission by
/// f(T_s). Each emitter still divides exactly its own sigma T^4 between band and window, so
/// energy closure is unaffected.
///
/// The window is not necessarily transparent: given a water-vapour continuum it absorbs and
/// emits like any other band. Both bands therefore run through the same recurrence and their
/// fluxes are summed, which reduces exactly to the transparent case when the continuum is
/// zero - a band with tau = 0 has unit transmittance, so it neither absorbs nor emits and the
/// surface's window emission passes straight through to space.
///
/// Each band may in turn be split into g-points by a correlated-k quadrature
/// (see KDistribution), representing the spread of absorption coefficients between line cores
/// and wings. Every g-point is another pass of the same recurrence with the optical depth
/// scaled, carrying its own share of the spectral interval; a grey band is simply the
/// one-point case, so the arrangement collapses back to a single pass exactly.
/// </summary>
public static class RadiationSolver
{
    /// <summary>
    /// One spectral band: the share of each emitter's Planck function it carries, the optical
    /// thickness the radiation sees inside it, and how that absorption is distributed across
    /// the band.
    /// </summary>
    private readonly record struct Band(double[] Share, double[] Tau, KDistribution Structure);

    public static RadiationResult Solve(Column column)
    {
        int n = column.Count;
        var options = column.Options;
        double d = options.Diffusivity;
        var segments = column.Segments;

        var blackbody = new double[n];
        for (int i = 0; i < n; i++) blackbody[i] = segments[i].BlackbodyEmissivePower;

        // Two bands: the absorbing band, and the window. Running both through the same
        // recurrence is what lets the window absorb and emit once it has a continuum, and it
        // reduces to the transparent case exactly when the continuum is zero - a band with
        // tau = 0 has unit transmittance, so it neither absorbs nor emits and the surface's
        // window emission passes straight through.
        // The k-distribution applies to the absorbing band only. The window's continuum stays
        // grey deliberately: smoothness between the lines is what makes it a continuum, so
        // giving it line structure would misrepresent it.
        var absorbing = new Band(new double[n], new double[n], options.BuildKDistribution());
        var window = new Band(new double[n], new double[n], KDistribution.Grey);

        for (int i = 0; i < n; i++)
        {
            double share = options.WindowShare(segments[i].Temperature);
            window.Share[i] = share;
            absorbing.Share[i] = 1.0 - share;

            absorbing.Tau[i] = segments[i].OpticalThickness(d);
            window.Tau[i] = segments[i].WindowOpticalThickness(d);
        }

        double surfaceWindowShare = options.WindowShare(column.SurfaceTemperature);
        var surfaceShare = new[] { 1.0 - surfaceWindowShare, surfaceWindowShare };
        var bands = new[] { absorbing, window };

        var up = new double[n + 1];
        var down = new double[n + 1];
        var heating = new double[n];
        var emission = new double[n];
        var absorbed = new double[n];

        double epsS = options.SurfaceEmissivity;
        double surfaceBlackbody = PhysicalConstants.StefanBoltzmann *
                                  Math.Pow(column.SurfaceTemperature, 4);

        for (int b = 0; b < bands.Length; b++)
        {
            var (share, tau, structure) = bands[b];

            // Each g-point is a pseudo-monochromatic sub-band: the same recurrence, with the
            // optical depth scaled by that sub-band's absorption coefficient, and its result
            // carrying the sub-band's share of the spectral interval. Summing them is the
            // k-distribution integral over cumulative probability.
            for (int j = 0; j < structure.Points; j++)
            {
                double weight = structure.Weights[j];
                double multiplier = structure.Multipliers[j];

                var transmittance = new double[n];
                var absorptivity = new double[n];
                for (int i = 0; i < n; i++)
                {
                    transmittance[i] = Math.Exp(-multiplier * tau[i]);
                    absorptivity[i] = 1.0 - transmittance[i];
                }

                var bandDown = new double[n + 1];
                var bandUp = new double[n + 1];

                // No longwave enters the top of the column, in any sub-band.
                bandDown[n] = 0.0;
                for (int i = n - 1; i >= 0; i--)
                {
                    bandDown[i] = bandDown[i + 1] * transmittance[i] +
                                  absorptivity[i] * weight * share[i] * blackbody[i];
                }

                // Surface: this sub-band's share of the Stefan-Boltzmann emission, plus
                // specular reflection of the back radiation arriving in it.
                bandUp[0] = epsS * weight * surfaceShare[b] * surfaceBlackbody +
                            (1.0 - epsS) * bandDown[0];

                for (int i = 0; i < n; i++)
                {
                    bandUp[i + 1] = bandUp[i] * transmittance[i] +
                                    absorptivity[i] * weight * share[i] * blackbody[i];
                }

                for (int i = 0; i <= n; i++)
                {
                    up[i] += bandUp[i];
                    down[i] += bandDown[i];
                }

                for (int i = 0; i < n; i++)
                {
                    absorbed[i] += absorptivity[i] * (bandUp[i] + bandDown[i + 1]);

                    // Emission is shared equally between the two hemispheres.
                    emission[i] += 2.0 * absorptivity[i] * weight * share[i] * blackbody[i];
                }
            }
        }

        var net = new double[n + 1];
        for (int i = 0; i <= n; i++) net[i] = up[i] - down[i];

        var koenigsberger = new double[n];
        var totalTau = new double[n];
        for (int i = 0; i < n; i++)
        {
            // Flux convergence: what enters the segment from below and above minus what leaves.
            heating[i] = net[i] - net[i + 1];
            koenigsberger[i] = segments[i].KoenigsbergerEmission;

            // Reported per-segment thickness is the absorbing band's, which is what the
            // Koenigsberger correspondence is about; read the window's back from the column.
            totalTau[i] = absorbing.Tau[i];
        }

        return new RadiationResult
        {
            UpwardFlux = up,
            DownwardFlux = down,
            NetUpwardFlux = net,
            RadiativeHeating = heating,
            OpticalThickness = totalTau,
            SegmentEmission = emission,
            KoenigsbergerEmission = koenigsberger,
            SegmentAbsorption = absorbed,
            WindowOpticalThickness = window.Tau
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
