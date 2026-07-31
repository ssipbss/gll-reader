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
    const int grid = 20;
    string[] z = { "1111", "0001", "0010", "0100", "1000", "1000", "1111" };
    string[] e = { "1111", "1000", "1000", "1111", "1000", "1000", "1111" };
    string[] r = { "1110", "1001", "1001", "1110", "1010", "1001", "1001" };
    string[] o = { "1110", "1001", "1001", "1001", "1001", "1001", "1110" };
    string[][] glyphs = { z, e, r, o };
    int rowStart = 6;
    int[] colStarts = { 1, 6, 11, 16 };

    bool[,] cells = new bool[grid, grid];
    for (int li = 0; li < glyphs.Length; li++) {
      string[] glyph = glyphs[li];
      for (int gy = 0; gy < 7; gy++) {
        for (int gx = 0; gx < 4; gx++) {
          if (glyph[gy][gx] == '1') {
            cells[rowStart + gy, colStarts[li] + gx] = true;
          }
        }
      }
    }

    float tileX = 8 * s;
    float tileY = 8 * s;
    float cell = 240f * s / grid;
    float gap = 1.0f * s;

    for (int pass = 0; pass < 2; pass++) {
      for (int gy = 0; gy < grid; gy++) {
        for (int gx = 0; gx < grid; gx++) {
          if (!cells[gy, gx]) continue;
          float ox = tileX + gx * cell;
          float oy = tileY + gy * cell;
          if (pass == 0) {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(55, 150, 156, 166))) {
              g.FillRectangle(b, ox + 0.8f * s, oy + 1.6f * s, cell - gap, cell - gap);
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