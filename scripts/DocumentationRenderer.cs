// Compiled only by render-docs-assets.ps1. Not part of the distributed app.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace CodexUsageBar
{
    // GDI TextRenderer does not honor a Graphics scale transform. Use the same
    // GDI renderer, font family, layout flags, and coordinates at the export DPI.
    // All geometry and content still come from UsageBarForm.OnPaint.
    internal static class DocumentationTextRenderer
    {
        public static void DrawText(Graphics graphics, string text, Font font,
            Point point, Color color, TextFormatFlags flags)
        {
            using (Matrix transform = graphics.Transform)
            {
                float scale = transform.Elements[0];
                PointF[] points = { new PointF(point.X, point.Y) };
                transform.TransformPoints(points);
                GraphicsState state = graphics.Save();
                try
                {
                    graphics.ResetTransform();
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font scaledFont = new Font(font.FontFamily, font.Size * scale, font.Style, font.Unit))
                        System.Windows.Forms.TextRenderer.DrawText(graphics, text, scaledFont,
                            Point.Round(points[0]), color, flags);
                }
                finally { graphics.Restore(state); }
            }
        }

        public static void DrawText(Graphics graphics, string text, Font font,
            Rectangle bounds, Color color, TextFormatFlags flags)
        {
            using (Matrix transform = graphics.Transform)
            {
                float scale = transform.Elements[0];
                PointF[] points = { new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Bottom) };
                transform.TransformPoints(points);
                Rectangle scaledBounds = Rectangle.FromLTRB((int)Math.Round(points[0].X),
                    (int)Math.Round(points[0].Y), (int)Math.Round(points[1].X), (int)Math.Round(points[1].Y));
                GraphicsState state = graphics.Save();
                try
                {
                    graphics.ResetTransform();
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    using (Font scaledFont = new Font(font.FontFamily, font.Size * scale, font.Style, font.Unit))
                        System.Windows.Forms.TextRenderer.DrawText(graphics, text, scaledFont, scaledBounds, color, flags);
                }
                finally { graphics.Restore(state); }
            }
        }
    }

    public static class DocumentationRenderer
    {
        public static Bitmap RenderTray(int percent, int state, bool claude, int scale)
        {
            if (scale < 1 || scale > 8) throw new ArgumentOutOfRangeException("scale");
            Bitmap image = new Bitmap(20 * scale, 20 * scale);
            image.SetResolution(96, 96);
            try
            {
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.ScaleTransform(scale, scale);
                    typeof(UsageBarForm).GetMethod("DrawPercentageTrayIcon", BindingFlags.Static | BindingFlags.NonPublic)
                        .Invoke(null, new object[] { graphics, percent, state, claude });
                }
                return image;
            }
            catch { image.Dispose(); throw; }
        }

        public static Bitmap Render(Form form, int scale)
        {
            if (scale < 1 || scale > 8) throw new ArgumentOutOfRangeException("scale");
            // GDI text can overwrite alpha bytes in a 32-bit offscreen bitmap.
            // Paint onto opaque RGB first, as a real window does, then mask only
            // the outside corners. This prevents transparent, jagged glyphs.
            Bitmap image = new Bitmap(form.ClientSize.Width * scale, form.ClientSize.Height * scale,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            image.SetResolution(96, 96);
            try
            {
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(form.BackColor);
                    graphics.ScaleTransform(scale, scale);
                    typeof(UsageBarForm).GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(form, new object[] { new PaintEventArgs(graphics, form.ClientRectangle) });
                }
                Bitmap rounded = new Bitmap(image.Width, image.Height);
                rounded.SetResolution(96, 96);
                using (Graphics graphics = Graphics.FromImage(rounded))
                using (TextureBrush texture = new TextureBrush(image))
                using (GraphicsPath path = (GraphicsPath)typeof(UsageBarForm)
                    .GetMethod("RoundedRectangle", BindingFlags.Static | BindingFlags.NonPublic, null,
                        new Type[] { typeof(RectangleF), typeof(float) }, null)
                    .Invoke(null, new object[] { new RectangleF(0, 0, image.Width, image.Height), 10f * scale }))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.FillPath(texture, path);
                }
                image.Dispose();
                return rounded;
            }
            catch { image.Dispose(); throw; }
        }
    }
}
