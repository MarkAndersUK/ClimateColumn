namespace ClimateColumn.Core;

/// <summary>
/// A sub-Lorentzian correction to the far wings of a line, <c>chi(|nu - nu_0|)</c>, multiplying
/// the Lorentz profile.
/// </summary>
/// <remarks>
/// <strong>Why the wings need correcting at all.</strong> The Lorentz profile comes from the
/// impact approximation, which assumes collisions are instantaneous compared with the time
/// between them. That holds near line centre and fails far from it: at a detuning of tens of
/// cm^-1 the radiation is probing the collision itself, over times short enough that the
/// finite duration of a collision matters. Real CO2 wings therefore fall off <em>faster</em>
/// than Lorentzian - they are sub-Lorentzian - and a model that keeps integrating a pure
/// Lorentz profile out to hundreds of cm^-1 absorbs radiation that the atmosphere does not.
///
/// <strong>Why it matters here in particular.</strong> This model's CO2 forcing comes almost
/// entirely from the far wings; that is where the logarithm comes from, since the band core is
/// saturated and only the wings can still respond to more gas. So the far-wing shape is not a
/// detail of the line list, it is the thing that sets the forcing coefficient. The project's
/// own convergence study showed exactly that: the coefficient runs 5.15, 6.63, 6.88, 6.98
/// W m^-2 per ln as the wing cutoff opens from 100 to 800 cm^-1, converging on a value about
/// 1.30 times the accepted 5.35. Truncating the wings early gave a coefficient close to the
/// accepted one, but for the wrong reason - it was a hard cutoff standing in for a smooth
/// physical decay, two errors cancelling.
///
/// <strong>The form</strong> is the three-segment exponential of Perrin and Hartmann (1989),
/// which is the standard empirical treatment of CO2 far wings:
/// <code>
/// chi(s) = 1                                                     s &lt;= s1
/// chi(s) = exp(-B1 (s - s1))                                     s1 &lt; s &lt;= s2
/// chi(s) = exp(-B1 (s2 - s1) - B2 (s - s2))                      s2 &lt; s &lt;= s3
/// chi(s) = exp(-B1 (s2 - s1) - B2 (s3 - s2) - B3 (s - s3))       s &gt; s3
/// </code>
/// with <c>s = |nu - nu_0|</c> in cm^-1. It is continuous at every breakpoint by construction,
/// equal to one inside the impact region, and monotonically decreasing outside it.
///
/// <strong>Which band the coefficients belong to matters, and it caught this model out.</strong>
/// Perrin and Hartmann's own 1989 measurement is of the <em>4.3 um nu_3</em> band of CO2 in N2,
/// not the 15 um nu_2 band this model absorbs in. The functional form above is general and is
/// theirs; the coefficients are per band and per collision partner, and using one band's numbers
/// for another is not a small error - the segment boundaries alone differ by a factor of two.
/// This type previously carried a set of nu_2 coefficients its own comments described as
/// unverified, and they turned out to match no published set.
///
/// <see cref="CarbonDioxideNu2InNitrogen"/> now carries measured nu_2 CO2-N2 values, which are
/// the ones an Earth-like column wants: the absorber is CO2 at trace concentration in a bath of
/// N2, so N2 is what broadens its lines.
///
/// <strong>Nothing here is tuned to make the forcing match 5.35.</strong> The coefficients are
/// the published fit, and whatever forcing comes out is reported as measured - which is the only
/// way it stays a prediction rather than a calibration.
///
/// The correction is applied to CO2 alone. Chi factors are band- and molecule-specific, and
/// this one is for the CO2 nu_2 band; applying it to water vapour or ozone would be inventing
/// spectroscopy rather than correcting it.
/// </remarks>
public sealed record ChiFactor(
    double FirstBreak,
    double SecondBreak,
    double ThirdBreak,
    double FirstDecay,
    double SecondDecay,
    double ThirdDecay)
{
    /// <summary>
    /// The nu_2 (15 um) band of CO2 broadened by N2, at a given temperature.
    /// </summary>
    /// <remarks>
    /// Boundaries 3 / 50 / 180 cm^-1, and B_i(T) = alpha + beta exp(-gamma T), are the measured
    /// CO2-N2 nu_2 values tabulated by Chaverot et al. (2025), A&amp;A 702, A137, Tables 2 and 3,
    /// in the Perrin-Hartmann form. That paper is a deliberate update of the older factors rather
    /// than a restatement of them, and Hartmann is among its authors.
    ///
    /// The default temperature is 296 K, matching the temperature at which this model uses
    /// HITRAN's line strengths. One temperature for the whole column is a simplification - a real
    /// column's chi varies with height - but it is the same simplification already made for the
    /// line strengths, so it adds no new inconsistency.
    /// </remarks>
    public static ChiFactor CarbonDioxideNu2InNitrogen(double temperature = 296.0) => new(
        FirstBreak: 3.0, SecondBreak: 50.0, ThirdBreak: 180.0,
        FirstDecay: 0.065 + 0.038 * Math.Exp(-0.003 * temperature),
        SecondDecay: 0.018 + 0.055 * Math.Exp(-0.020 * temperature),
        ThirdDecay: 0.0085);

    /// <summary>
    /// The nu_2 band of CO2 broadened by CO2 itself - a pure-CO2 atmosphere, not Earth's.
    /// </summary>
    /// <remarks>
    /// Kept because the difference between the two is the point: same band, same table, a
    /// different collision partner, and boundaries of 3 / 30 / 150 rather than 3 / 50 / 180. A
    /// chi factor is not a property of the absorber alone.
    /// </remarks>
    public static ChiFactor CarbonDioxideNu2InCarbonDioxide(double temperature = 296.0) => new(
        FirstBreak: 3.0, SecondBreak: 30.0, ThirdBreak: 150.0,
        FirstDecay: 0.085 + 1.962 * Math.Exp(-0.020 * temperature),
        SecondDecay: 0.0185,
        ThirdDecay: 0.011);

    /// <summary>The factor this model applies to CO2: its band, broadened by the air around it.</summary>
    public static readonly ChiFactor CarbonDioxideNu2 = CarbonDioxideNu2InNitrogen();

    /// <summary>No correction: the pure Lorentz profile the model used before.</summary>
    public static readonly ChiFactor None = new(
        FirstBreak: double.PositiveInfinity, SecondBreak: double.PositiveInfinity,
        ThirdBreak: double.PositiveInfinity,
        FirstDecay: 0.0, SecondDecay: 0.0, ThirdDecay: 0.0);

    /// <summary>The multiplier at a detuning of <paramref name='offset'/> cm^-1 from line centre.</summary>
    public double At(double offset)
    {
        double s = Math.Abs(offset);
        if (s <= FirstBreak) return 1.0;

        // Accumulated exponent, carried across the breakpoints so the result is continuous.
        double exponent = FirstDecay * (Math.Min(s, SecondBreak) - FirstBreak);
        if (s > SecondBreak)
        {
            exponent += SecondDecay * (Math.Min(s, ThirdBreak) - SecondBreak);
        }
        if (s > ThirdBreak)
        {
            exponent += ThirdDecay * (s - ThirdBreak);
        }

        return Math.Exp(-exponent);
    }
}
