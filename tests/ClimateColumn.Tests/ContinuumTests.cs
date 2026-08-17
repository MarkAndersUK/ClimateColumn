using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The water-vapour continuum inside the spectral window - the absorption between the lines
/// that actually closes the window over the humid tropics.
/// </summary>
/// <remarks>
/// The physically important behaviour is the scaling, not the absolute strength, which is a
/// single tunable number here. The self term goes as the vapour squared and the foreign term
/// as vapour times pressure, so warming the column raises the vapour by Clausius-Clapeyron and
/// the continuum grows faster than linearly. That is what makes the window shut as the climate
/// warms; a fixed transparent window never can.
/// </remarks>
[TestClass]
public class ContinuumTests
{
    private static ModelOptions WindowedOptions(
        double continuum = 0.0, double foreign = 0.5, double vapour = 1.2) => new()
    {
        SegmentCount = 40,
        TotalOpticalDepth = 1.8,
        WaterVapourOpticalDepth = vapour,
        WindowShortWavelength = 8e-6,
        WindowLongWavelength = 13e-6,
        WindowContinuumOpticalDepth = continuum,
        ContinuumForeignFraction = foreign
    };

    /// <summary>
    /// The regression that matters most: with no continuum the two-band solver has to give
    /// bit-identical results to a transparent window, because the window band's tau is zero and
    /// a zero-tau band neither absorbs nor emits.
    /// </summary>
    [TestMethod]
    public void ZeroContinuumLeavesTheWindowExactlyTransparent()
    {
        var column = Column.Build(WindowedOptions(continuum: 0.0));
        var rad = RadiationSolver.Solve(column);

        Assert.AreEqual(0.0, column.TotalWindowOpticalDepth(), 0.0,
            "no continuum means no window optical depth at all");

        foreach (double tau in rad.WindowOpticalThickness)
        {
            Assert.AreEqual(0.0, tau, 0.0, "every segment's window thickness must be exactly zero");
        }

        // The surface's whole window emission must reach space untouched.
        var options = column.Options;
        double windowFlux = options.WindowShare(column.SurfaceTemperature) *
                            options.SurfaceEmissivity *
                            RadiationSolver.StefanBoltzmannFlux(column.SurfaceTemperature);

        Assert.IsTrue(rad.OutgoingLongwave >= windowFlux - 1e-9,
            $"OLR {rad.OutgoingLongwave:F3} must carry the full window flux {windowFlux:F3}");
    }

    [TestMethod]
    public void ContinuumIsNormalisedToTheRequestedColumnDepth()
    {
        // At the reference temperature the Clausius-Clapeyron factor is 1, so the column
        // continuum optical depth should come out at exactly what was asked for. The profile
        // is set isothermally at the reference so the vapour scaling is unambiguous.
        foreach (double target in new[] { 0.2, 0.8, 2.0 })
        {
            var options = WindowedOptions(continuum: target);
            var column = Column.Build(options);

            foreach (var s in column.Segments)
                s.Temperature = options.WaterVapourReferenceTemperature;
            column.DistributeOpticalDepth();

            Assert.AreEqual(target, column.TotalWindowOpticalDepth(), 1e-9,
                $"continuum should normalise to {target} at the reference temperature");
        }
    }

    /// <summary>
    /// Doubling the vapour must multiply the continuum by f s + (1 - f) s^2 with s = 2: the
    /// foreign term is linear in vapour and the self term quadratic, and a mixture is their
    /// weighted <em>sum</em>, not a power law at some intermediate exponent.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.0, "pure self term, quadratic in vapour")]
    [DataRow(1.0, "pure foreign term, linear in vapour")]
    [DataRow(0.5, "half and half")]
    [DataRow(0.25, "mostly self")]
    public void ContinuumScalesWithVapourAsTheWeightedSum(
        double foreignFraction, string description)
    {
        // Double the Clausius-Clapeyron factor by construction: build at the reference
        // temperature, then find the temperature whose vapour loading is twice as large.
        var options = WindowedOptions(continuum: 1.0, foreign: foreignFraction);

        var cold = Column.Build(options);
        foreach (var s in cold.Segments) s.Temperature = options.WaterVapourReferenceTemperature;
        cold.DistributeOpticalDepth();
        double before = cold.TotalWindowOpticalDepth();

        // Solve exp(L/Rv (1/Tref - 1/T)) = 2 for T.
        double tRef = options.WaterVapourReferenceTemperature;
        double warmed = 1.0 / (1.0 / tRef - Math.Log(2.0) / PhysicalConstants.ClausiusClapeyronScale);

        var warm = Column.Build(options);
        foreach (var s in warm.Segments) s.Temperature = warmed;
        warm.DistributeOpticalDepth();
        double after = warm.TotalWindowOpticalDepth();

        Assert.AreEqual(2.0, warm.CurrentWaterVapourOpticalDepth() / options.WaterVapourOpticalDepth,
            1e-6, "the setup should have doubled the vapour loading");

        const double vapourFactor = 2.0;
        double expected = foreignFraction * vapourFactor +
                          (1.0 - foreignFraction) * vapourFactor * vapourFactor;

        Assert.AreEqual(expected, after / before, 1e-6,
            $"{description}: doubling the vapour should raise the continuum by " +
            $"{foreignFraction:F2}*2 + {1 - foreignFraction:F2}*4 = {expected:F3}");
    }

    /// <summary>
    /// The point of the whole exercise: the window closes as the column warms, so it stops
    /// being a permanent escape hatch.
    /// </summary>
    [TestMethod]
    public void WarmingClosesTheWindow()
    {
        var options = WindowedOptions(continuum: 0.5);
        var column = Column.Build(options);

        double before = column.TotalWindowOpticalDepth();

        foreach (var s in column.Segments) s.Temperature += 10.0;
        column.DistributeOpticalDepth();
        double after = column.TotalWindowOpticalDepth();

        Assert.IsTrue(after > before * 1.5,
            $"10 K of warming should close the window substantially ({before:F4} -> {after:F4})");
    }

    [TestMethod]
    public void ContinuumWarmsTheSurfaceAndCutsTheOutgoingLongwave()
    {
        var transparent = Column.Build(WindowedOptions(continuum: 0.0));
        var closing = Column.Build(WindowedOptions(continuum: 1.0));

        double openOlr = RadiationSolver.Solve(transparent).OutgoingLongwave;
        double closedOlr = RadiationSolver.Solve(closing).OutgoingLongwave;

        Assert.IsTrue(closedOlr < openOlr,
            $"closing the window must cut the outgoing longwave ({closedOlr:F2} vs {openOlr:F2} W/m2)");

        var open = TestSupport.Equilibrium("window-open",
            () => WindowedOptions(continuum: 0.0));
        var shut = TestSupport.Equilibrium("window-closing",
            () => WindowedOptions(continuum: 1.0));

        Assert.IsTrue(shut.Converged && open.Converged, "both configurations must reach equilibrium");
        Assert.IsTrue(shut.SurfaceTemperature > open.SurfaceTemperature,
            $"a closing window must warm the surface " +
            $"({shut.SurfaceTemperature:F2} vs {open.SurfaceTemperature:F2} K)");
    }

    /// <summary>
    /// Energy closure has to survive the window becoming an emitting band. These are the same
    /// two identities the absorbing band is held to, now with both bands contributing.
    /// </summary>
    [TestMethod]
    public void EnergyClosesWithAnAbsorbingWindow()
    {
        var column = Column.Build(WindowedOptions(continuum: 1.0));
        var rad = RadiationSolver.Solve(column);

        for (int i = 0; i < column.Count; i++)
        {
            Assert.AreEqual(rad.RadiativeHeating[i],
                rad.SegmentAbsorption[i] - rad.SegmentEmission[i], 1e-9,
                $"segment {i}: absorbed - emitted must equal the flux convergence");
        }

        double absorbed = 0.0, emitted = 0.0;
        for (int i = 0; i < column.Count; i++)
        {
            absorbed += rad.SegmentAbsorption[i];
            emitted += rad.SegmentEmission[i];
        }

        Assert.AreEqual(rad.SurfaceUpwardFlux + emitted - rad.SurfaceDownwardFlux,
            absorbed + rad.OutgoingLongwave, 1e-9,
            "column absorption + OLR = surface upward flux + column emission");
    }

    /// <summary>
    /// An opaque continuum shuts the escape route: instead of the surface's window emission
    /// reaching space, the window band emits from its own cold top like any other band.
    /// </summary>
    /// <remarks>
    /// Compared against the transparent case rather than against the window flux in isolation,
    /// because the reported OLR is the sum over both bands - the absorbing band contributes
    /// most of it, so total OLR never falls below the window flux and asserting that it does
    /// would be testing the wrong thing.
    ///
    /// It traps only about half, not nearly all, and the reason is worth knowing: the continuum
    /// follows the vapour and the self term squares it, so it is heavily bottom-heavy. The
    /// window therefore goes opaque close to the ground and keeps emitting from air that is
    /// only a little cooler than the surface. Piling on more continuum raises the emission
    /// level slowly, which is why the trapped fraction climbs but never approaches one.
    /// </remarks>
    [TestMethod]
    public void AnOpaqueContinuumTrapsPartOfTheWindowFlux()
    {
        var options = WindowedOptions(continuum: 0.0);
        var transparent = Column.Build(options);
        double openOlr = RadiationSolver.Solve(transparent).OutgoingLongwave;

        double windowFlux = options.WindowShare(transparent.SurfaceTemperature) *
                            options.SurfaceEmissivity *
                            RadiationSolver.StefanBoltzmannFlux(transparent.SurfaceTemperature);

        double Trapped(double continuum) =>
            openOlr - RadiationSolver.Solve(Column.Build(WindowedOptions(continuum: continuum)))
                .OutgoingLongwave;

        double trapped = Trapped(60.0);

        Assert.IsTrue(trapped > 0.4 * windowFlux,
            $"an opaque window should trap a substantial part of the {windowFlux:F1} W/m2 " +
            $"window flux (trapped {trapped:F1} W/m2)");
        Assert.IsTrue(trapped < windowFlux,
            $"it cannot trap more than the window carried in the first place " +
            $"(trapped {trapped:F1} vs {windowFlux:F1} W/m2)");

        // The mechanism: more continuum keeps raising the window's emission level, so the
        // trapped flux increases monotonically without ever reaching the whole window flux.
        double previous = 0.0;
        foreach (double continuum in new[] { 0.5, 2.0, 8.0, 30.0, 120.0 })
        {
            double now = Trapped(continuum);
            Assert.IsTrue(now > previous,
                $"continuum {continuum} should trap more than the step before " +
                $"({previous:F2} then {now:F2} W/m2)");
            Assert.IsTrue(now < windowFlux,
                $"continuum {continuum} traps {now:F2}, which exceeds the window flux {windowFlux:F1}");
            previous = now;
        }
    }

    [TestMethod]
    public void ContinuumWithoutVapourIsRejected()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            Column.Build(WindowedOptions(continuum: 1.0, vapour: 0.0)),
            "a vapour continuum needs vapour to drive it");
    }

    [TestMethod]
    public void ContinuumWithoutAWindowIsRejected()
    {
        var options = WindowedOptions(continuum: 1.0);
        options.WindowShortWavelength = 0.0;
        options.WindowLongWavelength = 0.0;

        Assert.ThrowsException<ArgumentException>(() => Column.Build(options),
            "a continuum needs a window to sit inside");
    }

    /// <summary>
    /// The continuum is bottom-heavy: it follows the vapour, which has a 2 km scale height, and
    /// the self term squares that. So it must fall off faster than the vapour absorber itself.
    /// </summary>
    [TestMethod]
    public void ContinuumIsMoreBottomHeavyThanTheVapourItself()
    {
        var options = WindowedOptions(continuum: 1.0, foreign: 0.0);
        var column = Column.Build(options);

        int mid = column.Count / 2;

        double vapourRatio = column.Segments[0].EmissionCoefficient /
                             column.Segments[mid].EmissionCoefficient;
        double continuumRatio = column.Segments[0].WindowEmissionCoefficient /
                                column.Segments[mid].WindowEmissionCoefficient;

        Assert.IsTrue(continuumRatio > vapourRatio,
            $"the squared self term must concentrate lower than the vapour ({continuumRatio:F1} vs {vapourRatio:F1})");
    }
}
