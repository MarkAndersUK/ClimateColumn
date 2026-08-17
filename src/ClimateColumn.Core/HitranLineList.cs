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
/// Two simplifications, both stated rather than buried. Intensities are used at their 296 K
/// values, because scaling them properly needs total internal partition sums, which is a
/// substantial dependency of its own; and only air broadening is applied, with the width taken
/// proportional to pressure. Neither matters for what this is used for, which is comparing band
/// approximations against a resolved spectrum at a common reference state - both the reference
/// and the approximation see exactly the same line data.
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

            if (intensity < minimumIntensity) continue;
            if (halfWidth <= 0) continue;

            lines.Add(new SpectralLine(nu, intensity, halfWidth));
        }

        if (lines.Count == 0)
        {
            throw new InvalidDataException(
                $"{path} yielded no lines at or above intensity {minimumIntensity:E1}. " +
                "Lower the cutoff or widen the wavenumber range.");
        }

        return lines;
    }

    /// <summary>
    /// The conventional location for the downloaded list, relative to the repository root:
    /// <c>data/hitran-co2-15um.csv</c>. Returns null when it has not been fetched.
    /// </summary>
    public static string? DefaultPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "ClimateColumn.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null) return null;

        string path = Path.Combine(directory.FullName, "data", "hitran-co2-15um.csv");
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
