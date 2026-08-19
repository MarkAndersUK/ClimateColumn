namespace ClimateColumn.Core;

/// <summary>One level of a vertical profile: where it is, and how warm it is there.</summary>
/// <remarks>
/// Altitude is geometric, metres above the surface, and pressure is the layer mean. Both are
/// carried because a profile is read two ways - against height, which is what a picture of the
/// atmosphere wants, and against pressure, which is what the radiative transfer actually sees.
/// </remarks>
public sealed record ProfileLevel(double Altitude, double Pressure, double Temperature);

/// <summary>
/// The vertical state of one equilibrium, kept after the run that produced it.
/// </summary>
/// <remarks>
/// A <see cref="Co2Sweep"/> previously reduced each equilibrium to a surface temperature and
/// discarded the column, which is all a response curve needs. It is not all there is to see:
/// nearly everything the model does happens between the ground and 50 km, and a surface
/// temperature is the one number that hides it.
///
/// This is a snapshot rather than a reference to the live <see cref="Column"/>. The column is
/// mutable and is reused - the forcing calculation copies temperatures out of it, and the
/// solver marches it in place - so holding one would risk showing a profile that had moved on
/// since the equilibrium it claims to be.
/// </remarks>
public sealed class ColumnProfile
{
    /// <summary>Which configuration this came from.</summary>
    public required string Label { get; init; }

    /// <summary>CO2 concentration this equilibrium was reached at, ppm.</summary>
    public required double Ppm { get; init; }

    /// <summary>Layer midpoints, lowest first.</summary>
    public required IReadOnlyList<ProfileLevel> Levels { get; init; }

    /// <summary>Ground temperature, K.</summary>
    public required double SurfaceTemperature { get; init; }

    /// <summary>
    /// Air temperature extrapolated to z = 0, K. Distinct from
    /// <see cref="SurfaceTemperature"/>: the difference between the two is what drives the
    /// sensible heat flux, so a profile that drew only one of them would hide the mechanism.
    /// </summary>
    public required double NearSurfaceAirTemperature { get; init; }

    /// <summary>Top of the convecting layer, m. Zero when nothing is convecting.</summary>
    public required double ConvectiveTopAltitude { get; init; }

    /// <summary>Critical lapse rate the convecting layer is held at, K m^-1.</summary>
    public required double CriticalLapseRate { get; init; }

    /// <summary>Planetary emission temperature (S(1-a)/4sigma)^(1/4), K.</summary>
    public required double EmissionTemperature { get; init; }

    /// <summary>Whether the march that produced this profile reached equilibrium.</summary>
    public required bool Converged { get; init; }

    /// <summary>
    /// Top of the modelled column, m - the upper boundary, not the highest level.
    /// </summary>
    /// <remarks>
    /// These differ, and the difference is visible on a figure. Levels sit at layer midpoints,
    /// so the highest one is half a layer below the top of the column: 48.3 km for a 50 km
    /// column of thirty layers. An axis scaled to the highest level would stop just short of a
    /// round number and read as though the model ended there.
    /// </remarks>
    public required double ColumnTopAltitude { get; init; }

    /// <summary>Fraction of the sky covered by cloud, 0 when there is none.</summary>
    public double CloudFraction { get; init; }

    /// <summary>Cloud base altitude, m. Meaningless when <see cref="CloudFraction"/> is zero.</summary>
    public double CloudBaseAltitude { get; init; }

    /// <summary>Cloud top altitude, m. Meaningless when <see cref="CloudFraction"/> is zero.</summary>
    public double CloudTopAltitude { get; init; }

    /// <summary>Net cloud radiative effect at this equilibrium, W m^-2. Zero without cloud.</summary>
    public double NetCloudRadiativeEffect { get; init; }

    /// <summary>
    /// Altitude at which the profile passes through the emission temperature, m, or NaN when
    /// it does not cross within the column.
    /// </summary>
    /// <remarks>
    /// This is a diagnostic, not a physical level. The actual emission to space is spectral -
    /// each band radiates from wherever it becomes transparent, the window from the ground and
    /// the 15 micron core from the stratosphere - so there is no single altitude the planet
    /// emits from. What this marks is the one height where the column happens to be as warm as
    /// the planet must be seen to be, which is a useful place to hang the greenhouse argument
    /// on a picture: the surface is warmer than that level, by the lapse rate times its height.
    ///
    /// The lowest crossing is taken. Above the convecting layer the profile can wander back
    /// across the same temperature, and the first crossing is the one that argument refers to.
    /// </remarks>
    public double EmissionAltitude
    {
        get
        {
            for (int i = 0; i + 1 < Levels.Count; i++)
            {
                double lower = Levels[i].Temperature, upper = Levels[i + 1].Temperature;

                // Descending through the target between these two levels.
                if ((lower - EmissionTemperature) * (upper - EmissionTemperature) > 0.0) continue;
                if (Math.Abs(lower - upper) < 1e-12) return Levels[i].Altitude;

                double f = (lower - EmissionTemperature) / (lower - upper);
                return Levels[i].Altitude + f * (Levels[i + 1].Altitude - Levels[i].Altitude);
            }
            return double.NaN;
        }
    }

    /// <summary>
    /// Temperature at an arbitrary altitude, K, linearly interpolated between levels and held
    /// flat outside them. Used to compare two profiles that were run at different resolutions.
    /// </summary>
    public double TemperatureAt(double altitude)
    {
        if (Levels.Count == 0) return double.NaN;
        if (altitude <= Levels[0].Altitude) return Levels[0].Temperature;
        if (altitude >= Levels[^1].Altitude) return Levels[^1].Temperature;

        for (int i = 0; i + 1 < Levels.Count; i++)
        {
            if (altitude > Levels[i + 1].Altitude) continue;

            double span = Levels[i + 1].Altitude - Levels[i].Altitude;
            if (span <= 0.0) return Levels[i].Temperature;

            double f = (altitude - Levels[i].Altitude) / span;
            return Levels[i].Temperature + f * (Levels[i + 1].Temperature - Levels[i].Temperature);
        }
        return Levels[^1].Temperature;
    }

    /// <summary>
    /// Whether this profile is the same curve as <paramref name="other"/>, to within
    /// <paramref name="tolerance"/> kelvin at every level.
    /// </summary>
    /// <remarks>
    /// The same rule the response chart applies, for the same reason: painting one curve
    /// exactly over another hides the first, and advertises a legend colour that is nowhere on
    /// the figure. It is not hypothetical on a profile either. At the reference concentration
    /// the two configurations are identical by construction - the frozen-vapour run is frozen
    /// at the other one's base state - so their profiles coincide exactly until CO2 is added.
    /// </remarks>
    public bool Matches(ColumnProfile other, double tolerance = 1e-6)
    {
        if (Levels.Count != other.Levels.Count) return false;
        if (Math.Abs(SurfaceTemperature - other.SurfaceTemperature) > tolerance) return false;

        for (int i = 0; i < Levels.Count; i++)
        {
            if (Math.Abs(Levels[i].Temperature - other.Levels[i].Temperature) > tolerance)
                return false;
        }
        return true;
    }

    /// <summary>Snapshots the vertical state of a finished run.</summary>
    public static ColumnProfile From(ModelResult result, string label, double ppm)
    {
        var levels = result.Column.Segments
            .Select(s => new ProfileLevel(s.MidAltitude, s.MidPressure, s.Temperature))
            .ToArray();

        return new ColumnProfile
        {
            Label = label,
            Ppm = ppm,
            Levels = levels,
            SurfaceTemperature = result.SurfaceTemperature,
            NearSurfaceAirTemperature = result.NearSurfaceAirTemperature,
            ColumnTopAltitude = result.Column.Options.TopAltitude,
            CloudFraction = result.Column.Options.CloudFraction,
            CloudBaseAltitude = result.Column.Options.CloudBaseAltitude,
            CloudTopAltitude = result.Column.Options.CloudTopAltitude,
            NetCloudRadiativeEffect = result.NetCloudRadiativeEffect,
            ConvectiveTopAltitude = result.ConvectiveTopAltitude,
            CriticalLapseRate = result.Column.Options.CriticalLapseRate,
            EmissionTemperature = result.EmissionTemperature,
            Converged = result.Converged
        };
    }
}
