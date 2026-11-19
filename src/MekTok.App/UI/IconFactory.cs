using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;

namespace MekTok.App.UI;

/// <summary>
/// Renders crisp vector-quality keycap icons for the Windows system tray (active vs muted/paused).
/// </summary>
public static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Drawing.Icon CreateTrayIcon(bool active, bool darkTaskbar = true)
    {
        const int size = 32;
        using var bmp = new Drawing.Bitmap(size, size, Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Drawing.Graphics g = Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Drawing.Color.Transparent);

        // Outer keycap skirt
        using Drawing2D.GraphicsPath outer = CreateRoundedRect(2f, 2f, size - 4f, size - 4f, 7f);
        Drawing.Color topGrad = active
            ? Drawing.Color.FromArgb(255, 76, 194, 255)
            : Drawing.Color.FromArgb(255, 118, 124, 136);
        Drawing.Color bottomGrad = active
            ? Drawing.Color.FromArgb(255, 0, 103, 192)
            : Drawing.Color.FromArgb(255, 72, 76, 86);

        using (var bgBrush = new Drawing2D.LinearGradientBrush(
            new Drawing.PointF(0, 2f),
            new Drawing.PointF(0, size - 2f),
            topGrad,
            bottomGrad))
        {
            g.FillPath(bgBrush, outer);
        }

        // Inner keycap dish
        using Drawing2D.GraphicsPath inner = CreateRoundedRect(6f, 4.8f, size - 12f, size - 13.5f, 4.5f);
        Drawing.Color dishColor = darkTaskbar
            ? Drawing.Color.FromArgb(245, 28, 32, 42)
            : Drawing.Color.FromArgb(245, 36, 40, 52);
        using (var dishBrush = new Drawing.SolidBrush(dishColor))
        {
            g.FillPath(dishBrush, inner);
        }

        if (active)
        {
            using var wavePen = new Drawing.Pen(Drawing.Color.FromArgb(255, 118, 214, 255), 2.3f)
            {
                StartCap = Drawing2D.LineCap.Round,
                EndCap = Drawing2D.LineCap.Round
            };
            g.DrawLine(wavePen, 11.5f, 12.2f, 11.5f, 16.4f);
            g.DrawLine(wavePen, 16.0f, 10.2f, 16.0f, 18.4f);
            g.DrawLine(wavePen, 20.5f, 12.5f, 20.5f, 16.1f);
        }
        else
        {
            using var mutePen = new Drawing.Pen(Drawing.Color.FromArgb(255, 255, 110, 110), 2.6f)
            {
                StartCap = Drawing2D.LineCap.Round,
                EndCap = Drawing2D.LineCap.Round
            };
            g.DrawLine(mutePen, 10f, 9.5f, 22f, 19.5f);
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var temp = Drawing.Icon.FromHandle(hIcon);
            return (Drawing.Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static Drawing2D.GraphicsPath CreateRoundedRect(float x, float y, float w, float h, float r)
    {
        var path = new Drawing2D.GraphicsPath();
        path.AddArc(x, y, r, r, 180, 90);
        path.AddArc(x + w - r, y, r, r, 270, 90);
        path.AddArc(x + w - r, y + h - r, r, r, 0, 90);
        path.AddArc(x, y + h - r, r, r, 90, 90);
        path.CloseFigure();
        return path;
    }
}

