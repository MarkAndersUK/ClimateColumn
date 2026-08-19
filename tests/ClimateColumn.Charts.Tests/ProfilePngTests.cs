using System.Drawing;
using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Charts.Tests;

/// <summary>
/// Covers what the Save profile button does, and the drawing decisions the profile figure makes
/// that a reader would only catch by looking at it.
/// </summary>
/// <remarks>
/// The pixel-counting tests here exist because of what this project has repeatedly got wrong.
/// Every chart defect found in this codebase - a duplicated reference law, a curve painted
/// invisibly over another, a whole suite passing against a stale assembly - was found by looking
/// at the rendered figure, never by an assertion, because the numbers were self-consistent every
/// time. Counting coloured pixels is the cheapest way to make an assertion look at the picture.
/// </remarks>
[TestClass]
public class ProfilePngTests
{
    public TestContext TestContext { get; set; } = null!;

    private string _directory = null!;

    [TestInitialize]
    public void CreateScratchDirectory()
    {
        _directory = Path.Combine(Path.GetTempPath(),
            "climatecolumn-profile-tests", TestContext.TestName ?? "unnamed");

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

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static void AssertIsPng(string path)
    {
        Assert.IsTrue(File.Exists(path), $"no file was written to {path}");

        var header = new byte[8];
        using (var stream = File.OpenRead(path))
        {
            Assert.AreEqual(8, stream.Read(header, 0, 8), "the file is too short to be a PNG");
        }

        CollectionAssert.AreEqual(PngSignature, header, "the file does not carry the PNG signature");
    }

    /// <summary>
    /// How many pixels sit close to a given colour. Anti-aliasing means an exact match undercounts
    /// badly, so this allows a channel-wise tolerance.
    /// </summary>
    private static int CountNear(Bitmap image, Color target, int tolerance = 40)
    {
        int found = 0;
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var p = image.GetPixel(x, y);
                if (Math.Abs(p.R - target.R) <= tolerance &&
                    Math.Abs(p.G - target.G) <= tolerance &&
                    Math.Abs(p.B - target.B) <= tolerance) found++;
            }
        }
        return found;
    }

    [TestMethod]
    public void WritesAValidPngAtTheRequestedSize()
    {
        string path = Path_("profile.png");

        ProfileExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 640, 840,
            Co2Sweep.HighlightIndex);

        AssertIsPng(path);

        using var image = new Bitmap(path);
        Assert.AreEqual(640, image.Width, "width should be what the caller asked for");
        Assert.AreEqual(840, image.Height, "height should be what the caller asked for");
    }

    /// <summary>
    /// The button passes the control's current size. A profile squeezed into a strip has fifty
    /// kilometres of column and nowhere to put it, so the export floors it - and at a taller
    /// floor than the response chart, because this figure is portrait by nature.
    /// </summary>
    [TestMethod]
    public void ClampsSizesTooSmallToDrawIn()
    {
        string path = Path_("tiny.png");

        ProfileExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 40, 20, 0);

        using var image = new Bitmap(path);
        Assert.AreEqual(520, image.Width, "width should be floored");
        Assert.AreEqual(520, image.Height, "height should be floored");
    }

    [TestMethod]
    public void CreatesMissingDirectories()
    {
        string path = Path_(Path.Combine("nested", "deeper", "profile.png"));

        ProfileExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 560, 700, 0);

        AssertIsPng(path);
    }

    [TestMethod]
    public void ThemeReachesThePixels()
    {
        string light = Path_("light.png");
        string dark = Path_("dark.png");

        ProfileExport.SavePng(light, SyntheticSweep.Pair(), ChartTheme.Light, 560, 700, 0);
        ProfileExport.SavePng(dark, SyntheticSweep.Pair(), ChartTheme.Dark, 560, 700, 0);

        using var lightImage = new Bitmap(light);
        using var darkImage = new Bitmap(dark);

        Color lightCorner = lightImage.GetPixel(4, 4);
        Color darkCorner = darkImage.GetPixel(4, 4);

        Assert.IsTrue(lightCorner.GetBrightness() > darkCorner.GetBrightness(),
            $"the light surface should be lighter than the dark one " +
            $"({lightCorner.GetBrightness():F2} vs {darkCorner.GetBrightness():F2})");
    }

    /// <summary>
    /// The readout panel is otherwise only reachable by moving a mouse over the live control.
    /// </summary>
    [TestMethod]
    public void HoverReadoutIsDrawnWhenAsked()
    {
        string plain = Path_("plain.png");
        string hovered = Path_("hovered.png");

        var sweeps = SyntheticSweep.Pair();

        ProfileExport.SavePng(plain, sweeps, ChartTheme.Light, 620, 760, Co2Sweep.HighlightIndex);
        ProfileExport.SavePng(hovered, sweeps, ChartTheme.Light, 620, 760, Co2Sweep.HighlightIndex,
            hoverLevel: 8);

        AssertIsPng(hovered);

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
    /// The defect this whole mechanism exists to prevent: two configurations that agree exactly
    /// must produce one curve, not one curve with another hidden underneath it.
    /// </summary>
    /// <remarks>
    /// Asserted on pixels rather than on <see cref="ColumnProfile.Matches"/>, which is tested
    /// separately. A unit test of the predicate would have passed throughout the period when
    /// the painter was calling it and drawing the curve anyway.
    /// </remarks>
    [TestMethod]
    public void IdenticalProfilesAreNotPaintedOverEachOther()
    {
        string distinct = Path_("distinct.png");
        string identical = Path_("identical.png");

        var second = ChartTheme.Light.Series[1];

        ProfileExport.SavePng(distinct, SyntheticSweep.Pair(), ChartTheme.Light, 620, 760,
            Co2Sweep.HighlightIndex);
        ProfileExport.SavePng(identical, SyntheticSweep.IdenticalPair(), ChartTheme.Light, 620, 760,
            Co2Sweep.HighlightIndex);

        using var distinctImage = new Bitmap(distinct);
        using var identicalImage = new Bitmap(identical);

        int drawn = CountNear(distinctImage, second);
        int suppressed = CountNear(identicalImage, second);

        Assert.IsTrue(drawn > 400,
            $"the second configuration should be clearly drawn when it differs ({drawn} px)");

        // Not zero: the legend still carries the second configuration's key, greyed, saying it
        // is identical here rather than silently dropping it.
        Assert.IsTrue(suppressed < drawn / 3,
            $"the second configuration should not be painted over the first when they agree " +
            $"({suppressed} px against {drawn} px when they differ)");
    }

    /// <summary>
    /// A selected index the sweep has no profile for must draw nothing rather than throw. The
    /// live control cannot produce one, but the export is public and the CLI takes the value
    /// from an argument.
    /// </summary>
    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(999)]
    public void OutOfRangeSelectionIsIgnored(int selected)
    {
        string path = Path_($"selected{selected}.png");

        ProfileExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 560, 700, selected);

        AssertIsPng(path);
    }

    [TestMethod]
    public void OutOfRangeHoverLevelIsIgnored()
    {
        string path = Path_("hover.png");

        ProfileExport.SavePng(path, SyntheticSweep.Pair(), ChartTheme.Light, 560, 700, 0,
            hoverLevel: 500);

        AssertIsPng(path);
    }

    /// <summary>
    /// A sweep built by hand carries no profiles - <see cref="Co2Sweep.Profiles"/> is not
    /// required - so the figure has to cope with having nothing to draw.
    /// </summary>
    [TestMethod]
    public void SweepsWithoutProfilesStillProduceAValidFile()
    {
        string path = Path_("none.png");

        var bare = new[]
        {
            new Co2Sweep
            {
                Label = "No profiles",
                Command = "--none",
                Points = Co2Sweep.Concentrations
                    .Select(p => new Co2Point(p, 1.0, 287.0, true)).ToList(),
                Forcings = Co2Sweep.Concentrations.Select(_ => 1.0).ToList()
            }
        };

        ProfileExport.SavePng(path, bare, ChartTheme.Light, 560, 700, 0);

        AssertIsPng(path);
        using var image = new Bitmap(path);
        Assert.AreEqual(560, image.Width);
    }

    [TestMethod]
    public void EmptySweepsStillProduceAValidFile()
    {
        string path = Path_("empty.png");

        ProfileExport.SavePng(path, Array.Empty<Co2Sweep>(), ChartTheme.Light, 560, 700, 0);

        AssertIsPng(path);
    }
}
