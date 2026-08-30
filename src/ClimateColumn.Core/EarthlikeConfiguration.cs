namespace ClimateColumn.Core;

/// <summary>
/// A spectral configuration built from real atmospheric abundances, with no absorber scale.
/// </summary>
/// <remarks>
/// <strong>What this is for.</strong> The shipped configuration scales every absorber by one
/// common factor chosen so the surface lands at an Earth-like temperature. That factor is a fit,
/// and it absorbs every deficiency in the model at once - missing gases, a tuned continuum,
/// unscaled line strengths, absent collision-induced absorption - so none of them can be seen
/// individually. It also makes the surface temperature an input rather than a result.
///
/// This configuration removes the factor. Each gas gets the column density its observed mixing
/// ratio implies, the optical depth follows from HITRAN's own line strengths, and the surface
/// temperature is whatever comes out.
///
/// <strong>It is expected to be worse at reproducing Earth, and that is the point.</strong> The
/// gap between what it produces and 288 K is a measurement of how much greenhouse effect the
/// model's spectroscopy fails to account for. In the shipped configuration that same gap exists
/// but is invisible, hidden inside a number chosen to make it disappear.
///
/// <strong>Units.</strong> HITRAN line strengths are cm^-1/(molecule cm^-2) and a Lorentz
/// profile integrates to one over wavenumber, so
/// <see cref="LineByLineBand.MeanCrossSection"/> is a band-mean cross-section in cm^2 per
/// molecule. Multiplied by a column density in molecules per cm^2 it gives a dimensionless
/// band-mean optical depth, which is exactly what
/// <see cref="BandDerivation.Molecule.OpticalDepth"/> expects. No free parameter enters.
///
/// <strong>What is still not physical here.</strong> The water-vapour continuum remains a tuned
/// number rather than fitted MT_CKD coefficients, and it is left out entirely rather than
/// smuggled back in with a fresh fudge factor - so the window in this configuration never
/// closes. Line strengths are still used at their 296 K values. There is no collision-induced
/// absorption and no aerosol. Each of those is now a visible omission instead of part of a
/// scale factor.
/// </remarks>
public static class EarthlikeConfiguration
{
    /// <summary>Avogadro's number, per mole.</summary>
    public const double Avogadro = 6.02214076e23;

    /// <summary>Molar mass of dry air, kg per mole.</summary>
    public const double DryAirMolarMass = 0.0289644;

    /// <summary>Molar mass of water, kg per mole.</summary>
    public const double WaterMolarMass = 0.018015;

    /// <summary>One Dobson unit, molecules per cm^2.</summary>
    public const double DobsonUnit = 2.6867e16;

    /// <summary>Pre-industrial CO2, parts per million by volume.</summary>
    public const double Co2Ppm = 285.0;

    /// <summary>Pre-industrial methane, parts per billion by volume.</summary>
    public const double MethanePpb = 700.0;

    /// <summary>Pre-industrial nitrous oxide, parts per billion by volume.</summary>
    public const double NitrousOxidePpb = 270.0;

    /// <summary>Global-mean precipitable water, kg per m^2 - about 25 mm of rain.</summary>
    public const double PrecipitableWater = 25.0;

    /// <summary>Global-mean ozone column, Dobson units.</summary>
    public const double OzoneColumn = 300.0;

    /// <summary>
    /// Molecules of dry air above a square centimetre, from surface pressure and gravity.
    /// </summary>
    /// <remarks>
    /// <c>p/g</c> is the mass of air over unit area whatever the density profile, so this needs
    /// no assumption about the column's structure. At 1013.25 hPa it gives 2.15e25 per cm^2,
    /// and 285 ppm of that is 6.1e21 - the standard figure for a pre-industrial CO2 column.
    /// </remarks>
    public static double AirColumnDensity(double surfacePressure = 101_325.0) =>
        surfacePressure / PhysicalConstants.Gravity / DryAirMolarMass * Avogadro / 1e4;

    /// <summary>Column density of a well-mixed gas at a given volume mixing ratio, per cm^2.</summary>
    public static double WellMixedColumn(double volumeMixingRatio, double surfacePressure = 101_325.0) =>
        volumeMixingRatio * AirColumnDensity(surfacePressure);

    /// <summary>Column density of water vapour, per cm^2, from precipitable water in kg/m^2.</summary>
    public static double WaterColumn(double precipitableWater = PrecipitableWater) =>
        precipitableWater / WaterMolarMass * Avogadro / 1e4;

    /// <summary>Column density of ozone, per cm^2, from a column in Dobson units.</summary>
    public static double OzoneColumnDensity(double dobson = OzoneColumn) => dobson * DobsonUnit;

    /// <summary>
    /// The configuration, or null when the HITRAN line lists have not been fetched.
    /// </summary>
    /// <param name="continuum">
    /// Band-mean optical depth of the water-vapour continuum. Zero by default, and deliberately
    /// so: the shipped model's continuum is a tuned number, and carrying it over would put a
    /// fitted parameter back into a configuration whose whole purpose is not to have one. Set it
    /// only to measure what the continuum is worth.
    /// </param>
    public static Func<double, ModelOptions>? Build(
        int bandCount = 16, int gPoints = 16, int segmentCount = 30, int samples = 80_000,
        double wingCutoff = Co2Sweep.DefaultWingCutoff, bool subLorentzianWings = true, double continuum = 0.0)
    {
        // Column densities from observed abundances. Nothing here is adjustable to taste.
        var recipe = new (string File, AbsorberKind Kind, double Column, bool Co2)[]
        {
            (HitranLineList.WaterVapourRotational, AbsorberKind.WaterVapour, WaterColumn(), false),
            (HitranLineList.WaterVapourBending,    AbsorberKind.WaterVapour, WaterColumn(), false),
            (HitranLineList.Co2FifteenMicron,      AbsorberKind.WellMixed,
                WellMixedColumn(Co2Ppm * 1e-6), true),
            (HitranLineList.OzoneNineSixMicron,    AbsorberKind.Ozone, OzoneColumnDensity(), false),
            (HitranLineList.MethaneSevenSevenMicron, AbsorberKind.WellMixed,
                WellMixedColumn(MethanePpb * 1e-9), false),
            (HitranLineList.NitrousOxideSevenEightMicron, AbsorberKind.WellMixed,
                WellMixedColumn(NitrousOxidePpb * 1e-9), false)
        };

        var molecules = new List<BandDerivation.Molecule>(recipe.Length);

        foreach (var (file, kind, column, co2) in recipe)
        {
            string? path = HitranLineList.DefaultPath(file);
            if (path is null) return null;

            var lines = HitranLineList.LoadCached(path, minimumIntensity: 1e-26);
            var chi = co2 && subLorentzianWings ? ChiFactor.CarbonDioxideNu2 : null;

            // The physical magnitude the normalised path throws away: a band-mean cross-section
            // in cm^2 per molecule, which times a column density in molecules per cm^2 is a
            // dimensionless optical depth.
            var band = LineByLineBand.FromLines(lines, 100.0, 2000.0, samples, wingCutoff, chi);
            double opticalDepth = band.MeanCrossSection * column;

            molecules.Add(new BandDerivation.Molecule(
                lines, kind, opticalDepth, co2, file, chi,
                RespondsToMethane: file == HitranLineList.MethaneSevenSevenMicron));
        }

        var bands = BandDerivation.DeriveShared(
            molecules, fromWavenumber: 100, toWavenumber: 2000, bandCount: bandCount,
            samples: samples, gPoints: gPoints, wingCutoff: wingCutoff,
            continuumOpticalDepth: continuum);

        return ppm => new ModelOptions
        {
            Co2Concentration = ppm,
            Co2ReferenceConcentration = Co2Ppm,
            SegmentCount = segmentCount,
            Bands = bands.ToArray(),

            // No WaterVapourOpticalDepth: the vapour is in the bands at its observed column, so
            // adding the single-band absorber on top would count it twice. That also means the
            // vapour here does not follow temperature - a fixed column rather than a feedback,
            // which is the honest reading of "put the observed amount in".
            OzoneFraction = 0.3
        };
    }

    /// <summary>
    /// The optical depth each gas contributes, for reporting what the configuration is made of.
    /// </summary>
    public static IReadOnlyList<(string Gas, double Column, double OpticalDepth)>? Inventory(
        int samples = 80_000, double wingCutoff = Co2Sweep.DefaultWingCutoff, bool subLorentzianWings = true)
    {
        var rows = new List<(string, double, double)>();

        foreach (var (file, column, co2) in new (string, double, bool)[]
        {
            (HitranLineList.WaterVapourRotational, WaterColumn(), false),
            (HitranLineList.WaterVapourBending, WaterColumn(), false),
            (HitranLineList.Co2FifteenMicron, WellMixedColumn(Co2Ppm * 1e-6), true),
            (HitranLineList.OzoneNineSixMicron, OzoneColumnDensity(), false),
            (HitranLineList.MethaneSevenSevenMicron, WellMixedColumn(MethanePpb * 1e-9), false),
            (HitranLineList.NitrousOxideSevenEightMicron, WellMixedColumn(NitrousOxidePpb * 1e-9), false)
        })
        {
            string? path = HitranLineList.DefaultPath(file);
            if (path is null) return null;

            var lines = HitranLineList.LoadCached(path, minimumIntensity: 1e-26);
            var band = LineByLineBand.FromLines(lines, 100.0, 2000.0, samples, wingCutoff,
                co2 && subLorentzianWings ? ChiFactor.CarbonDioxideNu2 : null);

            rows.Add((file, column, band.MeanCrossSection * column));
        }

        return rows;
    }
}
