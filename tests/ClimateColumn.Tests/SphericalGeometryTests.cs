using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Spherical geometry: the column as a set of shells on a planet rather than a stack of
/// plane-parallel slabs.
/// </summary>
/// <remarks>
/// The implementation rests on one identity, and most of what follows exists to check it.
/// Writing the spherical two-stream equation
/// <c>dF+/dr = -D eps' (F+ - sigma T^4) - (2/r) F+</c>
/// in terms of <c>G = (r/r_0)^2 F</c> - the power crossing radius r divided by the surface area
/// beneath it - eliminates the geometric term and leaves the plane-parallel equation with its
/// source scaled by <c>(r/r_0)^2</c>. If that is right, the solver needs no structural change at
/// all; if it is wrong, everything here is wrong together, which is why the central test
/// integrates the original spherical equation numerically and compares.
/// </remarks>
[TestClass]
public class SphericalGeometryTests
{
    private static ModelOptions Spherical(double radius = PhysicalConstants.EarthRadius) =>
        new() { SphericalGeometry = true, PlanetRadius = radius };

    /// <summary>
    /// Plane-parallel is the default and is bit-for-bit unchanged: every shell factor is
    /// exactly 1, not merely close to it.
    /// </summary>
    [TestMethod]
    public void PlaneParallelGeometryIsUntouched()
    {
        var column = Column.Build(new ModelOptions());

        Assert.IsFalse(column.Options.SphericalGeometry, "sphericity must stay off by default");
        Assert.AreEqual(1.0, column.TopGeometricFactor, 0.0);

        foreach (var s in column.Segments)
        {
            Assert.AreEqual(1.0, s.ShellVolumeFactor, 0.0,
                $"segment {s.Index} must have an exactly unit shell factor in plane geometry");
        }
    }

    /// <summary>
    /// The shell factors reconstruct the exact volume of the spherical shell they tile, which
    /// is the property they were defined to have - the volume mean of (r/r_0)^2, not the
    /// midpoint value.
    /// </summary>
    [TestMethod]
    public void ShellFactorsSumToTheExactSphericalShellVolume()
    {
        var column = Column.Build(Spherical());
        double r0 = column.Options.PlanetRadius;
        double top = column.Options.TopAltitude;

        double summed = column.Segments.Sum(s => s.ShellVolumeFactor * s.Thickness);

        // int_{r0}^{r0+H} (r/r0)^2 dr = [(r0+H)^3 - r0^3] / (3 r0^2)
        double rt = r0 + top;
        double exact = (rt * rt * rt - r0 * r0 * r0) / (3.0 * r0 * r0);

        Assert.AreEqual(exact, summed, exact * 1e-12,
            "the shell factors must tile the shell volume exactly, not to within a midpoint rule");
    }

    /// <summary>The top factor is (1 + H/r_0)^2 - about 1.0158 for 50 km on Earth.</summary>
    [TestMethod]
    public void TheTopGeometricFactorIsTheSquaredRadiusRatio()
    {
        var column = Column.Build(Spherical());
        double ratio = (PhysicalConstants.EarthRadius + column.Options.TopAltitude) /
                       PhysicalConstants.EarthRadius;

        Assert.AreEqual(ratio * ratio, column.TopGeometricFactor, 1e-12);
        Assert.AreEqual(1.0158, column.TopGeometricFactor, 0.0002,
            "50 km above Earth's surface is about 1.6% more area");
    }

    /// <summary>
    /// The central check. An isothermal column with a uniform absorber has a spherical
    /// solution the model never sees: integrating
    /// <c>dF/dr = -2 eps' (F - B) - (2/r) F</c>
    /// directly from the surface gives the flux at the top, and the solver's answer must match
    /// it. Nothing is shared between the two paths but the input numbers.
    /// </summary>
    /// <remarks>
    /// The case is chosen so that the geometric term is the <em>only</em> thing acting: an
    /// isothermal column over a black surface at the same temperature is in exact radiative
    /// equilibrium in plane-parallel geometry, so F+ would stay pinned at sigma T^4 all the way
    /// up. Everything the test sees is therefore sphericity, with no other physics to hide
    /// behind.
    /// </remarks>
    [DataTestMethod]
    [DataRow(1.0e-6, 300.0)]
    [DataRow(4.0e-6, 250.0)]
    [DataRow(2.0e-5, 288.0)]
    public void TheSolverMatchesADirectIntegrationOfTheSphericalEquation(
        double emissionCoefficient, double temperature)
    {
        const double top = 50_000.0;
        const double r0 = PhysicalConstants.EarthRadius;

        var options = Spherical();
        options.SegmentCount = 2000;
        options.TopAltitude = top;
        options.SurfaceEmissivity = 1.0;
        options.AtmosphericShortwaveFraction = 0.0;
        options.Convection = ConvectionMode.None;

        var column = Column.Build(options);
        column.SurfaceTemperature = temperature;
        foreach (var s in column.Segments)
        {
            s.Temperature = temperature;
            s.EmissionCoefficient = emissionCoefficient;
        }

        var rad = RadiationSolver.Solve(column);

        // What the solver reports is power per unit surface area; the flux at the top is that
        // spread over the area up there.
        double solver = rad.OutgoingLongwave / column.TopGeometricFactor;

        // Independent reference: fourth-order Runge-Kutta on the spherical equation, with the
        // diffusivity and the emission law taken from the constants rather than the solver.
        double b = PhysicalConstants.StefanBoltzmann * Math.Pow(temperature, 4);
        double d = options.Diffusivity;

        double Derivative(double r, double f) =>
            -d * emissionCoefficient * (f - b) - 2.0 * f / r;

        const int steps = 400_000;
        double h = top / steps;
        double flux = b;   // a black surface at the column's own temperature
        for (int k = 0; k < steps; k++)
        {
            double r = r0 + k * h;
            double k1 = Derivative(r, flux);
            double k2 = Derivative(r + 0.5 * h, flux + 0.5 * h * k1);
            double k3 = Derivative(r + 0.5 * h, flux + 0.5 * h * k2);
            double k4 = Derivative(r + h, flux + h * k3);
            flux += h * (k1 + 2.0 * k2 + 2.0 * k3 + k4) / 6.0;
        }

        // The residual is the solver holding the shell factor constant across each segment,
        // second order in dz. It must be far smaller than the effect being measured.
        double effect = Math.Abs(b - flux);
        double residual = Math.Abs(solver - flux);

        Assert.IsTrue(residual < 0.01 * effect,
            $"solver {solver:F5} against a direct integration {flux:F5} W/m2; the residual " +
            $"{residual:E2} is not small against the {effect:F3} W/m2 the geometry moves");
    }

    /// <summary>
    /// A large enough planet is flat. Pushing the radius up must reproduce the plane-parallel
    /// answer, and this is the test that would catch a term with the wrong sign - a sign error
    /// converges to the same place from the wrong side but never converges to nothing.
    /// </summary>
    [TestMethod]
    public void AnInfiniteRadiusRecoversPlaneParallelGeometry()
    {
        var flat = TestSupport.Default;

        double[] radii = { 6.371e6, 6.371e8, 6.371e10 };
        var errors = radii
            .Select(r => Math.Abs(
                TestSupport.Equilibrium($"spherical-{r:E0}", () => Spherical(r)).SurfaceTemperature
                - flat.SurfaceTemperature))
            .ToArray();

        for (int i = 1; i < radii.Length; i++)
        {
            Assert.IsTrue(errors[i] < errors[i - 1],
                $"radius {radii[i]:E1} m is further from the plane-parallel answer " +
                $"({errors[i]:E2} K) than {radii[i - 1]:E1} m was ({errors[i - 1]:E2} K)");
        }

        // Each hundredfold rise in radius should cut the discrepancy by about the same factor,
        // since the correction is O(H/r).
        Assert.IsTrue(errors[^1] < 1e-5,
            $"at a radius of {radii[^1]:E1} m the geometry still moves the surface by " +
            $"{errors[^1]:E2} K; the correction is not vanishing as 1/r");
    }

    /// <summary>
    /// The energy budget still closes. Sphericity redistributes emission and adds mass, so
    /// this is the test that would catch power appearing or vanishing in the rescaling.
    /// </summary>
    [TestMethod]
    public void TheEnergyBudgetClosesOnASphere()
    {
        var result = TestSupport.Equilibrium("spherical-earth", () => Spherical());
        var options = result.Column.Options;

        Assert.IsTrue(result.Converged, "the spherical column must reach equilibrium");

        Assert.AreEqual(options.AbsorbedSolarFlux, result.Radiation.OutgoingLongwave,
            options.FluxTolerance,
            "per unit surface area, what leaves the top must equal what the planet absorbs");

        double eps = options.SurfaceEmissivity;
        double net = result.Column.SurfaceShortwaveAbsorbed
                   + eps * result.Radiation.SurfaceDownwardFlux
                   - result.SurfaceEmission
                   - result.SensibleHeatFlux
                   - result.LatentHeatFlux;

        Assert.IsTrue(Math.Abs(net) < options.FluxTolerance,
            $"surface budget leaves {net:E3} W/m2 unaccounted for on a sphere");
    }

    /// <summary>
    /// The two independent expressions of a shell's emission agree: the solver's exponential
    /// form and the Koenigsberger law integrated over the shell volume. Both had to pick up the
    /// same geometric factor, and they are written in different files.
    /// </summary>
    [TestMethod]
    public void ShellEmissionAgreesWithTheKoenigsbergerLawOverTheShellVolume()
    {
        var options = Spherical();
        options.SegmentCount = 400;
        options.TotalOpticalDepth = 1e-4;      // thin, where the two forms must coincide
        options.AtmosphericShortwaveFraction = 0.0;
        options.Convection = ConvectionMode.None;

        var column = Column.Build(options);
        var rad = RadiationSolver.Solve(column);

        for (int i = 0; i < column.Count; i++)
        {
            double solver = rad.SegmentEmission[i];
            double law = rad.KoenigsbergerEmission[i];

            Assert.AreEqual(law, solver, law * 1e-3,
                $"segment {i}: the solver emits {solver:E4} W/m2 where 4 eps' sigma T^4 dV over " +
                $"the shell gives {law:E4}");
        }
    }

    /// <summary>
    /// A shell holds the mass a shell holds, so its heat capacity carries the same factor. The
    /// radial column density must <em>not</em> carry it: optical depth is a path integral along
    /// a ray, and a wider shell is no more opaque from below.
    /// </summary>
    [TestMethod]
    public void HeatCapacityScalesWithTheShellButOpticalDepthDoesNot()
    {
        var flat = Column.Build(new ModelOptions());
        var round = Column.Build(Spherical());

        for (int i = 0; i < flat.Count; i++)
        {
            var a = flat.Segments[i];
            var b = round.Segments[i];

            Assert.AreEqual(a.MassPerArea, b.MassPerArea, a.MassPerArea * 1e-12,
                $"segment {i}: the radial column density must not change with geometry");

            Assert.AreEqual(a.OpticalThickness(2.0), b.OpticalThickness(2.0),
                a.OpticalThickness(2.0) * 1e-12,
                $"segment {i}: optical depth is a radial path integral and must not change");

            Assert.AreEqual(a.HeatCapacity * b.ShellVolumeFactor, b.HeatCapacity,
                b.HeatCapacity * 1e-12,
                $"segment {i}: heat capacity must carry the shell factor exactly once");
        }

        // The factor has to actually do something, or the three assertions above are vacuous.
        Assert.IsTrue(round.Segments[^1].ShellVolumeFactor > 1.015,
            $"the top shell factor is only {round.Segments[^1].ShellVolumeFactor:F6}; if the " +
            "geometry is not being applied, this whole class proves nothing");
    }

    /// <summary>
    /// Sphericity composes with the band machinery. The shell factor is applied once, to the
    /// emitted power, so it has to survive being split across a window, a continuum and a
    /// k-distribution quadrature without being counted twice or dropped.
    /// </summary>
    [TestMethod]
    public void TheBudgetClosesWithBandsAndAQuadratureOnASphere()
    {
        var result = TestSupport.Equilibrium("spherical-banded", () =>
        {
            var o = Spherical();
            o.WindowShortWavelength = 8e-6;
            o.WindowLongWavelength = 13e-6;
            o.WaterVapourOpticalDepth = 0.4;   // the continuum is a vapour continuum
            o.WindowContinuumOpticalDepth = 1.2;
            o.KDistributionShape = KDistributionShape.Lognormal;
            o.KDistributionWidth = 1.5;
            o.KDistributionPoints = 8;
            return o;
        });

        var options = result.Column.Options;

        Assert.IsTrue(result.Converged, "the banded spherical column must reach equilibrium");
        Assert.AreEqual(options.AbsorbedSolarFlux, result.Radiation.OutgoingLongwave,
            options.FluxTolerance,
            "the shell factor must not create or destroy power when split across bands");

        // Every band's own emission must carry the factor, so the summed segment emission still
        // matches the Koenigsberger law over the shell volume in the thin upper segments.
        int top = result.Column.Count - 1;
        Assert.IsTrue(result.Column.Segments[top].ShellVolumeFactor > 1.015,
            "the top segment must actually be a larger shell for this test to mean anything");
    }

    /// <summary>
    /// What sphericity actually does to the answer, recorded so it stays visible: it cools the
    /// surface by 0.016 K on Earth. Two orders of magnitude smaller than the latent flux, and
    /// far smaller than the 1.6% area change might suggest, because the extra mass sits in the
    /// thin cold stratosphere while the greenhouse effect is made near the ground where the
    /// factor is still 1.
    /// </summary>
    [TestMethod]
    public void SphericityCoolsTheSurfaceSlightly()
    {
        var flat = TestSupport.Default;
        var round = TestSupport.Equilibrium("spherical-earth", () => Spherical());

        double shift = round.SurfaceTemperature - flat.SurfaceTemperature;

        Assert.IsTrue(shift < 0.0,
            $"sphericity moved the surface by {shift:+0.000;-0.000} K; the extra emitting mass " +
            "aloft cannot warm it");
        Assert.AreEqual(-0.016, shift, 0.005,
            $"the shift is {shift:F4} K, not the ~-0.016 K recorded in the README");

        // Whereas a small planet is a large effect, which is the check that the smallness above
        // is a property of Earth rather than of the implementation doing nothing.
        var small = TestSupport.Equilibrium("spherical-small", () => Spherical(2.0e5));
        double bigShift = small.SurfaceTemperature - flat.SurfaceTemperature;

        Assert.IsTrue(bigShift < 10.0 * shift,
            $"a 200 km planet shifted the surface by only {bigShift:F3} K against Earth's " +
            $"{shift:F3} K; the correction should scale with H/r");
    }
}
