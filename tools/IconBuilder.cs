using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

public static class IconBuilder
{
    // 市松模様の背景(明るいグレー)を外周からのflood-fillで透過にする
    static Bitmap RemoveBackground(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.DrawImage(src, 0, 0, w, h);

        var rect = new Rectangle(0, 0, w, h);
        var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        int stride = data.Stride;
        var px = new byte[stride * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);

        Func<int, int, bool> isBg = (x, y) =>
        {
            int i = y * stride + x * 4;
            byte b = px[i], gch = px[i + 1], r = px[i + 2], a = px[i + 3];
            if (a == 0) return false;
            int mx = Math.Max(r, Math.Max(gch, b)), mn = Math.Min(r, Math.Min(gch, b));
            return mn >= 220 && (mx - mn) <= 16;
        };

        var visited = new bool[w * h];
        var queue = new Queue<int>();
        Action<int, int> push = (x, y) =>
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return;
            int idx = y * w + x;
            if (visited[idx]) return;
            visited[idx] = true;
            if (isBg(x, y)) queue.Enqueue(idx);
        };
        for (int x = 0; x < w; x++) { push(x, 0); push(x, h - 1); }
        for (int y = 0; y < h; y++) { push(0, y); push(w - 1, y); }
        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int x = idx % w, y = idx / w;
            px[y * stride + x * 4 + 3] = 0; // alpha = 0
            push(x - 1, y); push(x + 1, y); push(x, y - 1); push(x, y + 1);
        }

        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    static Rectangle FindBounds(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int minX = w, minY = h, maxX = -1, maxY = -1;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new byte[data.Stride * h];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (px[y * data.Stride + x * 4 + 3] > 24)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        bmp.UnlockBits(data);
        return maxX < 0 ? new Rectangle(0, 0, w, h)
                        : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static Color Lighten(Color c, float t) => Color.FromArgb(255,
        (int)(c.R + (255 - c.R) * t),
        (int)(c.G + (255 - c.G) * t),
        (int)(c.B + (255 - c.B) * t));

    public static void Build(string srcPath, string icoPath, string previewPng)
    {
        var blue = Lighten(Color.FromArgb(163, 216, 225), 0.20f); // ロゴの水色
        var pink = Lighten(Color.FromArgb(232, 175, 207), 0.20f); // ロゴの桜色

        using var raw = new Bitmap(srcPath);
        using var cut = RemoveBackground(raw);
        var bounds = FindBounds(cut);

        int[] sizes = { 16, 24, 32, 48, 64, 256 };
        var pngs = new List<byte[]>();
        foreach (var size in sizes)
        {
            using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // 16x16でR4相当の角丸 + 左上=水色→右下=桜色のグラデーション
                float radius = Math.Max(2f, size * 3.5f / 16f);
                var rect = new RectangleF(0, 0, size, size);
                using var path = RoundedRect(rect, radius);
                using var brush = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(size, size), blue, pink);
                g.FillPath(brush, path);

                // 絵柄を少し内側に、縦横比を保って中央配置
                float margin = size * 0.06f;
                float avail = size - margin * 2;
                float scale = Math.Min(avail / bounds.Width, avail / bounds.Height);
                float dw = bounds.Width * scale, dh = bounds.Height * scale;
                g.DrawImage(cut,
                    new RectangleF((size - dw) / 2f, (size - dh) / 2f, dw, dh),
                    bounds, GraphicsUnit.Pixel);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs.Add(ms.ToArray());
            if (size == 256) File.WriteAllBytes(previewPng, ms.ToArray());
        }

        using var outFs = File.Create(icoPath);
        using var bw = new BinaryWriter(outFs);
        bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            bw.Write((byte)(s >= 256 ? 0 : s));
            bw.Write((byte)(s >= 256 ? 0 : s));
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((ushort)1); bw.Write((ushort)32);
            bw.Write((uint)pngs[i].Length); bw.Write((uint)offset);
            offset += pngs[i].Length;
        }
        foreach (var b in pngs) bw.Write(b);
    }
}
