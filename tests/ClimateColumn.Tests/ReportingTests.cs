using System.Globalization;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// The CSV and console renderings. The substantive constraint is that fluxes are interface
/// quantities and temperatures are segment quantities, so the two must never share a row -
/// a plot of flux against z would otherwise be displaced by half a segment.
/// </summary>
[TestClass]
public class ReportingTests
{
    private const int Segments = 12;

    private static ModelResult Result => TestSupport.Equilibrium("reporting",
        () => new ModelOptions { SegmentCount = Segments });

    private static string[] CsvLines() =>
        Reporting.ToCsv(Result).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    [TestMethod]
    public void CsvHasOneRowPerSegmentAndInterfacePlusTheSurface()
    {
        // header + top interface + (segment, interface) x n + surface
        Assert.AreEqual(2 * Segments + 3, CsvLines().Length,
            "unexpected CSV row count");
    }

    [TestMethod]
    public void CsvIsRectangular()
    {
        var lines = CsvLines();
        int columns = lines[0].Split(',').Length;

        foreach (var line in lines)
        {
            Assert.AreEqual(columns, line.Split(',').Length, $"ragged row: {line}");
        }
    }

    [TestMethod]
    public void CsvKeepsInterfaceFluxesAndSegmentTemperaturesOnSeparateRows()
    {
        var lines = CsvLines();
        var header = lines[0].Split(',');
        int temperatureIndex = Array.IndexOf(header, "temperature_K");
        int fluxIndex = Array.IndexOf(header, "flux_up_W_m2");

        foreach (var line in lines[1..])
        {
            var fields = line.Split(',');
            bool hasFlux = fields[fluxIndex].Length > 0;
            bool hasTemperature = fields[temperatureIndex].Length > 0;

            switch (fields[0])
            {
                case "SEGMENT" or "SURFACE":
                    Assert.IsFalse(hasFlux, $"{fields[0]} row must not carry an interface flux");
                    Assert.IsTrue(hasTemperature, $"{fields[0]} row must carry a temperature");
                    break;

                case "INTERFACE" or "INTERFACE_TOA" or "INTERFACE_SFC":
                    Assert.IsTrue(hasFlux, $"{fields[0]} row must carry a flux");
                    Assert.IsFalse(hasTemperature, $"{fields[0]} row must not carry a temperature");
                    break;
            }
        }
    }

    [TestMethod]
    public void CsvPlacesSegmentsAndInterfacesAtTheirOwnAltitudes()
    {
        var lines = CsvLines();
        var header = lines[0].Split(',');
        int zIndex = Array.IndexOf(header, "z_m");

        double? lowestSegmentZ = null;
        double? surfaceInterfaceZ = null;

        foreach (var line in lines[1..])
        {
            var fields = line.Split(',');
            if (fields[zIndex].Length == 0) continue;
            double z = double.Parse(fields[zIndex], CultureInfo.InvariantCulture);

            if (fields[0] == "SEGMENT" && fields[1] == "0") lowestSegmentZ = z;
            if (fields[0] == "INTERFACE_SFC") surfaceInterfaceZ = z;
        }

        double dz = Result.Column.Options.TopAltitude / Segments;

        Assert.IsNotNull(lowestSegmentZ, "no lowest segment row found");
        Assert.IsNotNull(surfaceInterfaceZ, "no surface interface row found");
        Assert.AreEqual(dz / 2, lowestSegmentZ!.Value, 1e-6, "segment 0 must sit at dz/2");
        Assert.AreEqual(0.0, surfaceInterfaceZ!.Value, 1e-9, "its bottom interface must sit at z = 0");
    }

    [TestMethod]
    public void CsvUsesInvariantNumberFormatting()
    {
        string csv = Reporting.ToCsv(Result);
        var lines = CsvLines();
        var header = lines[0].Split(',');
        int temperatureIndex = Array.IndexOf(header, "temperature_K");

        Assert.IsFalse(csv.Contains(';'), "a semicolon means a locale-dependent list separator");
        Assert.IsTrue(lines[2].Split(',')[temperatureIndex].Contains('.', StringComparison.Ordinal),
            "temperatures must use a decimal point");
    }

    [TestMethod]
    public void ProfileTableRendersEverySegmentAndInterface()
    {
        string report = Reporting.FormatProfile(Result);

        Assert.AreEqual(Segments, TestSupport.CountOccurrences(report, "\n  seg "),
            "one row per segment");
        Assert.AreEqual(Segments, TestSupport.CountOccurrences(report, "\n  ifc "),
            "one row per interface below the top");
        Assert.AreEqual(1, TestSupport.CountOccurrences(report, "\n  TOA "),
            "exactly one top-of-atmosphere row");
    }

    [TestMethod]
    public void SummaryReportsTheSurfaceTemperature()
    {
        Assert.IsTrue(Reporting.FormatSummary(Result).Contains("surface temperature"),
            "the summary must report the headline result");
    }
}
