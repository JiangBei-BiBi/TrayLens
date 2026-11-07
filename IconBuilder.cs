using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace TrayLens;

/// <summary>自己画一个仪表盘图标并导出多尺寸 .ico（exe 图标用）。</summary>
internal static class IconBuilder
{
    public static void BuildIco(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var frames = new List<(int size, byte[] png)>();

        foreach (int size in sizes)
        {
            using Bitmap bmp = Draw(size);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            frames.Add((size, ms.ToArray()));
        }

        using FileStream fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        w.Write((ushort)0);              // reserved
        w.Write((ushort)1);              // type = icon
        w.Write((ushort)frames.Count);

        int offset = 6 + 16 * frames.Count;
        foreach ((int size, byte[] png) in frames)
        {
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);            // color count
            w.Write((byte)0);            // reserved
            w.Write((ushort)1);          // planes
            w.Write((ushort)32);         // bpp
            w.Write(png.Length);
            w.Write(offset);
            offset += png.Length;
        }

        foreach ((_, byte[] png) in frames) w.Write(png);
        Console.WriteLine(path);
    }

    private static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        float pad = size * 0.04f;
        var rect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);

        using (var bg = new LinearGradientBrush(rect,
                   Color.FromArgb(0xFF, 0x0E, 0x7C, 0x62),
                   Color.FromArgb(0xFF, 0x18, 0x5A, 0xB4), 45f))
        {
            using GraphicsPath path = RoundedRect(rect, size * 0.22f);
            g.FillPath(bg, path);
        }

        // 表盘：下半圆留空，刻度弧
        float cx = size / 2f;
        float cy = size * 0.62f;
        float r = size * 0.30f;
        float thickness = Math.Max(1.4f, size * 0.055f);

        using (var arcPen = new Pen(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF), thickness)
               { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawArc(arcPen, cx - r, cy - r, r * 2, r * 2, 190, 160);
        }

        // 指针指向右上
        double angle = -55 * Math.PI / 180;
        using (var needle = new Pen(Color.White, Math.Max(1.2f, size * 0.05f))
               { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(needle, cx, cy,
                cx + (float)(Math.Cos(angle) * r * 0.80),
                cy + (float)(Math.Sin(angle) * r * 0.80));
        }

        // 轴心
        float dot = Math.Max(1.2f, size * 0.055f);
        using (var brush = new SolidBrush(Color.White))
        {
            g.FillEllipse(brush, cx - dot, cy - dot, dot * 2, dot * 2);
        }

        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
