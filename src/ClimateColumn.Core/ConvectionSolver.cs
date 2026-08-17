namespace ClimateColumn.Core;

/// <summary>
/// Non-radiative vertical heat transport: a bulk surface-to-air sensible heat flux and a
/// critical-lapse-rate convective adjustment for the atmospheric column.
/// </summary>
public static class ConvectionSolver
{
    /// <summary>
    /// Surface convective (film) heat transfer coefficient, W m^-2 K^-1, from the
    /// wind-speed relation h_c = 5.8 + 4.1 v used in Koenigsberger et al.,
    /// "Manual of Tropical Housing and Building". v in m s^-1.
    /// </summary>
    public static double SurfaceHeatTransferCoefficient(double windSpeed) =>
        5.8 + 4.1 * Math.Max(0.0, windSpeed);

    /// <summary>
    /// Air temperature at z = 0, obtained by extrapolating the two lowest segments down to
    /// the ground.
    /// </summary>
    /// <remarks>
    /// Using the lowest segment's temperature directly would evaluate the air at z = dz/2
    /// rather than at the surface, which puts an O(dz) error into the sensible heat flux and
    /// makes the whole model first order in dz. The radiative recurrence itself is exact for
    /// constant-temperature segments - subdividing an isothermal slab leaves its emission
    /// unchanged - so this extrapolation is what restores second-order convergence.
    /// </remarks>
    public static double NearSurfaceAirTemperature(Column column)
    {
        var lowest = column.Segments[0];
        if (column.Count < 2) return lowest.Temperature;

        var next = column.Segments[1];
        double dz = next.MidAltitude - lowest.MidAltitude;
        if (dz <= 0.0) return lowest.Temperature;

        double lapse = (lowest.Temperature - next.Temperature) / dz;
        return lowest.Temperature + lapse * lowest.MidAltitude;
    }

    /// <summary>
    /// Sensible heat flux from the surface into the lowest segment, W m^-2
    /// (positive upward).
    /// </summary>
    public static double SensibleHeatFlux(Column column)
    {
        if (column.Options.Convection == ConvectionMode.None) return 0.0;
        double hc = SurfaceHeatTransferCoefficient(column.Options.WindSpeed);
        return hc * (column.SurfaceTemperature - NearSurfaceAirTemperature(column));
    }

    /// <summary>
    /// Saturation vapour pressure over liquid water at <paramref name="temperature"/>, Pa,
    /// from the Clausius-Clapeyron relation integrated with a constant latent heat:
    /// <c>e_sat(T) = e_0 exp[(L/R_v)(1/T_0 - 1/T)]</c>.
    /// </summary>
    /// <remarks>
    /// This is the same relation the water-vapour absorber already uses to scale its loading
    /// with temperature, so evaporation and the greenhouse feedback are driven by one curve
    /// rather than two that could drift apart. Holding L constant overstates e_sat somewhat
    /// below freezing, where L should be the latent heat of sublimation; the surface flux this
    /// feeds is near zero at those temperatures anyway.
    /// </remarks>
    public static double SaturationVapourPressure(double temperature)
    {
        if (temperature <= 0.0) return 0.0;

        return PhysicalConstants.TriplePointVapourPressure * Math.Exp(
            PhysicalConstants.ClausiusClapeyronScale *
            (1.0 / PhysicalConstants.TriplePointTemperature - 1.0 / temperature));
    }

    /// <summary>
    /// Saturation specific humidity, kg water per kg moist air:
    /// <c>q_sat = epsilon e_sat / p</c>.
    /// </summary>
    public static double SaturationSpecificHumidity(double temperature, double pressure)
    {
        if (pressure <= 0.0) return 0.0;

        double e = SaturationVapourPressure(temperature);

        // The (1 - epsilon) e term keeps q below 1 as e approaches p, which matters only at
        // temperatures this model never reaches, but costs nothing to carry.
        return PhysicalConstants.VapourMixingRatio * e /
               (pressure - (1.0 - PhysicalConstants.VapourMixingRatio) * e);
    }

    /// <summary>
    /// d(q_sat)/dT, kg kg^-1 K^-1.
    /// </summary>
    /// <remarks>
    /// Not simply <c>q (L/R_v) / T^2</c>. That would be the answer if q were proportional to
    /// e, but the <c>(1 - epsilon) e</c> in the denominator moves too, and differentiating
    /// through it gives an extra factor <c>p / (p - (1-epsilon) e)</c> - about 1.007 at 288 K.
    /// Small, but the integrator's stability limit is built on this derivative, so it is worth
    /// being the derivative of the function actually used rather than of a simpler one.
    /// </remarks>
    public static double SaturationSpecificHumiditySlope(double temperature, double pressure)
    {
        if (pressure <= 0.0 || temperature <= 0.0) return 0.0;

        double e = SaturationVapourPressure(temperature);
        double denominator = pressure - (1.0 - PhysicalConstants.VapourMixingRatio) * e;
        if (denominator <= 0.0) return 0.0;

        // de/dT = e (L/R_v) / T^2, and dq/de = epsilon p / denominator^2.
        double dedt = e * PhysicalConstants.ClausiusClapeyronScale / (temperature * temperature);
        return PhysicalConstants.VapourMixingRatio * pressure /
               (denominator * denominator) * dedt;
    }

    /// <summary>
    /// Latent heat flux from the surface into the lowest segment, W m^-2 (positive upward):
    /// the energy carried off the surface by evaporation.
    /// </summary>
    /// <remarks>
    /// The bulk aerodynamic form, written through the sensible-heat coefficient the model
    /// already has:
    ///
    /// <code>
    ///   LE = beta * (h_c / c_p) * L * [ q_sat(T_s, p_s) - RH * q_sat(T_air, p_s) ]
    /// </code>
    ///
    /// The two fluxes share a transfer velocity - the same eddies carry heat and vapour - so
    /// <c>h_c = rho c_p C_H v</c> gives <c>h_c / c_p = rho C_H v</c>, the mass transfer
    /// coefficient in kg m^-2 s^-1, on the assumption <c>C_E = C_H</c>. That assumption is
    /// close to true over water and is what lets one wind-speed relation drive both.
    ///
    /// <c>RH</c> is the near-surface relative humidity, setting how far from saturation the
    /// receiving air is. <c>beta</c> is surface moisture availability, and it scales the whole
    /// flux rather than the surface humidity alone: the surface itself is saturated, and beta
    /// is the fraction of that potential evaporation a surface which cannot supply water fast
    /// enough actually delivers. Scaling the surface humidity instead would be a different and
    /// wrong statement - it would put a dry surface <em>below</em> the overlying air and drive
    /// perpetual dew deposition rather than merely suppressing evaporation.
    ///
    /// This term is off by default (<c>beta = 0</c>), because the model's h_c was calibrated
    /// with the sensible flux doing the work of both. Turning it on is a different model, not
    /// a correction to this one - see the note on ModelOptions.SurfaceMoistureAvailability.
    /// </remarks>
    public static double LatentHeatFlux(Column column)
    {
        if (column.Options.Convection == ConvectionMode.None) return 0.0;

        double beta = column.Options.SurfaceMoistureAvailability;
        if (beta <= 0.0) return 0.0;

        double pressure = StandardAtmosphere.SeaLevelPressure;
        double hc = SurfaceHeatTransferCoefficient(column.Options.WindSpeed);
        double massTransfer = hc / PhysicalConstants.DryAirSpecificHeat;

        double qSurface = SaturationSpecificHumidity(column.SurfaceTemperature, pressure);
        double qAir = column.Options.NearSurfaceRelativeHumidity *
                      SaturationSpecificHumidity(NearSurfaceAirTemperature(column), pressure);

        // A negative result is dew or frost deposition, which happens when the air is warmer
        // than the surface. Allowed - the surface budget has to close either way.
        return beta * massTransfer * PhysicalConstants.LatentHeatOfVaporisation *
               (qSurface - qAir);
    }

    /// <summary>
    /// How fast the latent flux grows with surface temperature, W m^-2 K^-1. The explicit
    /// integrator needs this: near 288 K it is larger than h_c itself, so leaving it out of
    /// the stability limit lets the surface oscillate rather than settle.
    /// </summary>
    public static double LatentHeatFluxSensitivity(Column column) =>
        LatentSensitivity(column, column.SurfaceTemperature, humidityScale: 1.0);

    /// <summary>
    /// How fast the latent flux <em>falls</em> as the receiving air warms, W m^-2 K^-1, since
    /// warmer air holds more vapour and takes less. Returned positive: it damps the lowest
    /// segment, so the integrator treats it exactly as it treats h_c there.
    /// </summary>
    public static double LatentHeatFluxAirSensitivity(Column column) =>
        LatentSensitivity(column, NearSurfaceAirTemperature(column),
            column.Options.NearSurfaceRelativeHumidity);

    private static double LatentSensitivity(Column column, double temperature, double humidityScale)
    {
        if (column.Options.Convection == ConvectionMode.None) return 0.0;

        double beta = column.Options.SurfaceMoistureAvailability;
        if (beta <= 0.0) return 0.0;
        if (humidityScale <= 0.0 || temperature <= 0.0) return 0.0;

        double hc = SurfaceHeatTransferCoefficient(column.Options.WindSpeed);
        double slope = SaturationSpecificHumiditySlope(
            temperature, StandardAtmosphere.SeaLevelPressure);

        return beta * hc / PhysicalConstants.DryAirSpecificHeat *
               PhysicalConstants.LatentHeatOfVaporisation * humidityScale * slope;
    }

    /// <summary>
    /// Sol-air temperature, K: the fictitious outdoor air temperature that would give the
    /// same surface heat flux as the combined effect of absorbed solar radiation and
    /// longwave exchange. Provided as a diagnostic of the surface energy balance.
    /// </summary>
    /// <param name="airTemperature">Air temperature adjacent to the surface, K.</param>
    /// <param name="absorbedShortwave">Solar flux absorbed by the surface, W m^-2.</param>
    /// <param name="netLongwaveLoss">Net longwave loss from the surface, W m^-2.</param>
    /// <param name="windSpeed">Near-surface wind speed, m s^-1.</param>
    public static double SolAirTemperature(
        double airTemperature, double absorbedShortwave, double netLongwaveLoss, double windSpeed)
    {
        double hc = SurfaceHeatTransferCoefficient(windSpeed);
        return airTemperature + (absorbedShortwave - netLongwaveLoss) / hc;
    }

    /// <summary>
    /// Convective adjustment of the atmospheric segments. Wherever the lapse rate between
    /// adjacent segments exceeds the critical value, the affected block is mixed onto
    /// exactly the critical lapse rate while conserving the enthalpy sum(C_i T_i).
    /// Returns the number of segments that were adjusted.
    /// </summary>
    /// <remarks>
    /// The surface is deliberately excluded. It exchanges heat with the air only through
    /// radiation and the bulk sensible flux h_c (T_s - T_1); letting the adjustment also
    /// mix the surface reservoir would double count that transfer and leave the surface
    /// energy budget open. Excluding it keeps the surface balance closed and permits the
    /// superadiabatic surface layer that the h_c relation is meant to describe.
    /// </remarks>
    public static int Adjust(Column column)
    {
        if (column.Options.Convection != ConvectionMode.Full) return 0;

        int m = column.Count;
        if (m < 2) return 0;

        double gamma = column.Options.CriticalLapseRate;

        var z = new double[m];
        var c = new double[m];
        var t = new double[m];

        for (int i = 0; i < m; i++)
        {
            z[i] = column.Segments[i].MidAltitude;
            c[i] = column.Segments[i].HeatCapacity;
            t[i] = column.Segments[i].Temperature;
        }

        const double tol = 1e-12;
        bool Unstable(int k) => t[k] - t[k + 1] > gamma * (z[k + 1] - z[k]) + tol;

        void Mix(int lo, int hi)
        {
            double sumC = 0.0, sumCT = 0.0, sumCz = 0.0;
            for (int k = lo; k <= hi; k++)
            {
                sumC += c[k];
                sumCT += c[k] * t[k];
                sumCz += c[k] * (z[k] - z[lo]);
            }
            // T_k = tRef - gamma * (z_k - z_lo), conserving sum(c_k T_k).
            double tRef = (sumCT + gamma * sumCz) / sumC;
            for (int k = lo; k <= hi; k++) t[k] = tRef - gamma * (z[k] - z[lo]);
        }

        var touched = new bool[m];
        bool changed = true;
        int guard = 0;
        const int guardLimit = 10_000;

        while (changed && guard++ < guardLimit)
        {
            changed = false;
            for (int k = 0; k < m - 1; k++)
            {
                if (!Unstable(k)) continue;

                int lo = k, hi = k + 1;
                while (true)
                {
                    Mix(lo, hi);
                    bool extended = false;
                    if (lo > 0 && Unstable(lo - 1)) { lo--; extended = true; }
                    if (hi < m - 1 && Unstable(hi)) { hi++; extended = true; }
                    if (!extended) break;
                }

                for (int j = lo; j <= hi; j++) touched[j] = true;
                changed = true;
            }
        }

        if (changed)
        {
            throw new InvalidOperationException(
                $"Convective adjustment failed to reach a stable profile in {guardLimit} sweeps. " +
                "The profile has been left unmodified.");
        }

        for (int i = 0; i < m; i++) column.Segments[i].Temperature = t[i];

        int count = 0;
        foreach (var b in touched) if (b) count++;
        return count;
    }
}
