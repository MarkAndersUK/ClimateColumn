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

    /// <summary>
    /// A single representative hemispheric optical thickness per segment: the bands' thicknesses
    /// weighted by the share of that segment's emission each carries. With one absorbing band and
    /// no window it is simply that band's thickness.
    /// </summary>
    public required double[] OpticalThickness { get; init; }

    /// <summary>Hemispheric optical thickness of each segment, per band: [band][segment].</summary>
    public required double[][] BandOpticalThickness { get; init; }

    /// <summary>Names of the bands, in the same order as <see cref="BandOpticalThickness"/>.</summary>
    public required string[] BandLabels { get; init; }

    /// <summary>Total hemispheric optical depth of band <paramref name="band"/>.</summary>
    public double TotalBandOpticalDepth(int band) => BandOpticalThickness[band].Sum();

    /// <summary>Emission actually used by the solver, up + down, per segment, W m^-2.</summary>
    public required double[] SegmentEmission { get; init; }

    /// <summary>Emission from the raw Koenigsberger form 4 eps' sigma T^4 dz, W m^-2.</summary>
    public required double[] KoenigsbergerEmission { get; init; }

    /// <summary>Longwave absorbed by each segment, W m^-2.</summary>
    public required double[] SegmentAbsorption { get; init; }

    /// <summary>Outgoing longwave radiation at the top of the column, W m^-2.</summary>
    public double OutgoingLongwave => UpwardFlux[^1];

    /// <summary>
    /// The all-sky result: <paramref name="clear"/> and <paramref name="cloudy"/> mixed by
    /// cloud fraction. This is the independent column approximation.
    /// </summary>
    /// <remarks>
    /// Exact for what it is asked to do, and approximate in what it assumes. Fluxes are
    /// additive, so a sky that is <c>f</c> cloudy and <c>1-f</c> clear really does emit the
    /// weighted mean of the two - there is no linearisation here. What is approximate is the
    /// premise: that the cloudy and clear parts of the sky can be treated as two independent
    /// columns side by side, with no radiation passing between them. That fails for broken
    /// cloud, where a gap lets a neighbouring cloud's side radiate out, and it is the standard
    /// approximation in models far larger than this one.
    ///
    /// Both solves see the same temperatures, because there is one atmosphere and one surface
    /// underneath both skies. Running two separate columns to their own equilibria would be a
    /// different and less defensible model - the air over a cloudy patch is not thermally
    /// isolated from the air 10 km away.
    /// </remarks>
    public static RadiationResult Blend(RadiationResult clear, RadiationResult cloudy, double fraction)
    {
        double f = Math.Clamp(fraction, 0.0, 1.0);
        if (f <= 0.0) return clear;
        if (f >= 1.0) return cloudy;

        static double[] Mix(double[] a, double[] b, double f)
        {
            var mixed = new double[a.Length];
            for (int i = 0; i < a.Length; i++) mixed[i] = (1.0 - f) * a[i] + f * b[i];
            return mixed;
        }

        var bandTau = new double[clear.BandOpticalThickness.Length][];
        for (int b = 0; b < bandTau.Length; b++)
        {
            bandTau[b] = Mix(clear.BandOpticalThickness[b], cloudy.BandOpticalThickness[b], f);
        }

        return new RadiationResult
        {
            UpwardFlux = Mix(clear.UpwardFlux, cloudy.UpwardFlux, f),
            DownwardFlux = Mix(clear.DownwardFlux, cloudy.DownwardFlux, f),
            NetUpwardFlux = Mix(clear.NetUpwardFlux, cloudy.NetUpwardFlux, f),
            RadiativeHeating = Mix(clear.RadiativeHeating, cloudy.RadiativeHeating, f),
            OpticalThickness = Mix(clear.OpticalThickness, cloudy.OpticalThickness, f),
            BandOpticalThickness = bandTau,
            BandLabels = cloudy.BandLabels,
            SegmentEmission = Mix(clear.SegmentEmission, cloudy.SegmentEmission, f),
            KoenigsbergerEmission = cloudy.KoenigsbergerEmission,
            SegmentAbsorption = Mix(clear.SegmentAbsorption, cloudy.SegmentAbsorption, f)
        };
    }

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
    /// One spectral band as the solver sees it: where its per-segment extinction comes from,
    /// what share of an emitter's Planck function it carries, and how absorption is distributed
    /// across it.
    /// </summary>
    /// <remarks>
    /// The coefficient and share are read through delegates rather than copied, so that the
    /// single-absorber arrangement keeps working exactly as before - tests that set
    /// <see cref="Segment.EmissionCoefficient"/> directly after building a column still drive
    /// the solver, because the delegate reads it live.
    /// </remarks>
    private readonly record struct BandPlan(
        Func<Segment, double> Coefficient,
        Func<double, double> Share,
        KDistribution Structure,
        string Label);

    /// <summary>
    /// Expresses whatever the options describe - explicit bands, or the single absorber with an
    /// optional window - as one list of band plans, so the solver has a single code path.
    /// </summary>
    private static BandPlan[] PlanBands(ModelOptions options)
    {
        if (!options.HasBands)
        {
            // The absorbing band is the complement of the window, which is not itself an
            // interval, so it takes whatever the window leaves.
            return new[]
            {
                new BandPlan(
                    s => s.EmissionCoefficient,
                    t => 1.0 - options.WindowShare(t),
                    options.BuildKDistribution(),
                    "absorbing"),
                new BandPlan(
                    s => s.WindowEmissionCoefficient,
                    options.WindowShare,
                    KDistribution.Grey,
                    "window")
            };
        }

        var bands = options.Bands;

        // If no band claims the remainder, one is added that is transparent. Without it the
        // interval bands' weights sum to less than one and the surface silently radiates less
        // than its own sigma T^4 - energy vanishing into the part of the spectrum nobody
        // described. Closing the spectrum here makes the weights sum to one by construction,
        // whatever intervals the caller chose.
        bool covered = false;
        foreach (var band in bands)
        {
            if (band.IsRemainder) { covered = true; break; }
        }

        var plans = new BandPlan[bands.Count + (covered ? 0 : 1)];

        if (!covered)
        {
            plans[^1] = new BandPlan(
                _ => 0.0,
                temperature =>
                {
                    double claimed = 0.0;
                    foreach (var band in bands) claimed += band.PlanckShare(temperature);
                    return Math.Clamp(1.0 - claimed, 0.0, 1.0);
                },
                KDistribution.Grey,
                "uncovered");
        }

        for (int b = 0; b < bands.Count; b++)
        {
            var band = bands[b];
            int index = b;

            Func<double, double> share = band.IsRemainder
                ? temperature =>
                {
                    // Whatever the interval bands leave. Clamped because the Planck series is
                    // accurate but not exact, and a hair below zero here would emit negatively.
                    double claimed = 0.0;
                    foreach (var other in bands)
                    {
                        if (!other.IsRemainder) claimed += other.PlanckShare(temperature);
                    }
                    return Math.Clamp(1.0 - claimed, 0.0, 1.0);
                }
                : band.PlanckShare;

            plans[b] = new BandPlan(
                s => s.BandEmissionCoefficients[index],
                share,
                band.Structure ?? KDistribution.Grey,
                band.Label);
        }

        return plans;
    }

    /// <param name="includeCloud">
    /// Whether the cloud deck's opacity is in the path. False gives the clear-sky solve that the
    /// independent column approximation blends with this one, and that the cloud radiative
    /// effect is measured against.
    /// </param>
    public static RadiationResult Solve(Column column, bool includeCloud = true)
    {
        int n = column.Count;
        var options = column.Options;
        double d = options.Diffusivity;
        var segments = column.Segments;

        // The emitted power per unit *surface* area, which is what every flux in this model is.
        // In plane-parallel geometry the shell factor is 1 and this is just sigma T^4.
        //
        // Sphericity enters here and nowhere else. Writing the spherical two-stream equations
        //     dF+/dr = -D eps' (F+ - sigma T^4) - (2/r) F+
        // in terms of G = (r/r_0)^2 F - the power crossing radius r, divided by the surface
        // area beneath it - removes the geometric term entirely:
        //     dG+/dr = -D eps' (G+ - (r/r_0)^2 sigma T^4)
        // which is the plane-parallel equation with the source scaled by (r/r_0)^2. So the
        // exponential recurrence below stays exact, the boundary conditions are untouched
        // (G = F at the surface, and nothing enters the top in either variable), and every
        // energy budget still closes in W per m^2 of planet surface.
        var blackbody = new double[n];
        for (int i = 0; i < n; i++)
        {
            blackbody[i] = segments[i].BlackbodyEmissivePower * segments[i].ShellVolumeFactor;
        }

        // Two bands: the absorbing band, and the window. Running both through the same
        // recurrence is what lets the window absorb and emit once it has a continuum, and it
        // reduces to the transparent case exactly when the continuum is zero - a band with
        // tau = 0 has unit transmittance, so it neither absorbs nor emits and the surface's
        // window emission passes straight through.
        var bands = PlanBands(options);

        // Per-band share of each emitter's Planck function, and the optical thickness the
        // radiation sees. Both are evaluated per segment because the share follows temperature.
        var share = new double[bands.Length][];
        var tau = new double[bands.Length][];
        var surfaceShare = new double[bands.Length];

        for (int b = 0; b < bands.Length; b++)
        {
            share[b] = new double[n];
            tau[b] = new double[n];

            for (int i = 0; i < n; i++)
            {
                share[b][i] = bands[b].Share(segments[i].Temperature);
                tau[b][i] = d * bands[b].Coefficient(segments[i]) * segments[i].Thickness;
            }

            surfaceShare[b] = bands[b].Share(column.SurfaceTemperature);
        }

        // Cloud opacity, kept out of the per-band arrays above on purpose. It is grey - liquid
        // droplets absorb across a band rather than in lines - so it must not be scaled by a
        // g-point's absorption multiplier, which describes the gas. Adding it into bandTau
        // would have made the cloud thin where the gas is transparent and thick where the gas
        // is opaque, which is the opposite of what a cloud does: it is most effective precisely
        // in the window, where the gas lets the surface radiate straight to space.
        var cloudTau = new double[n];
        if (includeCloud && options.HasCloud)
        {
            for (int i = 0; i < n; i++)
            {
                cloudTau[i] = d * segments[i].CloudExtinction * segments[i].Thickness;
            }
        }

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
            var structure = bands[b].Structure;
            var bandShare = share[b];
            var bandTau = tau[b];

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
                    transmittance[i] = Math.Exp(-(multiplier * bandTau[i] + cloudTau[i]));
                    absorptivity[i] = 1.0 - transmittance[i];
                }

                var bandDown = new double[n + 1];
                var bandUp = new double[n + 1];

                // No longwave enters the top of the column, in any sub-band.
                bandDown[n] = 0.0;
                for (int i = n - 1; i >= 0; i--)
                {
                    bandDown[i] = bandDown[i + 1] * transmittance[i] +
                                  absorptivity[i] * weight * bandShare[i] * blackbody[i];
                }

                // Surface: this sub-band's share of the Stefan-Boltzmann emission, plus
                // specular reflection of the back radiation arriving in it.
                bandUp[0] = epsS * weight * surfaceShare[b] * surfaceBlackbody +
                            (1.0 - epsS) * bandDown[0];

                for (int i = 0; i < n; i++)
                {
                    bandUp[i + 1] = bandUp[i] * transmittance[i] +
                                    absorptivity[i] * weight * bandShare[i] * blackbody[i];
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
                    emission[i] += 2.0 * absorptivity[i] * weight * bandShare[i] * blackbody[i];
                }
            }
        }

        var net = new double[n + 1];
        for (int i = 0; i <= n; i++) net[i] = up[i] - down[i];

        var koenigsberger = new double[n];
        var representativeTau = new double[n];
        for (int i = 0; i < n; i++)
        {
            // Flux convergence: what enters the segment from below and above minus what leaves.
            heating[i] = net[i] - net[i + 1];
            koenigsberger[i] = segments[i].KoenigsbergerEmission;

            // A single representative optical thickness for reporting and for the time-step
            // limiter: the bands' thicknesses weighted by the share of the segment's own
            // emission each carries. With one absorbing band and no window that is exactly the
            // band's own thickness, so the single-absorber case is unchanged.
            double weighted = 0.0;
            for (int b = 0; b < bands.Length; b++) weighted += share[b][i] * tau[b][i];

            // The cloud is in every band, so it adds rather than being weighted in. This number
            // drives the explicit time-step limiter as well as reporting: a cloud layer relaxes
            // faster than the clear air around it, and a limiter that did not know about the
            // cloud would take steps too long for it.
            representativeTau[i] = weighted + cloudTau[i];
        }

        return new RadiationResult
        {
            UpwardFlux = up,
            DownwardFlux = down,
            NetUpwardFlux = net,
            RadiativeHeating = heating,
            OpticalThickness = representativeTau,
            SegmentEmission = emission,
            KoenigsbergerEmission = koenigsberger,
            SegmentAbsorption = absorbed,
            BandOpticalThickness = tau,
            BandLabels = bands.Select(b => b.Label).ToArray()
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
