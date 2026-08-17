using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The non-radiative transport: the bulk surface flux, and the critical-lapse-rate
/// adjustment of the atmospheric segments.
/// </summary>
[TestClass]
public class ConvectionTests
{
    private static Column SuperadiabaticColumn()
    {
        var column = Column.Build(new ModelOptions
        {
            SegmentCount = 25, Convection = ConvectionMode.Full
        });

        column.SurfaceTemperature = 340.0;
        for (int i = 0; i < column.Count; i++)
            column.Segments[i].Temperature = 320.0 - 0.020 * column.Segments[i].MidAltitude;

        return column;
    }

    [TestMethod]
    public void AdjustmentConservesEnthalpy()
    {
        var column = SuperadiabaticColumn();
        double before = column.AtmosphericEnthalpy();

        int touched = ConvectionSolver.Adjust(column);

        Assert.IsTrue(touched > 0, "a strongly superadiabatic profile must be adjusted");
        Assert.AreEqual(1.0, column.AtmosphericEnthalpy() / before, 1e-12,
            "the adjustment must conserve sum(C T)");
    }

    /// <summary>
    /// The surface exchanges heat with the air only through radiation and h_c; letting the
    /// adjustment mix the surface reservoir as well would double count that transfer and
    /// leave the surface energy budget open.
    /// </summary>
    [TestMethod]
    public void AdjustmentLeavesTheSurfaceReservoirUntouched()
    {
        var column = SuperadiabaticColumn();
        double before = column.SurfaceTemperature;

        ConvectionSolver.Adjust(column);

        Assert.AreEqual(before, column.SurfaceTemperature, 0.0,
            "the surface must be excluded from the adjustment");
    }

    [TestMethod]
    public void AdjustmentRemovesEverySuperadiabaticLapse()
    {
        var column = SuperadiabaticColumn();
        double gamma = column.Options.CriticalLapseRate;

        ConvectionSolver.Adjust(column);

        for (int i = 0; i < column.Count - 1; i++)
        {
            var lower = column.Segments[i];
            var upper = column.Segments[i + 1];

            Assert.IsTrue(
                lower.Temperature - upper.Temperature <=
                gamma * (upper.MidAltitude - lower.MidAltitude) + 1e-9,
                $"segment {i} is left superadiabatic after adjustment");
        }
    }

    [TestMethod]
    public void AdjustmentIsANoOpWhenConvectionIsDisabled()
    {
        var column = Column.Build(new ModelOptions
        {
            SegmentCount = 25, Convection = ConvectionMode.None
        });
        double before = column.Enthalpy();

        int touched = ConvectionSolver.Adjust(column);

        Assert.AreEqual(0, touched, "no segment may be touched when convection is off");
        Assert.AreEqual(before, column.Enthalpy(), 1e-12, "enthalpy must be untouched");
    }

    [DataTestMethod]
    [DataRow(0.0, 5.8)]
    [DataRow(3.0, 18.1)]
    public void SurfaceHeatTransferCoefficientMatchesTheKoenigsbergerRelation(
        double windSpeed, double expected)
    {
        Assert.AreEqual(expected, ConvectionSolver.SurfaceHeatTransferCoefficient(windSpeed), 1e-12,
            $"h_c = 5.8 + 4.1 v at v = {windSpeed}");
    }

    [TestMethod]
    public void SurfaceHeatTransferCoefficientIsLinearInWindSpeed()
    {
        Assert.AreEqual(4.1,
            ConvectionSolver.SurfaceHeatTransferCoefficient(5.0) -
            ConvectionSolver.SurfaceHeatTransferCoefficient(4.0), 1e-12,
            "h_c must have slope 4.1 W/m2/K per m/s");
    }

    [TestMethod]
    public void SolAirTemperatureReducesToAirTemperatureWithNoNetGain()
    {
        Assert.AreEqual(290.0, ConvectionSolver.SolAirTemperature(290.0, 120.0, 120.0, 3.0), 1e-12,
            "solar gain balancing longwave loss leaves the air temperature unchanged");
    }

    [TestMethod]
    public void SolAirTemperatureOffsetIsTheNetGainOverHc()
    {
        Assert.AreEqual(290.0 + 100.0 / 18.1,
            ConvectionSolver.SolAirTemperature(290.0, 200.0, 100.0, 3.0), 1e-9,
            "sol-air offset = (SW - LW) / h_c");
    }

    /// <summary>
    /// Using the lowest segment's temperature directly would evaluate the air at z = dz/2
    /// rather than at the surface, which puts an O(dz) error into the sensible heat flux and
    /// makes the whole model first order in dz.
    /// </summary>
    [TestMethod]
    public void NearSurfaceAirTemperatureExtrapolatesToTheGround()
    {
        var column = Column.Build(new ModelOptions { SegmentCount = 10, TopAltitude = 10_000 });
        const double lapse = 0.006;
        const double ground = 300.0;
        foreach (var s in column.Segments) s.Temperature = ground - lapse * s.MidAltitude;

        Assert.AreEqual(ground, ConvectionSolver.NearSurfaceAirTemperature(column), 1e-9,
            "an exact 6 K/km profile must extrapolate back to its ground value");

        Assert.IsTrue(
            Math.Abs(ConvectionSolver.NearSurfaceAirTemperature(column) -
                     column.Segments[0].Temperature) > 2.0,
            "the extrapolation must differ materially from the lowest segment's temperature");
    }

    [TestMethod]
    public void SingleSegmentColumnFallsBackToThatSegmentsTemperature()
    {
        var column = Column.Build(new ModelOptions { SegmentCount = 1, TopAltitude = 10_000 });

        Assert.AreEqual(column.Segments[0].Temperature,
            ConvectionSolver.NearSurfaceAirTemperature(column), 0.0,
            "with nothing to extrapolate from, use the segment itself");
    }
}
