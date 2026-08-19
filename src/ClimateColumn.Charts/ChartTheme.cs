using System.Drawing;

namespace ClimateColumn.Charts;

/// <summary>
/// Colours for the chart, in light and dark variants. These are the same slots the HTML
/// renderer uses, so the WinForms figure and the generated page look like the same chart:
/// two categorical hues validated for colour-vision deficiency against both surfaces, with
/// warm-biased neutrals.
/// </summary>
public sealed class ChartTheme
{
    public required Color Surface { get; init; }
    public required Color Plane { get; init; }
    public required Color Ink { get; init; }
    public required Color InkSecondary { get; init; }
    public required Color Muted { get; init; }
    public required Color Grid { get; init; }
    public required Color Axis { get; init; }
    public required Color Hairline { get; init; }

    /// <summary>
    /// The accepted-law reference curve.
    /// </summary>
    /// <remarks>
    /// Its own slot rather than reusing <see cref="Axis"/>, because the two carry different
    /// weight. Axis draws structural hairlines - the baseline, gridline emphasis, a crosshair -
    /// which are meant to recede, and it sits at 1.5-1.8:1 against the surface. The reference
    /// curve is data: it is the comparison the forcing figure exists to make, and at that
    /// contrast it was nearly invisible. This clears the 3:1 bar for a meaningful graphic in
    /// both themes while staying quieter than any series.
    /// </remarks>
    public required Color Reference { get; init; }

    /// <summary>Categorical slots, one per configuration.</summary>
    public required Color[] Series { get; init; }

    public static ChartTheme Light { get; } = new()
    {
        Surface = FromHex("#fcfcfb"),
        Plane = FromHex("#f9f9f7"),
        Ink = FromHex("#0b0b0b"),
        InkSecondary = FromHex("#52514e"),
        // Darkened from #898781, which was 3.50:1 against this surface - below the 4.5:1 bar
        // for the small text it carries (tick labels, the highlighted-concentration callouts).
        Muted = FromHex("#747268"),
        Grid = FromHex("#e1e0d9"),
        Axis = FromHex("#c3c2b7"),
        Reference = FromHex("#8f8d85"),
        Hairline = Color.FromArgb(26, 11, 11, 11),
        // Slot 3 (aqua) sits at 2.74:1 against the light surface, below the 3:1 bar. The relief
        // rule applies and is met: every series carries a direct end label, and the values grid
        // repeats every number.
        Series = new[] { FromHex("#2a78d6"), FromHex("#eb6834"), FromHex("#1baf7a") }
    };

    public static ChartTheme Dark { get; } = new()
    {
        Surface = FromHex("#1a1a19"),
        Plane = FromHex("#0d0d0d"),
        Ink = FromHex("#ffffff"),
        InkSecondary = FromHex("#c3c2b7"),
        Muted = FromHex("#898781"),
        Grid = FromHex("#2c2c2a"),
        Axis = FromHex("#383835"),
        Reference = FromHex("#6e6d66"),
        Hairline = Color.FromArgb(26, 255, 255, 255),
        Series = new[] { FromHex("#3987e5"), FromHex("#d95926"), FromHex("#199e70") }
    };

    private static Color FromHex(string hex) => ColorTranslator.FromHtml(hex);
}
