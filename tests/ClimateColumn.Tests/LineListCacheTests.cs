using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Covers the parsed-line-list cache: that it returns the same data, keys correctly, and
/// notices when a file changes underneath it.
/// </summary>
/// <remarks>
/// Worth testing more than it is worth having. The cache saves about 70 ms per configuration
/// against a band derivation costing eight seconds - under 1% - so it is a tidy-up rather than
/// the speed-up it was introduced as. What it could easily do is serve stale data, which is why
/// the invalidation is pinned here.
/// </remarks>
[TestClass]
public class LineListCacheTests
{
    private static string? Path_() => HitranLineList.DefaultPath(HitranLineList.Co2FifteenMicron);

    [TestMethod]
    public void ReturnsTheSameLinesAsAnUncachedLoad()
    {
        string? path = Path_();
        if (path is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        var direct = HitranLineList.Load(path, 1e-26);
        var cached = HitranLineList.LoadCached(path, 1e-26);

        Assert.AreEqual(direct.Count, cached.Count);
        for (int i = 0; i < direct.Count; i += Math.Max(1, direct.Count / 500))
        {
            Assert.AreEqual(direct[i], cached[i], $"line {i} differs");
        }
    }

    [TestMethod]
    public void ServesTheSameInstanceOnRepeat()
    {
        string? path = Path_();
        if (path is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        Assert.AreSame(HitranLineList.LoadCached(path, 1e-26),
                       HitranLineList.LoadCached(path, 1e-26),
                       "a second call should not re-parse");
    }

    /// <summary>
    /// The intensity floor changes which lines survive, so it has to be part of the key or a
    /// caller asking for a different threshold would silently get the first one's answer.
    /// </summary>
    [TestMethod]
    public void KeysOnTheIntensityThreshold()
    {
        string? path = Path_();
        if (path is null) { Assert.Inconclusive("no HITRAN line data."); return; }

        var strict = HitranLineList.LoadCached(path, 1e-24);
        var loose = HitranLineList.LoadCached(path, 1e-26);

        Assert.AreNotSame(strict, loose);
        Assert.IsTrue(loose.Count > strict.Count,
            $"a lower floor should keep more lines ({loose.Count} against {strict.Count})");
    }

    /// <summary>
    /// The failure a cache like this usually introduces: a refetched or edited line list served
    /// from the previous run's parse.
    /// </summary>
    [TestMethod]
    public void NoticesWhenTheFileChanges()
    {
        string dir = Path.Combine(Path.GetTempPath(), "climatecolumn-linecache");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "lines.csv");

        // Two lines, then three: same path, different content. No header row - the real files
        // carry none and Load parses every line as data.
        File.WriteAllText(path, "700.0,1e-20,0.07,0.75\n710.0,1e-20,0.07,0.75\n");
        var before = HitranLineList.LoadCached(path);

        File.WriteAllText(path, "700.0,1e-20,0.07,0.75\n710.0,1e-20,0.07,0.75\n720.0,1e-20,0.07,0.75\n");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));

        var after = HitranLineList.LoadCached(path);

        Assert.AreEqual(2, before.Count);
        Assert.AreEqual(3, after.Count, "the cache should re-read a file that has changed");

        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
}
