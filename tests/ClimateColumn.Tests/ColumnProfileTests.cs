using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Covers the vertical profile a run leaves behind: that it is a faithful snapshot of the
/// column, and that the quantities the figure annotates are computed rather than assumed.
/// </summary>
[TestClass]
public class ColumnProfileTests
{
    /// <summary>A profile with a known shape, so interpolation can be checked against arithmetic.</summary>
    private static ColumnProfile Synthetic(double surface = 288.0, double lapse = 0.0065,
        double emission = 255.0, int count = 20, double top = 20_000.0)
    {
        var levels = new List<ProfileLevel>();
        for (int i = 0; i < count; i++)
        {
            double z = (i + 0.5) * top / count;
            levels.Add(new ProfileLevel(z, 101_325.0 * Math.Exp(-z / 8_400.0), surface - lapse * z));
        }

        return new ColumnProfile
        {
            Label = "Synthetic",
            Ppm = 285.0,
            Levels = levels,
            SurfaceTemperature = surface,
            NearSurfaceAirTemperature = surface - 1.0,
            ConvectiveTopAltitude = 5_000.0,
            CriticalLapseRate = lapse,
            EmissionTemperature = emission,
            ColumnTopAltitude = top,
            Converged = true
        };
    }

    private static ModelOptions Grey() => new()
    {
        SegmentCount = 20,
        TotalOpticalDepth = 2.0,
        Convection = ConvectionMode.Full
    };

    [TestMethod]
    public void CarriesOneLevelPerSegmentAscendingInAltitude()
    {
        var result = ColumnModel.RunToEquilibrium(Grey());
        var profile = ColumnProfile.From(result, "grey", 285.0);

        Assert.AreEqual(result.Column.Count, profile.Levels.Count,
            "there should be one level per segment");

        for (int i = 1; i < profile.Levels.Count; i++)
        {
            Assert.IsTrue(profile.Levels[i].Altitude > profile.Levels[i - 1].Altitude,
                $"level {i} should be above level {i - 1}");
            Assert.IsTrue(profile.Levels[i].Pressure < profile.Levels[i - 1].Pressure,
                $"level {i} should be at lower pressure than level {i - 1}");
        }
    }

    [TestMethod]
    public void ReportsTheColumnTopNotTheHighestLevel()
    {
        var options = Grey();
        var profile = ColumnProfile.From(ColumnModel.RunToEquilibrium(options), "grey", 285.0);

        Assert.AreEqual(options.TopAltitude, profile.ColumnTopAltitude, 1e-9,
            "the column top is the model's upper boundary");

        // The highest level is a layer midpoint, half a layer below the boundary. An axis scaled
        // to it would stop short of the top of the model and read as though the column ended
        // there - which is why these are two different properties.
        double halfLayer = 0.5 * options.TopAltitude / options.SegmentCount;
        Assert.AreEqual(options.TopAltitude - halfLayer, profile.Levels[^1].Altitude, 1e-6,
            "the highest level should sit half a layer below the boundary");
    }

    /// <summary>
    /// The surface and the air on it are different temperatures, and the figure draws both. If
    /// the snapshot collapsed them the picture would show an atmosphere in contact with a
    /// surface at its own temperature, which is not what the model solves.
    /// </summary>
    [TestMethod]
    public void KeepsTheSurfaceAndTheAirOnItApart()
    {
        var result = ColumnModel.RunToEquilibrium(Grey());
        var profile = ColumnProfile.From(result, "grey", 285.0);

        Assert.AreEqual(result.SurfaceTemperature, profile.SurfaceTemperature, 1e-12);
        Assert.AreEqual(result.NearSurfaceAirTemperature, profile.NearSurfaceAirTemperature, 1e-12);

        Assert.IsTrue(profile.SurfaceTemperature > profile.NearSurfaceAirTemperature,
            "the ground should be warmer than the air on it, since it is heating the air " +
            $"(surface {profile.SurfaceTemperature:F3} K, air {profile.NearSurfaceAirTemperature:F3} K)");
    }

    /// <summary>
    /// The column is mutable and is reused after a run - the forcing calculation reads
    /// temperatures out of it, and a later march writes to it. A profile holding a reference
    /// would show whatever the column later became.
    /// </summary>
    [TestMethod]
    public void IsASnapshotAndNotAViewOfTheLiveColumn()
    {
        var result = ColumnModel.RunToEquilibrium(Grey());
        var profile = ColumnProfile.From(result, "grey", 285.0);

        double before = profile.Levels[3].Temperature;
        double surfaceBefore = profile.SurfaceTemperature;

        result.Column.Segments[3].Temperature += 25.0;
        result.Column.SurfaceTemperature += 25.0;

        Assert.AreEqual(before, profile.Levels[3].Temperature, 1e-12,
            "the snapshot should not follow the column");
        Assert.AreEqual(surfaceBefore, profile.SurfaceTemperature, 1e-12,
            "the snapshot's surface should not follow the column");
    }

    [TestMethod]
    public void ConvectiveTopMatchesTheRunItCameFrom()
    {
        var result = ColumnModel.RunToEquilibrium(Grey());
        var profile = ColumnProfile.From(result, "grey", 285.0);

        Assert.AreEqual(result.ConvectiveTopAltitude, profile.ConvectiveTopAltitude, 1e-12);
        Assert.IsTrue(profile.ConvectiveTopAltitude > 0.0,
            "the default grey configuration convects");
    }

    /// <summary>
    /// The crossing is interpolated between levels, not snapped to the nearest one. With a
    /// constant lapse rate the answer is arithmetic: z = (T_air - T_e) / gamma.
    /// </summary>
    [TestMethod]
    public void EmissionAltitudeInterpolatesBetweenLevels()
    {
        var profile = Synthetic(surface: 288.0, lapse: 0.0065, emission: 255.0);

        // The levels are T = 288 - 0.0065 z, so 255 K is reached at 5076.9 m.
        Assert.AreEqual((288.0 - 255.0) / 0.0065, profile.EmissionAltitude, 1e-6);

        // Not a level altitude, which is what a snap-to-nearest implementation would return.
        Assert.IsFalse(profile.Levels.Any(l => Math.Abs(l.Altitude - profile.EmissionAltitude) < 1e-6),
            "the crossing should fall between levels here, so this is a real interpolation");
    }

    [TestMethod]
    public void EmissionAltitudeIsNotANumberWhenTheColumnNeverCrosses()
    {
        // A column that is everywhere warmer than the emission temperature.
        var profile = Synthetic(surface: 288.0, lapse: 0.0001, emission: 200.0);

        Assert.IsTrue(double.IsNaN(profile.EmissionAltitude),
            "a column that never reaches the emission temperature has no crossing");
    }

    /// <summary>
    /// Above the convecting layer a real profile turns over and can pass back through the same
    /// temperature. The greenhouse argument refers to the first crossing, so that is the one
    /// taken.
    /// </summary>
    [TestMethod]
    public void EmissionAltitudeTakesTheLowestCrossing()
    {
        var levels = new List<ProfileLevel>
        {
            new(1_000.0, 90_000.0, 280.0),
            new(3_000.0, 70_000.0, 250.0),   // crosses 255 K here
            new(5_000.0, 54_000.0, 240.0),
            new(7_000.0, 42_000.0, 260.0),   // and back across it here
            new(9_000.0, 33_000.0, 270.0)
        };

        var profile = new ColumnProfile
        {
            Label = "Turnover", Ppm = 285.0, Levels = levels,
            SurfaceTemperature = 288.0, NearSurfaceAirTemperature = 287.0,
            ConvectiveTopAltitude = 2_000.0, CriticalLapseRate = 0.0065,
            EmissionTemperature = 255.0, ColumnTopAltitude = 10_000.0, Converged = true
        };

        // Between 1 km / 280 K and 3 km / 250 K: 255 K is five sixths of the way up.
        Assert.AreEqual(1_000.0 + (280.0 - 255.0) / (280.0 - 250.0) * 2_000.0,
            profile.EmissionAltitude, 1e-6);
    }

    [TestMethod]
    public void TemperatureInterpolatesBetweenLevelsAndHoldsOutsideThem()
    {
        var profile = Synthetic(surface: 288.0, lapse: 0.0065, top: 20_000.0, count: 20);

        double z0 = profile.Levels[0].Altitude, z1 = profile.Levels[1].Altitude;
        double midpoint = 0.5 * (z0 + z1);

        Assert.AreEqual(0.5 * (profile.Levels[0].Temperature + profile.Levels[1].Temperature),
            profile.TemperatureAt(midpoint), 1e-9, "halfway between two levels is their mean");

        Assert.AreEqual(profile.Levels[0].Temperature, profile.TemperatureAt(0.0), 1e-12,
            "below the lowest level the value is held");
        Assert.AreEqual(profile.Levels[^1].Temperature, profile.TemperatureAt(1e9), 1e-12,
            "above the highest level the value is held");
    }

    [TestMethod]
    public void MatchesDistinguishesIdenticalProfilesFromDifferingOnes()
    {
        var a = Synthetic(surface: 288.0);
        var b = Synthetic(surface: 288.0);
        var c = Synthetic(surface: 289.0);

        Assert.IsTrue(a.Matches(b), "two profiles built the same way should match");
        Assert.IsFalse(a.Matches(c), "a kelvin apart is not a match");

        // A profile that agrees at every level but not at the ground still differs: the surface
        // is drawn as its own marker, so two curves that overlay perfectly can still carry two
        // distinct surface temperatures.
        var groundOnly = new ColumnProfile
        {
            Label = a.Label, Ppm = a.Ppm, Levels = a.Levels,
            SurfaceTemperature = a.SurfaceTemperature + 1.0,
            NearSurfaceAirTemperature = a.NearSurfaceAirTemperature,
            ConvectiveTopAltitude = a.ConvectiveTopAltitude,
            CriticalLapseRate = a.CriticalLapseRate,
            EmissionTemperature = a.EmissionTemperature,
            ColumnTopAltitude = a.ColumnTopAltitude, Converged = true
        };

        Assert.IsFalse(a.Matches(groundOnly),
            "profiles differing only at the surface are still two different results");
    }

    /// <summary>
    /// A sweep keeps one profile per concentration, in step with its points. The chart indexes
    /// both by the same integer, so a mismatch would draw one concentration's profile beside
    /// another's forcing.
    /// </summary>
    [TestMethod]
    public void SweepKeepsOneProfilePerConcentration()
    {
        var sweep = Co2Sweep.Run("grey", "--grey", () => Grey());

        Assert.AreEqual(sweep.Points.Count, sweep.Profiles.Count,
            "there should be one profile per swept concentration");

        for (int i = 0; i < sweep.Points.Count; i++)
        {
            Assert.AreEqual(sweep.Points[i].Ppm, sweep.Profiles[i].Ppm, 1e-12,
                $"profile {i} should belong to the same concentration as point {i}");
            Assert.AreEqual(sweep.Points[i].SurfaceTemperature,
                sweep.Profiles[i].SurfaceTemperature, 1e-12,
                $"profile {i} should carry the same surface temperature as point {i}");
        }
    }

    /// <summary>
    /// Adding CO2 lifts the height at which the column reaches the emission temperature. That
    /// is the mechanism the figure annotates, so it is worth pinning that the model produces it
    /// rather than that the drawing asserts it.
    /// </summary>
    [TestMethod]
    public void AddingCarbonDioxideLiftsTheEmissionLevel()
    {
        var sweep = Co2Sweep.Run("grey", "--grey", () => Grey());

        double reference = sweep.Profiles[0].EmissionAltitude;
        double highest = sweep.Profiles[^1].EmissionAltitude;

        Assert.IsFalse(double.IsNaN(reference), "the reference column should cross its emission temperature");
        Assert.IsTrue(highest > reference,
            $"more CO2 should raise the emission level ({reference:F0} m to {highest:F0} m)");
    }
}
