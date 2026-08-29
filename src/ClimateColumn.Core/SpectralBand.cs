namespace ClimateColumn.Core;

/// <summary>
/// One spectral band: the wavelengths it covers, what absorbs in it, and how that absorption is
/// distributed across it.
/// </summary>
/// <remarks>
/// Bands are what let different gases act where they actually act. A grey column has one
/// absorber standing in for everything at once, so CO2's 15 um band and water vapour's
/// rotational band cannot both be represented - they have different strengths, different
/// vertical profiles and, as the HITRAN comparison shows, very different line structure. Give
/// each its own band and each can carry its own.
///
/// A band's share of an emitter's radiation is the fraction of that emitter's Planck function
/// inside the interval, so it follows temperature rather than being fixed. Exactly as for the
/// window, this matters: the fraction of a 287 K surface's emission in a band is not the
/// fraction of a 217 K tropopause's.
///
/// One band may instead be the <em>remainder</em>, carrying whatever the others leave. That is
/// needed because "everything except the window" is not itself an interval, and it also
/// guarantees the weights sum to exactly one however the intervals are chosen - which is what
/// keeps energy closure exact.
/// </remarks>
// A record rather than a plain class so that a derived band can be copied with one property
// changed - the concentration sweep needs to clear Co2Fraction on bands whose derivation already
// holds the right amount of CO2, without rebuilding all nine properties by hand.
public sealed record SpectralBand
{
    /// <summary>Short-wavelength edge, m. Zero, with <see cref="LongWavelength"/>, marks the remainder band.</summary>
    public double ShortWavelength { get; init; }

    /// <summary>Long-wavelength edge, m.</summary>
    public double LongWavelength { get; init; }

    /// <summary>A name for reporting: "CO2 15 um", "window", and so on.</summary>
    public string Label { get; init; } = "band";

    /// <summary>
    /// True when this band carries whatever the interval bands do not. At most one band in a set
    /// may be the remainder.
    /// </summary>
    public bool IsRemainder => LongWavelength <= ShortWavelength;

    /// <summary>
    /// Column optical depth in this band from the well-mixed absorber, at
    /// <see cref="ModelOptions.Co2ReferenceConcentration"/>.
    /// </summary>
    public double OpticalDepth { get; init; }

    /// <summary>
    /// Share of <see cref="OpticalDepth"/> that is CO2 and so responds to concentration. The
    /// rest is held fixed. 1.0 by default, which suits a band named for CO2; set it to 0 for a
    /// band where CO2 does nothing.
    /// </summary>
    public double Co2Fraction { get; init; } = 1.0;

    /// <summary>
    /// Share of <see cref="OpticalDepth"/> that is methane, and so responds to
    /// <see cref="ModelOptions.MethaneConcentration"/>. Zero by default.
    /// </summary>
    /// <remarks>
    /// A second share rather than a general table of gases, because two are what the model
    /// actually dials. Both are well-mixed, so both sit inside <see cref="OpticalDepth"/> and
    /// the two shares plus the fixed remainder must sum to one - which is checked, since a band
    /// whose shares oversubscribe it would gain optical depth out of nowhere the moment either
    /// concentration moved.
    ///
    /// Methane earns the second slot because its band behaves differently from CO2's in the way
    /// that matters here: the 7.7 um band is weak and largely unsaturated, so its forcing grows
    /// as the square root of concentration rather than the logarithm. That contrast is a test of
    /// whether the band structure is doing real work, since nothing in the model imposes either
    /// law.
    /// </remarks>
    public double MethaneFraction { get; init; }

    /// <summary>
    /// Column optical depth in this band from water vapour, at
    /// <see cref="ModelOptions.WaterVapourReferenceTemperature"/>. Follows Clausius-Clapeyron
    /// and the vapour scale height, like the single-band vapour absorber.
    /// </summary>
    public double WaterVapourOpticalDepth { get; init; }

    /// <summary>
    /// Column optical depth in this band from the water-vapour continuum, at the reference
    /// temperature. Scales as the vapour squared for the self part and linearly for the foreign
    /// part, so a band carrying continuum closes as the column warms.
    /// </summary>
    public double ContinuumOpticalDepth { get; init; }

    /// <summary>
    /// Column optical depth in this band from ozone, distributed on the same Chapman layer as its
    /// solar heating. Ozone peaks in the stratosphere, so it needs its own profile rather than
    /// borrowing the well-mixed or vapour one.
    /// </summary>
    public double OzoneOpticalDepth { get; init; }

    /// <summary>
    /// Line structure within the band. Null leaves it grey. This is where a distribution
    /// measured from HITRAN belongs - the whole point of banding is that CO2's and water
    /// vapour's differ, and here they can.
    /// </summary>
    public KDistribution? Structure { get; init; }

    /// <summary>Share of an emitter's Planck function inside this band at <paramref name="temperature"/>.</summary>
    public double PlanckShare(double temperature) =>
        IsRemainder ? 0.0 : Planck.FractionBetween(ShortWavelength, LongWavelength, temperature);

    /// <summary>
    /// Effective well-mixed optical depth after the CO2 concentration scaling, exactly as
    /// <see cref="ModelOptions.EffectiveDryOpticalDepth"/> does for the single-band case.
    /// </summary>
    public double EffectiveOpticalDepth(double concentrationRatio) =>
        EffectiveOpticalDepth(concentrationRatio, 1.0);

    /// <summary>
    /// As above, with methane dialled as well. The fixed remainder is whatever neither gas
    /// claims, so a band with no methane in it behaves exactly as it did before methane existed.
    /// </summary>
    public double EffectiveOpticalDepth(double concentrationRatio, double methaneRatio) =>
        OpticalDepth * ((1.0 - Co2Fraction - MethaneFraction)
                        + Co2Fraction * concentrationRatio
                        + MethaneFraction * methaneRatio);

    /// <summary>True when nothing at all absorbs in this band.</summary>
    public bool IsTransparent =>
        OpticalDepth <= 0.0 && WaterVapourOpticalDepth <= 0.0 &&
        ContinuumOpticalDepth <= 0.0 && OzoneOpticalDepth <= 0.0;

    public override string ToString() =>
        IsRemainder
            ? $"{Label} (remainder)"
            : $"{Label} ({ShortWavelength * 1e6:F1}-{LongWavelength * 1e6:F1} um)";
}
