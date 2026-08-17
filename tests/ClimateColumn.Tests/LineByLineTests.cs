using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The band approximations checked against a resolved spectral calculation, rather than against
/// more of the model's own reasoning.
/// </summary>
/// <remarks>
/// Every other test in this project is a consistency check - a closed form the solver should
/// satisfy, a budget that should balance - and consistency cannot say whether a band
/// approximation is any good. These resolve the lines and compare.
///
/// The line list is synthetic and documented, not HITRAN, so what is being validated is the
/// method against exact spectral integration: that reordering plus quadrature reproduces the
/// true integral, that a grey band does not, and what the correlated-k assumption costs in an
/// inhomogeneous column. Whether the model resembles Earth's spectrum is a different question
/// and needs real line data.
/// </remarks>
[TestClass]
public class LineByLineTests
{
    private static LineByLineBand? _band;

    /// <summary>
    /// One band shared across the class. Resolving 60 lines at 60,000 wavenumbers is the
    /// expensive part, and it is deterministic, so it is built once.
    /// </summary>
    private static LineByLineBand Band => _band ??= LineByLineBand.Synthetic();

    private static readonly double[] OpticalDepths = { 0.05, 0.3, 1.0, 3.0, 10.0, 40.0 };

    [TestMethod]
    public void SyntheticBandIsWellFormedAndDeterministic()
    {
        var first = LineByLineBand.Synthetic(lineCount: 20, samples: 4000);
        var second = LineByLineBand.Synthetic(lineCount: 20, samples: 4000);

        Assert.AreEqual(4000, first.Samples, "the requested resolution should be used");
        Assert.IsTrue(first.Lines.Count > 20,
            $"lines should also populate the margins either side ({first.Lines.Count})");

        var a = first.AbsorptionCoefficients();
        var b = second.AbsorptionCoefficients();
        for (int i = 0; i < a.Length; i += 137)
        {
            Assert.AreEqual(a[i], b[i], 0.0, $"sample {i} must be reproducible from the seed");
        }

        // Normalisation: the band mean is 1 by construction, and the spectrum genuinely varies.
        double mean = 0.0, min = double.MaxValue, max = double.MinValue;
        foreach (double k in a)
        {
            mean += k / a.Length;
            min = Math.Min(min, k);
            max = Math.Max(max, k);
        }

        Assert.AreEqual(1.0, mean, 1e-12, "the band mean should be normalised to one");
        Assert.IsTrue(max / min > 100.0,
            $"a line spectrum should span orders of magnitude ({min:E2} to {max:E2})");
    }

    /// <summary>
    /// The central claim: reordering the spectrum by absorption strength and integrating over
    /// cumulative probability reproduces the true spectral integral. A permutation cannot change
    /// a mean, so this is exact in principle - which makes it a clean test of whether the
    /// implementation really does what the theory says.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.05)]
    [DataRow(0.3)]
    [DataRow(1.0)]
    [DataRow(3.0)]
    [DataRow(10.0)]
    [DataRow(40.0)]
    public void ReorderedSpectrumReproducesLineByLineTransmission(double opticalDepth)
    {
        double reference = Band.Transmission(opticalDepth);

        // One g-point per sample: the quadrature is then the spectrum itself, reordered.
        var exact = Band.ToKDistribution(Band.Samples);

        Assert.AreEqual(reference, exact.Transmission(opticalDepth), 1e-12,
            $"tau = {opticalDepth}: reordering must not change the band transmission");
    }

    /// <summary>
    /// The practical claim: a handful of g-points is enough. This is what makes correlated-k
    /// worth doing rather than resolving the spectrum every time.
    /// </summary>
    [DataTestMethod]
    [DataRow(8, 0.02)]
    [DataRow(16, 0.01)]
    [DataRow(32, 0.005)]
    public void FewGPointsReproduceLineByLineTransmission(int points, double tolerance)
    {
        var quadrature = Band.ToKDistribution(points);

        foreach (double tau in OpticalDepths)
        {
            double reference = Band.Transmission(tau);
            double approximation = quadrature.Transmission(tau);

            Assert.AreEqual(reference, approximation, tolerance,
                $"{points} g-points at tau = {tau}: line-by-line {reference:F5}, " +
                $"quadrature {approximation:F5}");
        }
    }

    [TestMethod]
    public void MoreGPointsConvergeOnTheLineByLineAnswer()
    {
        foreach (double tau in OpticalDepths)
        {
            double reference = Band.Transmission(tau);

            double coarse = Math.Abs(Band.ToKDistribution(4).Transmission(tau) - reference);
            double fine = Math.Abs(Band.ToKDistribution(64).Transmission(tau) - reference);

            Assert.IsTrue(fine <= coarse + 1e-12,
                $"tau = {tau}: refining should not move away from line-by-line " +
                $"({coarse:E2} then {fine:E2})");
        }
    }

    /// <summary>
    /// What the whole exercise is for: a grey band, holding exactly the same absorber, is far too
    /// opaque. Transmission is set by the weak wings, and a single mean coefficient cannot
    /// represent them.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.3)]
    [DataRow(1.0)]
    [DataRow(3.0)]
    [DataRow(10.0)]
    public void GreyBandIsMuchTooOpaqueComparedToLineByLine(double opticalDepth)
    {
        double reference = Band.Transmission(opticalDepth);
        double grey = Math.Exp(-opticalDepth);

        Assert.IsTrue(grey < reference,
            $"tau = {opticalDepth}: grey must under-transmit relative to line-by-line " +
            $"({grey:F5} vs {reference:F5})");

        // And the error is large, not marginal - which is why a k-distribution is needed at all.
        Assert.IsTrue(reference - grey > 0.05,
            $"tau = {opticalDepth}: the grey error should be substantial " +
            $"(line-by-line {reference:F4}, grey {grey:F4})");
    }

    /// <summary>
    /// The parametric shapes the model actually offers, judged against the resolved spectrum.
    /// The band is built with exponentially distributed line strengths, so the Goody-style
    /// exponential family should describe it better than a lognormal - and this is the test that
    /// says whether the width knob can be set to something defensible at all.
    /// </summary>
    [TestMethod]
    public void ParametricShapesBracketTheLineByLineTransmission()
    {
        const double tau = 1.0;
        double reference = Band.Transmission(tau);

        double greyError = Math.Abs(Math.Exp(-tau) - reference);

        double bestError = double.MaxValue;
        double bestWidth = 0.0;
        for (double width = 0.25; width <= 4.0; width += 0.25)
        {
            double error = Math.Abs(
                KDistribution.Build(KDistributionShape.Lognormal, width, 64).Transmission(tau) -
                reference);

            if (error < bestError)
            {
                bestError = error;
                bestWidth = width;
            }
        }

        Assert.IsTrue(bestError < 0.25 * greyError,
            $"a fitted lognormal should beat grey by a wide margin (best width {bestWidth:F2}, " +
            $"error {bestError:F4} vs grey {greyError:F4})");
        Assert.IsTrue(bestWidth is > 0.25 and < 4.0,
            $"the best width should be interior to the range searched, not at an edge ({bestWidth:F2})");
    }

    // ------------------------------------------------------- the correlated-k assumption

    /// <summary>
    /// Correlated-k assumes one spectral ordering serves every level. Across layers whose lines
    /// are broadened differently that is not exactly true, and this measures the cost.
    /// </summary>
    /// <remarks>
    /// The error is quantified rather than merely asserted to be small, because it is the one
    /// approximation in the scheme with no closed form to check against - and because a number
    /// is what lets someone decide whether it matters for their purpose.
    /// </remarks>
    [DataTestMethod]
    [DataRow(16)]
    [DataRow(64)]
    public void CorrelatedKTracksLineByLineAcrossAnInhomogeneousColumn(int points)
    {
        // Three layers with pressure falling by half each time, so line widths differ
        // threefold from bottom to top - a strong test of the correlation assumption.
        var pressures = new[] { 1.0, 0.5, 0.25 };
        var depths = new[] { 0.6, 0.3, 0.1 };

        var layers = new List<(double, double)>();
        for (int l = 0; l < pressures.Length; l++) layers.Add((pressures[l], depths[l]));

        double reference = Band.Transmission(layers);

        var quadratures = Band.CorrelatedQuadrature(points, pressures);
        double correlated = LineByLineBand.CorrelatedTransmission(quadratures, depths);

        Assert.AreEqual(reference, correlated, 0.02,
            $"{points} g-points: correlated-k {correlated:F5} against line-by-line {reference:F5}");
    }

    /// <summary>
    /// Correlated-k must beat the alternative of pretending the column is homogeneous, or the
    /// extra machinery earns nothing.
    /// </summary>
    [TestMethod]
    public void CorrelatedKBeatsTreatingTheColumnAsGrey()
    {
        var pressures = new[] { 1.0, 0.5, 0.25 };
        var depths = new[] { 0.6, 0.3, 0.1 };

        var layers = new List<(double, double)>();
        for (int l = 0; l < pressures.Length; l++) layers.Add((pressures[l], depths[l]));

        double reference = Band.Transmission(layers);
        double totalDepth = depths.Sum();

        double grey = Math.Exp(-totalDepth);
        double correlated = LineByLineBand.CorrelatedTransmission(
            Band.CorrelatedQuadrature(32, pressures), depths);

        Assert.IsTrue(Math.Abs(correlated - reference) < Math.Abs(grey - reference),
            $"correlated-k {correlated:F5} should be closer to line-by-line {reference:F5} " +
            $"than grey {grey:F5}");
    }

    /// <summary>
    /// The two error sources in a correlated-k calculation are separable, and this is what tells
    /// them apart: quadrature error falls without limit as g-points are added, while the
    /// correlation error does not fall at all.
    /// </summary>
    /// <remarks>
    /// Refining from 8 to 64 g-points cuts the total error roughly tenfold, and then it stops:
    /// past about 64 points the remaining discrepancy is the correlation assumption itself, not
    /// the quadrature. For this band and a threefold pressure range it floors near 8e-4 in
    /// transmission - some four hundred times smaller than treating the column as grey, which is
    /// the comparison that matters when deciding whether to bother.
    ///
    /// Knowing where the floor is also tells you when adding g-points has stopped buying
    /// anything, which no amount of internal consistency checking could have revealed.
    /// </remarks>
    [TestMethod]
    public void RefiningGPointsStopsHelpingOnceCorrelationErrorDominates()
    {
        var pressures = new[] { 1.0, 0.5, 0.25 };
        var depths = new[] { 0.6, 0.3, 0.1 };

        var layers = new List<(double, double)>();
        for (int l = 0; l < pressures.Length; l++) layers.Add((pressures[l], depths[l]));

        double reference = Band.Transmission(layers);

        double Error(int points) => Math.Abs(
            LineByLineBand.CorrelatedTransmission(
                Band.CorrelatedQuadrature(points, pressures), depths) - reference);

        double coarse = Error(8);
        double moderate = Error(64);
        double fine = Error(256);

        Assert.IsTrue(moderate < 0.5 * coarse,
            $"refining from 8 to 64 g-points should cut the error substantially " +
            $"({coarse:E2} then {moderate:E2})");

        // The floor: quadruple the points again and almost nothing changes, because what is
        // left is the correlation assumption rather than the quadrature.
        Assert.IsTrue(fine > 0.5 * moderate,
            $"past 64 g-points the error should have floored on the correlation assumption " +
            $"({moderate:E2} then {fine:E2})");
        Assert.IsTrue(fine is > 1e-5 and < 5e-3,
            $"the correlation floor should be small but real ({fine:E2})");

        // And it is far smaller than the alternative of ignoring the structure altogether.
        double grey = Math.Abs(Math.Exp(-depths.Sum()) - reference);
        Assert.IsTrue(grey > 100.0 * fine,
            $"the correlation floor {fine:E2} should be dwarfed by the grey error {grey:E2}");
    }

    /// <summary>
    /// A homogeneous stack is the case where reordering is exact, so correlated-k should be
    /// essentially perfect there. If it is not, the machinery is wrong rather than merely
    /// approximate - which is what separates the assumption's cost from an implementation bug.
    /// </summary>
    [TestMethod]
    public void CorrelatedKIsNearExactWhenEveryLayerSharesAPressure()
    {
        var pressures = new[] { 1.0, 1.0, 1.0 };
        var depths = new[] { 0.5, 0.3, 0.2 };

        var layers = new List<(double, double)>();
        for (int l = 0; l < pressures.Length; l++) layers.Add((pressures[l], depths[l]));

        double reference = Band.Transmission(layers);
        double correlated = LineByLineBand.CorrelatedTransmission(
            Band.CorrelatedQuadrature(256, pressures), depths);

        Assert.AreEqual(reference, correlated, 1e-4,
            $"with one pressure throughout, reordering is exact ({correlated:F6} vs {reference:F6})");
    }
}
