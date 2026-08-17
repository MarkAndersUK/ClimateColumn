using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The closed-form emission laws, checked against values computed independently of the
/// model code.
/// </summary>
[TestClass]
public class ConstantsTests
{
    [TestMethod]
    public void StefanBoltzmannMatchesTheLawEvaluatedIndependently()
    {
        double expected = 5.670374419e-8 * Math.Pow(288.15, 4);
        Assert.AreEqual(expected, RadiationSolver.StefanBoltzmannFlux(288.15), 1e-9,
            "sigma T^4 at 288.15 K");
    }

    [DataTestMethod]
    [DataRow(288.0, 390.1)]
    [DataRow(255.0, 239.8)]
    public void StefanBoltzmannMatchesPublishedValues(double temperature, double expected)
    {
        Assert.AreEqual(expected, RadiationSolver.StefanBoltzmannFlux(temperature), 0.05,
            $"sigma T^4 at {temperature} K");
    }

    [TestMethod]
    public void EmissivityScalesTheFluxLinearly()
    {
        Assert.AreEqual(0.5 * RadiationSolver.StefanBoltzmannFlux(300.0),
            RadiationSolver.StefanBoltzmannFlux(300.0, 0.5), 1e-12,
            "emissivity is a linear prefactor");
    }

    [TestMethod]
    public void EmissionTemperatureFollowsFromTheSolarConstantAndAlbedo()
    {
        var options = new ModelOptions();

        // (1361/4) * 0.70 = 238.175 W/m2, and (238.175/sigma)^(1/4) = 254.58 K.
        Assert.AreEqual(238.175, options.AbsorbedSolarFlux, 1e-3,
            "absorbed solar for S = 1361, a = 0.30");
        Assert.AreEqual(254.58, options.EmissionTemperature, 0.01,
            "emission temperature for S = 1361, a = 0.30");
    }

    [TestMethod]
    public void KoenigsbergerVolumetricEmissionMatchesTheLaw()
    {
        const double eps = 1.5e-4;   // m^-1
        const double t = 260.0;
        double expected = 4.0 * eps * 5.670374419e-8 * Math.Pow(t, 4);

        Assert.AreEqual(expected, RadiationSolver.KoenigsbergerVolumetricEmission(t, eps), 1e-15,
            "dq/dV = 4 eps' sigma T^4");

        var segment = new Segment
        {
            BottomAltitude = 0, TopAltitude = 1000, EmissionCoefficient = eps, Temperature = t
        };
        Assert.AreEqual(expected * 1000.0, segment.KoenigsbergerEmission, 1e-12,
            "segment emission = 4 eps' sigma T^4 dz");
    }

    [TestMethod]
    public void KoenigsbergerEmissionScalesAsTheFourthPower()
    {
        const double eps = 1.5e-4;
        const double t = 260.0;

        Assert.AreEqual(16.0,
            RadiationSolver.KoenigsbergerVolumetricEmission(2 * t, eps) /
            RadiationSolver.KoenigsbergerVolumetricEmission(t, eps), 1e-12,
            "doubling T must multiply emission by 16");
    }

    [TestMethod]
    public void KoenigsbergerDiffusivityIsExactlyTwo()
    {
        Assert.AreEqual(2.0, PhysicalConstants.KoenigsbergerDiffusivity, 1e-15,
            "the Koenigsberger closure fixes D = 2");
    }

    [TestMethod]
    public void HemisphericAbsorptivityApproachesTwoTauInTheThinLimit()
    {
        // The true hemispheric absorptivity is 1 - 2*E3(tau), which expands as 2*tau as
        // tau -> 0. That is what makes D = 2 the exact optically thin value rather than a
        // convenient choice, so it is checked against a numerically integrated E3.
        const double tau = 1e-6;
        double exact = 1.0 - 2.0 * TestSupport.ExponentialIntegral3(tau);

        Assert.AreEqual(1.0, exact / (2.0 * tau), 2e-5,
            "1 - 2 E3(tau) -> 2 tau in the thin limit");
    }
}
