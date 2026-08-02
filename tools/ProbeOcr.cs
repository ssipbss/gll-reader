using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

class ProbeOcr {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern IntPtr FindWindow(string cls, string name);

  [DllImport("user32.dll")]
  private static extern bool SetProcessDPIAware();

  [DllImport("user32.dll")]
  private static extern uint GetDpiForWindow(IntPtr hWnd);

  private static void Log(string s) {
    try { File.AppendAllText("C:\\tmp\\probeocr.log", DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { }
  }

  private static int PixelDiff(Bitmap a, Bitmap b) {
    int diff = 0;
    for (int y = 0; y < a.Height; y++) {
      for (int x = 0; x < a.Width; x++) {
        Color ca = a.GetPixel(x, y);
        Color cb = b.GetPixel(x, y);
        int d = Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
        if (d > 30) diff++;
      }
    }
    return diff;
  }

  private static int NonBg(Bitmap b) {
    int n = 0;
    for (int y = 0; y < b.Height; y++) {
      for (int x = 0; x < b.Width; x++) {
        Color c = b.GetPixel(x, y);
        if (c.R < 240 || c.G < 240 || c.B < 240) n++;
      }
    }
    return n;
  }

  [STAThread]
  private static void Main() {
    try { SetProcessDPIAware(); } catch { }
    Log("START");
    IntPtr tray = FindWindow("Shell_TrayWnd", null);
    AutomationElement rootEl = AutomationElement.FromHandle(tray);
    AutomationElementCollection btns = rootEl.FindAll(TreeScope.Descendants,
      new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
    foreach (AutomationElement el in btns) {
      string n = el.Current.Name ?? "";
      if (n.IndexOf("托盘输入指示器", StringComparison.Ordinal) >= 0 &&
          n.IndexOf("中文/英文", StringComparison.Ordinal) >= 0) {
        System.Windows.Rect r = el.Current.BoundingRectangle;
        uint dpi = GetDpiForWindow(tray);
        if (dpi == 0) dpi = 96;
        /* 进程处于 DPI 虚拟化：UIA 坐标与截图都用虚拟坐标，不做换算 */
        int x = (int)r.X, y = (int)r.Y;
        int w = Math.Max(1, (int)r.Width), h = Math.Max(1, (int)r.Height);
        Log("RECT=" + x + "," + y + " " + w + "x" + h + " dpi=" + dpi);
        Bitmap prev = null;
        for (int i = 0; i < 8; i++) {
          using (Bitmap bmp = new Bitmap(w, h)) {
            using (Graphics g = Graphics.FromImage(bmp)) {
              g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
            }
            int diff = prev == null ? -1 : PixelDiff(prev, bmp);
            Log("SHOT" + i + " diff=" + diff + " nonbg=" + NonBg(bmp));
            if (prev != null) prev.Dispose();
            prev = (Bitmap)bmp.Clone();
          }
          Thread.Sleep(1000);
        }
        if (prev != null) prev.Dispose();
      }
    }
    Log("DONE");
  }
}
