namespace ClimateColumn.Core;

/// <summary>
/// Physical constants used throughout the column model. SI units unless stated.
/// </summary>
public static class PhysicalConstants
{
    /// <summary>Stefan-Boltzmann constant, W m^-2 K^-4 (CODATA 2018, exact).</summary>
    public const double StefanBoltzmann = 5.670374419e-8;

    /// <summary>Standard gravitational acceleration, m s^-2.</summary>
    public const double Gravity = 9.80665;

    /// <summary>Specific gas constant for dry air, J kg^-1 K^-1.</summary>
    public const double DryAirGasConstant = 287.0528;

    /// <summary>Isobaric specific heat capacity of dry air, J kg^-1 K^-1.</summary>
    public const double DryAirSpecificHeat = 1004.68;

    /// <summary>Total solar irradiance at 1 AU, W m^-2.</summary>
    public const double SolarConstant = 1361.0;

    /// <summary>Dry adiabatic lapse rate, K m^-1 (= g / c_p).</summary>
    public const double DryAdiabaticLapseRate = Gravity / DryAirSpecificHeat;

    /// <summary>
    /// Two-stream diffusivity factor implied by the Koenigsberger volumetric
    /// emission law dq = 4 eps' sigma T^4 dV.
    /// </summary>
    /// <remarks>
    /// A slab of thickness dz emits 4 eps' sigma T^4 dz per unit horizontal area in
    /// total. Splitting that isotropically between the upward and downward hemispheres
    /// gives 2 eps' sigma T^4 dz each way, so the hemispheric emissivity of the slab is
    /// a = 2 eps' dz. Kirchhoff's law then forces the hemispheric absorptivity to the
    /// same value, i.e. the flux-space extinction coefficient is D * eps' with D = 2.
    /// This is also the exact optically thin limit of the true angular integral,
    /// since the hemispheric transmission 2*E3(tau) -> 1 - 2*tau as tau -> 0.
    /// The familiar D = 1.66 is a best fit across a broad range of optical depth and
    /// is offered as an alternative, but D = 2 is the value consistent with the
    /// Koenigsberger equation as written.
    /// </remarks>
    public const double KoenigsbergerDiffusivity = 2.0;

    /// <summary>Elsasser's empirical diffusivity factor, for comparison runs.</summary>
    public const double ElsasserDiffusivity = 1.66;

    /// <summary>Latent heat of vaporisation of water at 0 C, J kg^-1.</summary>
    public const double LatentHeatOfVaporisation = 2.501e6;

    /// <summary>Specific gas constant for water vapour, J kg^-1 K^-1.</summary>
    public const double WaterVapourGasConstant = 461.52;

    /// <summary>
    /// Clausius-Clapeyron temperature scale L / R_v, K. Saturation vapour pressure goes as
    /// exp(-L / (R_v T)), so a water-vapour absorber loading scales between temperatures as
    /// exp(L/R_v (1/T_ref - 1/T)) - about +6.5 %/K near 288 K.
    /// </summary>
    public const double ClausiusClapeyronScale = LatentHeatOfVaporisation / WaterVapourGasConstant;

    /// <summary>Saturation vapour pressure over water at the triple point, Pa.</summary>
    public const double TriplePointVapourPressure = 611.2;

    /// <summary>The triple point of water, K - the reference for the integrated C-C relation.</summary>
    public const double TriplePointTemperature = 273.16;

    /// <summary>
    /// Ratio of the dry-air and water-vapour gas constants, R_d / R_v = 0.622. Converts a
    /// vapour pressure into a specific humidity: q = epsilon e / p.
    /// </summary>
    public const double VapourMixingRatio = DryAirGasConstant / WaterVapourGasConstant;

    /// <summary>Seconds in a day.</summary>
    public const double SecondsPerDay = 86400.0;
}
