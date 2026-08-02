using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

/* 托盘指示器模板采集：每隔400ms截取"输入指示器"按钮区域，
 * 聚类相似图像，把每种不同外观保存为 PNG 并输出签名，供后续人工/程序标注中英状态。 */
class TrayTemplateCapture {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern IntPtr FindWindow(string cls, string name);
  [DllImport("user32.dll")]
  private static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")]
  private static extern bool GetCursorPos(out POINT pt);

  [StructLayout(LayoutKind.Sequential)]
  private struct POINT { public int X, Y; }

  private static string _outDir = "C:\\tmp\\traytpl";
  private static Rectangle _rect = Rectangle.Empty;

  private static void Log(string s) {
    string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s;
    Console.WriteLine(line);
    try { File.AppendAllText(_outDir + "\\capture.log", line + "\r\n"); } catch { }
  }

  private static void FindRect() {
    try {
      IntPtr tray = FindWindow("Shell_TrayWnd", null);
      if (tray == IntPtr.Zero) return;
      AutomationElement rootEl = AutomationElement.FromHandle(tray);
      AutomationElementCollection btns = rootEl.FindAll(TreeScope.Descendants,
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
      foreach (AutomationElement el in btns) {
        string n = el.Current.Name ?? "";
        if (n.IndexOf("托盘输入指示器", StringComparison.Ordinal) >= 0 &&
            n.IndexOf("中文/英文", StringComparison.Ordinal) >= 0) {
          System.Windows.Rect r = el.Current.BoundingRectangle;
          if (r.Width > 1 && r.Height > 1) {
            _rect = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            Log("RECT=" + _rect.X + "," + _rect.Y + " " + _rect.Width + "x" + _rect.Height);
          }
          return;
        }
      }
    } catch { }
  }

  private static int PixelDiff(Bitmap a, Bitmap b) {
    int diff = 0;
    int w = Math.Min(a.Width, b.Width);
    int h = Math.Min(a.Height, b.Height);
    for (int y = 0; y < h; y++) {
      for (int x = 0; x < w; x++) {
        Color ca = a.GetPixel(x, y);
        Color cb = b.GetPixel(x, y);
        int d = Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
        if (d > 30) diff++;
      }
    }
    return diff;
  }

  private static string Sign(Bitmap b) {
    StringBuilder sb = new StringBuilder();
    for (int y = 0; y < b.Height; y += 2) {
      for (int x = 0; x < b.Width; x += 2) {
        Color c = b.GetPixel(x, y);
        int g = (c.R + c.G + c.B) / 3;
        sb.Append((char)('a' + (g / 16)));
      }
    }
    return sb.ToString();
  }

  private static void Save(Bitmap b, string name) {
    try {
      string p = Path.Combine(_outDir, name + ".png");
      b.Save(p, ImageFormat.Png);
    } catch { }
  }

  [STAThread]
  private static void Main(string[] args) {
    try { SetProcessDPIAware(); } catch { }
    if (args.Length > 0) _outDir = args[0];
    Directory.CreateDirectory(_outDir);
    Console.OutputEncoding = Encoding.UTF8;
    Log("=== TrayTemplateCapture START ===");
    FindRect();
    List<Bitmap> clusters = new List<Bitmap>();
    List<string> clusterSigns = new List<string>();
    Bitmap prev = null;
    int ticks = 0;
    while (ticks < 900) {
      Thread.Sleep(400);
      ticks++;
      if (_rect.Width <= 0) {
        if (ticks % 25 == 0) FindRect();
        continue;
      }
      POINT pt;
      GetCursorPos(out pt);
      /* 鼠标悬停会导致按钮高亮，跳过，避免噪声模板 */
      if (pt.X >= _rect.X - 4 && pt.X <= _rect.X + _rect.Width + 4 &&
          pt.Y >= _rect.Y - 4 && pt.Y <= _rect.Y + _rect.Height + 4) continue;
      try {
        using (Bitmap bmp = new Bitmap(_rect.Width, _rect.Height)) {
          using (Graphics g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(_rect.X, _rect.Y, 0, 0, _rect.Size);
          }
          int diff = prev == null ? -1 : PixelDiff(bmp, prev);
          string sig = Sign(bmp);
          int best = -1;
          int bestDiff = int.MaxValue;
          for (int i = 0; i < clusters.Count; i++) {
            int d = PixelDiff(bmp, clusters[i]);
            if (d < bestDiff) { bestDiff = d; best = i; }
          }
          if (best < 0 || bestDiff > 250) {
            Bitmap copy = (Bitmap)bmp.Clone();
            clusters.Add(copy);
            clusterSigns.Add(sig);
            string name = "c" + clusters.Count + "_" + DateTime.Now.ToString("HHmmss");
            Save(copy, name);
            Log("NEW_CLUSTER id=" + clusters.Count + " name=" + name + " diffPrev=" + diff +
                " bestDiff=" + bestDiff + " sig=" + sig.Substring(0, Math.Min(48, sig.Length)));
          } else {
            Log("TICK id=" + (best + 1) + " diffPrev=" + diff + " bestDiff=" + bestDiff +
                " sig=" + sig.Substring(0, Math.Min(48, sig.Length)));
          }
          if (prev != null) prev.Dispose();
          prev = (Bitmap)bmp.Clone();
        }
      } catch { }
    }
    Log("=== DONE clusters=" + clusters.Count + " ===");
  }
}
