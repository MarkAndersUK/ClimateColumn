using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Covers the cloud deck: its shortwave bookkeeping, its longwave opacity, the independent
/// column approximation that mixes clear and cloudy sky, and the cloud radiative effect the
/// model reports.
/// </summary>
[TestClass]
public class CloudTests
{
    private static ModelOptions Cloudy(double fraction = 0.67, double top = 4_500.0,
        double emissivity = 0.90, int segments = 80)
    {
        return new ModelOptions
        {
            SegmentCount = segments,
            TotalOpticalDepth = 1.1329,
            CloudFraction = fraction,
            ClearSkyAlbedo = 0.155,
            CloudAlbedo = 0.361,
            CloudBaseAltitude = 1_000.0,
            CloudTopAltitude = top,
            CloudLongwaveEmissivity = emissivity
        };
    }

    /// <summary>
    /// Clouds are off by default and every cloud term is inert, so the shipped configuration is
    /// exactly what it was before they existed. The README quotes these numbers.
    /// </summary>
    [TestMethod]
    public void DefaultConfigurationHasNoCloudAndIsUnchanged()
    {
        var options = new ModelOptions();

        Assert.IsFalse(options.HasCloud, "clouds should be off by default");
        Assert.AreEqual(options.Albedo, options.EffectiveAlbedo, 1e-12,
            "with no cloud the effective albedo is the albedo");

        var result = ColumnModel.RunToEquilibrium(options);

        Assert.AreEqual(286.797, result.SurfaceTemperature, 5e-4);
        Assert.AreEqual(238.175, result.Radiation.OutgoingLongwave, 5e-4);
        Assert.AreEqual(0.0, result.NetCloudRadiativeEffect, 1e-12,
            "a cloud-free column has no cloud radiative effect");
        Assert.IsNull(result.ClearSkyRadiation,
            "there is no separate clear-sky solve to report when there is no cloud");
    }

    [TestMethod]
    public void EffectiveAlbedoMixesClearAndCloudySky()
    {
        var options = Cloudy(fraction: 0.67);

        Assert.AreEqual(0.33 * 0.155 + 0.67 * 0.361, options.EffectiveAlbedo, 1e-12);

        // The default mix reproduces Earth's all-sky albedo, so switching clouds on does not
        // quietly change how much sunlight the planet takes in.
        Assert.AreEqual(0.293, options.EffectiveAlbedo, 5e-4);
    }

    /// <summary>
    /// The deck is specified by the emissivity it should have as a whole, so that is what it
    /// must come out with - whatever the layer boundaries happen to fall on.
    /// </summary>
    [DataTestMethod]
    [DataRow(20)]
    [DataRow(37)]
    [DataRow(80)]
    [DataRow(160)]
    public void DeckReachesItsRequestedEmissivityAtAnyResolution(int segments)
    {
        var options = Cloudy(emissivity: 0.90, segments: segments);
        var column = Column.Build(options);

        double thickness = 0.0;
        foreach (var s in column.Segments)
        {
            thickness += options.Diffusivity * s.CloudExtinction * s.Thickness;
        }

        Assert.AreEqual(0.90, 1.0 - Math.Exp(-thickness), 1e-9,
            $"the deck should be 0.90 emissive with {segments} segments");
    }

    /// <summary>
    /// The opacity has to land in the cloud and nowhere else. Weighting by full segment
    /// thickness rather than by overlap would have spread a 1.0-4.5 km deck across whichever
    /// segments happened to straddle its edges.
    /// </summary>
    [TestMethod]
    public void OpacityIsConfinedToTheDeck()
    {
        var options = Cloudy();
        var column = Column.Build(options);

        foreach (var s in column.Segments)
        {
            bool overlaps = s.TopAltitude > options.CloudBaseAltitude &&
                            s.BottomAltitude < options.CloudTopAltitude;

            if (overlaps) continue;

            Assert.AreEqual(0.0, s.CloudExtinction, 0.0,
                $"segment at {s.MidAltitude:F0} m is outside the deck and should be clear");
        }
    }

    [TestMethod]
    public void ADeckEntirelyAboveTheColumnDoesNothing()
    {
        var options = Cloudy();
        options.CloudBaseAltitude = 60_000.0;
        options.CloudTopAltitude = 70_000.0;

        var column = Column.Build(options);

        Assert.IsTrue(column.Segments.All(s => s.CloudExtinction == 0.0),
            "a deck outside the column puts opacity nowhere");
    }

    /// <summary>
    /// The mix is the independent column approximation, and it has to be exactly linear in
    /// cloud fraction: fluxes add, so a sky that is f cloudy really does emit the weighted mean.
    /// </summary>
    [TestMethod]
    public void SkyMixIsLinearInCloudFraction()
    {
        var column = Column.Build(Cloudy(fraction: 0.5, segments: 30));

        var cloudy = RadiationSolver.Solve(column, includeCloud: true);
        var clear = RadiationSolver.Solve(column, includeCloud: false);

        Assert.AreEqual(clear.OutgoingLongwave,
            RadiationResult.Blend(clear, cloudy, 0.0).OutgoingLongwave, 1e-12,
            "an empty sky is the clear sky");
        Assert.AreEqual(cloudy.OutgoingLongwave,
            RadiationResult.Blend(clear, cloudy, 1.0).OutgoingLongwave, 1e-12,
            "a full sky is the cloudy sky");

        var half = RadiationResult.Blend(clear, cloudy, 0.5);
        Assert.AreEqual(0.5 * (clear.OutgoingLongwave + cloudy.OutgoingLongwave),
            half.OutgoingLongwave, 1e-12);

        for (int i = 0; i < column.Count; i++)
        {
            Assert.AreEqual(0.5 * (clear.RadiativeHeating[i] + cloudy.RadiativeHeating[i]),
                half.RadiativeHeating[i], 1e-12,
                $"heating in segment {i} should mix linearly too");
        }
    }

    /// <summary>
    /// A cloud is grey: droplets absorb across a band rather than in lines. So its opacity must
    /// not be scaled by a g-point multiplier, and in particular it has to close the window -
    /// the part of the spectrum where the gas is transparent and the surface radiates straight
    /// to space. That is where a cloud does most of its longwave work.
    /// </summary>
    [TestMethod]
    public void CloudClosesTheAtmosphericWindow()
    {
        ModelOptions WithWindow(double fraction)
        {
            var o = Cloudy(fraction: fraction, emissivity: 1.0, segments: 40);
            o.WindowShortWavelength = 8e-6;
            o.WindowLongWavelength = 12e-6;
            return o;
        }

        var open = Column.Build(WithWindow(0.0));
        var closed = Column.Build(WithWindow(1.0));

        // Same temperatures in both, so the only difference is the cloud.
        for (int i = 0; i < open.Count; i++) closed.Segments[i].Temperature = open.Segments[i].Temperature;
        closed.SurfaceTemperature = open.SurfaceTemperature;

        double withoutCloud = RadiationSolver.Solve(open, includeCloud: false).OutgoingLongwave;
        double withCloud = RadiationSolver.Solve(closed, includeCloud: true).OutgoingLongwave;

        Assert.IsTrue(withCloud < withoutCloud - 20.0,
            $"a black deck should shut off a large part of the outgoing longwave " +
            $"({withoutCloud:F1} to {withCloud:F1} W/m2)");
    }

    [TestMethod]
    public void ZeroEmissivityLeavesTheLongwaveAlone()
    {
        var column = Column.Build(Cloudy(emissivity: 0.0, segments: 30));

        Assert.AreEqual(
            RadiationSolver.Solve(column, includeCloud: false).OutgoingLongwave,
            RadiationSolver.Solve(column, includeCloud: true).OutgoingLongwave, 1e-12,
            "a deck with no emissivity is not there");
    }

    /// <summary>
    /// The two effects pull opposite ways, which is the whole interest of clouds: they reflect
    /// sunlight away and they trap outgoing longwave.
    /// </summary>
    [TestMethod]
    public void CloudsCoolInTheShortwaveAndWarmInTheLongwave()
    {
        var result = ColumnModel.RunToEquilibrium(Cloudy());

        Assert.IsTrue(result.ShortwaveCloudRadiativeEffect < 0.0,
            "a cloud reflects, so it reduces absorbed sunlight");
        Assert.IsTrue(result.LongwaveCloudRadiativeEffect > 0.0,
            "a cloud radiates to space from a cold top, so it reduces outgoing longwave");
        Assert.AreEqual(
            result.ShortwaveCloudRadiativeEffect + result.LongwaveCloudRadiativeEffect,
            result.NetCloudRadiativeEffect, 1e-12);
    }

    /// <summary>
    /// A higher deck is a colder deck, and a colder deck emits less to space - so raising it
    /// traps more. This is the sensitivity that made the deck's height the knob used to
    /// calibrate the longwave effect.
    /// </summary>
    [TestMethod]
    public void RaisingTheDeckTrapsMoreLongwave()
    {
        double low = ColumnModel.RunToEquilibrium(Cloudy(top: 3_000.0, segments: 40))
            .LongwaveCloudRadiativeEffect;
        double high = ColumnModel.RunToEquilibrium(Cloudy(top: 7_000.0, segments: 40))
            .LongwaveCloudRadiativeEffect;

        Assert.IsTrue(high > low + 5.0,
            $"a higher deck should trap more ({low:F2} at 3 km against {high:F2} at 7 km W/m2)");
    }

    /// <summary>
    /// The calibrated configuration, against the CERES satellite record. The shortwave figure is
    /// arithmetic on the two albedos; the longwave one came out of the radiative transfer once
    /// the deck's height was chosen.
    /// </summary>
    [TestMethod]
    public void TypicalCloudReproducesTheObservedRadiativeEffect()
    {
        var result = ColumnModel.RunToEquilibrium(ModelOptions.WithTypicalCloud());

        Assert.AreEqual(-47.1, result.ShortwaveCloudRadiativeEffect, 1.0,
            "shortwave cloud radiative effect, CERES -47.1 W/m2");
        Assert.AreEqual(26.2, result.LongwaveCloudRadiativeEffect, 1.5,
            "longwave cloud radiative effect, CERES +26.2 W/m2");
        Assert.AreEqual(-20.9, result.NetCloudRadiativeEffect, 1.5,
            "net cloud radiative effect, CERES -20.9 W/m2");
    }

    /// <summary>
    /// The calibration's other constraint: the cloudy configuration sits at the same surface
    /// temperature as the cloud-free default, so the two are comparable in base state and differ
    /// only in how they reach it.
    /// </summary>
    [TestMethod]
    public void TypicalCloudSitsAtTheSameSurfaceTemperatureAsTheDefault()
    {
        double bare = ColumnModel.RunToEquilibrium(new ModelOptions()).SurfaceTemperature;
        double cloudy = ColumnModel.RunToEquilibrium(ModelOptions.WithTypicalCloud()).SurfaceTemperature;

        Assert.AreEqual(bare, cloudy, 0.01,
            $"the cloudy configuration is calibrated to the cloud-free one ({bare:F3} vs {cloudy:F3} K)");
    }

    /// <summary>
    /// The finding that made a separate calibration necessary. The shipped configuration's
    /// absorbers were scaled to reach an Earth-like temperature with an 0.30 albedo and no
    /// cloud - a planet carrying the clouds' reflection but none of their greenhouse - so
    /// adding a real cloud supplies that greenhouse twice and the column overheats.
    /// </summary>
    [TestMethod]
    public void SwitchingCloudsOnOverTheDefaultOverheatsTheColumn()
    {
        var naive = new ModelOptions
        {
            CloudFraction = 0.67,
            ClearSkyAlbedo = 0.155,
            CloudAlbedo = 0.361,
            CloudBaseAltitude = 1_000.0,
            CloudTopAltitude = 4_500.0
        };

        double bare = ColumnModel.RunToEquilibrium(new ModelOptions()).SurfaceTemperature;
        double overheated = ColumnModel.RunToEquilibrium(naive).SurfaceTemperature;

        Assert.IsTrue(overheated > bare + 8.0,
            $"clouds added over the default's gas loading should overheat it, not sit on top of " +
            $"it ({bare:F2} to {overheated:F2} K) - which is why WithTypicalCloud halves the gas");
    }

    /// <summary>
    /// Forcing against the reference state is zero at the reference concentration, by
    /// construction - nothing has been changed. That has to hold with a cloud deck too.
    /// </summary>
    /// <remarks>
    /// This is the test that was missing. The forcing calculation solved the perturbed column
    /// directly, which returns the <em>fully cloudy</em> sky, and differenced it against a
    /// baseline whose outgoing longwave is the all-sky blend of clear and cloudy. Two different
    /// skies subtracted from each other is not a forcing, and the give-away is exactly here: it
    /// did not come out zero where it must.
    ///
    /// Cloud-free the two paths are the same single solve, so every existing configuration was
    /// unaffected and nothing failed. A cloud fraction is what separates them.
    /// </remarks>
    [DataTestMethod]
    [DataRow(0.0)]
    [DataRow(0.33)]
    [DataRow(0.67)]
    [DataRow(1.0)]
    public void ForcingIsZeroAtTheReferenceWhateverTheSky(double fraction)
    {
        var forcings = Co2Sweep.ForcingCurve(ppm =>
        {
            var o = Cloudy(fraction, segments: 30);
            o.Co2Concentration = ppm;
            o.Co2AbsorberFraction = 0.1;
            return o;
        });

        Assert.AreEqual(0.0, forcings[0], 1e-9,
            $"at {fraction:P0} cloud the reference concentration must force by exactly nothing");

        Assert.IsTrue(forcings[^1] > 0.0,
            $"at {fraction:P0} cloud, more CO2 should still force positively " +
            $"({forcings[^1]:F4} W/m2)");
    }

    /// <summary>
    /// A cloud deck sits below most of the CO2 that matters, so it masks part of the forcing:
    /// the column it hides was already absorbing what the extra CO2 would have absorbed.
    /// </summary>
    [TestMethod]
    public void CloudMasksPartOfTheCarbonDioxideForcing()
    {
        double Forcing(double fraction) => Co2Sweep.ForcingCurve(ppm =>
        {
            var o = Cloudy(fraction, segments: 30);
            o.Co2Concentration = ppm;
            o.Co2AbsorberFraction = 0.1;
            return o;
        })[^1];

        double clear = Forcing(0.0);
        double clouded = Forcing(0.67);

        Assert.IsTrue(clouded < clear,
            $"a cloud deck should mask some of the CO2 forcing, not add to it " +
            $"({clear:F4} clear against {clouded:F4} at 67% cloud)");
    }

    /// <summary>
    /// The two charted configurations start from the same surface, so switching clouds on shows
    /// what the deck does rather than what a different base state does.
    /// </summary>
    /// <remarks>
    /// This is the whole point of keeping two calibrated absorber scales. Reusing one would put
    /// the cloudy column about 15 K hotter, and every difference read off the figures afterwards
    /// would be that offset rather than the cloud.
    /// </remarks>
    [TestMethod]
    public void BothChartedSkiesShareABaseState()
    {
        var clear = Co2Sweep.ForChart(clouds: false);
        var cloudy = Co2Sweep.ForChart(clouds: true);

        if (clear.Length == 0 || cloudy.Length == 0)
        {
            Assert.Inconclusive("no HITRAN line data; run scripts/fetch-hitran.ps1 -Molecule all.");
            return;
        }

        Assert.AreEqual(clear[0].BaseTemperature, cloudy[0].BaseTemperature, 0.05,
            $"the cloudy configuration should start where the clear one does " +
            $"({clear[0].BaseTemperature:F3} against {cloudy[0].BaseTemperature:F3} K)");

        // And it must actually be a different atmosphere, not the same one relabelled.
        Assert.AreNotEqual(
            Co2Sweep.CalibratedAbsorberScale(0.0),
            Co2Sweep.CalibratedAbsorberScale(Co2Sweep.CalibratedCloudFraction),
            "the two skies need different gas loadings to reach the same surface");

        int last = Co2Sweep.Concentrations.Length - 1;
        Assert.IsTrue(cloudy[0].Forcings[last] < clear[0].Forcings[last],
            $"the deck should mask part of the CO2 forcing " +
            $"({clear[0].Forcings[last]:F3} clear against {cloudy[0].Forcings[last]:F3} cloudy)");
    }

    [DataTestMethod]
    [DataRow(-0.1)]
    [DataRow(1.1)]
    public void RejectsAnImpossibleCloudFraction(double fraction)
    {
        var options = new ModelOptions { CloudFraction = fraction };
        Assert.ThrowsException<ArgumentException>(() => options.Validate());
    }

    [TestMethod]
    public void RejectsADeckWhoseTopIsBelowItsBase()
    {
        var options = Cloudy();
        options.CloudBaseAltitude = 5_000.0;
        options.CloudTopAltitude = 2_000.0;

        Assert.ThrowsException<ArgumentException>(() => options.Validate());
    }

    /// <summary>
    /// A perfectly black deck is infinitely thick. It has to be handled rather than produce an
    /// infinity that then propagates into every flux in the column.
    /// </summary>
    [TestMethod]
    public void APerfectlyBlackDeckStaysFinite()
    {
        var column = Column.Build(Cloudy(emissivity: 1.0, segments: 30));

        Assert.IsTrue(column.Segments.All(s => double.IsFinite(s.CloudExtinction)),
            "a black deck should be capped, not infinite");

        double olr = RadiationSolver.Solve(column).OutgoingLongwave;
        Assert.IsTrue(double.IsFinite(olr) && olr > 0.0,
            $"the outgoing longwave should still be a real number ({olr})");
    }
}
