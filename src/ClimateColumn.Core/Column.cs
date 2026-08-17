namespace ClimateColumn.Core;

/// <summary>
/// A vertical column of atmosphere divided into segments, plus the underlying surface.
/// Interfaces are numbered 0..N where 0 is the surface and N is the top of the column;
/// segment i lies between interface i and interface i+1.
/// </summary>
public sealed class Column
{
    public Segment[] Segments { get; }

    /// <summary>Surface (skin) temperature, K.</summary>
    public double SurfaceTemperature { get; set; }

    /// <summary>Solar flux absorbed by the surface, W m^-2.</summary>
    public double SurfaceShortwaveAbsorbed { get; set; }

    public ModelOptions Options { get; }

    public int Count => Segments.Length;

    /// <summary>Total air mass of the column per unit area, kg m^-2.</summary>
    public double MassPerArea { get; }

    private Column(Segment[] segments, ModelOptions options, double massPerArea)
    {
        Segments = segments;
        Options = options;
        MassPerArea = massPerArea;
    }

    public static Column Build(ModelOptions options)
    {
        options.Validate();

        int n = options.SegmentCount;
        double dz = options.TopAltitude / n;

        var segments = new Segment[n];
        double totalMass = 0.0;

        for (int i = 0; i < n; i++)
        {
            double zb = i * dz;
            double zt = (i + 1) * dz;
            double pb = StandardAtmosphere.Pressure(zb);
            double pt = StandardAtmosphere.Pressure(zt);
            double mass = (pb - pt) / PhysicalConstants.Gravity;
            totalMass += mass;

            segments[i] = new Segment
            {
                Index = i,
                BottomAltitude = zb,
                TopAltitude = zt,
                BottomPressure = pb,
                TopPressure = pt,
                MassPerArea = mass,
                Temperature = options.InitialiseFromStandardAtmosphere
                    ? StandardAtmosphere.Temperature(0.5 * (zb + zt))
                    : options.EmissionTemperature
            };
        }

        var column = new Column(segments, options, totalMass)
        {
            SurfaceTemperature = options.InitialSurfaceTemperature
        };

        column.DistributeOpticalDepth();
        column.DistributeShortwave();
        return column;
    }

    /// <summary>
    /// Assign the volumetric emission coefficient eps' to each segment. The absorber has
    /// two components, each normalised so that its column hemispheric optical depth
    /// sum(D * eps' * dz) equals its requested total:
    /// a dry absorber distributed as rho * (p/p0)^n (well mixed for n = 0, concentrated
    /// downward by pressure broadening for n &gt; 0), and an optional water-vapour-like
    /// absorber distributed as exp(-z/H) whose column total follows Clausius-Clapeyron on
    /// the current near-surface air temperature - call this again after the temperatures
    /// change to keep that feedback current.
    /// </summary>
    public void DistributeOpticalDepth()
    {
        double dryTarget = Options.EffectiveDryOpticalDepth;
        double wvTarget = CurrentWaterVapourOpticalDepth();

        // The normalisation uses the Koenigsberger diffusivity D = 2, not
        // Options.Diffusivity, so that eps' describes the absorber itself and not the
        // two-stream closure. Normalising by Options.Diffusivity would make it cancel out
        // of tau = D * eps' * dz, turning D into a knob with no effect, and would rescale
        // eps' so that the Koenigsberger emission 4 eps' sigma T^4 dz no longer matched the
        // absorber it is supposed to describe.
        double d2 = PhysicalConstants.KoenigsbergerDiffusivity;
        double p0 = Segments.Length > 0 ? Segments[0].BottomPressure : 0.0;
        double n = Options.PressureBroadeningExponent;

        double DryWeight(Segment s) =>
            s.Density * (n == 0.0 ? 1.0 : Math.Pow(s.MidPressure / p0, n));
        double WaterVapourWeight(Segment s) =>
            Math.Exp(-s.MidAltitude / Options.WaterVapourScaleHeight);

        double dryColumn = 0.0, wvColumn = 0.0;
        foreach (var s in Segments)
        {
            dryColumn += DryWeight(s) * s.Thickness;
            wvColumn += WaterVapourWeight(s) * s.Thickness;
        }

        double cDry = dryColumn > 0 ? dryTarget / (d2 * dryColumn) : 0.0;
        double cWv = wvColumn > 0 ? wvTarget / (d2 * wvColumn) : 0.0;

        foreach (var s in Segments)
        {
            s.EmissionCoefficient = cDry * DryWeight(s) + cWv * WaterVapourWeight(s);
        }

        DistributeWindowContinuum(WaterVapourWeight);
        DistributeBands(DryWeight, WaterVapourWeight);
    }

    /// <summary>
    /// Assigns each explicit band's extinction coefficient to every segment.
    /// </summary>
    /// <remarks>
    /// Each band's three absorber components keep their own vertical profile: the well-mixed
    /// part follows air density with any pressure broadening, water vapour follows its own scale
    /// height and the Clausius-Clapeyron scaling, and the continuum follows the vapour squared
    /// for its self part and vapour times pressure for its foreign part. That is the whole point
    /// of banding - a single coefficient per segment cannot represent gases that sit in
    /// different places.
    /// </remarks>
    private void DistributeBands(Func<Segment, double> dryWeight, Func<Segment, double> vapourWeight)
    {
        if (!Options.HasBands)
        {
            foreach (var s in Segments) s.BandEmissionCoefficients = Array.Empty<double>();
            return;
        }

        int bandCount = Options.Bands.Count;
        foreach (var s in Segments)
        {
            if (s.BandEmissionCoefficients.Length != bandCount)
                s.BandEmissionCoefficients = new double[bandCount];
            else
                Array.Clear(s.BandEmissionCoefficients);
        }

        double d2 = PhysicalConstants.KoenigsbergerDiffusivity;
        double p0 = Segments.Length > 0 ? Segments[0].BottomPressure : 0.0;

        double vapourScale = Options.WaterVapourOpticalDepth > 0
            ? CurrentWaterVapourOpticalDepth() / Options.WaterVapourOpticalDepth
            : ClausiusClapeyronScale();

        double Self(Segment s) => vapourWeight(s) * vapourWeight(s);
        double Foreign(Segment s) => p0 > 0 ? vapourWeight(s) * (s.MidPressure / p0) : 0.0;

        double dryColumn = 0.0, vapourColumn = 0.0, selfColumn = 0.0, foreignColumn = 0.0;
        double ozoneColumn = 0.0;
        foreach (var s in Segments)
        {
            dryColumn += dryWeight(s) * s.Thickness;
            vapourColumn += vapourWeight(s) * s.Thickness;
            selfColumn += Self(s) * s.Thickness;
            foreignColumn += Foreign(s) * s.Thickness;
            ozoneColumn += ChapmanWeight(s) * s.Thickness;
        }

        double ratio = Options.Co2ConcentrationRatio;
        double foreignShare = Options.ContinuumForeignFraction;

        for (int b = 0; b < bandCount; b++)
        {
            var band = Options.Bands[b];

            double cDry = dryColumn > 0
                ? band.EffectiveOpticalDepth(ratio) / (d2 * dryColumn) : 0.0;
            double cVapour = vapourColumn > 0
                ? band.WaterVapourOpticalDepth * vapourScale / (d2 * vapourColumn) : 0.0;

            double continuumForeign = band.ContinuumOpticalDepth * foreignShare;
            double continuumSelf = band.ContinuumOpticalDepth - continuumForeign;

            double cSelf = selfColumn > 0 ? continuumSelf / (d2 * selfColumn) : 0.0;
            double cForeign = foreignColumn > 0 ? continuumForeign / (d2 * foreignColumn) : 0.0;
            double cOzone = ozoneColumn > 0
                ? band.OzoneOpticalDepth / (d2 * ozoneColumn) : 0.0;

            foreach (var s in Segments)
            {
                s.BandEmissionCoefficients[b] =
                    cDry * dryWeight(s) +
                    cVapour * vapourWeight(s) +
                    cSelf * vapourScale * vapourScale * Self(s) +
                    cForeign * vapourScale * Foreign(s) +
                    cOzone * ChapmanWeight(s);
            }
        }
    }

    /// <summary>
    /// The Clausius-Clapeyron amplification of the vapour relative to its reference temperature.
    /// Used by banded runs, where the vapour loading lives on the bands rather than on
    /// <see cref="ModelOptions.WaterVapourOpticalDepth"/>.
    /// </summary>
    private double ClausiusClapeyronScale()
    {
        double air = ConvectionSolver.NearSurfaceAirTemperature(this);
        if (air <= 0) return 1.0;

        return Math.Exp(PhysicalConstants.ClausiusClapeyronScale *
            (1.0 / Options.WaterVapourReferenceTemperature - 1.0 / air));
    }

    /// <summary>Total hemispheric optical depth of band <paramref name="band"/>.</summary>
    public double TotalBandOpticalDepth(int band)
    {
        double sum = 0.0;
        foreach (var s in Segments) sum += s.BandOpticalThickness(band, Options.Diffusivity);
        return sum;
    }

    /// <summary>
    /// Assign the window's continuum extinction, m^-1, to each segment.
    /// </summary>
    /// <remarks>
    /// The continuum is conventionally split into a self term going as the vapour pressure
    /// squared and a foreign term going as vapour pressure times air pressure. Both scalings
    /// appear here: the self part is quadratic in the vapour profile weight, the foreign part
    /// is linear in it and in p/p0. Each part is normalised so that at the reference
    /// temperature the column continuum optical depth is exactly
    /// Options.WindowContinuumOpticalDepth, divided between the two by
    /// Options.ContinuumForeignFraction.
    ///
    /// The Clausius-Clapeyron factor then enters linearly in the foreign term and quadratically
    /// in the self term, which is what makes the window shut as the column warms rather than
    /// staying open forever.
    /// </remarks>
    private void DistributeWindowContinuum(Func<Segment, double> vapourWeight)
    {
        if (!Options.HasWindowContinuum)
        {
            foreach (var s in Segments) s.WindowEmissionCoefficient = 0.0;
            return;
        }

        double d2 = PhysicalConstants.KoenigsbergerDiffusivity;
        double p0 = Segments.Length > 0 ? Segments[0].BottomPressure : 0.0;

        // Amplification of the vapour relative to the reference state, from Clausius-Clapeyron.
        double scale = Options.WaterVapourOpticalDepth > 0
            ? CurrentWaterVapourOpticalDepth() / Options.WaterVapourOpticalDepth
            : 0.0;

        double Self(Segment s) => vapourWeight(s) * vapourWeight(s);
        double Foreign(Segment s) => p0 > 0 ? vapourWeight(s) * (s.MidPressure / p0) : 0.0;

        double selfColumn = 0.0, foreignColumn = 0.0;
        foreach (var s in Segments)
        {
            selfColumn += Self(s) * s.Thickness;
            foreignColumn += Foreign(s) * s.Thickness;
        }

        double foreignTarget = Options.WindowContinuumOpticalDepth * Options.ContinuumForeignFraction;
        double selfTarget = Options.WindowContinuumOpticalDepth - foreignTarget;

        double cSelf = selfColumn > 0 ? selfTarget / (d2 * selfColumn) : 0.0;
        double cForeign = foreignColumn > 0 ? foreignTarget / (d2 * foreignColumn) : 0.0;

        foreach (var s in Segments)
        {
            s.WindowEmissionCoefficient =
                cSelf * scale * scale * Self(s) + cForeign * scale * Foreign(s);
        }
    }

    /// <summary>Total hemispheric optical depth of the window band.</summary>
    public double TotalWindowOpticalDepth()
    {
        double sum = 0.0;
        foreach (var s in Segments) sum += s.WindowOpticalThickness(Options.Diffusivity);
        return sum;
    }

    /// <summary>
    /// Column optical depth of the water-vapour absorber at the current near-surface air
    /// temperature: the reference loading scaled by the Clausius-Clapeyron factor
    /// exp(L/R_v (1/T_ref - 1/T_air)). Zero when the feedback is disabled.
    /// </summary>
    public double CurrentWaterVapourOpticalDepth()
    {
        double loading = Options.WaterVapourOpticalDepth;
        if (loading <= 0.0) return 0.0;

        double airTemperature = ConvectionSolver.NearSurfaceAirTemperature(this);
        return loading * Math.Exp(PhysicalConstants.ClausiusClapeyronScale *
            (1.0 / Options.WaterVapourReferenceTemperature - 1.0 / airTemperature));
    }

    /// <summary>
    /// Split the absorbed solar flux between the atmosphere and the surface. The
    /// atmospheric share is spread by air mass, except for an optional ozone-like fraction
    /// deposited on a Chapman profile exp(1 - x - e^-x), x = (z - z0)/H, which peaks at z0
    /// and integrates to e*H - the shape of absorption of an exponentially attenuated beam
    /// in an exponential absorber.
    /// </summary>
    /// <summary>
    /// Chapman-layer shape at a segment, exp(1 - x - e^-x) with x = (z - z0)/H: the profile of
    /// absorption of an exponentially attenuated beam in an exponential absorber. Shared by
    /// ozone's solar heating and its longwave band absorption, since it is the same ozone.
    /// </summary>
    private double ChapmanWeight(Segment s)
    {
        double x = (s.MidAltitude - Options.OzoneLayerAltitude) / Options.OzoneLayerWidth;
        return Math.Exp(1.0 - x - Math.Exp(-x));
    }

    public void DistributeShortwave()
    {
        double absorbed = Options.AbsorbedSolarFlux;
        double inAtmosphere = absorbed * Options.AtmosphericShortwaveFraction;

        double chapmanColumn = 0.0;
        foreach (var s in Segments) chapmanColumn += ChapmanWeight(s) * s.Thickness;

        // If the layer lies entirely outside the column its weights vanish; fall back to
        // distributing everything by mass rather than losing the flux.
        double ozone = chapmanColumn > 0.0 ? inAtmosphere * Options.OzoneFraction : 0.0;
        double byMass = inAtmosphere - ozone;

        foreach (var s in Segments)
        {
            s.ShortwaveAbsorbed =
                (MassPerArea > 0 ? byMass * (s.MassPerArea / MassPerArea) : 0.0) +
                (ozone > 0 ? ozone * ChapmanWeight(s) * s.Thickness / chapmanColumn : 0.0);
        }

        SurfaceShortwaveAbsorbed = absorbed - inAtmosphere;
    }

    /// <summary>Total hemispheric optical depth of the column.</summary>
    public double TotalOpticalDepth()
    {
        double sum = 0.0;
        foreach (var s in Segments) sum += s.OpticalThickness(Options.Diffusivity);
        return sum;
    }

    /// <summary>Enthalpy of the atmospheric segments per unit area, J m^-2.</summary>
    public double AtmosphericEnthalpy()
    {
        double h = 0.0;
        foreach (var s in Segments) h += s.HeatCapacity * s.Temperature;
        return h;
    }

    /// <summary>Column enthalpy per unit area including the surface reservoir, J m^-2.</summary>
    public double Enthalpy() =>
        Options.SurfaceHeatCapacity * SurfaceTemperature + AtmosphericEnthalpy();

    public double[] Temperatures()
    {
        var t = new double[Count];
        for (int i = 0; i < Count; i++) t[i] = Segments[i].Temperature;
        return t;
    }
}
