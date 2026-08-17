namespace ClimateColumn.Core;

/// <summary>
/// U.S. Standard Atmosphere 1976, used to initialise the temperature profile and to
/// build the hydrostatic pressure/mass grid of the column.
/// </summary>
/// <remarks>
/// The standard is defined on <em>geopotential</em> altitude with gravity fixed at the defined
/// constant g_0 = 9.80665 m s^-2. That is not an approximation in the standard - it is how the
/// standard absorbs the variation of gravity with height, by measuring altitude in units of
/// work done against gravity rather than in metres.
///
/// So there are two consistent ways to use these tables, and they differ by which altitude the
/// caller means. The single-argument methods treat their argument as geopotential, which is the
/// standard's own variable and reproduces its published table exactly. The overloads taking a
/// planet radius treat their argument as <em>geometric</em> altitude and convert,
/// <c>H = r_0 z / (r_0 + z)</c> - 50 geometric km is 49.61 geopotential km on Earth. That
/// conversion is where the inverse-square law enters.
/// </remarks>
public static class StandardAtmosphere
{
    // Geopotential base altitudes (m), base temperatures (K), lapse rates (K/m).
    private static readonly double[] BaseAltitude = { 0, 11000, 20000, 32000, 47000, 51000, 71000, 84852 };
    private static readonly double[] BaseTemperature = { 288.15, 216.65, 216.65, 228.65, 270.65, 270.65, 214.65, 186.946 };
    private static readonly double[] LapseRate = { -0.0065, 0.0, 0.001, 0.0028, 0.0, -0.0028, -0.002, 0.0 };

    /// <summary>Sea level pressure, Pa.</summary>
    public const double SeaLevelPressure = 101325.0;

    private static readonly double[] BasePressure = BuildBasePressures();

    private static double[] BuildBasePressures()
    {
        var p = new double[BaseAltitude.Length];
        p[0] = SeaLevelPressure;
        for (int b = 1; b < p.Length; b++)
        {
            p[b] = PressureWithinLayer(b - 1, p[b - 1], BaseAltitude[b]);
        }
        return p;
    }

    private static double PressureWithinLayer(int b, double basePressure, double z)
    {
        double tb = BaseTemperature[b];
        double lb = LapseRate[b];
        double dz = z - BaseAltitude[b];
        double exponent = PhysicalConstants.Gravity / (PhysicalConstants.DryAirGasConstant * lb);

        if (Math.Abs(lb) < 1e-12)
        {
            return basePressure * Math.Exp(-PhysicalConstants.Gravity * dz /
                                           (PhysicalConstants.DryAirGasConstant * tb));
        }

        return basePressure * Math.Pow(tb / (tb + lb * dz), exponent);
    }

    private static int LayerIndex(double z)
    {
        int b = 0;
        while (b < BaseAltitude.Length - 1 && z >= BaseAltitude[b + 1]) b++;
        return b;
    }

    /// <summary>
    /// Geopotential altitude, m, corresponding to geometric altitude <paramref name="z"/>:
    /// <c>H = r_0 z / (r_0 + z)</c>.
    /// </summary>
    /// <remarks>
    /// The geopotential is the work per unit mass done climbing to z against the inverse-square
    /// field, <c>Phi(z) = int_0^z g_0 (r_0/(r_0+z'))^2 dz' = g_0 r_0 z / (r_0 + z)</c>, and the
    /// geopotential altitude is that divided by g_0. It is always slightly below the geometric
    /// altitude, because gravity weakens on the way up: 49,610 m for 50,000 m on Earth.
    /// </remarks>
    public static double GeopotentialAltitude(double z, double planetRadius)
    {
        if (planetRadius <= 0.0) return z;
        return planetRadius * z / (planetRadius + z);
    }

    /// <summary>Standard temperature (K) at geopotential altitude h (m).</summary>
    public static double Temperature(double h)
    {
        h = Math.Clamp(h, 0.0, BaseAltitude[^1]);
        int b = LayerIndex(h);
        return BaseTemperature[b] + LapseRate[b] * (h - BaseAltitude[b]);
    }

    /// <summary>Standard temperature (K) at <em>geometric</em> altitude z (m).</summary>
    public static double Temperature(double z, double planetRadius) =>
        Temperature(GeopotentialAltitude(z, planetRadius));

    /// <summary>Standard pressure (Pa) at geopotential altitude h (m).</summary>
    public static double Pressure(double h)
    {
        h = Math.Clamp(h, 0.0, BaseAltitude[^1]);
        int b = LayerIndex(h);
        return PressureWithinLayer(b, BasePressure[b], h);
    }

    /// <summary>Standard pressure (Pa) at <em>geometric</em> altitude z (m).</summary>
    public static double Pressure(double z, double planetRadius) =>
        Pressure(GeopotentialAltitude(z, planetRadius));

    /// <summary>Standard air density (kg m^-3) at geopotential altitude h (m).</summary>
    public static double Density(double h) =>
        Pressure(h) / (PhysicalConstants.DryAirGasConstant * Temperature(h));

    /// <summary>Standard air density (kg m^-3) at <em>geometric</em> altitude z (m).</summary>
    public static double Density(double z, double planetRadius) =>
        Density(GeopotentialAltitude(z, planetRadius));
}
