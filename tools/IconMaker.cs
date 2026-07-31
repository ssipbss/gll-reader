using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

class IconMaker {
  static PrivateFontCollection _pfc = new PrivateFontCollection();

  static void Main(string[] args) {
    string icoPath = args.Length > 0 ? args[0] : "app.ico";
    string pngPath = args.Length > 1 ? args[1] : "icon-preview.png";
    int[] sizes = { 256, 128, 64, 48, 32, 24, 16 };
    List<byte[]> dibs = new List<byte[]>();
    using (Bitmap preview = Draw(256)) {
      preview.Save(pngPath, ImageFormat.Png);
    }
    foreach (int s in sizes) {
      using (Bitmap bmp = Draw(s)) {
        dibs.Add(ToDib(bmp));
      }
    }
    WriteIco(icoPath, sizes, dibs);
    Console.WriteLine("ICON OK: " + icoPath);
  }

  static Bitmap Draw(int size) {
    Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (Graphics g = Graphics.FromImage(bmp)) {
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.Clear(Color.Transparent);
      float s = size / 256f;

      RectangleF bg = new RectangleF(8 * s, 8 * s, 240 * s, 240 * s);
      using (LinearGradientBrush b = new LinearGradientBrush(bg,
               Color.FromArgb(255, 255, 255), Color.FromArgb(244, 246, 248), 90f)) {
        g.FillRectangle(b, bg);
      }
      using (GraphicsPath p = new GraphicsPath()) {
        p.AddRectangle(bg);
        using (PathGradientBrush pb = new PathGradientBrush(p)) {
          pb.CenterColor = Color.FromArgb(255, 255, 255);
          pb.SurroundColors = new Color[] { Color.FromArgb(233, 236, 240) };
          g.FillPath(pb, p);
        }
      }
      using (Pen pen = new Pen(Color.FromArgb(226, 229, 234), 1.5f)) {
        g.DrawRectangle(pen, bg.X, bg.Y, bg.Width, bg.Height);
      }

      DrawPixelGlyph(g, size, s);
    }
    return bmp;
  }

  static void DrawPixelGlyph(Graphics g, int size, float s) {
    const int grid = 16;
    using (Bitmap gb = new Bitmap(64, 64, PixelFormat.Format32bppArgb)) {
      using (Graphics gg = Graphics.FromImage(gb)) {
        gg.Clear(Color.White);
        gg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (Font f = PickFont(60f, true)) {
          StringFormat sf = new StringFormat();
          sf.Alignment = StringAlignment.Center;
          sf.LineAlignment = StringAlignment.Center;
          using (SolidBrush ink = new SolidBrush(Color.Black)) {
            gg.DrawString("零", f, ink, new RectangleF(0, 0, 64, 64), sf);
          }
        }
      }

      bool[,] cells = new bool[grid, grid];
      float cellPx = 64f / grid;
      for (int gy = 0; gy < grid; gy++) {
        for (int gx = 0; gx < grid; gx++) {
          int dark = 0;
          int total = 0;
          int x0 = (int)(gx * cellPx);
          int y0 = (int)(gy * cellPx);
          int x1 = Math.Min(64, (int)((gx + 1) * cellPx));
          int y1 = Math.Min(64, (int)((gy + 1) * cellPx));
          for (int yy = y0; yy < y1; yy++) {
            for (int xx = x0; xx < x1; xx++) {
              Color c = gb.GetPixel(xx, yy);
              total++;
              if (c.R < 140) dark++;
            }
          }
          cells[gy, gx] = dark * 5 > total * 2;
        }
      }

      // 加粗：把已填充格子的八邻域也填充
      bool[,] thick = new bool[grid, grid];
      for (int gy = 0; gy < grid; gy++) {
        for (int gx = 0; gx < grid; gx++) {
          if (!cells[gy, gx]) continue;
          for (int dy = -1; dy <= 1; dy++) {
            for (int dx = -1; dx <= 1; dx++) {
              int ny = gy + dy;
              int nx = gx + dx;
              if (ny >= 0 && ny < grid && nx >= 0 && nx < grid) thick[ny, nx] = true;
            }
          }
        }
      }

      int minG = grid, maxG = -1, minH = grid, maxH = -1;
      for (int gy = 0; gy < grid; gy++) {
        for (int gx = 0; gx < grid; gx++) {
          if (thick[gy, gx]) {
            if (gx < minG) minG = gx;
            if (gx > maxG) maxG = gx;
            if (gy < minH) minH = gy;
            if (gy > maxH) maxH = gy;
          }
        }
      }
      float area = 224 * s;
      float cell = area / grid;
      float glyphW = (maxG - minG + 1) * cell;
      float glyphH = (maxH - minH + 1) * cell;
      float offX = (size - glyphW) / 2f - minG * cell;
      float offY = (size - glyphH) / 2f - minH * cell;
      float gap = 2.0f * s;

      for (int pass = 0; pass < 2; pass++) {
        for (int gy = 0; gy < grid; gy++) {
          for (int gx = 0; gx < grid; gx++) {
            if (!thick[gy, gx]) continue;
            float ox = offX + gx * cell;
            float oy = offY + gy * cell;
            if (pass == 0) {
              using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 150, 156, 166))) {
                g.FillRectangle(b, ox + 1.2f * s, oy + 2.4f * s, cell - gap, cell - gap);
              }
            } else {
              using (SolidBrush b = new SolidBrush(Color.FromArgb(31, 41, 55))) {
                g.FillRectangle(b, ox, oy, cell - gap, cell - gap);
              }
            }
          }
        }
      }
    }
  }

  static Font PickFont(float size, bool bold = false) {
    try {
      if (_pfc.Families.Length == 0 && File.Exists("夕体Pro.ttf")) {
        _pfc.AddFontFile("夕体Pro.ttf");
      }
      if (_pfc.Families.Length > 0) {
        return new Font(_pfc.Families[0], size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
      }
    } catch { }
    return new Font("Microsoft YaHei", size, FontStyle.Bold, GraphicsUnit.Pixel);
  }

  static byte[] ToDib(Bitmap bmp) {
    int w = bmp.Width;
    int h = bmp.Height;
    using (MemoryStream ms = new MemoryStream()) {
      using (BinaryWriter bw = new BinaryWriter(ms)) {
        bw.Write(40);
        bw.Write(w);
        bw.Write(h * 2);
        bw.Write((short)1);
        bw.Write((short)32);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        byte[] pixels = new byte[w * h * 4];
        BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try {
          Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
        } finally {
          bmp.UnlockBits(bd);
        }
        for (int y = h - 1; y >= 0; y--) {
          bw.Write(pixels, y * w * 4, w * 4);
        }
        int maskStride = ((w + 31) / 32) * 4;
        byte[] mask = new byte[maskStride * h];
        bw.Write(mask);
      }
      return ms.ToArray();
    }
  }

  static void WriteIco(string path, int[] sizes, List<byte[]> dibs) {
    using (FileStream fs = File.Create(path))
    using (BinaryWriter bw = new BinaryWriter(fs)) {
      bw.Write((short)0);
      bw.Write((short)1);
      bw.Write((short)sizes.Length);
      int offset = 6 + 16 * sizes.Length;
      for (int i = 0; i < sizes.Length; i++) {
        int s = sizes[i];
        bw.Write((byte)(s >= 256 ? 0 : s));
        bw.Write((byte)(s >= 256 ? 0 : s));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((short)1);
        bw.Write((short)32);
        bw.Write(dibs[i].Length);
        bw.Write(offset);
        offset += dibs[i].Length;
      }
      foreach (byte[] d in dibs) {
        bw.Write(d);
      }
    }
  }
}
