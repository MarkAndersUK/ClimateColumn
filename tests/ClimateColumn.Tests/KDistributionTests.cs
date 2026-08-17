using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The correlated-k quadrature: the spread of absorption coefficients within a band, and what
/// it does to transmission and to the column.
/// </summary>
[TestClass]
public class KDistributionTests
{
    private static ModelOptions Options(
        KDistributionShape shape = KDistributionShape.Lognormal,
        double width = 1.5, int points = 16, double tau = 1.8) => new()
    {
        SegmentCount = 40,
        TotalOpticalDepth = tau,
        KDistributionShape = shape,
        KDistributionWidth = width,
        KDistributionPoints = points
    };

    /// <summary>
    /// The quadrature must not change how much absorber the column holds: the weights sum to
    /// one and the weighted mean coefficient is exactly the band mean. Without this, turning a
    /// k-distribution on would silently retune the model.
    /// </summary>
    [DataTestMethod]
    [DataRow(KDistributionShape.Lognormal, 0.5, 8)]
    [DataRow(KDistributionShape.Lognormal, 1.5, 16)]
    [DataRow(KDistributionShape.Lognormal, 3.0, 32)]
    [DataRow(KDistributionShape.Exponential, 1.0, 16)]
    [DataRow(KDistributionShape.Exponential, 1.0, 64)]
    public void QuadraturePreservesTheWeightsAndTheBandMean(
        KDistributionShape shape, double width, int points)
    {
        var k = KDistribution.Build(shape, width, points);

        double weightSum = 0.0, mean = 0.0;
        for (int j = 0; j < k.Points; j++)
        {
            weightSum += k.Weights[j];
            mean += k.Weights[j] * k.Multipliers[j];
        }

        Assert.AreEqual(points, k.Points, "one sub-band per requested g-point");
        Assert.AreEqual(1.0, weightSum, 1e-12, "weights must sum to one");
        Assert.AreEqual(1.0, mean, 1e-12, "the weighted mean must be exactly the band mean");
    }

    [TestMethod]
    public void ZeroWidthCollapsesOntoAGreyBand()
    {
        var k = KDistribution.Build(KDistributionShape.Lognormal, 0.0, 16);

        Assert.AreEqual(1, k.Points, "a grey band needs only one sub-band");
        Assert.AreEqual(1.0, k.Multipliers[0], 0.0, "and that sub-band carries the band mean");
        Assert.AreEqual(1.0, k.Weights[0], 0.0);
    }

    [TestMethod]
    public void GreyShapeIgnoresWidthAndPoints()
    {
        var k = KDistribution.Build(KDistributionShape.Grey, 3.0, 32);

        Assert.AreEqual(1, k.Points, "a grey band is one sub-band whatever else is asked for");
    }

    /// <summary>
    /// The Goody random band model - lines at random with exponentially distributed strengths -
    /// has the closed-form transmission 1 / (1 + k u). Reproducing it is an external check on
    /// the quadrature machinery, since the answer comes from the analytic model rather than
    /// from this code.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.1)]
    [DataRow(0.5)]
    [DataRow(1.0)]
    [DataRow(4.0)]
    [DataRow(20.0)]
    public void ExponentialQuadratureConvergesToTheGoodyTransmission(double opticalDepth)
    {
        double analytic = 1.0 / (1.0 + opticalDepth);

        // The exponential tail needs a lot of points to resolve, so this is a convergence
        // statement: a coarse quadrature is close, a fine one is closer.
        double coarse = KDistribution.Build(KDistributionShape.Exponential, 1.0, 32)
            .Transmission(opticalDepth);
        double fine = KDistribution.Build(KDistributionShape.Exponential, 1.0, 4096)
            .Transmission(opticalDepth);

        Assert.AreEqual(analytic, fine, 0.01,
            $"tau = {opticalDepth}: a fine quadrature should reproduce 1/(1+tau) = {analytic:F4}");
        Assert.IsTrue(Math.Abs(fine - analytic) <= Math.Abs(coarse - analytic) + 1e-9,
            $"tau = {opticalDepth}: refining the quadrature should not move away from the " +
            $"analytic answer (coarse {coarse:F4}, fine {fine:F4}, exact {analytic:F4})");
    }

    /// <summary>
    /// The optically thin limit is untouched, because &lt;1 - exp(-k u)&gt; -&gt; &lt;k&gt; u and
    /// the mean is preserved. This is what lets the Koenigsberger correspondence and the D = 2
    /// closure survive a non-grey band: thin-layer absorption still depends only on the band
    /// mean.
    /// </summary>
    /// <remarks>
    /// Written as a limit rather than a single evaluation, because a wide spread puts some
    /// g-points at enormous k and the linearisation only holds once tau is small enough that
    /// even those are thin: at width 3 a tau of 1e-7 still leaves a relative error near 3e-4.
    /// Uses Absorption rather than 1 - Transmission, since the latter's cancellation at tiny
    /// tau would swamp exactly the quantity being measured.
    /// </remarks>
    [DataTestMethod]
    [DataRow(KDistributionShape.Lognormal, 1.5)]
    [DataRow(KDistributionShape.Lognormal, 3.0)]
    [DataRow(KDistributionShape.Exponential, 1.0)]
    public void ThinLimitAbsorptionIsUnchanged(KDistributionShape shape, double width)
    {
        var k = KDistribution.Build(shape, width, 256);

        double RelativeError(double tau) => Math.Abs(k.Absorption(tau) - tau) / tau;

        double coarse = RelativeError(1e-6);
        double fine = RelativeError(1e-12);

        Assert.IsTrue(fine <= coarse,
            $"the linearisation should tighten as tau shrinks ({coarse:E2} then {fine:E2})");
        Assert.IsTrue(fine < 1e-6,
            $"in the thin limit absorption must be the band-mean optical depth whatever the " +
            $"spread (relative error {fine:E2})");
    }

    /// <summary>
    /// Absorption and transmission must still be complements wherever the direct subtraction is
    /// well conditioned, so the stable form is not quietly computing something else.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.1)]
    [DataRow(1.0)]
    [DataRow(5.0)]
    public void AbsorptionAndTransmissionAreComplements(double opticalDepth)
    {
        var k = KDistribution.Build(KDistributionShape.Lognormal, 1.5, 32);

        Assert.AreEqual(1.0 - k.Transmission(opticalDepth), k.Absorption(opticalDepth), 1e-12,
            $"tau = {opticalDepth}: absorption and transmission must sum to one");
    }

    /// <summary>
    /// The whole reason k-distributions exist: at the same mean absorber amount, a band with
    /// structure transmits more than a grey one, because transmission is dominated by the weak
    /// wings rather than the mean. A grey band therefore overstates how opaque the atmosphere is.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.5)]
    [DataRow(2.0)]
    [DataRow(10.0)]
    public void StructuredBandsTransmitMoreThanGreyOnes(double opticalDepth)
    {
        double grey = Math.Exp(-opticalDepth);
        double narrow = KDistribution.Build(KDistributionShape.Lognormal, 1.0, 256)
            .Transmission(opticalDepth);
        double wide = KDistribution.Build(KDistributionShape.Lognormal, 2.5, 256)
            .Transmission(opticalDepth);

        Assert.IsTrue(narrow > grey,
            $"tau = {opticalDepth}: a structured band must transmit more than grey " +
            $"({narrow:F5} vs {grey:F5})");
        Assert.IsTrue(wide > narrow,
            $"tau = {opticalDepth}: a wider spread must transmit more still " +
            $"({wide:F5} vs {narrow:F5})");
    }

    [TestMethod]
    public void MorePointsResolveTheSpreadMoreFinely()
    {
        var coarse = KDistribution.Build(KDistributionShape.Lognormal, 2.0, 4);
        var fine = KDistribution.Build(KDistributionShape.Lognormal, 2.0, 64);

        Assert.IsTrue(fine.Multipliers[^1] > coarse.Multipliers[^1],
            "a finer quadrature should reach further into the strong-absorption tail");
        Assert.IsTrue(fine.Multipliers[0] < coarse.Multipliers[0],
            "and further into the weak-absorption tail");
    }

    // ------------------------------------------------------------------ in the column

    [TestMethod]
    public void GreyOptionsLeaveTheSolverBitIdentical()
    {
        // Turning the feature on with zero width must change nothing at all, since that is how
        // every existing configuration is expressed.
        var plain = Column.Build(new ModelOptions { SegmentCount = 30 });
        var declared = Column.Build(new ModelOptions
        {
            SegmentCount = 30,
            KDistributionShape = KDistributionShape.Lognormal,
            KDistributionWidth = 0.0
        });

        var a = RadiationSolver.Solve(plain);
        var b = RadiationSolver.Solve(declared);

        Assert.AreEqual(a.OutgoingLongwave, b.OutgoingLongwave, 0.0,
            "a zero-width k-distribution must be bit-identical to a grey band");
        for (int i = 0; i < plain.Count; i++)
        {
            Assert.AreEqual(a.RadiativeHeating[i], b.RadiativeHeating[i], 0.0,
                $"segment {i} heating must be bit-identical");
        }
    }

    /// <summary>
    /// Energy closure has to survive the band being split into g-points. These are the same two
    /// identities the grey band is held to, now summed over every sub-band.
    /// </summary>
    [DataTestMethod]
    [DataRow(KDistributionShape.Lognormal, 1.5)]
    [DataRow(KDistributionShape.Exponential, 1.0)]
    public void EnergyClosesAcrossTheQuadrature(KDistributionShape shape, double width)
    {
        var column = Column.Build(Options(shape, width, points: 12));
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
    /// In the column the extra transmission shows up as a higher outgoing longwave at fixed
    /// temperature, and a cooler equilibrium surface: the same gas is doing less greenhouse work
    /// once its line structure is admitted.
    /// </summary>
    [TestMethod]
    public void BandStructureRaisesOutgoingLongwaveAndCoolsTheSurface()
    {
        var grey = Column.Build(new ModelOptions { SegmentCount = 40 });
        var structured = Column.Build(Options(width: 2.0, points: 16));

        double greyOlr = RadiationSolver.Solve(grey).OutgoingLongwave;
        double structuredOlr = RadiationSolver.Solve(structured).OutgoingLongwave;

        Assert.IsTrue(structuredOlr > greyOlr,
            $"line structure must let more longwave out at fixed temperature " +
            $"({structuredOlr:F2} vs {greyOlr:F2} W/m2)");

        var greyRun = TestSupport.Default;
        var structuredRun = TestSupport.Equilibrium("k-lognormal-2",
            () => Options(width: 2.0, points: 16));

        Assert.IsTrue(structuredRun.Converged, "the structured column must reach equilibrium");
        Assert.IsTrue(structuredRun.SurfaceTemperature < greyRun.SurfaceTemperature,
            $"admitting line structure must cool the equilibrium surface " +
            $"({structuredRun.SurfaceTemperature:F2} vs {greyRun.SurfaceTemperature:F2} K)");
    }

    /// <summary>
    /// The g-point count is an accuracy dial, not a physics dial: refining it must converge
    /// rather than keep moving the answer.
    /// </summary>
    [TestMethod]
    public void ResultConvergesAsGPointsAreRefined()
    {
        double Olr(int points) =>
            RadiationSolver.Solve(Column.Build(Options(width: 2.0, points: points))).OutgoingLongwave;

        double[] olr = { Olr(4), Olr(8), Olr(16), Olr(32), Olr(64) };

        double coarseStep = Math.Abs(olr[1] - olr[0]);
        double fineStep = Math.Abs(olr[^1] - olr[^2]);

        Assert.IsTrue(fineStep < coarseStep,
            $"refinement should settle ({coarseStep:F4} then {fineStep:F4} W/m2 per step)");
        Assert.IsTrue(fineStep < 0.5,
            $"by 64 g-points the answer should be stable to well under 1 W/m2 ({fineStep:F4})");
    }
}
