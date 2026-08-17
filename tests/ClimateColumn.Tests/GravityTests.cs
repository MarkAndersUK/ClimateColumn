using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Gravity falling with height by the inverse-square law, and the geopotential grid that goes
/// with it.
/// </summary>
/// <remarks>
/// The subtle part is not g itself but where it belongs. The U.S. Standard Atmosphere is defined
/// on <em>geopotential</em> altitude with gravity fixed at the defined constant g_0; that is not
/// a simplification in the standard but its way of absorbing the variation of gravity, by
/// measuring height in work done rather than in metres. So switching gravity on means reading the
/// tables at <c>H = r_0 z / (r_0 + z)</c> and dividing pressure drops by the local g - and those
/// two changes have to be mutually consistent, or the column's density stops matching the
/// profile it was built from. The density test below is what checks that.
/// </remarks>
[TestClass]
public class GravityTests
{
    private const double Radius = PhysicalConstants.EarthRadius;

    private static ModelOptions Varying(double radius = Radius) =>
        new() { VariableGravity = true, PlanetRadius = radius };

    /// <summary>Constant gravity is the default, and geometric altitude is then geopotential.</summary>
    [TestMethod]
    public void ConstantGravityIsTheDefault()
    {
        var options = new ModelOptions();

        Assert.IsFalse(options.VariableGravity, "gravity must stay constant by default");
        Assert.AreEqual(PhysicalConstants.Gravity, options.GravityAt(0.0), 0.0);
        Assert.AreEqual(PhysicalConstants.Gravity, options.GravityAt(50_000.0), 0.0,
            "with the option off, altitude must not change g at all");

        var flat = Column.Build(options);
        var moving = Column.Build(Varying());

        Assert.AreEqual(PhysicalConstants.Gravity, moving.Options.GravityAt(0.0), 1e-12,
            "at the surface the two must agree exactly");
        Assert.AreNotEqual(flat.Segments[^1].MassPerArea, moving.Segments[^1].MassPerArea,
            "if the top segment's mass is unchanged, the option is doing nothing");
    }

    /// <summary>The inverse-square law, at both ends of the column.</summary>
    [TestMethod]
    public void GravityFollowsTheInverseSquareLaw()
    {
        Assert.AreEqual(PhysicalConstants.Gravity, PhysicalConstants.GravityAt(0.0, Radius), 1e-12);

        double expected = PhysicalConstants.Gravity *
                          Math.Pow(Radius / (Radius + 50_000.0), 2);

        Assert.AreEqual(expected, PhysicalConstants.GravityAt(50_000.0, Radius), 1e-12);
        Assert.AreEqual(9.6545, PhysicalConstants.GravityAt(50_000.0, Radius), 0.0005,
            "g at 50 km should be about 9.6545 m/s2, 1.55% below sea level");
    }

    /// <summary>
    /// The defining property of geopotential altitude: g_0 H is the work per unit mass actually
    /// done climbing to z through the varying field. Checked by integrating that work.
    /// </summary>
    [TestMethod]
    public void GeopotentialAltitudeIsTheWorkDoneAgainstGravity()
    {
        const double top = 50_000.0;
        const int steps = 2_000_000;
        double h = top / steps;

        // Midpoint rule on int_0^z g(z') dz'.
        double work = 0.0;
        for (int k = 0; k < steps; k++)
        {
            work += PhysicalConstants.GravityAt((k + 0.5) * h, Radius) * h;
        }

        double geopotential = StandardAtmosphere.GeopotentialAltitude(top, Radius);

        Assert.AreEqual(work / PhysicalConstants.Gravity, geopotential, 1e-4,
            $"g_0 H should be the integrated work: H = {geopotential:F3} m against " +
            $"{work / PhysicalConstants.Gravity:F3} m");

        Assert.AreEqual(49_610.0, geopotential, 5.0,
            "50 geometric km is about 49.61 geopotential km on Earth");
        Assert.IsTrue(geopotential < top,
            "geopotential altitude is always below geometric - gravity weakens on the way up");
    }

    /// <summary>A large enough planet has uniform gravity and the two altitudes coincide.</summary>
    [TestMethod]
    public void AnInfiniteRadiusRecoversConstantGravity()
    {
        const double z = 50_000.0;

        Assert.AreEqual(z, StandardAtmosphere.GeopotentialAltitude(z, 1e15), z * 1e-9);
        Assert.AreEqual(PhysicalConstants.Gravity,
            PhysicalConstants.GravityAt(z, 1e15), PhysicalConstants.Gravity * 1e-9);

        double flat = TestSupport.Default.SurfaceTemperature;
        double huge = TestSupport.Equilibrium("gravity-huge", () => Varying(6.371e10))
            .SurfaceTemperature;

        Assert.AreEqual(flat, huge, 1e-5,
            "on a planet a hundred thousand times Earth's radius the correction must vanish");
    }

    /// <summary>
    /// The consistency check that matters. A segment's mass comes from dp / g(z); its density is
    /// that over the thickness. Independently, the ideal gas law gives the density from the
    /// pressure and temperature the standard atmosphere reports at the same altitude. The two
    /// must agree, and they only do if the geopotential conversion and the local g are applied
    /// consistently - getting either backwards leaves a residue of about 1.5%.
    /// </summary>
    [TestMethod]
    public void SegmentDensityMatchesTheProfileItWasBuiltFrom()
    {
        var options = Varying();
        options.SegmentCount = 500;
        var column = Column.Build(options);

        foreach (var s in column.Segments)
        {
            // Layer-mean density from the mass grid.
            double fromMass = s.Density;

            // Layer-mean density from the profile, integrated across the segment. The ideal gas
            // law is evaluated at geometric altitudes, converting inside StandardAtmosphere.
            const int steps = 200;
            double dz = s.Thickness / steps;
            double integral = 0.0;
            for (int k = 0; k < steps; k++)
            {
                double z = s.BottomAltitude + (k + 0.5) * dz;
                integral += StandardAtmosphere.Density(z, Radius) * dz;
            }
            double fromProfile = integral / s.Thickness;

            Assert.AreEqual(fromProfile, fromMass, fromProfile * 2e-4,
                $"segment {s.Index} at {s.MidAltitude / 1000.0:F1} km: mass grid gives " +
                $"{fromMass:E4} kg/m3, the profile gives {fromProfile:E4}");
        }
    }

    /// <summary>
    /// Weaker gravity aloft means more mass is needed to hold up a given pressure drop, so every
    /// segment above the surface gets heavier and the column's total mass rises.
    /// </summary>
    [TestMethod]
    public void WeakerGravityAloftMeansMoreMass()
    {
        var flat = Column.Build(new ModelOptions());
        var varying = Column.Build(Varying());

        Assert.IsTrue(varying.MassPerArea > flat.MassPerArea,
            $"column mass fell from {flat.MassPerArea:F2} to {varying.MassPerArea:F2} kg/m2");

        double rise = varying.MassPerArea / flat.MassPerArea - 1.0;
        Assert.IsTrue(rise is > 0.0005 and < 0.01,
            $"the column gained {100 * rise:F3}% mass; weighted by where the air actually is " +
            "this should be a few tenths of a percent, not the full 1.6% at the top");

        // The effect must grow with height, since g falls with height.
        double lowest = varying.Segments[0].MassPerArea / flat.Segments[0].MassPerArea;
        double highest = varying.Segments[^1].MassPerArea / flat.Segments[^1].MassPerArea;

        Assert.IsTrue(highest > lowest,
            $"the top segment gained {100 * (highest - 1):F3}% against the bottom's " +
            $"{100 * (lowest - 1):F3}%; the correction must increase with altitude");
    }

    /// <summary>The dry adiabat is g / c_p, so it relaxes with height along with gravity.</summary>
    [TestMethod]
    public void TheDryAdiabatRelaxesWithHeight()
    {
        double surface = PhysicalConstants.DryAdiabaticLapseRateAt(0.0, Radius);
        double top = PhysicalConstants.DryAdiabaticLapseRateAt(50_000.0, Radius);

        Assert.AreEqual(PhysicalConstants.DryAdiabaticLapseRate, surface, 1e-12,
            "at the surface the height-dependent form must reduce to the constant");
        Assert.AreEqual(9.761e-3, surface, 1e-5, "9.761 K/km at the surface");
        Assert.AreEqual(9.609e-3, top, 1e-5, "9.609 K/km at 50 km");

        // Recorded because nothing uses it: the adjustment runs on the prescribed critical lapse
        // rate, not on g / c_p, so this relaxation does not reach the convection scheme.
        Assert.AreEqual(0.0065, new ModelOptions().CriticalLapseRate, 0.0,
            "the critical lapse rate is prescribed and is not derived from gravity");
    }

    /// <summary>
    /// The finding worth keeping: variable gravity and sphericity push the surface temperature in
    /// <em>opposite</em> directions and very nearly cancel. Adding either alone overstates the
    /// combined effect by roughly fivefold, which is a good reason not to fold one into the other.
    /// </summary>
    [TestMethod]
    public void GravityAndSphericityLargelyCancel()
    {
        double flat = TestSupport.Default.SurfaceTemperature;

        double gravityOnly = TestSupport
            .Equilibrium("gravity-earth", () => Varying()).SurfaceTemperature - flat;

        double sphericalOnly = TestSupport
            .Equilibrium("spherical-earth", () => new ModelOptions { SphericalGeometry = true })
            .SurfaceTemperature - flat;

        double both = TestSupport.Equilibrium("gravity-and-sphericity", () =>
            new ModelOptions { SphericalGeometry = true, VariableGravity = true })
            .SurfaceTemperature - flat;

        Assert.IsTrue(gravityOnly > 0.0,
            $"gravity alone moved the surface {gravityOnly:+0.000;-0.000} K; more mass in the " +
            "column should warm it");
        Assert.IsTrue(sphericalOnly < 0.0,
            $"sphericity alone moved the surface {sphericalOnly:+0.000;-0.000} K; it should cool");

        Assert.IsTrue(Math.Abs(both) < Math.Abs(gravityOnly),
            $"together they give {both:+0.000;-0.000} K, which is not smaller than gravity's " +
            $"own {gravityOnly:+0.000;-0.000} K - the cancellation has stopped happening");
        Assert.IsTrue(Math.Abs(both) < Math.Abs(sphericalOnly),
            $"together they give {both:+0.000;-0.000} K against sphericity's " +
            $"{sphericalOnly:+0.000;-0.000} K");

        // The values in the README.
        Assert.AreEqual(+0.012, gravityOnly, 0.005);
        Assert.AreEqual(-0.016, sphericalOnly, 0.005);
        Assert.AreEqual(-0.003, both, 0.005);
    }

    /// <summary>The energy budget closes with gravity varying, in either geometry.</summary>
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TheEnergyBudgetClosesWithVariableGravity(bool spherical)
    {
        var result = TestSupport.Equilibrium(
            spherical ? "gravity-and-sphericity" : "gravity-earth",
            () => new ModelOptions { VariableGravity = true, SphericalGeometry = spherical });

        var options = result.Column.Options;

        Assert.IsTrue(result.Converged, "the column must reach equilibrium");
        Assert.AreEqual(options.AbsorbedSolarFlux, result.Radiation.OutgoingLongwave,
            options.FluxTolerance,
            "what leaves the top must equal what the planet absorbs");

        double eps = options.SurfaceEmissivity;
        double net = result.Column.SurfaceShortwaveAbsorbed
                   + eps * result.Radiation.SurfaceDownwardFlux
                   - result.SurfaceEmission
                   - result.SensibleHeatFlux
                   - result.LatentHeatFlux;

        Assert.IsTrue(Math.Abs(net) < options.FluxTolerance,
            $"surface budget leaves {net:E3} W/m2 unaccounted for");
    }
}
