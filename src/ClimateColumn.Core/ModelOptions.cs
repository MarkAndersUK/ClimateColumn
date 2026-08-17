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
    /// Two knobs bring the magnitude back, neither distorting the concentration dependence
    /// much: a spectral window (<see cref="WindowShortWavelength"/>), which suppresses every
    /// forcing by roughly the share of the Planck function it removes, and
    /// <see cref="Co2AbsorberFraction"/>, which says only part of the opacity is CO2 at all.
    /// The second is usually the better choice
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
    /// Short-wavelength edge of the transparent spectral window, m. Zero width (the default,
    /// with <see cref="WindowLongWavelength"/> also 0) is the pure grey model. Earth's
    /// water-vapour window is about 8 to 13 um.
    /// </summary>
    /// <remarks>
    /// The window is specified as an interval rather than as a fraction of the spectrum
    /// because the fraction is not a property of the atmosphere alone - it depends on the
    /// temperature of whatever is emitting. For 8-13 um it is 31.1 % of a 287 K surface's
    /// emission and 20.0 % of a 217 K tropopause's. Naming the interval lets each emitter's
    /// share follow from its own Planck function; naming a single fraction forces one
    /// emitter's answer on all of them, and gets the cold end wrong by nearly a factor of two
    /// exactly where the outgoing longwave is set.
    ///
    /// Inside the window the absorber neither absorbs nor emits, so the window share of the
    /// surface emission escapes to space unattenuated. That is what tames the grey model's
    /// badly overstated doubling forcing. Read the share back with
    /// <see cref="WindowShare(double)"/>.
    /// </remarks>
    public double WindowShortWavelength { get; set; } = 0.0;

    /// <summary>Long-wavelength edge of the transparent spectral window, m.</summary>
    public double WindowLongWavelength { get; set; } = 0.0;

    /// <summary>
    /// Column optical depth of the water-vapour continuum inside the window, evaluated at
    /// <see cref="WaterVapourReferenceTemperature"/>. 0 (the default) leaves the window
    /// perfectly transparent. Requires <see cref="WaterVapourOpticalDepth"/> to be non-zero -
    /// it is a vapour continuum, so there has to be vapour.
    /// </summary>
    /// <remarks>
    /// The window is not actually transparent. What closes it over the humid tropics is the
    /// water-vapour continuum: broad absorption between the lines, conventionally split into a
    /// self term going as the vapour pressure squared and a foreign term going as vapour
    /// pressure times air pressure,
    ///
    ///     k ~ e (C_s e + C_f p).
    ///
    /// Both scalings are reproduced here, with <see cref="ContinuumForeignFraction"/> setting
    /// their balance at the reference state, but the strength is a single tunable number rather
    /// than fitted MT_CKD coefficients - the rest of the model is grey, so pretending to
    /// spectral accuracy here would be false precision.
    ///
    /// The quadratic self term is the important behaviour: warm the column and
    /// Clausius-Clapeyron raises the vapour, and the continuum grows as roughly its square.
    /// The window therefore shuts as the climate warms, which a fixed transparent window can
    /// never do.
    /// </remarks>
    public double WindowContinuumOpticalDepth { get; set; } = 0.0;

    /// <summary>
    /// Share of the window continuum coming from the foreign (pressure-broadened) term at the
    /// reference state; the rest comes from the self term. 0.5 by default.
    /// </summary>
    /// <remarks>
    /// The two terms respond differently to warming. The foreign term is linear in vapour, the
    /// self term quadratic, so this sets how sharply the window closes as the column warms.
    /// </remarks>
    public double ContinuumForeignFraction { get; set; } = 0.5;

    /// <summary>True when a continuum has been configured inside the window.</summary>
    public bool HasWindowContinuum =>
        HasWindow && WindowContinuumOpticalDepth > 0.0 && WaterVapourOpticalDepth > 0.0;

    /// <summary>
    /// How absorption is distributed within the absorbing band.
    /// <see cref="KDistributionShape.Grey"/> (the default) is one coefficient for the whole
    /// band, which is what the rest of the model assumes.
    /// </summary>
    /// <remarks>
    /// Absorption inside a real band spans orders of magnitude, and a single coefficient
    /// systematically overestimates how opaque the band is: transmission is dominated by the
    /// weak wings, not the mean. A correlated-k quadrature fixes that while preserving the
    /// band-mean absorber amount, so the column holds exactly as much gas as before.
    ///
    /// It applies to the absorbing band only. The window's continuum stays grey deliberately -
    /// smoothness between the lines is what makes it a continuum, so giving it line structure
    /// would be wrong.
    /// </remarks>
    public KDistributionShape KDistributionShape { get; set; } = KDistributionShape.Grey;

    /// <summary>
    /// Spread of absorption coefficients within the band. For
    /// <see cref="KDistributionShape.Lognormal"/> this is the log standard deviation, and 0
    /// collapses exactly onto a grey band. 1.5 is a moderately non-grey band.
    /// </summary>
    public double KDistributionWidth { get; set; } = 0.0;

    /// <summary>
    /// Number of quadrature points (g-points) across the band. Each one costs another pass of
    /// the flux recurrence, so this is the accuracy/speed dial; 16 is ample for the shapes
    /// here.
    /// </summary>
    public int KDistributionPoints { get; set; } = 16;

    /// <summary>Builds the quadrature these options describe.</summary>
    public KDistribution BuildKDistribution() =>
        KDistribution.Build(KDistributionShape, KDistributionWidth, KDistributionPoints);

    /// <summary>True when a window of non-zero width has been configured.</summary>
    public bool HasWindow => WindowLongWavelength > WindowShortWavelength;

    /// <summary>
    /// Share of a blackbody's emission at <paramref name="temperature"/> (K) that falls inside
    /// the window, and so escapes without interacting with the absorber. Zero when no window
    /// is configured.
    /// </summary>
    public double WindowShare(double temperature) =>
        HasWindow
            ? Planck.FractionBetween(WindowShortWavelength, WindowLongWavelength, temperature)
            : 0.0;

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
        if (WindowShortWavelength < 0)
            throw new ArgumentException("WindowShortWavelength must be >= 0.");
        if (WindowLongWavelength < 0)
            throw new ArgumentException("WindowLongWavelength must be >= 0.");
        if (WindowLongWavelength > 0 && WindowLongWavelength <= WindowShortWavelength)
            throw new ArgumentException(
                "WindowLongWavelength must be greater than WindowShortWavelength.");
        if (WindowContinuumOpticalDepth < 0)
            throw new ArgumentException("WindowContinuumOpticalDepth must be >= 0.");
        if (KDistributionWidth < 0)
            throw new ArgumentException("KDistributionWidth must be >= 0.");
        if (KDistributionPoints is < 1 or > 256)
            throw new ArgumentException(
                "KDistributionPoints must be in [1, 256]; the Gauss-Hermite nodes lose accuracy " +
                "beyond that, and 16 points already give a transmission error near 1e-4.");
        if (ContinuumForeignFraction is < 0 or > 1)
            throw new ArgumentException("ContinuumForeignFraction must be in [0, 1].");
        if (WindowContinuumOpticalDepth > 0 && WaterVapourOpticalDepth <= 0)
            throw new ArgumentException(
                "WindowContinuumOpticalDepth needs WaterVapourOpticalDepth to be non-zero: " +
                "the continuum is a water-vapour continuum, so there has to be vapour to drive it.");
        if (WindowContinuumOpticalDepth > 0 && !HasWindow)
            throw new ArgumentException(
                "WindowContinuumOpticalDepth needs a window to sit inside; set " +
                "WindowShortWavelength and WindowLongWavelength.");
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
        // The window share is evaluated at the starting surface temperature; a window wide
        // enough to swallow the whole Planck function leaves the air unable to radiate at all.
        double absorber = EffectiveDryOpticalDepth + WaterVapourOpticalDepth;
        if ((absorber <= 0.0 || WindowShare(InitialSurfaceTemperature) >= 0.999) &&
            AtmosphericShortwaveFraction > 0.0)
        {
            throw new ArgumentException(
                "A longwave-transparent atmosphere (no absorber, or a window covering the " +
                "whole spectrum) cannot radiate away the solar flux it absorbs, so no " +
                "equilibrium exists. Set AtmosphericShortwaveFraction to 0 in that case.");
        }
    }
}
