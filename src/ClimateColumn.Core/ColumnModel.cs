namespace ClimateColumn.Core;

public sealed class ModelResult
{
    public required Column Column { get; init; }
    public required RadiationResult Radiation { get; init; }
    public required int Steps { get; init; }
    public required double SimulatedSeconds { get; init; }
    public required bool Converged { get; init; }

    /// <summary>Net downward flux at the top of the column, W m^-2 (0 at equilibrium).</summary>
    public required double TopOfAtmosphereImbalance { get; init; }

    /// <summary>Net downward flux into the surface, W m^-2 (0 at equilibrium).</summary>
    public required double SurfaceImbalance { get; init; }

    /// <summary>Sensible heat flux from surface to air, W m^-2.</summary>
    public required double SensibleHeatFlux { get; init; }

    /// <summary>
    /// Latent heat flux from surface to air, W m^-2 - energy carried by evaporation. Zero
    /// unless SurfaceMoistureAvailability is set, which it is not by default.
    /// </summary>
    public double LatentHeatFlux { get; init; }

    /// <summary>
    /// Bowen ratio, sensible over latent. Roughly 0.2 over tropical ocean and above 1 over
    /// dry land; NaN when there is no evaporation to divide by.
    /// </summary>
    public double BowenRatio =>
        LatentHeatFlux == 0.0 ? double.NaN : SensibleHeatFlux / LatentHeatFlux;

    /// <summary>
    /// Sol-air temperature diagnostic at the surface, K. At equilibrium this collapses onto
    /// the surface temperature by construction, so it is a check rather than a prediction.
    /// It carries no information when convection is disabled.
    /// </summary>
    public required double SolAirTemperature { get; init; }

    /// <summary>Air temperature extrapolated to z = 0, K.</summary>
    public required double NearSurfaceAirTemperature { get; init; }

    /// <summary>
    /// The longwave solution with the cloud removed and everything else held, or the same
    /// object as <see cref="Radiation"/> when there is no cloud.
    /// </summary>
    public RadiationResult? ClearSkyRadiation { get; init; }

    /// <summary>
    /// Longwave cloud radiative effect, W m^-2: how much less longwave the planet exports
    /// because the cloud is there. Positive - a cloud warms in the longwave, because it
    /// radiates to space from its cold top instead of letting the warm surface do it.
    /// </summary>
    public double LongwaveCloudRadiativeEffect =>
        ClearSkyRadiation is null ? 0.0
            : ClearSkyRadiation.OutgoingLongwave - Radiation.OutgoingLongwave;

    /// <summary>
    /// Shortwave cloud radiative effect, W m^-2: how much less sunlight the planet absorbs
    /// because the cloud is there. Negative - a cloud cools in the shortwave, by reflecting.
    /// </summary>
    public double ShortwaveCloudRadiativeEffect =>
        Column.Options.HasCloud
            ? Column.Options.AbsorbedSolarFlux - Column.Options.ClearSkyAbsorbedSolarFlux
            : 0.0;

    /// <summary>
    /// Net cloud radiative effect, W m^-2 - the two competing terms added. Negative on Earth:
    /// clouds reflect more than they trap, by around 20 W m^-2.
    /// </summary>
    /// <remarks>
    /// Both terms are measured the way CERES measures them: the difference between the all-sky
    /// planet and the same planet with the clouds taken out and nothing else touched. That is a
    /// diagnostic of one equilibrium, not a climate response - it does not say what would happen
    /// if the clouds actually went away, because the atmosphere would then adjust to their
    /// absence.
    /// </remarks>
    public double NetCloudRadiativeEffect =>
        LongwaveCloudRadiativeEffect + ShortwaveCloudRadiativeEffect;

    public double SurfaceTemperature => Column.SurfaceTemperature;
    public double EmissionTemperature => Column.Options.EmissionTemperature;

    /// <summary>Surface warming relative to the planet's emission temperature, K.</summary>
    public double GreenhouseWarming => SurfaceTemperature - EmissionTemperature;

    /// <summary>Longwave emitted by the surface itself, eps_s sigma Ts^4, W m^-2.</summary>
    public double SurfaceEmission =>
        RadiationSolver.StefanBoltzmannFlux(SurfaceTemperature, Column.Options.SurfaceEmissivity);

    /// <summary>
    /// Greenhouse effect measured as a flux: surface emission minus outgoing longwave, W m^-2.
    /// Uses the surface's own emission, not the upward flux at the surface, which also
    /// carries the reflected share (1 - eps_s) of the back radiation.
    /// </summary>
    public double GreenhouseFlux => SurfaceEmission - Radiation.OutgoingLongwave;

    /// <summary>
    /// Altitude of the top of the convecting layer, m, taken as the highest segment
    /// interface still sitting on the critical lapse rate, interpolated into the interval above
    /// rather than snapped to the grid. Zero when nothing is convecting.
    /// </summary>
    public double ConvectiveTopAltitude
    {
        get
        {
            if (Column.Options.Convection != ConvectionMode.Full) return 0.0;

            double gamma = Column.Options.CriticalLapseRate;
            double tolerance = 1e-6 * Math.Max(gamma, 1e-12);

            // Walk up while the profile still sits on the critical lapse rate. The mixed layer
            // reaches the top edge of the last such segment, not its centre.
            int last = -1;
            double lapseAbove = 0.0;
            for (int i = 0; i < Column.Count - 1; i++)
            {
                var lower = Column.Segments[i];
                var upper = Column.Segments[i + 1];
                double lapse = (lower.Temperature - upper.Temperature)
                    / (upper.MidAltitude - lower.MidAltitude);

                if (Math.Abs(lapse - gamma) > tolerance) { lapseAbove = lapse; break; }
                last = i + 1;
            }

            if (last < 0) return 0.0;

            var top = Column.Segments[last];
            double edge = top.MidAltitude + 0.5 * top.Thickness;

            // The interval above is usually part-way convecting rather than abruptly stable, so
            // the crossing is placed within it by how much of the critical lapse survives. This
            // is the same treatment ColumnProfile.EmissionAltitude gives its own crossing, and
            // without it the figure can only ever land on a grid point.
            if (last + 1 < Column.Count && lapseAbove > 0.0 && lapseAbove < gamma)
            {
                double next = Column.Segments[last + 1].MidAltitude + 0.5 * Column.Segments[last + 1].Thickness;
                edge += (lapseAbove / gamma) * (next - edge);
            }

            return edge;
        }
    }

    /// <summary>
    /// Longwave heating rate of each segment, K per day. Negative almost everywhere:
    /// this is the radiative cooling that solar absorption and convection must offset.
    /// </summary>
    public double[] LongwaveHeatingRatesPerDay()
    {
        var rates = new double[Column.Count];
        for (int i = 0; i < Column.Count; i++)
        {
            var s = Column.Segments[i];
            rates[i] = Radiation.RadiativeHeating[i] / s.HeatCapacity * PhysicalConstants.SecondsPerDay;
        }
        return rates;
    }

    /// <summary>
    /// Net non-convective heating rate of each segment, K per day: longwave convergence
    /// plus absorbed solar, plus the surface sensible flux for the lowest segment. This is
    /// zero wherever the segment is in radiative equilibrium, and is exactly the tendency
    /// the convective adjustment has to remove inside the convecting layer.
    /// </summary>
    public double[] NetHeatingRatesPerDay()
    {
        var rates = new double[Column.Count];
        for (int i = 0; i < Column.Count; i++)
        {
            var s = Column.Segments[i];
            double flux = Radiation.RadiativeHeating[i] + s.ShortwaveAbsorbed;
            if (i == 0) flux += SensibleHeatFlux + LatentHeatFlux;
            rates[i] = flux / s.HeatCapacity * PhysicalConstants.SecondsPerDay;
        }
        return rates;
    }
}

/// <summary>
/// Marches the column to radiative(-convective) equilibrium with an adaptive explicit
/// forward-Euler step on each segment's heat capacity.
/// </summary>
public sealed class ColumnModel
{
    public Column Column { get; }
    public ModelOptions Options => Column.Options;

    public ColumnModel(ModelOptions options) : this(Column.Build(options)) { }

    public ColumnModel(Column column) => Column = column;

    /// <summary>
    /// The longwave solution the column actually experiences, and the clear-sky one it is
    /// measured against.
    /// </summary>
    /// <remarks>
    /// With no cloud the two are the same object and there is exactly one solve, so the cost of
    /// this machinery to a caller who is not using clouds is a boolean test per step.
    /// </remarks>
    public static (RadiationResult AllSky, RadiationResult Clear) SolveSky(Column column)
    {
        if (!column.Options.HasCloud)
        {
            var only = RadiationSolver.Solve(column);
            return (only, only);
        }

        var cloudy = RadiationSolver.Solve(column, includeCloud: true);
        var clear = RadiationSolver.Solve(column, includeCloud: false);

        return (RadiationResult.Blend(clear, cloudy, column.Options.CloudFraction), clear);
    }

    /// <summary>Advance the column to equilibrium.</summary>
    public ModelResult Run(Action<int, double, double>? progress = null)
    {
        Options.ValidateForIntegration();

        int n = Column.Count;
        var tendency = new double[n];
        var previousTemperature = new double[n];
        double time = 0.0;
        int step = 0;
        bool converged = false;
        double toaImbalance = 0.0;
        double surfaceImbalance = 0.0;
        double sensible = 0.0;
        double latent = 0.0;

        // The water-vapour absorber follows the evolving temperature (Clausius-Clapeyron),
        // so its distribution must be refreshed before every radiation solve. The fixed dry
        // absorber needs no refresh.
        bool temperatureDependentAbsorber =
            Options.WaterVapourOpticalDepth > 0.0 && Options.WaterVapourFeedback;

        RadiationResult rad = SolveSky(Column).AllSky;

        for (step = 1; step <= Options.MaxSteps; step++)
        {
            if (temperatureDependentAbsorber) Column.DistributeOpticalDepth();
            rad = SolveSky(Column).AllSky;
            sensible = ConvectionSolver.SensibleHeatFlux(Column);
            latent = ConvectionSolver.LatentHeatFlux(Column);

            // Segment energy budget: longwave convergence + absorbed solar, with both surface
            // turbulent fluxes delivered to the lowest segment.
            //
            // Latent heat is released where the vapour condenses, which is spread through the
            // convecting layer rather than concentrated at its base. Depositing it at the base
            // is nonetheless equivalent here: the convective adjustment immediately mixes that
            // block to the critical lapse rate conserving enthalpy, so heat added anywhere
            // inside it produces the same adjusted profile. That equivalence is why this is a
            // placement detail and not an approximation - but it holds only under
            // ConvectionMode.Full, which is why the flux is zero without convection.
            for (int i = 0; i < n; i++)
            {
                tendency[i] = rad.RadiativeHeating[i] + Column.Segments[i].ShortwaveAbsorbed;
            }
            tendency[0] += sensible + latent;

            // Surface energy budget.
            double epsS = Options.SurfaceEmissivity;
            double surfaceEmission = RadiationSolver.StefanBoltzmannFlux(Column.SurfaceTemperature, epsS);
            surfaceImbalance = Column.SurfaceShortwaveAbsorbed
                             + epsS * rad.SurfaceDownwardFlux
                             - surfaceEmission
                             - sensible
                             - latent;

            toaImbalance = Column.TotalShortwaveAbsorbed - rad.OutgoingLongwave;

            // Adaptive time step. Two constraints: no level may move more than
            // MaxTemperatureStep in one step, and the step must stay inside the explicit
            // stability limit set by the local radiative relaxation time C / lambda,
            // where lambda = d(net loss)/dT.
            double dt = Options.MaxTimeStep;
            double limit = Options.MaxTemperatureStep;
            for (int i = 0; i < n; i++)
            {
                var s = Column.Segments[i];
                double mag = Math.Abs(tendency[i]);
                if (mag > 1e-30) dt = Math.Min(dt, limit * s.HeatCapacity / mag);

                // Both hemispheres radiate, hence the factor 2.
                double lambda = 2.0 * (1.0 - Math.Exp(-rad.OpticalThickness[i])) *
                                4.0 * PhysicalConstants.StefanBoltzmann * Math.Pow(s.Temperature, 3);
                if (i == 0 && Options.Convection != ConvectionMode.None)
                {
                    lambda += ConvectionSolver.SurfaceHeatTransferCoefficient(Options.WindSpeed);
                    lambda += ConvectionSolver.LatentHeatFluxAirSensitivity(Column);
                }
                if (lambda > 1e-30) dt = Math.Min(dt, 0.8 * s.HeatCapacity / lambda);
            }

            if (Math.Abs(surfaceImbalance) > 1e-30)
                dt = Math.Min(dt, limit * Options.SurfaceHeatCapacity / Math.Abs(surfaceImbalance));

            // The latent term belongs here as much as h_c does. Near 288 K with open water it
            // is the largest entry in this sum - larger than h_c and than the Planck term - so
            // omitting it would let the surface overshoot and oscillate instead of settling.
            double surfaceLambda = epsS * 4.0 * PhysicalConstants.StefanBoltzmann *
                                   Math.Pow(Column.SurfaceTemperature, 3) +
                                   (Options.Convection == ConvectionMode.None
                                       ? 0.0
                                       : ConvectionSolver.SurfaceHeatTransferCoefficient(Options.WindSpeed)) +
                                   ConvectionSolver.LatentHeatFluxSensitivity(Column);
            if (surfaceLambda > 1e-30)
                dt = Math.Min(dt, 0.8 * Options.SurfaceHeatCapacity / surfaceLambda);

            // Snapshot the state so that convergence is measured across the complete step,
            // adjustment included. Inside a convecting block the radiative tendency of an
            // individual segment does not vanish at equilibrium - it is balanced by the
            // convective adjustment - so the tendency alone is not a convergence measure.
            double previousSurface = Column.SurfaceTemperature;
            for (int i = 0; i < n; i++) previousTemperature[i] = Column.Segments[i].Temperature;

            for (int i = 0; i < n; i++)
            {
                Column.Segments[i].Temperature += tendency[i] * dt / Column.Segments[i].HeatCapacity;
            }
            Column.SurfaceTemperature += surfaceImbalance * dt / Options.SurfaceHeatCapacity;

            ConvectionSolver.Adjust(Column);

            double maxChange = Math.Abs(Column.SurfaceTemperature - previousSurface);
            for (int i = 0; i < n; i++)
            {
                maxChange = Math.Max(maxChange,
                    Math.Abs(Column.Segments[i].Temperature - previousTemperature[i]));
            }

            time += dt;
            progress?.Invoke(step, time, toaImbalance);

            // Equilibrium: the column exports exactly what it absorbs, the surface budget
            // closes, and the state has stopped moving.
            if (Math.Abs(toaImbalance) < Options.FluxTolerance &&
                Math.Abs(surfaceImbalance) < Options.FluxTolerance &&
                maxChange < Options.TemperatureTolerance)
            {
                converged = true;
                break;
            }
        }

        if (temperatureDependentAbsorber) Column.DistributeOpticalDepth();
        var (allSky, clearSky) = SolveSky(Column);
        rad = allSky;
        sensible = ConvectionSolver.SensibleHeatFlux(Column);
        latent = ConvectionSolver.LatentHeatFlux(Column);
        double eps = Options.SurfaceEmissivity;
        double emissionFlux = RadiationSolver.StefanBoltzmannFlux(Column.SurfaceTemperature, eps);
        surfaceImbalance = Column.SurfaceShortwaveAbsorbed + eps * rad.SurfaceDownwardFlux
                           - emissionFlux - sensible - latent;
        toaImbalance = Column.TotalShortwaveAbsorbed - rad.OutgoingLongwave;

        double netLongwaveLoss = emissionFlux - eps * rad.SurfaceDownwardFlux;
        double airTemperature = ConvectionSolver.NearSurfaceAirTemperature(Column);
        double solAir = ConvectionSolver.SolAirTemperature(
            airTemperature,
            Column.SurfaceShortwaveAbsorbed,
            netLongwaveLoss,
            Options.WindSpeed);

        return new ModelResult
        {
            Column = Column,
            Radiation = rad,
            Steps = Math.Min(step, Options.MaxSteps),
            SimulatedSeconds = time,
            Converged = converged,
            TopOfAtmosphereImbalance = toaImbalance,
            SurfaceImbalance = surfaceImbalance,
            SensibleHeatFlux = sensible,
            LatentHeatFlux = latent,
            SolAirTemperature = solAir,
            NearSurfaceAirTemperature = airTemperature,
            ClearSkyRadiation = Options.HasCloud ? clearSky : null
        };
    }

    /// <summary>Build and run a column in one call.</summary>
    public static ModelResult RunToEquilibrium(ModelOptions options) =>
        new ColumnModel(options).Run();
}
