namespace ClimateColumn.Core;

/// <summary>Which absorber a derived band's opacity belongs to, and so which vertical profile.</summary>
public enum AbsorberKind
{
    /// <summary>A well-mixed gas, following air density. CO2 and friends.</summary>
    WellMixed,

    /// <summary>
    /// Water vapour, following its own scale height and the Clausius-Clapeyron scaling on the
    /// near-surface air temperature.
    /// </summary>
    WaterVapour,

    /// <summary>
    /// Ozone, following the Chapman layer already used for its solar heating. Ozone is neither
    /// well mixed nor bottom-heavy - it peaks in the stratosphere - so giving it either of the
    /// other profiles would put its 9.6 um absorption in the wrong place entirely.
    /// </summary>
    Ozone
}

/// <summary>
/// Builds a spectral band set from resolved line data instead of by hand.
/// </summary>
/// <remarks>
/// Banding the spectrum by hand means choosing where the bands sit, how opaque each one is
/// relative to the others, and what its line structure looks like - three guesses per band. Given
/// a line list, all three follow from the data: boundaries from the Planck function, relative
/// opacity from the mean absorption measured inside each band, and structure from the measured
/// distribution of that absorption.
///
/// One free parameter remains, and it should: how much gas there is. That is concentration, not
/// spectroscopy, so <c>opticalDepth</c> below sets the Planck-weighted mean optical depth across
/// the derived bands and the relative pattern from the data is scaled to match.
/// </remarks>
public static class BandDerivation
{
    /// <summary>
    /// Derives bands for one gas from its resolved spectrum.
    /// </summary>
    /// <param name="spectrum">The resolved spectrum, covering the gas's range.</param>
    /// <param name="bandCount">How many bands to carve out of it.</param>
    /// <param name="opticalDepth">
    /// Planck-weighted mean column optical depth across the derived bands, at
    /// <paramref name="referenceTemperature"/>. This is the one thing not taken from the data.
    /// </param>
    /// <param name="kind">Which absorber the opacity belongs to, hence its vertical profile.</param>
    /// <param name="label">Prefix for the band labels.</param>
    /// <param name="gPoints">g-points per band.</param>
    /// <param name="strategy">How to place the boundaries.</param>
    /// <param name="referenceTemperature">
    /// Temperature at which the Planck weighting is evaluated, both for placing boundaries and
    /// for normalising the optical depth. Around 260 K suits an emitting atmosphere.
    /// </param>
    public static IReadOnlyList<SpectralBand> Derive(
        LineByLineBand spectrum,
        int bandCount,
        double opticalDepth,
        AbsorberKind kind,
        string label,
        int gPoints = 16,
        LineByLineBand.SubdivisionStrategy strategy = LineByLineBand.SubdivisionStrategy.EqualPlanckEnergy,
        double referenceTemperature = 260.0)
    {
        if (opticalDepth < 0) throw new ArgumentException("opticalDepth must be >= 0.");

        var pieces = spectrum.Subdivide(bandCount, gPoints, strategy, referenceTemperature);

        // Normalise so the Planck-weighted mean optical depth is what was asked for. Weighting by
        // Planck share rather than by band width is what makes the knob mean something radiative:
        // a band that carries little of the emission should not pull the average around. Dividing
        // by the total share keeps it a mean rather than a sum, so the number means the same thing
        // however much of the spectrum the line data happens to cover.
        double weighted = 0.0, shares = 0.0;
        foreach (var piece in pieces)
        {
            double share = ShareOf(piece, referenceTemperature);
            shares += share;
            weighted += share * piece.RelativeStrength;
        }

        double scale = weighted > 0 ? opticalDepth * shares / weighted : 0.0;

        var bands = new List<SpectralBand>(pieces.Count);
        for (int b = 0; b < pieces.Count; b++)
        {
            var piece = pieces[b];
            double depth = scale * piece.RelativeStrength;

            bands.Add(new SpectralBand
            {
                Label = $"{label} {b + 1}",

                // Wavenumbers run the opposite way to wavelengths, so the upper wavenumber edge
                // is the shorter wavelength.
                ShortWavelength = Wavelength(piece.ToWavenumber),
                LongWavelength = Wavelength(piece.FromWavenumber),

                OpticalDepth = kind == AbsorberKind.WellMixed ? depth : 0.0,
                Co2Fraction = kind == AbsorberKind.WellMixed ? 1.0 : 0.0,
                WaterVapourOpticalDepth = kind == AbsorberKind.WaterVapour ? depth : 0.0,

                Structure = piece.Structure
            });
        }

        return bands;
    }

    /// <summary>
    /// Combines several derived sets, plus a remainder band for everything they do not cover.
    /// </summary>
    /// <remarks>
    /// Two gases derived from their own line lists will cover two disjoint stretches of the
    /// spectrum, leaving most of it undescribed. The remainder carries whatever opacity you want
    /// to attribute to the gases and bands not modelled; give it zero and the rest of the spectrum
    /// is simply transparent.
    /// </remarks>
    public static IReadOnlyList<SpectralBand> Combine(
        double remainderOpticalDepth,
        params IReadOnlyList<SpectralBand>[] sets)
    {
        var combined = new List<SpectralBand>();
        foreach (var set in sets) combined.AddRange(set);

        combined.Sort((a, b) => a.ShortWavelength.CompareTo(b.ShortWavelength));

        combined.Add(new SpectralBand
        {
            Label = "remainder",
            OpticalDepth = remainderOpticalDepth,
            Co2Fraction = 0.0
        });

        return combined;
    }

    /// <summary>One gas going into a shared band derivation.</summary>
    /// <param name="Lines">Its line list.</param>
    /// <param name="Kind">Which vertical profile its opacity follows.</param>
    /// <param name="OpticalDepth">
    /// Planck-weighted mean column optical depth for this gas across the whole derived range.
    /// </param>
    /// <param name="RespondsToCo2">
    /// True for CO2, so that the bands it dominates respond to concentration and the others do not.
    /// </param>
    /// <param name="Label">A name, for reporting.</param>
    public readonly record struct Molecule(
        IReadOnlyList<SpectralLine> Lines,
        AbsorberKind Kind,
        double OpticalDepth,
        bool RespondsToCo2,
        string Label);

    /// <summary>
    /// Derives one band set covering several molecules at once, on a shared wavenumber grid.
    /// </summary>
    /// <remarks>
    /// Deriving each gas separately produces sets that overlap - N2O's 7.8 um band sits inside
    /// methane's, and water vapour is everywhere - and overlapping bands are rejected, rightly,
    /// since each would claim its own share of every emitter's Planck function. Putting every
    /// molecule on one grid solves that: the bands partition the range once, and each band records
    /// how much opacity each gas contributes to it.
    ///
    /// A band's opacity is then split by vertical profile, so the well-mixed gases, water vapour
    /// and ozone each keep their own, and its <see cref="SpectralBand.Co2Fraction"/> is CO2's share
    /// of that band's well-mixed opacity - so a band CO2 dominates responds fully to concentration
    /// and one dominated by methane barely responds. Nothing about that split is chosen; it comes
    /// out of the line strengths.
    ///
    /// The one real approximation is gas overlap. A band's k-distribution is measured from the
    /// <em>total</em> absorption of all the gases in it at their reference amounts, so moving one
    /// gas far from its reference degrades the distribution while leaving its optical depth
    /// correct. This is the classic difficulty in correlated-k and it is not solved here, only
    /// bounded: re-derive if a concentration moves by more than a factor of a few.
    /// </remarks>
    /// <param name="continuumOpticalDepth">
    /// Column optical depth of the water-vapour continuum, spread across the bands overlapping
    /// <paramref name="continuumFrom"/> to <paramref name="continuumTo"/> in proportion to how much
    /// of that interval each covers.
    /// </param>
    /// <param name="continuumFrom">Lower edge of the continuum's range, cm^-1.</param>
    /// <param name="continuumTo">Upper edge, cm^-1.</param>
    /// <remarks>
    /// The continuum is not optional in any physical sense. Line data alone leaves the atmospheric
    /// window perfectly transparent, which caps the greenhouse effect however much gas is added -
    /// reaching an Earth-like surface temperature from lines alone needs absorber amounts about
    /// twenty-five times larger than anything reasonable, because the window simply lets the
    /// radiation out. What closes it over the humid tropics is the continuum, and HITRAN's line
    /// lists do not contain it: it is smooth absorption between the lines, described separately.
    /// So it has to be added here rather than derived, and its default range is the window itself.
    /// </remarks>
    public static IReadOnlyList<SpectralBand> DeriveShared(
        IReadOnlyList<Molecule> molecules,
        double fromWavenumber,
        double toWavenumber,
        int bandCount,
        int samples = 120_000,
        int gPoints = 16,
        double wingCutoff = 25.0,
        double referenceTemperature = 260.0,
        double continuumOpticalDepth = 0.0,
        double continuumFrom = 800.0,
        double continuumTo = 1250.0)
    {
        if (molecules.Count == 0) throw new ArgumentException("at least one molecule is needed.");
        if (bandCount < 1) throw new ArgumentException("bandCount must be >= 1.");

        // Every gas on the same grid, so their absorption can be added sample by sample.
        var spectra = new List<(Molecule Molecule, double[] Tau)>(molecules.Count);
        LineByLineBand? reference = null;

        foreach (var molecule in molecules)
        {
            var band = LineByLineBand.FromLines(
                molecule.Lines, fromWavenumber, toWavenumber, samples, wingCutoff);
            reference ??= band;

            // AbsorptionCoefficients normalises to a mean of one over the whole range, so scaling
            // by the requested depth makes the mean exactly that depth.
            var tau = band.AbsorptionCoefficients();
            for (int i = 0; i < tau.Length; i++) tau[i] *= molecule.OpticalDepth;

            spectra.Add((molecule, tau));
        }

        double[] edges = PlanckEdges(fromWavenumber, toWavenumber, bandCount, referenceTemperature);
        double step = (toWavenumber - fromWavenumber) / samples;

        // How much of the continuum's range each band covers, so the continuum can be shared out
        // among the bands that overlap it.
        double continuumSpan = Math.Max(0.0, continuumTo - continuumFrom);
        var continuumWeight = new double[bandCount];
        if (continuumOpticalDepth > 0 && continuumSpan > 0)
        {
            for (int b = 0; b < bandCount; b++)
            {
                double overlap = Math.Min(edges[b + 1], continuumTo) - Math.Max(edges[b], continuumFrom);
                continuumWeight[b] = Math.Max(0.0, overlap) / continuumSpan;
            }
        }

        var bands = new List<SpectralBand>(bandCount);

        for (int b = 0; b < bandCount; b++)
        {
            int from = Math.Clamp((int)Math.Ceiling((edges[b] - fromWavenumber) / step), 0, samples);
            int to = Math.Clamp((int)Math.Ceiling((edges[b + 1] - fromWavenumber) / step), from + 1, samples);
            int width = to - from;

            double wellMixed = 0.0, vapour = 0.0, ozone = 0.0, carbon = 0.0;

            foreach (var (molecule, tau) in spectra)
            {
                double mean = 0.0;
                for (int i = from; i < to; i++) mean += tau[i];
                mean /= width;

                switch (molecule.Kind)
                {
                    case AbsorberKind.WellMixed: wellMixed += mean; break;
                    case AbsorberKind.WaterVapour: vapour += mean; break;
                    case AbsorberKind.Ozone: ozone += mean; break;
                }

                if (molecule.RespondsToCo2) carbon += mean;
            }

            // The band's structure is the distribution of the total absorption in it, relative to
            // that total's own mean.
            double total = wellMixed + vapour + ozone;
            var slice = new double[width];
            for (int i = from; i < to; i++)
            {
                double sum = 0.0;
                foreach (var (_, tau) in spectra) sum += tau[i];
                slice[i - from] = total > 0 ? sum / total : 1.0;
            }
            Array.Sort(slice);

            bands.Add(new SpectralBand
            {
                Label = $"{edges[b]:F0}-{edges[b + 1]:F0} cm-1",

                // Wavenumbers run the opposite way to wavelengths.
                ShortWavelength = Wavelength(edges[b + 1]),
                LongWavelength = Wavelength(edges[b]),

                OpticalDepth = wellMixed,
                Co2Fraction = wellMixed > 0 ? Math.Clamp(carbon / wellMixed, 0.0, 1.0) : 0.0,
                WaterVapourOpticalDepth = vapour,
                OzoneOpticalDepth = ozone,
                ContinuumOpticalDepth = continuumOpticalDepth * continuumWeight[b],

                Structure = reference!.QuadratureFrom(slice, Math.Min(gPoints, width))
            });
        }

        return bands;
    }

    /// <summary>
    /// Band edges in cm^-1 placing an equal share of the Planck function in each, by bisecting the
    /// cumulative emission. Duplicated from the single-gas path because that one works from a
    /// spectrum's own range while this one is told the range directly.
    /// </summary>
    private static double[] PlanckEdges(double from, double to, int count, double temperature)
    {
        var edges = new double[count + 1];
        edges[0] = from;
        edges[count] = to;

        double Cumulative(double wavenumber) =>
            Planck.FractionBelow(Wavelength(from) * temperature) -
            Planck.FractionBelow(Wavelength(wavenumber) * temperature);

        double total = Cumulative(to);
        if (total <= 0)
        {
            for (int b = 1; b < count; b++) edges[b] = from + (to - from) * b / count;
            return edges;
        }

        for (int b = 1; b < count; b++)
        {
            double target = total * b / count;
            double low = from, high = to;

            for (int iteration = 0; iteration < 80; iteration++)
            {
                double middle = 0.5 * (low + high);
                if (Cumulative(middle) < target) low = middle; else high = middle;
            }

            edges[b] = 0.5 * (low + high);
        }

        return edges;
    }

    /// <summary>Planck share of a sub-band at a reference temperature.</summary>
    private static double ShareOf(LineByLineBand.DerivedSubBand piece, double temperature) =>
        Planck.FractionBetween(
            Wavelength(piece.ToWavenumber), Wavelength(piece.FromWavenumber), temperature);

    /// <summary>Wavelength in metres for a wavenumber in cm^-1.</summary>
    private static double Wavelength(double wavenumber) => 0.01 / wavenumber;
}
