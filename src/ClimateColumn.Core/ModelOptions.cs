namespace ClimateColumn.Core;

public enum ConvectionMode
{
    /// <summary>Pure radiative equilibrium. No convective transport at all.</summary>
    None,

    /// <summary>Surface-to-air sensible heat exchange only (Koenigsberger h_c).</summary>
    SurfaceOnly,

    /// <summary>Surface exchange plus a critical-lapse-rate convective adjustment.</summary>
    Full
}

public sealed class ModelOptions
{
    /// <summary>
    /// Number of segments the column is divided into. Default 80 (625 m each over a 50 km
    /// column), which resolves the profile comfortably and is well inside the numerical
    /// noise floor: the equilibrium surface temperature moves by under 0.01 K on refinement
    /// to 640 segments.
    /// </summary>
    /// <remarks>
    /// The flux recurrence is the exact solution of the Schwarzschild equation across a
    /// constant-temperature segment, so subdividing an isothermal slab changes nothing at
    /// all; the only discretisation error is from the temperature varying within a segment,
    /// which is second order in dz. Resolution is therefore chosen for profile detail
    /// rather than for accuracy. Use --grid-convergence in the CLI to confirm this for a
    /// given configuration - it matters more at large optical depth, where the temperature
    /// gradient across a segment is steeper.
    /// </remarks>
    public int SegmentCount { get; set; } = 80;

    /// <summary>Altitude of the top of the column, m.</summary>
    public double TopAltitude { get; set; } = 50_000.0;

    /// <summary>Total solar irradiance, W m^-2.</summary>
    public double SolarConstant { get; set; } = PhysicalConstants.SolarConstant;

    /// <summary>Planetary albedo (fraction of incoming solar reflected).</summary>
    public double Albedo { get; set; } = 0.30;

    /// <summary>Fraction of the absorbed solar flux deposited in the atmosphere.</summary>
    public double AtmosphericShortwaveFraction { get; set; } = 0.22;

    /// <summary>Longwave emissivity of the surface (Stefan-Boltzmann boundary).</summary>
    public double SurfaceEmissivity { get; set; } = 0.98;

    /// <summary>
    /// Absorber loading, expressed as the column hemispheric optical depth it produces at
    /// the Koenigsberger diffusivity D = 2. The per-segment eps' is distributed in
    /// proportion to air density, i.e. a well-mixed grey absorber, and is independent of
    /// <see cref="Diffusivity"/>. The optical depth the flux solver actually sees is
    /// therefore (D / 2) times this value; read it back with Column.TotalOpticalDepth().
    /// </summary>
    public double TotalOpticalDepth { get; set; } = 1.8;

    /// <summary>
    /// Multiplier on <see cref="TotalOpticalDepth"/>, for forcing experiments. It scales the
    /// dry absorber only, not <see cref="WaterVapourOpticalDepth"/>: the dry gas is the
    /// forcing agent and the water vapour is the feedback that responds to it.
    /// </summary>
    public double OpticalDepthScale { get; set; } = 1.0;

    /// <summary>
    /// CO2 concentration, ppm. Optical depth is linear in absorber amount, so the CO2 share
    /// of the dry absorber scales as <see cref="Co2Concentration"/> /
    /// <see cref="Co2ReferenceConcentration"/>. Defaults equal, so the factor is 1 and the
    /// model is unchanged unless you set it.
    /// </summary>
    /// <remarks>
    /// Linear tau is the correct optical-depth scaling; it is the *forcing* that is
    /// logarithmic in concentration. The grey model gets the qualitative shape of that
    /// right - successive doublings do force less and less, because the emission level
    /// rises into thinner air - but it oversaturates (each doubling buys about half the
    /// last, where the real gas is near-constant) and, far worse, its absolute magnitude is
    /// roughly an order of magnitude too large: about 54 W/m2 per doubling against the
    /// accepted 3.7. So a raw concentration run is not a credible estimate.
    ///
    /// Two knobs bring the magnitude back, both multiplicative and neither distorting the
    /// concentration dependence: <see cref="WindowFraction"/>, which scales every forcing by
    /// exactly (1 - f) at fixed temperature, and <see cref="Co2AbsorberFraction"/>, which
    /// says only part of the opacity is CO2 at all. The second is usually the better choice
    /// for concentration work because it leaves the base state alone, whereas a large window
    /// cools the column badly. Calibrate one of them against a known forcing, then read the
    /// temperature response off the model's own dynamics.
    /// </remarks>
    public double Co2Concentration { get; set; } = 285.0;

    /// <summary>Concentration at which <see cref="TotalOpticalDepth"/> is specified, ppm.</summary>
    public double Co2ReferenceConcentration { get; set; } = 285.0;

    /// <summary>
    /// Share of the dry absorber attributable to CO2 **at the reference concentration** -
    /// the part that responds to <see cref="Co2Concentration"/>. The remaining (1 - this)
    /// stands for the other well-mixed gases and never changes. 1.0 by default: with the
    /// water vapour component carried separately by <see cref="WaterVapourOpticalDepth"/>,
    /// the dry absorber is predominantly CO2.
    /// </summary>
    /// <remarks>
    /// This is a fixed input, not a state variable: raising <see cref="Co2Concentration"/>
    /// does not change it. What does change is the CO2 fraction the column actually ends up
    /// with, because the CO2 component grows while the rest does not. Starting from a share
    /// f at the reference, at a concentration ratio r the realised fraction is
    /// f r / ((1 - f) + f r) - so f = 0.06 at 285 ppm is 6.0 % of the dry absorber there and
    /// 8.7 % of it at 425 ppm. Set this from the composition of the reference state and
    /// leave it alone across a scenario; changing it per concentration would be double
    /// counting the growth the ratio already applies.
    /// </remarks>
    public double Co2AbsorberFraction { get; set; } = 1.0;

    /// <summary>
    /// Fraction of the longwave spectrum lying in a transparent window where the absorber
    /// neither absorbs nor emits. The remaining (1 - f) of the spectrum stays grey. 0 (the
    /// default) is the pure grey model; Earth's water-vapour window is roughly 0.3 for the
    /// surface Planck spectrum. The window fraction of the surface emission escapes to space
    /// unattenuated, which is what tames the grey model's badly overstated doubling forcing:
    /// the instantaneous forcing scales as exactly (1 - f).
    /// </summary>
    public double WindowFraction { get; set; } = 0.0;

    /// <summary>
    /// Pressure-broadening exponent n: the dry absorber is distributed as
    /// eps' ~ rho (p/p0)^n instead of eps' ~ rho, renormalised to the same column optical
    /// depth. 0 (the default) is the unbroadened well-mixed absorber; n = 1 approximates
    /// collision-broadened line wings. Positive n concentrates the absorber toward the
    /// surface without changing the column total.
    /// </summary>
    public double PressureBroadeningExponent { get; set; } = 0.0;

    /// <summary>
    /// Fraction of the atmospheric solar absorption deposited in an ozone-like stratospheric
    /// layer instead of being spread by air mass. The layer has the classic Chapman profile
    /// exp(1 - x - e^-x) with x = (z - z0)/H. 0 (the default) disables it; ~0.3 produces a
    /// realistic stratospheric temperature inversion.
    /// </summary>
    public double OzoneFraction { get; set; } = 0.0;

    /// <summary>Altitude of the Chapman-layer heating maximum, m.</summary>
    public double OzoneLayerAltitude { get; set; } = 25_000.0;

    /// <summary>Chapman-layer scale height H, m.</summary>
    public double OzoneLayerWidth { get; set; } = 5_000.0;

    /// <summary>
    /// Column optical depth of a water-vapour-like absorber at
    /// <see cref="WaterVapourReferenceTemperature"/>. 0 (the default) disables it. The
    /// actual loading follows Clausius-Clapeyron on the near-surface air temperature,
    /// exp(L/R_v (1/T_ref - 1/T_air)), and is re-evaluated at every time step, so it is a
    /// genuine temperature feedback: forcing experiments via
    /// <see cref="OpticalDepthScale"/> perturb the dry absorber and the vapour responds.
    /// It is distributed as exp(-z / <see cref="WaterVapourScaleHeight"/>), concentrated in
    /// the lower troposphere, unlike the well-mixed dry absorber.
    /// </summary>
    public double WaterVapourOpticalDepth { get; set; } = 0.0;

    /// <summary>Scale height of the water-vapour absorber, m (~2 km on Earth).</summary>
    public double WaterVapourScaleHeight { get; set; } = 2_000.0;

    /// <summary>Temperature at which <see cref="WaterVapourOpticalDepth"/> is specified, K.</summary>
    public double WaterVapourReferenceTemperature { get; set; } = 288.15;

    /// <summary>Two-stream diffusivity factor D. 2.0 is Koenigsberger-consistent.</summary>
    public double Diffusivity { get; set; } = PhysicalConstants.KoenigsbergerDiffusivity;

    /// <summary>Convective transport treatment.</summary>
    public ConvectionMode Convection { get; set; } = ConvectionMode.Full;

    /// <summary>Near-surface wind speed used in the Koenigsberger h_c relation, m s^-1.</summary>
    public double WindSpeed { get; set; } = 3.0;

    /// <summary>Critical lapse rate for convective adjustment, K m^-1.</summary>
    public double CriticalLapseRate { get; set; } = 0.0065;

    /// <summary>Surface heat capacity per unit area, J m^-2 K^-1 (default ~10 m of water).</summary>
    public double SurfaceHeatCapacity { get; set; } = 4.18e7;

    /// <summary>Initial surface temperature, K.</summary>
    public double InitialSurfaceTemperature { get; set; } = 288.15;

    /// <summary>Maximum number of time steps taken while marching to equilibrium.</summary>
    public int MaxSteps { get; set; } = 500_000;

    /// <summary>Largest temperature change allowed in one step, K (sets the adaptive dt).</summary>
    public double MaxTemperatureStep { get; set; } = 1.0;

    /// <summary>Hard cap on the time step, s.</summary>
    public double MaxTimeStep { get; set; } = 5.0 * PhysicalConstants.SecondsPerDay;

    /// <summary>Convergence threshold on the top-of-atmosphere net flux, W m^-2.</summary>
    public double FluxTolerance { get; set; } = 1e-6;

    /// <summary>Convergence threshold on the largest temperature tendency, K per step.</summary>
    public double TemperatureTolerance { get; set; } = 1e-9;

    /// <summary>Initialise the profile from the U.S. Standard Atmosphere (else isothermal).</summary>
    public bool InitialiseFromStandardAtmosphere { get; set; } = true;

    public ModelOptions Clone() => (ModelOptions)MemberwiseClone();

    /// <summary>
    /// Column optical depth of the dry absorber actually used, after the CO2 concentration
    /// scaling and <see cref="OpticalDepthScale"/>.
    /// </summary>
    public double EffectiveDryOpticalDepth
    {
        get
        {
            double f = Co2AbsorberFraction;
            double ratio = Co2ReferenceConcentration > 0
                ? Co2Concentration / Co2ReferenceConcentration
                : 1.0;
            return TotalOpticalDepth * OpticalDepthScale * ((1.0 - f) + f * ratio);
        }
    }

    /// <summary>Solar flux absorbed by the planet, averaged over the sphere, W m^-2.</summary>
    public double AbsorbedSolarFlux => 0.25 * SolarConstant * (1.0 - Albedo);

    /// <summary>Equivalent blackbody (emission) temperature of the planet, K.</summary>
    public double EmissionTemperature =>
        Math.Pow(AbsorbedSolarFlux / PhysicalConstants.StefanBoltzmann, 0.25);

    public void Validate()
    {
        if (SegmentCount < 1) throw new ArgumentException("SegmentCount must be >= 1.");
        if (TopAltitude <= 0) throw new ArgumentException("TopAltitude must be positive.");
        if (Albedo is < 0 or >= 1) throw new ArgumentException("Albedo must be in [0, 1).");
        if (AtmosphericShortwaveFraction is < 0 or > 1)
            throw new ArgumentException("AtmosphericShortwaveFraction must be in [0, 1].");
        if (SurfaceEmissivity is <= 0 or > 1)
            throw new ArgumentException("SurfaceEmissivity must be in (0, 1].");
        if (TotalOpticalDepth < 0) throw new ArgumentException("TotalOpticalDepth must be >= 0.");
        if (Diffusivity <= 0) throw new ArgumentException("Diffusivity must be positive.");
        if (WindowFraction is < 0 or > 1)
            throw new ArgumentException("WindowFraction must be in [0, 1].");
        if (Co2Concentration < 0) throw new ArgumentException("Co2Concentration must be >= 0.");
        if (Co2ReferenceConcentration <= 0)
            throw new ArgumentException("Co2ReferenceConcentration must be positive.");
        if (Co2AbsorberFraction is < 0 or > 1)
            throw new ArgumentException("Co2AbsorberFraction must be in [0, 1].");
        if (PressureBroadeningExponent < 0)
            throw new ArgumentException("PressureBroadeningExponent must be >= 0.");
        if (OzoneFraction is < 0 or > 1)
            throw new ArgumentException("OzoneFraction must be in [0, 1].");
        if (OzoneLayerWidth <= 0) throw new ArgumentException("OzoneLayerWidth must be positive.");
        if (WaterVapourOpticalDepth < 0)
            throw new ArgumentException("WaterVapourOpticalDepth must be >= 0.");
        if (WaterVapourScaleHeight <= 0)
            throw new ArgumentException("WaterVapourScaleHeight must be positive.");
        if (WaterVapourReferenceTemperature <= 0)
            throw new ArgumentException("WaterVapourReferenceTemperature must be positive.");

        if (SurfaceHeatCapacity <= 0) throw new ArgumentException("SurfaceHeatCapacity must be positive.");
    }

    /// <summary>
    /// Additional checks that only matter when the column is marched in time. Building and
    /// inspecting a column is always allowed; integrating one that has no equilibrium is not.
    /// </summary>
    public void ValidateForIntegration()
    {
        Validate();

        // A longwave-transparent atmosphere that still absorbs sunlight has no way to shed
        // that energy: there is no equilibrium and the integration runs away to arbitrarily
        // high temperatures. Transparency arises two ways: no absorber at all (dry and water
        // vapour both zero), or a window covering the whole spectrum. Reject the combination
        // rather than return a garbage state.
        double absorber = EffectiveDryOpticalDepth + WaterVapourOpticalDepth;
        if ((absorber <= 0.0 || WindowFraction >= 1.0) && AtmosphericShortwaveFraction > 0.0)
        {
            throw new ArgumentException(
                "A longwave-transparent atmosphere (no absorber, or a window covering the " +
                "whole spectrum) cannot radiate away the solar flux it absorbs, so no " +
                "equilibrium exists. Set AtmosphericShortwaveFraction to 0 in that case.");
        }
    }
}
