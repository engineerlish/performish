using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

// Icon generator for Performish - follows the exact process in _ToolTemplate/DESIGN.md section 4:
// compute geometry, don't eyeball it; solid full-opacity strokes/fills only; render+look at a
// 256px preview and the 32px frame specifically before shipping.
internal static class Program
{
    private static readonly Color Bg = Color.FromArgb(255, 10, 12, 10);
    private static readonly Color Accent = Color.FromArgb(255, 87, 242, 135);

    private static void Main()
    {
        var outDir = AppContext.BaseDirectory;
        DrawPreview(Path.Combine(outDir, "preview.png"), 256);

        var icoPath = Path.Combine(outDir, "performish.ico");
        WriteIco(icoPath, new[] { 16, 32, 48, 64, 128, 256 });
        Console.WriteLine("Wrote " + icoPath);

        using (var loaded = new Icon(icoPath))
        {
            Console.WriteLine($"Verified load OK: {loaded.Width}x{loaded.Height}");
            using var small = new Icon(icoPath, 32, 32);
            using var smallBmp = small.ToBitmap();
            smallBmp.Save(Path.Combine(outDir, "icon-check-32.png"), ImageFormat.Png);
        }
    }

    private static void DrawPreview(string path, int size)
    {
        using var bmp = Draw(size);
        bmp.Save(path, ImageFormat.Png);
        Console.WriteLine("Wrote " + path);
    }

    private static void WriteIco(string path, int[] sizes)
    {
        var pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using var bmp = Draw(sizes[i]);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs[i] = ms.ToArray();
        }

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write((short)0);
        bw.Write((short)1);
        bw.Write((short)sizes.Length);

        int headerSize = 6 + 16 * sizes.Length;
        int offset = headerSize;

        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            byte dim = (byte)(size >= 256 ? 0 : size);
            bw.Write(dim);
            bw.Write(dim);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((short)1);
            bw.Write((short)32);
            bw.Write(pngs[i].Length);
            bw.Write(offset);
            offset += pngs[i].Length;
        }

        foreach (var png in pngs)
            bw.Write(png);
    }

    /// <summary>A performance gauge: a 270-degree dial arc (90-degree gap centered on top) with a
    /// needle pointing near the high end of the sweep - reads as "speed/performance meter, pegged
    /// high" at a glance. All angles computed from the gap's symmetric placement around straight-up
    /// (270 degrees in screen-space convention), not eyeballed.</summary>
    public static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        float pad = size * 0.06f;
        var backRect = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
        float radius = size * 0.20f;
        using (var backPath = RoundedRect(backRect, radius))
        using (var backBrush = new SolidBrush(Bg))
            g.FillPath(backBrush, backPath);

        float s = size;
        float cx = s * 0.5f;
        float cy = s * 0.54f;
        float dialRadius = s * 0.28f;
        float strokeWidth = s * 0.07f;

        // Gap is centered on straight-up (270 deg); half-gap is 45 deg either side, so the arc
        // covers the remaining 270 deg, starting just past the gap on the left side.
        const float gapHalfWidthDeg = 45f;
        const float startAngleDeg = 270f + gapHalfWidthDeg; // = 315
        const float sweepDeg = 360f - gapHalfWidthDeg * 2;  // = 270

        var arcRect = new RectangleF(cx - dialRadius, cy - dialRadius, dialRadius * 2, dialRadius * 2);
        using (var arcPen = new Pen(Accent, strokeWidth) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawArc(arcPen, arcRect, startAngleDeg, sweepDeg);

        // Needle points a quarter of the way into the swept range from the start (near-max reading,
        // just past the top-right end of the gap) - computed from the same start/sweep, not guessed.
        double needleAngleDeg = startAngleDeg + sweepDeg * 0.09;
        double needleRad = needleAngleDeg * Math.PI / 180.0;
        float needleLength = dialRadius * 0.78f;
        var needleTip = new PointF(
            cx + (float)Math.Cos(needleRad) * needleLength,
            cy + (float)Math.Sin(needleRad) * needleLength);

        using (var needlePen = new Pen(Accent, strokeWidth * 0.75f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(needlePen, cx, cy, needleTip.X, needleTip.Y);

        float hubRadius = strokeWidth * 0.9f;
        using (var hubBrush = new SolidBrush(Accent))
            g.FillEllipse(hubBrush, cx - hubRadius, cy - hubRadius, hubRadius * 2, hubRadius * 2);

        return bmp;
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
