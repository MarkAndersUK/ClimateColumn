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
    WaterVapour
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

    /// <summary>Planck share of a sub-band at a reference temperature.</summary>
    private static double ShareOf(LineByLineBand.DerivedSubBand piece, double temperature) =>
        Planck.FractionBetween(
            Wavelength(piece.ToWavenumber), Wavelength(piece.FromWavenumber), temperature);

    /// <summary>Wavelength in metres for a wavenumber in cm^-1.</summary>
    private static double Wavelength(double wavenumber) => 0.01 / wavenumber;
}
