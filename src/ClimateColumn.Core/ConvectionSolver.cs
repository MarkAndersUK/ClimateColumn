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
