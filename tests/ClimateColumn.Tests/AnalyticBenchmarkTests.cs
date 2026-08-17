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
    /// One slab opaque in the grey band over a black surface, under a transparent window.
    /// The slab neither emits nor absorbs inside the window, so it absorbs the band share of
    /// the surface emission and re-emits it both ways:
    ///
    ///   (1 - f(Ts)) sigma Ts^4 = 2 (1 - f(Ta)) sigma Ta^4
    ///
    /// and at the top the escaping flux is the slab's band emission plus the surface's window
    /// emission, F0 = (1 - f(Ta)) sigma Ta^4 + f(Ts) sigma Ts^4. Eliminating the slab gives
    ///
    ///   Ts = (2 / (1 + f(Ts)))^(1/4) Te
    ///
    /// which is the same closed form as for a flat window, except that f is now evaluated at
    /// the surface's own temperature - so it is implicit in Ts and has to be solved for. That
    /// makes it a stronger benchmark than the flat-window version, where f was simply whatever
    /// the caller passed in.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.0, 0.0, "no window")]
    [DataRow(8.0, 13.0, "Earth's water-vapour window")]
    [DataRow(8.0, 20.0, "a deliberately wide window")]
    public void WindowedOpaqueSlabMatchesTheAnalyticSolution(
        double fromMicrons, double toMicrons, string description)
    {
        var options = new ModelOptions
        {
            SegmentCount = 1,
            TopAltitude = 50_000,
            AtmosphericShortwaveFraction = 0.0,
            SurfaceEmissivity = 1.0,
            Convection = ConvectionMode.None,
            WindowShortWavelength = fromMicrons * 1e-6,
            WindowLongWavelength = toMicrons * 1e-6,
            FluxTolerance = 1e-5,
            TemperatureTolerance = 1e-8
        };
        var column = Column.Build(options);
        foreach (var s in column.Segments) s.EmissionCoefficient = 1.0;   // dtau >> 1

        var result = new ColumnModel(column).Run();
        Assert.IsTrue(result.Converged, $"windowed slab ({description}) must converge");

        // Ts = (2 / (1 + f(Ts)))^(1/4) Te, solved by fixed-point iteration from the
        // no-window answer. The map is a mild contraction here, so this settles quickly.
        double te = options.EmissionTemperature;
        double analytic = Math.Pow(2.0, 0.25) * te;
        for (int i = 0; i < 200; i++)
        {
            double next = Math.Pow(2.0 / (1.0 + options.WindowShare(analytic)), 0.25) * te;
            if (Math.Abs(next - analytic) < 1e-10) { analytic = next; break; }
            analytic = next;
        }

        Assert.AreEqual(analytic, result.SurfaceTemperature, 0.05,
            $"{description}: Ts = (2/(1+f(Ts)))^(1/4) Te  (f(Ts) = {options.WindowShare(analytic):F4})");

        // The slab's own budget, checked directly against the model's temperatures rather
        // than against a closed form for Ta, which is implicit too.
        double slabTemperature = column.Segments[0].Temperature;
        double fromSurface = (1.0 - options.WindowShare(result.SurfaceTemperature)) *
                             RadiationSolver.StefanBoltzmannFlux(result.SurfaceTemperature);
        double slabEmission = 2.0 * (1.0 - options.WindowShare(slabTemperature)) *
                              RadiationSolver.StefanBoltzmannFlux(slabTemperature);

        Assert.AreEqual(fromSurface, slabEmission, 0.2,
            $"{description}: the slab must re-emit exactly the band flux it absorbs");
    }

    /// <summary>
    /// The window share has to come from the emitter's temperature, not from the column as a
    /// whole. Checked against values computed independently of the model.
    /// </summary>
    [DataTestMethod]
    [DataRow(8.0, 13.0, 286.8, 0.3105)]
    [DataRow(8.0, 13.0, 216.7, 0.1998)]
    [DataRow(8.0, 12.0, 286.8, 0.2517)]
    [DataRow(8.0, 12.0, 216.7, 0.1513)]
    public void WindowShareFollowsThePlanckFunction(
        double fromMicrons, double toMicrons, double temperature, double expected)
    {
        double share = Planck.FractionBetween(fromMicrons * 1e-6, toMicrons * 1e-6, temperature);

        Assert.AreEqual(expected, share, 0.002,
            $"share of {fromMicrons}-{toMicrons} um at {temperature} K");
    }

    [TestMethod]
    public void WholeSpectrumIntegratesToOne()
    {
        // A window spanning everything must capture the entire Planck function, and the
        // fraction below any cut must be monotonic in that cut.
        Assert.AreEqual(1.0, Planck.FractionBetween(1e-9, 1e-2, 288.0), 1e-6,
            "0.001 to 10000 um should capture all of it");

        double previous = 0.0;
        for (double micron = 1.0; micron <= 60.0; micron += 1.0)
        {
            double below = Planck.FractionBelow(micron * 1e-6 * 288.0);
            Assert.IsTrue(below >= previous - 1e-12,
                $"the fraction below {micron} um must not decrease");
            previous = below;
        }

        // 97.9 % of a 288 K Planck function lies below 60 um; the far-infrared tail is long.
        Assert.AreEqual(0.9786, previous, 0.002, $"fraction below 60 um at 288 K");
        Assert.IsTrue(Planck.FractionBelow(200e-6 * 288.0) > 0.999,
            "by 200 um essentially all of it is below the cut");
    }
}
