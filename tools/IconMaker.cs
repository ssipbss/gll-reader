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
      g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
      g.Clear(Color.Transparent);
      float s = size / 256f;

      RectangleF bg = new RectangleF(6 * s, 6 * s, 244 * s, 244 * s);
      using (GraphicsPath p = RoundRect(bg, 58 * s)) {
        using (LinearGradientBrush b = new LinearGradientBrush(bg,
                 Color.FromArgb(255, 255, 255), Color.FromArgb(245, 247, 249), 90f)) {
          g.FillPath(b, p);
        }
      }
      using (GraphicsPath p = RoundRect(bg, 58 * s)) {
        using (PathGradientBrush pb = new PathGradientBrush(p)) {
          pb.CenterColor = Color.FromArgb(255, 255, 255);
          pb.SurroundColors = new Color[] { Color.FromArgb(234, 237, 241) };
          g.FillPath(pb, p);
        }
      }
      using (GraphicsPath p = RoundRect(bg, 58 * s)) {
        using (Pen pen = new Pen(Color.FromArgb(226, 229, 234), 1.5f)) {
          g.DrawPath(pen, p);
        }
      }

      string glyph = "零";
      using (Font font = PickFont(176 * s)) {
        using (GraphicsPath gp = new GraphicsPath()) {
          gp.AddString(glyph, font.FontFamily, (int)FontStyle.Regular, font.Size,
                       new PointF(0, 0), StringFormat.GenericTypographic);
          RectangleF b = gp.GetBounds();
          float ox = (size - b.Width) / 2f - b.X;
          float oy = (size - b.Height) / 2f - b.Y;
          using (SolidBrush shadow = new SolidBrush(Color.FromArgb(60, 160, 166, 176))) {
            g.TranslateTransform(ox, oy + 2 * s);
            g.FillPath(shadow, gp);
            g.ResetTransform();
          }
          using (SolidBrush ink = new SolidBrush(Color.FromArgb(31, 41, 55))) {
            g.TranslateTransform(ox, oy);
            g.FillPath(ink, gp);
            g.ResetTransform();
          }
        }
      }
    }
    return bmp;
  }

  static Font PickFont(float size) {
    try {
      if (_pfc.Families.Length == 0 && File.Exists("夕体Pro.ttf")) {
        _pfc.AddFontFile("夕体Pro.ttf");
      }
      if (_pfc.Families.Length > 0) {
        return new Font(_pfc.Families[0], size, FontStyle.Regular, GraphicsUnit.Pixel);
      }
    } catch { }
    string[] candidates = { "STXingkai", "华文行楷", "KaiTi", "楷体" };
    foreach (string name in candidates) {
      try {
        using (Font probe = new Font(name, size, FontStyle.Regular, GraphicsUnit.Pixel)) {
          if (string.Equals(probe.Name, name, StringComparison.OrdinalIgnoreCase) || probe.Name == name) {
            return new Font(name, size, FontStyle.Regular, GraphicsUnit.Pixel);
          }
        }
      } catch { }
    }
    return new Font("Microsoft YaHei", size, FontStyle.Bold, GraphicsUnit.Pixel);
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
