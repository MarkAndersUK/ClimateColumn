namespace ClimateColumn.Core;

/// <summary>
/// The fractional blackbody function: what share of a blackbody's total emission falls
/// inside a wavelength interval.
/// </summary>
/// <remarks>
/// This is what turns the spectral window from a free knob into a computed quantity. A bare
/// "30 % of the spectrum is transparent" cannot be right or wrong, because nothing says which
/// wavelengths; naming the interval instead makes the share follow from the interval and the
/// emitter's own temperature.
///
/// The share matters because it is strongly temperature dependent. For 8-12 um it is 25.2 %
/// of a 287 K surface's emission but only 15.1 % of a 217 K tropopause's - a factor of 1.7
/// across the range a column actually spans, and the cold end is where the outgoing longwave
/// is set.
/// </remarks>
public static class Planck
{
    /// <summary>Second radiation constant c_2 = h c / k_B, m K.</summary>
    public const double SecondRadiationConstant = 1.438776877e-2;

    private const double FifteenOverPiFourth = 15.0 / (Math.PI * Math.PI * Math.PI * Math.PI);

    /// <summary>
    /// Share of a blackbody's emission at wavelengths below <paramref name="lambdaTimesT"/>,
    /// the product of wavelength (m) and temperature (K).
    /// </summary>
    /// <remarks>
    /// The fraction depends only on the product lambda*T, not on either separately, so one
    /// evaluation covers every wavelength and temperature. Uses the standard series
    ///
    ///   F(0 -&gt; x) = (15/pi^4) sum_n e^-nx (x^3/n + 3x^2/n^2 + 6x/n^3 + 6/n^4),
    ///   x = c_2 / (lambda T)
    ///
    /// which converges geometrically in e^-x. Over the range this model uses - roughly
    /// 1700 to 4200 um K, so x from 3.4 to 8.5 - a handful of terms suffice; the loop simply
    /// runs until the terms stop mattering.
    /// </remarks>
    public static double FractionBelow(double lambdaTimesT)
    {
        if (lambdaTimesT <= 0.0) return 0.0;

        double x = SecondRadiationConstant / lambdaTimesT;

        // Far into the Rayleigh-Jeans tail the whole spectrum is below the cut.
        if (x < 1e-6) return 1.0;

        double sum = 0.0;
        for (int n = 1; n <= 10_000; n++)
        {
            double nd = n;
            double term = Math.Exp(-nd * x) *
                          (x * x * x / nd + 3.0 * x * x / (nd * nd) +
                           6.0 * x / (nd * nd * nd) + 6.0 / (nd * nd * nd * nd));
            sum += term;

            // The terms fall off like e^-nx, so once one is negligible the tail is too.
            if (term < 1e-16 * Math.Max(sum, 1e-30)) break;
        }

        return Math.Clamp(FifteenOverPiFourth * sum, 0.0, 1.0);
    }

    /// <summary>
    /// Share of a blackbody's emission at temperature <paramref name="temperature"/> (K)
    /// falling between <paramref name="shortWavelength"/> and
    /// <paramref name="longWavelength"/> (both m). Zero for a degenerate interval.
    /// </summary>
    public static double FractionBetween(double shortWavelength, double longWavelength,
        double temperature)
    {
        if (temperature <= 0.0 || longWavelength <= shortWavelength) return 0.0;

        double upper = FractionBelow(longWavelength * temperature);
        double lower = FractionBelow(shortWavelength * temperature);

        return Math.Clamp(upper - lower, 0.0, 1.0);
    }
}
