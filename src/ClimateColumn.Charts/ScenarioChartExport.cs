using System.Drawing;
using System.Drawing.Imaging;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Renders the coupled-gas scenario straight to a PNG, in the same shape as the other exports.
/// </summary>
public static class ScenarioChartExport
{
    public static void SavePng(string path, IReadOnlyList<ScenarioPoint> points, ChartTheme theme,
        int width, int height)
    {
        width = Math.Max(760, width);
        height = Math.Max(460, height);

        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            ScenarioChartPainter.Paint(g, new Rectangle(0, 0, width, height), points,
                ScenarioSweep.CouplingNote, theme);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        bitmap.Save(path, ImageFormat.Png);
    }
}
