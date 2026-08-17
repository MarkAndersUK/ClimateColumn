namespace ClimateColumn.Core;

/// <summary>One spectral line: where it sits, how strong it is, and how broad.</summary>
/// <param name="Wavenumber">Line centre, cm^-1.</param>
/// <param name="Strength">Integrated line strength, in the arbitrary units of the band.</param>
/// <param name="HalfWidth">Lorentz half-width at the reference pressure, cm^-1.</param>
public readonly record struct SpectralLine(double Wavenumber, double Strength, double HalfWidth);

/// <summary>
/// A brute-force spectral reference: an explicit list of lines, resolved on a fine wavenumber
/// grid, with no band approximation anywhere.
/// </summary>
/// <remarks>
/// This exists to check the k-distribution against a first-principles calculation rather than
/// against more of the model's own reasoning. Every other verification in this project is a
/// consistency check - closed forms the solver should satisfy, budgets that should balance -
/// and consistency cannot tell you whether a band approximation is any good. Only resolving
/// the lines can.
///
/// A caveat that matters, stated plainly: <b>the line list is synthetic</b>. It is generated
/// from a documented rule with a fixed seed, not read from HITRAN, so this validates the
/// <i>method</i> against exact spectral integration - does reordering plus quadrature reproduce
/// the true integral, and how much does the correlated-k assumption cost in an inhomogeneous
/// column - and not the model against Earth's actual spectrum. Answering that second question
/// needs real line data.
///
/// Everything is dimensionless by construction: absorption coefficients are normalised so the
/// band mean is 1, and the absorber amount is then just the band-mean optical depth. That keeps
/// the arithmetic about the shape of the distribution, which is what the band approximation
/// actually turns on.
/// </remarks>
public sealed class LineByLineBand
{
    private readonly double[] _wavenumbers;
    private readonly SpectralLine[] _lines;

    private LineByLineBand(double[] wavenumbers, SpectralLine[] lines, double start, double end)
    {
        _wavenumbers = wavenumbers;
        _lines = lines;
        Start = start;
        End = end;
    }

    /// <summary>Lower edge of the evaluated band, cm^-1.</summary>
    public double Start { get; }

    /// <summary>Upper edge of the evaluated band, cm^-1.</summary>
    public double End { get; }

    /// <summary>Number of wavenumber samples across the band.</summary>
    public int Samples => _wavenumbers.Length;

    /// <summary>The lines making up the band.</summary>
    public IReadOnlyList<SpectralLine> Lines => _lines;

    /// <summary>
    /// Builds a synthetic band: <paramref name="lineCount"/> lines placed pseudo-randomly with
    /// exponentially distributed strengths, resolved at <paramref name="samples"/> points.
    /// </summary>
    /// <remarks>
    /// Exponential strengths are the Goody random band model's own assumption, which makes this
    /// a fair test of a k-distribution built on that family. Lines are seeded from a fixed
    /// linear congruential generator so a given configuration always produces the same band -
    /// a reference that moved between runs would be no reference at all.
    ///
    /// Lines are also placed in a margin either side of the evaluated band, so that the wings
    /// reaching in from outside are present. Without them the band edges would be
    /// systematically too transparent, which is an artefact of the window rather than physics.
    /// </remarks>
    public static LineByLineBand Synthetic(
        double start = 600.0, double end = 700.0, int lineCount = 60, int samples = 60_000,
        double halfWidth = 0.08, uint seed = 20260817u)
    {
        if (end <= start) throw new ArgumentException("end must exceed start.");
        if (lineCount < 1) throw new ArgumentException("lineCount must be >= 1.");
        if (samples < 2) throw new ArgumentException("samples must be >= 2.");
        if (halfWidth <= 0) throw new ArgumentException("halfWidth must be positive.");

        // Populate a margin of strong wings either side of the evaluated interval.
        double margin = 0.25 * (end - start);
        double lineFrom = start - margin, lineTo = end + margin;

        uint state = seed;
        int totalLines = (int)Math.Round(lineCount * (lineTo - lineFrom) / (end - start));

        var lines = new SpectralLine[totalLines];
        for (int i = 0; i < totalLines; i++)
        {
            double position = lineFrom + NextDouble(ref state) * (lineTo - lineFrom);

            // Exponentially distributed strength, mean 1, by inverse CDF.
            double u = Math.Clamp(NextDouble(ref state), 1e-12, 1.0 - 1e-12);
            double strength = -Math.Log(1.0 - u);

            lines[i] = new SpectralLine(position, strength, halfWidth);
        }

        var grid = new double[samples];
        double step = (end - start) / samples;
        for (int i = 0; i < samples; i++) grid[i] = start + (i + 0.5) * step;

        return new LineByLineBand(grid, lines, start, end);
    }

    /// <summary>
    /// Builds a band from an explicit line list - HITRAN data, or anything else.
    /// </summary>
    /// <param name="lines">The lines, including any lying outside the evaluated interval.</param>
    /// <param name="start">Lower edge of the evaluated band, cm^-1.</param>
    /// <param name="end">Upper edge, cm^-1.</param>
    /// <param name="samples">Wavenumber samples across the band.</param>
    /// <param name="wingCutoff">
    /// How far from its centre a line is still evaluated, cm^-1. Real line lists are large
    /// enough that evaluating every line at every sample is not affordable, and truncating the
    /// wings is what every line-by-line code does; 25 cm^-1 is the usual choice. Infinity keeps
    /// every line everywhere, which is what the synthetic band uses.
    /// </param>
    public static LineByLineBand FromLines(
        IReadOnlyList<SpectralLine> lines, double start, double end, int samples,
        double wingCutoff = double.PositiveInfinity)
    {
        if (end <= start) throw new ArgumentException("end must exceed start.");
        if (samples < 2) throw new ArgumentException("samples must be >= 2.");
        if (lines.Count == 0) throw new ArgumentException("at least one line is needed.");
        if (wingCutoff <= 0) throw new ArgumentException("wingCutoff must be positive.");

        var grid = new double[samples];
        double step = (end - start) / samples;
        for (int i = 0; i < samples; i++) grid[i] = start + (i + 0.5) * step;

        return new LineByLineBand(grid, lines.ToArray(), start, end) { WingCutoff = wingCutoff };
    }

    /// <summary>How far from its centre a line is evaluated, cm^-1.</summary>
    public double WingCutoff { get; private init; } = double.PositiveInfinity;

    /// <summary>
    /// Absorption coefficient at every wavenumber sample, for a path at
    /// <paramref name="pressureRatio"/> times the reference pressure, normalised so that the
    /// band mean at the reference pressure is exactly 1.
    /// </summary>
    /// <remarks>
    /// Lines are Lorentz-shaped with half-width proportional to pressure, which is the
    /// collision broadening the model's own PressureBroadeningExponent gestures at. Because
    /// the profile is normalised in area, broadening moves absorption from the cores into the
    /// wings without changing the band mean - so a pressure change reshapes the distribution
    /// while leaving the total absorber amount alone, which is exactly the case that tests
    /// whether correlated-k holds.
    /// </remarks>
    public double[] AbsorptionCoefficients(double pressureRatio = 1.0)
    {
        if (pressureRatio <= 0) throw new ArgumentException("pressureRatio must be positive.");

        var k = Accumulate(pressureRatio);

        // Normalise by the reference-pressure band mean so results are dimensionless. The
        // reference mean is used at every pressure, since area-normalised broadening leaves it
        // unchanged and rescaling per pressure would hide that.
        double norm = BandMeanAtReference();
        if (norm > 0)
        {
            for (int i = 0; i < k.Length; i++) k[i] /= norm;
        }

        return k;
    }

    /// <summary>
    /// Sums the Lorentz profiles onto the wavenumber grid.
    /// </summary>
    /// <remarks>
    /// Scatters each line onto the samples within its cutoff rather than looping every line
    /// over every sample. With a real line list the difference is decisive: 28,000 CO2 lines
    /// against 90,000 samples is billions of evaluations the direct way, and a fraction of that
    /// once each line only touches its own neighbourhood.
    /// </remarks>
    private double[] Accumulate(double pressureRatio)
    {
        var k = new double[_wavenumbers.Length];
        if (_wavenumbers.Length < 2) return k;

        double step = _wavenumbers[1] - _wavenumbers[0];
        double first = _wavenumbers[0];

        foreach (var line in _lines)
        {
            double gamma = line.HalfWidth * pressureRatio;
            double gammaSquared = gamma * gamma;
            double amplitude = line.Strength * gamma / Math.PI;

            int from = 0, to = k.Length - 1;
            if (!double.IsPositiveInfinity(WingCutoff))
            {
                from = (int)Math.Ceiling((line.Wavenumber - WingCutoff - first) / step);
                to = (int)Math.Floor((line.Wavenumber + WingCutoff - first) / step);
                if (from < 0) from = 0;
                if (to > k.Length - 1) to = k.Length - 1;
            }

            for (int i = from; i <= to; i++)
            {
                double offset = _wavenumbers[i] - line.Wavenumber;
                k[i] += amplitude / (offset * offset + gammaSquared);
            }
        }

        return k;
    }

    private double? _referenceMean;

    private double BandMeanAtReference()
    {
        if (_referenceMean is double cached) return cached;

        var k = Accumulate(1.0);

        double sum = 0.0;
        foreach (double value in k) sum += value;

        double mean = sum / k.Length;
        _referenceMean = mean;
        return mean;
    }

    /// <summary>
    /// Exact band transmission through a homogeneous path of band-mean optical depth
    /// <paramref name="opticalDepth"/>: the spectral mean of exp(-k(nu) u), with no band
    /// approximation.
    /// </summary>
    public double Transmission(double opticalDepth, double pressureRatio = 1.0)
    {
        var k = AbsorptionCoefficients(pressureRatio);

        double sum = 0.0;
        for (int i = 0; i < k.Length; i++) sum += Math.Exp(-k[i] * opticalDepth);
        return sum / k.Length;
    }

    /// <summary>
    /// Exact band transmission through a stack of layers, each with its own pressure and
    /// band-mean optical depth. The spectral integral is done after summing optical depths at
    /// each wavenumber, which is what makes this the reference an inhomogeneous correlated-k
    /// calculation has to be judged against.
    /// </summary>
    public double Transmission(IReadOnlyList<(double PressureRatio, double OpticalDepth)> layers)
    {
        var total = new double[_wavenumbers.Length];

        foreach (var (pressureRatio, opticalDepth) in layers)
        {
            var k = AbsorptionCoefficients(pressureRatio);
            for (int i = 0; i < total.Length; i++) total[i] += k[i] * opticalDepth;
        }

        double sum = 0.0;
        for (int i = 0; i < total.Length; i++) sum += Math.Exp(-total[i]);
        return sum / total.Length;
    }

    /// <summary>
    /// The band's own k-distribution, measured rather than assumed: sort the resolved
    /// absorption coefficients, divide them into equal-weight groups, and take each group's
    /// mean.
    /// </summary>
    /// <remarks>
    /// Averaging within each g-interval rather than sampling a point inside it preserves the
    /// band mean automatically, so the quadrature holds exactly as much absorber as the
    /// spectrum did.
    /// </remarks>
    public KDistribution ToKDistribution(int points, double pressureRatio = 1.0)
    {
        if (points < 1) throw new ArgumentException("points must be >= 1.");

        var k = AbsorptionCoefficients(pressureRatio);
        Array.Sort(k);

        return GroupIntoQuadrature(new[] { k }, points).Single();
    }

    /// <summary>
    /// One sub-band carved out of a resolved spectrum: where it sits, how strongly it absorbs
    /// relative to the whole range, and the line structure inside it.
    /// </summary>
    /// <param name="FromWavenumber">Lower edge, cm^-1.</param>
    /// <param name="ToWavenumber">Upper edge, cm^-1.</param>
    /// <param name="RelativeStrength">
    /// Mean absorption coefficient in this sub-band as a multiple of the whole range's mean. This
    /// is what makes the derivation a derivation: the relative opacity of the bands comes from
    /// the line data, not from a choice.
    /// </param>
    /// <param name="Structure">The sub-band's own k-distribution, relative to its own mean.</param>
    public readonly record struct DerivedSubBand(
        double FromWavenumber, double ToWavenumber, double RelativeStrength, KDistribution Structure);

    /// <summary>How to place sub-band boundaries across a resolved spectrum.</summary>
    public enum SubdivisionStrategy
    {
        /// <summary>
        /// Each sub-band carries an equal share of the Planck function at a reference
        /// temperature, so bands are narrow where the emission is concentrated and wide in the
        /// tails. Balances what each band contributes to the radiation budget.
        /// </summary>
        EqualPlanckEnergy,

        /// <summary>Equal width in wavenumber. Simple, and useful for checking the above.</summary>
        UniformWavenumber
    }

    /// <summary>
    /// Carves the resolved spectrum into <paramref name="count"/> sub-bands, measuring each one's
    /// strength and line structure from the data.
    /// </summary>
    /// <remarks>
    /// This is what turns hand-specified bands into derived ones. Nothing about the outcome is
    /// chosen except how many bands to use and where to cut: each band's opacity relative to its
    /// neighbours is the mean of the resolved absorption inside it, and its k-distribution is the
    /// measured distribution of that absorption. What remains a free parameter is the total amount
    /// of gas, which is a matter of concentration rather than spectroscopy.
    /// </remarks>
    public IReadOnlyList<DerivedSubBand> Subdivide(
        int count, int gPoints,
        SubdivisionStrategy strategy = SubdivisionStrategy.EqualPlanckEnergy,
        double referenceTemperature = 260.0)
    {
        if (count < 1) throw new ArgumentException("count must be >= 1.");
        if (gPoints < 1) throw new ArgumentException("gPoints must be >= 1.");
        if (referenceTemperature <= 0) throw new ArgumentException("referenceTemperature must be positive.");

        var k = AbsorptionCoefficients();
        double[] edges = Edges(count, strategy, referenceTemperature);

        var result = new List<DerivedSubBand>(count);
        double step = _wavenumbers[1] - _wavenumbers[0];

        for (int b = 0; b < count; b++)
        {
            int from = (int)Math.Ceiling((edges[b] - _wavenumbers[0]) / step);
            int to = (int)Math.Ceiling((edges[b + 1] - _wavenumbers[0]) / step);

            from = Math.Clamp(from, 0, k.Length);
            to = Math.Clamp(to, from + 1, k.Length);

            double mean = 0.0;
            for (int i = from; i < to; i++) mean += k[i];
            mean /= to - from;

            // The quadrature is relative to this sub-band's own mean, so the mean itself carries
            // the band's opacity and the distribution carries only its shape.
            var slice = new double[to - from];
            for (int i = from; i < to; i++) slice[i - from] = mean > 0 ? k[i] / mean : 1.0;
            Array.Sort(slice);

            result.Add(new DerivedSubBand(
                edges[b], edges[b + 1], mean,
                GroupIntoQuadrature(new[] { slice }, Math.Min(gPoints, slice.Length)).Single()));
        }

        return result;
    }

    /// <summary>Sub-band boundaries in cm^-1, count + 1 of them.</summary>
    private double[] Edges(int count, SubdivisionStrategy strategy, double referenceTemperature)
    {
        var edges = new double[count + 1];
        edges[0] = Start;
        edges[count] = End;

        if (strategy == SubdivisionStrategy.UniformWavenumber)
        {
            for (int b = 1; b < count; b++) edges[b] = Start + (End - Start) * b / count;
            return edges;
        }

        // Equal Planck energy. The fraction of a Planck function below a wavelength is a function
        // of the product lambda*T, and wavelength is 1/wavenumber, so the cumulative emission
        // between two wavenumbers is a difference of two such fractions. Bisect on wavenumber to
        // split that total evenly - the function is monotonic, so bisection is safe and exact
        // enough for a band edge.
        double Cumulative(double wavenumber) =>
            Planck.FractionBelow(Wavelength(Start) * referenceTemperature) -
            Planck.FractionBelow(Wavelength(wavenumber) * referenceTemperature);

        double total = Cumulative(End);

        if (total <= 0)
        {
            for (int b = 1; b < count; b++) edges[b] = Start + (End - Start) * b / count;
            return edges;
        }

        for (int b = 1; b < count; b++)
        {
            double target = total * b / count;
            double low = Start, high = End;

            for (int iteration = 0; iteration < 80; iteration++)
            {
                double middle = 0.5 * (low + high);
                if (Cumulative(middle) < target) low = middle; else high = middle;
            }

            edges[b] = 0.5 * (low + high);
        }

        return edges;
    }

    /// <summary>Wavelength in metres for a wavenumber in cm^-1.</summary>
    private static double Wavelength(double wavenumber) => 0.01 / wavenumber;

    /// <summary>
    /// A correlated-k quadrature over a stack of layers: order the spectrum once, by the
    /// reference layer, and use that same ordering at every level.
    /// </summary>
    /// <remarks>
    /// This is the assumption the name refers to, and the one worth measuring. Absorption is
    /// only strictly reorderable for a homogeneous path; across layers whose line widths differ,
    /// a given g no longer picks out the same wavenumbers, and the error that introduces is
    /// what these quadratures let you quantify against
    /// <see cref="Transmission(IReadOnlyList{ValueTuple{double, double}})"/>.
    /// </remarks>
    public IReadOnlyList<KDistribution> CorrelatedQuadrature(
        int points, IReadOnlyList<double> pressureRatios, double orderingPressureRatio = 1.0)
    {
        var ordering = AbsorptionCoefficients(orderingPressureRatio);

        // The permutation that sorts the reference layer, applied to every layer.
        var order = new int[ordering.Length];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort((double[])ordering.Clone(), order);

        var reordered = new List<double[]>(pressureRatios.Count);
        foreach (double pressureRatio in pressureRatios)
        {
            var k = AbsorptionCoefficients(pressureRatio);
            var sorted = new double[k.Length];
            for (int i = 0; i < k.Length; i++) sorted[i] = k[order[i]];
            reordered.Add(sorted);
        }

        return GroupIntoQuadrature(reordered, points);
    }

    /// <summary>
    /// Builds a quadrature from an already-sorted set of absorption coefficients, relative to their
    /// own mean. Lets a caller that has assembled a spectrum itself - a mixture of gases, say - use
    /// the same grouping the single-gas path uses.
    /// </summary>
    public KDistribution QuadratureFrom(double[] sortedCoefficients, int points) =>
        GroupIntoQuadrature(new[] { sortedCoefficients }, points).Single();

    /// <summary>
    /// Collapses already-ordered spectra into equal-weight g-point groups, averaging within
    /// each. Every input shares the same grouping, which is what keeps the sub-bands aligned
    /// across layers.
    /// </summary>
    private static IReadOnlyList<KDistribution> GroupIntoQuadrature(
        IReadOnlyList<double[]> ordered, int points)
    {
        int samples = ordered[0].Length;
        var results = new List<KDistribution>(ordered.Count);

        foreach (var spectrum in ordered)
        {
            var weights = new double[points];
            var multipliers = new double[points];

            for (int j = 0; j < points; j++)
            {
                int from = (int)((long)j * samples / points);
                int to = (int)((long)(j + 1) * samples / points);
                if (to <= from) to = Math.Min(from + 1, samples);

                double sum = 0.0;
                for (int i = from; i < to; i++) sum += spectrum[i];

                weights[j] = (to - from) / (double)samples;
                multipliers[j] = sum / (to - from);
            }

            results.Add(new KDistribution { Weights = weights, Multipliers = multipliers });
        }

        return results;
    }

    /// <summary>
    /// Transmission through a stack using a correlated-k quadrature: optical depths are summed
    /// per sub-band, then the sub-bands are combined.
    /// </summary>
    public static double CorrelatedTransmission(
        IReadOnlyList<KDistribution> quadratures, IReadOnlyList<double> opticalDepths)
    {
        int points = quadratures[0].Points;
        double sum = 0.0;

        for (int j = 0; j < points; j++)
        {
            double tau = 0.0;
            for (int l = 0; l < quadratures.Count; l++)
            {
                tau += quadratures[l].Multipliers[j] * opticalDepths[l];
            }
            sum += quadratures[0].Weights[j] * Math.Exp(-tau);
        }

        return sum;
    }

    /// <summary>
    /// Deterministic uniform in [0, 1) from a linear congruential generator, so that a given
    /// seed always rebuilds the same band.
    /// </summary>
    private static double NextDouble(ref uint state)
    {
        state = unchecked(state * 1664525u + 1013904223u);
        return state / 4294967296.0;
    }
}
