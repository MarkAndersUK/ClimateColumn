using System.Globalization;

namespace ClimateColumn.Core;

/// <summary>
/// Reads a line list downloaded from HITRAN into <see cref="SpectralLine"/>s.
/// </summary>
/// <remarks>
/// The file is the comma-separated output of hitran.org's line-by-line API, requested with
/// <c>request_params=nu,sw,gamma_air,n_air</c>: line centre in cm^-1, intensity at 296 K in
/// cm^-1/(molecule cm^-2), air-broadened half-width in cm^-1/atm at 296 K, and the temperature
/// exponent for that width. <c>scripts/fetch-hitran.ps1</c> downloads it.
///
/// The data is not committed. It is third-party data with its own citation requirement, and
/// keeping it out means the test suite still builds and runs with no network and no external
/// files - tests that want real lines skip when the file is absent rather than failing.
///
/// Two simplifications remain, both stated rather than buried. Intensities are used at their
/// 296 K values, because scaling them properly needs total internal partition sums, which is a
/// substantial dependency of its own; and only air broadening is applied, with no self
/// broadening. Half-widths <em>are</em> scaled with temperature via n_air. Neither omission
/// matters much for what this is used for, which is comparing band approximations against a
/// resolved spectrum - both the reference and the approximation see exactly the same line data.
/// </remarks>
public static class HitranLineList
{
    /// <summary>
    /// Loads lines from <paramref name="path"/>, keeping those at or above
    /// <paramref name="minimumIntensity"/>.
    /// </summary>
    /// <param name="minimumIntensity">
    /// Intensity cutoff in HITRAN's units. A real band's line list has a long tail of extremely
    /// weak transitions that cost time and change nothing; 0 keeps every line.
    /// </param>
    public static IReadOnlyList<SpectralLine> Load(string path, double minimumIntensity = 0.0)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"No HITRAN line list at {path}", path);

        var lines = new List<SpectralLine>();
        int lineNumber = 0;

        foreach (string row in File.ReadLines(path))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(row)) continue;

            var fields = row.Split(',');
            if (fields.Length < 3)
            {
                throw new FormatException(
                    $"{path}:{lineNumber} has {fields.Length} fields; expected at least " +
                    "nu, sw, gamma_air. Re-download with " +
                    "request_params=nu,sw,gamma_air,n_air.");
            }

            double nu = Parse(fields[0], path, lineNumber, "nu");
            double intensity = Parse(fields[1], path, lineNumber, "sw");
            double halfWidth = Parse(fields[2], path, lineNumber, "gamma_air");

            // n_air is optional so that a list fetched before this column was read still loads.
            // Absent, the width stays temperature independent, which is the old behaviour.
            double temperatureExponent = fields.Length > 3
                ? Parse(fields[3], path, lineNumber, "n_air")
                : 0.0;

            if (intensity < minimumIntensity) continue;
            if (halfWidth <= 0) continue;

            lines.Add(new SpectralLine(nu, intensity, halfWidth, temperatureExponent));
        }

        if (lines.Count == 0)
        {
            throw new InvalidDataException(
                $"{path} yielded no lines at or above intensity {minimumIntensity:E1}. " +
                "Lower the cutoff or widen the wavenumber range.");
        }

        return lines;
    }

    /// <summary>The CO2 15 um band, as written by scripts/fetch-hitran.ps1.</summary>
    public const string Co2FifteenMicron = "hitran-co2-15um.csv";

    /// <summary>The H2O pure rotational band, as written by scripts/fetch-hitran.ps1.</summary>
    public const string WaterVapourRotational = "hitran-h2o-rot.csv";

    /// <summary>The H2O nu2 bending band, around 6.3 um.</summary>
    public const string WaterVapourBending = "hitran-h2o-bend.csv";

    /// <summary>The ozone 9.6 um band, which sits inside the atmospheric window.</summary>
    public const string OzoneNineSixMicron = "hitran-o3-9.6um.csv";

    /// <summary>The methane 7.7 um band.</summary>
    public const string MethaneSevenSevenMicron = "hitran-ch4-7.7um.csv";

    /// <summary>The nitrous oxide 7.8 um band.</summary>
    public const string NitrousOxideSevenEightMicron = "hitran-n2o-7.8um.csv";

    /// <summary>
    /// Locates a downloaded list under <c>data/</c> beside the solution. Returns null when it
    /// has not been fetched, which is what lets callers skip rather than fail.
    /// </summary>
    public static string? DefaultPath(string fileName = Co2FifteenMicron)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "ClimateColumn.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null) return null;

        string path = Path.Combine(directory.FullName, "data", fileName);
        return File.Exists(path) ? path : null;
    }

    private static double Parse(string field, string path, int lineNumber, string name)
    {
        if (!double.TryParse(field.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                out double value))
        {
            throw new FormatException($"{path}:{lineNumber} could not parse {name} from '{field}'.");
        }
        return value;
    }
}
