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

    /// <summary>Categorical slots, one per configuration.</summary>
    public required Color[] Series { get; init; }

    public static ChartTheme Light { get; } = new()
    {
        Surface = FromHex("#fcfcfb"),
        Plane = FromHex("#f9f9f7"),
        Ink = FromHex("#0b0b0b"),
        InkSecondary = FromHex("#52514e"),
        Muted = FromHex("#898781"),
        Grid = FromHex("#e1e0d9"),
        Axis = FromHex("#c3c2b7"),
        Hairline = Color.FromArgb(26, 11, 11, 11),
        Series = new[] { FromHex("#2a78d6"), FromHex("#eb6834") }
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
        Hairline = Color.FromArgb(26, 255, 255, 255),
        Series = new[] { FromHex("#3987e5"), FromHex("#d95926") }
    };

    private static Color FromHex(string hex) => ColorTranslator.FromHtml(hex);
}
