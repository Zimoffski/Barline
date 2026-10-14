using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Barline.Services;

/// <summary>
/// Draws the tray icon: the three bars of the logo, monochrome like the Windows 11 tray
/// glyphs — white on a dark taskbar, black on a light one. Bars are hinted to whole pixels
/// with the same formula as scripts/generate-icons.py, so every DPI size stays crisp.
/// </summary>
internal static class TrayIconFactory
{
    private static readonly double[] Heights = [0.56, 1.0, 0.75];

    public static Icon Create(bool lightTaskbar)
    {
        var size = Math.Max(16, Forms.SystemInformation.SmallIconSize.Width);
        var color = lightTaskbar ? Color.Black : Color.White;

        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            DrawBars(graphics, size, color);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static void DrawBars(Graphics graphics, int box, Color color)
    {
        var width = Math.Max(2, RoundHalfUp(box * 0.1875));
        var gap = Math.Max(1, RoundHalfUp(box * 0.11));
        var left = (box - (3 * width + 2 * gap)) / 2;
        var bottom = box - RoundHalfUp(box * 0.125);
        var tallest = RoundHalfUp(box * 0.75);

        using var brush = new SolidBrush(color);
        using var softBrush = new SolidBrush(Color.FromArgb(110, color));

        for (var index = 0; index < Heights.Length; index++)
        {
            var x = left + index * (width + gap);
            var top = bottom - RoundHalfUp(tallest * Heights[index]);
            var height = bottom - top;

            if (width <= 4)
            {
                // Small sizes: square bars with the top corners softened by one pixel.
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.FillRectangle(brush, x, top + 1, width, height - 1);
                graphics.FillRectangle(brush, x + 1, top, width - 2, 1);
                graphics.FillRectangle(softBrush, x, top, 1, 1);
                graphics.FillRectangle(softBrush, x + width - 1, top, 1, 1);
                continue;
            }

            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var radius = Math.Max(1, RoundHalfUp(width * 0.3));
            using var path = RoundedRectangle(new RectangleF(x, top, width, height), radius);
            graphics.FillPath(brush, path);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint hIcon);
}
