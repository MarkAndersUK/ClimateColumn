using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Intercepting sunlight on the top-of-atmosphere disc rather than the solid planet's.
/// </summary>
/// <remarks>
/// The temptation here is to multiply the absorbed solar by the area ratio (r_top/r_0)^2 and
/// call it done. That is wrong twice over, and most of what follows exists to hold the line
/// against it. The extra light arrives on <em>limb</em> paths - impact parameters between r_0
/// and r_top - which graze through the atmosphere and out the other side. So it never touches
/// the ground, meaning the surface albedo has nothing to act through and the surface gets none
/// of it; and only part of it is absorbed at all, because a ray grazing at 40 km passes through
/// almost no air.
/// </remarks>
[TestClass]
public class LimbInterceptionTests
{
    private static ModelOptions Limb() => new() { TopOfAtmosphereInterception = true };

    /// <summary>Off by default, and then identically absent rather than merely small.</summary>
    [TestMethod]
    public void TheDefaultInterceptsOnThePlanetsOwnDisc()
    {
        var result = TestSupport.Default;

        Assert.IsFalse(result.Column.Options.TopOfAtmosphereInterception,
            "the top-of-atmosphere disc must stay off by default");
        Assert.AreEqual(0.0, result.Column.LimbShortwaveAbsorbed, 0.0);
        Assert.AreEqual(result.Column.Options.AbsorbedSolarFlux,
            result.Column.TotalShortwaveAbsorbed, 0.0,
            "with no limb term the total absorbed must be exactly the disc term");
    }

    /// <summary>
    /// The annulus intercepts what its area says it does. This is the one quantity the naive
    /// rescaling gets right, so it is worth pinning: the disagreement is about the <em>fate</em>
    /// of this power, not its amount.
    /// </summary>
    [TestMethod]
    public void TheAnnulusInterceptsWhatItsAreaImplies()
    {
        var column = Column.Build(Limb());
        var options = column.Options;

        // S0 * pi (r_top^2 - r_0^2) / (4 pi r_0^2), per unit of the planet's surface area.
        double intercepted = 0.25 * options.SolarConstant * (column.TopRadiusRatioSquared - 1.0);

        Assert.AreEqual(5.362, intercepted, 0.01,
            $"the annulus should intercept about 5.36 W/m2 of planet surface, not {intercepted:F3}");

        // And the absorbed part must be a strict fraction of it - not all, and not none.
        Assert.IsTrue(column.LimbShortwaveAbsorbed > 0.0,
            "a limb path through 50 km of atmosphere must absorb something");
        Assert.IsTrue(column.LimbShortwaveAbsorbed < intercepted,
            $"absorbed {column.LimbShortwaveAbsorbed:F3} W/m2 of {intercepted:F3} intercepted; a " +
            "ray grazing near the top of the column cannot be fully absorbed");
    }

    /// <summary>
    /// The naive rescaling of the absorbed solar by the area ratio is a third larger than the
    /// real absorption, and would put most of it in the wrong place. Recorded as a test because
    /// the naive version is the obvious thing to write and looks perfectly reasonable.
    /// </summary>
    [TestMethod]
    public void TheNaiveAreaRescalingWouldOverstateTheEffect()
    {
        var column = Column.Build(Limb());
        var options = column.Options;

        double naive = options.AbsorbedSolarFlux * (column.TopRadiusRatioSquared - 1.0);
        double real = column.LimbShortwaveAbsorbed;

        Assert.IsTrue(real < 0.8 * naive,
            $"the limb calculation gives {real:F3} W/m2 against the naive {naive:F3}; if these " +
            "agree, the slant path is not being resolved");

        Assert.AreEqual(3.753, naive, 0.01, "the naive rescaling adds about 3.75 W/m2");
        Assert.AreEqual(2.563, real, 0.05, "the limb calculation absorbs about 2.56 W/m2");
    }

    /// <summary>
    /// None of the limb energy reaches the surface. This is the assertion the naive rescaling
    /// would break most badly: it would route 78% of the extra flux to the ground.
    /// </summary>
    [TestMethod]
    public void NoLimbEnergyReachesTheSurface()
    {
        var flat = Column.Build(new ModelOptions());
        var limb = Column.Build(Limb());

        Assert.AreEqual(flat.SurfaceShortwaveAbsorbed, limb.SurfaceShortwaveAbsorbed, 1e-12,
            "a ray that misses the planet cannot warm its surface");

        // Every joule of it therefore lands in the atmosphere.
        double atmosphere = limb.Segments.Sum(s => s.ShortwaveAbsorbed) -
                            flat.Segments.Sum(s => s.ShortwaveAbsorbed);

        Assert.AreEqual(limb.LimbShortwaveAbsorbed, atmosphere, limb.LimbShortwaveAbsorbed * 1e-9,
            $"the limb term absorbed {limb.LimbShortwaveAbsorbed:F4} W/m2 but the segments only " +
            $"gained {atmosphere:F4}");
    }

    /// <summary>
    /// The shortwave budget closes: what the segments and the surface absorb is exactly what the
    /// planet is said to absorb. This is what would catch the limb term being counted in the
    /// total but never deposited, or deposited twice.
    /// </summary>
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TheShortwaveBudgetClosesOverTheWholeColumn(bool limb)
    {
        var column = Column.Build(new ModelOptions { TopOfAtmosphereInterception = limb });

        double deposited = column.SurfaceShortwaveAbsorbed +
                           column.Segments.Sum(s => s.ShortwaveAbsorbed);

        Assert.AreEqual(column.TotalShortwaveAbsorbed, deposited,
            column.TotalShortwaveAbsorbed * 1e-12,
            $"the column absorbs {column.TotalShortwaveAbsorbed:F6} W/m2 but only " +
            $"{deposited:F6} is deposited anywhere");
    }

    /// <summary>
    /// Limb energy lands high. A grazing ray is absorbed where the air first becomes thick
    /// enough along its slant path, which is far above where the vertical beam deposits.
    /// </summary>
    [TestMethod]
    public void LimbEnergyIsDepositedWellAboveTheVerticalBeam()
    {
        var flat = Column.Build(new ModelOptions());
        var limb = Column.Build(Limb());

        double Centroid(Func<Segment, double> weight)
        {
            double sum = 0.0, moment = 0.0;
            foreach (var s in limb.Segments)
            {
                double w = weight(s);
                sum += w;
                moment += w * s.MidAltitude;
            }
            return sum > 0 ? moment / sum : 0.0;
        }

        double verticalCentroid = Centroid(s => flat.Segments[s.Index].ShortwaveAbsorbed);
        double limbCentroid = Centroid(s =>
            s.ShortwaveAbsorbed - flat.Segments[s.Index].ShortwaveAbsorbed);

        Assert.IsTrue(limbCentroid > verticalCentroid,
            $"limb absorption centres at {limbCentroid / 1000.0:F1} km against the vertical " +
            $"beam's {verticalCentroid / 1000.0:F1} km; grazing paths must deposit higher");

        Assert.IsTrue(limbCentroid > 10_000.0,
            $"limb absorption centres at only {limbCentroid / 1000.0:F1} km; a tangent ray " +
            "cannot be depositing that low");
    }

    /// <summary>
    /// The impact-parameter quadrature is converged: halving and doubling the point count moves
    /// the answer by far less than the answer itself.
    /// </summary>
    [DataTestMethod]
    [DataRow(200)]
    [DataRow(800)]
    [DataRow(3200)]
    public void TheLimbQuadratureIsConverged(int points)
    {
        var reference = Column.Build(Limb()).LimbShortwaveAbsorbed;

        var options = Limb();
        options.LimbQuadraturePoints = points;
        double actual = Column.Build(options).LimbShortwaveAbsorbed;

        double error = Math.Abs(actual - reference) / reference;

        Assert.IsTrue(error < 2e-3,
            $"{points} impact parameters gives {actual:F5} W/m2 against the default's " +
            $"{reference:F5}, a {100 * error:F3}% shift - the default is not converged");
    }

    /// <summary>
    /// A transparent atmosphere absorbs nothing on any path, limb included. The limb term must
    /// not manufacture absorption out of geometry alone.
    /// </summary>
    [TestMethod]
    public void ATransparentAtmosphereAbsorbsNoLimbLight()
    {
        var options = Limb();
        options.AtmosphericShortwaveFraction = 0.0;

        var column = Column.Build(options);

        Assert.AreEqual(0.0, column.LimbShortwaveAbsorbed, 0.0,
            "with no shortwave absorber the annulus must pass straight through");
    }

    /// <summary>
    /// More absorber means more of the annulus is captured, and the capture saturates towards
    /// the whole annulus rather than exceeding it.
    /// </summary>
    [TestMethod]
    public void MoreAbsorberCapturesMoreOfTheAnnulus()
    {
        double[] fractions = { 0.05, 0.22, 0.5, 0.9 };
        var captured = new double[fractions.Length];

        double intercepted = 0.0;
        for (int i = 0; i < fractions.Length; i++)
        {
            var options = Limb();
            options.AtmosphericShortwaveFraction = fractions[i];
            var column = Column.Build(options);
            captured[i] = column.LimbShortwaveAbsorbed;
            intercepted = 0.25 * options.SolarConstant * (column.TopRadiusRatioSquared - 1.0);
        }

        for (int i = 1; i < fractions.Length; i++)
        {
            Assert.IsTrue(captured[i] > captured[i - 1],
                $"capture fell from {captured[i - 1]:F3} to {captured[i]:F3} W/m2 as the " +
                $"absorber rose from {fractions[i - 1]} to {fractions[i]}");
            Assert.IsTrue(captured[i] < intercepted,
                $"captured {captured[i]:F3} W/m2 of {intercepted:F3} intercepted - the annulus " +
                "cannot give up more than it holds");
        }
    }

    /// <summary>
    /// A vanishingly thin atmosphere has no annulus, so the limb term vanishes and the disc
    /// term is all that is left.
    /// </summary>
    [TestMethod]
    public void AThinAtmosphereHasNoAnnulus()
    {
        var options = Limb();
        options.TopAltitude = 50.0;          // 50 m rather than 50 km
        options.SegmentCount = 10;

        var column = Column.Build(options);
        double intercepted = 0.25 * options.SolarConstant * (column.TopRadiusRatioSquared - 1.0);

        Assert.IsTrue(intercepted < 0.01,
            $"a 50 m atmosphere should present essentially no annulus, not {intercepted:F4} W/m2");
        Assert.IsTrue(column.LimbShortwaveAbsorbed <= intercepted,
            "the limb term cannot exceed what the annulus intercepts");
    }

    /// <summary>
    /// What it does to the answer, and why it is much bigger than the other geometric
    /// refinements: this one changes how much energy the planet absorbs, rather than
    /// redistributing what it already had.
    /// </summary>
    [TestMethod]
    public void LimbInterceptionWarmsTheSurface()
    {
        var flat = TestSupport.Default;
        var limb = TestSupport.Equilibrium("limb", () => Limb());

        Assert.IsTrue(limb.Converged, "the column must reach equilibrium with the limb term");

        double shift = limb.SurfaceTemperature - flat.SurfaceTemperature;
        Assert.IsTrue(shift > 0.0,
            $"absorbing an extra {limb.Column.LimbShortwaveAbsorbed:F3} W/m2 moved the surface " +
            $"{shift:+0.000;-0.000} K; more absorbed energy cannot cool the planet");

        Assert.AreEqual(0.309, shift, 0.02,
            $"the shift is {shift:F4} K, not the ~+0.31 K recorded in the README");

        // At equilibrium the planet exports what it absorbs - including the limb term, which is
        // the check that the new energy entered the budget rather than only the diagnostics.
        Assert.AreEqual(limb.Column.TotalShortwaveAbsorbed, limb.Radiation.OutgoingLongwave,
            limb.Column.Options.FluxTolerance,
            "the outgoing longwave must balance the total absorbed, limb included");
        Assert.IsTrue(limb.Radiation.OutgoingLongwave >
                      flat.Column.Options.AbsorbedSolarFlux + 1.0,
            "the outgoing longwave should have risen by the limb absorption");
    }

    /// <summary>
    /// The three geometric refinements compose, and their effects are of very different sizes:
    /// the two that redistribute energy nearly cancel at a few hundredths of a kelvin, while the
    /// one that changes how much energy arrives is an order of magnitude larger.
    /// </summary>
    [TestMethod]
    public void TheGeometricCorrectionsComposeAndTheSolarOneDominates()
    {
        double flat = TestSupport.Default.SurfaceTemperature;

        double all = TestSupport.Equilibrium("all-geometry", () => new ModelOptions
        {
            SphericalGeometry = true,
            VariableGravity = true,
            TopOfAtmosphereInterception = true
        }).SurfaceTemperature - flat;

        double solarOnly = TestSupport.Equilibrium("limb", () => Limb()).SurfaceTemperature - flat;

        Assert.IsTrue(all > 0.0,
            $"all three together moved the surface {all:+0.000;-0.000} K; the solar term should " +
            "dominate and it warms");
        Assert.IsTrue(Math.Abs(all - solarOnly) < 0.05,
            $"together they give {all:F3} K against the solar term's {solarOnly:F3} K alone; the " +
            "other two should contribute only hundredths of a kelvin");
    }
}
