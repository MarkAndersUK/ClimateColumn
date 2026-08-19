using System.Drawing;
using System.Drawing.Imaging;
using ClimateColumn.Core;

namespace ClimateColumn.Charts;

/// <summary>
/// Renders the vertical profile straight to a PNG, with no window involved. Shares
/// <see cref="ProfilePainter"/> with the on-screen control, so the exported image is the same
/// figure.
/// </summary>
public static class ProfileExport
{
    /// <param name="selected">Concentration index to draw the profile at.</param>
    /// <param name="hoverLevel">
    /// Optional model level to draw the readout box at, as though the pointer were resting
    /// there. Without it the figure is the plain profile; with it the export also exercises
    /// the hover drawing that is otherwise only reachable through the live UI.
    /// </param>
    public static void SavePng(string path, IReadOnlyList<Co2Sweep> sweeps, ChartTheme theme,
        int width, int height, int selected, int? hoverLevel = null)
    {
        // Taller floor than the response chart: this figure is portrait by nature, and a short
        // one puts fifty kilometres of column into a strip too thin to read.
        width = Math.Max(520, width);
        height = Math.Max(520, height);

        using var bitmap = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bitmap))
        {
            ProfilePainter.Paint(g, new Rectangle(0, 0, width, height), sweeps, theme,
                selected, hoverLevel);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        bitmap.Save(path, ImageFormat.Png);
    }
}
