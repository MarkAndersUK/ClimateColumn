using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Equilibria the model must reproduce that have closed-form answers derived independently
/// of the code. These are the assertions that would catch a physics error rather than a
/// coding one.
/// </summary>
[TestClass]
public class AnalyticBenchmarkTests
{
    private static ModelOptions TransparentOptions() => new()
    {
        TotalOpticalDepth = 0.0,
        AtmosphericShortwaveFraction = 0.0,
        SurfaceEmissivity = 1.0,
        Convection = ConvectionMode.None,
        SegmentCount = 10
    };

    [TestMethod]
    public void TransparentAtmosphereLeavesTheSurfaceAtTheEmissionTemperature()
    {
        var options = TransparentOptions();
        var result = TestSupport.Equilibrium("transparent", TransparentOptions);

        Assert.IsTrue(result.Converged, "transparent case must converge");
        Assert.AreEqual(options.EmissionTemperature, result.SurfaceTemperature, 1e-3,
            "with no absorber the surface must sit at T_e");
        Assert.AreEqual(options.AbsorbedSolarFlux, result.Radiation.OutgoingLongwave, 1e-4,
            "OLR must balance absorbed solar");
        Assert.AreEqual(0.0, result.GreenhouseWarming, 1e-3, "greenhouse warming must vanish");
    }

    /// <summary>
    /// Exact grey radiative equilibrium. With no solar absorbed in the air the net flux is
    /// constant, F_up - F_down = F0, and the two-stream equations give F_up + F_down =
    /// F0 (1 + tau). At the ground F_up = sigma Ts^4, hence sigma Ts^4 = F0 (1 + tau/2).
    /// </summary>
    [DataTestMethod]
    [DataRow(0.5)]
    [DataRow(1.8)]
    [DataRow(4.0)]
    public void GreyRadiativeEquilibriumMatchesTheAnalyticSurfaceTemperature(double tau)
    {
        var options = new ModelOptions
        {
            SegmentCount = 160,
            TotalOpticalDepth = tau,
            AtmosphericShortwaveFraction = 0.0,
            SurfaceEmissivity = 1.0,
            Convection = ConvectionMode.None
        };
        var result = ColumnModel.RunToEquilibrium(options);

        double analytic = Math.Pow(
            options.AbsorbedSolarFlux * (1.0 + 0.5 * tau) / PhysicalConstants.StefanBoltzmann,
            0.25);

        Assert.IsTrue(result.Converged, $"grey radiative equilibrium at tau = {tau} must converge");

        // The residual is the second-order error from holding T constant across a segment;
        // it grows with tau because the gradient across a segment steepens.
        Assert.AreEqual(analytic, result.SurfaceTemperature, 0.05,
            $"tau = {tau}: Ts = [F0 (1 + tau/2) / sigma]^(1/4)");
    }

    /// <summary>
    /// Classic result for N perfectly absorbing grey slabs over a black surface:
    /// T_surface = (N+1)^(1/4) T_e, and the k-th layer counted from the top is k^(1/4) T_e.
    /// </summary>
    [DataTestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    public void OpaqueSlabStackMatchesTheAnalyticSolution(int layers)
    {
        var options = new ModelOptions
        {
            SegmentCount = layers,
            TopAltitude = 50_000,
            AtmosphericShortwaveFraction = 0.0,
            SurfaceEmissivity = 1.0,
            Convection = ConvectionMode.None,
            FluxTolerance = 1e-5,
            TemperatureTolerance = 1e-8
        };

        var column = Column.Build(options);
        foreach (var s in column.Segments) s.EmissionCoefficient = 1.0;   // dtau >> 1

        var result = new ColumnModel(column).Run();
        double te = options.EmissionTemperature;

        Assert.AreEqual(Math.Pow(layers + 1, 0.25) * te, result.SurfaceTemperature, 0.05,
            $"{layers}-slab: T_surface = {layers + 1}^(1/4) T_e");

        for (int i = 0; i < layers; i++)
        {
            int k = layers - i;                       // k-th slab counted from the top
            Assert.AreEqual(Math.Pow(k, 0.25) * te, column.Segments[i].Temperature, 0.05,
                $"{layers}-slab: layer {k} from the top must sit at {k}^(1/4) T_e");
        }

        Assert.AreEqual(options.AbsorbedSolarFlux, result.Radiation.OutgoingLongwave, 1e-3,
            $"{layers}-slab: OLR must balance absorbed solar");
    }

    /// <summary>
    /// One slab opaque in the grey band over a black surface, under a window covering
    /// fraction f of the spectrum. The slab budget gives sigma Ta^4 = sigma Ts^4 / 2
    /// regardless of f, and the TOA balance F0 = (1-f) sigma Ta^4 + f sigma Ts^4 then yields
    /// Ts = (2 / (1 + f))^(1/4) Te - the classic 2^(1/4) Te at f = 0, collapsing to Te at
    /// f = 1.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.0)]
    [DataRow(0.2)]
    [DataRow(0.5)]
    public void WindowedOpaqueSlabMatchesTheAnalyticSolution(double window)
    {
        var options = new ModelOptions
        {
            SegmentCount = 1,
            TopAltitude = 50_000,
            AtmosphericShortwaveFraction = 0.0,
            SurfaceEmissivity = 1.0,
            Convection = ConvectionMode.None,
            WindowFraction = window,
            FluxTolerance = 1e-5,
            TemperatureTolerance = 1e-8
        };
        var column = Column.Build(options);
        foreach (var s in column.Segments) s.EmissionCoefficient = 1.0;   // dtau >> 1

        var result = new ColumnModel(column).Run();

        Assert.IsTrue(result.Converged, $"windowed slab (f = {window}) must converge");
        Assert.AreEqual(Math.Pow(2.0 / (1.0 + window), 0.25) * options.EmissionTemperature,
            result.SurfaceTemperature, 0.05,
            $"windowed slab: Ts = (2/(1+{window}))^(1/4) Te");
        Assert.AreEqual(Math.Pow(0.5, 0.25) * result.SurfaceTemperature,
            column.Segments[0].Temperature, 0.05,
            $"windowed slab (f = {window}): the slab sits at (1/2)^(1/4) Ts regardless of f");
    }
}
