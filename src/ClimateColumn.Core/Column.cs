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
    public void DistributeShortwave()
    {
        double absorbed = Options.AbsorbedSolarFlux;
        double inAtmosphere = absorbed * Options.AtmosphericShortwaveFraction;

        double ChapmanWeight(Segment s)
        {
            double x = (s.MidAltitude - Options.OzoneLayerAltitude) / Options.OzoneLayerWidth;
            return Math.Exp(1.0 - x - Math.Exp(-x));
        }

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
