namespace ClimateColumn.Core;

/// <summary>One equilibrium point of a methane sweep.</summary>
public sealed record MethanePoint(double Ppb, double SurfaceTemperature, bool Converged);

/// <summary>
/// A methane concentration sweep, and the square-root law its forcing ought to follow.
/// </summary>
/// <remarks>
/// <strong>This exists because methane is not CO2, and the difference is the point.</strong>
/// CO2's 15 um band is saturated at its core, so extra gas can only act in the wings and the
/// forcing grows as ln(C). Methane's 7.7 um band is weak and largely unsaturated, so extra gas
/// acts across the whole band and the forcing grows as the square root of concentration.
///
/// The accepted law, from Myhre et al. (1998), is
/// <c>dF = 0.036 (sqrt(M) - sqrt(M0))</c> with M in ppb - the same form used for N2O, and quite
/// unlike CO2's logarithm. It carries an overlap correction with N2O which is <em>not</em>
/// included here; that term is a few hundredths of a W m^-2 over this range, and including a
/// correction the model has no way to produce would make the comparison less honest rather than
/// more accurate.
///
/// Nothing in the model imposes either law. The band structure comes from HITRAN line data and
/// the absorber amount is exactly linear in concentration, so if the response comes out closer
/// to a square root than to a logarithm, that is the spectroscopy saying so - which makes this
/// a sharper test of the band machinery than the CO2 sweep, where a logarithm was expected all
/// along.
/// </remarks>
public sealed class MethaneSweep
{
    /// <summary>
    /// Concentrations swept, ppb. The reference is first.
    /// </summary>
    /// <remarks>
    /// 700 ppb is roughly pre-industrial; about 1900 is present day; 3500 is well beyond any
    /// mainstream projection and is included because a square root and a logarithm are hard to
    /// tell apart over a narrow range and easy to separate over a wide one.
    /// </remarks>
    public static readonly double[] Concentrations =
        { 700, 900, 1100, 1300, 1500, 1700, 1900, 2400, 2900, 3500 };

    /// <summary>Present-day methane, ppb - the value worth calling out.</summary>
    public const double PresentDayPpb = 1900.0;

    /// <summary>Index of <see cref="PresentDayPpb"/>, looked up rather than hard-coded.</summary>
    public static readonly int PresentDayIndex = Array.IndexOf(Concentrations, PresentDayPpb);

    /// <summary>
    /// Accepted methane forcing coefficient, W m^-2 per sqrt(ppb), from Myhre et al. (1998).
    /// </summary>
    public const double AcceptedForcingCoefficient = 0.036;

    public required string Label { get; init; }
    public required string Command { get; init; }
    public required IReadOnlyList<MethanePoint> Points { get; init; }

    /// <summary>Instantaneous forcing at each concentration, against the reference state.</summary>
    public required IReadOnlyList<double> Forcings { get; init; }

    public double ReferencePpb => Concentrations[0];
    public double BaseTemperature => Points[0].SurfaceTemperature;

    /// <summary>Accepted forcing at index <paramref name="i"/>, W m^-2.</summary>
    public double AcceptedForcing(int i) =>
        AcceptedForcingCoefficient * (Math.Sqrt(Concentrations[i]) - Math.Sqrt(ReferencePpb));

    /// <summary>Warming from the reference to index <paramref name="i"/>, K.</summary>
    public double Warming(int i) => Points[i].SurfaceTemperature - BaseTemperature;

    /// <summary>
    /// How well the measured forcings fit a square root against a logarithm, as the residual of
    /// each best fit through the origin. Smaller is a better fit.
    /// </summary>
    /// <remarks>
    /// Reported as a pair rather than a verdict, because the interesting outcome is the ratio
    /// between them. Both curves are monotonic and both pass through zero at the reference, so
    /// over a narrow range either will fit; it is the wide end of the sweep that separates them.
    /// </remarks>
    /// <summary>
    /// The three candidate laws, each with the residual its best fit through the origin leaves.
    /// Smallest wins.
    /// </summary>
    /// <remarks>
    /// Three rather than two, and the third was added after looking at the figure. A band's
    /// response depends on how saturated it is: an optically thick band gives ln(M), a partly
    /// saturated one gives sqrt(M), and a genuinely thin one gives M - the absorber is simply
    /// linear because nothing is blocking anything yet.
    ///
    /// Once methane was calibrated down to an amount that produces the observed present-day
    /// forcing, the model's 7.7 um band became thin enough that its response is nearly linear -
    /// weaker curvature than the sqrt(M) the real atmosphere shows. Leaving the comparison at
    /// two laws would have reported "closer to sqrt(M)" and hidden that.
    /// </remarks>
    public (string Name, double Residual)[] LawFits() => new[]
    {
        ("linear in M", Residual(m => m - ReferencePpb)),
        ("√M", Residual(m => Math.Sqrt(m) - Math.Sqrt(ReferencePpb))),
        ("ln M", Residual(m => Math.Log(m / ReferencePpb)))
    };

    /// <summary>The law that fits best, by residual.</summary>
    public (string Name, double Residual) BestFit() =>
        LawFits().OrderBy(f => f.Item2).First();

    private double Residual(Func<double, double> basis)
    {
        double num = 0.0, den = 0.0;
        for (int i = 0; i < Points.Count; i++)
        {
            double x = basis(Concentrations[i]);
            num += x * Forcings[i];
            den += x * x;
        }
        double slope = den > 0 ? num / den : 0.0;

        double residual = 0.0;
        for (int i = 0; i < Points.Count; i++)
        {
            double d = Forcings[i] - slope * basis(Concentrations[i]);
            residual += d * d;
        }
        return residual;
    }

    public (double SquareRoot, double Logarithm) FitResiduals()
    {
        double SumSquares(Func<double, double> basis)
        {
            // Least-squares slope through the origin, then the residual it leaves.
            double num = 0.0, den = 0.0;
            for (int i = 0; i < Points.Count; i++)
            {
                double x = basis(Concentrations[i]);
                num += x * Forcings[i];
                den += x * x;
            }
            double slope = den > 0 ? num / den : 0.0;

            double residual = 0.0;
            for (int i = 0; i < Points.Count; i++)
            {
                double d = Forcings[i] - slope * basis(Concentrations[i]);
                residual += d * d;
            }
            return residual;
        }

        double c0 = ReferencePpb;
        return (SumSquares(m => Math.Sqrt(m) - Math.Sqrt(c0)),
                SumSquares(m => Math.Log(m / c0)));
    }

    /// <summary>
    /// Runs the spectral configuration across every methane concentration, or null when the
    /// HITRAN line lists have not been fetched.
    /// </summary>
    /// <param name="equilibrate">
    /// Also march each concentration to equilibrium, so surface temperatures are available.
    /// False measures forcings alone, which needs one march rather than ten and is what a study
    /// of the forcing law wants.
    /// </param>
    /// <param name="rederive">
    /// Re-derive the bands at every methane concentration rather than deriving once at the
    /// reference and scaling each band's methane share. See the remarks on
    /// <see cref="Co2Sweep.SpectralConfiguration"/>: scaling the share stretches a band's mean
    /// while its k-distribution goes on describing the reference atmosphere, so saturation that
    /// ought to develop in methane's strong lines never appears. Costs one band derivation per
    /// concentration.
    /// </param>
    public static MethaneSweep? Run(bool equilibrate = true, double cloudFraction = 0.0,
        double methaneShare = Co2Sweep.CalibratedMethaneShare,
        double absorberScale = double.NaN, bool rederive = false)
    {
        Func<double, ModelOptions>? At(double ppb) => Co2Sweep.SpectralConfiguration(
            absorberScale: absorberScale, cloudFraction: cloudFraction, methaneShare: methaneShare,
            methaneRatio: rederive ? ppb / Concentrations[0] : 1.0);

        var configure = At(Concentrations[0]);
        if (configure is null) return null;

        // The bands are derived once at the reference; methane is dialled through each band's
        // recorded share, exactly as CO2 is when it is not re-derived. That is a real
        // approximation and it is the same one: the band mean scales correctly with
        // concentration while the k-distribution inside it goes on describing the reference
        // atmosphere.
        ModelOptions Options(double ppb)
        {
            // Re-derived bands already hold this concentration, so the dial stays at the
            // reference; otherwise the dial is what does the work.
            var options = (rederive ? At(ppb)! : configure)(Co2Sweep.Concentrations[0]);
            options.MethaneConcentration = rederive ? Concentrations[0] : ppb;
            return options;
        }

        var reference = ColumnModel.RunToEquilibrium(Options(Concentrations[0]));

        int n = Concentrations.Length;
        var points = new MethanePoint[n];
        var forcings = new double[n];

        void Measure(int i)
        {
            var options = Options(Concentrations[i]);
            var result = i == 0 || !equilibrate
                ? reference
                : ColumnModel.RunToEquilibrium(options);

            points[i] = new MethanePoint(
                Concentrations[i], result.SurfaceTemperature, result.Converged);
            forcings[i] = ForcingFrom(reference, options);
        }

        Measure(0);
        Parallel.For(1, n, Measure);

        return new MethaneSweep
        {
            Label = "Methane, derived from HITRAN bands" +
                    (cloudFraction > 0.0 ? $" under {cloudFraction:P0} cloud" : ""),
            Command = "see MethaneSweep.Run - 6 molecules, 16 derived bands, 16 g-points",
            Points = points,
            Forcings = forcings
        };
    }

    /// <summary>
    /// Instantaneous forcing, W m^-2: the drop in outgoing longwave when the methane is changed
    /// while the temperatures stay at the reference equilibrium's.
    /// </summary>
    /// <remarks>
    /// The same held-temperature definition the CO2 sweep uses, and the same all-sky treatment -
    /// both sides must see the same sky, or the difference is between two planets rather than
    /// between two methane amounts.
    /// </remarks>
    private static double ForcingFrom(ModelResult baseline, ModelOptions perturbed)
    {
        var held = Column.Build(perturbed);
        for (int i = 0; i < held.Count; i++)
            held.Segments[i].Temperature = baseline.Column.Segments[i].Temperature;
        held.SurfaceTemperature = baseline.SurfaceTemperature;
        held.DistributeOpticalDepth();

        return baseline.Radiation.OutgoingLongwave -
               ColumnModel.SolveSky(held).AllSky.OutgoingLongwave;
    }
}
