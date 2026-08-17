using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The surface latent heat flux: evaporation as an energy term rather than only as a source
/// of absorber.
/// </summary>
/// <remarks>
/// This is the largest non-radiative term in a real surface energy budget - roughly 80 W m^-2
/// against 20 for sensible heat - and the model did without it until the sensible flux was
/// asked to carry both. It is off by default for exactly that reason: h_c was calibrated
/// against a budget with no evaporation in it, so switching this on is a different model
/// rather than a repair to this one. The first test here is the guard on that promise.
/// </remarks>
[TestClass]
public class LatentHeatTests
{
    private static ModelOptions Moist(double beta) =>
        new() { SurfaceMoistureAvailability = beta };

    /// <summary>
    /// The default is untouched: with no moisture available the term vanishes identically,
    /// not merely to within a tolerance, and every documented result stands.
    /// </summary>
    [TestMethod]
    public void TheDefaultConfigurationHasNoLatentFluxAtAll()
    {
        var result = TestSupport.Default;

        Assert.AreEqual(0.0, result.Column.Options.SurfaceMoistureAvailability, 0.0,
            "evaporation must stay off by default; h_c is calibrated without it");
        Assert.AreEqual(0.0, result.LatentHeatFlux, 0.0,
            "a zero moisture availability must give exactly zero, not a small residue");

        var explicitlyDry = TestSupport.Equilibrium("beta-0", () => Moist(0.0));

        Assert.AreEqual(result.SurfaceTemperature, explicitlyDry.SurfaceTemperature, 1e-9,
            "setting the moisture availability to zero must reproduce the default exactly");
    }

    /// <summary>
    /// Saturation vapour pressure against the measured values, which is what decides whether
    /// the evaporation rate is right at all.
    /// </summary>
    /// <remarks>
    /// Integrating Clausius-Clapeyron with a constant latent heat is an approximation - L
    /// falls by about 3% between 0 and 30 C - so this runs a few percent high at the warm end.
    /// The tolerance admits that rather than hiding it: what matters is that the curve has the
    /// right value and the right slope where the model actually sits.
    /// </remarks>
    [DataTestMethod]
    [DataRow(273.16, 611.7)]
    [DataRow(283.15, 1228.0)]
    [DataRow(288.15, 1705.6)]
    [DataRow(293.15, 2339.3)]
    [DataRow(303.15, 4247.0)]
    public void SaturationVapourPressureMatchesTheMeasuredCurve(
        double temperature, double expected)
    {
        double actual = ConvectionSolver.SaturationVapourPressure(temperature);
        double error = Math.Abs(actual - expected) / expected;

        Assert.IsTrue(error < 0.05,
            $"e_sat({temperature} K) = {actual:F1} Pa against a measured {expected:F1} Pa, " +
            $"off by {100 * error:F1}% - more than a constant latent heat can account for");
    }

    /// <summary>
    /// Specific humidity is the vapour pressure converted with R_d/R_v, so a saturated surface
    /// at 288 K should hold about 10.6 g of water per kg of air.
    /// </summary>
    [TestMethod]
    public void SaturationSpecificHumidityIsThePressureScaledByTheGasConstantRatio()
    {
        double q = ConvectionSolver.SaturationSpecificHumidity(
            288.15, StandardAtmosphere.SeaLevelPressure);

        Assert.IsTrue(q is > 0.009 and < 0.012,
            $"q_sat(288 K) = {1000 * q:F2} g/kg, outside the range a saturated surface occupies");
    }

    /// <summary>
    /// Moisture availability scales the whole flux, so evaporation rises with it and never
    /// changes sign on the way. Getting this wrong - scaling the surface humidity instead -
    /// puts a dry surface below the overlying air and drives permanent dew deposition.
    /// </summary>
    [TestMethod]
    public void EvaporationRisesMonotonicallyWithMoistureAvailability()
    {
        double[] betas = { 0.0, 0.2, 0.4, 0.6, 0.8, 1.0 };
        var fluxes = betas
            .Select(b => TestSupport.Equilibrium($"beta-{b}", () => Moist(b)).LatentHeatFlux)
            .ToArray();

        Assert.AreEqual(0.0, fluxes[0], 0.0, "no moisture available means no evaporation");

        for (int i = 1; i < betas.Length; i++)
        {
            Assert.IsTrue(fluxes[i] > fluxes[i - 1],
                $"latent flux fell from {fluxes[i - 1]:F2} to {fluxes[i]:F2} W/m2 as the " +
                $"moisture availability rose from {betas[i - 1]} to {betas[i]}");
            Assert.IsTrue(fluxes[i] > 0.0,
                $"beta = {betas[i]} gave a downward latent flux of {fluxes[i]:F2} W/m2; a " +
                "surface warmer than the air cannot be depositing dew");
        }
    }

    /// <summary>
    /// The point of the term: evaporation is a loss from the surface, so the surface settles
    /// cooler and the convecting layer deepens to carry the extra flux.
    /// </summary>
    [TestMethod]
    public void EvaporationCoolsTheSurface()
    {
        var dry = TestSupport.Default;
        var wet = TestSupport.Equilibrium("beta-1", () => Moist(1.0));

        Assert.IsTrue(wet.SurfaceTemperature < dry.SurfaceTemperature,
            $"open water gave {wet.SurfaceTemperature:F3} K against a dry " +
            $"{dry.SurfaceTemperature:F3} K; an energy loss cannot warm the surface");

        Assert.IsTrue(wet.ConvectiveTopAltitude >= dry.ConvectiveTopAltitude,
            $"convecting layer fell from {dry.ConvectiveTopAltitude:F0} m to " +
            $"{wet.ConvectiveTopAltitude:F0} m despite carrying more heat");
    }

    /// <summary>
    /// The surface budget still closes with the new term in it. This is the test that would
    /// catch latent heat being taken from the surface and never delivered to the air.
    /// </summary>
    [TestMethod]
    public void TheSurfaceEnergyBudgetClosesWithEvaporationIncluded()
    {
        var result = TestSupport.Equilibrium("beta-1", () => Moist(1.0));
        double eps = result.Column.Options.SurfaceEmissivity;

        double net = result.Column.SurfaceShortwaveAbsorbed
                   + eps * result.Radiation.SurfaceDownwardFlux
                   - result.SurfaceEmission
                   - result.SensibleHeatFlux
                   - result.LatentHeatFlux;

        Assert.IsTrue(Math.Abs(net) < result.Column.Options.FluxTolerance,
            $"surface budget leaves {net:E3} W/m2 unaccounted for once evaporation is included");

        Assert.IsTrue(Math.Abs(result.TopOfAtmosphereImbalance) < result.Column.Options.FluxTolerance,
            $"top-of-atmosphere imbalance {result.TopOfAtmosphereImbalance:E3} W/m2: latent " +
            "heat taken from the surface must reappear in the atmosphere, not vanish");
    }

    /// <summary>
    /// The analytic sensitivity used by the integrator's stability limit agrees with a finite
    /// difference of the flux it claims to differentiate.
    /// </summary>
    /// <remarks>
    /// Worth checking directly rather than through its effect. Near 288 K with open water this
    /// derivative is larger than h_c and larger than the Planck term, so an error in it would
    /// not produce a wrong answer - it would produce an oscillation that never converges, and
    /// diagnosing that from the outside is far harder than testing it here.
    /// </remarks>
    [TestMethod]
    public void TheLatentSensitivityMatchesAFiniteDifferenceOfTheFlux()
    {
        var column = Column.Build(Moist(1.0));
        column.SurfaceTemperature = 288.15;

        double analytic = ConvectionSolver.LatentHeatFluxSensitivity(column);

        const double h = 1e-4;
        column.SurfaceTemperature = 288.15 + h;
        double up = ConvectionSolver.LatentHeatFlux(column);
        column.SurfaceTemperature = 288.15 - h;
        double down = ConvectionSolver.LatentHeatFlux(column);

        double numeric = (up - down) / (2 * h);

        Assert.AreEqual(numeric, analytic, Math.Abs(numeric) * 1e-6,
            $"analytic dLE/dT = {analytic:F4} against a finite difference {numeric:F4} W/m2/K");

        // The magnitude is the reason it cannot be left out: it exceeds h_c = 18.1 W/m2/K.
        Assert.IsTrue(analytic > 25.0,
            $"dLE/dT = {analytic:F2} W/m2/K is smaller than the C-C relation permits at 288 K");
    }

    /// <summary>
    /// Evaporation follows Clausius-Clapeyron, at about 6.5 %/K near 288 K - the same rate
    /// that scales the water-vapour absorber, because both come off the same curve.
    /// </summary>
    [TestMethod]
    public void EvaporationFollowsClausiusClapeyron()
    {
        double q = ConvectionSolver.SaturationSpecificHumidity(
            288.15, StandardAtmosphere.SeaLevelPressure);
        double slope = ConvectionSolver.SaturationSpecificHumiditySlope(
            288.15, StandardAtmosphere.SeaLevelPressure);

        Assert.AreEqual(0.065, slope / q, 0.005,
            $"saturation humidity grows at {100 * slope / q:F2} %/K, not the ~6.5 %/K that " +
            "Clausius-Clapeyron gives near 288 K");

        // The same curve drives the water-vapour absorber, so the two must agree. They differ
        // only by the p / (p - (1-epsilon) e) factor that converting to specific humidity adds.
        double absorberRate = PhysicalConstants.ClausiusClapeyronScale / (288.15 * 288.15);

        Assert.AreEqual(absorberRate, slope / q, absorberRate * 0.01,
            $"evaporation grows at {100 * slope / q:F3} %/K while the absorber grows at " +
            $"{100 * absorberRate:F3} %/K; both come off Clausius-Clapeyron and must not drift");
    }

    /// <summary>
    /// Without convection there is no turbulent transport to carry vapour away, so the latent
    /// flux is zero for the same reason the sensible flux is.
    /// </summary>
    [TestMethod]
    public void NoConvectionMeansNoEvaporation()
    {
        var options = Moist(1.0);
        options.Convection = ConvectionMode.None;
        options.AtmosphericShortwaveFraction = 0.0;

        var column = Column.Build(options);

        Assert.AreEqual(0.0, ConvectionSolver.LatentHeatFlux(column), 0.0,
            "with no convective transport there is nothing to carry vapour off the surface");
        Assert.AreEqual(0.0, ConvectionSolver.LatentHeatFluxSensitivity(column), 0.0,
            "a flux that is identically zero cannot have a non-zero derivative");
    }

    /// <summary>
    /// Where the model does and does not resemble Earth once evaporation is on.
    /// </summary>
    /// <remarks>
    /// The partition it can match: near beta = 0.35 the Bowen ratio lands on Earth's global
    /// mean of about 0.25. The magnitude it cannot: the total turbulent flux there is roughly
    /// 41 W m^-2 against Earth's 100. That ceiling is h_c, which comes from a building-physics
    /// film-coefficient relation rather than a global-mean bulk transfer coefficient, so no
    /// choice of beta reaches the observed magnitude. Recorded as a test so the limitation
    /// stays visible rather than resurfacing as a surprise.
    /// </remarks>
    [TestMethod]
    public void TheModelMatchesEarthsPartitionButNotItsMagnitude()
    {
        var result = TestSupport.Equilibrium("beta-earthlike", () => Moist(0.35));

        Assert.AreEqual(0.25, result.BowenRatio, 0.06,
            $"Bowen ratio {result.BowenRatio:F2} against Earth's ~0.25");

        double turbulent = result.LatentHeatFlux + result.SensibleHeatFlux;
        Assert.IsTrue(turbulent < 60.0,
            $"total turbulent flux {turbulent:F1} W/m2 exceeds what h_c can carry; if this " +
            "has risen towards Earth's ~100 W/m2, the transfer coefficient changed");
    }

    /// <summary>Moisture availability outside 0 to 1 is rejected rather than extrapolated.</summary>
    [DataTestMethod]
    [DataRow(-0.1)]
    [DataRow(1.5)]
    public void ImpossibleMoistureAvailabilityIsRejected(double beta)
    {
        Assert.ThrowsException<ArgumentException>(() => Moist(beta).ValidateForIntegration());
    }
}
