using System.Drawing;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Charts.Tests;

/// <summary>
/// Covers the coupled-gas figure. The points are made here rather than run, because what is
/// under test is the drawing - that it fills a bitmap at the size asked for, and that the two
/// curves and the band between them survive a run where methane contributes nothing.
/// </summary>
[TestClass]
public class ScenarioChartTests
{
    public TestContext TestContext { get; set; } = null!;

    private string Path_(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "climatecolumn-scenario-tests",
            TestContext.TestName ?? "unnamed");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    private static ScenarioPoint[] Points(double methaneShare = 0.2) =>
        Co2Sweep.Concentrations.Select(ppm =>
        {
            double both = 5.0 * Math.Log(ppm / Co2Sweep.Concentrations[0]) / Math.Log(1000.0 / 285.0);
            return new ScenarioPoint(ppm, ScenarioSweep.MethaneFor(ppm),
                both, both * (1.0 - methaneShare));
        }).ToArray();

    [TestMethod]
    public void WritesAValidPngAtTheRequestedSize()
    {
        string path = Path_("scenario.png");
        ScenarioChartExport.SavePng(path, Points(), ChartTheme.Light, 1040, 640);

        using var image = new Bitmap(path);
        Assert.AreEqual(1040, image.Width);
        Assert.AreEqual(640, image.Height);
    }

    [TestMethod]
    public void ClampsSizesTooSmallToDrawIn()
    {
        string path = Path_("tiny.png");
        ScenarioChartExport.SavePng(path, Points(), ChartTheme.Dark, 10, 10);

        using var image = new Bitmap(path);
        Assert.IsTrue(image.Width >= 700 && image.Height >= 400);
    }

    [TestMethod]
    public void DrawsWithoutFailingWhenMethaneAddsNothing()
    {
        // The two curves coincide and the band between them has zero area. Nothing here should
        // divide by that.
        string path = Path_("no-methane.png");
        ScenarioChartExport.SavePng(path, Points(methaneShare: 0.0), ChartTheme.Light, 800, 500);

        Assert.IsTrue(new FileInfo(path).Length > 0);
    }

    [TestMethod]
    public void PaintsSomethingOtherThanTheBackground()
    {
        using var bitmap = new Bitmap(900, 560);
        using (var g = Graphics.FromImage(bitmap))
        {
            ScenarioChartPainter.Paint(g, new Rectangle(0, 0, 900, 560), Points(),
                ScenarioSweep.CouplingNote, ChartTheme.Light);
        }

        var surface = ChartTheme.Light.Surface;
        int drawn = 0;
        for (int y = 0; y < bitmap.Height; y += 4)
            for (int x = 0; x < bitmap.Width; x += 4)
                if (bitmap.GetPixel(x, y).ToArgb() != surface.ToArgb()) drawn++;

        Assert.IsTrue(drawn > 200, $"only {drawn} sampled pixels differ from the surface");
    }

    [TestMethod]
    public void DrawsNothingRatherThanThrowingOnTooFewPoints()
    {
        using var bitmap = new Bitmap(400, 300);
        using var g = Graphics.FromImage(bitmap);

        ScenarioChartPainter.Paint(g, new Rectangle(0, 0, 400, 300),
            Array.Empty<ScenarioPoint>(), "", ChartTheme.Light);

        Assert.AreEqual(ChartTheme.Light.Surface.ToArgb(), bitmap.GetPixel(200, 150).ToArgb());
    }
}
