using System.Globalization;
using System.Text;

namespace ClimateColumn.Core;

public static class Reporting
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// Renders the column from the top down. Fluxes live on interfaces and temperatures on
    /// segments, so the two are printed on separate rows: an interface row carries only
    /// fluxes at its own altitude, a segment row only segment properties. Putting them on
    /// one row would attach an interface flux to a mid-segment altitude.
    /// </summary>
    public static string FormatProfile(ModelResult result)
    {
        var c = result.Column;
        var r = result.Radiation;
        var lw = result.LongwaveHeatingRatesPerDay();
        var net = result.NetHeatingRatesPerDay();
        var sb = new StringBuilder();

        sb.AppendLine("  level         z    p        T     eps'      dtau       F_up     F_down      F_net   4e'sT^4dz      LW      net");
        sb.AppendLine("             [km] [hPa]     [K]    [1/m]         -     [W/m2]     [W/m2]     [W/m2]      [W/m2] [K/day]  [K/day]");
        sb.AppendLine(new string('-', 121));

        Interface(sb, "TOA", c.Options.TopAltitude, c.Segments[^1].TopPressure, r, c.Count);

        for (int i = c.Count - 1; i >= 0; i--)
        {
            var s = c.Segments[i];
            sb.AppendLine(string.Format(Inv,
                "  seg {0,4} {1,7:F2} {2,6:F2} {3,7:F2} {4,8:E1} {5,9:F4}          -          -          -  {6,10:F3} {7,7:F3} {8,8:F3}",
                i, s.MidAltitude / 1000.0, s.MidPressure / 100.0, s.Temperature,
                s.EmissionCoefficient, r.OpticalThickness[i],
                r.KoenigsbergerEmission[i], lw[i], net[i]));

            string label = i == 0 ? "ifc  sfc" : $"ifc {i,4}";
            Interface(sb, label, s.BottomAltitude, s.BottomPressure, r, i);
        }

        sb.AppendLine(new string('-', 121));
        sb.AppendLine(string.Format(Inv,
            "  SURFACE     0.00 {0,6:F2} {1,7:F2}        -         - {2,10:F3} {3,10:F3} {4,10:F3}           -       -        -",
            c.Segments[0].BottomPressure / 100.0, c.SurfaceTemperature,
            r.UpwardFlux[0], r.DownwardFlux[0], r.NetUpwardFlux[0]));

        sb.AppendLine();
        sb.AppendLine("  Fluxes are interface quantities and appear only on 'ifc'/TOA rows; temperature,");
        sb.AppendLine("  eps' and emission are segment quantities and appear only on 'seg' rows. The");
        sb.AppendLine("  surface coincides with interface 0. LW is the longwave flux convergence alone;");
        sb.AppendLine("  net adds absorbed solar (and sensible heat in segment 0), so a non-zero net");
        sb.AppendLine("  marks a segment whose balance is closed by convection rather than radiation.");

        return sb.ToString();
    }

    private static void Interface(StringBuilder sb, string label, double z, double p,
        RadiationResult r, int index)
    {
        sb.AppendLine(string.Format(Inv,
            "  {0,-8} {1,7:F2} {2,6:F2}       -        -         - {3,10:F3} {4,10:F3} {5,10:F3}           -       -        -",
            label, z / 1000.0, p / 100.0,
            r.UpwardFlux[index], r.DownwardFlux[index], r.NetUpwardFlux[index]));
    }

    public static string FormatSummary(ModelResult result)
    {
        var o = result.Column.Options;
        var r = result.Radiation;
        var sb = new StringBuilder();

        sb.AppendLine("Configuration");
        sb.AppendLine($"  segments                  : {o.SegmentCount} x {o.TopAltitude / o.SegmentCount:F1} m  (top {o.TopAltitude / 1000.0:F1} km)");
        sb.AppendLine($"  convection                : {o.Convection}");
        sb.AppendLine($"  diffusivity factor D      : {o.Diffusivity:F3}" +
                      (IsKoenigsbergerDiffusivity(o) ? "  (Koenigsberger-consistent)" : "  (NOT Koenigsberger-consistent)"));
        sb.AppendLine($"  absorber loading (tau@D=2): {o.TotalOpticalDepth * o.OpticalDepthScale:F4}");
        sb.AppendLine($"  column optical depth      : {result.Column.TotalOpticalDepth():F4}");
        sb.AppendLine($"  solar constant / albedo   : {o.SolarConstant:F1} W/m2 / {o.Albedo:F3}");
        sb.AppendLine($"  surface emissivity        : {o.SurfaceEmissivity:F3}");
        sb.AppendLine($"  wind speed / h_c          : {o.WindSpeed:F2} m/s / {ConvectionSolver.SurfaceHeatTransferCoefficient(o.WindSpeed):F2} W/m2/K");

        if (Math.Abs(o.Co2Concentration - o.Co2ReferenceConcentration) > 1e-9)
            sb.AppendLine($"  CO2                       : {o.Co2Concentration:F1} ppm vs {o.Co2ReferenceConcentration:F1} ppm reference  " +
                          $"(dry tau {o.TotalOpticalDepth * o.OpticalDepthScale:F4} -> {o.EffectiveDryOpticalDepth:F4})");
        if (o.HasWindow)
        {
            sb.AppendLine($"  spectral window           : {o.WindowShortWavelength * 1e6:F1} - " +
                          $"{o.WindowLongWavelength * 1e6:F1} um transparent");
            sb.AppendLine($"    ... share at surface    : {o.WindowShare(result.SurfaceTemperature):F3}" +
                          $"  (of the surface's own emission)");
            sb.AppendLine($"    ... share at column top : {o.WindowShare(result.Column.Segments[^1].Temperature):F3}" +
                          $"  (colder, so a smaller share)");

            if (o.HasWindowContinuum)
            {
                sb.AppendLine($"    ... continuum tau      : {result.Column.TotalWindowOpticalDepth():F4} now  " +
                              $"({o.WindowContinuumOpticalDepth:F4} at {o.WaterVapourReferenceTemperature:F2} K, " +
                              $"foreign share {o.ContinuumForeignFraction:F2})");
            }
            else
            {
                sb.AppendLine("    ... continuum          :       none  (the window never closes)");
            }
        }
        if (o.KDistributionShape != KDistributionShape.Grey && o.KDistributionWidth > 0)
        {
            var k = o.BuildKDistribution();
            sb.AppendLine($"  band structure            : {o.KDistributionShape}, width {o.KDistributionWidth:F2}, " +
                          $"{k.Points} g-points");
            sb.AppendLine($"    ... k spread            : {k.Multipliers[0]:E2} to {k.Multipliers[^1]:E2} " +
                          $"x the band mean");
        }
        if (o.PressureBroadeningExponent > 0)
            sb.AppendLine($"  pressure broadening       : eps' ~ rho (p/p0)^{o.PressureBroadeningExponent:F2}");
        if (o.OzoneFraction > 0)
            sb.AppendLine($"  ozone-layer solar heating : {o.OzoneFraction:F2} of atmospheric SW at " +
                          $"{o.OzoneLayerAltitude / 1000.0:F1} km (H = {o.OzoneLayerWidth / 1000.0:F1} km)");
        if (o.WaterVapourOpticalDepth > 0)
            sb.AppendLine($"  water vapour tau          : {result.Column.CurrentWaterVapourOpticalDepth():F4} now  " +
                          $"({o.WaterVapourOpticalDepth:F4} at {o.WaterVapourReferenceTemperature:F2} K, feedback on)");

        if (o.HasWindow)
        {
            sb.AppendLine();
            sb.AppendLine("  NOTE: with a spectral window each segment's emission is (1 - f(T)) x the");
            sb.AppendLine("        full-spectrum Koenigsberger column 4e'sT^4dz, with f evaluated at that");
            sb.AppendLine("        segment's own temperature, so the ratio varies down the column.");
        }

        if (!IsKoenigsbergerDiffusivity(o))
        {
            sb.AppendLine();
            sb.AppendLine($"  NOTE: with D = {o.Diffusivity:F3} the solver's hemispheric emission is");
            sb.AppendLine($"        2 D eps' sigma T^4 dz, i.e. {o.Diffusivity / 2.0:F3} x the Koenigsberger value.");
            sb.AppendLine("        The 4e'sT^4dz column below is the D = 2 form and will not match.");
        }

        sb.AppendLine();
        sb.AppendLine("Energy budget at equilibrium");
        sb.AppendLine($"  absorbed solar            : {o.AbsorbedSolarFlux,10:F3} W/m2");
        sb.AppendLine($"    ... at the surface      : {result.Column.SurfaceShortwaveAbsorbed,10:F3} W/m2");
        sb.AppendLine($"    ... in the atmosphere   : {o.AbsorbedSolarFlux - result.Column.SurfaceShortwaveAbsorbed,10:F3} W/m2");
        sb.AppendLine($"  outgoing longwave (TOA)   : {r.OutgoingLongwave,10:F3} W/m2");
        sb.AppendLine($"  surface emission          : {result.SurfaceEmission,10:F3} W/m2");
        sb.AppendLine($"  upward longwave at surface: {r.SurfaceUpwardFlux,10:F3} W/m2  (emission + reflection)");
        sb.AppendLine($"  surface downward longwave : {r.SurfaceDownwardFlux,10:F3} W/m2");
        sb.AppendLine($"  surface sensible heat     : {result.SensibleHeatFlux,10:F3} W/m2");
        sb.AppendLine($"  TOA imbalance             : {result.TopOfAtmosphereImbalance,10:E3} W/m2");
        sb.AppendLine($"  surface imbalance         : {result.SurfaceImbalance,10:E3} W/m2");
        sb.AppendLine();

        sb.AppendLine("Result");
        sb.AppendLine($"  emission temperature      : {result.EmissionTemperature,10:F3} K");
        sb.AppendLine($"  surface temperature       : {result.SurfaceTemperature,10:F3} K   ({result.SurfaceTemperature - 273.15:F2} C)");
        sb.AppendLine($"  near-surface air          : {result.NearSurfaceAirTemperature,10:F3} K");
        sb.AppendLine($"  greenhouse warming        : {result.GreenhouseWarming,10:F3} K");
        if (o.Convection == ConvectionMode.Full)
        {
            sb.AppendLine(result.ConvectiveTopAltitude > 0
                ? $"  convecting layer top      : {result.ConvectiveTopAltitude / 1000.0,10:F2} km"
                : "  convecting layer top      :       none  (radiative equilibrium is stable)");
        }
        sb.AppendLine($"  greenhouse flux           : {result.GreenhouseFlux,10:F3} W/m2");
        sb.AppendLine(o.Convection == ConvectionMode.None
            ? "  sol-air temperature       :        n/a       (undefined without convection)"
            : $"  sol-air temperature       : {result.SolAirTemperature,10:F3} K   (equals T_surface at equilibrium)");
        sb.AppendLine($"  steps / simulated time    : {result.Steps} / {result.SimulatedSeconds / 3.15576e7:F1} yr");
        sb.AppendLine($"  converged                 : {result.Converged}");

        if (!result.Converged)
        {
            sb.AppendLine();
            sb.AppendLine($"  WARNING: the run stopped at the {o.MaxSteps}-step cap without reaching");
            sb.AppendLine($"  equilibrium. The profile above is NOT an equilibrium state - the top of");
            sb.AppendLine($"  atmosphere is still out of balance by {result.TopOfAtmosphereImbalance:F4} W/m2.");
            sb.AppendLine("  Raise --max-steps, or check that the configuration admits an equilibrium.");
        }

        return sb.ToString();
    }

    private static bool IsKoenigsbergerDiffusivity(ModelOptions o) =>
        Math.Abs(o.Diffusivity - PhysicalConstants.KoenigsbergerDiffusivity) < 1e-9;

    /// <summary>
    /// Long-format CSV. Interface rows and segment rows are emitted separately, each with
    /// the altitude the quantity actually belongs to, so plotting flux against z is correct.
    /// </summary>
    public static string ToCsv(ModelResult result)
    {
        var c = result.Column;
        var r = result.Radiation;
        var lw = result.LongwaveHeatingRatesPerDay();
        var net = result.NetHeatingRatesPerDay();
        var sb = new StringBuilder();

        sb.AppendLine("level,index,z_m,p_Pa,mass_kg_m2,temperature_K,emission_coefficient_1_m," +
                      "optical_thickness,flux_up_W_m2,flux_down_W_m2,flux_net_up_W_m2," +
                      "emission_solver_W_m2,emission_koenigsberger_W_m2,absorbed_lw_W_m2," +
                      "absorbed_sw_W_m2,lw_heating_K_day,net_heating_K_day");

        void InterfaceRow(string level, int index, double z, double p) =>
            sb.AppendLine(string.Join(",", level, index, F(z), F(p), "", "", "", "",
                F(r.UpwardFlux[index]), F(r.DownwardFlux[index]), F(r.NetUpwardFlux[index]),
                "", "", "", "", "", ""));

        InterfaceRow("INTERFACE_TOA", c.Count, c.Options.TopAltitude, c.Segments[^1].TopPressure);

        for (int i = c.Count - 1; i >= 0; i--)
        {
            var s = c.Segments[i];
            sb.AppendLine(string.Join(",", "SEGMENT", i, F(s.MidAltitude), F(s.MidPressure),
                F(s.MassPerArea), F(s.Temperature), F(s.EmissionCoefficient),
                F(r.OpticalThickness[i]), "", "", "",
                F(r.SegmentEmission[i]), F(r.KoenigsbergerEmission[i]),
                F(r.SegmentAbsorption[i]), F(s.ShortwaveAbsorbed), F(lw[i]), F(net[i])));

            InterfaceRow(i == 0 ? "INTERFACE_SFC" : "INTERFACE", i, s.BottomAltitude, s.BottomPressure);
        }

        // The surface itself: its own emission and the longwave it actually absorbs,
        // eps_s * F_down, which is less than the incident F_down on the interface row above.
        sb.AppendLine(string.Join(",", "SURFACE", -1, "0", F(c.Segments[0].BottomPressure),
            "", F(c.SurfaceTemperature), "", "", "", "", "",
            F(result.SurfaceEmission), "",
            F(c.Options.SurfaceEmissivity * r.SurfaceDownwardFlux),
            F(c.SurfaceShortwaveAbsorbed), "", ""));

        return sb.ToString();
    }

    private static string F(double v) => v.ToString("G10", Inv);
}
