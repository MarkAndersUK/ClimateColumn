using System.Globalization;
using ClimateColumn.Core;

namespace ClimateColumn.Cli;

public static class Program
{
    public static int Main(string[] rawArgs)
    {
        try
        {
            var args = ArgumentParser.Parse(rawArgs);

            if (args.ContainsKey("help") || args.ContainsKey("h"))
            {
                PrintHelp();
                return 0;
            }

            var options = BuildOptions(args);
            var result = ColumnModel.RunToEquilibrium(options);

            Console.WriteLine();
            Console.WriteLine("=== Vertical atmospheric column: radiative-convective equilibrium ===");
            Console.WriteLine("    Longwave emission : Koenigsberger   dq = 4 eps' sigma T^4 dV");
            Console.WriteLine("    Surface emission  : Stefan-Boltzmann  F = eps_s sigma T^4");
            Console.WriteLine();
            Console.WriteLine(Reporting.FormatSummary(result));
            Console.WriteLine(Reporting.FormatProfile(result));

            if (args.TryGetValue("csv", out var csvPath) && !string.IsNullOrWhiteSpace(csvPath))
            {
                File.WriteAllText(csvPath, Reporting.ToCsv(result));
                Console.WriteLine($"Profile written to {csvPath}");
            }

            if (args.ContainsKey("sensitivity"))
            {
                double factor = GetDouble(args, "sensitivity", 2.0);
                RunSensitivity(options, factor, result);
            }

            if (args.ContainsKey("compare-convection"))
            {
                RunConvectionComparison(options);
            }

            if (args.ContainsKey("co2-scenario"))
            {
                RunCo2Scenario(options, args["co2-scenario"]);
            }

            if (args.ContainsKey("grid-convergence"))
            {
                RunGridConvergence(options);
            }

            return result.Converged ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static void RunSensitivity(ModelOptions baseline, double factor, ModelResult baseResult)
    {
        var perturbed = baseline.Clone();
        perturbed.OpticalDepthScale = baseline.OpticalDepthScale * factor;
        var pr = ColumnModel.RunToEquilibrium(perturbed);

        // Instantaneous forcing: hold the baseline temperatures, change only the dry
        // absorber. Redistributing after the temperatures are copied re-evaluates the
        // water-vapour component at the baseline state, so the vapour is held fixed here
        // and only responds in the equilibrium run - forcing and feedback stay separate.
        var forcedColumn = Column.Build(perturbed);
        for (int i = 0; i < forcedColumn.Count; i++)
            forcedColumn.Segments[i].Temperature = baseResult.Column.Segments[i].Temperature;
        forcedColumn.SurfaceTemperature = baseResult.SurfaceTemperature;
        forcedColumn.DistributeOpticalDepth();
        var forcedRad = RadiationSolver.Solve(forcedColumn);
        double forcing = baseResult.Radiation.OutgoingLongwave - forcedRad.OutgoingLongwave;

        Console.WriteLine($"Sensitivity experiment (optical depth x {factor:F2})");
        Console.WriteLine($"  optical depth       : {baseResult.Column.TotalOpticalDepth():F4} -> {pr.Column.TotalOpticalDepth():F4}");
        Console.WriteLine($"  instantaneous forcing: {forcing,8:F3} W/m2");
        Console.WriteLine($"  surface temperature  : {baseResult.SurfaceTemperature:F3} K -> {pr.SurfaceTemperature:F3} K");
        Console.WriteLine($"  delta T_s            : {pr.SurfaceTemperature - baseResult.SurfaceTemperature,8:F3} K");
        if (Math.Abs(forcing) > 1e-9)
            Console.WriteLine($"  climate sensitivity  : {(pr.SurfaceTemperature - baseResult.SurfaceTemperature) / forcing,8:F4} K per W/m2");
        Console.WriteLine();
    }

    /// <summary>
    /// Equilibrium at a list of CO2 concentrations, with the forcing of each step measured
    /// instantaneously against the one before it (baseline temperatures held, absorber
    /// changed). Any water vapour is re-evaluated at those held temperatures, so it stays
    /// out of the forcing and appears only in the equilibrium response.
    /// </summary>
    private static void RunCo2Scenario(ModelOptions baseline, string spec)
    {
        var ppm = new List<double>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(part.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new ArgumentException($"--co2-scenario expects comma-separated numbers, got '{part}'");
            ppm.Add(v);
        }
        if (ppm.Count < 2) throw new ArgumentException("--co2-scenario needs at least two concentrations.");

        Console.WriteLine("CO2 scenario");
        Console.WriteLine($"  reference {baseline.Co2ReferenceConcentration:F0} ppm = tau {baseline.TotalOpticalDepth:F3}, " +
                          $"CO2 share of the dry absorber {baseline.Co2AbsorberFraction:F2}, " +
                          (baseline.HasWindow
                              ? $"window {baseline.WindowShortWavelength * 1e6:F1}-{baseline.WindowLongWavelength * 1e6:F1} um"
                              : "no window"));
        Console.WriteLine();
        Console.WriteLine("   CO2 [ppm]   dry tau    T_s [K]   dT [K]   forcing [W/m2]   dT/dF [K/(W/m2)]");

        ModelResult? previous = null;
        double first = 0.0;

        foreach (double c in ppm)
        {
            var o = baseline.Clone();
            o.Co2Concentration = c;
            var r = ColumnModel.RunToEquilibrium(o);
            if (!r.Converged)
                Console.WriteLine($"  WARNING: {c:F0} ppm did not converge; the row below is not an equilibrium.");

            string dt = "-", forcingText = "-", sensitivity = "-";
            if (previous is not null)
            {
                var held = Column.Build(o);
                for (int i = 0; i < held.Count; i++)
                    held.Segments[i].Temperature = previous.Column.Segments[i].Temperature;
                held.SurfaceTemperature = previous.SurfaceTemperature;
                held.DistributeOpticalDepth();
                double forcing = previous.Radiation.OutgoingLongwave -
                                 RadiationSolver.Solve(held).OutgoingLongwave;

                double warming = r.SurfaceTemperature - previous.SurfaceTemperature;
                dt = warming.ToString("F3", CultureInfo.InvariantCulture);
                forcingText = forcing.ToString("F3", CultureInfo.InvariantCulture);
                if (Math.Abs(forcing) > 1e-9)
                    sensitivity = (warming / forcing).ToString("F4", CultureInfo.InvariantCulture);
            }
            else
            {
                first = r.SurfaceTemperature;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,10:F0} {1,9:F3} {2,10:F3} {3,8} {4,16} {5,18}",
                c, o.EffectiveDryOpticalDepth, r.SurfaceTemperature, dt, forcingText, sensitivity));

            previous = r;
        }

        Console.WriteLine();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  total change {0:F0} -> {1:F0} ppm : {2:+0.000;-0.000} K",
            ppm[0], ppm[^1], previous!.SurfaceTemperature - first));
        Console.WriteLine();
    }

    private static void RunConvectionComparison(ModelOptions baseline)
    {
        Console.WriteLine("Convection comparison");
        Console.WriteLine("  mode           T_surface [K]   OLR [W/m2]   lapse 0-10km [K/km]");
        foreach (var mode in new[] { ConvectionMode.None, ConvectionMode.SurfaceOnly, ConvectionMode.Full })
        {
            var o = baseline.Clone();
            o.Convection = mode;
            var r = ColumnModel.RunToEquilibrium(o);

            double lapse = EstimateLapseRate(r.Column, 10_000.0) * 1000.0;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-12} {1,12:F3} {2,12:F3} {3,21:F3}",
                mode, r.SurfaceTemperature, r.Radiation.OutgoingLongwave, lapse));
        }
        Console.WriteLine();
    }

    private static void RunGridConvergence(ModelOptions baseline)
    {
        var start = baseline.Clone();
        start.SegmentCount = Math.Max(10, baseline.SegmentCount / 8);
        var study = GridConvergence.Study(start, 4);

        Console.WriteLine("Grid convergence");
        Console.WriteLine("  segments   dz [m]   T_surface [K]   change [K]");
        for (int i = 0; i < study.Levels.Count; i++)
        {
            var level = study.Levels[i];
            string change = i == 0 ? "-" : study.Differences[i - 1].ToString("F3", CultureInfo.InvariantCulture);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,8} {1,8:F1} {2,15:F3} {3,12}",
                level.SegmentCount, baseline.TopAltitude / level.SegmentCount,
                level.SurfaceTemperature, change));
        }
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  observed order of convergence : {0:F3}  (noisy once the spread is small)",
            study.ObservedOrder));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  Richardson limit (dz -> 0)    : {0:F3} K", study.ExtrapolatedSurfaceTemperature));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "  residual error on finest grid : {0:F3} K", study.FinestGridError));
        Console.WriteLine();
    }

    private static double EstimateLapseRate(Column column, double depth)
    {
        Segment? top = null;
        foreach (var s in column.Segments)
        {
            if (s.MidAltitude <= depth) top = s;
        }
        if (top is null || top.MidAltitude < 1.0) return double.NaN;
        return (column.SurfaceTemperature - top.Temperature) / top.MidAltitude;
    }

    private static ModelOptions BuildOptions(Dictionary<string, string> args)
    {
        var o = new ModelOptions
        {
            SegmentCount = (int)GetDouble(args, "segments", 80),
            TopAltitude = GetDouble(args, "top-km", 50.0) * 1000.0,
            SolarConstant = GetDouble(args, "solar", PhysicalConstants.SolarConstant),
            Albedo = GetDouble(args, "albedo", 0.30),
            AtmosphericShortwaveFraction = GetDouble(args, "sw-atm-fraction", 0.22),
            SurfaceEmissivity = GetDouble(args, "surface-emissivity", 0.98),
            TotalOpticalDepth = GetDouble(args, "optical-depth", 1.8),
            OpticalDepthScale = GetDouble(args, "optical-depth-scale", 1.0),
            Diffusivity = GetDouble(args, "diffusivity", PhysicalConstants.KoenigsbergerDiffusivity),
            Co2Concentration = GetDouble(args, "co2-ppm", 285.0),
            Co2ReferenceConcentration = GetDouble(args, "co2-reference-ppm", 285.0),
            Co2AbsorberFraction = GetDouble(args, "co2-fraction", 1.0),
            WindowShortWavelength = GetMicrons(args, "window-from-um", 0.0),
            WindowLongWavelength = GetMicrons(args, "window-to-um", 0.0),
            WindowContinuumOpticalDepth = GetDouble(args, "continuum-tau", 0.0),
            ContinuumForeignFraction = GetDouble(args, "continuum-foreign", 0.5),
            PressureBroadeningExponent = GetDouble(args, "pressure-broadening", 0.0),
            OzoneFraction = GetDouble(args, "ozone-fraction", 0.0),
            OzoneLayerAltitude = GetDouble(args, "ozone-altitude-km", 25.0) * 1000.0,
            OzoneLayerWidth = GetDouble(args, "ozone-width-km", 5.0) * 1000.0,
            WaterVapourOpticalDepth = GetDouble(args, "wv-tau", 0.0),
            WaterVapourScaleHeight = GetDouble(args, "wv-scale-height-km", 2.0) * 1000.0,
            WindSpeed = GetDouble(args, "wind", 3.0),
            CriticalLapseRate = GetDouble(args, "lapse-rate", 6.5) / 1000.0,
            SurfaceHeatCapacity = GetDouble(args, "surface-heat-capacity", 4.18e7),
            MaxSteps = (int)GetDouble(args, "max-steps", 500_000)
        };

        if (args.TryGetValue("convection", out var conv))
        {
            o.Convection = conv.ToLowerInvariant() switch
            {
                "none" => ConvectionMode.None,
                "surface" or "surfaceonly" or "surface-only" => ConvectionMode.SurfaceOnly,
                "full" or "adjust" => ConvectionMode.Full,
                _ => throw new ArgumentException($"unknown convection mode '{conv}'")
            };
        }

        if (args.ContainsKey("isothermal")) o.InitialiseFromStandardAtmosphere = false;

        return o;
    }

    /// <summary>
    /// Reads a wavelength given in microns and returns metres. Rejects the old
    /// <c>--window</c> spelling explicitly, since silently reinterpreting a fraction as a
    /// wavelength would produce a plausible-looking wrong answer.
    /// </summary>
    private static double GetMicrons(Dictionary<string, string> args, string key, double fallback)
    {
        if (args.ContainsKey("window"))
        {
            throw new ArgumentException(
                "--window took a bare fraction of the spectrum, which is not well defined on " +
                "its own: the share of emission inside a fixed wavelength interval depends on " +
                "the emitter's temperature. Name the interval instead, e.g. " +
                "--window-from-um 8 --window-to-um 13 for Earth's water-vapour window.");
        }

        return GetDouble(args, key, fallback) * 1e-6;
    }

    private static double GetDouble(Dictionary<string, string> args, string key, double fallback)
    {
        if (!args.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"--{key} expects a number, got '{raw}'");
        return value;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        climatecolumn - 1-D radiative-convective model of a vertical atmospheric column

        The column is divided into segments. Longwave emission from each segment follows the
        Koenigsberger equation dq = 4 eps' sigma T^4 dV; the surface radiates by
        Stefan-Boltzmann. The model marches to equilibrium and prints the flux profile.

        Usage: climatecolumn [options]

          --segments N               number of segments                     (80)
          --top-km X                 altitude of the column top, km         (50)
          --solar X                  solar constant, W/m2                   (1361)
          --albedo X                 planetary albedo                       (0.30)
          --sw-atm-fraction X        share of absorbed solar taken by air   (0.22)
          --surface-emissivity X     surface longwave emissivity            (0.98)
          --optical-depth X          column hemispheric optical depth       (1.8)
          --optical-depth-scale X    multiplier on the above                (1.0)
          --diffusivity X            two-stream factor D; 2 = Koenigsberger (2.0)
          --co2-ppm X                CO2 concentration                      (285)
          --co2-reference-ppm X      ppm at which --optical-depth applies   (285)
          --co2-fraction X           CO2 share of dry absorber at ref ppm   (1.0)
          --co2-scenario A,B,C       equilibrium at each ppm, with forcings
          --window-from-um X         transparent window, short edge, um     (none)
          --window-to-um X           transparent window, long edge, um      (none)
          --continuum-tau X          water-vapour continuum in the window   (0)
          --continuum-foreign X      foreign share of the continuum         (0.5)
          --pressure-broadening N    dry absorber ~ rho (p/p0)^N            (0)
          --ozone-fraction X         share of atm. solar into ozone layer   (0)
          --ozone-altitude-km X      Chapman layer peak altitude            (25)
          --ozone-width-km X         Chapman layer scale height             (5)
          --wv-tau X                 water vapour tau at 288.15 K; CC feedback (0)
          --wv-scale-height-km X     water vapour scale height              (2)
          --convection MODE          none | surface | full                  (full)
          --wind X                   surface wind speed, m/s, for h_c       (3.0)
          --lapse-rate X             critical lapse rate, K/km              (6.5)
          --surface-heat-capacity X  J/m2/K                                 (4.18e7)
          --max-steps N              iteration cap                          (500000)
          --isothermal               start isothermal instead of US Std Atm
          --csv PATH                 write the profile to a CSV file
          --sensitivity F            also run with optical depth x F and report dT/dF
          --compare-convection       run all three convection modes side by side
          --grid-convergence         refine the grid 4x and report the convergence order
          --help                     this message

        The flux recurrence is exact across a constant-temperature segment, so resolution
        buys profile detail rather than accuracy. --grid-convergence confirms this for a
        given configuration; it matters most at large optical depth.
        """);
    }
}

internal static class ArgumentParser
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (!a.StartsWith('-')) continue;

            string key = a.TrimStart('-');
            string value = "";

            int eq = key.IndexOf('=');
            if (eq >= 0)
            {
                value = key[(eq + 1)..];
                key = key[..eq];
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[++i];
            }

            result[key] = value;
        }
        return result;
    }
}
