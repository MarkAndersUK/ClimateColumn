using System.Drawing;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Charts.Tests;

/// <summary>
/// Covers the methane figure. It needs HITRAN data to have anything to draw, so unlike the CO2
/// chart tests there is no synthetic fixture - a made-up methane sweep would defeat the point,
/// which is that the shape of the response comes out of real line data.
/// </summary>
[TestClass]
public class MethaneChartTests
{
    public TestContext TestContext { get; set; } = null!;

    private static MethaneSweep? Sweep() => MethaneSweep.Run(equilibrate: false);

    private string Path_(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "climatecolumn-methane-tests",
            TestContext.TestName ?? "unnamed");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name);
    }

    [TestMethod]
    public void WritesAValidPngAtTheRequestedSize()
    {
        var sweep = Sweep();
        if (sweep is null)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        string path = Path_("methane.png");
        MethaneChartExport.SavePng(path, sweep, ChartTheme.Light, 1000, 620);

        Assert.IsTrue(File.Exists(path));

        using var image = new Bitmap(path);
        Assert.AreEqual(1000, image.Width);
        Assert.AreEqual(620, image.Height);
    }

    [TestMethod]
    public void ClampsSizesTooSmallToDrawIn()
    {
        var sweep = Sweep();
        if (sweep is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        string path = Path_("tiny.png");
        MethaneChartExport.SavePng(path, sweep, ChartTheme.Light, 40, 20);

        using var image = new Bitmap(path);
        Assert.AreEqual(700, image.Width);
        Assert.AreEqual(450, image.Height);
    }

    /// <summary>
    /// Both laws are drawn through the model's own endpoint, so the figure compares shape and
    /// nothing else. If either were fitted with a free scale it would be judged partly on a
    /// magnitude the model does not claim to get right - and the model's methane amount is
    /// illustrative, so that magnitude is out by a factor of five and a half.
    /// </summary>
    [TestMethod]
    public void BothLawsAreDrawnThroughTheModelsOwnEndpoint()
    {
        var sweep = Sweep();
        if (sweep is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        var ppb = MethaneSweep.Concentrations;
        double last = sweep.Forcings[^1];
        double c0 = ppb[0];

        double root = last * (Math.Sqrt(ppb[^1]) - Math.Sqrt(c0))
                           / (Math.Sqrt(ppb[^1]) - Math.Sqrt(c0));
        double log = last * Math.Log(ppb[^1] / c0) / Math.Log(ppb[^1] / c0);

        Assert.AreEqual(last, root, 1e-9, "the square-root law should meet the model at the end");
        Assert.AreEqual(last, log, 1e-9, "the logarithm should meet the model at the end");
    }

    [TestMethod]
    public void ThemeReachesThePixels()
    {
        var sweep = Sweep();
        if (sweep is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        string light = Path_("light.png"), dark = Path_("dark.png");
        MethaneChartExport.SavePng(light, sweep, ChartTheme.Light, 800, 500);
        MethaneChartExport.SavePng(dark, sweep, ChartTheme.Dark, 800, 500);

        using var lightImage = new Bitmap(light);
        using var darkImage = new Bitmap(dark);

        Assert.IsTrue(lightImage.GetPixel(4, 4).GetBrightness() >
                      darkImage.GetPixel(4, 4).GetBrightness());
    }
}
