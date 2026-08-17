using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The standard atmosphere the column is built from, and the mass and absorber bookkeeping
/// that follows from it.
/// </summary>
[TestClass]
public class ColumnGeometryTests
{
    [DataTestMethod]
    [DataRow(0.0, 288.15, 1e-6)]
    [DataRow(11_000.0, 216.65, 1e-6)]
    public void StandardAtmosphereTemperatureMatchesPublishedValues(
        double altitude, double expected, double tolerance)
    {
        Assert.AreEqual(expected, StandardAtmosphere.Temperature(altitude), tolerance,
            $"US Std Atm T({altitude / 1000.0} km)");
    }

    [DataTestMethod]
    [DataRow(0.0, 101_325.0, 1e-3)]
    [DataRow(11_000.0, 22_632.0, 2.0)]
    [DataRow(20_000.0, 5474.9, 2.0)]
    public void StandardAtmospherePressureMatchesPublishedValues(
        double altitude, double expected, double tolerance)
    {
        Assert.AreEqual(expected, StandardAtmosphere.Pressure(altitude), tolerance,
            $"US Std Atm p({altitude / 1000.0} km)");
    }

    [TestMethod]
    public void StandardAtmosphereSurfaceDensityMatchesPublishedValue()
    {
        Assert.AreEqual(1.225, StandardAtmosphere.Density(0), 1e-3, "US Std Atm rho(0 km)");
    }

    [TestMethod]
    public void ColumnMassIsTheHydrostaticPressureDifference()
    {
        var column = Column.Build(new ModelOptions { SegmentCount = 200, TopAltitude = 84_000 });
        double expected = (StandardAtmosphere.Pressure(0) - StandardAtmosphere.Pressure(84_000)) /
                          PhysicalConstants.Gravity;

        Assert.AreEqual(expected, column.MassPerArea, 1e-6, "column mass = dp/g");
        Assert.IsTrue(Math.Abs(column.MassPerArea - 101325.0 / 9.80665) < 1.0,
            "an 84 km column carries essentially the whole atmosphere, p_s/g");
    }

    [TestMethod]
    public void SegmentMassesSumToTheColumnMass()
    {
        var column = Column.Build(new ModelOptions { SegmentCount = 200, TopAltitude = 84_000 });

        double sum = 0.0;
        foreach (var s in column.Segments) sum += s.MassPerArea;

        Assert.AreEqual(column.MassPerArea, sum, 1e-6, "segment masses must partition the column");
    }

    [DataTestMethod]
    [DataRow(0.0)]
    [DataRow(0.5)]
    [DataRow(1.8)]
    [DataRow(6.0)]
    public void OpticalDepthIsNormalisedToTheRequestedTotal(double target)
    {
        var column = Column.Build(new ModelOptions { TotalOpticalDepth = target, SegmentCount = 40 });

        Assert.AreEqual(target, column.TotalOpticalDepth(), 1e-9,
            $"column optical depth normalised to {target}");
    }

    [TestMethod]
    public void WellMixedAbsorberThinsWithAltitude()
    {
        var column = Column.Build(new ModelOptions { SegmentCount = 40 });

        Assert.IsTrue(
            column.Segments[0].EmissionCoefficient > column.Segments[^1].EmissionCoefficient,
            "eps' ~ rho must decrease with altitude for a well-mixed absorber");
    }
}
