using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

/* 关联探针：同时记录 托盘输入指示器像素变化 / IMM 转换状态 / 键盘布局 / UIA 名称，
 * 观察用户正常切换中英时各通道是否一致。 */
class TrayImeCorrelate {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern IntPtr FindWindow(string cls, string name);
  [DllImport("user32.dll")]
  private static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")]
  private static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]
  private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")]
  private static extern IntPtr GetKeyboardLayout(uint idThread);
  [DllImport("user32.dll")]
  private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
  [DllImport("imm32.dll")]
  private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);

  private const uint WM_IME_CONTROL = 0x0283;
  private const uint IMC_GETCONVERSIONMODE = 0x0005;
  private const uint IMC_GETOPENSTATUS = 0x0007;
  private const uint IME_CMODE_NATIVE = 0x0001;

  private static string _log = "C:\\tmp\\traycorr.log";
  private static Rectangle _rect = Rectangle.Empty;
  private static Bitmap _prev;
  private static string _lastUia = "";

  private static void Log(string s) {
    string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + s;
    Console.WriteLine(line);
    try { File.AppendAllText(_log, line + "\r\n"); } catch { }
  }

  private static string ProcName(uint pid) {
    try {
      using (System.Diagnostics.Process p = System.Diagnostics.Process.GetProcessById((int)pid))
        return p.ProcessName;
    } catch { return pid.ToString(); }
  }

  private static string ImmState(IntPtr hwnd) {
    try {
      IntPtr imeWnd = ImmGetDefaultIMEWnd(hwnd);
      if (imeWnd == IntPtr.Zero) return "noImeWnd";
      IntPtr mode = SendMessage(imeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETCONVERSIONMODE, IntPtr.Zero);
      IntPtr open = SendMessage(imeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, IntPtr.Zero);
      uint m = (uint)(mode.ToInt32() & 0xFFFF);
      uint o = (uint)(open.ToInt32() & 0xFFFF);
      bool native = (m & IME_CMODE_NATIVE) != 0;
      return "conv=0x" + m.ToString("X") + " open=" + o + " " + (native ? "ZH" : "EN");
    } catch (Exception ex) {
      return "err " + ex.Message;
    }
  }

  private static void FindIndicatorRect() {
    try {
      IntPtr tray = FindWindow("Shell_TrayWnd", null);
      if (tray == IntPtr.Zero) return;
      AutomationElement rootEl = AutomationElement.FromHandle(tray);
      if (rootEl == null) return;
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
          _lastUia = n;
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

  [STAThread]
  private static void Main(string[] args) {
    try { SetProcessDPIAware(); } catch { }
    if (args.Length > 0) _log = args[0];
    Console.OutputEncoding = Encoding.UTF8;
    Log("=== TrayImeCorrelate START ===");
    FindIndicatorRect();
    int ticks = 0;
    string lastImm = "";
    long lastHkl = 0;
    string lastProc = "";
    while (ticks < 240) {
      Thread.Sleep(500);
      ticks++;
      IntPtr hwnd = GetForegroundWindow();
      if (hwnd == IntPtr.Zero) continue;
      uint tid, pid;
      tid = GetWindowThreadProcessId(hwnd, out pid);
      string proc = ProcName(pid) + ":" + tid;
      if (proc != lastProc) {
        Log("FOCUS " + proc);
        lastProc = proc;
      }
      string imm = ImmState(hwnd);
      if (imm != lastImm) {
        Log("IMM_CHANGE " + lastImm + " -> " + imm + " proc=" + proc);
        lastImm = imm;
      }
      long hkl = GetKeyboardLayout(tid).ToInt64();
      if (hkl != lastHkl) {
        Log("HKL_CHANGE 0x" + hkl.ToString("X") + " proc=" + proc);
        lastHkl = hkl;
      }
      if (_rect.Width <= 0) {
        if (ticks % 20 == 0) FindIndicatorRect();
        continue;
      }
      try {
        using (Bitmap bmp = new Bitmap(_rect.Width, _rect.Height)) {
          using (Graphics g = Graphics.FromImage(bmp)) {
            g.CopyFromScreen(_rect.X, _rect.Y, 0, 0, _rect.Size);
          }
          if (_prev != null) {
            int diff = PixelDiff(bmp, _prev);
            if (diff > 40) {
              Log("PIXEL_CHANGE diff=" + diff + " imm=" + imm + " proc=" + proc);
            }
          }
          if (_prev != null) _prev.Dispose();
          _prev = (Bitmap)bmp.Clone();
        }
      } catch { }
      /* UIA 名称变化 */
      try {
        IntPtr tray = FindWindow("Shell_TrayWnd", null);
        if (tray != IntPtr.Zero) {
          AutomationElement rootEl = AutomationElement.FromHandle(tray);
          AutomationElementCollection btns = rootEl.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
          foreach (AutomationElement el in btns) {
            string n = el.Current.Name ?? "";
            if (n.IndexOf("托盘输入指示器", StringComparison.Ordinal) >= 0) {
              if (n != _lastUia) {
                Log("UIA_CHANGE [" + _lastUia + "] -> [" + n + "] imm=" + imm);
                _lastUia = n;
              }
              break;
            }
          }
        }
      } catch { }
    }
    Log("=== DONE ===");
  }
}
