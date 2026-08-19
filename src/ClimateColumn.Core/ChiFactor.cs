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
/// which is the standard empirical treatment of the CO2 nu_2 band:
/// <code>
/// chi(s) = 1                                                     s &lt;= s1
/// chi(s) = exp(-B1 (s - s1))                                     s1 &lt; s &lt;= s2
/// chi(s) = exp(-B1 (s2 - s1) - B2 (s - s2))                      s2 &lt; s &lt;= s3
/// chi(s) = exp(-B1 (s2 - s1) - B2 (s3 - s2) - B3 (s - s3))       s &gt; s3
/// </code>
/// with <c>s = |nu - nu_0|</c> in cm^-1. It is continuous at every breakpoint by construction,
/// equal to one inside the impact region, and monotonically decreasing outside it.
///
/// <strong>A caveat that must travel with the numbers.</strong> The functional form above is
/// Perrin and Hartmann's. The default coefficients are representative values for the CO2 nu_2
/// band near 296 K, and they have <em>not</em> been checked against the original paper in this
/// work - so treat this as a correctly shaped sub-Lorentzian correction rather than as a
/// faithful reproduction of a published fit. They are constructor parameters precisely so that
/// anyone holding the paper can put the exact values in and re-run the measurement. Nothing here
/// is tuned to make the forcing match 5.35; whatever coefficient comes out is reported as
/// measured, which is the only way the forcing stays a prediction rather than a calibration.
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
    /// Representative Perrin-Hartmann coefficients for the CO2 nu_2 band near 296 K. See the
    /// caveat on this type: the shape is theirs, the exact numbers are unverified here.
    /// </summary>
    public static readonly ChiFactor CarbonDioxideNu2 = new(
        FirstBreak: 3.0, SecondBreak: 30.0, ThirdBreak: 120.0,
        FirstDecay: 0.0888, SecondDecay: 0.0232, ThirdDecay: 0.0160);

    /// <summary>No correction: the pure Lorentz profile the model used before.</summary>
    public static readonly ChiFactor None = new(
        FirstBreak: double.PositiveInfinity, SecondBreak: double.PositiveInfinity,
        ThirdBreak: double.PositiveInfinity,
        FirstDecay: 0.0, SecondDecay: 0.0, ThirdDecay: 0.0);

    /// <summary>The multiplier at a detuning of <paramref name="offset"/> cm^-1 from line centre.</summary>
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
