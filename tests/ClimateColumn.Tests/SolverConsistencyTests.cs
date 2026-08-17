using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The correspondence between the Koenigsberger emission law and the two-stream recurrence
/// the solver actually runs, and the conservation properties of that recurrence.
/// </summary>
[TestClass]
public class SolverConsistencyTests
{
    /// <summary>
    /// The solver's hemispheric emission is 2 (1 - exp(-D eps' dz)) sigma T^4, whose
    /// linearisation in dz is 2 D eps' dz sigma T^4. That equals the Koenigsberger form
    /// 4 eps' sigma T^4 dz if and only if D = 2. This checks that identity, and that it
    /// genuinely breaks at other D - which is the whole content of the correspondence.
    /// </summary>
    [DataTestMethod]
    [DataRow(2.0, 1.0)]
    [DataRow(1.66, 0.83)]
    [DataRow(1.0, 0.5)]
    [DataRow(4.0, 2.0)]
    public void SolverEmissionMatchesKoenigsbergerOnlyAtDiffusivityTwo(
        double diffusivity, double expectedRatio)
    {
        var column = Column.Build(new ModelOptions
        {
            SegmentCount = 4000, TopAltitude = 50_000, Diffusivity = diffusivity
        });
        var rad = RadiationSolver.Solve(column);

        // Use the optically thinnest segment, where the linearisation is tightest.
        int top = column.Count - 1;
        double ratio = rad.SegmentEmission[top] / rad.KoenigsbergerEmission[top];

        Assert.AreEqual(expectedRatio, ratio, 1e-3,
            $"D = {diffusivity}: solver emission / Koenigsberger emission");
    }

    [TestMethod]
    public void ThinLayerAbsorptivityReproducesTheKoenigsbergerEmission()
    {
        var segment = new Segment
        {
            BottomAltitude = 0, TopAltitude = 1.0, EmissionCoefficient = 1e-7, Temperature = 250
        };
        double tau = segment.OpticalThickness(PhysicalConstants.KoenigsbergerDiffusivity);
        double solverEmission = 2.0 * (1.0 - Math.Exp(-tau)) * segment.BlackbodyEmissivePower;

        Assert.AreEqual(1.0, solverEmission / segment.KoenigsbergerEmission, 1e-6,
            "thin layer: 2 a sigma T^4 = 4 eps' sigma T^4 dz");
    }

    /// <summary>
    /// The constant-source recurrence is the exact solution of the Schwarzschild equation
    /// across an isothermal layer, so slicing one isothermal slab into more segments must
    /// not change a single flux. This exercises the recurrence itself, unlike a check
    /// against the thin-layer linearisation.
    /// </summary>
    [TestMethod]
    public void SubdividingAnIsothermalSlabLeavesEveryFluxUnchanged()
    {
        double reference = double.NaN;

        foreach (int n in new[] { 1, 2, 4, 16, 256 })
        {
            var options = new ModelOptions
            {
                SegmentCount = n,
                TopAltitude = 10_000,
                AtmosphericShortwaveFraction = 0.0,
                SurfaceEmissivity = 1.0
            };
            var column = Column.Build(options);

            // Uniform absorber, uniform temperature: total optical depth 2.0 whatever n is.
            foreach (var s in column.Segments)
            {
                s.EmissionCoefficient = 1.0 / (options.Diffusivity * options.TopAltitude) * 2.0;
                s.Temperature = 250.0;
            }
            column.SurfaceTemperature = 300.0;

            double olr = RadiationSolver.Solve(column).OutgoingLongwave;
            if (double.IsNaN(reference)) reference = olr;

            Assert.AreEqual(reference, olr, 1e-9,
                $"OLR must not change when the slab is cut into {n} segments");
        }
    }

    /// <summary>
    /// eps' describes the absorber and must not depend on the two-stream closure, so
    /// changing D must change the optical depth the solver sees.
    /// </summary>
    [DataTestMethod]
    [DataRow(1.0)]
    [DataRow(1.66)]
    [DataRow(2.0)]
    [DataRow(3.0)]
    public void DiffusivityScalesTheOpticalDepthTheSolverSees(double diffusivity)
    {
        var column = Column.Build(new ModelOptions
        {
            TotalOpticalDepth = 1.8, Diffusivity = diffusivity
        });

        Assert.AreEqual(1.8 * diffusivity / 2.0, column.TotalOpticalDepth(), 1e-9,
            $"D = {diffusivity}: column optical depth = (D/2) x loading");
    }

    [TestMethod]
    public void StrongerDiffusivityGivesAWarmerSurface()
    {
        var weak = TestSupport.Equilibrium("D=1.66", () => new ModelOptions
        {
            Diffusivity = PhysicalConstants.ElsasserDiffusivity
        });
        var strong = TestSupport.Default;

        Assert.IsTrue(strong.SurfaceTemperature - weak.SurfaceTemperature > 0.5,
            $"D = 2 must be warmer than D = 1.66 ({strong.SurfaceTemperature:F3} vs " +
            $"{weak.SurfaceTemperature:F3} K)");
    }

    [TestMethod]
    public void NoLongwaveEntersTheTopOfTheColumn()
    {
        var column = Column.Build(new ModelOptions());
        var rad = RadiationSolver.Solve(column);

        Assert.AreEqual(0.0, rad.DownwardFlux[column.Count], 1e-15, "F_down at TOA is zero");
    }

    [TestMethod]
    public void AllFluxesAreNonNegative()
    {
        var column = Column.Build(new ModelOptions());
        var rad = RadiationSolver.Solve(column);

        for (int i = 0; i <= column.Count; i++)
        {
            Assert.IsTrue(rad.UpwardFlux[i] >= 0 && rad.DownwardFlux[i] >= 0,
                $"negative flux at interface {i}");
        }
    }

    /// <summary>
    /// Absorption is computed from the incident fluxes and emission from sigma T^4 - two
    /// different expressions - so their agreement with the flux convergence is a real
    /// constraint on the recurrence rather than an identity.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.0)]
    [DataRow(0.35)]
    public void PerSegmentAbsorptionMinusEmissionEqualsFluxConvergence(double window)
    {
        var column = Column.Build(new ModelOptions { SegmentCount = 30, WindowFraction = window });
        var rad = RadiationSolver.Solve(column);

        for (int i = 0; i < column.Count; i++)
        {
            Assert.AreEqual(rad.RadiativeHeating[i],
                rad.SegmentAbsorption[i] - rad.SegmentEmission[i], 1e-9,
                $"segment {i}: absorbed - emitted must equal the flux convergence");
        }
    }

    /// <summary>
    /// Whatever leaves the surface is either absorbed on the way up or escapes, so the
    /// flux-convergence terms must telescope exactly across the whole column. With a window
    /// the transparent flux appears in both the OLR and the surface upward flux and cancels.
    /// </summary>
    [DataTestMethod]
    [DataRow(0.0)]
    [DataRow(0.35)]
    public void ColumnLongwaveBudgetTelescopes(double window)
    {
        var column = Column.Build(new ModelOptions { SegmentCount = 30, WindowFraction = window });
        var rad = RadiationSolver.Solve(column);

        double absorbed = 0.0, emitted = 0.0;
        for (int i = 0; i < column.Count; i++)
        {
            absorbed += rad.SegmentAbsorption[i];
            emitted += rad.SegmentEmission[i];
        }

        Assert.AreEqual(rad.SurfaceUpwardFlux + emitted - rad.SurfaceDownwardFlux,
            absorbed + rad.OutgoingLongwave, 1e-9,
            "column absorption + OLR = surface upward flux + column emission");
    }
}
