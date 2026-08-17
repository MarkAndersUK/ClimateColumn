using System.Drawing;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Charts.Tests;

/// <summary>
/// Covers what the Save PNG button does. The button itself is two lines: put up a
/// <c>SaveFileDialog</c>, then call <see cref="Co2ChartExport.SavePng"/> with the chart's
/// current size and theme. Everything below the dialog is tested here.
/// </summary>
/// <remarks>
/// The dialog is deliberately not driven. It is OS shell code, a test that automates it
/// asserts nothing about this project, and a modal window in a test run is a reliable source
/// of hangs. What is worth pinning is that a real, correctly sized PNG comes out, that the
/// theme and the hover state reach the pixels, and that the awkward cases - a missing folder,
/// an existing file, a control too small to draw in - behave.
/// </remarks>
[TestClass]
public class SavePngTests
{
    public TestContext TestContext { get; set; } = null!;

    private string _directory = null!;

    [TestInitialize]
    public void CreateScratchDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(),
            "climatecolumn-charts-tests", TestContext.TestName ?? "unnamed");

        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        Directory.CreateDirectory(_directory);
    }

    [TestCleanup]
    public void RemoveScratchDirectory()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A scratch file left behind must not fail an otherwise passing test.
        }
    }

    private string Path_(string name) => Path.Combine(_directory, name);

    /// <summary>PNG files start with this 8-byte signature; anything else is not a PNG.</summary>
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static void AssertIsPng(string path)
    {
        Assert.IsTrue(File.Exists(path), $"no file was written to {path}");

        var header = new byte[8];
        using (var stream = File.OpenRead(path))
        {
            Assert.AreEqual(8, stream.Read(header, 0, 8), "the file is too short to be a PNG");
        }

        CollectionAssert.AreEqual(PngSignature, header,
            "the file does not carry the PNG signature");
    }

    [TestMethod]
    public void WritesAValidPngAtTheRequestedSize()
    {
        string path = Path_("chart.png");

        Co2ChartExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 1100, 700);

        AssertIsPng(path);

        using var image = new Bitmap(path);
        Assert.AreEqual(1100, image.Width, "width should be what the caller asked for");
        Assert.AreEqual(700, image.Height, "height should be what the caller asked for");
    }

    /// <summary>
    /// The button passes the chart control's current size, so a window dragged very small
    /// would otherwise ask for a figure with no room for the axes. The export floors it.
    /// </summary>
    [TestMethod]
    public void ClampsSizesTooSmallToDrawIn()
    {
        string path = Path_("tiny.png");

        Co2ChartExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 40, 20);

        using var image = new Bitmap(path);
        Assert.AreEqual(700, image.Width, "width should be floored");
        Assert.AreEqual(450, image.Height, "height should be floored");
    }

    /// <summary>
    /// The dialog lets the user type a path into a folder that does not exist yet, so the
    /// export creates it rather than throwing.
    /// </summary>
    [TestMethod]
    public void CreatesMissingDirectories()
    {
        string path = Path_(Path.Combine("nested", "deeper", "chart.png"));

        Co2ChartExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 800, 500);

        AssertIsPng(path);
    }

    /// <summary>The dialog's own overwrite prompt means the second save must succeed.</summary>
    [TestMethod]
    public void OverwritesAnExistingFile()
    {
        string path = Path_("chart.png");

        Co2ChartExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 800, 500);
        long first = new FileInfo(path).Length;

        Co2ChartExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Dark, 900, 560);

        AssertIsPng(path);
        using var image = new Bitmap(path);
        Assert.AreEqual(900, image.Width, "the second save should have replaced the first");
        Assert.IsTrue(first > 0, "the first save should have written something");
    }

    /// <summary>
    /// The button saves in whichever theme the window is showing. If the painter ever stopped
    /// reading the theme, both exports would come out identical and nobody would notice from
    /// the file sizes alone - so compare actual pixels.
    /// </summary>
    [TestMethod]
    public void ThemeReachesThePixels()
    {
        string light = Path_("light.png");
        string dark = Path_("dark.png");

        Co2ChartExport.SavePng(light, SyntheticSweep.Pair(), ChartTheme.Light, 800, 500);
        Co2ChartExport.SavePng(dark, SyntheticSweep.Pair(), ChartTheme.Dark, 800, 500);

        using var lightImage = new Bitmap(light);
        using var darkImage = new Bitmap(dark);

        Color lightCorner = lightImage.GetPixel(4, 4);
        Color darkCorner = darkImage.GetPixel(4, 4);

        Assert.AreNotEqual(lightCorner.ToArgb(), darkCorner.ToArgb(),
            "the two themes should not paint the same background");
        Assert.IsTrue(lightCorner.GetBrightness() > darkCorner.GetBrightness(),
            $"the light surface should be lighter than the dark one " +
            $"({lightCorner.GetBrightness():F2} vs {darkCorner.GetBrightness():F2})");
    }

    /// <summary>
    /// The readout panel is otherwise only reachable by moving a mouse over the live chart,
    /// which no test can do. Exporting with a hover index draws the same panel, so this is
    /// the one automated check that the drawing path runs and puts ink on the page.
    /// </summary>
    [TestMethod]
    public void HoverReadoutIsDrawnWhenAsked()
    {
        string plain = Path_("plain.png");
        string hovered = Path_("hovered.png");

        var sweeps = SyntheticSweep.Pair();
        int idx = Co2Sweep.CalibrationIndex;

        Co2ChartExport.SavePng(plain, sweeps, ChartTheme.Light, 900, 560);
        Co2ChartExport.SavePng(hovered, sweeps, ChartTheme.Light, 900, 560, hoverIndex: idx);

        AssertIsPng(hovered);

        Assert.AreNotEqual(
            File.ReadAllBytes(plain).Length,
            File.ReadAllBytes(hovered).Length,
            "the hovered figure should differ from the plain one");

        using var plainImage = new Bitmap(plain);
        using var hoveredImage = new Bitmap(hovered);

        int differing = 0;
        for (int y = 0; y < plainImage.Height; y += 4)
        {
            for (int x = 0; x < plainImage.Width; x += 4)
            {
                if (plainImage.GetPixel(x, y).ToArgb() != hoveredImage.GetPixel(x, y).ToArgb())
                    differing++;
            }
        }

        Assert.IsTrue(differing > 200,
            $"the readout panel and crosshair should change a visible area (only {differing} sampled pixels differ)");
    }

    /// <summary>
    /// An out-of-range hover index must not throw. The live control cannot produce one, but
    /// the export is public and the CLI takes the value from an argument.
    /// </summary>
    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(999)]
    public void OutOfRangeHoverIndexIsIgnored(int hoverIndex)
    {
        string path = Path_($"hover{hoverIndex}.png");

        Co2ChartExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 800, 500, hoverIndex);

        AssertIsPng(path);
    }

    /// <summary>
    /// The button is disabled until the sweep finishes, so this should be unreachable through
    /// the UI - but a blank figure is the right answer if it ever is reached, not an exception.
    /// </summary>
    [TestMethod]
    public void EmptySweepsStillProduceAValidFile()
    {
        string path = Path_("empty.png");

        Co2ChartExport.SavePng(path, Array.Empty<Co2Sweep>(), ChartTheme.Light, 800, 500);

        AssertIsPng(path);
        using var image = new Bitmap(path);
        Assert.AreEqual(800, image.Width);
    }
}
