using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

class ProbeTrayIme {
  private static string _log;
  private static AutomationElement _watched;

  [DllImport("user32.dll")]
  private static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMin, uint wMax);
  [DllImport("user32.dll")]
  private static extern bool TranslateMessage(ref MSG lpMsg);
  [DllImport("user32.dll")]
  private static extern IntPtr DispatchMessage(ref MSG lpMsg);

  [StructLayout(LayoutKind.Sequential)]
  private struct MSG {
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public int ptX;
    public int ptY;
  }

  private static void Log(string s) {
    try { File.AppendAllText(_log, DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { }
  }

  [STAThread]
  private static void Main(string[] args) {
    _log = args.Length > 0 ? args[0] : "C:\\tmp\\probetray.log";
    Log("START");
    /* 枚举桌面所有元素，找名称含"中/英/输入"或托盘相关 */
    AutomationElement root = AutomationElement.RootElement;
    AutomationElementCollection all = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
    Log("TOTAL=" + all.Count);
    int shown = 0;
    foreach (AutomationElement el in all) {
      try {
        string name = el.Current.Name ?? "";
        string cls = el.Current.ClassName ?? "";
        string pid = el.Current.ProcessId.ToString();
        if (name.IndexOf("中", StringComparison.Ordinal) >= 0 ||
            name.IndexOf("英", StringComparison.Ordinal) >= 0 ||
            name.IndexOf("输入", StringComparison.Ordinal) >= 0 ||
            cls.IndexOf("Tray", StringComparison.OrdinalIgnoreCase) >= 0 ||
            cls.IndexOf("IME", StringComparison.OrdinalIgnoreCase) >= 0 ||
            pid == "13292" || pid == "13740" || pid == "29900") {
          Log("EL pid=" + pid + " type=" + el.Current.ControlType.ProgrammaticName +
              " cls=" + cls + " name=[" + name + "]");
          shown++;
          if (shown > 60) break;
        }
      } catch { }
    }
    Log("DONE shown=" + shown);
    /* 找到"托盘输入指示器"元素并监听 Name 变化 */
    foreach (AutomationElement el in all) {
      try {
        string name = el.Current.Name ?? "";
        if (name.IndexOf("托盘输入指示器", StringComparison.Ordinal) >= 0) {
          if (_watched == null) _watched = el;
          Log("WATCH pid=" + el.Current.ProcessId + " name=[" + name.Replace("\r", "\\r").Replace("\n", "\\n") + "]");
        }
      } catch (Exception ex) {
        Log("WATCH_FAIL " + ex.Message);
      }
    }
    /* 消息泵 + 轮询：每500ms对比指示器名称，检测中英切换 */
    string last = "";
    DateTime end = DateTime.Now.AddSeconds(30);
    while (DateTime.Now < end) {
      MSG msg;
      while (GetMessage(out msg, IntPtr.Zero, 0, 0)) {
        TranslateMessage(ref msg);
        DispatchMessage(ref msg);
      }
      try {
        if (_watched != null) {
          string n = _watched.Current.Name ?? "";
          if (n != last) {
            Log("STATE_CHANGED [" + (last.Length > 0 ? last.Replace("\r", "\\r").Replace("\n", "\\n") : "-") +
                "] -> [" + n.Replace("\r", "\\r").Replace("\n", "\\n") + "]");
            last = n;
          }
        }
      } catch { }
      System.Threading.Thread.Sleep(500);
    }
    Log("DONE");
  }
}
