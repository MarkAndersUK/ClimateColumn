using System.Drawing;
using System.Drawing.Imaging;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Renders the methane law comparison straight to a PNG. Shares
/// <see cref="MethaneChartPainter"/> with nothing else yet - there is no on-screen methane view -
/// but it is kept in the same shape as the other exports so adding one changes only the caller.
/// </summary>
public static class MethaneChartExport
{
    public static void SavePng(string path, MethaneSweep sweep, ChartTheme theme,
        int width, int height)
    {
        width = Math.Max(700, width);
        height = Math.Max(450, height);

        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            MethaneChartPainter.Paint(g, new Rectangle(0, 0, width, height), sweep, theme);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        bitmap.Save(path, ImageFormat.Png);
    }
}
