using System.Drawing;
using System.Drawing.Imaging;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Renders the chart straight to a PNG, with no window involved. Shares
/// <see cref="Co2ChartPainter"/> with the on-screen control, so the exported image is the
/// same figure - and it gives the app a headless mode that a build or a script can call.
/// </summary>
public static class Co2ChartExport
{
    /// <param name="hoverIndex">
    /// Optional concentration index to draw the readout box at, as though the pointer were
    /// resting there. Without it the figure is the plain chart; with it the export also
    /// exercises the hover drawing that is otherwise only reachable through the live UI.
    /// </param>
    public static void SavePng(string path, IReadOnlyList<Co2Sweep> sweeps, ChartTheme theme,
        int width, int height, int? hoverIndex = null)
    {
        width = Math.Max(700, width);
        height = Math.Max(450, height);

        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            Co2ChartPainter.Paint(g, new Rectangle(0, 0, width, height), sweeps, theme, hoverIndex);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        bitmap.Save(path, ImageFormat.Png);
    }
}
