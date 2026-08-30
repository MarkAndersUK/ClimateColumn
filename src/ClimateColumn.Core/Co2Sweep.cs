namespace ClimateColumn.Core;

/// <summary>One equilibrium point of a concentration sweep.</summary>
public sealed record Co2Point(double Ppm, double DryOpticalDepth, double SurfaceTemperature, bool Converged);

/// <summary>
/// A CO2 concentration sweep of one model configuration, together with the logarithmic
/// response it would have if its forcing followed the accepted law.
/// </summary>
/// <remarks>
/// Forcings here are measured against the <em>fixed</em> reference equilibrium, not against
/// the previous step. That is the only definition comparable to 5.35 ln(C/C0); the stepwise
/// forcings the CLI prints are each taken against the previous (warmer) equilibrium and must
/// not be summed and held against the logarithmic law.
/// </remarks>
public sealed class Co2Sweep
{
    /// <summary>Concentrations swept, ppm. The reference is first.</summary>
    public static readonly double[] Concentrations =
        { 285, 350, 425, 500, 580, 600, 700, 800, 900, 1000 };

    /// <summary>
    /// A concentration worth calling out on the figure, ppm. Just above twice the 285 ppm
    /// reference, so it is close to the doubling the accepted forcing law is usually quoted for.
    /// </summary>
    public const double HighlightPpm = 580.0;

    /// <summary>Index of <see cref="HighlightPpm"/>, looked up rather than hard-coded.</summary>
    public static readonly int HighlightIndex = Array.IndexOf(Concentrations, HighlightPpm);

    /// <summary>The concentration both configurations are calibrated at, ppm.</summary>
    public const double CalibrationPpm = 425.0;

    /// <summary>
    /// Index of <see cref="CalibrationPpm"/> in the sweep. Looked up rather than hard-coded
    /// so the sweep range can change without silently pointing at the wrong point.
    /// </summary>
    public static readonly int CalibrationIndex = Array.IndexOf(Concentrations, CalibrationPpm);

    /// <summary>Accepted CO2 forcing coefficient, W m^-2 per ln(C/C0).</summary>
    public const double AcceptedForcingCoefficient = 5.35;

    /// <summary>Cloud fraction the cloudy spectral configuration is calibrated at.</summary>
    public const double CalibratedCloudFraction = 0.67;

    /// <summary>
    /// Methane's share of the absorber recipe, chosen so its present-day forcing matches
    /// observation.
    /// </summary>
    /// <remarks>
    /// <strong>This is a calibration, and it changes what the methane sweep can claim.</strong>
    /// The share was 0.2, picked to sit sensibly among the other gases and never checked against
    /// anything. At that loading the pre-industrial to present-day rise forced 3.42 W m^-2 against
    /// an accepted 0.62, because the recipe sets relative amounts rather than observed abundances.
    ///
    /// Solved jointly with <see cref="CalibratedAbsorberScale"/> and <em>in the re-derived
    /// mode</em>, which is what <see cref="MethaneSweep"/> now uses: the share sets the forcing,
    /// the scale sets the base state, and cutting methane removes absorber, so neither can be
    /// chosen alone. Three passes converge on 0.00687 and 17.7622 with the base state held at
    /// 286.7964 K and the forcing on 0.6167 W m^-2 against 0.6167 accepted.
    ///
    /// Calibrated in one mode does not mean calibrated in the other. Scaling the band share
    /// instead of re-deriving gives a different magnitude at this share, which is why the two are
    /// not interchangeable.
    ///
    /// Setting this means methane's forcing <em>magnitude</em> is no longer a prediction. Its
    /// shape still is, and with re-derivation the shape comes out right: sqrt(M), the observed
    /// law. The model does not predict atmospheric composition and never claimed to - CO2's amount
    /// is calibrated too, through the absorber scale. The difference is that CO2's scale is set by
    /// the base state and its forcing coefficient then comes out as a genuine prediction, whereas
    /// methane's is set by the forcing directly. Worth knowing when reading the two figures side
    /// by side.
    /// </remarks>
    public const double CalibratedMethaneShare = 0.00687;

    /// <summary>
    /// Absorber scale that puts the spectral configuration's base state at an Earth-like
    /// surface, for a given cloud fraction.
    /// </summary>
    /// <remarks>
    /// There are two, and there have to be. A cloud deck is a greenhouse agent in its own
    /// right, so a gas loading calibrated without one supplies that greenhouse twice when a
    /// deck is added, and the column runs about 15 K hot. Both values here put the base state
    /// at the same 286.796 K, reached different ways: the cloud-free column does the whole
    /// greenhouse with gas, the cloudy one hands a third of it to the deck.
    ///
    /// Anything comparing the two must go through here rather than reusing one scale, or it
    /// will be comparing two different planets and calling the difference a cloud effect.
    /// </remarks>
    public static double CalibratedAbsorberScale(double cloudFraction) =>
        cloudFraction > 0.0 ? 5.6982 : 17.7622;

    /// <summary>
    /// The sweeps a chart should show: the spectrally derived configuration alone, or nothing
    /// when the HITRAN line lists have not been fetched.
    /// </summary>
    /// <remarks>
    /// One place decides this, because it previously did not. A calibrated grey curve beside the
    /// spectral one invited the figure to be read as a comparison of two models rather than as one
    /// model against the forcing law it ought to follow, so it was removed - but only from the
    /// HTML artifact. The WinForms app and the PNG export went on building all three sweeps for
    /// several commits, because each surface chose its own list. They now all call this.
    ///
    /// Returning empty rather than falling back to the grey configurations is deliberate: a
    /// caller with no line data should say so, not quietly draw something else.
    /// </remarks>
    /// <param name="clouds">
    /// Draw the cloudy configuration instead of the cloud-free one. Both sit at the same
    /// 286.796 K base state - see <see cref="CalibratedAbsorberScale"/> - so switching between
    /// them shows what the deck does rather than what a 15 K jump in base state does.
    /// </param>
    public static Co2Sweep[] ForChart(bool clouds = false)
    {
        double fraction = clouds ? CalibratedCloudFraction : 0.0;

        var withFeedback = SpectralBands(cloudFraction: fraction);
        if (withFeedback is null) return Array.Empty<Co2Sweep>();

        // The same configuration with the vapour held at its reference loading. On the forcing
        // panel the two curves coincide - instantaneous forcing is measured at held temperatures,
        // so the feedback cannot act on it - and that coincidence is worth showing, because it is
        // the cleanest demonstration that a feedback changes the response and not the forcing.
        // They separate on the temperature panel.
        var fixedVapour = SpectralBands(
            waterVapourFeedback: false,
            fixedVapourTemperature: withFeedback.BaseAirTemperature,
            cloudFraction: fraction);

        return fixedVapour is null
            ? new[] { withFeedback }
            : new[] { withFeedback, fixedVapour };
    }

    public required string Label { get; init; }
    public required string Command { get; init; }
    public required IReadOnlyList<Co2Point> Points { get; init; }

    /// <summary>Instantaneous forcing at each concentration, against the reference state.</summary>
    public required IReadOnlyList<double> Forcings { get; init; }

    /// <summary>
    /// The vertical state at each concentration, in the same order as <see cref="Points"/>.
    /// </summary>
    /// <remarks>
    /// Not required, and empty on a sweep built by hand: a caller that only wants the response
    /// curve should not have to invent thirty levels per concentration to get one. Anything
    /// drawing a profile must therefore check for emptiness rather than assume.
    /// </remarks>
    public IReadOnlyList<ColumnProfile> Profiles { get; init; } = Array.Empty<ColumnProfile>();

    /// <summary>
    /// Near-surface air temperature of the reference equilibrium, K. Used to freeze a
    /// no-feedback counterpart at exactly this state so the two share a base.
    /// </summary>
    public double BaseAirTemperature { get; init; }

    /// <summary>
    /// Fraction of the sky the run carried cloud over, 0 for a clear-sky configuration.
    /// </summary>
    /// <remarks>
    /// Read off the options the sweep actually ran with rather than passed in beside them, so a
    /// figure captioned "67 % cloud" cannot end up describing a column that had none. The label
    /// already carries the same fact in words, but a caption should not be produced by parsing
    /// prose.
    /// </remarks>
    public double CloudFraction { get; init; }

    public double ReferencePpm => Concentrations[0];
    public double BaseTemperature => Points[0].SurfaceTemperature;

    /// <summary>
    /// The configuration's own climate sensitivity, K per W m^-2, measured at the
    /// calibration point. Taken from the model rather than assumed, so the expectation
    /// curve tracks the model if the configuration changes.
    /// </summary>
    public double Sensitivity =>
        (Points[CalibrationIndex].SurfaceTemperature - BaseTemperature) / Forcings[CalibrationIndex];

    /// <summary>Accepted forcing at index <paramref name="i"/>, W m^-2.</summary>
    public double AcceptedForcing(int i) =>
        AcceptedForcingCoefficient * Math.Log(Concentrations[i] / ReferencePpm);

    /// <summary>
    /// Surface temperature the configuration would reach if its forcing were the accepted
    /// logarithmic one, at its own sensitivity. This is the curve the model ought to follow.
    /// </summary>
    public double Expected(int i) => BaseTemperature + Sensitivity * AcceptedForcing(i);

    /// <summary>Warming from the reference to index <paramref name="i"/>, K.</summary>
    public double Warming(int i) => Points[i].SurfaceTemperature - BaseTemperature;

    /// <summary>Gap between the model and the logarithmic expectation at index i, K.</summary>
    public double Overshoot(int i) => Points[i].SurfaceTemperature - Expected(i);

    /// <summary>Runs a configuration across every concentration and measures its forcings.</summary>
    public static Co2Sweep Run(string label, string command, Func<ModelOptions> configure) =>
        Run(label, command, ppm =>
        {
            var options = configure();
            options.Co2Concentration = ppm;
            return options;
        });

    /// <summary>
    /// As above, but the configuration is built per concentration rather than once and then had
    /// its CO2 dialled in.
    /// </summary>
    /// <remarks>
    /// This exists for the spectral configuration, where the difference matters. A band carries
    /// two things that depend on how much CO2 is present: its mean optical depth, which
    /// <see cref="SpectralBand.OpticalDepthAt"/> scales correctly with concentration, and its
    /// k-distribution, which does not scale at all - it is measured from a resolved spectrum at
    /// whatever amounts it was derived with. Dialling CO2 up therefore stretches the mean while
    /// leaving the distribution describing a different atmosphere.
    ///
    /// Giving the caller the concentration lets it re-derive instead of extrapolate.
    /// </remarks>
    public static Co2Sweep Run(string label, string command, Func<double, ModelOptions> configure)
    {
        var reference = ColumnModel.RunToEquilibrium(configure(Concentrations[0]));

        int n = Concentrations.Length;
        var points = new Co2Point[n];
        var forcings = new double[n];
        var profiles = new ColumnProfile[n];

        // Every concentration above the reference is independent of every other: each builds its
        // own column, marches it, and measures its forcing against the one reference state. So
        // they run concurrently. On a 32-core machine this takes the pair of sweeps the charts
        // need from about 217 seconds to about 55.
        //
        // This is a scheduling change and not a numerical one. Results are written to fixed
        // indices rather than appended, so the output does not depend on completion order, and
        // each march touches only state it allocated itself. The one piece of shared state that
        // is written during a sweep is the band cache used when re-deriving, which is now a
        // concurrent dictionary.
        //
        // The reference march stays sequential because everything else needs its result.
        void At(int i)
        {
            var options = configure(Concentrations[i]);
            var result = i == 0 ? reference : ColumnModel.RunToEquilibrium(options);

            points[i] = new Co2Point(
                Concentrations[i], options.EffectiveDryOpticalDepth,
                result.SurfaceTemperature, result.Converged);

            // Snapshotted before ForcingFrom runs. That call copies temperatures out of this
            // result's column into a column of its own, so it does not disturb this one - but
            // taking the snapshot first makes that an ordering fact about this code rather than
            // a standing assumption about someone else's.
            profiles[i] = ColumnProfile.From(result, label, Concentrations[i]);

            // Reads the reference column; never writes to it. That is what makes the shared
            // baseline safe to hand to every thread at once.
            forcings[i] = ForcingFrom(reference, options);
        }

        At(0);
        Parallel.For(1, n, At);

        return new Co2Sweep
        {
            Label = label, Command = command, Points = points, Forcings = forcings,
            Profiles = profiles, BaseAirTemperature = reference.NearSurfaceAirTemperature,
            CloudFraction = reference.Column.Options.CloudFraction
        };
    }

    /// <summary>
    /// The forcing curve alone, without equilibrating at every concentration.
    /// </summary>
    /// <remarks>
    /// Instantaneous forcing is defined at <em>held</em> temperatures, so it needs one radiation
    /// solve per concentration and one equilibrium march in total - the reference state. A full
    /// <see cref="Run"/> marches to equilibrium nine times because it also wants the temperature
    /// response, which a study of the forcing law does not.
    ///
    /// That is a ninefold saving, and it is what makes a resolution-convergence study affordable
    /// at 32 or 64 g-points, where the equilibrium march is the whole cost.
    /// </remarks>
    public static IReadOnlyList<double> ForcingCurve(Func<double, ModelOptions> configure)
    {
        var reference = ColumnModel.RunToEquilibrium(configure(Concentrations[0]));

        var forcings = new double[Concentrations.Length];
        for (int i = 0; i < Concentrations.Length; i++)
        {
            forcings[i] = ForcingFrom(reference, configure(Concentrations[i]));
        }
        return forcings;
    }

    /// <summary>
    /// Instantaneous forcing, W m^-2: the drop in outgoing longwave when the absorber is
    /// changed to <paramref name="perturbed"/> while the temperatures stay at
    /// <paramref name="baseline"/>'s equilibrium. Redistributing after the temperatures are
    /// copied re-evaluates any water vapour at the baseline state, keeping the feedback out
    /// of the forcing.
    /// </summary>
    private static double ForcingFrom(ModelResult baseline, ModelOptions perturbed)
    {
        var held = Column.Build(perturbed);
        for (int i = 0; i < held.Count; i++)
            held.Segments[i].Temperature = baseline.Column.Segments[i].Temperature;
        held.SurfaceTemperature = baseline.SurfaceTemperature;
        held.DistributeOpticalDepth();

        // Both sides must see the same sky. The baseline's outgoing longwave is the all-sky
        // result - clear and cloudy blended by cloud fraction - so the perturbed side has to be
        // blended too. Calling the solver directly returns the fully cloudy column, and
        // differencing that against an all-sky baseline is not a forcing at all: it is the
        // difference between two different planets, and at the reference concentration, where
        // the forcing is zero by construction, it does not even come out zero.
        //
        // Cloud-free this is the same single solve it always was, which is why nothing caught
        // it until clouds were switched on.
        return baseline.Radiation.OutgoingLongwave -
               ColumnModel.SolveSky(held).AllSky.OutgoingLongwave;
    }

    /// <summary>The configuration used for the README's no-feedback calibration.</summary>
    public static Co2Sweep NoFeedback() => Run(
        "No vapour feedback",
        "--co2-fraction 0.06",
        () => new ModelOptions { Co2AbsorberFraction = 0.06 });

    /// <summary>
    /// A configuration driven by spectral bands derived from HITRAN line data, or null when the
    /// line lists have not been fetched.
    /// </summary>
    /// <remarks>
    /// The other two configurations are the calibrated single-band absorber: one grey optical depth
    /// standing in for the whole longwave spectrum, with a CO2 share tuned until the forcing matched
    /// an accepted value. This one instead puts six molecules into twelve bands derived from their
    /// own line strengths, each band carrying its own opacity, vertical profile and measured line
    /// structure - so its CO2 response comes out of the spectroscopy rather than being calibrated
    /// in.
    ///
    /// Two honest caveats. The absorber amounts are scaled to reach an Earth-like present-day
    /// surface temperature, exactly as the other two configurations were, so the three are
    /// comparable in base state and differ in how they represent absorption. And the continuum is
    /// added rather than derived: HITRAN's line lists do not contain it, and without it the derived
    /// window is perfectly transparent and caps the greenhouse effect no matter how much gas is
    /// added.
    ///
    /// Returns null rather than throwing so the charts can simply show two curves when the data is
    /// absent.
    /// </remarks>
    /// <summary>
    /// The spectrally derived sweep.
    /// </summary>
    /// <param name="rederive">
    /// Re-derive the bands at every concentration rather than deriving once at the reference and
    /// scaling. See the remarks - this is the difference between the response being logarithmic
    /// and merely nearly so.
    /// </param>
    /// <remarks>
    /// A band carries two concentration-dependent things, and only one of them scales. Its mean
    /// optical depth scales exactly, through <see cref="SpectralBand.OpticalDepthAt"/>. Its
    /// k-distribution does not scale at all: it is measured from a resolved spectrum, and the
    /// spectrum it was measured from had a particular amount of CO2 in it. Deriving once and then
    /// dialling CO2 up stretches the mean while the distribution goes on describing the reference
    /// atmosphere - which is exactly what the project's own notes on gas overlap warn against.
    ///
    /// Re-deriving costs a full band derivation per concentration. That is the expensive part of
    /// the sweep, but it is not in any inner loop: the bands are built once per equilibrium run,
    /// not once per timestep.
    ///
    /// The resolution parameters are arguments so that convergence can be measured rather than
    /// assumed, and the defaults are what that measurement settled on. The study is recorded in
    /// artifacts/convergence-study.txt; its conclusions were not what was expected, so they are
    /// worth stating.
    ///
    /// <strong>The wing cutoff is the parameter that matters most</strong>, which in hindsight
    /// follows from where the logarithm comes from: the far wings. Truncating them at 15 cm^-1,
    /// as this configuration originally did, discards exactly the part of the spectrum that makes
    /// the response logarithmic. Widening to 400 cm^-1 converges the forcing coefficient; 800
    /// moves it a further 1%.
    ///
    /// <strong>The old 8 bands x 4 g-points at a 15 cm^-1 cutoff got the right answer by
    /// cancellation.</strong> It reported A = 6.994 against a converged 6.9-7.1, but only because
    /// its truncated wings and coarse band split compensated. Widening the cutoff alone took it
    /// to 9.35, badly wrong - the two errors had to move together. That fragility, not the value,
    /// is why the defaults changed.
    ///
    /// <strong>The absorber scale is resolution dependent.</strong> It exists to put the base
    /// state at an Earth-like surface temperature, and the 13.0 that did so at the old resolution
    /// leaves the surface 2.4 K too cold here; 14.5781 restores it. Changing resolution without
    /// re-calibrating changes two things at once - see SpectralCalibrationTests, which bisects it.
    /// </remarks>
    public static Co2Sweep? SpectralBands(
        int bandCount = 16, int gPoints = 16, int segmentCount = 30, int samples = 80_000,
        bool rederive = false, double wingCutoff = 400.0, double absorberScale = double.NaN,
        bool waterVapourFeedback = true, double? fixedVapourTemperature = null,
        bool subLorentzianWings = true, double cloudFraction = 0.0,
        double methaneShare = CalibratedMethaneShare, double methaneRatio = 1.0)
    {
        var configure = SpectralConfiguration(bandCount, gPoints, segmentCount, samples, rederive,
            wingCutoff, absorberScale, waterVapourFeedback, fixedVapourTemperature,
            subLorentzianWings, cloudFraction, methaneShare);
        if (configure is null) return null;

        string sky = cloudFraction > 0.0 ? $" under {cloudFraction:P0} cloud" : "";

        return Run(
            waterVapourFeedback
                ? "Derived from HITRAN bands" + sky
                : "Same bands, water vapour held fixed" + sky,
            $"see Co2Sweep.SpectralBands - 6 molecules, {bandCount} derived bands, {gPoints} g-points" +
            (rederive ? ", re-derived per concentration" : "") +
            (waterVapourFeedback ? "" : ", water vapour frozen at the base state") +
            (cloudFraction > 0.0 ? $", {cloudFraction:P0} cloud deck" : ""),
            configure);
    }

    /// <summary>
    /// The configuration behind <see cref="SpectralBands"/>, as a function from concentration to
    /// options, or null when the HITRAN line lists have not been fetched.
    /// </summary>
    /// <remarks>
    /// Exposed so that <see cref="ForcingCurve"/> can be driven at high resolutions without
    /// paying for nine equilibrium marches.
    /// </remarks>
    public static Func<double, ModelOptions>? SpectralConfiguration(
        int bandCount = 16, int gPoints = 16, int segmentCount = 30, int samples = 80_000,
        bool rederive = false, double wingCutoff = 400.0, double absorberScale = double.NaN,
        bool waterVapourFeedback = true, double? fixedVapourTemperature = null,
        bool subLorentzianWings = true, double cloudFraction = 0.0,
        double methaneShare = CalibratedMethaneShare, double methaneRatio = 1.0)
    {
        // Relative amounts per gas, then a common scale chosen for the base state.
        var recipe = new (string File, AbsorberKind Kind, double Share, bool Co2)[]
        {
            (HitranLineList.WaterVapourRotational, AbsorberKind.WaterVapour, 6.0, false),
            (HitranLineList.WaterVapourBending,    AbsorberKind.WaterVapour, 2.0, false),
            (HitranLineList.Co2FifteenMicron,      AbsorberKind.WellMixed,   2.0, true),
            (HitranLineList.OzoneNineSixMicron,    AbsorberKind.Ozone,       0.5, false),
            (HitranLineList.MethaneSevenSevenMicron, AbsorberKind.WellMixed, methaneShare, false),
            (HitranLineList.NitrousOxideSevenEightMicron, AbsorberKind.WellMixed, 0.1, false)
        };

        // Left unset, the loading follows the cloud setting - a gas loading calibrated without a
        // deck over-warms by about 15 K once one is added, because the deck supplies greenhouse
        // the gas was already standing in for. An explicit value still wins, which is what the
        // calibration and convergence studies pass.
        double scale = double.IsNaN(absorberScale) ? CalibratedAbsorberScale(cloudFraction) : absorberScale;

        // Line lists are loaded once even when re-deriving; it is the derivation that repeats,
        // not the file I/O.
        var lines = new List<(IReadOnlyList<SpectralLine> Lines, AbsorberKind Kind, double Amount,
                              bool Co2, string File)>();
        foreach (var (file, kind, share, co2) in recipe)
        {
            string? path = HitranLineList.DefaultPath(file);
            if (path is null) return null;

            lines.Add((HitranLineList.Load(path, minimumIntensity: 1e-26),
                kind, share * scale, co2, file));
        }

        // Bands derived with CO2 present at the given multiple of its reference amount.
        static bool IsMethane(string file) => file == HitranLineList.MethaneSevenSevenMicron;

        IReadOnlyList<SpectralBand> Derive(double co2Ratio)
        {
            // Methane is scaled inside the derivation rather than through the band share, so its
            // k-distribution is measured from a spectrum that actually contains that much of it.
            // Scaling the share instead stretches the band mean while the distribution goes on
            // describing the reference atmosphere - which is the approximation this parameter
            // exists to test.
            double Amount((IReadOnlyList<SpectralLine> Lines, AbsorberKind Kind, double Amount,
                           bool Co2, string File) m) =>
                m.Co2 ? m.Amount * co2Ratio
                     : IsMethane(m.File) ? m.Amount * methaneRatio
                     : m.Amount;

            // Only CO2 gets a chi factor, and only when asked for: it is fitted to the CO2 nu_2
            // band, so giving it to water vapour or ozone would be inventing spectroscopy.
            var molecules = lines
                .Select(m => new BandDerivation.Molecule(
                    m.Lines, m.Kind, Amount(m), m.Co2, m.File,
                    m.Co2 && subLorentzianWings ? ChiFactor.CarbonDioxideNu2 : null,

                    // Marked dialable so each band records methane's share and can scale it
                    // later. When methaneRatio is not 1 the derivation already holds the right
                    // amount, and the share is cleared below so it is not scaled twice.
                    RespondsToMethane: IsMethane(m.File)))
                .ToList();

            return BandDerivation.DeriveShared(
                molecules, fromWavenumber: 100, toWavenumber: 2000, bandCount: bandCount,
                samples: samples, gPoints: gPoints, wingCutoff: wingCutoff,
                continuumOpticalDepth: 1.2 * scale);
        }

        // A re-derived methane band already holds its own amount, so its share must be cleared or
        // dialling MethaneConcentration would scale it a second time.
        var referenceBands = rederive
            ? null
            : methaneRatio != 1.0
                ? Derive(1.0).Select(b => b with { MethaneFraction = 0.0 }).ToArray()
                : Derive(1.0);
        double referencePpm = Concentrations[0];

        // Re-derivation is memoised: ForcingCurve asks for each concentration twice (once for the
        // reference march, once for the forcing solve) and a derivation is the expensive step.
        // Concurrent because Run now evaluates concentrations in parallel. GetOrAdd may run the
        // factory twice for the same key under contention, which wastes a derivation but cannot
        // produce a wrong answer - the derivation is a pure function of the concentration.
        var cache = new System.Collections.Concurrent.ConcurrentDictionary<double, SpectralBand[]>();

        return ppm =>
            {
                SpectralBand[] bands;
                double concentration;

                if (rederive)
                {
                    // The derivation already holds this concentration's CO2, so the band mean must
                    // not be scaled a second time: Co2Fraction is zeroed.
                    bands = cache.GetOrAdd(ppm, p => Derive(p / referencePpm)
                        .Select(b => b with { Co2Fraction = 0.0 })
                        .ToArray());
                    concentration = ppm;
                }
                else
                {
                    bands = referenceBands!.ToArray();
                    concentration = ppm;
                }

                var options = new ModelOptions
                {
                    Co2Concentration = concentration,
                    SegmentCount = segmentCount,
                    Bands = bands,
                    WaterVapourOpticalDepth = 1.0,
                    WaterVapourFeedback = waterVapourFeedback,
                    WaterVapourFixedTemperature = fixedVapourTemperature,
                    OzoneFraction = 0.3
                };

                if (cloudFraction > 0.0)
                {
                    // The same deck WithTypicalCloud uses, so the cloudy spectral column and the
                    // cloudy grey one describe the same sky.
                    var deck = ModelOptions.WithTypicalCloud();
                    options.CloudFraction = cloudFraction;
                    options.ClearSkyAlbedo = deck.ClearSkyAlbedo;
                    options.CloudAlbedo = deck.CloudAlbedo;
                    options.CloudBaseAltitude = deck.CloudBaseAltitude;
                    options.CloudTopAltitude = deck.CloudTopAltitude;
                    options.CloudLongwaveEmissivity = deck.CloudLongwaveEmissivity;
                }

                return options;
            };
    }

    /// <summary>The configuration used for the README's water-vapour-feedback calibration.</summary>
    public static Co2Sweep WithWaterVapourFeedback() => Run(
        "With water vapour feedback",
        "--optical-depth 2.0 --wv-tau 1.8 --ozone-fraction 0.3 --pressure-broadening 1 --co2-fraction 0.11",
        () => new ModelOptions
        {
            TotalOpticalDepth = 2.0,
            WaterVapourOpticalDepth = 1.8,
            OzoneFraction = 0.3,
            PressureBroadeningExponent = 1.0,
            Co2AbsorberFraction = 0.11
        });
}
