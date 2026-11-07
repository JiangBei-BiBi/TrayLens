using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace TrayLens;

/// <summary>把 Segoe Fluent Icons 的一段字形渲染成对照图，用来挑图标码位。</summary>
internal static class GlyphSheet
{
    public static void RenderList(string path, int[] codes, string fontName = "Segoe Fluent Icons")
    {
        const int cols = 8;
        const int cellW = 96;
        const int cellH = 92;
        int rows = (codes.Length + cols - 1) / cols;
        int width = cols * cellW + 8;
        int height = rows * cellH + 12;

        using Bitmap bmp = new(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(250, 250, 250));
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using Font icon = new(fontName, 40, FontStyle.Regular, GraphicsUnit.Pixel);
        using Font label = new("Consolas", 11, FontStyle.Bold, GraphicsUnit.Pixel);
        using SolidBrush ink = new(Color.FromArgb(20, 20, 20));
        using SolidBrush gray = new(Color.FromArgb(110, 110, 110));

        for (int i = 0; i < codes.Length; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = 4 + col * cellW;
            float y = 4 + row * cellH;
            g.DrawString(char.ConvertFromUtf32(codes[i]), icon, ink, new RectangleF(x, y, cellW - 8, cellH - 30));
            g.DrawString(codes[i].ToString("X4"), label, gray, x + 6, y + cellH - 26);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine(path);
    }

    public static void Render(string path, int start, int count, string fontName = "Segoe Fluent Icons")
    {
        const int cols = 16;
        const int cellW = 58;
        const int cellH = 64;
        int rows = (count + cols - 1) / cols;
        int width = cols * cellW + 8;
        int height = rows * cellH + 30;

        using Bitmap bmp = new(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(250, 250, 250));
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using Font icon = new(fontName, 26, FontStyle.Regular, GraphicsUnit.Pixel);
        using Font label = new("Consolas", 9, FontStyle.Regular, GraphicsUnit.Pixel);
        using SolidBrush ink = new(Color.FromArgb(30, 30, 30));
        using SolidBrush gray = new(Color.FromArgb(120, 120, 120));

        for (int i = 0; i < count; i++)
        {
            int code = start + i;
            int col = i % cols;
            int row = i / cols;
            float x = 4 + col * cellW;
            float y = 20 + row * cellH;

            string glyph = char.ConvertFromUtf32(code);
            g.DrawString(glyph, icon, ink, new RectangleF(x, y, cellW - 4, cellH - 26));
            g.DrawString(code.ToString("X4"), label, gray, x + 2, y + 34);
        }

        using Font title = new("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString($"Segoe Fluent Icons  {start:X4} - {start + count - 1:X4}", title, ink, 6, 2);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine(path);
    }
}
