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

                    With no arguments it opens a window: the chart, a values grid, a light/dark
                    toggle and a Save PNG action. Hovering reads out every series at the nearest
                    swept concentration.

                    Usage: climatecolumn-charts [options]

                      --png PATH        render straight to a PNG and exit, no window
                      --dark            use the dark palette for --png
                      --width N         PNG width in pixels   (1100)
                      --height N        PNG height in pixels  (700)
                      --hover PPM       draw the readout box at this concentration
                      --help            this message

                    The sweep itself lives in ClimateColumn.Core (Co2Sweep), so this app, the
                    CLI and the test suite all plot the same numbers.
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
                var sweeps = new[] { Co2Sweep.NoFeedback(), Co2Sweep.WithWaterVapourFeedback() };

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

                Co2ChartExport.SavePng(png, sweeps, theme, width, height, hover);

                int last = Co2Sweep.Concentrations.Length - 1;
                foreach (var sweep in sweeps)
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-28} +{1:F2} K   (expected +{2:F2} K, over by {3:F2})",
                        sweep.Label, sweep.Warming(last),
                        sweep.Expected(last) - sweep.BaseTemperature, sweep.Overshoot(last)));
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
