using System;
using System.Runtime.InteropServices;
using System.Text;

class WinScan {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")]
  private static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
  [DllImport("user32.dll")]
  private static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")]
  private static extern int GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)]
  private struct RECT { public int L, T, R, B; }

  static void Main() {
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      StringBuilder cls = new StringBuilder(256);
      StringBuilder txt = new StringBuilder(256);
      GetClassName(h, cls, 256);
      GetWindowText(h, txt, 256);
      string c = cls.ToString();
      string t = txt.ToString();
      RECT r; GetWindowRect(h, out r);
      int w = r.R - r.L, hh = r.B - r.T;
      // small borderless floating windows (potential stuck floater)
      if (c.IndexOf("WindowsForms10", StringComparison.OrdinalIgnoreCase) >= 0 && w < 300 && hh < 120) {
        Console.WriteLine("SUSPECT h=0x" + h.ToInt64().ToString("X") + " cls=[" + c + "] title=[" + t + "] " + w + "x" + hh + " at " + r.L + "," + r.T);
      }
      return true;
    }, IntPtr.Zero);
    Console.WriteLine("scan done");
  }
}