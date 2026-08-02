using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;

class ProbeTrayLoc {
  [DllImport("user32.dll", CharSet = CharSet.Unicode)]
  private static extern IntPtr FindWindow(string cls, string name);

  private static void Log(string s) {
    try { File.AppendAllText("C:\\tmp\\trayloc.log", DateTime.Now.ToString("HH:mm:ss.fff") + " " + s + "\r\n"); } catch { }
  }

  [STAThread]
  private static void Main() {
    Log("START");
    IntPtr tray = FindWindow("Shell_TrayWnd", null);
    Log("Shell_TrayWnd=" + tray);
    if (tray != IntPtr.Zero) {
      AutomationElement el = AutomationElement.FromHandle(tray);
      if (el != null) {
        AutomationElementCollection all = el.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        Log("tray children=" + all.Count);
        int n = 0;
        foreach (AutomationElement c in all) {
          string name = c.Current.Name ?? "";
          if (name.IndexOf("输入", StringComparison.Ordinal) >= 0 ||
              name.IndexOf("模式", StringComparison.Ordinal) >= 0 ||
              name.IndexOf("中", StringComparison.Ordinal) >= 0 ||
              name.IndexOf("英", StringComparison.Ordinal) >= 0) {
            Log("TRAY_EL type=" + c.Current.ControlType.ProgrammaticName +
                " cls=" + (c.Current.ClassName ?? "") + " name=[" + name + "]");
            n++;
            if (n > 20) break;
          }
        }
      }
    }
    /* 枚举 explorer 窗口找输入指示器 */
    Log("DONE");
  }
}
