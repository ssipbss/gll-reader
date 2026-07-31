using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

class IconMaker {
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
    const int gw = 64;
    const int gh = 20;
    using (Bitmap gb = new Bitmap(192, 80, PixelFormat.Format32bppArgb)) {
      using (Graphics gg = Graphics.FromImage(gb)) {
        gg.Clear(Color.White);
        gg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (Font f = new Font("Arial Black", 60f, FontStyle.Regular, GraphicsUnit.Pixel)) {
          StringFormat sf = new StringFormat();
          sf.Alignment = StringAlignment.Center;
          sf.LineAlignment = StringAlignment.Center;
          using (SolidBrush ink = new SolidBrush(Color.Black)) {
            gg.DrawString("ZERO", f, ink, new RectangleF(0, 0, 192, 80), sf);
          }
        }
      }

      bool[,] cells = new bool[gh, gw];
      float cellPx = 192f / gw;
      float cellPy = 80f / gh;
      for (int gy = 0; gy < gh; gy++) {
        for (int gx = 0; gx < gw; gx++) {
          int dark = 0;
          int total = 0;
          int x0 = (int)(gx * cellPx);
          int y0 = (int)(gy * cellPy);
          int x1 = Math.Min(192, (int)((gx + 1) * cellPx));
          int y1 = Math.Min(80, (int)((gy + 1) * cellPy));
          for (int yy = y0; yy < y1; yy++) {
            for (int xx = x0; xx < x1; xx++) {
              Color c = gb.GetPixel(xx, yy);
              total++;
              if (c.R < 130) dark++;
            }
          }
          cells[gy, gx] = dark * 2 > total;
        }
      }

      // 带框：中间三分之一高度，上下留直边
      float marginX = 12 * s;
      float bandTop = 84 * s;
      float bandH = 88 * s;
      float cellW = (size - 2 * marginX) / gw;
      float cellH = bandH / gh;
      float gap = 0.8f * s;

      for (int pass = 0; pass < 2; pass++) {
        for (int gy = 0; gy < gh; gy++) {
          for (int gx = 0; gx < gw; gx++) {
            if (!cells[gy, gx]) continue;
            float ox = marginX + gx * cellW;
            float oy = bandTop + gy * cellH;
            if (pass == 0) {
              using (SolidBrush b = new SolidBrush(Color.FromArgb(55, 150, 156, 166))) {
                g.FillRectangle(b, ox + 1.0f * s, oy + 2.0f * s, cellW - gap, cellH - gap);
              }
            } else {
              using (SolidBrush b = new SolidBrush(Color.FromArgb(31, 41, 55))) {
                g.FillRectangle(b, ox, oy, cellW - gap, cellH - gap);
              }
            }
          }
        }
      }
    }
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