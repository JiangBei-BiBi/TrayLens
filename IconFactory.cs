using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TrayLens;

internal static class IconFactory
{
    internal static readonly Color DownLight = Color.FromArgb(16, 124, 96);
    internal static readonly Color DownDark = Color.FromArgb(126, 240, 200);
    internal static readonly Color BattLight = Color.FromArgb(150, 90, 0);
    internal static readonly Color BattDark = Color.FromArgb(255, 199, 120);
    internal static readonly Color WarnLight = Color.FromArgb(180, 30, 40);
    internal static readonly Color WarnDark = Color.FromArgb(255, 130, 140);

    private static readonly StringFormat Center = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        Trimming = StringTrimming.None
    };

    public static bool TaskbarIsLight()
    {
        try
        {
            object? v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0);
            return v is int i && i == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>单行文本图标。字号按 size * ratio 计算，16px 下 ratio 超过 0.5 基本必被裁。</summary>
    public static Icon CreateSingle(string text, int size, Color lightColor, Color darkColor, float ratio)
    {
        using Bitmap bmp = DrawSingle(text, size, lightColor, darkColor, ratio, TaskbarIsLight());
        return FromBitmap(bmp);
    }

    private static Bitmap DrawSingle(string text, int size, Color lightColor, Color darkColor, float ratio, bool light)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.None;
        // 16px 下抗锯齿会把笔画糊成一团，硬边反而更好认
        g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
        using Font font = new("Segoe UI", size * ratio, FontStyle.Bold, GraphicsUnit.Pixel);
        using SolidBrush brush = new(light ? lightColor : darkColor);
        g.DrawString(text, font, brush, new RectangleF(0, 0, size, size), Center);
        return bmp;
    }

    public static Icon FromBitmap(Bitmap bmp)
    {
        IntPtr handle = bmp.GetHicon();
        try
        {
            using Icon temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    // ---------------- 预览 ----------------

    private sealed record Sample(string Title, string Text, float Ratio, bool IsBattery, bool IsLow);

    public static void RenderPreview(string outDir)
    {
        Directory.CreateDirectory(outDir);

        var samples = new[]
        {
            new Sample("网速 5.6M", "5.6M", 0.46f, false, false),
            new Sample("网速 1.2M", "1.2M", 0.46f, false, false),
            new Sample("网速 980K", "980K", 0.46f, false, false),
            new Sample("网速 0", "0", 0.46f, false, false),
            new Sample("电量 87", "87", 0.62f, true, false),
            new Sample("电量 9", "9", 0.62f, true, false),
            new Sample("低电 15", "15", 0.62f, true, true),
        };
        int[] sizes = { 16, 24, 32 };
        const int zoom = 8;

        int cellW = 16 * zoom + 26;
        int leftPad = 70;
        int width = leftPad + cellW * samples.Length + 16;
        int height = 40 + sizes.Length * 2 * 26 + 16 * zoom + 40 + 60;

        using Bitmap canvas = new(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(250, 250, 250));
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            using Font title = new("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using Font small = new("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
            using SolidBrush ink = new(Color.FromArgb(40, 40, 40));
            using SolidBrush gray = new(Color.FromArgb(130, 130, 130));

            g.DrawString("TrayLens 图标最终稿 · 每条：上=深色任务栏 / 下=浅色任务栏", title, ink, 10, 8);

            for (int i = 0; i < samples.Length; i++)
            {
                g.DrawString(samples[i].Title, small, gray, leftPad + i * cellW, 26);
            }

            int y = 44;
            foreach (int size in sizes)
            {
                g.DrawString($"{size}px", small, gray, 10, y + 2);

                using (SolidBrush bar = new(Color.FromArgb(32, 32, 32)))
                    g.FillRectangle(bar, leftPad, y, cellW * samples.Length, 24);
                for (int i = 0; i < samples.Length; i++)
                {
                    using Bitmap bmp = Compose(samples[i], size, dark: true);
                    g.DrawImage(bmp, leftPad + i * cellW + 4, y + (24 - size) / 2f, size, size);
                }
                y += 24;

                using (SolidBrush bar = new(Color.FromArgb(243, 243, 243)))
                    g.FillRectangle(bar, leftPad, y, cellW * samples.Length, 24);
                for (int i = 0; i < samples.Length; i++)
                {
                    using Bitmap bmp = Compose(samples[i], size, dark: false);
                    g.DrawImage(bmp, leftPad + i * cellW + 4, y + (24 - size) / 2f, size, size);
                }
                y += 28;
            }

            // 16px 放大对照（深色底）
            g.DrawString("16px 放大 8×", small, gray, 10, y + 2);
            using (SolidBrush bar = new(Color.FromArgb(32, 32, 32)))
                g.FillRectangle(bar, leftPad, y, cellW * samples.Length, 16 * zoom + 8);
            for (int i = 0; i < samples.Length; i++)
            {
                using Bitmap bmp = Compose(samples[i], 16, dark: true);
                g.DrawImage(bmp, leftPad + i * cellW + 4, y + 4, 16 * zoom, 16 * zoom);
            }
            y += 16 * zoom + 14;

            g.DrawString("注：ratio 固定 0.46(网速) / 0.62(电量)。16px 下 4 字符是上限，再多就被裁。", small, gray, 10, y);
        }

        string path = Path.Combine(outDir, "icon-final.png");
        canvas.Save(path, ImageFormat.Png);
        Console.WriteLine(path);
    }

    private static Bitmap Compose(Sample s, int size, bool dark)
    {
        Color light = s.IsBattery ? (s.IsLow ? WarnLight : BattLight) : DownLight;
        Color darkColor = s.IsBattery ? (s.IsLow ? WarnDark : BattDark) : DownDark;
        // 深色任务栏要亮色文字，浅色任务栏要暗色文字
        return DrawSingle(s.Text, size, light, darkColor, s.Ratio, light: !dark);
    }

    public static void RenderCompare(string outDir)
    {
        Directory.CreateDirectory(outDir);

        string[] texts = { "5.6M", "12M", "980K", "1.2M" };
        float[] ratios = { 0.38f, 0.42f, 0.46f, 0.50f };
        var hints = new[] { TextRenderingHint.AntiAliasGridFit, TextRenderingHint.SingleBitPerPixelGridFit };
        const int zoom = 12;
        const int cell = 16 * zoom + 14;

        int width = 86 + cell * ratios.Length + 16;
        int height = 46 + hints.Length * (texts.Length * (16 * zoom + 26) + 30);

        using Bitmap canvas = new(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.FromArgb(250, 250, 250));
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            using Font title = new("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using Font small = new("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
            using SolidBrush ink = new(Color.FromArgb(40, 40, 40));
            using SolidBrush gray = new(Color.FromArgb(130, 130, 130));

            g.DrawString("16px 网速图标：字号 × 抗锯齿方式", title, ink, 10, 8);
            for (int r = 0; r < ratios.Length; r++)
                g.DrawString("ratio " + ratios[r].ToString("0.00"), small, gray, 86 + r * cell, 28);

            int y = 46;
            foreach (var hint in hints)
            {
                g.DrawString(hint == TextRenderingHint.SingleBitPerPixelGridFit ? "无抗锯齿" : "抗锯齿",
                    title, ink, 10, y + 2);
                y += 20;

                foreach (string text in texts)
                {
                    g.DrawString(text, small, gray, 10, y + 40);
                    for (int r = 0; r < ratios.Length; r++)
                    {
                        using Bitmap bmp = DrawWith(text, 16, DownDark, ratios[r], hint);
                        g.DrawImage(bmp, 86 + r * cell, y, 16 * zoom, 16 * zoom);
                    }
                    y += 16 * zoom + 26;
                }
                y += 10;
            }
        }

        string path = Path.Combine(outDir, "icon-compare.png");
        canvas.Save(path, ImageFormat.Png);
        Console.WriteLine(path);
    }

    private static Bitmap DrawWith(string text, int size, Color color, float ratio, TextRenderingHint hint)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.TextRenderingHint = hint;
        using Font font = new("Segoe UI", size * ratio, FontStyle.Bold, GraphicsUnit.Pixel);
        using SolidBrush brush = new(color);
        g.DrawString(text, font, brush, new RectangleF(0, 0, size, size), Center);
        return bmp;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
