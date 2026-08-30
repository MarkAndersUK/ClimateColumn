using ClimateColumn.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClimateColumn.Tests;

/// <summary>
/// Covers the coupling, not the physics. <see cref="ScenarioSweep.MethaneFor"/> is the one place
/// the model asserts something it cannot derive - how much methane accompanies a given CO2 level
/// - so it is worth pinning to the anchors it claims to pass through.
/// </summary>
[TestClass]
public class ScenarioSweepTests
{
    [TestMethod]
    public void PassesThroughBothObservedAnchors()
    {
        Assert.AreEqual(ScenarioSweep.PreIndustrialMethane,
            ScenarioSweep.MethaneFor(ScenarioSweep.PreIndustrialCo2), 1e-9);
        Assert.AreEqual(ScenarioSweep.PresentMethane,
            ScenarioSweep.MethaneFor(ScenarioSweep.PresentCo2), 1e-9);
    }

    [TestMethod]
    public void FollowsTheCurrentTrendAbovePresentDay()
    {
        // Eight ppb a year against 2.4 ppm a year is 3.33 ppb per ppm, so a hundred ppm of CO2
        // beyond today brings 333 ppb of methane with it.
        double expected = ScenarioSweep.PresentMethane
            + 100.0 * ScenarioSweep.MethaneTrend / ScenarioSweep.Co2Trend;

        Assert.AreEqual(expected, ScenarioSweep.MethaneFor(ScenarioSweep.PresentCo2 + 100.0), 1e-9);
    }

    [TestMethod]
    public void UsesTheHistoricalPathBelowPresentDayRatherThanTodaysRate()
    {
        // The historical slope is (1920-700)/(421-285) = 8.97 ppb per ppm, nearly three times
        // today's 3.33. Extrapolating today's rate backwards would contradict what happened, so
        // the two halves must genuinely differ.
        double historical = (ScenarioSweep.PresentMethane - ScenarioSweep.PreIndustrialMethane)
            / (ScenarioSweep.PresentCo2 - ScenarioSweep.PreIndustrialCo2);

        Assert.IsTrue(historical > 2.0 * ScenarioSweep.MethaneTrend / ScenarioSweep.Co2Trend,
            "the two halves of the coupling should not be interchangeable");

        double mid = ScenarioSweep.MethaneFor(0.5 * (ScenarioSweep.PreIndustrialCo2 + ScenarioSweep.PresentCo2));
        Assert.AreEqual(0.5 * (ScenarioSweep.PreIndustrialMethane + ScenarioSweep.PresentMethane), mid, 1e-9);
    }

    [TestMethod]
    public void RisesMonotonicallyAcrossTheSweptRange()
    {
        double previous = double.NegativeInfinity;
        foreach (double ppm in Co2Sweep.Concentrations)
        {
            double ppb = ScenarioSweep.MethaneFor(ppm);
            Assert.IsTrue(ppb > previous, $"methane fell at {ppm} ppm");
            previous = ppb;
        }
    }

    [TestMethod]
    public void StatesTheCouplingInItsOwnNote()
    {
        // The figure carries this string, so it has to name the assumption rather than describe
        // the curve. If someone reads it as a projection the figure has failed.
        string note = ScenarioSweep.CouplingNote;

        StringAssert.Contains(note, "421");
        StringAssert.Contains(note, "1920");
        StringAssert.Contains(note, "extrapolation");
    }
}
