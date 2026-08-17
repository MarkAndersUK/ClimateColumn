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
                    climatecolumn-charts - plots the CO2 concentration response of the column

                    With no arguments it opens a window: the chart, a values grid, a
                    forcing/temperature toggle, a light/dark toggle and a Save PNG action.
                    Hovering reads out every series at the nearest swept concentration.

                    The default view is radiative forcing in W/m2, model against the accepted
                    5.35 ln(C/C0). That comparison borrows nothing: the accepted law is itself a
                    statement about forcing. The temperature view carries no reference curve,
                    because turning that law into a temperature would need the model's own
                    sensitivity - which would make the reference partly a restatement of the
                    thing it is meant to test.

                    Usage: climatecolumn-charts [options]

                      --png PATH        render straight to a PNG and exit, no window
                      --temperature     plot surface temperature instead of forcing
                      --dark            use the dark palette for --png
                      --width N         PNG width in pixels   (1100)
                      --height N        PNG height in pixels  (700)
                      --hover PPM       draw the readout box at this concentration
                      --help            this message

                    The sweep itself lives in ClimateColumn.Core (Co2Sweep), so this app, the
                    CLI and the test suite all plot the same numbers. Only the spectrally derived
                    configuration is charted, so HITRAN data is required - fetch it with
                    scripts/fetch-hitran.ps1 -Molecule all.
                    """);
                return 0;
            }

            string? png = Value(args, "--png");

            if (png is not null)
            {
                var theme = args.Contains("--dark") ? ChartTheme.Dark : ChartTheme.Light;
                int width = Int(args, "--width", 1100);
                int height = Int(args, "--height", 700);

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

                var quantity = args.Contains("--temperature")
                    ? Co2ChartQuantity.SurfaceTemperature
                    : Co2ChartQuantity.Forcing;

                int? hover = null;
                string? hoverPpm = Value(args, "--hover");
                if (hoverPpm is not null)
                {
                    int at = Array.IndexOf(Co2Sweep.Concentrations,
                        double.Parse(hoverPpm, CultureInfo.InvariantCulture));
                    if (at < 0)
                    {
                        throw new ArgumentException(
                            $"--hover expects one of the swept concentrations: " +
                            string.Join(", ", Co2Sweep.Concentrations.Select(c => c.ToString("F0", CultureInfo.InvariantCulture))));
                    }
                    hover = at;
                }

                Co2ChartExport.SavePng(png, sweeps, theme, width, height, hover, quantity);

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

                Console.WriteLine($"Chart written to {Path.GetFullPath(png)}");
                return 0;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static string? Value(string[] args, string name)
    {
        int at = Array.IndexOf(args, name);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
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
