namespace ClimateColumn.Core;

/// <summary>How absorption is distributed within a band.</summary>
public enum KDistributionShape
{
    /// <summary>
    /// One absorption coefficient for the whole band. This is the grey band the rest of the
    /// model assumes, and the default.
    /// </summary>
    Grey,

    /// <summary>
    /// Lognormal spread of absorption coefficients about the band mean, with
    /// <see cref="ModelOptions.KDistributionWidth"/> as the log standard deviation. Width 0
    /// collapses exactly onto <see cref="Grey"/>, so it is the natural way to dial line
    /// structure up from nothing.
    /// </summary>
    Lognormal,

    /// <summary>
    /// Exponential spread, which is the Goody random band model: lines placed at random with
    /// exponentially distributed strengths. Its band transmission has the closed form
    /// 1 / (1 + k u), which is what the quadrature is verified against.
    /// </summary>
    Exponential
}

/// <summary>
/// A correlated-k quadrature over a band: a handful of pseudo-monochromatic sub-bands, each
/// with a weight and an absorption coefficient, that together stand in for the band's real
/// line structure.
/// </summary>
/// <remarks>
/// Absorption inside a band varies over orders of magnitude - strong line cores, weak wings -
/// so a single coefficient cannot represent it. The transmission
///
///     T(u) = (1/dnu) integral exp(-k(nu) u) dnu
///
/// depends only on the <em>distribution</em> of k across the band, not on where in the band
/// each value sits. Reordering k by magnitude gives a monotonic k(g) over the cumulative
/// fraction g in [0,1], and then
///
///     T(u) = integral_0^1 exp(-k(g) u) dg  ~  sum_j w_j exp(-k_j u).
///
/// Each (w_j, k_j) is a sub-band that runs through the ordinary grey recurrence, and the
/// results are summed. Assuming the ordering holds at every pressure and temperature - so a
/// given g refers to the same spectral places all the way down the column - is what makes it
/// "correlated" k and what makes it usable in an inhomogeneous atmosphere.
///
/// Two properties are enforced by construction, and both matter:
/// the weights sum to 1, and the weighted mean of k_j is exactly the band mean. The second is
/// what stops a k-distribution from quietly changing how much absorber the column holds. It
/// also preserves the optically thin limit, since
/// &lt;1 - exp(-k u)&gt; -&gt; &lt;k&gt; u, so the Koenigsberger correspondence and the D = 2
/// closure survive unchanged.
/// </remarks>
public sealed class KDistribution
{
    /// <summary>Quadrature weights over the cumulative-probability coordinate g. Sum to 1.</summary>
    public required double[] Weights { get; init; }

    /// <summary>
    /// Absorption coefficient of each sub-band as a multiple of the band mean. The
    /// weighted mean is exactly 1.
    /// </summary>
    public required double[] Multipliers { get; init; }

    public int Points => Weights.Length;

    /// <summary>The single-coefficient case: one sub-band carrying the whole band.</summary>
    public static KDistribution Grey { get; } = new()
    {
        Weights = new[] { 1.0 },
        Multipliers = new[] { 1.0 }
    };

    /// <summary>
    /// Builds the quadrature, then rescales the coefficients so the weighted mean is exactly
    /// the band mean.
    /// </summary>
    /// <remarks>
    /// The quadrature rule is chosen to match the distribution, which matters a great deal
    /// here. Sampling the inverse CDF at equal-probability midpoints is the obvious approach
    /// and converges appallingly - about N^-1/2, because k(g) is unbounded at both ends of the
    /// interval, so at 16 points a lognormal of width 2 still carries a transmission error near
    /// 7 %. Integrating instead in the variable where the density is standard fixes it:
    /// Gauss-Hermite against the normal density gives 3e-4 at the same 16 points, some two
    /// hundred times better, and keeps improving geometrically.
    ///
    /// So the lognormal uses Gauss-Hermite. The exponential shape keeps equal-probability
    /// midpoints: it exists to be checked against the Goody model's closed-form transmission
    /// rather than to be run in anger, and its own natural rule (Gauss-Laguerre) is
    /// ill-conditioned at the point counts that would be needed.
    ///
    /// The rescaling is not cosmetic. No finite quadrature reproduces the distribution's mean
    /// exactly, and without the correction the column's absorber amount would drift as the
    /// point count changed - turning an accuracy dial into a physics dial.
    /// </remarks>
    public static KDistribution Build(KDistributionShape shape, double width, int points)
    {
        if (shape == KDistributionShape.Grey || width <= 0.0 || points <= 1) return Grey;

        double[] weights;
        double[] multipliers;

        if (shape == KDistributionShape.Lognormal)
        {
            var (nodes, hermiteWeights) = GaussHermite(points);
            weights = hermiteWeights;
            multipliers = new double[points];
            for (int j = 0; j < points; j++)
            {
                // k = exp(-sigma^2/2 + sigma z), so E[k] = 1 before rescaling.
                multipliers[j] = Math.Exp(-0.5 * width * width + width * nodes[j]);
            }
        }
        else
        {
            weights = new double[points];
            multipliers = new double[points];
            for (int j = 0; j < points; j++)
            {
                weights[j] = 1.0 / points;
                multipliers[j] = ExponentialQuantile((j + 0.5) / points, width);
            }
        }

        // Normalise the weights to an exact probability measure first. Gauss-Hermite loses a
        // little precision in the weight sum at high point counts - about 3e-7 at 256 points -
        // and that residue would otherwise dominate any optically thin calculation, where the
        // signal itself is tiny.
        double weightSum = 0.0;
        for (int j = 0; j < points; j++) weightSum += weights[j];
        if (weightSum > 0.0)
        {
            for (int j = 0; j < points; j++) weights[j] /= weightSum;
        }

        // Then preserve the band mean exactly.
        double mean = 0.0;
        for (int j = 0; j < points; j++) mean += weights[j] * multipliers[j];
        if (mean > 0.0)
        {
            for (int j = 0; j < points; j++) multipliers[j] /= mean;
        }

        // Ascending in k, so the ends of the arrays are the weak and strong tails.
        Array.Sort(multipliers, weights);

        return new KDistribution { Weights = weights, Multipliers = multipliers };
    }

    /// <summary>
    /// Gauss-Hermite quadrature rewritten for the standard normal density: returns nodes z and
    /// weights that sum to 1, so that sum_j w_j f(z_j) approximates the expectation of f under
    /// N(0, 1).
    /// </summary>
    /// <remarks>
    /// Nodes are the roots of the Hermite polynomial, found by Newton iteration from the
    /// standard asymptotic starting guesses. The roots come in a symmetric pair for every
    /// positive one, so only half are computed. The physicists' rule integrates against
    /// exp(-x^2) with weights summing to sqrt(pi); substituting z = sqrt(2) x and dividing by
    /// sqrt(pi) converts it to the probabilists' form used here.
    /// </remarks>
    private static (double[] Nodes, double[] Weights) GaussHermite(int n)
    {
        var nodes = new double[n];
        var weights = new double[n];

        // The asymptotic starting guesses below are written in terms of the physicists' roots,
        // so those are kept unscaled here and converted to the probabilists' form only at the
        // end. Feeding already-scaled roots back into the guesses sends Newton to the wrong
        // root and silently produces duplicates.
        var raw = new double[n];

        const double inversePiQuarter = 0.7511255444649425;   // pi^(-1/4)
        double z = 0.0, derivative = 0.0;

        for (int i = 0; i < (n + 1) / 2; i++)
        {
            z = i switch
            {
                0 => Math.Sqrt(2.0 * n + 1.0) - 1.85575 * Math.Pow(2.0 * n + 1.0, -1.0 / 6.0),
                1 => z - 1.14 * Math.Pow(n, 0.426) / z,
                2 => 1.86 * z - 0.86 * raw[0],
                3 => 1.91 * z - 0.91 * raw[1],
                _ => 2.0 * z - raw[i - 2]
            };

            for (int iteration = 0; iteration < 30; iteration++)
            {
                double p1 = inversePiQuarter, p2 = 0.0;
                for (int j = 1; j <= n; j++)
                {
                    double p3 = p2;
                    p2 = p1;
                    p1 = z * Math.Sqrt(2.0 / j) * p2 - Math.Sqrt((j - 1.0) / j) * p3;
                }

                derivative = Math.Sqrt(2.0 * n) * p2;
                double step = p1 / derivative;
                z -= step;
                if (Math.Abs(step) < 1e-14) break;
            }

            raw[i] = z;
            raw[n - 1 - i] = -z;

            // Physicists' weights sum to sqrt(pi); dividing makes them a probability measure.
            double w = 2.0 / (derivative * derivative) / Math.Sqrt(Math.PI);
            weights[i] = w;
            weights[n - 1 - i] = w;
        }

        // z = sqrt(2) x turns the exp(-x^2) rule into one against the standard normal density.
        for (int i = 0; i < n; i++) nodes[i] = raw[i] * Math.Sqrt(2.0);

        return (nodes, weights);
    }

    /// <summary>
    /// Exponential quantile with unit mean, k = -ln(1 - g). The width parameter stretches the
    /// distribution about its mean in log space, so width 1 is the plain Goody model.
    /// </summary>
    private static double ExponentialQuantile(double g, double width)
    {
        double k = -Math.Log(1.0 - g);
        return width == 1.0 ? k : Math.Pow(k, width);
    }

    /// <summary>
    /// Band transmission through absorber amount <paramref name="opticalDepth"/> (the band-mean
    /// optical depth), summed over the quadrature.
    /// </summary>
    public double Transmission(double opticalDepth)
    {
        double sum = 0.0;
        for (int j = 0; j < Points; j++)
        {
            sum += Weights[j] * Math.Exp(-Multipliers[j] * opticalDepth);
        }
        return sum;
    }

    /// <summary>
    /// Band absorptivity, the complement of <see cref="Transmission"/>, computed without the
    /// cancellation that <c>1 - Transmission</c> suffers when the band is nearly transparent.
    /// </summary>
    /// <remarks>
    /// Forming 1 - T directly throws away almost all the precision once T approaches 1: at an
    /// optical depth of 1e-12 the subtraction keeps only about four significant digits. Summing
    /// the per-sub-band absorptivities instead, each evaluated stably, keeps full precision -
    /// which is what makes the optically thin limit testable at all.
    /// </remarks>
    public double Absorption(double opticalDepth)
    {
        double sum = 0.0;
        for (int j = 0; j < Points; j++)
        {
            sum += Weights[j] * OneMinusExp(Multipliers[j] * opticalDepth);
        }
        return sum;
    }

    /// <summary>
    /// 1 - exp(-x), accurate for small x. .NET has no expm1, so the series is used below the
    /// threshold where the direct form starts losing digits.
    /// </summary>
    private static double OneMinusExp(double x)
    {
        if (Math.Abs(x) >= 1e-5) return 1.0 - Math.Exp(-x);

        // 1 - e^-x = x - x^2/2 + x^3/6 - ...
        return x * (1.0 - x * (0.5 - x / 6.0));
    }
}
