namespace ClimateColumn.Core;

/// <summary>
/// U.S. Standard Atmosphere 1976, used to initialise the temperature profile and to
/// build the hydrostatic pressure/mass grid of the column.
/// </summary>
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

    /// <summary>Standard temperature (K) at geometric altitude z (m).</summary>
    public static double Temperature(double z)
    {
        z = Math.Clamp(z, 0.0, BaseAltitude[^1]);
        int b = LayerIndex(z);
        return BaseTemperature[b] + LapseRate[b] * (z - BaseAltitude[b]);
    }

    /// <summary>Standard pressure (Pa) at geometric altitude z (m).</summary>
    public static double Pressure(double z)
    {
        z = Math.Clamp(z, 0.0, BaseAltitude[^1]);
        int b = LayerIndex(z);
        return PressureWithinLayer(b, BasePressure[b], z);
    }

    /// <summary>Standard air density (kg m^-3) at geometric altitude z (m).</summary>
    public static double Density(double z) =>
        Pressure(z) / (PhysicalConstants.DryAirGasConstant * Temperature(z));
}
