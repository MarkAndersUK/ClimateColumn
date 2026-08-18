namespace ClimateColumn.Core;

/// <summary>
/// A quantity a CO2 sweep can be plotted against concentration, together with whatever
/// reference curve belongs beside it and how to scale an axis for it.
/// </summary>
/// <remarks>
/// This exists because the two plottable quantities differ in what can honestly be compared
/// against them, and that difference is the point rather than an inconvenience.
///
/// <strong>Forcing carries a reference; temperature does not.</strong> The accepted law
/// 5.35 ln(C/C0) is a statement about forcing in W m^-2, so plotting the model's own forcing
/// beside it compares like with like and borrows nothing. Turning that law into a temperature
/// needs a sensitivity, and the only one available is the model's own - which makes the
/// resulting curve partly a restatement of the model it is meant to test. So the temperature
/// panel shows the model alone: what it says, with no reference implying agreement or
/// disagreement.
///
/// The alternative closure - back-calculating temperature from Stefan-Boltzmann,
/// <c>T(F) = T_0 (S/(S-F))^(1/4)</c> - is model-independent but answers a different question:
/// it is the no-feedback Planck response, about 0.30 K per W m^-2 here against the model's
/// 0.639, so the gap it opens measures the model's water-vapour feedback rather than anything
/// about the forcing law. Comparing forcings directly avoids having to pick between them.
/// </remarks>
public sealed record Co2ChartQuantity(
    string Name,
    string AxisTitle,
    string Unit,
    string TickFormat,
    string EndLabelFormat,
    string ValueFormat,
    bool AnchorAtZero,
    Func<Co2Sweep, int, double> Model,
    Func<Co2Sweep, int, double>? Reference,
    string? ReferenceLabel)
{
    /// <summary>Whether a reference curve belongs beside this quantity.</summary>
    public bool HasReference => Reference is not null;

    /// <summary>
    /// Instantaneous forcing against the fixed reference state, W m^-2, beside the accepted
    /// logarithmic law. This is the comparison that borrows nothing from the model.
    /// </summary>
    public static readonly Co2ChartQuantity Forcing = new(
        Name: "Forcing",
        AxisTitle: "Radiative forcing (W m⁻²)",
        Unit: "W/m²",
        TickFormat: "F1",
        EndLabelFormat: "F2",
        ValueFormat: "F3",
        AnchorAtZero: true,
        Model: (s, i) => s.Forcings[i],
        Reference: (s, i) => s.AcceptedForcing(i),
        ReferenceLabel: "5.35 ln(C/C₀)");

    /// <summary>
    /// Equilibrium surface temperature, K. No reference: see the note on this type.
    /// </summary>
    public static readonly Co2ChartQuantity SurfaceTemperature = new(
        Name: "Surface temperature",
        AxisTitle: "Surface temperature (K)",
        Unit: "K",
        TickFormat: "F0",
        EndLabelFormat: "F2",
        ValueFormat: "F3",
        AnchorAtZero: false,
        Model: (s, i) => s.Points[i].SurfaceTemperature,
        Reference: null,
        ReferenceLabel: null);

    /// <summary>Both quantities, in the order a figure should present them.</summary>
    public static readonly Co2ChartQuantity[] All = { Forcing, SurfaceTemperature };

    /// <summary>
    /// Axis minimum, maximum and gridline spacing covering every series this quantity draws.
    /// </summary>
    /// <remarks>
    /// Shared by the WinForms painter and the HTML renderer so the two figures cannot drift
    /// apart - they previously computed the same range twice, in two files.
    ///
    /// Forcing anchors at zero because zero is a real point on that axis: it is the reference
    /// concentration, where the forcing is zero by construction. Temperature has no such
    /// anchor, so its range is padded around the data instead.
    /// </remarks>
    public (double Min, double Max, double Step) Range(IReadOnlyList<Co2Sweep> sweeps)
    {
        double lo = double.MaxValue, hi = double.MinValue;

        foreach (var sweep in sweeps)
        {
            for (int i = 0; i < sweep.Points.Count; i++)
            {
                double value = Model(sweep, i);
                lo = Math.Min(lo, value);
                hi = Math.Max(hi, value);

                if (Reference is not null)
                {
                    double reference = Reference(sweep, i);
                    lo = Math.Min(lo, reference);
                    hi = Math.Max(hi, reference);
                }
            }
        }

        if (lo > hi) return (0.0, 1.0, 0.5);

        if (AnchorAtZero)
        {
            lo = Math.Min(0.0, lo);
            hi = Math.Max(0.0, hi);
        }

        double span = hi - lo;
        if (span <= 0.0) span = Math.Max(1.0, Math.Abs(hi));

        double step = NiceStep(span / 6.0);

        double min = AnchorAtZero && lo >= 0.0 ? 0.0 : Math.Floor(lo / step) * step;
        double max = Math.Ceiling((hi + 0.35 * step) / step) * step;

        return (min, max, step);
    }

    /// <summary>
    /// Whether sweep <paramref name="index"/> plots the same curve as the first one, to within
    /// <paramref name="tolerance"/>.
    /// </summary>
    /// <remarks>
    /// Drawing one curve exactly over another communicates nothing: the later series hides the
    /// earlier, the legend advertises a colour that is nowhere on the figure, and the duplicated
    /// end markers and labels read as two different values that happen to coincide.
    ///
    /// It is not hypothetical here. Instantaneous forcing is measured at held temperatures, so the
    /// water-vapour feedback cannot change it, and the two configurations produce forcing curves
    /// identical to four decimal places. That identity is the result worth showing - but it is
    /// shown by saying so, not by painting the same line twice.
    /// </remarks>
    public bool DuplicatesFirst(IReadOnlyList<Co2Sweep> sweeps, int index, double tolerance = 1e-6)
    {
        if (index <= 0 || index >= sweeps.Count) return false;

        var first = sweeps[0];
        var other = sweeps[index];
        if (first.Points.Count != other.Points.Count) return false;

        for (int i = 0; i < first.Points.Count; i++)
        {
            double a = Model(first, i);
            double b = Model(other, i);
            if (Math.Abs(a - b) > tolerance * Math.Max(1.0, Math.Abs(a))) return false;
        }
        return true;
    }

    /// <summary>
    /// The 1 / 2 / 2.5 / 5 / 10 multiple of a power of ten nearest above
    /// <paramref name="raw"/>, so gridlines land on numbers a reader can do arithmetic with.
    /// </summary>
    public static double NiceStep(double raw)
    {
        if (raw <= 0.0 || double.IsNaN(raw)) return 1.0;

        double magnitude = Math.Pow(10.0, Math.Floor(Math.Log10(raw)));
        double normalised = raw / magnitude;

        double snapped = normalised switch
        {
            <= 1.0 => 1.0,
            <= 2.0 => 2.0,
            <= 2.5 => 2.5,
            <= 5.0 => 5.0,
            _ => 10.0
        };

        return snapped * magnitude;
    }
}
