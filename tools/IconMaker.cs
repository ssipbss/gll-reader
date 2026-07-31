using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
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
      using (GraphicsPath p = RoundRect(bg, 58 * s)) {
        using (LinearGradientBrush b = new LinearGradientBrush(bg,
                 Color.FromArgb(9, 54, 96), Color.FromArgb(22, 148, 170), 135f)) {
          g.FillPath(b, p);
        }
      }
      using (GraphicsPath p = RoundRect(bg, 58 * s)) {
        using (Pen pen = new Pen(Color.FromArgb(70, 255, 255, 255), 2 * s)) {
          g.DrawPath(pen, p);
        }
      }

      float penW = 16 * s;
      RectangleF ring1 = new RectangleF(58 * s, 94 * s, 60 * s, 60 * s);
      using (Pen pen = new Pen(Color.White, penW)) {
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        g.DrawEllipse(pen, ring1);
      }
      RectangleF ring2 = new RectangleF(138 * s, 94 * s, 60 * s, 60 * s);
      using (Pen pen = new Pen(Color.White, penW)) {
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        g.DrawEllipse(pen, ring2);
      }

      using (Pen pen = new Pen(Color.White, 6 * s)) {
        pen.StartCap = LineCap.Round;
        pen.EndCap = LineCap.Round;
        g.DrawArc(pen, 196 * s, 42 * s, 30 * s, 30 * s, -60, 120);
        g.DrawArc(pen, 210 * s, 30 * s, 42 * s, 42 * s, -60, 120);
      }
    }
    return bmp;
  }

  static GraphicsPath RoundRect(RectangleF r, float radius) {
    GraphicsPath p = new GraphicsPath();
    float d = radius * 2;
    p.AddArc(r.X, r.Y, d, d, 180, 90);
    p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
    p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
    p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
    p.CloseFigure();
    return p;
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
