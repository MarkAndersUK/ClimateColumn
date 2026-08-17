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

        // With gravity held constant, geometric and geopotential altitude coincide and the
        // standard atmosphere is read directly. With it varying, the altitudes are geometric and
        // have to be converted to the geopotential altitude the standard's tables are defined on.
        double radius = options.VariableGravity ? options.PlanetRadius : 0.0;

        for (int i = 0; i < n; i++)
        {
            double zb = i * dz;
            double zt = (i + 1) * dz;
            double pb = StandardAtmosphere.Pressure(zb, radius);
            double pt = StandardAtmosphere.Pressure(zt, radius);

            // Hydrostatic balance, dp = -rho g dz, so the mass holding up a given pressure drop
            // is dp / g. Weaker gravity aloft therefore means more mass per unit pressure drop.
            double mass = (pb - pt) / options.GravityAt(0.5 * (zb + zt));
            totalMass += mass;

            segments[i] = new Segment
            {
                Index = i,
                BottomAltitude = zb,
                TopAltitude = zt,
                BottomPressure = pb,
                TopPressure = pt,
                MassPerArea = mass,
                ShellVolumeFactor = ShellVolumeFactor(options, zb, zt),
                Temperature = options.InitialiseFromStandardAtmosphere
                    ? StandardAtmosphere.Temperature(0.5 * (zb + zt), radius)
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
    /// The volume of the shell between <paramref name="bottom"/> and <paramref name="top"/>
    /// divided by the volume of the slab of equal thickness standing on the surface, which is
    /// the exact integral of (r/r_0)^2 across the layer:
    /// <c>(r_t^3 - r_b^3) / (3 r_0^2 dz)</c>. Identically 1 in plane-parallel geometry.
    /// </summary>
    private static double ShellVolumeFactor(ModelOptions options, double bottom, double top)
    {
        if (!options.SphericalGeometry) return 1.0;

        double r0 = options.PlanetRadius;
        double rb = r0 + bottom;
        double rt = r0 + top;
        double dz = top - bottom;
        if (dz <= 0.0) return 1.0;

        return (rt * rt * rt - rb * rb * rb) / (3.0 * r0 * r0 * dz);
    }

    /// <summary>
    /// The geometric factor at the top of the column, (r_top/r_0)^2. Fluxes reported by the
    /// solver are power per unit <em>surface</em> area; dividing the outgoing longwave by this
    /// gives the actual radiant flux crossing the top of the atmosphere.
    /// </summary>
    public double TopGeometricFactor =>
        Options.SphericalGeometry ? TopRadiusRatioSquared : 1.0;

    /// <summary>
    /// (r_top / r_0)^2 regardless of which geometric options are set - the ratio of the
    /// top-of-atmosphere disc's area to the solid planet's. About 1.0158 for 50 km on Earth.
    /// </summary>
    public double TopRadiusRatioSquared
    {
        get
        {
            double ratio = (Options.PlanetRadius + Options.TopAltitude) / Options.PlanetRadius;
            return ratio * ratio;
        }
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
        LimbShortwaveAbsorbed = AddLimbAbsorption(chapmanColumn);
    }

    /// <summary>
    /// Solar flux absorbed from rays that miss the solid planet but pass through its atmosphere,
    /// W m^-2 of planet surface. Zero unless
    /// <see cref="ModelOptions.TopOfAtmosphereInterception"/> is set.
    /// </summary>
    public double LimbShortwaveAbsorbed { get; private set; }

    /// <summary>
    /// Total solar flux the planet absorbs per unit surface area: the disc term the options
    /// describe, plus whatever the limb annulus captures. This, not
    /// <see cref="ModelOptions.AbsorbedSolarFlux"/>, is what the outgoing longwave has to
    /// balance at equilibrium.
    /// </summary>
    public double TotalShortwaveAbsorbed =>
        Options.AbsorbedSolarFlux + LimbShortwaveAbsorbed;

    /// <summary>
    /// Absorption of the sunlight that passes through the atmosphere without striking the
    /// ground - the annulus of impact parameters between r_0 and r_0 + H.
    /// </summary>
    /// <remarks>
    /// For each impact parameter b the ray descends to a tangent point at radius b and climbs
    /// back out, crossing every shell above b twice. The path length through shell i on one leg
    /// is <c>sqrt(r_t^2 - b^2) - sqrt(max(r_b, b)^2 - b^2)</c>, and the beam is walked in order
    /// so that absorption is attenuated by everything already traversed. The annulus is then
    /// integrated with the area weight <c>2 pi b db</c>, divided by the planet's surface area
    /// so the result is a flux per unit surface area like everything else in the model.
    ///
    /// Two choices are worth naming rather than burying.
    ///
    /// <strong>The extinction coefficient is inferred, not given.</strong> The model's shortwave
    /// is a prescribed deposition profile, not a radiative transfer calculation, so there is no
    /// coefficient to reuse. One is constructed by asking what vertical optical depth would
    /// produce the prescribed absorption, <c>tau = -ln(1 - f)</c>, and distributing it with the
    /// same mass-and-Chapman shape the deposition already uses. That makes the limb path
    /// consistent with the vertical one by construction, but it is a construction: a different
    /// reading of f - the disc-averaged slant absorption rather than the vertical - would give a
    /// larger tau and rather more limb absorption.
    ///
    /// <strong>No albedo is applied.</strong> A limb ray never reaches the surface, and the
    /// model has no scattering, so the planetary albedo - which is a prescribed reflection of the
    /// disc - has nothing to act through here. This makes the limb term an upper bound relative
    /// to treating it as equally reflective.
    /// </remarks>
    private double AddLimbAbsorption(double chapmanColumn)
    {
        if (!Options.TopOfAtmosphereInterception) return 0.0;

        int n = Count;
        double f = Options.AtmosphericShortwaveFraction;

        // A longwave-only atmosphere is transparent to sunlight on every path, limb included.
        if (n == 0 || f <= 0.0) return 0.0;

        double r0 = Options.PlanetRadius;
        double rTop = r0 + Options.TopAltitude;
        if (rTop <= r0) return 0.0;

        // The vertical optical depth that reproduces the prescribed absorption, spread with the
        // deposition's own vertical shape.
        double tauVertical = f >= 1.0 ? 50.0 : -Math.Log(1.0 - f);
        var kappa = new double[n];
        var bottom = new double[n];
        var top = new double[n];

        for (int i = 0; i < n; i++)
        {
            var s = Segments[i];
            double weight = (1.0 - Options.OzoneFraction) *
                            (MassPerArea > 0.0 ? s.MassPerArea / MassPerArea : 0.0);
            if (chapmanColumn > 0.0)
            {
                weight += Options.OzoneFraction * ChapmanWeight(s) * s.Thickness / chapmanColumn;
            }

            kappa[i] = s.Thickness > 0.0 ? tauVertical * weight / s.Thickness : 0.0;
            bottom[i] = r0 + s.BottomAltitude;
            top[i] = r0 + s.TopAltitude;
        }

        int points = Math.Max(1, Options.LimbQuadraturePoints);
        double db = (rTop - r0) / points;

        var deposit = new double[n];
        var alongPath = new double[n];
        double captured = 0.0;

        for (int q = 0; q < points; q++)
        {
            double b = r0 + (q + 0.5) * db;

            // The lowest shell the ray actually enters: the tangent point sits inside it.
            int lowest = 0;
            while (lowest < n && top[lowest] <= b) lowest++;
            if (lowest >= n) continue;

            Array.Clear(alongPath);
            double transmission = 1.0;

            // Descending to the tangent point, then climbing back out. Each leg crosses the
            // same shells, so the ray sees every shell above b exactly twice.
            for (int leg = 0; leg < 2; leg++)
            {
                for (int step = 0; step < n - lowest; step++)
                {
                    int i = leg == 0 ? n - 1 - step : lowest + step;

                    double lower = Math.Max(bottom[i], b);
                    double ds = Math.Sqrt(top[i] * top[i] - b * b) -
                                Math.Sqrt(lower * lower - b * b);
                    if (ds <= 0.0) continue;

                    double absorbedHere = transmission * (1.0 - Math.Exp(-kappa[i] * ds));
                    alongPath[i] += absorbedHere;
                    transmission -= absorbedHere;
                }
            }

            // Power intercepted in this annulus per unit of the planet's surface area:
            // S0 * 2 pi b db / (4 pi r0^2).
            double intercepted = Options.SolarConstant * b * db / (2.0 * r0 * r0);

            captured += intercepted * (1.0 - transmission);
            for (int i = 0; i < n; i++) deposit[i] += intercepted * alongPath[i];
        }

        for (int i = 0; i < n; i++) Segments[i].ShortwaveAbsorbed += deposit[i];

        return captured;
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
