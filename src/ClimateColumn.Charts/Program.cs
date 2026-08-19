using System.Globalization;
using System.Windows.Forms;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Any(a => a is "--help" or "-h"))
            {
                Console.WriteLine("""
                    climatecolumn-charts - plots the CO2 response and vertical profile of the column

                    With no arguments it opens a window: the response chart, the vertical
                    temperature profile beside it, a values grid, a forcing/temperature toggle, a
                    light/dark toggle and a Save PNG action for each figure. The two figures are
                    linked - hovering a concentration on the chart draws that concentration's
                    profile, and clicking pins it so the pointer can go elsewhere.

                    The default view is radiative forcing in W/m2, model against the accepted
                    5.35 ln(C/C0). That comparison borrows nothing: the accepted law is itself a
                    statement about forcing. The temperature view carries no reference curve,
                    because turning that law into a temperature would need the model's own
                    sensitivity - which would make the reference partly a restatement of the
                    thing it is meant to test.

                    Usage: climatecolumn-charts [options]

                      --png PATH        render the response chart to a PNG and exit, no window
                      --profile-png PATH  render the vertical profile to a PNG and exit
                      --profile-ppm N   which concentration the profile is drawn at    (580)
                      --warming         plot warming from 285 ppm instead of forcing
                      --temperature     plot absolute surface temperature instead of forcing
                      --dark            use the dark palette for --png
                      --width N         PNG width in pixels   (1100, 620 for the profile)
                      --height N        PNG height in pixels  (700, 820 for the profile)
                      --hover PPM       draw the readout box at this concentration
                      --help            this message

                    The profile figure draws each configuration at the chosen concentration over
                    the reference profile, marks the convecting layer and the height at which the
                    column reaches the planet's emission temperature, and shows the ground
                    separately from the air just above it - the gap between those two is what
                    drives the sensible heat flux.

                    The sweep itself lives in ClimateColumn.Core (Co2Sweep), so this app, the
                    CLI and the test suite all plot the same numbers. Only the spectrally derived
                    configuration is charted, so HITRAN data is required - fetch it with
                    scripts/fetch-hitran.ps1 -Molecule all.
                    """);
                return 0;
            }

            string? png = Value(args, "--png");
            string? profilePng = Value(args, "--profile-png");

            if (png is not null || profilePng is not null)
            {
                var theme = args.Contains("--dark") ? ChartTheme.Dark : ChartTheme.Light;

                Console.WriteLine("Running the column to equilibrium at each concentration…");

                // Empty unless the HITRAN line lists have been fetched.
                var sweeps = Co2Sweep.ForChart();
                if (sweeps.Length == 0)
                {
                    Console.Error.WriteLine(
                        "error: no HITRAN data, so there is nothing to chart. Run " +
                        "scripts/fetch-hitran.ps1 -Molecule all.");
                    return 1;
                }

                if (profilePng is not null)
                {
                    int at = Index(args, "--profile-ppm", Co2Sweep.HighlightPpm);

                    ProfileExport.SavePng(profilePng, sweeps, theme,
                        Int(args, "--width", 620), Int(args, "--height", 820), at);

                    var profile = sweeps[0].Profiles[at];
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0:F0} ppm  surface {1:F3} K, convecting to {2:F2} km, " +
                        "Te = {3:F1} K reached at {4:F2} km",
                        profile.Ppm, profile.SurfaceTemperature,
                        profile.ConvectiveTopAltitude / 1000.0, profile.EmissionTemperature,
                        profile.EmissionAltitude / 1000.0));

                    Console.WriteLine($"Profile written to {Path.GetFullPath(profilePng)}");
                    if (png is null) return 0;
                }

                int width = Int(args, "--width", 1100);
                int height = Int(args, "--height", 700);

                var quantity =
                    args.Contains("--temperature") ? Co2ChartQuantity.SurfaceTemperature :
                    args.Contains("--warming") ? Co2ChartQuantity.Warming :
                    Co2ChartQuantity.Forcing;

                int? hover = Value(args, "--hover") is null ? null : Index(args, "--hover", 0.0);

                Co2ChartExport.SavePng(png!, sweeps, theme, width, height, hover, quantity);

                int last = Co2Sweep.Concentrations.Length - 1;
                foreach (var sweep in sweeps)
                {
                    double accepted = sweep.AcceptedForcing(last);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-28} F = {1:F3} W/m2 vs {2:F3} accepted (ratio {3:F2}),  +{4:F2} K",
                        sweep.Label, sweep.Forcings[last], accepted,
                        Math.Abs(accepted) > 1e-9 ? sweep.Forcings[last] / accepted : double.NaN,
                        sweep.Warming(last)));
                }

                Console.WriteLine($"Chart written to {Path.GetFullPath(png!)}");
                return 0;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            // The message alone is not enough for a window that fails to open. A layout
            // exception from a WinForms control names the property it rejected and nothing
            // about which control or which line set it, so the one-line form sent the search
            // to the wrong place entirely.
            Console.Error.WriteLine($"error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static string? Value(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    /// <summary>
    /// A swept concentration named by an argument, as an index. Only the concentrations the
    /// sweep actually ran are accepted: an unswept one has no equilibrium behind it, so
    /// interpolating to it would invent a profile the model never produced.
    /// </summary>
    private static int Index(string[] args, string name, double fallback)
    {
        string? raw = Value(args, name);
        double ppm = raw is null
            ? fallback
            : double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);

        int at = Array.IndexOf(Co2Sweep.Concentrations, ppm);
        if (at < 0)
        {
            throw new ArgumentException(
                $"{name} expects one of the swept concentrations: " +
                string.Join(", ", Co2Sweep.Concentrations.Select(
                    c => c.ToString("F0", CultureInfo.InvariantCulture))));
        }
        return at;
    }

    private static int Int(string[] args, string name, int fallback)
    {
        string? raw = Value(args, name);
        if (raw is null) return fallback;

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            throw new ArgumentException($"{name} expects a whole number, got '{raw}'");

        return value;
    }
}
