using System.Drawing;
using System.Drawing.Imaging;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>Renders the absorption bands straight to a PNG, like the other exports.</summary>
public static class AbsorptionChartExport
{
    public static void SavePng(string path, IReadOnlyList<AbsorptionTrace> traces,
        double wingCutoff, ChartTheme theme, int width, int height)
    {
        width = Math.Max(760, width);
        height = Math.Max(420, height);

        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            AbsorptionChartPainter.Paint(g, new Rectangle(0, 0, width, height), traces,
                wingCutoff, theme);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        bitmap.Save(path, ImageFormat.Png);
    }
}
