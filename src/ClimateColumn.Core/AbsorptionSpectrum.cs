namespace ClimateColumn.Core;

/// <summary>One gas's band-averaged absorptivity across the infrared.</summary>
public sealed record AbsorptionTrace(string Gas, IReadOnlyList<double> Absorptivity);

/// <summary>
/// The infrared absorption bands of the atmosphere's gases, computed from HITRAN line data at
/// observed abundances.
/// </summary>
/// <remarks>
/// <strong>Absorptivity is averaged, not optical depth.</strong> A bin holding both saturated
/// line cores and transparent gaps has a mean optical depth that describes neither: the cores
/// contribute enormous values that no amount of real absorption could reach, and averaging them
/// with the gaps produces a number the band never exhibits anywhere. Averaging
/// <c>1 - exp(-tau)</c> instead gives the fraction of radiation the band actually stops, which
/// is bounded, physical, and what a broad-band figure should show.
///
/// The columns come from <see cref="EarthlikeConfiguration"/> - observed abundances rather than
/// the fitted absorber recipe - so the optical depths are Earth's. That also means the window
/// appears more open here than in the real atmosphere, because that configuration carries no
/// water-vapour continuum and the continuum is a large part of what closes it.
/// </remarks>
public static class AbsorptionSpectrum
{
    /// <summary>Lower edge of the range plotted, cm^-1.</summary>
    public const double FromWavenumber = 100.0;

    /// <summary>Upper edge, cm^-1.</summary>
    public const double ToWavenumber = 2000.0;

    /// <summary>Lower edge of the atmospheric window, cm^-1.</summary>
    public const double WindowFrom = 800.0;

    /// <summary>Upper edge of the atmospheric window, cm^-1.</summary>
    public const double WindowTo = 1250.0;

    /// <summary>Bin centres, cm^-1.</summary>
    public static double[] Wavenumbers(int bins)
    {
        var nu = new double[bins];
        for (int b = 0; b < bins; b++)
        {
            nu[b] = FromWavenumber + (ToWavenumber - FromWavenumber) * (b + 0.5) / bins;
        }
        return nu;
    }

    /// <summary>
    /// The traces, or null when the HITRAN line lists have not been fetched. The last trace is
    /// every gas together.
    /// </summary>
    /// <remarks>
    /// Costs a line-by-line accumulation per molecule at <paramref name="samples"/> resolution,
    /// so it is slow enough to want running off the UI thread.
    /// </remarks>
    public static IReadOnlyList<AbsorptionTrace>? Compute(
        int bins = 240, int samples = 60_000, double wingCutoff = Co2Sweep.DefaultWingCutoff,
        bool subLorentzianWings = true)
    {
        // Water vapour arrives as two line lists and is one gas, so traces are accumulated by
        // label rather than by file.
        var recipe = new (string Gas, string File, double Column, bool Co2)[]
        {
            ("Water vapour", HitranLineList.WaterVapourRotational,
                EarthlikeConfiguration.WaterColumn(), false),
            ("Water vapour", HitranLineList.WaterVapourBending,
                EarthlikeConfiguration.WaterColumn(), false),
            ("Carbon dioxide", HitranLineList.Co2FifteenMicron,
                EarthlikeConfiguration.WellMixedColumn(EarthlikeConfiguration.Co2Ppm * 1e-6), true),
            ("Ozone", HitranLineList.OzoneNineSixMicron,
                EarthlikeConfiguration.OzoneColumnDensity(), false),
            ("Methane", HitranLineList.MethaneSevenSevenMicron,
                EarthlikeConfiguration.WellMixedColumn(EarthlikeConfiguration.MethanePpb * 1e-9), false),
            ("Nitrous oxide", HitranLineList.NitrousOxideSevenEightMicron,
                EarthlikeConfiguration.WellMixedColumn(EarthlikeConfiguration.NitrousOxidePpb * 1e-9), false),
        };

        var tau = new Dictionary<string, double[]>();
        var order = new List<string>();

        foreach (var (gas, file, column, co2) in recipe)
        {
            string? path = HitranLineList.DefaultPath(file);
            if (path is null) return null;

            var lines = HitranLineList.LoadCached(path, minimumIntensity: 1e-27);
            var band = LineByLineBand.FromLines(lines, FromWavenumber, ToWavenumber, samples,
                wingCutoff, co2 && subLorentzianWings ? ChiFactor.CarbonDioxideNu2 : null);

            var shape = band.AbsorptionCoefficients();
            double crossSection = band.MeanCrossSection;

            if (!tau.TryGetValue(gas, out var acc))
            {
                acc = new double[samples];
                tau[gas] = acc;
                order.Add(gas);
            }

            for (int i = 0; i < samples; i++) acc[i] += shape[i] * crossSection * column;
        }

        var total = new double[samples];
        foreach (var acc in tau.Values)
        {
            for (int i = 0; i < samples; i++) total[i] += acc[i];
        }

        var traces = new List<AbsorptionTrace>(order.Count + 1);
        foreach (string gas in order) traces.Add(new AbsorptionTrace(gas, Bin(tau[gas], bins)));
        traces.Add(new AbsorptionTrace("All gases", Bin(total, bins)));

        return traces;
    }

    /// <summary>Mean absorptivity within each bin - see the remarks on this type.</summary>
    private static double[] Bin(double[] opticalDepth, int bins)
    {
        var binned = new double[bins];
        int per = opticalDepth.Length / bins;

        for (int b = 0; b < bins; b++)
        {
            double sum = 0.0;
            int count = 0;
            for (int i = b * per; i < (b + 1) * per && i < opticalDepth.Length; i++)
            {
                sum += 1.0 - Math.Exp(-opticalDepth[i]);
                count++;
            }
            binned[b] = count > 0 ? sum / count : 0.0;
        }

        return binned;
    }

    /// <summary>Mean absorptivity of a trace between two wavenumbers - for captions.</summary>
    public static double MeanBetween(AbsorptionTrace trace, double from, double to)
    {
        var nu = Wavenumbers(trace.Absorptivity.Count);
        double sum = 0.0;
        int count = 0;

        for (int b = 0; b < nu.Length; b++)
        {
            if (nu[b] < from || nu[b] > to) continue;
            sum += trace.Absorptivity[b];
            count++;
        }

        return count > 0 ? sum / count : 0.0;
    }
}
